using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using PhotoMargin.App.ViewModels;

namespace PhotoMargin.App.Views;

public enum SettingsSection { General, Layout, Camera, Export }

public sealed partial class SettingsPage : Page
{
    private readonly MainViewModel viewModel;
    private bool initialized, settingSelection, returningHome, changingEditor, transitioning;
    private int bitmapVersion;
    public event EventHandler? BackRequested;
    internal SettingsSection SelectedSection { get; private set; }

    public SettingsPage(MainViewModel viewModel, SettingsSection destination = SettingsSection.General)
    {
        this.viewModel = viewModel;
        viewModel.RuleEditor.Begin(viewModel.SelectedMarginRuleId);
        InitializeComponent(); DataContext = viewModel;
        SettingsNavigation.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => UpdateHeaderButtons());
        NavigateTo(destination);
        Loaded += (_, _) =>
        {
            viewModel.RuleEditor.PropertyChanged += EditorStateChanged;
            viewModel.PropertyChanged += WorkStateChanged;
            UpdateHeaderButtons(); SynchronizeEditorSelection();
            if (SelectedSection == SettingsSection.Layout) viewModel.RuleEditor.StartPreview(this);
            _ = UpdateDemoBitmapAsync();
        };
        Unloaded += (_, _) =>
        {
            bitmapVersion++; viewModel.RuleEditor.StopPreview(this);
            viewModel.RuleEditor.PropertyChanged -= EditorStateChanged; viewModel.PropertyChanged -= WorkStateChanged;
        };
    }
    internal void NavigateTo(SettingsSection section)
    {
        settingSelection = true;
        SettingsNavigation.SelectedItem = SettingsNavigation.MenuItems.OfType<NavigationViewItem>().First(item => item.Tag?.ToString() == section.ToString());
        settingSelection = false;
        SelectedSection = section;
        GeneralContent.Visibility = section == SettingsSection.General ? Visibility.Visible : Visibility.Collapsed;
        CommonSettingsScrollViewer.Visibility = section == SettingsSection.Layout ? Visibility.Collapsed : Visibility.Visible;
        LayoutContent.Visibility = section == SettingsSection.Layout ? Visibility.Visible : Visibility.Collapsed;
        CameraContent.Visibility = section == SettingsSection.Camera ? Visibility.Visible : Visibility.Collapsed;
        ExportContent.Visibility = section == SettingsSection.Export ? Visibility.Visible : Visibility.Collapsed;
        initialized = true;
        if (IsLoaded)
        {
            if (section == SettingsSection.Layout) viewModel.RuleEditor.StartPreview(this);
            else viewModel.RuleEditor.StopPreview(this);
        }
    }
    private async void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (GeneralContent is null) return;
        if (!settingSelection && args.SelectedItem is NavigationViewItem item && Enum.TryParse<SettingsSection>(item.Tag?.ToString(), out var section))
        {
            if (transitioning || returningHome) return;
            transitioning = true;
            try
            {
                await Task.Yield(); // Let native selection bookkeeping finish before restoring it.
                settingSelection = true;
                SettingsNavigation.SelectedItem = SettingsNavigation.MenuItems.OfType<NavigationViewItem>().First(entry => entry.Tag?.ToString() == SelectedSection.ToString());
                settingSelection = false;
                if (await PrepareToLeaveAsync()) NavigateTo(section);
            }
            finally { transitioning = false; }
        }
    }
    internal void RequestBack()
        => _ = RequestBackAsync();
    internal async Task RequestBackAsync()
    {
        if (returningHome || transitioning) return;
        transitioning = true;
        try
        {
            if (!await PrepareToLeaveAsync()) return;
            returningHome = true;
            BackRequested?.Invoke(this, EventArgs.Empty);
        }
        finally { transitioning = false; }
    }
    internal Task<bool> PrepareToLeaveAsync() => viewModel.RuleEditor.LeaveAsync(ConfirmDiscardAsync);
    private async Task<bool> ConfirmDiscardAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "放棄尚未儲存的預設修改？",
            Content = "尚未儲存的編輯草稿將被丟棄；原預設與目前作品保持不變。",
            PrimaryButtonText = "放棄修改", CloseButtonText = "繼續編輯", DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private void Navigation_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args) => RequestBack();
    private void HeaderBack_Click(object sender, RoutedEventArgs args) => RequestBack();
    private void BackAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    { args.Handled = true; RequestBack(); }
    private void HeaderPaneToggle_Click(object sender, RoutedEventArgs args) => SettingsNavigation.IsPaneOpen = !SettingsNavigation.IsPaneOpen;
    private void Navigation_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args) => UpdateHeaderButtons();
    private void UpdateHeaderButtons()
    {
        if (SettingsHeaderButtons is null || PaneHeaderSpace is null) return;
        var expanded = SettingsNavigation.DisplayMode == NavigationViewDisplayMode.Expanded && SettingsNavigation.IsPaneOpen;
        SettingsHeaderButtons.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        PaneHeaderSpace.Height = expanded ? 48 : 0;
        SettingsNavigation.IsBackButtonVisible = expanded ? NavigationViewBackButtonVisible.Collapsed : NavigationViewBackButtonVisible.Visible;
        SettingsNavigation.IsPaneToggleButtonVisible = !expanded;
    }
    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (SettingsNavigation is null) return;
        var query = sender.Text.Trim();
        foreach (var item in SettingsNavigation.MenuItems.OfType<NavigationViewItem>())
            item.Visibility = item.Content?.ToString()?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true ? Visibility.Visible : Visibility.Collapsed;
    }
    private void WorkStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.PreviewBytes) or nameof(MainViewModel.SourcePath))
            viewModel.RuleEditor.RefreshPreview();
    }
    private async Task UpdateDemoBitmapAsync()
    {
        var version = ++bitmapVersion;
        if (viewModel.RuleEditor.PreviewBytes is not { Length: > 0 } bytes) return;
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            { writer.WriteBytes(bytes); await writer.StoreAsync(); }
            stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
            if (IsLoaded && version == bitmapVersion && ReferenceEquals(bytes, viewModel.RuleEditor.PreviewBytes)) RulePreviewImage.Source = bitmap;
        }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine(exception); }
    }
    private void PresetWorkspace_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var narrow = args.NewSize.Width < 600;
        PresetControlsColumn.MinWidth = narrow ? 0 : 240;
        PresetControlsColumn.Width = narrow ? new GridLength(0) : new GridLength(0.46, GridUnitType.Star);
        PresetControlsRow.Height = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        PresetPreviewRow.Height = narrow ? new GridLength(Math.Max(120, Math.Min(250, args.NewSize.Height * 0.4))) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(PresetControlsScroller, narrow ? 0 : 1);
        Grid.SetRow(PresetControlsScroller, narrow ? 1 : 0);
    }
    private void SynchronizeEditorSelection()
    {
        changingEditor = true;
        RuleModeOptions.SelectedIndex = viewModel.RuleEditor.ModeIndex;
        RuleEditorSelector.SelectedValue = viewModel.RuleEditor.SelectedRuleId;
        changingEditor = false;
    }
    private void EditorStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(RuleEditorViewModel.PreviewBytes)) _ = UpdateDemoBitmapAsync();
        if (args.PropertyName is nameof(RuleEditorViewModel.SelectedRuleId) or nameof(RuleEditorViewModel.ModeIndex))
            DispatcherQueue.TryEnqueue(() => { if (IsLoaded) SynchronizeEditorSelection(); });
    }
    private async void RuleMode_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (!initialized || changingEditor) return;
        var target = (PresetMode)RuleModeOptions.SelectedIndex;
        if (target == viewModel.RuleEditor.Mode) return;
        if (transitioning) return;
        transitioning = true;
        try
        {
            // Re-entering RadioButtons.SelectedIndex inside SelectionChanged corrupts
            // its internal selection bookkeeping on the next attempt. Restore afterward.
            await Task.Yield(); SynchronizeEditorSelection();
            await viewModel.RuleEditor.ChangeModeAsync(target, ConfirmDiscardAsync);
        }
        finally { transitioning = false; SynchronizeEditorSelection(); }
    }
    private async void RuleSelection_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (!initialized || changingEditor) return;
        var target = RuleEditorSelector.SelectedValue as string;
        // Replacing/removing an ObservableCollection item transiently clears native
        // selection. This is not an explicit user request to abandon a preset.
        if (!viewModel.RuleEditor.CanEdit || (target is null && viewModel.MarginRules.Count > 0))
        { DispatcherQueue.TryEnqueue(() => { if (IsLoaded) SynchronizeEditorSelection(); }); return; }
        if (target == viewModel.RuleEditor.SelectedRuleId) return;
        if (transitioning) return;
        transitioning = true;
        try
        {
            await Task.Yield(); SynchronizeEditorSelection();
            await viewModel.RuleEditor.SelectPresetAsync(target, ConfirmDiscardAsync);
        }
        finally { transitioning = false; SynchronizeEditorSelection(); }
    }
    private async void SaveRule_Click(object sender, RoutedEventArgs e) => await viewModel.RuleEditor.SaveAsync();
    private async void DeleteRule_Click(object sender, RoutedEventArgs e)
        => await ShowDeletePresetAsync();
    internal async Task ShowDeletePresetAsync()
    {
        var editor = viewModel.RuleEditor;
        var request = editor.BeginDeletion();
        if (request is null) return;
        var acknowledgement = new CheckBox
        {
            Name = "DeletePresetAcknowledgement", IsChecked = false,
            Content = new TextBlock { Text = "我已了解，此操作將永久刪除這個預設且無法復原。", TextWrapping = TextWrapping.Wrap }
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(acknowledgement, "我已了解，此操作將永久刪除這個預設且無法復原。");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(new TextBlock { Text = $"即將永久刪除『{request.Name}』。刪除後無法復原，確定要繼續嗎？", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(acknowledgement); content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "永久刪除預設？", Content = content,
            PrimaryButtonText = "永久刪除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false, PrimaryButtonStyle = (Style)Application.Current.Resources["DefaultButtonStyle"]
        };
        acknowledgement.Checked += (_, _) => { request.Acknowledged = true; dialog.IsPrimaryButtonEnabled = !editor.IsSaving; };
        acknowledgement.Unchecked += (_, _) => { request.Acknowledged = false; dialog.IsPrimaryButtonEnabled = false; };
        dialog.Opened += (_, _) =>
        {
            if (FindButton(dialog, "PrimaryButton") is { } primary)
            {
                primary.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("ms-appx:///Views/DangerButtonResources.xaml") });
                primary.Style = new Style(typeof(Button)) { BasedOn = (Style)Application.Current.Resources["DefaultButtonStyle"] };
                // The native primary was already disabled before its scoped style was
                // attached. Reapply the native state setters, without ever enabling it.
                VisualStateManager.GoToState(primary, "Normal", false);
                VisualStateManager.GoToState(primary, "Disabled", false);
            }
            FindButton(dialog, "CloseButton")?.Focus(FocusState.Programmatic);
        };
        dialog.Closing += (_, args) => { if (editor.IsSaving) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (!request.Acknowledged || editor.IsSaving) return;
            var deferral = args.GetDeferral();
            try
            {
                dialog.IsPrimaryButtonEnabled = false; acknowledgement.IsEnabled = false;
                if (FindButton(dialog, "CloseButton") is { } close) close.IsEnabled = false;
                args.Cancel = !await editor.DeleteAsync(request);
                if (args.Cancel) error.Text = editor.Message;
            }
            finally
            {
                acknowledgement.IsEnabled = true;
                if (FindButton(dialog, "CloseButton") is { } close) close.IsEnabled = true;
                dialog.IsPrimaryButtonEnabled = request.Acknowledged && !editor.IsSaving;
                deferral.Complete();
            }
        };
        try { await dialog.ShowAsync(); }
        finally { editor.CancelDeletion(request); SynchronizeEditorSelection(); }
    }
    private static Button? FindButton(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button button && button.Name == name) return button;
            if (FindButton(child, name) is { } found) return found;
        }
        return null;
    }
}
