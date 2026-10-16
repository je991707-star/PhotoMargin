using System.Text.Json;
using PhotoMargin.Core.Models;

namespace PhotoMargin.App.Services;

public enum ThemePreference
{
    System,
    Dark,
    Light
}

public sealed record PersistedSettings
{
    public const int CurrentVersion = 3;
    public int Version { get; init; } = CurrentVersion;
    public LayoutSettings Layout { get; init; } = new();
    public bool ShowGuides { get; init; } = true;
    public ThemePreference Theme { get; init; } = ThemePreference.System;
    public string? LastOutputDirectory { get; init; }
    public List<MarginRule> MarginRules { get; init; } = [];
    public string SelectedMarginRuleId { get; init; } = MarginRule.DefaultId;
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

    public async Task<PersistedSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return new PersistedSettings();
            }

            // A reader may finish reading the previous snapshot while another process replaces it.
            await using var stream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var value = await JsonSerializer.DeserializeAsync<PersistedSettings>(stream, Options, cancellationToken);
            if (value is null || value.Version is not (2 or PersistedSettings.CurrentVersion))
            {
                return new PersistedSettings();
            }

            var layout = (value.Layout ?? new LayoutSettings()).Validate();
            return value with
            {
                Version = PersistedSettings.CurrentVersion,
                Layout = layout with { Visibility = layout.Visibility ?? new FieldVisibility() },
                MarginRules = (value.MarginRules ?? []).Where(rule => rule is not null && !string.IsNullOrWhiteSpace(rule.Id))
                    .DistinctBy(rule => rule.Id).Select(rule => rule.Validate()).ToList(),
                Theme = Enum.IsDefined(value.Theme) ? value.Theme : ThemePreference.System
            };
        }
        catch (JsonException)
        {
            return new PersistedSettings();
        }
        catch (IOException)
        {
            return new PersistedSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new PersistedSettings();
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
                await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken).ConfigureAwait(false);
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
