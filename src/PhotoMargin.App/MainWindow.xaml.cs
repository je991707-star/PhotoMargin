using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoMargin.App.Services;
using PhotoMargin.App.ViewModels;
using PhotoMargin.App.Views;
using PhotoMargin.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace PhotoMargin.App;

public sealed partial class MainWindow : Window
{
    private readonly FileDialogService dialogs;
    private readonly WindowSizingConstraints sizingConstraints;
    private bool syncingOutputSize, closed;
    private int bitmapVersion;
    private Control? settingsOrigin;
    public MainViewModel ViewModel { get; }
    internal Task Initialization { get; }
    public MainWindow() : this(new SettingsStore()) { }

    internal MainWindow(SettingsStore settingsStore)
    {
        var renderer = new ImageRenderer();
        ViewModel = new MainViewModel(new ImageLoader(new ExifReader()),
            new PhotoMargin.Core.LayoutEngine(new MagickTextMeasurer()), new PreviewService(renderer), new ExportService(renderer), settingsStore);
        InitializeComponent();
        RootGrid.DataContext = ViewModel;
        UpdatePhotoState();
        PhotoDatePicker.MinDate = new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero);
        PhotoDatePicker.MaxDate = new DateTimeOffset(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);
        dialogs = new FileDialogService(this);
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += MainWindow_Closed;
        var id = Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
        var workArea = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.GetFromWindowId(id).Resize(new SizeInt32(Math.Min(1344, workArea.Width), Math.Min(892, workArea.Height)));
        sizingConstraints = new WindowSizingConstraints(WindowNative.GetWindowHandle(this));
        Initialization = InitializeAsync();
    }
    private async Task InitializeAsync()
    {
        await ViewModel.InitializeAsync(); ApplyTheme(); SyncOutputSizeSelection();
    }
    private void SyncOutputSizeSelection()
    {
        syncingOutputSize = true;
        OutputSizeBox.SelectedIndex = ViewModel.OutputSize switch { 1080 => 0, 2048 => 1, _ => 2 };
        CustomOutputSizeBox.Visibility = OutputSizeBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        syncingOutputSize = false;
    }
    private void OutputSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null || syncingOutputSize || CustomOutputSizeBox is null) return;
        CustomOutputSizeBox.Visibility = OutputSizeBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (OutputSizeBox.SelectedIndex == 0) ViewModel.OutputSize = 1080;
        else if (OutputSizeBox.SelectedIndex == 1) ViewModel.OutputSize = 2048;
    }
    private void ApplyTheme() => RootGrid.RequestedTheme = (ThemePreference)ViewModel.ThemeChoice switch
    { ThemePreference.Dark => ElementTheme.Dark, ThemePreference.Light => ElementTheme.Light, _ => ElementTheme.Default };
    private void UpdatePhotoState()
    {
        var hasPhoto = ViewModel.HasPhoto;
        PreviewViewport.Visibility = hasPhoto ? Visibility.Visible : Visibility.Collapsed;
        EmptyPhotoState.Visibility = hasPhoto ? Visibility.Collapsed : Visibility.Visible;
        ChangePhotoButton.Visibility = hasPhoto ? Visibility.Visible : Visibility.Collapsed;
        SourceSummaryText.Visibility = hasPhoto || ViewModel.HasError ? Visibility.Visible : Visibility.Collapsed;
    }
    private void PhotoTimePicker_Loaded(object sender, RoutedEventArgs args) => ConstrainTimePickerMinimum();
    private void PhotoTimePicker_ActualThemeChanged(FrameworkElement sender, object args) =>
        DispatcherQueue.TryEnqueue(ConstrainTimePickerMinimum);
    private void ConstrainTimePickerMinimum()
    {
        if (PhotoTimePicker is null || closed) return;
        PhotoTimePicker.ApplyTemplate();
        // SDK 2.5.1 hard-codes a 242 DIP minimum on this native template part.
        // Relax only that layout constraint; keep its template, flyout and interactions.
        if (FindTimePickerButton(PhotoTimePicker) is { } button) button.MinWidth = 0;
    }
    private static Button? FindTimePickerButton(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Button { Name: "FlyoutButton" } button) return button;
            if (FindTimePickerButton(child) is { } nested) return nested;
        }
        return null;
    }
    private void Settings_Click(object sender, RoutedEventArgs e) { settingsOrigin = SettingsButton; OpenSettings(SettingsSection.General); }
    private void AddRule_Click(object sender, RoutedEventArgs e) { settingsOrigin = AddRuleButton; OpenSettings(SettingsSection.Layout); }
    internal void OpenSettings(SettingsSection section)
    {
        var page = new SettingsPage(ViewModel, section);
        page.BackRequested += Settings_BackRequested;
        SettingsFrame.Content = page; WorkspaceGrid.Visibility = Visibility.Collapsed; SettingsFrame.Visibility = Visibility.Visible;
    }
    private void Settings_BackRequested(object? sender, EventArgs e)
    {
        if (sender is not SettingsPage page || !ReferenceEquals(SettingsFrame.Content, page)) return;
        page.BackRequested -= Settings_BackRequested;
        WorkspaceGrid.Visibility = Visibility.Visible; SettingsFrame.Visibility = Visibility.Collapsed; SettingsFrame.Content = null;
        SettingsFrame.BackStack.Clear(); SettingsFrame.ForwardStack.Clear();
        MarginRuleBox.SelectedValue = ViewModel.SelectedMarginRuleId;
        WorkspaceGrid.UpdateLayout(); (settingsOrigin ?? SettingsButton).Focus(FocusState.Programmatic);
    }
    private async void ChangePhoto_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanSelectPhoto) return;
        var path = await dialogs.PickInputAsync();
        if (path is not null) await ViewModel.LoadPhotoAsync(path);
    }
    private async void ReloadExif_Click(object sender, RoutedEventArgs e) => await ReloadPhotoInformationAsync();
    internal Task<bool> ReloadPhotoInformationAsync() => ViewModel.ReloadExifAsync(ConfirmReloadInformationAsync);
    private async Task<bool> ConfirmReloadInformationAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot, Title = "重新讀取照片資訊？",
            Content = "是否以來源照片資訊覆蓋手動修改的機身、鏡頭、拍攝時間與拍攝參數？版面與輸出設定不會改變。",
            PrimaryButtonText = "覆蓋並重新讀取", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanExport) return;
        var name = ViewModel.SourcePath is null ? "PhotoMargin" : Path.GetFileNameWithoutExtension(ViewModel.SourcePath);
        var path = await dialogs.PickOutputAsync($"{name}_margin");
        if (path is not null) await ViewModel.ExportAsync(path);
    }
    private void Preview_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = ViewModel.CanSelectPhoto && e.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.DragUIOverride.Caption = "選擇或更換橫向照片";
    }
    private async void Preview_Drop(object sender, DragEventArgs e)
    {
        if (!ViewModel.CanSelectPhoto || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.OfType<StorageFile>().FirstOrDefault() is { } file) await ViewModel.LoadPhotoAsync(file.Path);
        }
        finally { deferral.Complete(); }
    }
    private async void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.OutputSize)) SyncOutputSizeSelection();
        if (e.PropertyName == nameof(MainViewModel.ThemeChoice)) ApplyTheme();
        if (e.PropertyName is nameof(MainViewModel.HasPhoto) or nameof(MainViewModel.HasError)) UpdatePhotoState();
        if (e.PropertyName != nameof(MainViewModel.PreviewBytes)) return;
        var version = ++bitmapVersion;
        if (ViewModel.PreviewBytes is not { Length: > 0 } bytes) { PreviewImage.Source = null; return; }
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            { writer.WriteBytes(bytes); await writer.StoreAsync(); await writer.FlushAsync(); writer.DetachStream(); }
            stream.Seek(0);
            var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
            if (!closed && version == bitmapVersion && ReferenceEquals(bytes, ViewModel.PreviewBytes)) PreviewImage.Source = bitmap;
        }
        catch (Exception) when (closed || version != bitmapVersion) { }
    }
    private void MainWindow_Closed(object sender, WindowEventArgs args)
    { closed = true; bitmapVersion++; ViewModel.PropertyChanged -= ViewModel_PropertyChanged; sizingConstraints.Dispose(); ViewModel.Dispose(); }
}
