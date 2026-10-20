using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using PhotoMargin.App.Services;
using PhotoMargin.App.ViewModels;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;
using PhotoMargin.Imaging;

namespace PhotoMargin.App.LogicTests;

public sealed partial class StateAndPersistenceTests
{
    private DataLocation Location(string name = "Data", string? legacy = null) => new(Path.Combine(root, name), legacy);
    private MainViewModel DataVm(DataLocation location, IRawMetadataReader? rawReader = null) => new(new ImageLoader(new ExifReader()), new LayoutEngine(new MagickTextMeasurer()),
        new PreviewService(new ImageRenderer()), new ExportService(new ImageRenderer()), new SettingsStore(location), rawReader);

    [Fact]
    public async Task DataInitializesSplitsFilesAndMigratesOnlyOnce()
    {
        var token = TestContext.Current.CancellationToken;
        var legacy = Path.Combine(root, "legacy.json"); var location = Location(legacy: legacy);
        var preset = BuiltInMarginPresets.Create() with { Id = "custom-stable", Name = "Legacy custom" };
        await new SettingsStore(legacy).SaveAsync(new() { Version = 10, MarginRules = [preset], Theme = ThemePreference.Light,
            Layout = new() { LensFontSize = 12, OutputSize = 2160 }, Export = new() { Format = ExportFormat.WebP, WebpQuality = 31 } }, token);
        var repository = new DataRepository(location); var loaded = await repository.LoadAsync(token);
        Assert.True(File.Exists(legacy)); Assert.True(Directory.Exists(location.BackupsPath));
        Assert.Equal("custom-stable", Assert.Single(loaded.MarginRules).Id); Assert.Equal(12, loaded.Layout.LensFontSize); Assert.Equal(31, loaded.Export!.WebpQuality);
        var settings = JsonNode.Parse(await File.ReadAllTextAsync(location.SettingsPath, token))!;
        var presets = JsonNode.Parse(await File.ReadAllTextAsync(location.PresetsPath, token))!;
        Assert.Equal(1, settings["schemaVersion"]!.GetValue<int>()); Assert.Null(settings["settings"]!["marginRules"]);
        Assert.Contains("default", presets["deletedBuiltInIds"]!.ToJsonString());
        await File.WriteAllTextAsync(legacy, "corrupted old file after successful migration", token);
        Assert.Equal("custom-stable", Assert.Single((await new DataRepository(location).LoadAsync(token)).MarginRules).Id);
        var rule = loaded.MarginRules[0];
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        foreach (var arrangement in new[] { InformationArrangement.TwoColumns, InformationArrangement.Stacked })
            Assert.Equal(preset.ForAspect(aspect, arrangement), rule.ForAspect(aspect, arrangement));
    }

    [Fact]
    public async Task FailedPairCommitRestoresBothFilesAndRetainsFiveBackupGroups()
    {
        var token = TestContext.Current.CancellationToken; var location = Location(); var repository = new DataRepository(location);
        var value = await repository.LoadAsync(token);
        for (var n = 0; n < 8; n++) await repository.SaveAsync(value with { Layout = value.Layout with { PhotoSideMargin = 50 + n } }, token);
        Assert.Equal(5, Directory.GetDirectories(location.BackupsPath).Length);
        var originalSettings = await File.ReadAllBytesAsync(location.SettingsPath, token); var originalPresets = await File.ReadAllBytesAsync(location.PresetsPath, token);
        foreach (var failureStage in new[] { "backup-created", "after-backup", "after-settings", "after-presets" })
        {
            repository.Fault = stage => { if (stage == failureStage) throw new IOException("Injected failure"); };
            await Assert.ThrowsAsync<IOException>(() => repository.SaveAsync(value with { Theme = ThemePreference.Dark, MarginRules = [] }, token));
            Assert.Equal(originalSettings, await File.ReadAllBytesAsync(location.SettingsPath, token)); Assert.Equal(originalPresets, await File.ReadAllBytesAsync(location.PresetsPath, token));
            Assert.False(File.Exists(Path.Combine(location.DirectoryPath, ".transaction.json")));
        }
        repository.Fault = null; await repository.SaveAsync(value, token); Assert.Equal(5, Directory.GetDirectories(location.BackupsPath).Length);
    }

