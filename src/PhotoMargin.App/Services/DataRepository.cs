using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Globalization;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;

namespace PhotoMargin.App.Services;

internal sealed record SettingsDocument(int SchemaVersion, string TransactionId, PersistedSettings Settings);
internal sealed record PresetsDocument(int SchemaVersion, string TransactionId, List<MarginRule> Presets, List<string> DeletedBuiltInIds);
internal sealed record DataPair(PersistedSettings Settings, List<string> DeletedBuiltInIds);
internal sealed record DataTransaction(string Id, bool HadSettings, bool HadPresets);

public sealed partial class DataRepository(DataLocation location)
{
    public const int SchemaVersion = 1;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private SemaphoreSlim Gate => Gates.GetOrAdd(Path.GetFullPath(location.DirectoryPath), _ => new(1, 1));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal Action<string>? Fault { get; set; }
    public string DirectoryPath => location.DirectoryPath;
    public string RecoveryNotice { get; private set; } = "";
    private bool blocked;
    private string JournalPath => Path.Combine(DirectoryPath, ".transaction.json");
    private string StagePath(string id) => Path.Combine(DirectoryPath, ".transaction-" + id);

    public Task<PersistedSettings> LoadAsync(CancellationToken token = default) => Task.Run(() => LoadCoreAsync(token), token);
    private async Task<PersistedSettings> LoadCoreAsync(CancellationToken token)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            using var processLock = await LockAsync(token).ConfigureAwait(false);
            RecoverTransaction();
            if (!File.Exists(location.SettingsPath) && !File.Exists(location.PresetsPath) &&
                (!Directory.Exists(location.BackupsPath) || !Directory.GetDirectories(location.BackupsPath).Any()))
            {
                var initial = location.LegacySettingsPath is { } legacy && File.Exists(legacy)
                    ? SettingsStore.ReadLegacyForData(legacy) : SettingsStore.InitialSettings();
                await CommitAsync(initial, token).ConfigureAwait(false);
                RecoveryNotice = location.LegacySettingsPath is { } old && File.Exists(old) ? "已遷移舊資料，原檔仍保留。" : "";
            }
            Directory.CreateDirectory(location.BackupsPath);
            DataPair pair;
            try { pair = ReadPair(DirectoryPath); }
            catch (Exception error) when (error is JsonException or InvalidDataException or IOException)
            {
                // A newer schema is not corruption; do not downgrade it from a backup.
                if (error is UnsupportedDataVersionException) throw;
                pair = ReadValidBackup() ?? throw new InvalidDataException("資料損壞，找不到有效備份；已保留原檔並停止保存。", error);
                await CommitAsync(pair.Settings, token).ConfigureAwait(false);
                RecoveryNotice = "已從有效備份復原；原始資料保留於 Backups。";
            }
            blocked = false;
            return pair.Settings;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { blocked = true; throw new SettingsLoadException("無法讀取 Data，已停止保存以保護原資料。\n" + error.Message, error); }
        finally { Gate.Release(); }
    }

    public Task SaveAsync(PersistedSettings value, CancellationToken token = default) => Task.Run(() => SaveCoreAsync(value, token), token);
    private async Task SaveCoreAsync(PersistedSettings value, CancellationToken token)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (blocked) throw new SettingsLoadException("Data 尚未成功讀取，禁止覆寫。");
            Directory.CreateDirectory(DirectoryPath);
            using var processLock = await LockAsync(token).ConfigureAwait(false);
            RecoverTransaction(); _ = ReadPair(DirectoryPath);
            await CommitAsync(value, token).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    private static readonly string[] PairFiles = ["settings.json", "presets.json"];
    private static byte[] SettingsBytes(PersistedSettings value, string id)
    {
        var node = JsonSerializer.SerializeToNode(new SettingsDocument(SchemaVersion, id, value), Json)!.AsObject();
        node["settings"]!.AsObject().Remove("marginRules");
        return JsonSerializer.SerializeToUtf8Bytes(node, Json);
    }
    private static byte[] PresetsBytes(PersistedSettings value, string id) => JsonSerializer.SerializeToUtf8Bytes(
        new PresetsDocument(SchemaVersion, id, value.MarginRules.Select(rule => rule.Validate()).ToList(),
            value.MarginRules.Any(rule => rule.Id == MarginRule.DefaultId) ? [] : [MarginRule.DefaultId]), Json);

    private static DataPair DecodePair(byte[] settings, byte[] presets)
    {
        var first = JsonSerializer.Deserialize<SettingsDocument>(settings, Json) ?? throw new InvalidDataException("settings.json 為空。");
        var second = JsonSerializer.Deserialize<PresetsDocument>(presets, Json) ?? throw new InvalidDataException("presets.json 為空。");
        if (first.SchemaVersion > SchemaVersion || second.SchemaVersion > SchemaVersion) throw new UnsupportedDataVersionException("不支援較新的資料格式。");
        if (first.SchemaVersion != SchemaVersion || second.SchemaVersion != SchemaVersion ||
            !Guid.TryParseExact(first.TransactionId, "N", out _) || first.TransactionId != second.TransactionId)
            throw new InvalidDataException("設定與預設不是同一筆更新。");
        var value = first.Settings ?? throw new InvalidDataException("缺少設定。");
        if (value.Version is < 2 or > PersistedSettings.CurrentVersion) throw new UnsupportedDataVersionException("不支援此設定格式版本。");
        if (second.Presets is null || second.DeletedBuiltInIds is null || second.DeletedBuiltInIds.Any(id => id != MarginRule.DefaultId) ||
            second.DeletedBuiltInIds.Distinct().Count() != second.DeletedBuiltInIds.Count ||
            second.Presets.Any(rule => rule is null || string.IsNullOrWhiteSpace(rule.Id) || rule.Id.Length > 128 || rule.Id.IndexOfAny(['/', '\\', ':']) >= 0) ||
            second.Presets.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != second.Presets.Count ||
            second.Presets.Any(rule => second.DeletedBuiltInIds.Contains(rule.Id)))
            throw new InvalidDataException("預設 ID 或刪除狀態無效。");
        value = value with { MarginRules = second.Presets.Select(rule => rule.Validate()).ToList() };
        if (value.Version < PersistedSettings.CurrentVersion)
        {
            value = SettingsStore.NormalizeLegacy(value, false);
            if (second.DeletedBuiltInIds.Contains(MarginRule.DefaultId)) value = value with { MarginRules = value.MarginRules.Where(rule => rule.Id != MarginRule.DefaultId).ToList() };
        }
        else
        {
            foreach (var rule in second.Presets) ValidateRule(rule);
        }
        ValidateSettings(value);
        if (value.SelectedMarginRuleId is { } selected && !value.MarginRules.Any(rule => rule.Id == selected))
            value = value with { SelectedMarginRuleId = value.MarginRules.FirstOrDefault()?.Id };
        return new(value, second.DeletedBuiltInIds);
    }
    private static void ValidateSettings(PersistedSettings value)
    {
        if (value.Layout is null || !ValidLayout(value.Layout) || !Enum.IsDefined(value.Theme) ||
            !Enum.IsDefined(value.DemonstrationAspect) || value.DemonstrationArrangement is { } arrangement && !Enum.IsDefined(arrangement) ||
            value.Export is { } export && export.Validate() != export || value.WorkLayout is { } work && !ValidLayout(work))
            throw new InvalidDataException("設定數值超出有效範圍。");
        SettingsStore.ValidateWorkInformation(value.WorkInformation);
    }
    private static bool ValidLayout(LayoutSettings layout) => layout.Validate() == layout && !string.IsNullOrWhiteSpace(layout.FontFamily) && layout.FontFamily.Length <= 512 &&
        layout.Visibility is not null && new[] { layout.PhotoSideMargin, layout.PhotoTopMargin, layout.PhotoInfoGap, layout.ExposureItemGap,
            layout.BottomSafeDistance, layout.CropExtraPadding, layout.PrimaryFontSize, layout.SecondaryFontSize, layout.LensFontSize,
            layout.DateFontSize, layout.MinimumFontSize }.All(double.IsFinite);
    private static void ValidateRule(MarginRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 512 || rule.ValidateValues() != rule || rule.Arrangements is null)
            throw new InvalidDataException("預設名稱或版面數值無效。");
        foreach (var aspect in Enum.GetValues<SourceAspect>())
        foreach (var arrangement in new[] { InformationArrangement.TwoColumns, InformationArrangement.Stacked })
        {
            var configuration = rule.Arrangements.Get(aspect)?.Get(arrangement) ?? throw new InvalidDataException("缺少比例／排列配置。");
            if (configuration.ToRule().ValidateValues() != configuration.ToRule()) throw new InvalidDataException("版面配置超出有效範圍。");
        }
    }
    private static DataPair ReadPair(string root) => DecodePair(ReadLimited(Path.Combine(root, "settings.json")), ReadLimited(Path.Combine(root, "presets.json")));
    internal static byte[] ReadLimited(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("資料檔過大。");
        using var memory = new MemoryStream(); var buffer = new byte[16384]; int count;
        while ((count = stream.Read(buffer)) > 0)
        { if (memory.Length + count > 4 * 1024 * 1024) throw new InvalidDataException("資料檔過大。"); memory.Write(buffer, 0, count); }
        return memory.ToArray();
    }
    private DataPair? ReadValidBackup()
    {
        if (!Directory.Exists(location.BackupsPath)) return null;
        foreach (var backup in Directory.GetDirectories(location.BackupsPath).Where(IsBackupGroup).OrderDescending(StringComparer.Ordinal))
        {
            try { return ReadPair(backup); }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return null;
    }

    private async Task CommitAsync(PersistedSettings value, CancellationToken token, bool force = false)
    {
        // A burst of UI edits can queue multiple saves of the same final state.
        // Skip identical snapshots so they do not churn files or evict backups.
        try
        {
            var current = ReadPair(DirectoryPath).Settings; var comparisonId = Guid.Empty.ToString("N");
            if (!force && SettingsBytes(current, comparisonId).AsSpan().SequenceEqual(SettingsBytes(value, comparisonId)) &&
                PresetsBytes(current, comparisonId).AsSpan().SequenceEqual(PresetsBytes(value, comparisonId))) return;
        }
        catch (UnsupportedDataVersionException) { throw; }
        catch (Exception error) when (error is IOException or JsonException) { } // initialization or explicit backup recovery
        var id = Guid.NewGuid().ToString("N");
        var settings = SettingsBytes(value, id); var presets = PresetsBytes(value, id);
        _ = DecodePair(settings, presets); // validate the complete pair before touching saved data
        var stage = StagePath(id); Directory.CreateDirectory(stage);
        var transaction = new DataTransaction(id, File.Exists(location.SettingsPath), File.Exists(location.PresetsPath));
        var hadPrevious = transaction.HadSettings || transaction.HadPresets;
        var journalPublished = false;
        try
        {
            WriteDurable(Path.Combine(stage, "new-settings.json"), settings);
            WriteDurable(Path.Combine(stage, "new-presets.json"), presets);
            foreach (var file in PairFiles)
                if (File.Exists(Path.Combine(DirectoryPath, file))) File.Copy(Path.Combine(DirectoryPath, file), Path.Combine(stage, "old-" + file));
            token.ThrowIfCancellationRequested();
            if (hadPrevious) BackupCurrent();
            Fault?.Invoke("after-backup"); token.ThrowIfCancellationRequested();
            AtomicWrite(JournalPath, JsonSerializer.SerializeToUtf8Bytes(transaction, Json)); journalPublished = true;
            File.Move(Path.Combine(stage, "new-settings.json"), location.SettingsPath, true);
            Fault?.Invoke("after-settings");
            // Once the journal is published cancellation cannot leave a half commit.
            File.Move(Path.Combine(stage, "new-presets.json"), location.PresetsPath, true);
            Fault?.Invoke("after-presets");
            File.Delete(JournalPath); journalPublished = false;
            PruneBackups();
        }
        catch
        {
            if (journalPublished) { Rollback(transaction); journalPublished = false; }
            throw;
        }
        finally { if (!journalPublished) CleanStage(stage); }
        await Task.CompletedTask;
    }
    private void RecoverTransaction()
    {
        if (!File.Exists(JournalPath)) return;
        var transaction = JsonSerializer.Deserialize<DataTransaction>(ReadLimited(JournalPath), Json);
        if (transaction is null || !Guid.TryParseExact(transaction.Id, "N", out _)) throw new InvalidDataException("資料復原記錄無效。");
        var completed = false;
        try
        {
            using var first = JsonDocument.Parse(ReadLimited(location.SettingsPath));
            using var second = JsonDocument.Parse(ReadLimited(location.PresetsPath));
            completed = first.RootElement.GetProperty("transactionId").GetString() == transaction.Id && second.RootElement.GetProperty("transactionId").GetString() == transaction.Id;
            if (completed) _ = ReadPair(DirectoryPath);
        }
        catch (UnsupportedDataVersionException) { throw; }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or KeyNotFoundException) { completed = false; }
        if (!completed) Rollback(transaction);
        else { File.Delete(JournalPath); CleanStage(StagePath(transaction.Id)); }
    }
    private void Rollback(DataTransaction transaction)
    {
        var stage = StagePath(transaction.Id);
        foreach (var file in PairFiles)
        {
            var original = Path.Combine(stage, "old-" + file); var current = Path.Combine(DirectoryPath, file);
            if (File.Exists(original)) AtomicWrite(current, File.ReadAllBytes(original));
            else if (file == "settings.json" ? transaction.HadSettings : transaction.HadPresets)
                throw new IOException("無法完整復原舊資料，已保留交易記錄；請勿覆寫 Data。");
            else if (File.Exists(current)) File.Delete(current);
        }
        File.Delete(JournalPath); CleanStage(stage);
    }
    private void BackupCurrent()
    {
        Directory.CreateDirectory(location.BackupsPath);
        var root = Path.Combine(location.BackupsPath, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(location.BackupsPath, ".backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in PairFiles) if (File.Exists(Path.Combine(DirectoryPath, file))) File.Copy(Path.Combine(DirectoryPath, file), Path.Combine(staging, file));
            Fault?.Invoke("backup-created");
            Directory.Move(staging, root); // publish the whole group only after every copy succeeds
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                foreach (var file in PairFiles) if (File.Exists(Path.Combine(staging, file))) File.Delete(Path.Combine(staging, file));
                Directory.Delete(staging);
            }
        }
    }
    private void PruneBackups()
    {
        try
        {
        if (!Directory.Exists(location.BackupsPath)) return;
        foreach (var directory in Directory.GetDirectories(location.BackupsPath).Where(IsBackupGroup).OrderDescending(StringComparer.Ordinal).Skip(5))
        {
            // Delete only our exact backup files; never recursively delete unknown user data.
            if (Directory.GetDirectories(directory).Length != 0 || Directory.GetFiles(directory).Any(file => !PairFiles.Contains(Path.GetFileName(file)))) continue;
            try { foreach (var file in PairFiles) File.Delete(Path.Combine(directory, file)); Directory.Delete(directory); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private static bool IsBackupGroup(string directory)
    {
        var name = Path.GetFileName(directory);
        return name.Length == 55 && name[22] == '-' && Guid.TryParseExact(name[23..], "N", out _) &&
            DateTime.TryParseExact(name[..22], "yyyyMMdd'T'HHmmssfffffff", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _) &&
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0;
    }
    private static void CleanStage(string stage)
    {
        try
        {
        foreach (var name in new[] { "new-settings.json", "new-presets.json", "old-settings.json", "old-presets.json" })
            if (File.Exists(Path.Combine(stage, name))) File.Delete(Path.Combine(stage, name));
        if (Directory.Exists(stage)) Directory.Delete(stage);
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private static void WriteDurable(string path, byte[] data)
    { using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); stream.Write(data); stream.Flush(true); }
    private static void AtomicWrite(string destination, byte[] data)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { WriteDurable(temporary, data); File.Move(temporary, destination, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private async Task<FileStream> LockAsync(CancellationToken token)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (true)
        {
            try { return new FileStream(Path.Combine(DirectoryPath, ".data.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 && Environment.TickCount64 < deadline) { await Task.Delay(25, token).ConfigureAwait(false); }
        }
    }
}

public sealed class UnsupportedDataVersionException(string message) : IOException(message);
