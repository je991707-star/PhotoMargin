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
    public const int CurrentVersion = 8;
    public int Version { get; init; } = CurrentVersion;
    public LayoutSettings Layout { get; init; } = new();
    public bool ShowGuides { get; init; } = true;
    public ThemePreference Theme { get; init; } = ThemePreference.System;
    public string? LastOutputDirectory { get; init; }
    public List<MarginRule> MarginRules { get; init; } = [];
    public string? SelectedMarginRuleId { get; init; } = MarginRule.DefaultId;
    public SourceAspect DemonstrationAspect { get; init; } = SourceAspect.FourByThree;
    public InformationArrangement? DemonstrationArrangement { get; init; }
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string settingsPath;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private SettingsLoadException? loadFailure;

    public SettingsStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoMargin",
        "settings.json"))
    {
    }

    internal SettingsStore(string settingsPath) => this.settingsPath = Path.GetFullPath(settingsPath);
    private static PersistedSettings InitialSettings()
    {
        var builtIn = BuiltInMarginPresets.Create();
        return new() { MarginRules = [builtIn], Layout = builtIn.ForAspect(SourceAspect.FourByThree).ApplyTo(new()) };
    }

    public async Task<PersistedSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A reader may finish reading the previous snapshot while another process replaces it.
            await using var stream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var value = await JsonSerializer.DeserializeAsync<PersistedSettings>(stream, Options, cancellationToken).ConfigureAwait(false);
            if (value is null || value.Version is not (2 or 3 or 4 or 5 or 6 or 7 or PersistedSettings.CurrentVersion))
            {
                throw new InvalidDataException("設定內容為空或格式版本不受支援。");
            }

            var layout = (value.Layout ?? new LayoutSettings()).Validate();
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
            if (oldBuiltIn is not null)
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
            var loaded = value with
            {
                Version = PersistedSettings.CurrentVersion,
                Layout = layout with { Visibility = layout.Visibility ?? new FieldVisibility() },
                MarginRules = rules,
                DemonstrationAspect = Enum.IsDefined(value.DemonstrationAspect) ? value.DemonstrationAspect : SourceAspect.FourByThree,
                DemonstrationArrangement = (value.DemonstrationArrangement ?? layout.Arrangement) == InformationArrangement.Stacked
                    ? InformationArrangement.Stacked : InformationArrangement.TwoColumns,
                Theme = Enum.IsDefined(value.Theme) ? value.Theme : ThemePreference.System
            };
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
