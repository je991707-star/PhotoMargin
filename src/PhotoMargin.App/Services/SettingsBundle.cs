using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using PhotoMargin.Core.Models;

namespace PhotoMargin.App.Services;

internal sealed record BundleManifest(int SchemaVersion, int SettingsSchemaVersion, int PresetsSchemaVersion,
    string ApplicationVersion, DateTimeOffset ExportedAtUtc, string SettingsSha256, string PresetsSha256);

public sealed class SettingsBundleCandidate
{
    private readonly byte[] settings, presets;
    internal SettingsBundleCandidate(byte[] settings, byte[] presets) { this.settings = settings.ToArray(); this.presets = presets.ToArray(); }
    internal (byte[], byte[]) Snapshot() => (settings.ToArray(), presets.ToArray());
}

public sealed partial class DataRepository
{
    public Task ExportBundleAsync(string destination, CancellationToken token = default) => Task.Run(() => ExportBundleCoreAsync(destination, token), token);
    private async Task ExportBundleCoreAsync(string destination, CancellationToken token)
    {
        byte[] settings, presets;
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var processLock = await LockAsync(token).ConfigureAwait(false); RecoverTransaction();
            var snapshot = ReadPair(DirectoryPath).Settings;
            var portable = Portable(snapshot); var id = Guid.NewGuid().ToString("N");
            settings = SettingsBytes(portable, id); presets = PresetsBytes(portable, id);
        }
        finally { Gate.Release(); }
        var manifest = new BundleManifest(1, SchemaVersion, SchemaVersion,
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0", DateTimeOffset.UtcNow,
            Convert.ToHexString(SHA256.HashData(settings)), Convert.ToHexString(SHA256.HashData(presets)));
        destination = Path.GetFullPath(destination);
        ProtectDataTarget(destination);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await Task.Run(() =>
            {
                using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    foreach (var (name, data) in new[] { ("manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, Json)), ("settings.json", settings), ("presets.json", presets) })
                    { token.ThrowIfCancellationRequested(); using var entry = archive.CreateEntry(name, CompressionLevel.Optimal).Open(); entry.Write(data); }
                }
                stream.Flush(true);
            }, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<SettingsBundleCandidate> ReadBundleAsync(string path, CancellationToken token = default) =>
        await Task.Run(() =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("設定集過大。");
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var allowed = new[] { "manifest.json", "settings.json", "presets.json" };
            if (archive.Entries.Count != 3 || archive.Entries.Any(entry => !allowed.Contains(entry.FullName, StringComparer.Ordinal)) ||
                archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count() != 3 ||
                archive.Entries.Any(entry => entry.Length > 4 * 1024 * 1024) || archive.Entries.Sum(entry => entry.Length) > 8 * 1024 * 1024)
                throw new InvalidDataException("設定集含非預期檔案、重複項目或超出大小限制。");
            byte[] Read(string name)
            {
                var entry = archive.GetEntry(name)!;
                if (entry.Length > 4 * 1024 * 1024) throw new InvalidDataException("設定集檔案過大。");
                using var source = entry.Open(); using var memory = new MemoryStream(); var buffer = new byte[16384];
                int count;
                while ((count = source.Read(buffer)) > 0)
                { token.ThrowIfCancellationRequested(); if (memory.Length + count > 4 * 1024 * 1024) throw new InvalidDataException("設定集解壓大小超出限制。"); memory.Write(buffer, 0, count); }
                return memory.ToArray();
            }
            var manifest = JsonSerializer.Deserialize<BundleManifest>(Read("manifest.json"), Json) ?? throw new InvalidDataException("缺少 manifest。");
            if (manifest.SchemaVersion != 1 || manifest.SettingsSchemaVersion != SchemaVersion || manifest.PresetsSchemaVersion != SchemaVersion)
                throw new UnsupportedDataVersionException("不支援此設定集格式版本。");
            var settings = Read("settings.json"); var presets = Read("presets.json");
            if (manifest.SettingsSha256 != Convert.ToHexString(SHA256.HashData(settings)) || manifest.PresetsSha256 != Convert.ToHexString(SHA256.HashData(presets)))
                throw new InvalidDataException("設定集內容驗證失敗。");
            var pair = DecodePair(settings, presets);
            if (pair.Settings.WorkInformation is not null || pair.Settings.WorkLayout is not null || pair.Settings.LastOutputDirectory is not null || pair.Settings.Export?.Directory is not null ||
                Portable(pair.Settings).Layout.FontFamily != pair.Settings.Layout.FontFamily ||
                Portable(pair.Settings).Export!.FileNamePattern != pair.Settings.Export?.FileNamePattern)
                throw new InvalidDataException("設定集不得包含作品紀錄或本機路徑。");
            return new SettingsBundleCandidate(settings, presets);
        }, token).ConfigureAwait(false);

    public Task<PersistedSettings> ImportBundleAsync(SettingsBundleCandidate candidate,
        Func<PersistedSettings, PersistedSettings, PersistedSettings>? preserveWork = null, CancellationToken token = default) =>
        Task.Run(() => ImportBundleCoreAsync(candidate, preserveWork, token), token);
    private async Task<PersistedSettings> ImportBundleCoreAsync(SettingsBundleCandidate candidate,
        Func<PersistedSettings, PersistedSettings, PersistedSettings>? preserveWork, CancellationToken token)
    {
        var (settings, presets) = candidate.Snapshot(); var incoming = DecodePair(settings, presets).Settings;
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (blocked) throw new SettingsLoadException("Data 尚未成功讀取，禁止匯入。");
            using var processLock = await LockAsync(token).ConfigureAwait(false); RecoverTransaction();
            var local = ReadPair(DirectoryPath).Settings;
            var result = incoming with
            {
                LastOutputDirectory = local.LastOutputDirectory,
                Export = (incoming.Export ?? new()) with { Directory = local.Export?.Directory ?? local.LastOutputDirectory },
                WorkInformation = local.WorkInformation, WorkLayout = local.WorkLayout
            };
            if (preserveWork is not null) result = preserveWork(local, result);
            await CommitAsync(result, token, true).ConfigureAwait(false);
            return result;
        }
        finally { Gate.Release(); }
    }
    private static PersistedSettings Portable(PersistedSettings snapshot)
    {
        var font = snapshot.Layout.FontFamily;
        if (Path.IsPathRooted(font) || font.Contains(':') || font.Contains('\\') || font.Contains('/')) font = "Microsoft JhengHei UI";
        var export = snapshot.Export ?? new ExportPreferences { JpegQuality = snapshot.Layout.JpegQuality };
        var pattern = new string(export.FileNamePattern.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray());
        return snapshot with { WorkInformation = null, WorkLayout = null, LastOutputDirectory = null,
            Layout = snapshot.Layout with { FontFamily = font }, Export = export with { Directory = null, FileNamePattern = pattern } };
    }
    private void ProtectDataTarget(string destination)
    {
        var root = Path.GetFullPath(DirectoryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (destination.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("請將設定集另存為 Data 資料夾以外的 ZIP。");
    }
}
