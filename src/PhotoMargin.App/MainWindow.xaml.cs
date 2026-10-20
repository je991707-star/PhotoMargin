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
    private bool syncingOutputSize, closed, closing, allowClose;
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
        StartThemeTracking();
        UpdatePhotoState();
        PhotoDatePicker.MinDate = new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero);
        PhotoDatePicker.MaxDate = new DateTimeOffset(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);
        dialogs = new FileDialogService(this);
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += MainWindow_Closed;
        var id = Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
        AppWindow.GetFromWindowId(id).Closing += MainWindow_Closing;
        var workArea = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.GetFromWindowId(id).Resize(new SizeInt32(Math.Min(1344, workArea.Width), Math.Min(892, workArea.Height)));
        sizingConstraints = new WindowSizingConstraints(WindowNative.GetWindowHandle(this));
        Initialization = InitializeAsync();
    }
    private async Task InitializeAsync()
    {
        await ViewModel.InitializeAsync(); ApplyTheme(); SyncOutputSizeSelection();
        if (!ViewModel.HasSettingsLoadFailure) return;
        if (!RootGrid.IsLoaded)
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded(object sender, RoutedEventArgs args) => ready.TrySetResult();
            RootGrid.Loaded += Loaded;
            try { await ready.Task; }
            finally { RootGrid.Loaded -= Loaded; }
        }
        InteractionHost.IsEnabled = false;
        try
        {
            while (!closed && ViewModel.HasSettingsLoadFailure)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme,
                    Title = "無法讀取設定", Content = ViewModel.PersistenceError + "\n\n重試會重新載入原設定；暫不重試時，本次使用期間不會保存設定或預設，可重新啟動程式再試。",
                    PrimaryButtonText = "重試", CloseButtonText = "暫不重試", DefaultButton = ContentDialogButton.Primary
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) break;
                if (await ViewModel.RetryLoadSettingsAsync()) { ApplyTheme(); SyncOutputSizeSelection(); }
            }
        }
        finally { InteractionHost.IsEnabled = true; }
    }
    private void SyncOutputSizeSelection()
    {
        syncingOutputSize = true;
        OutputSizeBox.SelectedIndex = ViewModel.OutputSize switch { 1080 => 0, 2048 => 1, _ => 2 };
        CustomOutputSizeBox.Visibility = OutputSizeBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        syncingOutputSize = false;
    }
    private void LensFontSize_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        args.Handled = true;
        ApplyLensFontSizeText(sender, args.Text);
    }
    private void ApplyLensFontSizeText(ComboBox sender, string text)
    {
        ViewModel.TrySetLensFontSizeText(text);
        sender.SelectedItem = ViewModel.LensFontSize;
        sender.Text = ViewModel.LensFontSize.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
    }
    private void OutputSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null || syncingOutputSize || CustomOutputSizeBox is null) return;
        CustomOutputSizeBox.Visibility = OutputSizeBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (OutputSizeBox.SelectedIndex == 0) ViewModel.OutputSize = 1080;
        else if (OutputSizeBox.SelectedIndex == 1) ViewModel.OutputSize = 2048;
    }
    private void ApplyTheme()
    {
        var foreground = systemColors.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground);
        var systemDark = 5 * foreground.G + 2 * foreground.R + foreground.B > 8 * 128;
        RootGrid.RequestedTheme = (ThemePreference)ViewModel.ThemeChoice switch
        { ThemePreference.Dark => ElementTheme.Dark, ThemePreference.Light => ElementTheme.Light, _ => systemDark ? ElementTheme.Dark : ElementTheme.Light };
        SynchronizeThemeChrome();
    }
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
        var page = new SettingsPage(ViewModel, section, dialogs);
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
            Content = "從作品照片重新讀取 EXIF，並取代手動修改的機身、鏡頭、日期時間與拍攝參數。已確認補入的 RAW 欄位及快照會保留；版面與輸出設定不會改變。",
            PrimaryButtonText = "覆蓋並重新讀取", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExportWithDialogsAsync(dialogs.PickOutputAsync, async path =>
        {
            var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, RequestedTheme = RootGrid.ActualTheme,
                Title = "覆寫同名檔案？", Content = Path.GetFileName(path), PrimaryButtonText = "覆寫", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }, dialogs.OpenDirectoryAsync);
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
            if (!closed && version == bitmapVersion && ReferenceEquals(bytes, ViewModel.PreviewBytes))
            { PreviewImage.Source = bitmap; }
        }
        catch (Exception) when (closed || version != bitmapVersion) { }
    }
    private void MainWindow_Closed(object sender, WindowEventArgs args)
    { closed = true; bitmapVersion++; StopThemeTracking(); ViewModel.PropertyChanged -= ViewModel_PropertyChanged; sizingConstraints.Dispose(); ViewModel.Dispose(); }

    private void MainWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (allowClose) return;
        args.Cancel = true;
        if (!closing) _ = SaveBeforeCloseAsync();
    }
    private async Task SaveBeforeCloseAsync()
    {
        closing = true;
        try
        {
            if (SettingsFrame.Content is SettingsPage page && !await page.PrepareToLeaveAsync()) return;
            InteractionHost.IsEnabled = false;
            await Initialization;
            await ViewModel.FlushSettingsAsync();
            allowClose = true;
        }
        catch (Exception exception)
        {
            InteractionHost.IsEnabled = true;
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot, Title = "設定尚未儲存",
                Content = $"{exception.Message}\n留在程式可重試；仍然關閉會放棄尚未保存的設定。",
                PrimaryButtonText = "仍然關閉", CloseButtonText = "留在程式", DefaultButton = ContentDialogButton.Close
            };
            try { allowClose = await dialog.ShowAsync() == ContentDialogResult.Primary; }
            catch (Exception) { allowClose = false; } // Another native dialog may already be open; keep the work alive.
        }
        finally { closing = false; if (!allowClose) InteractionHost.IsEnabled = true; }
        if (allowClose) Close();
    }
}
