using PhotoMargin.App.Services;
using PhotoMargin.App.ViewModels;
using PhotoMargin.Core.Models;

namespace PhotoMargin.App.LogicTests;

public sealed partial class StateAndPersistenceTests
{
    [Fact]
    public void LockedSettingsCannotBeReplacedAfterUnlockWithoutSuccessfulReload() => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "locked-settings.json");
        await new SettingsStore(path).SaveAsync(new PersistedSettings { MarginRules = [A, B], SelectedMarginRuleId = "b", Layout = new() { PhotoSideMargin = 91 } });
        var before = File.ReadAllBytes(path); var store = new SettingsStore(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<SettingsLoadException>(() => store.LoadAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<SettingsLoadException>(() => store.SaveAsync(new PersistedSettings(), TestContext.Current.CancellationToken));
        Assert.Equal(before, File.ReadAllBytes(path));
        var loaded = await store.LoadAsync(); Assert.Equal(new[] { A, B }, loaded.MarginRules); Assert.Equal("b", loaded.SelectedMarginRuleId);
        await store.SaveAsync(loaded with { Theme = ThemePreference.Dark });
        Assert.Equal(new[] { A, B }, (await store.LoadAsync()).MarginRules);
    });

    [Theory]
    [InlineData("{ broken json")]
    [InlineData("null")]
    [InlineData("{\"version\":999,\"marginRules\":[{\"id\":\"future\",\"name\":\"Do not erase\"}]}")]
    public void InvalidOrUnknownSettingsAreReportedAndNeverOverwritten(string content) => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "invalid.json"); File.WriteAllText(path, content);
        var before = File.ReadAllBytes(path); var store = new SettingsStore(path);
        await Assert.ThrowsAsync<SettingsLoadException>(() => store.LoadAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<SettingsLoadException>(() => store.SaveAsync(new PersistedSettings(), TestContext.Current.CancellationToken));
        Assert.Equal(before, File.ReadAllBytes(path));
    });

    [Fact]
    public void MissingFileAfterFailedReadDoesNotSilentlyReleaseWriteProtection() => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "disappeared.json"); File.WriteAllText(path, "not valid json");
        var store = new SettingsStore(path); await Assert.ThrowsAsync<SettingsLoadException>(() => store.LoadAsync(TestContext.Current.CancellationToken));
        File.Move(path, path + ".backup");
        await Assert.ThrowsAsync<SettingsLoadException>(() => store.LoadAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<SettingsLoadException>(() => store.SaveAsync(new PersistedSettings(), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(path)); Assert.Equal("not valid json", File.ReadAllText(path + ".backup"));
    });

    [Fact]
    public void TrueFirstLaunchStillSeedsAndSavesNormally() => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "first-run", "settings.json"); var store = new SettingsStore(path);
        var loaded = await store.LoadAsync(); Assert.Equal(MarginRule.DefaultId, Assert.Single(loaded.MarginRules).Id);
        await store.SaveAsync(loaded); Assert.True(File.Exists(path)); Assert.Single((await store.LoadAsync()).MarginRules);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidParentPathIsNotTreatedAsFirstLaunch(bool nested) => Ui.Run(async () =>
    {
        var parent = Path.Combine(root, "not-a-directory"); File.WriteAllText(parent, "Owned blocker");
        var path = nested ? Path.Combine(parent, "subdirectory", "settings.json") : Path.Combine(parent, "settings.json");
        var store = new SettingsStore(path);
        await Assert.ThrowsAsync<SettingsLoadException>(() => store.LoadAsync(TestContext.Current.CancellationToken));
        File.Delete(parent); Directory.CreateDirectory(parent);
        await Assert.ThrowsAsync<SettingsLoadException>(() => store.SaveAsync(new PersistedSettings(), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(path));
    });

    [Fact]
    public void ViewModelBlocksAutosavePresetCommandsAndCloseFlushUntilExplicitRetry() => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "vm-locked.json");
        await new SettingsStore(path).SaveAsync(new PersistedSettings { MarginRules = [A, B], SelectedMarginRuleId = "b", Layout = new() { PhotoSideMargin = 91 }, Theme = ThemePreference.Dark });
        var before = File.ReadAllBytes(path); using var vm = Create(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) await vm.InitializeAsync();
        Assert.True(vm.HasSettingsLoadFailure); Assert.True(vm.IsSettingsReadOnly); Assert.Contains("無法讀取設定", vm.PersistenceError);
        vm.ThemeChoice = 2; vm.JpegQuality = 82; vm.RuleEditor.Name = "Must not replace presets";
        Assert.False(vm.RuleEditor.CanSave); Assert.False(await vm.RuleEditor.SaveAsync()); Assert.False(await vm.DeleteRuleAsync("b"));
        await Assert.ThrowsAsync<IOException>(() => vm.FlushSettingsAsync());
        Assert.Equal(before, File.ReadAllBytes(path)); Assert.True(vm.HasSettingsLoadFailure);
        Assert.True(await vm.RetryLoadSettingsAsync()); Assert.False(vm.IsSettingsReadOnly); Assert.False(vm.HasSettingsLoadFailure);
        Assert.Empty(vm.PersistenceError); Assert.Equal(91, vm.PhotoSideMargin); Assert.Equal(1, vm.ThemeChoice);
        Assert.Equal(new[] { A, B }, vm.MarginRules); Assert.Equal("b", vm.SelectedMarginRuleId);
        // Retrying again replaces the collection instead of adding duplicate presets.
        Assert.True(await vm.RetryLoadSettingsAsync()); Assert.Equal(2, vm.MarginRules.Count);
        await vm.FlushSettingsAsync(); Assert.Equal(new[] { A, B }, (await new SettingsStore(path).LoadAsync()).MarginRules);
    });

    [Fact]
    public void BindingChangesQueuedDuringInitializationCannotSaveTemporaryDefaults() => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "queued-initialize.json"); var store = new SettingsStore(path);
        await store.SaveAsync(new PersistedSettings { MarginRules = [A, B], SelectedMarginRuleId = "b", Theme = ThemePreference.Dark });
        var renderer = new PhotoMargin.Imaging.ImageRenderer();
        using var vm = new MainViewModel(new PhotoMargin.Imaging.ImageLoader(new PhotoMargin.Imaging.ExifReader()),
            new PhotoMargin.Core.LayoutEngine(new PhotoMargin.Imaging.MagickTextMeasurer()), new PreviewService(renderer), new PhotoMargin.Imaging.ExportService(renderer), store);
        var gate = (SemaphoreSlim)typeof(SettingsStore).GetField("writeGate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(store)!;
        await gate.WaitAsync(); Task initialization;
        try { initialization = vm.InitializeAsync(); vm.ThemeChoice = 2; vm.JpegQuality = 84; }
        finally { gate.Release(); }
        await initialization; await vm.FlushSettingsAsync(); var saved = await new SettingsStore(path).LoadAsync();
        Assert.Equal(ThemePreference.Dark, saved.Theme); Assert.Equal(95, saved.Layout.JpegQuality);
        Assert.Equal(new[] { A, B }, saved.MarginRules); Assert.Equal("b", saved.SelectedMarginRuleId);
    });

    [Fact]
    public void SaveAsAtomicallyPersistsNewSelectionWithoutReapplyingWork() => Ui.Run(async () =>
    {
        using var vm = await Presets(A, B); var priorSelection = vm.SelectedMarginRuleId;
        vm.RuleEditor.Side = 123; vm.RuleEditor.Name = "Save-as selection";
        var work = vm.WorkingMargins;
        Assert.True(await vm.RuleEditor.SaveAsync()); var saved = vm.MarginRules.Single(rule => rule.Name == "Save-as selection");
        Assert.NotEqual(priorSelection, saved.Id); Assert.Equal(saved.Id, vm.SelectedMarginRuleId); Assert.Equal(saved.Id, vm.RuleEditor.SelectedRuleId);
        Assert.Equal(work, vm.WorkingMargins);
        var stored = await new SettingsStore(PresetPath).LoadAsync(); Assert.Equal(saved.Id, stored.SelectedMarginRuleId);
        Assert.Equal(123, stored.Layout.PhotoSideMargin);
        Assert.True(await vm.RuleEditor.LeaveAsync()); vm.RuleEditor.Begin(vm.SelectedMarginRuleId);
        Assert.Equal(saved.Id, vm.RuleEditor.SelectedRuleId);
        using var restarted = Create(PresetPath); await restarted.InitializeAsync();
        Assert.Equal(saved.Id, restarted.SelectedMarginRuleId); Assert.Equal(saved.Id, restarted.RuleEditor.SelectedRuleId);
        Assert.Equal(123, restarted.PhotoSideMargin);
    });

    [Fact]
    public void FailedSaveAsPreservesBothSelectionsAndDiskUntilRetryCommits() => Ui.Run(async () =>
    {
        using var vm = await Presets(A, B); vm.RuleEditor.Name = "Retry selection"; vm.RuleEditor.Side = 99;
        await vm.FlushSettingsAsync(); var before = File.ReadAllBytes(PresetPath);
        var directory = Path.GetDirectoryName(PresetPath)!;
        Directory.Move(directory, directory + "-backup"); File.WriteAllText(directory, "Owned write blocker");
        Assert.False(await vm.RuleEditor.SaveAsync()); Assert.Equal("a", vm.SelectedMarginRuleId); Assert.Equal("a", vm.RuleEditor.SelectedRuleId);
        Assert.Equal(2, vm.MarginRules.Count);
        File.Delete(directory); Directory.Move(directory + "-backup", directory);
        Assert.Equal(before, File.ReadAllBytes(PresetPath)); Assert.True(await vm.RuleEditor.SaveAsync());
        Assert.Equal(vm.RuleEditor.SelectedRuleId, vm.SelectedMarginRuleId);
        Assert.Equal(vm.SelectedMarginRuleId, (await new SettingsStore(PresetPath).LoadAsync()).SelectedMarginRuleId);
    });

    [Fact]
    public void SaveAsWithRealPhotoDoesNotApplyDifferentAspectConfiguration() => Ui.Run(async () =>
    {
        using var vm = await Presets(A, B); await vm.LoadPhotoAsync(Source()); await Until(() => vm.CanExport);
        vm.Camera = "Keep manual information"; vm.RuleEditor.AspectIndex = 2; vm.RuleEditor.Side = 139;
        vm.RuleEditor.Name = "Keep work across save-as"; await Until(() => vm.CanExport);
        var preview = vm.PreviewBytes; var work = vm.WorkingMargins; Assert.True(await vm.RuleEditor.SaveAsync());
        Assert.Equal(work, vm.WorkingMargins); Assert.Same(preview, vm.PreviewBytes); Assert.Equal("Keep manual information", vm.Camera);
        Assert.Equal(139, vm.PhotoSideMargin); Assert.Equal(vm.RuleEditor.SelectedRuleId, vm.SelectedMarginRuleId);
        await vm.FlushSettingsAsync();
    });
}
