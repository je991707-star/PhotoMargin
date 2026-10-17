using System.Text.Json;
using ImageMagick;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PhotoMargin.App.Services;
using PhotoMargin.App.ViewModels;
using PhotoMargin.App.Views;
using Windows.Graphics;

namespace PhotoMargin.App;

public sealed partial class MainWindow
{
    internal async Task RunPresetRestoreRegressionAsync()
    {
        var checks = new List<object>(); Exception? failure = null;
        try
        {
            await Initialization;
            Check(ViewModel.MarginRules.Count == 1 && ViewModel.MarginRules[0].Id == MarginRule.DefaultId,
                "Fresh native process does not resurrect the permanently deleted custom preset", checks);
            Check(ViewModel.PhotoSideMargin == 64 && ViewModel.MarginRules[0].Side == 48,
                "Restart restores independent work values instead of reapplying the selected preset", checks);
            OpenSettings(SettingsSection.Layout); var page = (SettingsPage)SettingsFrame.Content;
            await Task.Delay(200);
            Check(((Slider)page.FindName("SideMarginSlider")).Value == 64 && ViewModel.RuleEditor.Mode == PresetMode.Apply &&
                  ((ComboBox)page.FindName("RuleEditorSelector")).SelectedValue?.ToString() == MarginRule.DefaultId,
                "Actual layout controls restore the work snapshot and valid selection", checks);
            Check(!((Button)page.FindName("DeleteRuleButton")).IsEnabled && !ViewModel.HasPhoto,
                "Restored apply mode remains natively disabled for deletion without inventing a source photo", checks);
            await CaptureWindowAsync("preset-restarted.png");
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "preset-restore-result.json"), JsonSerializer.Serialize(
                new { Passed = failure is null, Checks = checks.Count, Results = checks, Error = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }
    internal async Task RunPresetWorkflowRegressionAsync()
    {
        var checks = new List<object>(); Exception? failure = null;
        try
        {
            await Initialization; ViewModel.ThemeChoice = 1;
            var fixture = Path.Combine(AppContext.BaseDirectory, "preset-source.png");
            using (var image = new MagickImage(MagickColors.SteelBlue, 1200, 900)) image.Write(fixture);
            await ViewModel.LoadPhotoAsync(fixture); await WaitForPreviewAsync();
            ViewModel.Camera = "Actual test camera"; ViewModel.Lens = "Actual test lens"; ViewModel.ShowGuides = false;
            await WaitForPreviewAsync();
            OpenSettings(SettingsSection.Layout); var page = (SettingsPage)SettingsFrame.Content;
            var modeOptions = (RadioButtons)page.FindName("RuleModeOptions");
            var side = (Slider)page.FindName("SideMarginSlider"); var name = (TextBox)page.FindName("PresetNameBox");
            var save = (Button)page.FindName("SaveRuleButton"); var delete = (Button)page.FindName("DeleteRuleButton");
            await Task.Delay(350);
            Check(!delete.IsEnabled && ViewModel.RuleEditor.Mode == PresetMode.Apply && ViewModel.RuleEditor.BeginDeletion() is null,
                "Apply mode disables native delete and rejects direct deletion entrance", checks);
            Check(page.FindName("NewRuleButton") is null && Descendants((DependencyObject)page.FindName("LayoutContent")).OfType<Slider>().Count() == 6,
                "Only the requested sixth item-spacing slider is added; no new-rule or protection entrances", checks);
            name.Text = "UI preset"; side.Value = 64; await Task.Delay(150); InvokeButton(save);
            await UntilPresetAsync(() => !ViewModel.RuleEditor.IsSaving && ViewModel.MarginRules.Any(rule => rule.Name == "UI preset"));
            var preset = ViewModel.MarginRules.Single(rule => rule.Name == "UI preset");
            Check(ViewModel.MarginRules.Single(rule => rule.Id == MarginRule.DefaultId).Side == 48 && ViewModel.PhotoSideMargin == 64,
                "Native apply slider changes work but not the saved source preset", checks);
            Check(!save.IsEnabled && !delete.IsEnabled, "Same-name apply save is unavailable; delete remains disabled", checks);
            ViewModel.SelectedMarginRuleId = preset.Id;
            AppWindow.Resize(new SizeInt32(1596, 1256)); await Task.Delay(400); RootGrid.UpdateLayout();
            await UntilPresetAsync(() => ViewModel.RuleEditor.CompletedPreviewVersion == ViewModel.RuleEditor.PreviewVersion);
            Check(((Image)page.FindName("RulePreviewImage")).Source is not null && !ReferenceEquals(((Image)page.FindName("RulePreviewImage")).Source, PreviewImage.Source), "Layout page renders an independent source-aspect demonstration", checks);
            Check(ElementBounds((FrameworkElement)page.FindName("PresetPreviewSurface")).Right < ElementBounds((FrameworkElement)page.FindName("PresetControlsScroller")).Left,
                "Reference-size layout keeps preview left and editor right", checks);
            await CaptureWindowAsync("preset-apply-dark.png");

            modeOptions.SelectedIndex = 1; await Task.Delay(150);
            Check(ViewModel.RuleEditor.IsEditMode && delete.IsEnabled, "Native edit mode enables custom-preset delete", checks);
            side.Value = 111; await Task.Delay(100); modeOptions.SelectedIndex = 0;
            var discard = await WaitForPresetDialogAsync("放棄尚未儲存的預設修改？");
            Check(ViewModel.RuleEditor.IsEditMode && ViewModel.RuleEditor.Side == 111 && discard.DefaultButton == ContentDialogButton.Close,
                "Mode change asks before discarding and keeps the draft while awaiting confirmation", checks);
            InvokeButton(Descendants(discard).OfType<Button>().Single(button => button.Name == "CloseButton"));
            await UntilPresetAsync(() => ViewModel.RuleEditor.CanEdit); await Task.Delay(100);
            Check(modeOptions.SelectedIndex == 1 && side.Value == 111 && ViewModel.MarginRules.Single(rule => rule.Id == preset.Id).Side == 64,
                "Cancel mode change retains edit selection and unsaved draft", checks);
            modeOptions.SelectedIndex = 0; discard = await WaitForPresetDialogAsync("放棄尚未儲存的預設修改？");
            InvokeButton(Descendants(discard).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
            await UntilPresetAsync(() => ViewModel.RuleEditor.CanEdit && !ViewModel.RuleEditor.IsEditMode); await Task.Delay(100);
            Check(modeOptions.SelectedIndex == 0 && side.Value == 64 && ViewModel.PhotoSideMargin == 64,
                "Confirmed discard restores work without changing saved preset", checks);

            modeOptions.SelectedIndex = 1; await Task.Delay(100); side.Value = 92; name.Text = "Renamed UI preset";
            await Task.Delay(100); InvokeButton(save);
            await UntilPresetAsync(() => !ViewModel.RuleEditor.IsSaving && ViewModel.MarginRules.Any(rule => rule.Id == preset.Id && rule.Side == 92));
            Check(ViewModel.MarginRules.Count == 2 && ViewModel.MarginRules.Single(rule => rule.Id == preset.Id).Name == "Renamed UI preset" && ViewModel.PhotoSideMargin == 64,
                "Edit save updates the stable ID, not the current work or another preset", checks);
            await Task.Delay(200); await CaptureWindowAsync("preset-edit-dark.png");
            Check(ViewModel.RuleEditor.SelectedRuleId == preset.Id && ((ComboBox)page.FindName("RuleEditorSelector")).SelectedValue?.ToString() == preset.Id,
                "Replacing a saved preset does not turn native selection into an empty user selection", checks);
            Check(delete.Background is SolidColorBrush red && red.Color.R > red.Color.G * 2, "Enabled edit delete uses the scoped red native brush", checks);

            var deletion = page.ShowDeletePresetAsync(); var dialog = await WaitForPresetDialogAsync("永久刪除預設？");
            var checkbox = Descendants(dialog).OfType<CheckBox>().Single(check => check.Name == "DeletePresetAcknowledgement");
            var primary = Descendants(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton");
            var cancel = Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton");
            Check(checkbox.IsChecked == false && !primary.IsEnabled && !dialog.IsPrimaryButtonEnabled && dialog.DefaultButton == ContentDialogButton.Close,
                "Deletion starts unchecked, primary disabled, Enter defaulting to Cancel", checks);
            var disabledPresenter = Descendants(primary).OfType<ContentPresenter>().First(presenter => presenter.Name == "ContentPresenter");
            Check(disabledPresenter.Background is SolidColorBrush disabledBrush && Math.Abs(disabledBrush.Color.R - disabledBrush.Color.G) < 3,
                "Unchecked destructive primary is visually native disabled gray, not only logically disabled", checks);
            Check(cancel.FocusState != FocusState.Unfocused && primary.FocusState == FocusState.Unfocused, "Initial native focus is Cancel, not the destructive primary", checks);
            Check(((StackPanel)dialog.Content).Children.OfType<TextBlock>().First().Text.Contains("Renamed UI preset", StringComparison.Ordinal),
                "Dialog shows the saved name captured for the deletion ID", checks);
            await CaptureWindowAsync("preset-delete-unchecked.png");
            checkbox.IsChecked = true; await Task.Delay(100);
            Check(primary.IsEnabled && primary.Background is SolidColorBrush danger && danger.Color.R > danger.Color.G * 2,
                "Acknowledgement enables a red primary; native template is retained", checks);
            await CaptureWindowAsync("preset-delete-checked.png");
            checkbox.IsChecked = false; await Task.Delay(100);
            Check(!primary.IsEnabled && disabledPresenter.Background is SolidColorBrush uncheckBrush && Math.Abs(uncheckBrush.Color.R - uncheckBrush.Color.G) < 3,
                "Unchecking restores the native disabled gray state and revokes confirmation", checks);
            checkbox.IsChecked = true; await Task.Delay(100);
            InvokeButton(cancel); await deletion;
            Check(ViewModel.MarginRules.Any(rule => rule.Id == preset.Id) && ViewModel.RuleEditor.Side == 92,
                "Cancelling acknowledged deletion preserves preset and editor", checks);

            deletion = page.ShowDeletePresetAsync(); dialog = await WaitForPresetDialogAsync("永久刪除預設？");
            checkbox = Descendants(dialog).OfType<CheckBox>().Single(check => check.Name == "DeletePresetAcknowledgement");
            Check(checkbox.IsChecked == false && !dialog.IsPrimaryButtonEnabled, "Reopening always requires a fresh acknowledgement", checks);
            checkbox.IsChecked = true;
            InvokeButton(Descendants(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton")); await deletion;
            Check(!ViewModel.MarginRules.Any(rule => rule.Id == preset.Id) && ViewModel.PhotoSideMargin == 64 && ViewModel.SelectedMarginRuleId == MarginRule.DefaultId,
                "Permanent delete removes only its target, keeps work, and selects a valid fallback without applying it", checks);
            Check(!delete.IsEnabled && ViewModel.RuleEditor.SelectedRuleId == MarginRule.DefaultId, "Built-in fallback remains selected, protected and natively disabled", checks);
            var stored = await new SettingsStore(Path.Combine(AppContext.BaseDirectory, "ui-preset-settings.json")).LoadAsync();
            Check(!stored.MarginRules.Any(rule => rule.Id == preset.Id) && stored.Layout.PhotoSideMargin == 64, "Deletion is persisted and work is independent on restart", checks);

            ViewModel.ThemeChoice = 2; await Task.Delay(250); await CaptureWindowAsync("preset-apply-light.png");
            AppWindow.Resize(new SizeInt32(800, 700)); await Task.Delay(250);
            Check(Grid.GetRow((FrameworkElement)page.FindName("PresetControlsScroller")) == 1 &&
                  ((ScrollViewer)page.FindName("PresetControlsScroller")).HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled,
                "Small windows retain accessible vertical scrolling without horizontal overlap", checks);
            await CaptureWindowAsync("preset-small-light.png");
            await page.RequestBackAsync(); await WaitForPreviewAsync();
            Check(WorkspaceGrid.Visibility == Visibility.Visible && ViewModel.PhotoSideMargin == 64 && ViewModel.Camera == "Actual test camera",
                "Returning home preserves the photo, manual metadata and copied work values", checks);
        }
        catch (Exception exception) { failure = exception; await CaptureWindowAsync("preset-failure.png"); }
        finally
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "preset-regression-result.json"), JsonSerializer.Serialize(
                new { Passed = failure is null, Checks = checks.Count, Dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)), Results = checks, Error = failure?.ToString() },
                new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }
    private async Task<ContentDialog> WaitForPresetDialogAsync(string title)
    {
        ContentDialog? dialog = null;
        await UntilPresetAsync(() => (dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(RootGrid.XamlRoot)
            .SelectMany(popup => Descendants(popup.Child).Prepend(popup.Child)).OfType<ContentDialog>().FirstOrDefault(candidate => candidate.Title?.ToString() == title)) is not null);
        await Task.Delay(150); return dialog!;
    }
    private static async Task UntilPresetAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500; i++) { if (condition()) return; await Task.Delay(25); }
        throw new TimeoutException("Native preset workflow did not reach its expected state.");
    }
}
