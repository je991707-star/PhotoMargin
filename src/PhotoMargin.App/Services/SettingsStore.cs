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

public sealed record PersistedSettings
{
    public const int CurrentVersion = 5;
    public int Version { get; init; } = CurrentVersion;
    public LayoutSettings Layout { get; init; } = new();
    public bool ShowGuides { get; init; } = true;
    public ThemePreference Theme { get; init; } = ThemePreference.System;
    public string? LastOutputDirectory { get; init; }
    public List<MarginRule> MarginRules { get; init; } = [];
    public string? SelectedMarginRuleId { get; init; } = MarginRule.DefaultId;
    public SourceAspect DemonstrationAspect { get; init; } = SourceAspect.FourByThree;
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string settingsPath;
    private readonly SemaphoreSlim writeGate = new(1, 1);

    public SettingsStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoMargin",
        "settings.json"))
    {
    }

    internal SettingsStore(string settingsPath) => this.settingsPath = Path.GetFullPath(settingsPath);
    private static PersistedSettings InitialSettings() => new() { MarginRules = [new MarginRule()] };

    public async Task<PersistedSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return InitialSettings();
            }

            // A reader may finish reading the previous snapshot while another process replaces it.
            await using var stream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var value = await JsonSerializer.DeserializeAsync<PersistedSettings>(stream, Options, cancellationToken);
            if (value is null || value.Version is not (2 or 3 or 4 or PersistedSettings.CurrentVersion))
            {
                return InitialSettings();
            }

            var layout = (value.Layout ?? new LayoutSettings()).Validate();
            var rules = (value.MarginRules ?? []).Where(rule => rule is not null && !string.IsNullOrWhiteSpace(rule.Id))
                .DistinctBy(rule => rule.Id).Select(rule => rule.Validate() with { }).ToList();
            // Only legacy documents need a built-in seed. A v4 empty list is deliberate.
            if (value.Version < 4 && !rules.Any(rule => rule.Id == MarginRule.DefaultId)) rules.Insert(0, MarginRule.FromLayout(layout).Validate());
            return value with
            {
                Version = PersistedSettings.CurrentVersion,
                Layout = layout with { Visibility = layout.Visibility ?? new FieldVisibility() },
                MarginRules = rules,
                DemonstrationAspect = Enum.IsDefined(value.DemonstrationAspect) ? value.DemonstrationAspect : SourceAspect.FourByThree,
                Theme = Enum.IsDefined(value.Theme) ? value.Theme : ThemePreference.System
            };
        }
        catch (JsonException)
        {
            return InitialSettings();
        }
        catch (IOException)
        {
            return InitialSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return InitialSettings();
        }
    }

    public async Task SaveAsync(PersistedSettings settings, CancellationToken cancellationToken = default)
    {
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporary = null;
        try
        {
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
