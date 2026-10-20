using System.Text.Json;
using PhotoMargin.Core.Models;
using PhotoMargin.Core;

namespace PhotoMargin.App.Services;

public enum ThemePreference
{
    System,
    Dark,
    Light
}

public sealed class SettingsLoadException(string message, Exception? inner = null) : IOException(message, inner);

public sealed record PersistedSettings
{
    public const int CurrentVersion = 10;
    public ExportPreferences? Export { get; init; }
    public int Version { get; init; } = CurrentVersion;
    public LayoutSettings Layout { get; init; } = new();
    public bool ShowGuides { get; init; } = true;
    public ThemePreference Theme { get; init; } = ThemePreference.System;
    public string? LastOutputDirectory { get; init; }
    public List<MarginRule> MarginRules { get; init; } = [];
    public string? SelectedMarginRuleId { get; init; } = MarginRule.DefaultId;
    public SourceAspect DemonstrationAspect { get; init; } = SourceAspect.FourByThree;
    public InformationArrangement? DemonstrationArrangement { get; init; }
    public WorkInformationSnapshot? WorkInformation { get; init; }
    public LayoutSettings? WorkLayout { get; init; }
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string settingsPath;
    private readonly DataRepository? dataRepository;
    public string DataDirectory => dataRepository?.DirectoryPath ?? Path.GetDirectoryName(settingsPath)!;
    public string DataNotice => dataRepository?.RecoveryNotice ?? "";
    internal DataRepository Repository => dataRepository ?? throw new InvalidOperationException("此測試用舊格式儲存庫不支援設定集。");
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private SettingsLoadException? loadFailure;

    public SettingsStore() : this(DataLocation.Current) { }
    internal SettingsStore(DataLocation location) { settingsPath = location.SettingsPath; dataRepository = new(location); }

    internal SettingsStore(string settingsPath) => this.settingsPath = Path.GetFullPath(settingsPath);
    internal static PersistedSettings InitialSettings()
    {
        var builtIn = BuiltInMarginPresets.Create();
        return new() { MarginRules = [builtIn], Layout = builtIn.ForAspect(SourceAspect.FourByThree).ApplyTo(new()) };
    }

    public async Task<PersistedSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (dataRepository is not null) return await dataRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A reader may finish reading the previous snapshot while another process replaces it.
            await using var stream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var value = await JsonSerializer.DeserializeAsync<PersistedSettings>(stream, Options, cancellationToken).ConfigureAwait(false);
            if (value is null || value.Version is not (2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or PersistedSettings.CurrentVersion))
            {
                throw new InvalidDataException("設定內容為空或格式版本不受支援。");
            }

