using System.Text.Json;
using ImageMagick;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using PhotoMargin.App.Services;
using PhotoMargin.App.Views;
using PhotoMargin.Core;
using Windows.Graphics;

namespace PhotoMargin.App;

public sealed partial class MainWindow
{
    private async Task WaitForDemoAsync(SettingsPage page)
    {
        try
        {
            await UntilPresetAsync(() => ViewModel.RuleEditor.CompletedPreviewVersion == ViewModel.RuleEditor.PreviewVersion &&
                ViewModel.RuleEditor.PreviewBytes is not null && ((Image)page.FindName("RulePreviewImage")).Source is not null);
        }
        catch (TimeoutException exception)
        { throw new TimeoutException($"Demo {ViewModel.RuleEditor.CompletedPreviewVersion}/{ViewModel.RuleEditor.PreviewVersion}: {ViewModel.RuleEditor.PreviewError}; page loaded={page.IsLoaded}", exception); }
        if (ViewModel.RuleEditor.PreviewError.Length > 0) throw new InvalidOperationException(ViewModel.RuleEditor.PreviewError);
        await Task.Delay(120);
    }
    internal async Task RunAspectRestoreAsync()
    {
        var checks = new List<object>(); Exception? failure = null;
        try
        {
            await Initialization; OpenSettings(SettingsSection.Layout); var page = (SettingsPage)SettingsFrame.Content;
            await WaitForDemoAsync(page);
            Check(!ViewModel.HasPhoto && ViewModel.RuleEditor.AspectIndex == 0, "New process restores chosen demonstration ratio without a user source", checks);
            await ViewModel.RuleEditor.ChangeModeAsync(ViewModels.PresetMode.Edit);
            var preset = ViewModel.MarginRules.Single(rule => rule.Name == "Four ratio QA");
            await ViewModel.RuleEditor.SelectPresetAsync(preset.Id);
            for (var index = 0; index < 4; index++)
            for (var arrangement = 0; arrangement < 2; arrangement++)
            {
                ViewModel.RuleEditor.AspectIndex = index; ViewModel.RuleEditor.ArrangementIndex = arrangement; await WaitForDemoAsync(page);
                Check(ViewModel.RuleEditor.Side == 110 + index + arrangement * 20 && ViewModel.RuleEditor.ExposureItemGap == 12 + index + arrangement * 10,
                    $"Fresh process restores ratio {index}, arrangement {arrangement}, and item spacing", checks);
            }
            await CaptureWindowAsync("aspect-restarted.png");
        }
        catch (Exception exception) { failure = exception; }
        finally { WriteAspectResult("aspect-restore-result.json", checks, failure); Close(); }
    }
    internal async Task RunAspectPreviewAsync()
    {
        var checks = new List<object>(); Exception? failure = null;
        try
        {
            await Initialization; ViewModel.ThemeChoice = 1; AppWindow.Resize(new SizeInt32(1344, 892));
            OpenSettings(SettingsSection.Layout); var page = (SettingsPage)SettingsFrame.Content;
            await WaitForDemoAsync(page);
            var ratio = (ComboBox)page.FindName("ImageAspectSelector"); var side = (Slider)page.FindName("SideMarginSlider");
            var arrangementSelector = (ComboBox)page.FindName("InformationArrangementSelector");
            var itemGap = (Slider)page.FindName("ExposureItemGapSlider"); var gapReadout = (TextBlock)page.FindName("ExposureItemGapValue");
            var readout = (TextBlock)page.FindName("SideMarginValue");
            Check(ratio.SelectedIndex == 2 && ratio.Items.Count == 4, "Default native ComboBox is 4:3 with exactly four source ratios", checks);
            Check(arrangementSelector.Items.Cast<string>().SequenceEqual(new[] { "左右排列", "垂直排列" }) &&
                  arrangementSelector.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point()).Y > ratio.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point()).Y,
                "Exactly two native arrangement options immediately below source ratio", checks);
            Check(!ViewModel.HasPhoto && ViewModel.PreviewBytes is null && string.IsNullOrEmpty(ViewModel.Camera) && ViewModel.SelectedDate is null,
                "Offline demo leaves main source, main preview and actual EXIF empty", checks);
            Check(ViewModel.RuleEditor.PreviewLayout!.IsValid && ViewModel.RuleEditor.PreviewLayout.TextRuns.Count > 0,
                "Offline preview includes real photo composition and sample metadata", checks);
            Check(!((Button)page.FindName("DeleteRuleButton")).IsEnabled, "Apply delete stays natively disabled", checks);
            await CaptureWindowAsync("aspect-empty-4x3-dark.png");
            ((RadioButtons)page.FindName("RuleModeOptions")).SelectedIndex = 1;
            await UntilPresetAsync(() => ViewModel.RuleEditor.IsEditMode);
            var original = ViewModel.MarginRules[0];
            for (var index = 0; index < 4; index++)
            for (var arrangement = 0; arrangement < 2; arrangement++)
            {
                ratio.SelectedIndex = index; arrangementSelector.SelectedIndex = arrangement;
                side.Value = 110 + index + arrangement * 20; itemGap.Value = 12 + index + arrangement * 10;
                await WaitForDemoAsync(page);
                Check(gapReadout.Text == $"{12 + index + arrangement * 10} px" && ViewModel.RuleEditor.PreviewLayout!.UsesStackedInfo == (arrangement == 1),
                    $"Native ratio {index}/arrangement {arrangement} renders independently with visible item-gap value", checks);
            }
            arrangementSelector.SelectedIndex = 0;
            await WaitForDemoAsync(page);
            AppWindow.Resize(new SizeInt32(1596, 1256)); await Task.Delay(200);
            await CaptureWindowAsync("eight-configurations-layout.png");
            for (var index = 0; index < 4; index++)
            {
                ratio.SelectedIndex = index; await Task.Delay(50);
                side.Value = 90 + index; await WaitForDemoAsync(page);
                var before = ViewModel.RuleEditor.PreviewVersion;
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(side)!;
                ((IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue)).SetValue(110 + index);
                await WaitForDemoAsync(page);
                var (w, h) = ((SourceAspect)index).Dimensions(); var size = ViewModel.RuleEditor.PreviewSourceSize;
                Check(Math.Abs(size.Width / size.Height - (double)w / h) < 0.000001 && ViewModel.RuleEditor.PreviewLayout!.CanvasSize == 720,
                    $"Native ratio {ratio.SelectedItem} renders correct source ratio on square canvas", checks);
                Check(readout.Text == $"{110 + index} px" && ViewModel.RuleEditor.CompletedPreviewVersion > before,
                    $"Native slider updates persistent readout and composition before save ({index})", checks);
            }
            ratio.SelectedIndex = 2; await WaitForDemoAsync(page);
            Check(side.Value == 112 && ViewModel.MarginRules[0] == original, "Switching ratios restores independent unsaved draft without changing saved preset", checks);
            var oldLayout = ViewModel.RuleEditor.PreviewLayout!; side.Value = 230; await WaitForDemoAsync(page);
            Check(ViewModel.RuleEditor.PreviewLayout!.PhotoBounds.Width < oldLayout.PhotoBounds.Width, "Slider shrinks actual photograph, not only its guide", checks);
            side.Value = 112;
            for (var i = 0; i < 30; i++) { ratio.SelectedIndex = i % 4; side.Value = 160 + i; }
            for (var index = 0; index < 4; index++) { ratio.SelectedIndex = index; side.Value = 110 + index; }
            ratio.SelectedIndex = 0; await WaitForDemoAsync(page);
            Check(ViewModel.RuleEditor.CompletedPreviewVersion == ViewModel.RuleEditor.PreviewVersion && readout.Text == "110 px",
                "Rapid slider and ratio updates finish on latest snapshot", checks);
            ((TextBox)page.FindName("PresetNameBox")).Text = "Four ratio QA";
            // Editing built-in keeps its ID; save-as belongs exclusively to Apply.
            await ViewModel.RuleEditor.ChangeModeAsync(ViewModels.PresetMode.Apply, () => Task.FromResult(true));
            for (var index = 0; index < 4; index++)
            for (var arrangement = 0; arrangement < 2; arrangement++)
            { ratio.SelectedIndex = index; arrangementSelector.SelectedIndex = arrangement; side.Value = 110 + index + arrangement * 20; itemGap.Value = 12 + index + arrangement * 10; }
            ratio.SelectedIndex = 0; arrangementSelector.SelectedIndex = 0;
            ((TextBox)page.FindName("PresetNameBox")).Text = "Four ratio QA";
            InvokeButton((Button)page.FindName("SaveRuleButton"));
            await UntilPresetAsync(() => ViewModel.MarginRules.Any(rule => rule.Name == "Four ratio QA") && !ViewModel.RuleEditor.IsSaving);
            var saved = ViewModel.MarginRules.Single(rule => rule.Name == "Four ratio QA");
            Check(Enum.GetValues<SourceAspect>().All(a => saved.ForAspect(a, PhotoMargin.Core.Models.InformationArrangement.TwoColumns).Side == 110 + (int)a &&
                saved.ForAspect(a, PhotoMargin.Core.Models.InformationArrangement.Stacked).Side == 130 + (int)a &&
                saved.ForAspect(a, PhotoMargin.Core.Models.InformationArrangement.TwoColumns).ExposureItemGap == 12 + (int)a &&
                saved.ForAspect(a, PhotoMargin.Core.Models.InformationArrangement.Stacked).ExposureItemGap == 22 + (int)a),
                "Native Save persists all eight configurations and item gaps under one preset ID", checks);
            await WaitForDemoAsync(page); await CaptureWindowAsync("aspect-16x9-dark.png");
            ViewModel.ThemeChoice = 2; await Task.Delay(150); await CaptureWindowAsync("aspect-light.png");
            AppWindow.Resize(new SizeInt32(800, 700)); await Task.Delay(150);
            Check(Grid.GetRow((FrameworkElement)page.FindName("PresetControlsScroller")) == 1 &&
                ((ScrollViewer)page.FindName("PresetControlsScroller")).HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled,
                "Small window keeps native vertical scrolling and square preview", checks);
            await CaptureWindowAsync("aspect-small.png"); await page.RequestBackAsync();
            Check(WorkspaceGrid.Visibility == Visibility.Visible && !ViewModel.HasPhoto, "Single Back returns home without promoting demo into actual source", checks);
            OpenSettings(SettingsSection.Layout); page = (SettingsPage)SettingsFrame.Content;
            await WaitForDemoAsync(page); await page.RequestBackAsync();
            Check(WorkspaceGrid.Visibility == Visibility.Visible, "Add-rule destination uses same direct-home Back behavior", checks);
            await ViewModel.FlushSettingsAsync();
        }
        catch (Exception exception) { failure = exception; await CaptureWindowAsync("aspect-failure.png"); }
        finally { WriteAspectResult("aspect-result.json", checks, failure); Close(); }
    }
    private void WriteAspectResult(string file, List<object> checks, Exception? failure) => File.WriteAllText(
        Path.Combine(AppContext.BaseDirectory, file), JsonSerializer.Serialize(new
        { Passed = failure is null, Checks = checks.Count, Dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)), Results = checks, Error = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
}
