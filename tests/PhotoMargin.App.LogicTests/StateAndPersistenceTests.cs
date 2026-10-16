using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using ImageMagick;
using PhotoMargin.App.Services;
using PhotoMargin.App.ViewModels;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;
using PhotoMargin.Imaging;

namespace PhotoMargin.App.LogicTests;

// Exercise the actual production ViewModels on a serialized UI-like context,
// without introducing a dependency on a WinUI window or changing production code.
public sealed class StateAndPersistenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PhotoMargin.LogicTests", Guid.NewGuid().ToString("N"));
    public StateAndPersistenceTests() => Directory.CreateDirectory(root);
    private MainViewModel Create(string? settings = null) => new(new ImageLoader(new ExifReader()), new LayoutEngine(new MagickTextMeasurer()),
        new PreviewService(new ImageRenderer()), new ExportService(new ImageRenderer()), new SettingsStore(settings ?? Path.Combine(root, "settings.json")));
    private string Source(bool withExif = false)
    {
        var path = Path.Combine(root, "source.jpg");
        using var image = new MagickImage(MagickColors.CornflowerBlue, 900, 600);
        if (withExif)
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.FocalLength, new Rational(355, 10)); exif.SetValue(ExifTag.FNumber, new Rational(28, 10));
            exif.SetValue(ExifTag.ExposureTime, new Rational(13, 10)); image.SetProfile(exif);
        }
        image.Write(path); return path;
    }
    private string BlockedStore()
    {
        var blocker = Path.Combine(root, "blocked"); File.WriteAllText(blocker, "Test-owned blocker.");
        return Path.Combine(blocker, "settings.json");
    }

    [Fact]
    public void RecoveringPreviewClearsOnlyItsOwnFailure() => Ui.Run(async () =>
    {
        using var vm = Create(); await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source()); await Until(() => vm.CanExport);
        vm.Arrangement = InformationArrangement.TwoColumns; vm.Lens = new string('長', 100);
        await Until(() => vm.ErrorText.Contains("安全檢查失敗", StringComparison.Ordinal)); Assert.False(vm.CanExport);
        vm.Lens = "Short lens"; await Until(() => vm.CanExport);
        Assert.Empty(vm.ErrorText); Assert.DoesNotContain("安全檢查失敗", vm.PreviewCaption);
        await vm.FlushSettingsAsync();
    });

    [Fact]
    public void PreviewCannotEraseSaveFailureAndSuccessfulRetryClearsIt() => Ui.Run(async () =>
    {
        var path = BlockedStore(); using var vm = Create(path); await vm.InitializeAsync();
        await vm.LoadPhotoAsync(Source()); await Until(() => vm.CanExport);
        vm.JpegQuality = 91; await Until(() => vm.CanExport && vm.PersistenceError.Length > 0);
        Assert.Contains("無法儲存設定", vm.ErrorText);
        vm.Lens = "Another lens"; await Until(() => vm.CanExport);
        Assert.Contains("無法儲存設定", vm.ErrorText);
        File.Delete(Path.GetDirectoryName(path)!); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await vm.FlushSettingsAsync(); Assert.Empty(vm.PersistenceError);
        Assert.Equal(91, (await new SettingsStore(path).LoadAsync()).Layout.JpegQuality);
    });

    [Fact]
    public void FailedRuleSaveRetainsDraftAndRetryDoesNotDuplicate() => Ui.Run(async () =>
    {
        var path = BlockedStore(); using var vm = Create(path); await vm.InitializeAsync();
        vm.RuleEditor.New(); vm.RuleEditor.Name = "Retry rule"; vm.RuleEditor.Side = 65;
        Assert.False(await vm.RuleEditor.SaveAsync()); Assert.Contains("尚未儲存", vm.RuleEditor.Message);
        Assert.Equal("Retry rule", vm.RuleEditor.Name); Assert.Equal(65, vm.RuleEditor.Side);
        File.Delete(Path.GetDirectoryName(path)!); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Assert.True(await vm.RuleEditor.SaveAsync()); Assert.Equal("規則已儲存。", vm.RuleEditor.Message);
        Assert.Single((await new SettingsStore(path).LoadAsync()).MarginRules, rule => rule.Name == "Retry rule");
    });

    [Fact]
    public void FailedDeleteRestoresRuleAndCanBeRetried() => Ui.Run(async () =>
    {
        var path = BlockedStore(); using var vm = Create(path); await vm.InitializeAsync();
        vm.RuleEditor.New(); vm.RuleEditor.Name = "Delete rule"; Assert.False(await vm.RuleEditor.SaveAsync());
        var id = vm.RuleEditor.SelectedRuleId;
        await vm.RuleEditor.DeleteAsync(); Assert.Contains("未刪除", vm.RuleEditor.Message);
        Assert.Contains(vm.MarginRules, rule => rule.Id == id); Assert.True(vm.RuleEditor.CanDelete);
        File.Delete(Path.GetDirectoryName(path)!); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await vm.RuleEditor.DeleteAsync(); Assert.Equal("規則已刪除。", vm.RuleEditor.Message);
        Assert.DoesNotContain((await new SettingsStore(path).LoadAsync()).MarginRules, rule => rule.Id == id);
    });

    [Fact]
    public void WrittenImageIsSuccessfulEvenIfPreferencesCannotBeSaved() => Ui.Run(async () =>
    {
        using var vm = Create(BlockedStore()); await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source()); await Until(() => vm.CanExport);
        var output = Path.Combine(root, "exported.jpg"); await vm.ExportAsync(output);
        using var image = new MagickImage(output); Assert.Equal(1080u, image.Width); Assert.Equal(1080u, image.Height);
        Assert.StartsWith("已匯出：", vm.StatusText); Assert.Contains("無法儲存設定", vm.PersistenceError);
    });

    [Fact]
    public void RenderingDisposalDoesNotCancelQueuedPreferences() => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "close.json"); var store = new SettingsStore(path); await store.SaveAsync(new PersistedSettings());
        using var vm = new MainViewModel(new ImageLoader(new ExifReader()), new LayoutEngine(new MagickTextMeasurer()),
            new PreviewService(new ImageRenderer()), new ExportService(new ImageRenderer()), store);
        await vm.InitializeAsync();
        var gate = (SemaphoreSlim)typeof(SettingsStore).GetField("writeGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        await gate.WaitAsync(); Task flush;
        try { vm.JpegQuality = 82; vm.Arrangement = InformationArrangement.Stacked; flush = vm.FlushSettingsAsync(); vm.Dispose(); }
        finally { gate.Release(); }
        await flush; var saved = await store.LoadAsync(); Assert.Equal(82, saved.Layout.JpegQuality);
        Assert.Equal(InformationArrangement.Stacked, saved.Layout.Arrangement);
    });

    [Fact]
    public void MultipleStoresCommitCompleteSnapshotsWithoutTempCollisions() => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "shared.json");
        var tasks = Enumerable.Range(0, 12).Select(index => new SettingsStore(path).SaveAsync(new PersistedSettings
        {
            Layout = new LayoutSettings { JpegQuality = 80 + index },
            MarginRules = Enumerable.Range(0, 200).Select(i => new MarginRule { Id = i.ToString(CultureInfo.InvariantCulture), Name = "Rule " + i }).ToList()
        }));
        await Task.WhenAll(tasks); var saved = await new SettingsStore(path).LoadAsync();
        Assert.InRange(saved.Layout.JpegQuality, 80, 91); Assert.Equal(200, saved.MarginRules.Count);
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    });

    [Fact]
    public void CancelledSavePreservesPreviousSnapshotAndCleansTemp() => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "cancelled.json"); var store = new SettingsStore(path); await store.SaveAsync(new PersistedSettings());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new PersistedSettings { Layout = new LayoutSettings { JpegQuality = 82 } }, cancellation.Token));
        Assert.Equal(95, (await store.LoadAsync()).Layout.JpegQuality); Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    });

    [Fact]
    public void DecimalCommaExifRemainsNumericAndEditable() => Ui.Run(async () =>
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            using var vm = Create(); await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source(true)); await Until(() => vm.CanExport);
            Assert.Equal(35.5, vm.FocalLengthValue); Assert.Equal(2.8, vm.ApertureValue); Assert.Equal("1.3", vm.ShutterInput);
            vm.ShutterInput = "1.3"; Assert.Empty(vm.InputError); vm.FocalLengthValue = 52.5;
            Assert.Equal("52.5mm", vm.FocalLength); await Until(() => vm.CanExport);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    });

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 600; i++) { if (condition()) return; await Task.Delay(25); }
        throw new TimeoutException("UI-like regression did not reach the expected state.");
    }
    public void Dispose() => Directory.Delete(root, true);

    private sealed class Ui : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => queue.Add((callback, state));
        public static void Run(Func<Task> action)
        {
            var previous = Current; using var ui = new Ui(); SetSynchronizationContext(ui);
            try
            {
                var task = action();
                while (!task.IsCompleted) if (ui.queue.TryTake(out var item, 100)) item.Callback(item.State);
                task.GetAwaiter().GetResult();
            }
            finally { SetSynchronizationContext(previous); }
        }
        public void Dispose() => queue.Dispose();
    }
}
