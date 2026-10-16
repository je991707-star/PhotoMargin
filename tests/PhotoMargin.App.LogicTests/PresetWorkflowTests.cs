using PhotoMargin.App.Services;
using PhotoMargin.App.ViewModels;
using PhotoMargin.Core.Models;
using ImageMagick;

namespace PhotoMargin.App.LogicTests;

public sealed partial class StateAndPersistenceTests
{
    private static MarginRule A => new MarginRule { Id = "a", Name = "Preset A", Side = 61, Top = 41, Gap = 27, Bottom = 81, Extra = 29, ProtectPortrait = false, ProtectLandscape = true }.Validate();
    private static MarginRule B => new MarginRule { Id = "b", Name = "Preset B", Side = 75 }.Validate();
    private string PresetPath => Path.Combine(root, "presets", "settings.json");
    private async Task<MainViewModel> Presets(params MarginRule[] rules)
    {
        await new SettingsStore(PresetPath).SaveAsync(new PersistedSettings { MarginRules = rules.ToList(), SelectedMarginRuleId = rules.FirstOrDefault()?.Id });
        var vm = Create(PresetPath); await vm.InitializeAsync(); return vm;
    }

    [Fact]
    public void ApplyingPresetCopiesEveryMarginAndChangingWorkNeverChangesPreset() => Ui.Run(async () =>
    {
        using var vm = await Presets(new MarginRule(), A);
        var saved = vm.MarginRules.Single(rule => rule.Id == "a");
        Assert.True(await vm.RuleEditor.SelectPresetAsync("a"));
        Assert.Equal(A.Side, vm.PhotoSideMargin); Assert.Equal(A.Top, vm.PhotoTopMargin); Assert.Equal(A.Gap, vm.PhotoInfoGap);
        Assert.Equal(A.Bottom, vm.BottomSafeDistance); Assert.Equal(A.Extra, vm.CropExtraPadding);
        Assert.Equal(A.ProtectPortrait, vm.ProtectFourByFive); Assert.Equal(A.ProtectLandscape, vm.ProtectFiveByFour);
        vm.RuleEditor.Side = 123; vm.RuleEditor.Top = 83; vm.RuleEditor.Name = "Work only";
        await vm.FlushSettingsAsync();
        Assert.Equal(123, vm.PhotoSideMargin); Assert.Equal(saved, vm.MarginRules.Single(rule => rule.Id == "a"));
        Assert.NotSame(saved, vm.WorkingMargins);
        var stored = await new SettingsStore(PresetPath).LoadAsync(); Assert.Equal(A, stored.MarginRules.Single(rule => rule.Id == "a"));
        using var restarted = Create(PresetPath); await restarted.InitializeAsync();
        Assert.Equal(123, restarted.PhotoSideMargin); Assert.Equal(83, restarted.PhotoTopMargin);
        Assert.Equal(A, restarted.MarginRules.Single(rule => rule.Id == "a"));
    });

    [Fact]
    public void ApplyModeSaveAsRejectsExistingNameAndNeverOverwritesId() => Ui.Run(async () =>
    {
        using var vm = await Presets(A); vm.RuleEditor.Side = 110;
        Assert.False(await vm.RuleEditor.SaveAsync()); Assert.Single(vm.MarginRules); Assert.Equal(A, vm.MarginRules[0]);
        vm.RuleEditor.Name = "New preset"; Assert.True(await vm.RuleEditor.SaveAsync());
        Assert.Equal(2, vm.MarginRules.Count); Assert.Equal(A, vm.MarginRules.Single(rule => rule.Id == "a"));
        var created = vm.MarginRules.Single(rule => rule.Name == "New preset"); Assert.NotEqual("a", created.Id); Assert.Equal(110, created.Side);
    });