            var loaded = NormalizeLegacy(value);
            loadFailure = null;
            return loaded;
        }
        catch (FileNotFoundException) when (loadFailure is null)
        {
            return InitialSettings();
        }
        catch (DirectoryNotFoundException) when (loadFailure is null && IsNewDirectoryLocation())
        {
            return InitialSettings();
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            loadFailure = new SettingsLoadException("無法讀取設定；為保護既有預設，已停止保存。請解除檔案鎖定或修復設定檔後重新讀取。\n" + exception.Message, exception);
            throw loadFailure;
        }
        finally { writeGate.Release(); }
    }

    internal static PersistedSettings ReadLegacyForData(string path)
    {
        var value = JsonSerializer.Deserialize<PersistedSettings>(DataRepository.ReadLimited(path), Options)
            ?? throw new InvalidDataException("舊設定為空。");
        return NormalizeLegacy(value, false);
    }
    internal static PersistedSettings NormalizeLegacy(PersistedSettings value, bool refreshBuiltIns = true)
    {
            if (value.Version is < 2 or > PersistedSettings.CurrentVersion) throw new UnsupportedDataVersionException("不支援此舊設定版本。");
            var layout = (value.Layout ?? new LayoutSettings()).Validate();
            ValidateWorkInformation(value.WorkInformation);
            // Previous shipped UI had no explicit 5:4 selection entrance: the
            // dual-true combination was its automatic default. Preserve 5:4-only
            // choices and all explicit v7 combinations. Never alter numeric values.
            if (value.Version < 7 && layout.ProtectFourByFive && layout.ProtectFiveByFour)
                layout = layout with { ProtectFiveByFour = false };
            var rules = (value.MarginRules ?? []).Where(rule => rule is not null && !string.IsNullOrWhiteSpace(rule.Id))
                .DistinctBy(rule => rule.Id).Select(rule => NormalizeRule(rule, value.Version < 7)).ToList();
            // Only legacy documents need a built-in seed. A v4 empty list is deliberate.
            if (value.Version < 4 && !rules.Any(rule => rule.Id == MarginRule.DefaultId)) rules.Insert(0, MarginRule.FromLayout(layout).Validate());
            // Catalog revisions are independent of the settings schema. A v8
            // document can still contain revision 0/1 preset data.
            var oldBuiltIn = rules.FirstOrDefault(rule => rule.Id == MarginRule.DefaultId);
            if (oldBuiltIn is not null && refreshBuiltIns)
            {
                var active = layout.Arrangement;
                var work = MarginConfiguration.From(MarginRule.FromLayout(layout));
                var refreshed = BuiltInMarginPresets.RefreshPreviousCatalog(oldBuiltIn, rules.Select(rule => rule.Name));
                var upgraded = BuiltInMarginPresets.UpgradeLegacy(refreshed.Current);
                // Never replace a manually adjusted work snapshot. A matching
                // copy of the selected built-in follows its repaired configuration.
                var followsPreviousCatalog = oldBuiltIn.BuiltInRevision == 1 &&
                    work == oldBuiltIn.ForAspect(SourceAspect.FourByThree, active);
                var followsLegacy = oldBuiltIn.BuiltInRevision == 0 &&
                    oldBuiltIn.ForAspect(SourceAspect.FourByThree, active) == BuiltInMarginPresets.LegacyConfiguration &&
                    work == BuiltInMarginPresets.LegacyConfiguration;
                if (value.SelectedMarginRuleId == MarginRule.DefaultId && (followsPreviousCatalog || followsLegacy))
                    layout = upgraded.ForAspect(SourceAspect.FourByThree, active).ApplyTo(layout);
                rules[rules.IndexOf(oldBuiltIn)] = upgraded;
                if (refreshed.Backup is not null) rules.Add(refreshed.Backup);
            }
            return value with
            {
                Version = PersistedSettings.CurrentVersion,
                Export = (value.Export ?? new ExportPreferences { JpegQuality = layout.JpegQuality, Directory = value.LastOutputDirectory }).Validate(),
                Layout = layout with { Visibility = layout.Visibility ?? new FieldVisibility() },
                MarginRules = rules,
                DemonstrationAspect = Enum.IsDefined(value.DemonstrationAspect) ? value.DemonstrationAspect : SourceAspect.FourByThree,
                DemonstrationArrangement = (value.DemonstrationArrangement ?? layout.Arrangement) == InformationArrangement.Stacked
                    ? InformationArrangement.Stacked : InformationArrangement.TwoColumns,
                Theme = Enum.IsDefined(value.Theme) ? value.Theme : ThemePreference.System
            };
    }

    private static MarginRule NormalizeRule(MarginRule rule, bool migrateDualProtection)
    {
        var normalized = rule.Validate();
        if (!migrateDualProtection) return normalized;
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        foreach (var arrangement in new[] { InformationArrangement.TwoColumns, InformationArrangement.Stacked })
        {
            var configuration = normalized.ForAspect(aspect, arrangement);
            if (configuration.ProtectPortrait && configuration.ProtectLandscape)
                normalized = normalized.WithConfiguration(aspect, arrangement, configuration with { ProtectLandscape = false });
        }
        return normalized;
    }

    internal static void ValidateWorkInformation(WorkInformationSnapshot? value)
    {
        if (value is null) return;
        if (string.IsNullOrWhiteSpace(value.SourcePath) || value.Length < 0 || value.PhotoExif is null || value.Current is null ||
            value.Origins is null || value.Origins.Count != Enum.GetValues<InformationField>().Length ||
            value.Origins.Any(pair => !Enum.IsDefined(pair.Key) || !Enum.IsDefined(pair.Value)) || !ValidTime(value.Current.Time) ||
            value.Raw is { } raw && (string.IsNullOrWhiteSpace(raw.SourcePath) || raw.Metadata is null || raw.AppliedValues is null ||
                raw.AppliedFields is null || raw.AppliedFields.Any(field => !Enum.IsDefined(field)) || !ValidTime(raw.AppliedValues.Time)))
            throw new InvalidDataException("已保存的照片／RAW 資訊快照無效；為避免遺失資料，請修復設定後重試。");
        static bool ValidTime(TimeSpan? time) => time is null || time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);
    }

    private bool IsNewDirectoryLocation()
    {
        // Walk to the nearest existing ancestor. A file blocking any parent, an
        // unavailable drive or an access error is not a legitimate first run.
        var directory = Path.GetDirectoryName(settingsPath);
        while (directory is not null)
        {
            try { return (File.GetAttributes(directory) & FileAttributes.Directory) != 0; }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            directory = Path.GetDirectoryName(directory);
        }
        return false;
    }

    public async Task SaveAsync(PersistedSettings settings, CancellationToken cancellationToken = default)
    {
        if (dataRepository is not null) { await dataRepository.SaveAsync(settings, cancellationToken).ConfigureAwait(false); return; }
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            if (loadFailure is not null) throw new SettingsLoadException("設定尚未成功重新讀取，禁止覆寫既有設定。", loadFailure);
            var directory = Path.GetDirectoryName(settingsPath)!;
            Directory.CreateDirectory(directory);
            // Each writer owns its temporary file. Cross-process commits are serialized below;
            // each save is a complete snapshot, with the last completed commit winning.
            temporary = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, settings with
                {
                    Version = PersistedSettings.CurrentVersion,
                    MarginRules = settings.MarginRules.Select(rule => rule.Validate()).ToList()
                }, Options, cancellationToken).ConfigureAwait(false);
            }

            await using var commitLock = await AcquireCommitLockAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, settingsPath, true);
        }
        finally
        {
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
            finally { writeGate.Release(); }
        }
    }

    private async Task<FileStream> AcquireCommitLockAsync(CancellationToken cancellationToken)
    {
        // An OS-owned sharing lock also works across processes and is released on a crash.
        // Keep the empty lock file: deleting it would introduce an unlink/reopen race.
        var deadline = Environment.TickCount64 + 5000;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(settingsPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33 && Environment.TickCount64 < deadline)
            { await Task.Delay(25, cancellationToken).ConfigureAwait(false); }
        }
    }
}
