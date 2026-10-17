using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using PhotoMargin.App.Services;
using PhotoMargin.App.Views;

namespace PhotoMargin.App;

public sealed partial class MainWindow
{
    internal async Task RunSettingsRecoveryAsync(FileStream heldLock, string settingsPath, byte[] original, bool skipRetry)
    {
        var checks = new List<object>(); Exception? failure = null;
        try
        {
            await Task.Delay(200); var dialog = await WaitForPresetDialogAsync("無法讀取設定");
            Check(ViewModel.HasSettingsLoadFailure && ViewModel.IsSettingsReadOnly && ViewModel.PersistenceError.Contains("無法讀取設定", StringComparison.Ordinal),
                "Locked startup displays a native error and enters write-protected state", checks);
            Check(ViewModel.MarginRules.Count == 0 && !InteractionHost.IsEnabled,
                "Failed read does not seed replacement presets or allow interaction behind the retry dialog", checks);
            await CaptureWindowAsync(skipRetry ? "settings-readonly-prompt.png" : "settings-retry-prompt.png");
            if (skipRetry)
            {
                InvokeButton(Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton"));
                await Initialization; heldLock.Dispose();
                ViewModel.ThemeChoice = 2;
                try { await ViewModel.FlushSettingsAsync(); throw new InvalidOperationException("Read-only close flush unexpectedly saved."); }
                catch (IOException) { }
                Check(InteractionHost.IsEnabled && ViewModel.IsSettingsReadOnly && ViewModel.HasSettingsLoadFailure,
                    "Skip retry permits session use but does not unlock settings writes", checks);
                Check(original.SequenceEqual(File.ReadAllBytes(settingsPath)), "Unlocking the file alone never allows defaults to overwrite saved presets", checks);
                OpenSettings(SettingsSection.Layout); var page = (SettingsPage)SettingsFrame.Content; await Task.Delay(150);
                Check(!((Button)page.FindName("SaveRuleButton")).IsEnabled && !((Button)page.FindName("DeleteRuleButton")).IsEnabled,
                    "Native preset save/delete controls remain disabled after a failed settings read", checks);
                return;
            }
            InvokeButton(Descendants(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
            await Task.Delay(450); var nextDialog = await WaitForPresetDialogAsync("無法讀取設定");
            Check(!ReferenceEquals(dialog, nextDialog) && ViewModel.IsSettingsReadOnly,
                "Retrying while still locked keeps the guard and shows another native retry prompt", checks);
            heldLock.Dispose();
            InvokeButton(Descendants(nextDialog).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
            await Initialization;
            Check(!ViewModel.HasSettingsLoadFailure && !ViewModel.IsSettingsReadOnly && ViewModel.PersistenceError.Length == 0,
                "Successful explicit retry clears the error and releases write protection", checks);
            Check(ViewModel.MarginRules.Count == 2 && ViewModel.SelectedMarginRuleId == "recovery-b" && ViewModel.PhotoSideMargin == 91,
                "Retry restores original presets, selected ID and work values without applying a different preset", checks);
            Check(original.SequenceEqual(File.ReadAllBytes(settingsPath)), "Retry reads the original file without rewriting it", checks);
            OpenSettings(SettingsSection.Layout); var settings = (SettingsPage)SettingsFrame.Content; await Task.Delay(200);
            ((TextBox)settings.FindName("PresetNameBox")).Text = "Native synchronized selection";
            ((Slider)settings.FindName("SideMarginSlider")).Value = 139;
            InvokeButton((Button)settings.FindName("SaveRuleButton"));
            await UntilPresetAsync(() => !ViewModel.RuleEditor.IsSaving && ViewModel.MarginRules.Any(rule => rule.Name == "Native synchronized selection"));
            var saved = ViewModel.MarginRules.Single(rule => rule.Name == "Native synchronized selection");
            Check(ViewModel.SelectedMarginRuleId == saved.Id && ViewModel.RuleEditor.SelectedRuleId == saved.Id && MarginRuleBox.SelectedValue?.ToString() == saved.Id,
                "Native save-as synchronizes editor, main ComboBox and model selection", checks);
            Check(ViewModel.PhotoSideMargin == 139, "Selection synchronization does not reapply preset values to the work", checks);
            var stored = await new SettingsStore(settingsPath).LoadAsync();
            Check(stored.SelectedMarginRuleId == saved.Id && stored.MarginRules.Any(rule => rule.Id == saved.Id),
                "Preset and new selected ID are committed in the same stored snapshot", checks);
            await settings.RequestBackAsync(); OpenSettings(SettingsSection.Layout); settings = (SettingsPage)SettingsFrame.Content; await Task.Delay(200);
            Check(((ComboBox)settings.FindName("RuleEditorSelector")).SelectedValue?.ToString() == saved.Id && ViewModel.RuleEditor.SelectedRuleId == saved.Id,
                "Returning home and reopening settings retains the newly saved selection", checks);
            await ViewModel.FlushSettingsAsync();
        }
        catch (Exception exception) { failure = exception; await CaptureWindowAsync("settings-recovery-failure.png"); }
        finally
        {
            heldLock.Dispose();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, skipRetry ? "settings-readonly-result.json" : "settings-recovery-result.json"), JsonSerializer.Serialize(
                new { Passed = failure is null, Checks = checks.Count, Results = checks, Error = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            // Fixture cleanup must not show a second, intentional unsaved-settings dialog.
            allowClose = true; Close();
        }
    }
}
