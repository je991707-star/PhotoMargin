using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoMargin.App.ViewModels;

namespace PhotoMargin.App.Views;

public enum SettingsSection { General, Layout, Camera, Export }

public sealed partial class SettingsPage : Page
{
    private readonly MainViewModel viewModel;
    private bool initialized, settingSelection, returningHome;
    public event EventHandler? BackRequested;
    internal SettingsSection SelectedSection { get; private set; }

    public SettingsPage(MainViewModel viewModel, SettingsSection destination = SettingsSection.General)
    {
        this.viewModel = viewModel;
        InitializeComponent(); DataContext = viewModel;
        SettingsNavigation.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => UpdateHeaderButtons());
        NavigateTo(destination);
        Loaded += (_, _) => UpdateHeaderButtons();
    }
    internal void NavigateTo(SettingsSection section)
    {
        settingSelection = true;
        SettingsNavigation.SelectedItem = SettingsNavigation.MenuItems.OfType<NavigationViewItem>().First(item => item.Tag?.ToString() == section.ToString());
        settingSelection = false;
        SelectedSection = section;
        GeneralContent.Visibility = section == SettingsSection.General ? Visibility.Visible : Visibility.Collapsed;
        LayoutContent.Visibility = section == SettingsSection.Layout ? Visibility.Visible : Visibility.Collapsed;
        CameraContent.Visibility = section == SettingsSection.Camera ? Visibility.Visible : Visibility.Collapsed;
        ExportContent.Visibility = section == SettingsSection.Export ? Visibility.Visible : Visibility.Collapsed;
        if (section == SettingsSection.Layout && (!initialized || viewModel.RuleEditor.SelectedRuleId is null)) viewModel.RuleEditor.Begin(viewModel.SelectedMarginRuleId);
        initialized = true;
    }
    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (GeneralContent is null) return;
        if (!settingSelection && args.SelectedItem is NavigationViewItem item && Enum.TryParse<SettingsSection>(item.Tag?.ToString(), out var section)) NavigateTo(section);
    }
    internal void RequestBack()
    {
        // All settings back entrances return home once, never through section history.
        if (returningHome) return;
        returningHome = true;
        BackRequested?.Invoke(this, EventArgs.Empty);
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
    private void NewRule_Click(object sender, RoutedEventArgs e) => viewModel.RuleEditor.New();
    private async void SaveRule_Click(object sender, RoutedEventArgs e) => await viewModel.RuleEditor.SaveAsync();
    private async void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.RuleEditor.CanDelete) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "刪除邊距規則？", Content = $"確定刪除「{viewModel.RuleEditor.Name}」？",
            PrimaryButtonText = "刪除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await viewModel.RuleEditor.DeleteAsync();
    }
}
