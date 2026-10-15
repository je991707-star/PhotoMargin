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

    internal SettingsStore(string settingsPath) => this.settingsPath = settingsPath;

    public async Task<PersistedSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return new PersistedSettings();
            }

            await using var stream = File.OpenRead(settingsPath);
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
        try
        {
            var directory = Path.GetDirectoryName(settingsPath)!;
            Directory.CreateDirectory(directory);
            var temporary = settingsPath + ".tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, settingsPath, true);
        }
        finally
        {
            writeGate.Release();
        }
    }
}