    [Fact]
    public async Task CorruptionRecoversBackupWithoutDestroyingOriginalOrFutureFormats()
    {
        var token = TestContext.Current.CancellationToken; var location = Location(); var repository = new DataRepository(location);
        var value = await repository.LoadAsync(token); await repository.SaveAsync(value with { Theme = ThemePreference.Dark }, token);
        await File.WriteAllTextAsync(location.SettingsPath, "CORRUPT original", token);
        var restored = await repository.LoadAsync(token); Assert.Equal(value.Theme, restored.Theme); Assert.Contains("備份", repository.RecoveryNotice);
        Assert.Contains(Directory.GetDirectories(location.BackupsPath), folder => File.ReadAllText(Path.Combine(folder, "settings.json")) == "CORRUPT original");
        var node = JsonNode.Parse(await File.ReadAllTextAsync(location.SettingsPath, token))!; node["schemaVersion"] = 99;
        var future = node.ToJsonString(); await File.WriteAllTextAsync(location.SettingsPath, future, token);
        await Assert.ThrowsAsync<SettingsLoadException>(() => repository.LoadAsync(token));
        Assert.Equal(future, await File.ReadAllTextAsync(location.SettingsPath, token));
        await Assert.ThrowsAsync<SettingsLoadException>(() => repository.SaveAsync(value, token));
    }

