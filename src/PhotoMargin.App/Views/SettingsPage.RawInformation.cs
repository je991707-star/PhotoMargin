using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using PhotoMargin.App.Services;

namespace PhotoMargin.App.Views;

public sealed partial class SettingsPage
{
    private readonly FileDialogService? fileDialogs;
    private readonly CancellationTokenSource rawPageLifetime = new();
    private async void ReadRawInformation_Click(object sender, RoutedEventArgs args)
    {
        if (!viewModel.CanReadRaw || fileDialogs is null) return;
        var source = viewModel.SourceRevision;
        try
        {
            var path = await fileDialogs.PickRawInformationAsync();
            if (path is null || rawPageLifetime.IsCancellationRequested || source != viewModel.SourceRevision) return;
            await ImportRawInformationAsync(path);
        }
        catch (Exception exception)
        {
            if (!rawPageLifetime.IsCancellationRequested)
                await new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "無法選取 RAW", Content = exception.Message, CloseButtonText = "關閉" }.ShowAsync();
        }
    }
    internal Task<bool> ImportRawInformationAsync(string path) => viewModel.ImportSelectedRawInformationAsync(path, ConfirmRawInformationAsync, rawPageLifetime.Token);
    private async void ReloadRawInformation_Click(object sender, RoutedEventArgs args) =>
        await viewModel.ReloadSelectedRawInformationAsync(ConfirmRawInformationAsync, rawPageLifetime.Token);
    private async void RestorePhotoExif_Click(object sender, RoutedEventArgs args) => await RestorePhotoExifAsync();
    internal Task<bool> RestorePhotoExifAsync() => viewModel.RestorePhotoExifAsync(ConfirmPhotoExifRestoreAsync);
    private async Task<bool> ConfirmPhotoExifRestoreAsync()
    {
        if (rawPageLifetime.IsCancellationRequested) return false;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = viewModel.RestorePhotoExifLabel + "？",
            Content = "將還原目前作品照片原本的資訊，清除已補入的 RAW 關聯與手動修改。",
            PrimaryButtonText = "還原", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
        };
        using var registration = rawPageLifetime.Token.Register(() => DispatcherQueue.TryEnqueue(dialog.Hide));
        return await dialog.ShowAsync() == ContentDialogResult.Primary && !rawPageLifetime.IsCancellationRequested;
    }
    private void RawActions_SizeChanged(object sender, SizeChangedEventArgs args) =>
        ((StackPanel)sender).Orientation = args.NewSize.Width < 360 ? Orientation.Vertical : Orientation.Horizontal;
    private void CommonSettingsScroller_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (CommonSettingsBody is null) return;
        // ActualWidth-to-ActualWidth binding forms a layout feedback loop and can
        // retain the wider desired width after resize. Bound to the viewport
        // instead, excluding its padding; native overlay scrolling is unchanged.
        var scroller = (ScrollViewer)sender;
        CommonSettingsBody.Width = Math.Min(720, Math.Max(0, args.NewSize.Width - scroller.Padding.Left - scroller.Padding.Right));
    }
    internal async Task<RawInformationSelection?> ConfirmRawInformationAsync(RawInformationReview review)
    {
        if (rawPageLifetime.IsCancellationRequested) return null;
        var root = XamlRoot;
        var content = new Grid { RowSpacing = 12 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var heading = new StackPanel { Spacing = 8 };
        heading.Children.Add(new TextBlock { Text = "選擇 RAW 檔：" + Path.GetFileName(review.SourcePath), TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(new TextBlock { Text = review.MatchWarning, TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
        content.Children.Add(heading);
        var rows = new StackPanel { Spacing = 16 };
        var selections = new Dictionary<InformationField, RadioButton>();
        foreach (var row in review.Differences)
        {
            var groupName = "RawField-" + row.Field + "-" + Guid.NewGuid().ToString("N");
            var current = new RadioButton { GroupName = groupName,
                Content = new TextBlock { Text = $"{row.Source}：{Provided(row.Current)}（目前值）", TextWrapping = TextWrapping.Wrap } };
            var incoming = new RadioButton { GroupName = groupName, IsEnabled = !string.IsNullOrWhiteSpace(row.Incoming),
                Content = new TextBlock { Text = "RAW：" + Provided(row.Incoming), TextWrapping = TextWrapping.Wrap } };
            var options = new RadioButtons { Name = "RawField" + row.Field, Header = row.Name, MaxColumns = 1 };
            AutomationProperties.SetName(options, row.Name + "資訊來源");
            AutomationProperties.SetName(current, $"{row.Name}：{row.Source}：{Provided(row.Current)}（目前值）");
            AutomationProperties.SetName(incoming, $"{row.Name}：RAW：{Provided(row.Incoming)}");
            options.Items.Add(current); options.Items.Add(incoming);
            options.SelectedIndex = row.FillMissing && incoming.IsEnabled ? 1 : 0;
            selections.Add(row.Field, incoming); rows.Children.Add(options);
        }
        var scroller = new ScrollViewer { Name = "RawFieldChoicesScroller", HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 0, 12, 0), Content = rows };
        Grid.SetRow(scroller, 1); content.Children.Add(scroller);
        void FitContent() => content.Height = Math.Min(560, Math.Max(80, root.Size.Height - 240));
        void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => FitContent();
        FitContent(); root.Changed += RootChanged;
        var dialog = new ContentDialog { Name = "RawInformationReviewDialog", XamlRoot = root, RequestedTheme = ActualTheme,
            Title = "確認 RAW 拍攝資訊", Content = content, PrimaryButtonText = "套用", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        dialog.Resources["ContentDialogMaxWidth"] = 548d;
        AutomationProperties.SetName(dialog, "確認 RAW 拍攝資訊差異");
        dialog.Opened += (_, _) => FindButton(dialog, "CloseButton")?.Focus(FocusState.Programmatic);
        using var registration = rawPageLifetime.Token.Register(() => DispatcherQueue.TryEnqueue(dialog.Hide));
        try
        {
            var result = await dialog.ShowAsync();
            // Actual child state avoids transitional native SelectedIndex values.
            return result == ContentDialogResult.Primary && !rawPageLifetime.IsCancellationRequested
                ? new RawInformationSelection(selections.Where(pair => pair.Value.IsEnabled && pair.Value.IsChecked == true).Select(pair => pair.Key)) : null;
        }
        finally { root.Changed -= RootChanged; }
        static string Provided(string value) => string.IsNullOrWhiteSpace(value) ? "未提供" : value;
    }
}