    [Fact]
    public void DirtyEditCancelRetainsDraftAndDiscardRestoresWorkWithoutChangingSavedPreset() => Ui.Run(async () =>
    {
        using var vm = await Presets(A);
        Assert.True(await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit)); vm.RuleEditor.Side = 104; vm.RuleEditor.Name = "Unsaved";
        Assert.True(vm.RuleEditor.HasDirtyChanges); Assert.Equal(48, vm.PhotoSideMargin); Assert.Equal(A, vm.MarginRules[0]);
        await vm.FlushSettingsAsync(); var stored = await new SettingsStore(PresetPath).LoadAsync();
        Assert.Equal(48, stored.Layout.PhotoSideMargin); Assert.Equal(A, stored.MarginRules[0]);
        Assert.False(await vm.RuleEditor.ChangeModeAsync(PresetMode.Apply));
        Assert.False(await vm.RuleEditor.ChangeModeAsync(PresetMode.Apply, () => Task.FromResult(false)));
        Assert.Equal(104, vm.RuleEditor.Side); Assert.Equal("Unsaved", vm.RuleEditor.Name); Assert.Equal(PresetMode.Edit, vm.RuleEditor.Mode);
        Assert.True(await vm.RuleEditor.ChangeModeAsync(PresetMode.Apply, () => Task.FromResult(true)));
        Assert.Equal(48, vm.RuleEditor.Side); Assert.Equal(A, vm.MarginRules[0]); Assert.Equal(48, vm.PhotoSideMargin);
    });

    [Fact]
    public void EditSaveKeepsStableIdAndPersistsOnlyAfterExplicitSave() => Ui.Run(async () =>
    {
        using var vm = await Presets(A, B); await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit);
        vm.RuleEditor.Side = 101; vm.RuleEditor.Name = "Renamed A";
        Assert.Equal(A, (await new SettingsStore(PresetPath).LoadAsync()).MarginRules[0]);
        Assert.True(await vm.RuleEditor.SaveAsync()); Assert.False(vm.RuleEditor.HasDirtyChanges);
        var stored = await new SettingsStore(PresetPath).LoadAsync(); Assert.Equal(2, stored.MarginRules.Count);
        Assert.Equal("a", stored.MarginRules[0].Id); Assert.Equal(101, stored.MarginRules[0].Side); Assert.Equal("Renamed A", stored.MarginRules[0].Name);
        Assert.Equal(B, stored.MarginRules[1]); Assert.Equal(48, vm.PhotoSideMargin);
        using var restarted = Create(PresetPath); await restarted.InitializeAsync(); Assert.Equal(stored.MarginRules[0], restarted.MarginRules[0]);
        Assert.True(await vm.RuleEditor.ChangeModeAsync(PresetMode.Apply));
        Assert.Equal(101, vm.PhotoSideMargin); Assert.Equal("a", vm.SelectedMarginRuleId);
        await vm.FlushSettingsAsync();
    });

    [Fact]
    public void FailedEditSaveKeepsOriginalAndRetryUpdatesTheSameId() => Ui.Run(async () =>
    {
        using var vm = await Presets(A, B); await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit); vm.RuleEditor.Side = 98;
        var directory = Path.GetDirectoryName(PresetPath)!;
        Directory.Move(directory, directory + "-backup"); File.WriteAllText(directory, "Owned write blocker");
        Assert.False(await vm.RuleEditor.SaveAsync()); Assert.Equal(A, vm.MarginRules[0]);
        Assert.Equal(98, vm.RuleEditor.Side); Assert.Equal("a", vm.RuleEditor.SelectedRuleId); Assert.True(vm.RuleEditor.HasDirtyChanges);
        File.Delete(directory); Directory.Move(directory + "-backup", directory);
        Assert.Equal(A, (await new SettingsStore(PresetPath).LoadAsync()).MarginRules[0]);
        Assert.True(await vm.RuleEditor.SaveAsync()); Assert.Equal(2, vm.MarginRules.Count);
        Assert.Equal(98, vm.MarginRules.Single(rule => rule.Id == "a").Side); Assert.Equal(B, vm.MarginRules[1]);
    });

    [Fact]
    public void DirtySelectionAndLeaveRequireConfirmationAndNeverAutoSave() => Ui.Run(async () =>
    {
        using var vm = await Presets(A, B); await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit); vm.RuleEditor.Side = 112;
        Assert.False(await vm.RuleEditor.SelectPresetAsync("b", () => Task.FromResult(false))); Assert.Equal("a", vm.RuleEditor.SelectedRuleId);
        Assert.False(await vm.RuleEditor.LeaveAsync(() => Task.FromResult(false))); Assert.Equal(112, vm.RuleEditor.Side);
        Assert.True(await vm.RuleEditor.SelectPresetAsync("b", () => Task.FromResult(true))); Assert.Equal(B.Side, vm.RuleEditor.Side);
        vm.RuleEditor.Side = 99; Assert.True(await vm.RuleEditor.LeaveAsync(() => Task.FromResult(true)));
        Assert.Equal(PresetMode.Apply, vm.RuleEditor.Mode); Assert.Equal(48, vm.PhotoSideMargin);
        Assert.Equal(new[] { A, B }, (await new SettingsStore(PresetPath).LoadAsync()).MarginRules);
    });

    [Fact]
    public void ApplyModeAndBuiltInCannotDeleteEvenThroughDirectMethods() => Ui.Run(async () =>
    {
        using var vm = await Presets(A, new MarginRule());
        Assert.False(vm.RuleEditor.CanDelete); Assert.Null(vm.RuleEditor.BeginDeletion());
        Assert.False(await vm.RuleEditor.DeleteAsync(null)); Assert.False(await vm.DeleteRuleAsync("a"));
        Assert.True(await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit)); Assert.True(vm.RuleEditor.CanDelete);
        await vm.RuleEditor.SelectPresetAsync(MarginRule.DefaultId);
        Assert.False(vm.RuleEditor.CanDelete); Assert.Null(vm.RuleEditor.BeginDeletion());
        Assert.False(await vm.DeleteRuleAsync(MarginRule.DefaultId)); Assert.Equal(2, vm.MarginRules.Count);
    });

    [Fact]
    public void EachDeletionRequiresNewAcknowledgementAndCancelledTokenCannotBeReused() => Ui.Run(async () =>
    {
        using var vm = await Presets(A); await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit); vm.RuleEditor.Side = 95;
        var first = vm.RuleEditor.BeginDeletion()!; Assert.False(first.Acknowledged); Assert.Equal(A.Name, first.Name);
        Assert.False(await vm.RuleEditor.DeleteAsync(first)); first.Acknowledged = true;
        vm.RuleEditor.CancelDeletion(first); Assert.Equal(95, vm.RuleEditor.Side); Assert.Equal(A, vm.MarginRules[0]);
        Assert.False(await vm.RuleEditor.DeleteAsync(first));
        var second = vm.RuleEditor.BeginDeletion()!; Assert.False(second.Acknowledged); Assert.False(await vm.RuleEditor.DeleteAsync(second));
        vm.RuleEditor.CancelDeletion(second); Assert.True(vm.RuleEditor.HasDirtyChanges);
    });

    [Fact]
    public void DeletionIsIdentityBoundAndRevalidatesSavedPreset() => Ui.Run(async () =>
    {
        using var vm = await Presets(A, B); await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit);
        var request = vm.RuleEditor.BeginDeletion()!; request.Acknowledged = true;
        Assert.False(await vm.RuleEditor.SelectPresetAsync("b")); Assert.False(await vm.RuleEditor.ChangeModeAsync(PresetMode.Apply));
        vm.MarginRules[0] = A with { Name = "Externally updated" };
        Assert.False(await vm.RuleEditor.DeleteAsync(request)); Assert.Equal(2, vm.MarginRules.Count); Assert.Contains(vm.MarginRules, rule => rule.Id == "b");
        vm.RuleEditor.CancelDeletion(request);
    });

    [Fact]
    public void DeletionCommitsBeforeRemovalAndQueuedAutosaveCannotResurrectPreset() => Ui.Run(async () =>
    {
        using var vm = await Presets(A, B); vm.ApplyPresetToWork(A); vm.RuleEditor.Begin("a"); vm.RuleEditor.Side = 87;
        await vm.FlushSettingsAsync(); var work = vm.WorkingMargins;
        await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit);
        var request = vm.RuleEditor.BeginDeletion()!; request.Acknowledged = true;
        var store = (SettingsStore)typeof(MainViewModel).GetField("settingsStore", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vm)!;
        var gate = (SemaphoreSlim)typeof(SettingsStore).GetField("writeGate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(store)!;
        await gate.WaitAsync(); Task<bool> deletion;
        try
        {
            deletion = vm.RuleEditor.DeleteAsync(request); Assert.True(vm.RuleEditor.IsSaving);
            Assert.Contains(vm.MarginRules, rule => rule.Id == "a");
            Assert.False(await vm.RuleEditor.DeleteAsync(request));
            vm.ThemeChoice = 1; // Queued behind the preset transaction, must build a fresh snapshot.
        }
        finally { gate.Release(); }
        Assert.True(await deletion); await vm.FlushSettingsAsync();
        Assert.DoesNotContain(vm.MarginRules, rule => rule.Id == "a"); Assert.Equal(B, Assert.Single(vm.MarginRules));
        Assert.Equal(work, vm.WorkingMargins); Assert.Equal("b", vm.SelectedMarginRuleId);
        using var restarted = Create(PresetPath); await restarted.InitializeAsync();
        Assert.DoesNotContain(restarted.MarginRules, rule => rule.Id == "a"); Assert.Equal(87, restarted.PhotoSideMargin);
        Assert.False(await vm.RuleEditor.DeleteAsync(request));
    });

    [Fact]
    public void LastPresetDeletionAndRestartRemainEmptyWithoutApplyingFallback() => Ui.Run(async () =>
    {
        using var vm = await Presets(A); vm.RuleEditor.Side = 88; await vm.FlushSettingsAsync();
        await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit); var request = vm.RuleEditor.BeginDeletion()!; request.Acknowledged = true;
        Assert.True(await vm.RuleEditor.DeleteAsync(request)); Assert.Empty(vm.MarginRules);
        Assert.Null(vm.RuleEditor.SelectedRuleId); Assert.Null(vm.SelectedMarginRuleId); Assert.False(vm.RuleEditor.CanDelete); Assert.Equal(88, vm.PhotoSideMargin);
        using var restarted = Create(PresetPath); await restarted.InitializeAsync();
        Assert.Empty(restarted.MarginRules); Assert.Null(restarted.SelectedMarginRuleId); Assert.Equal(88, restarted.PhotoSideMargin);
    });

    [Fact]
    public void DraftRendersWithoutChangingWorkOrExportAndDiscardRestoresPreview() => Ui.Run(async () =>
    {
        using var vm = await Presets(A); await vm.LoadPhotoAsync(Source()); await Until(() => vm.CanExport);
        var before = vm.PreviewBytes!; vm.RuleEditor.StartPreview(); await Until(() => vm.RuleEditor.PreviewBytes is not null);
        var demoBefore = vm.RuleEditor.PreviewBytes;
        await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit);
        vm.RuleEditor.Side = 170; await Until(() => vm.RuleEditor.CompletedPreviewVersion == vm.RuleEditor.PreviewVersion);
        Assert.NotSame(demoBefore, vm.RuleEditor.PreviewBytes);
        Assert.True(vm.CanExport); Assert.Same(before, vm.PreviewBytes); Assert.Equal(48, vm.PhotoSideMargin); Assert.Equal(A, vm.MarginRules[0]);
        await vm.RuleEditor.LeaveAsync(() => Task.FromResult(true)); await Until(() => vm.CanExport);
        using var expected = new MagickImage(before); using var restored = new MagickImage(vm.PreviewBytes!);
        Assert.Equal(0, expected.Compare(restored, ErrorMetric.Absolute));
        await vm.RuleEditor.StopPreviewAsync(); await vm.FlushSettingsAsync();
    });
}