    [Fact]
    public async Task BundleHasOnlyPortableCommittedDataAndPreservesDeletedBuiltInState()
    {
        var token = TestContext.Current.CancellationToken; var location = Location(); var repository = new DataRepository(location);
        var saved = await repository.LoadAsync(token);
        var workPath = Source();
        using (var vm = DataVm(location))
        {
            await vm.InitializeAsync(); await vm.LoadPhotoAsync(workPath); await Until(() => vm.CanExport);
            vm.ExportDirectory = Path.Combine(root, "PRIVATE output"); vm.ThemeChoice = 1; await vm.FlushSettingsAsync();
        }
        saved = await repository.LoadAsync(token);
        var custom = BuiltInMarginPresets.Create() with { Id = "only-custom", Name = "Saved" };
        await repository.SaveAsync(saved with { MarginRules = [custom], SelectedMarginRuleId = custom.Id }, token);
        var zip = Path.Combine(root, "settings.zip"); await repository.ExportBundleAsync(zip, token);
        using (var archive = ZipFile.OpenRead(zip))
        {
            Assert.Equal(new[] { "manifest.json", "presets.json", "settings.json" }, archive.Entries.Select(entry => entry.FullName).Order().ToArray());
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open()); var text = await reader.ReadToEndAsync(token);
                Assert.DoesNotContain(root.Replace("\\", "\\\\"), text); Assert.DoesNotContain("workInformation", text); Assert.DoesNotContain("workLayout", text);
            }
        }
        var other = Location("OtherData"); var destination = new DataRepository(other); var local = await destination.LoadAsync(token);
        await destination.SaveAsync(local with { Export = new() { Directory = "E:\\DestinationOnly" }, LastOutputDirectory = "E:\\DestinationOnly" }, token);
        var imported = await destination.ImportBundleAsync(await destination.ReadBundleAsync(zip, token), token: token);
        Assert.Equal("only-custom", Assert.Single(imported.MarginRules).Id); Assert.Equal("E:\\DestinationOnly", imported.Export!.Directory);
        var restarted = await new DataRepository(other).LoadAsync(token); Assert.DoesNotContain(restarted.MarginRules, rule => rule.Id == "default");
        Assert.Equal(2, Directory.GetDirectories(other.BackupsPath).Count(folder => Directory.GetFiles(folder).Length == 2));
    }

    [Theory]
    [InlineData("../escape.json")]
    [InlineData("/absolute.json")]
    [InlineData("C:\\evil.exe")]
    [InlineData("nested/settings.json")]
    public async Task BundleRejectsUnexpectedAndTraversalEntries(string entryName)
    {
        var token = TestContext.Current.CancellationToken; var repository = new DataRepository(Location()); await repository.LoadAsync(token);
        var path = Path.Combine(root, "bad.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "manifest.json", "settings.json", entryName })
            { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write("{}"); }
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ReadBundleAsync(path, token));
        Assert.False(File.Exists(Path.Combine(root, "escape.json")));
    }

    [Fact]
    public void DataLocationUsesExecutableBaseAndHasOnePermissionFallback()
    {
        var exe = Path.Combine(root, "Executable"); var user = Path.Combine(root, "User");
        var portable = DataLocation.Resolve(exe, user, false, _ => true);
        Assert.Equal(Path.Combine(exe, "Data"), portable.DirectoryPath);
        Assert.Equal(Path.Combine(user, "Data"), DataLocation.Resolve(exe, user, false, _ => false).DirectoryPath);
        Assert.Equal(Path.Combine(user, "Data"), DataLocation.Resolve(exe, user, true, _ => throw new InvalidOperationException()).DirectoryPath);
    }

    [Fact]
    public void ImportPreservesLiveWorkAndRestoresPreferencesWithoutApplyingSelectedPreset() => Ui.Run(async () =>
    {
        var token = TestContext.Current.CancellationToken; var source = new DataRepository(Location("SourceData")); var presetSettings = await source.LoadAsync(token);
        await source.SaveAsync(presetSettings with { Theme = ThemePreference.Light, Layout = presetSettings.Layout with { FontFamily = "Consolas", LensFontSize = 8, PhotoSideMargin = 300, OutputSize = 1440 },
            Export = new() { Format = ExportFormat.WebP, WebpQuality = 23, WebpLossless = true }, MarginRules = [] }, token);
        var zip = Path.Combine(root, "incoming.zip"); await source.ExportBundleAsync(zip, token);
        var location = Location(); using var vm = DataVm(location, new RawReader(_ => Task.FromResult(RawExample))); await vm.InitializeAsync(); var path = Source(); await vm.LoadPhotoAsync(path);
        Assert.True(await vm.ImportSelectedRawInformationAsync(FakeRaw, _ => Task.FromResult<RawInformationSelection?>(new([InformationField.Lens]))));
        vm.Camera = "Keep manual"; await Until(() => vm.CanExport);
        var side = vm.PhotoSideMargin; var font = vm.FontFamily; var lensSize = vm.LensFontSize; vm.ExportDirectory = "E:\\KeepLocal";
        Assert.True(await vm.ImportSettingsBundleAsync(() => Task.FromResult<string?>(zip), () => Task.FromResult(true), () => Task.FromResult(ImportDraftChoice.Cancel)));
        Assert.Equal("Keep manual", vm.Camera); Assert.Equal(path, vm.SourcePath); Assert.Equal(side, vm.PhotoSideMargin); Assert.Equal(font, vm.FontFamily); Assert.Equal(lensSize, vm.LensFontSize);
        Assert.Equal(FakeRaw, vm.ConfirmedRaw!.SourcePath); Assert.Equal(RawExample.LensModel, vm.Lens); Assert.Equal(InformationOrigin.Raw, vm.InformationSource(InformationField.Lens));
        Assert.Empty(vm.MarginRules); Assert.Equal(2, vm.ThemeChoice); Assert.Equal(1, vm.ColorModeIndex); Assert.Equal(1440, vm.OutputSize);
        Assert.Equal(2, vm.ExportFormatIndex); Assert.Equal(23, vm.ExportQuality); Assert.Equal("E:\\KeepLocal", vm.ExportDirectory);
        await Until(() => vm.CanExport); await vm.FlushSettingsAsync();
        var stored = await new DataRepository(location).LoadAsync(token); Assert.Equal("Consolas", stored.Layout.FontFamily); Assert.Equal(font, stored.WorkLayout!.FontFamily);
        using var restored = DataVm(location); await restored.InitializeAsync(); await restored.LoadPhotoAsync(path); await Until(() => restored.CanExport);
        Assert.Equal("Keep manual", restored.Camera); Assert.Equal(font, restored.FontFamily); Assert.Equal(side, restored.PhotoSideMargin); await restored.FlushSettingsAsync();
    });

    private async Task<string> RewriteBundleAsync(string source, string destination, Action<JsonObject, JsonObject, JsonObject> mutate)
    {
        var data = new Dictionary<string, JsonObject>();
        using (var archive = ZipFile.OpenRead(source))
            foreach (var entry in archive.Entries)
            { using var reader = new StreamReader(entry.Open()); data[entry.FullName] = JsonNode.Parse(await reader.ReadToEndAsync(TestContext.Current.CancellationToken))!.AsObject(); }
        mutate(data["manifest.json"], data["settings.json"], data["presets.json"]);
        var settings = Encoding.UTF8.GetBytes(data["settings.json"].ToJsonString()); var presets = Encoding.UTF8.GetBytes(data["presets.json"].ToJsonString());
        data["manifest.json"]["settingsSha256"] = Convert.ToHexString(SHA256.HashData(settings));
        data["manifest.json"]["presetsSha256"] = Convert.ToHexString(SHA256.HashData(presets));
        using var result = ZipFile.Open(destination, ZipArchiveMode.Create);
        foreach (var item in data) { using var writer = new StreamWriter(result.CreateEntry(item.Key).Open(), new UTF8Encoding(false)); writer.Write(item.Value.ToJsonString()); }
        return destination;
    }

    [Fact]
    public async Task BundleRejectsInvalidIdsValuesPathsAndFutureSchemas()
    {
        var token = TestContext.Current.CancellationToken; var repository = new DataRepository(Location()); await repository.LoadAsync(token);
        var original = Path.Combine(root, "valid.zip"); await repository.ExportBundleAsync(original, token);
        var duplicate = await RewriteBundleAsync(original, Path.Combine(root, "duplicate.zip"), (_, _, p) => p["presets"]!.AsArray().Add(p["presets"]![0]!.DeepClone()));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ReadBundleAsync(duplicate, token));
        var badValue = await RewriteBundleAsync(original, Path.Combine(root, "value.zip"), (_, s, _) => s["settings"]!["layout"]!["outputSize"] = -1);
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ReadBundleAsync(badValue, token));
        var path = await RewriteBundleAsync(original, Path.Combine(root, "path.zip"), (_, s, _) => s["settings"]!["lastOutputDirectory"] = "C:\\Private");
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ReadBundleAsync(path, token));
        var newer = await RewriteBundleAsync(original, Path.Combine(root, "future.zip"), (m, _, _) => m["schemaVersion"] = 99);
        await Assert.ThrowsAsync<UnsupportedDataVersionException>(() => repository.ReadBundleAsync(newer, token));
        var old = await RewriteBundleAsync(original, Path.Combine(root, "old.zip"), (_, s, p) =>
        {
            s["settings"]!["version"] = 8;
            var rule = p["presets"]![0]!.AsObject(); rule.Remove("arrangements"); rule["builtInRevision"] = 0;
        });
        var candidate = await repository.ReadBundleAsync(old, token); var imported = await repository.ImportBundleAsync(candidate, token: token);
        Assert.Equal("default", Assert.Single(imported.MarginRules).Id);
        foreach (var aspect in Enum.GetValues<SourceAspect>()) Assert.NotNull(imported.MarginRules[0].Arrangements!.Get(aspect)!.Stacked);
        var large = Path.Combine(root, "large.zip");
        using (var archive = ZipFile.Open(large, ZipArchiveMode.Create))
            foreach (var name in new[] { "manifest.json", "settings.json", "presets.json" })
            { using var entry = archive.CreateEntry(name).Open(); if (name == "settings.json") entry.Write(new byte[4 * 1024 * 1024 + 1]); else entry.Write("{}"u8); }
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.ReadBundleAsync(large, token));
    }

    [Fact]
    public async Task PendingInterruptedTransactionRestoresOldMatchingPair()
    {
        var token = TestContext.Current.CancellationToken; var location = Location(); var repository = new DataRepository(location); var value = await repository.LoadAsync(token);
        var id = Guid.NewGuid().ToString("N"); var stage = Path.Combine(location.DirectoryPath, ".transaction-" + id); Directory.CreateDirectory(stage);
        File.Copy(location.SettingsPath, Path.Combine(stage, "old-settings.json")); File.Copy(location.PresetsPath, Path.Combine(stage, "old-presets.json"));
        var partial = JsonNode.Parse(await File.ReadAllTextAsync(location.SettingsPath, token))!;
        partial["transactionId"] = id; partial["settings"]!["theme"] = 2;
        await File.WriteAllTextAsync(location.SettingsPath, partial.ToJsonString(), token);
        await File.WriteAllTextAsync(Path.Combine(location.DirectoryPath, ".transaction.json"), JsonSerializer.Serialize(new { id, hadSettings = true, hadPresets = true }), token);
        Assert.Equal(value.Theme, (await repository.LoadAsync(token)).Theme); Assert.False(Directory.Exists(stage));
    }

    [Fact]
    public void DirtyImportCancelPreservesDraftAndSuccessfulDiscardDoesNotSaveDraft() => Ui.Run(async () =>
    {
        var token = TestContext.Current.CancellationToken; var location = Location(); using var vm = DataVm(location); await vm.InitializeAsync();
        vm.RuleEditor.Begin(vm.SelectedMarginRuleId); Assert.True(await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit));
        vm.RuleEditor.Side = 123; Assert.True(vm.RuleEditor.HasDirtyChanges);
        var zip = Path.Combine(root, "clean.zip"); await new DataRepository(location).ExportBundleAsync(zip, token);
        Assert.False(await vm.ImportSettingsBundleAsync(() => Task.FromResult<string?>(zip), () => Task.FromResult(true), () => Task.FromResult(ImportDraftChoice.Cancel)));
        Assert.True(vm.RuleEditor.HasDirtyChanges); Assert.Equal(123, vm.RuleEditor.Side);
        Assert.False(await vm.ImportSettingsBundleAsync(() => Task.FromResult<string?>(zip), () => Task.FromResult(false), () => Task.FromResult(ImportDraftChoice.Discard)));
        Assert.True(vm.RuleEditor.HasDirtyChanges);
        Assert.True(await vm.ImportSettingsBundleAsync(() => Task.FromResult<string?>(zip), () => Task.FromResult(true), () => Task.FromResult(ImportDraftChoice.Discard)));
        Assert.False(vm.RuleEditor.HasDirtyChanges); Assert.NotEqual(123, vm.MarginRules[0].ForAspect(SourceAspect.FourByThree).Side);
        var pending = new TaskCompletionSource<string?>(); var export = vm.ExportSettingsBundleAsync(_ => pending.Task);
        Assert.True(vm.IsDataBusy); Assert.False(vm.CanManageData); Assert.False(vm.CanSelectPhoto);
        await vm.ExportSettingsBundleAsync(_ => throw new InvalidOperationException("Must not reenter")); pending.SetResult(null); await export;
        Assert.False(vm.IsDataBusy); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void ConfirmedSaveDraftIsBackedUpBeforeReplacementAndImportFailurePreservesUi() => Ui.Run(async () =>
    {
        var token = TestContext.Current.CancellationToken; var location = Location(); var store = new SettingsStore(location);
        using var vm = new MainViewModel(new ImageLoader(new ExifReader()), new LayoutEngine(new MagickTextMeasurer()), new PreviewService(new ImageRenderer()), new ExportService(new ImageRenderer()), store);
        await vm.InitializeAsync(); vm.RuleEditor.Begin(vm.SelectedMarginRuleId); await vm.RuleEditor.ChangeModeAsync(PresetMode.Edit); vm.RuleEditor.Side = 123;
        var incoming = new DataRepository(Location("IncomingData")); var value = await incoming.LoadAsync(token);
        await incoming.SaveAsync(value with { Theme = ThemePreference.Dark, MarginRules = [] }, token);
        var zip = Path.Combine(root, "incoming.zip"); await incoming.ExportBundleAsync(zip, token);
        Assert.False(await vm.ImportSettingsBundleAsync(() => Task.FromResult<string?>(zip), () => Task.FromResult(false), () => Task.FromResult(ImportDraftChoice.Save)));
        Assert.True(vm.RuleEditor.HasDirtyChanges); Assert.NotEqual(123, (await store.LoadAsync(token)).MarginRules[0].ForAspect(SourceAspect.FourByThree).Side);
        store.Repository.Fault = stage => { if (stage == "after-settings") throw new IOException("Injected import failure"); };
        Assert.False(await vm.ImportSettingsBundleAsync(() => Task.FromResult<string?>(zip), () => Task.FromResult(true), () => Task.FromResult(ImportDraftChoice.Discard)));
        Assert.True(vm.RuleEditor.HasDirtyChanges); Assert.Single(vm.MarginRules); Assert.Equal(0, vm.ThemeChoice);
        store.Repository.Fault = null;
        Assert.True(await vm.ImportSettingsBundleAsync(() => Task.FromResult<string?>(zip), () => Task.FromResult(true), () => Task.FromResult(ImportDraftChoice.Save)));
        Assert.Empty(vm.MarginRules); Assert.False(vm.RuleEditor.HasDirtyChanges);
        Assert.Contains(Directory.GetDirectories(location.BackupsPath), folder =>
        {
            var preset = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "presets.json")))!["presets"]!.AsArray().FirstOrDefault();
            return preset?["arrangements"]?["fourByThree"]?["twoColumns"]?["side"]?.GetValue<double>() == 123;
        });
        await vm.FlushSettingsAsync();
    });

    [Fact]
    public async Task DataMigrationDoesNotRefreshModifiedLegacyBuiltIn()
    {
        var token = TestContext.Current.CancellationToken; var legacy = Path.Combine(root, "old.json");
        var modified = BuiltInMarginPresets.Create("My built-in name").WithConfiguration(SourceAspect.FourByThree, InformationArrangement.Stacked,
            new() { Side = 333, Gap = 87 }) with { BuiltInRevision = 1 };
        await new SettingsStore(legacy).SaveAsync(new() { MarginRules = [modified], SelectedMarginRuleId = modified.Id }, token);
        var loaded = await new DataRepository(Location(legacy: legacy)).LoadAsync(token);
        Assert.Equal(modified, Assert.Single(loaded.MarginRules));
    }

    [Fact]
    public async Task ConfiguredUserLegacyDataMigratesReadOnly()
    {
        var path = Environment.GetEnvironmentVariable("PHOTOMARGIN_LEGACY_SAMPLE");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(path) || !File.Exists(path), "Set PHOTOMARGIN_LEGACY_SAMPLE for the existing user-data read-only check.");
        var token = TestContext.Current.CancellationToken; var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path!, token)));
        var expected = SettingsStore.ReadLegacyForData(path!); var location = Location(legacy: path);
        var actual = await new DataRepository(location).LoadAsync(token);
        Assert.Equal(expected.MarginRules, actual.MarginRules); Assert.Equal(expected.Layout, actual.Layout); Assert.Equal(expected.Export, actual.Export);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path!, token))));
        Assert.Equal(expected.MarginRules.Count, (await new DataRepository(location).LoadAsync(token)).MarginRules.Count);
    }

    [Fact]
    public async Task BackupRetentionIgnoresUnrelatedFolders()
    {
        var token = TestContext.Current.CancellationToken; var location = Location(); var repository = new DataRepository(location); var value = await repository.LoadAsync(token);
        for (var index = 0; index < 5; index++) Directory.CreateDirectory(Path.Combine(location.BackupsPath, "zzz-user-folder-" + index));
        for (var index = 0; index < 8; index++) await repository.SaveAsync(value with { Layout = value.Layout with { PhotoSideMargin = 60 + index } }, token);
        Assert.Equal(5, Directory.GetDirectories(location.BackupsPath).Count(folder => !Path.GetFileName(folder).StartsWith("zzz-")));
        Assert.Equal(5, Directory.GetDirectories(location.BackupsPath).Count(folder => Path.GetFileName(folder).StartsWith("zzz-")));
    }

    [Fact]
    public void BackupAndBundleDatesDoNotUseTaiwanCalendarYear() => Ui.Run(async () =>
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var culture = new System.Globalization.CultureInfo("zh-TW", false);
            culture.DateTimeFormat.Calendar = new System.Globalization.TaiwanCalendar();
            System.Globalization.CultureInfo.CurrentCulture = culture;
            var location = Location(); var repository = new DataRepository(location); var token = TestContext.Current.CancellationToken;
            var value = await repository.LoadAsync(token);
            for (var i = 0; i < 7; i++) await repository.SaveAsync(value with { Layout = value.Layout with { PhotoSideMargin = 60 + i } }, token);
            Assert.Equal(5, Directory.GetDirectories(location.BackupsPath).Length);
            Assert.All(Directory.GetDirectories(location.BackupsPath), folder => Assert.StartsWith(DateTime.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture), Path.GetFileName(folder)));
            using var vm = DataVm(location); await vm.InitializeAsync();
            await vm.ExportSettingsBundleAsync(name =>
            {
                Assert.StartsWith("PhotoMargin_Settings_" + DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture), name);
                return Task.FromResult<string?>(null);
            });
            await vm.FlushSettingsAsync();
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    });
}
