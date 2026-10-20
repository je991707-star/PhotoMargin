using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;
using PhotoMargin.App.Services;
using PhotoMargin.App.ViewModels;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;
using PhotoMargin.Imaging;

namespace PhotoMargin.App.LogicTests;

public sealed partial class StateAndPersistenceTests
{
    [Fact]
    public void PerFieldRawSelectionChangesOnlyChosenValuesAndPersistsTheirOrigins() => Ui.Run(async () =>
    {
        var path = Source(true); var settings = Path.Combine(root, "field-selection.json");
        using var vm = RawVm(new RawReader(_ => Task.FromResult(RawExample)), settings);
        await vm.InitializeAsync(); await vm.LoadPhotoAsync(path); vm.Camera = "Manual camera"; vm.IsoValue = 800;
        var photo = PhotoObject(vm); var shutter = vm.Shutter;
        Assert.True(await vm.ImportSelectedRawInformationAsync(FakeRaw, review =>
        {
            Assert.Equal("手動修改", review.Differences.Single(row => row.Field == InformationField.Camera).Source);
            Assert.Equal("JPG", review.Differences.Single(row => row.Field == InformationField.Shutter).Source);
            return Task.FromResult<RawInformationSelection?>(new([InformationField.Lens, InformationField.FocalLength]));
        }));
        Assert.Equal(RawExample.LensModel, vm.Lens); Assert.Equal(31, vm.FocalLengthValue);
        Assert.Equal("Manual camera", vm.Camera); Assert.Equal(800, vm.IsoValue); Assert.Equal(shutter, vm.Shutter);
        Assert.Equal(InformationOrigin.Manual, vm.InformationSource(InformationField.Camera));
        Assert.Equal(InformationOrigin.PhotoExif, vm.InformationSource(InformationField.Shutter));
        Assert.Equal(InformationOrigin.Raw, vm.InformationSource(InformationField.Lens)); Assert.Same(photo, PhotoObject(vm));
        await Until(() => vm.CanExport); await vm.FlushSettingsAsync();
        using var restored = RawVm(new RawReader(_ => throw new IOException("Do not reopen RAW")), settings);
        await restored.InitializeAsync(); await restored.LoadPhotoAsync(path); await Until(() => restored.CanExport);
        Assert.Equal(vm.InformationRows, restored.InformationRows); await restored.FlushSettingsAsync();
    });

    [Fact]
    public void KeepingEveryCurrentValueDoesNotCreateOrRetargetRawAssociation() => Ui.Run(async () =>
    {
        using var vm = RawVm(new RawReader(_ => Task.FromResult(RawExample))); await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source());
        vm.Camera = "Manual"; var before = vm.InformationRows.ToArray();
        Assert.True(await vm.ImportSelectedRawInformationAsync(FakeRaw, _ => Task.FromResult<RawInformationSelection?>(new([]))));
        Assert.Equal(before, vm.InformationRows); Assert.Null(vm.ConfirmedRaw);
        Assert.True(await vm.ImportSelectedRawInformationAsync(FakeRaw, _ => Task.FromResult<RawInformationSelection?>(new([InformationField.Lens]))));
        var originalRaw = vm.ConfirmedRaw;
        Assert.True(await vm.ImportSelectedRawInformationAsync(Path.Combine(root, "other.rw2"), _ => Task.FromResult<RawInformationSelection?>(new([]))));
        Assert.Same(originalRaw, vm.ConfirmedRaw);
        await Until(() => vm.CanExport); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void MissingInvalidCancelledAndStaleFieldChoicesCannotEraseOrOverwriteCurrentData() => Ui.Run(async () =>
    {
        using var vm = RawVm(new RawReader(_ => Task.FromResult(RawExample with { LensModel = null })));
        await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source()); vm.Lens = "Keep lens";
        Assert.True(await vm.ImportSelectedRawInformationAsync(FakeRaw, _ => Task.FromResult<RawInformationSelection?>(new([InformationField.Lens]))));
        Assert.Equal("Keep lens", vm.Lens); Assert.Null(vm.ConfirmedRaw);
        Assert.False(await vm.ImportSelectedRawInformationAsync(FakeRaw, _ => Task.FromResult<RawInformationSelection?>(null)));
        Assert.False(await vm.ImportSelectedRawInformationAsync(FakeRaw, _ => Task.FromResult<RawInformationSelection?>(new([(InformationField)99]))));
        Assert.False(await vm.ImportSelectedRawInformationAsync(FakeRaw, _ =>
        { vm.Camera = "New manual"; return Task.FromResult<RawInformationSelection?>(new([InformationField.Camera])); }));
        Assert.Equal("New manual", vm.Camera); Assert.Equal("Keep lens", vm.Lens); Assert.Null(vm.ConfirmedRaw);
        await Until(() => vm.CanExport); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void ReadOnlyInformationRowsShowMissingAndTrackMainEditsAndRawOrigins() => Ui.Run(async () =>
    {
        using var vm = RawVm(new RawReader(_ => Task.FromResult(RawExample))); await vm.InitializeAsync();
        Assert.Equal(8, vm.InformationRows.Count); Assert.All(vm.InformationRows, row => Assert.Equal("未提供", row.Value));
        Assert.False(vm.CanRestorePhotoExif); Assert.Contains("尚未載入", vm.RawSourceDescription);
        var changed = new List<string?>(); vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        await vm.LoadPhotoAsync(Source()); vm.Camera = "Main edit";
        Assert.Equal("Main edit", vm.InformationRows.Single(row => row.Field == InformationField.Camera).Value);
        Assert.Contains("主畫面", vm.CameraInformationSource); Assert.Contains(nameof(vm.InformationRows), changed);
        Assert.Contains("JPG", vm.DateInformationSource); Assert.Contains("非 EXIF", vm.DateInformationSource);
        Assert.True(await vm.ImportRawInformationAsync(FakeRaw, FillRaw));
        var lens = vm.InformationRows.Single(row => row.Field == InformationField.Lens);
        Assert.Equal(RawExample.LensModel, lens.Value); Assert.Contains("RAW", lens.Source);
        Assert.Contains(Path.GetFileName(FakeRaw), vm.RawSourceDescription);
        Assert.Contains(vm.SourceFileName, vm.RawSourceDescription);
        await Until(() => vm.CanExport); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void RestorePhotoExifCancelsSafelyThenClearsRawAndManualButPreservesPhotoAndLayout() => Ui.Run(async () =>
    {
        var path = Source(); var settings = Path.Combine(root, "restore-photo-info.json");
        using var vm = RawVm(new RawReader(_ => Task.FromResult(RawExample)), settings);
        await vm.InitializeAsync(); await vm.LoadPhotoAsync(path);
        var photo = PhotoObject(vm); var original = vm.InformationRows.ToArray();
        vm.RuleEditor.Gap = 37; vm.JpegQuality = 89;
        Assert.True(await vm.ImportRawInformationAsync(FakeRaw, OverwriteRaw)); vm.Camera = "Manual"; vm.ShutterInput = "invalid";
        var raw = vm.ConfirmedRaw;
        Assert.False(await vm.RestorePhotoExifAsync(() => Task.FromResult(false)));
        Assert.Same(raw, vm.ConfirmedRaw); Assert.Equal("Manual", vm.Camera); Assert.NotEmpty(vm.InputError);
        Assert.True(await vm.RestorePhotoExifAsync(() => Task.FromResult(true)));
        Assert.Same(photo, PhotoObject(vm)); Assert.Equal(path, vm.SourcePath); Assert.Null(vm.ConfirmedRaw);
        Assert.Equal(original, vm.InformationRows); Assert.Empty(vm.InputError); Assert.False(vm.HasInformationOverrides);
        Assert.Equal(37, vm.PhotoInfoGap); Assert.Equal(89, vm.JpegQuality); Assert.False(vm.CanRestorePhotoExif);
        await Until(() => vm.CanExport); await vm.FlushSettingsAsync();
        using var restored = RawVm(new RawReader(_ => throw new InvalidOperationException("RAW must not be read")), settings);
        await restored.InitializeAsync(); await restored.LoadPhotoAsync(path); Assert.Null(restored.ConfirmedRaw);
        Assert.Equal(original, restored.InformationRows); await Until(() => restored.CanExport); await restored.FlushSettingsAsync();
    });

    [Fact]
    public void RestoreConfirmationCannotOverwriteNewerManualInformation() => Ui.Run(async () =>
    {
        using var vm = RawVm(new RawReader(_ => Task.FromResult(RawExample))); await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source());
        Assert.True(await vm.ImportRawInformationAsync(FakeRaw, FillRaw));
        Assert.False(await vm.RestorePhotoExifAsync(() => { vm.Camera = "Newer edit"; return Task.FromResult(true); }));
        Assert.Equal("Newer edit", vm.Camera); Assert.NotNull(vm.ConfirmedRaw);
        await Until(() => vm.CanExport); await vm.FlushSettingsAsync();
    });

    private sealed class RawReader(Func<CancellationToken, Task<PhotoMetadata>> read) : IRawMetadataReader
    {
        public int Calls { get; private set; }
        public Task<PhotoMetadata> ReadAsync(string path, CancellationToken token) { Calls++; return read(token); }
    }
    private MainViewModel RawVm(IRawMetadataReader reader, string? path = null) => new(new ImageLoader(new ExifReader()),
        new LayoutEngine(new MagickTextMeasurer()), new PreviewService(new ImageRenderer()), new ExportService(new ImageRenderer()),
        new SettingsStore(path ?? Path.Combine(root, "raw-settings.json")), reader);
    private static PhotoMetadata RawExample => new() { Make = "Panasonic", Model = "DC-GH5", LensModel = "OLYMPUS M.12-40mm F2.8",
        CapturedAt = new DateTimeOffset(2026, 4, 15, 16, 19, 51, TimeSpan.FromHours(8)), DateSource = "DateTimeOriginal",
        FocalLengthMm = 31, FNumber = 3.2, ExposureSeconds = 1d / 60, Iso = 500 };
    private static Task<RawMergeMode?> FillRaw(RawInformationReview _) => Task.FromResult<RawMergeMode?>(RawMergeMode.FillMissing);
    private static Task<RawMergeMode?> OverwriteRaw(RawInformationReview _) => Task.FromResult<RawMergeMode?>(RawMergeMode.OverwriteProvided);
    private string FakeRaw => Path.Combine(root, "metadata-only.rw2");
    private static object? PhotoObject(MainViewModel vm) => typeof(MainViewModel).GetField("photo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm);

    [Fact]
    public void RawFillPreservesManualFieldsReplacesCreationFallbackAndNeverReplacesPhoto() => Ui.Run(async () =>
    {
        using var vm = RawVm(new RawReader(_ => Task.FromResult(RawExample))); await vm.InitializeAsync();
        await vm.LoadPhotoAsync(Source()); await Until(() => vm.CanExport);
        var photo = PhotoObject(vm); var path = vm.SourcePath; var summary = vm.SourceSummary;
        vm.Camera = "Manual camera"; vm.IsoValue = 800;
        Assert.True(await vm.ImportRawInformationAsync(FakeRaw, review =>
        {
            Assert.True(review.Differences.Single(row => row.Field == InformationField.Date).FillMissing);
            Assert.False(review.Differences.Single(row => row.Field == InformationField.Camera).FillMissing);
            Assert.Contains("無法確認", review.MatchWarning); return FillRaw(review);
        }));
        await Until(() => vm.CanExport);
        Assert.Same(photo, PhotoObject(vm)); Assert.Equal(path, vm.SourcePath); Assert.Equal(summary, vm.SourceSummary);
        Assert.Equal("Manual camera", vm.Camera); Assert.Equal(800, vm.IsoValue);
        Assert.Equal(RawExample.LensModel, vm.Lens); Assert.Equal(31, vm.FocalLengthValue);
        Assert.Equal(RawExample.CapturedAt, vm.SelectedDate); Assert.Equal(RawExample.CapturedAt!.Value.TimeOfDay, vm.SelectedTime);
        Assert.Equal(InformationOrigin.Manual, vm.InformationSource(InformationField.Camera));
        Assert.Equal(InformationOrigin.Raw, vm.InformationSource(InformationField.Lens));
        Assert.Equal(new PhotoMetadata(), vm.PhotoExif); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void RawOverwriteOnlyTouchesProvidedFieldsAndKeepsPreviousRawWhenNewReadLacksThem() => Ui.Run(async () =>
    {
        var data = RawExample; using var vm = RawVm(new RawReader(_ => Task.FromResult(data))); await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source());
        Assert.True(await vm.ImportRawInformationAsync(FakeRaw, OverwriteRaw));
        vm.Lens = "manual edit"; vm.IsoValue = 800;
        data = new() { LensModel = "New lens" };
        Assert.True(await vm.ImportRawInformationAsync(FakeRaw, OverwriteRaw));
        Assert.Equal("New lens", vm.Lens); Assert.Equal(800, vm.IsoValue); Assert.Equal(31, vm.FocalLengthValue);
        Assert.Equal(InformationOrigin.Manual, vm.InformationSource(InformationField.Iso));
        Assert.Equal(InformationOrigin.Raw, vm.InformationSource(InformationField.Lens));
        Assert.False(await vm.ReloadExifAsync(() => Task.FromResult(false))); Assert.Equal(800, vm.IsoValue);
        Assert.True(await vm.ReloadExifAsync(() => Task.FromResult(true)));
        Assert.Equal("New lens", vm.Lens); Assert.Equal(31, vm.FocalLengthValue); Assert.Equal(500, vm.IsoValue);
        Assert.NotNull(vm.ConfirmedRaw); await Until(() => vm.CanExport); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void RawCancellationFailureAndNoSourceDoNotChangeValues() => Ui.Run(async () =>
    {
        var fail = false; var reader = new RawReader(_ => fail ? Task.FromException<PhotoMetadata>(new IOException("fixture failure")) : Task.FromResult(RawExample));
        using var vm = RawVm(reader); await vm.InitializeAsync();
        Assert.False(await vm.ImportRawInformationAsync(FakeRaw, FillRaw)); Assert.Equal(0, reader.Calls); Assert.False(vm.HasPhoto);
        await vm.LoadPhotoAsync(Source()); vm.Lens = "Keep lens";
        Assert.False(await vm.ImportRawInformationAsync(FakeRaw, _ => Task.FromResult<RawMergeMode?>(null)));
        Assert.Equal("Keep lens", vm.Lens); Assert.Null(vm.ConfirmedRaw);
        fail = true; Assert.False(await vm.ImportRawInformationAsync(FakeRaw, FillRaw));
        Assert.Equal("Keep lens", vm.Lens); Assert.Null(vm.ConfirmedRaw); Assert.Contains("保留目前資訊", vm.ErrorText);
        await Until(() => vm.CanExport); await vm.FlushSettingsAsync();
    });

    [Theory]
    [InlineData("source")] [InlineData("manual")] [InlineData("cancel")]
    public void OldRawReadCannotApplyAfterSourceEditOrCancellation(string change) => Ui.Run(async () =>
    {
        var completion = new TaskCompletionSource<PhotoMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var vm = RawVm(new RawReader(_ => completion.Task)); await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source());
        using var cancellation = new CancellationTokenSource();
        var dialogCalls = 0; var pending = vm.ImportRawInformationAsync(FakeRaw, review => { dialogCalls++; return FillRaw(review); }, cancellation.Token);
        if (change == "source")
        {
            var other = Path.Combine(root, "other.png"); using (var image = new MagickImage(MagickColors.White, 1200, 700)) image.Write(other);
            await vm.LoadPhotoAsync(other);
        }
        else if (change == "manual") vm.Lens = "New manual edit";
        else cancellation.Cancel();
        completion.SetResult(RawExample); Assert.False(await pending); Assert.Equal(0, dialogCalls); Assert.Null(vm.ConfirmedRaw);
        if (change == "manual") Assert.Equal("New manual edit", vm.Lens);
        await Until(() => vm.CanExport); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void RawFillDoesNotHideInvalidManualShutterAndOverwriteCanRepairIt() => Ui.Run(async () =>
    {
        using var vm = RawVm(new RawReader(_ => Task.FromResult(RawExample))); await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source());
        vm.ShutterInput = "1/zero"; Assert.NotEmpty(vm.InputError);
        Assert.True(await vm.ImportRawInformationAsync(FakeRaw, FillRaw));
        Assert.Equal("1/zero", vm.ShutterInput); Assert.NotEmpty(vm.InputError); Assert.False(vm.CanExport);
        Assert.True(await vm.ImportRawInformationAsync(FakeRaw, OverwriteRaw));
        Assert.Equal("1/60", vm.ShutterInput); Assert.Empty(vm.InputError);
        await Until(() => vm.CanExport); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void ManualChangeDuringRawConfirmationInvalidatesReview() => Ui.Run(async () =>
    {
        using var vm = RawVm(new RawReader(_ => Task.FromResult(RawExample))); await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source());
        Assert.False(await vm.ImportRawInformationAsync(FakeRaw, review => { vm.Lens = "Typed during review"; return OverwriteRaw(review); }));
        Assert.Equal("Typed during review", vm.Lens); Assert.Null(vm.ConfirmedRaw); await Until(() => vm.CanExport); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void SavedRawSnapshotSurvivesMissingRawAndClearsWhenSourceChanges() => Ui.Run(async () =>
    {
        var path = Source(); var settings = Path.Combine(root, "snapshot.json");
        using (var vm = RawVm(new RawReader(_ => Task.FromResult(RawExample)), settings))
        {
            await vm.InitializeAsync(); await vm.LoadPhotoAsync(path); Assert.True(await vm.ImportRawInformationAsync(FakeRaw, FillRaw));
            vm.Camera = "Saved manual camera"; await Until(() => vm.CanExport); await vm.FlushSettingsAsync();
        }
        var reader = new RawReader(_ => Task.FromException<PhotoMetadata>(new FileNotFoundException("RAW was moved")));
        using var restored = RawVm(reader, settings); await restored.InitializeAsync();
        Assert.False(restored.HasPhoto); await restored.LoadPhotoAsync(path); await Until(() => restored.CanExport);
        Assert.Equal("Saved manual camera", restored.Camera); Assert.Equal(RawExample.LensModel, restored.Lens); Assert.Equal(0, reader.Calls);
        Assert.Equal(InformationOrigin.Manual, restored.InformationSource(InformationField.Camera));
        Assert.Equal(InformationOrigin.Raw, restored.InformationSource(InformationField.Lens));
        Assert.False(await restored.ReloadRawInformationAsync(FillRaw)); Assert.Equal(RawExample.LensModel, restored.Lens);
        var exported = Path.Combine(root, "raw-snapshot-export.jpg"); await restored.ExportAsync(exported); Assert.True(File.Exists(exported));
        var other = Path.Combine(root, "different.png"); using (var image = new MagickImage(MagickColors.White, 1400, 800)) image.Write(other);
        await restored.LoadPhotoAsync(other); await Until(() => restored.CanExport); Assert.Null(restored.ConfirmedRaw); Assert.Empty(restored.Lens);
        await restored.FlushSettingsAsync(); Assert.Null((await new SettingsStore(settings).LoadAsync()).WorkInformation!.Raw);
    });

    [Fact]
    public void ConfiguredRealGh5RawAndEditedJpegMergeAndExportReadOnly() => Ui.Run(async () =>
    {
        var raw = Environment.GetEnvironmentVariable("PHOTOMARGIN_RW2_SAMPLE"); var jpeg = Environment.GetEnvironmentVariable("PHOTOMARGIN_EDITED_SAMPLE");
        if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrWhiteSpace(jpeg)) { Assert.Skip("Set real RAW/JPG fixture paths for optional read-only validation."); return; }
        static async Task<string> Hash(string path) { await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream)); }
        var rawHash = await Hash(raw); var jpegHash = await Hash(jpeg);
        using var vm = RawVm(new RawMetadataReader()); await vm.InitializeAsync(); await vm.LoadPhotoAsync(jpeg);
        vm.Arrangement = InformationArrangement.Stacked; await Until(() => vm.CanExport);
        var photo = PhotoObject(vm); var sourceExif = vm.PhotoExif;
        Assert.True(await vm.ImportRawInformationAsync(raw, OverwriteRaw)); await Until(() => vm.CanExport);
        Assert.Same(photo, PhotoObject(vm)); Assert.Equal(Path.GetFullPath(jpeg), vm.SourcePath);
        var metadata = vm.ConfirmedRaw!.Metadata;
        Assert.Equal(metadata.CameraDisplay, vm.Camera); Assert.Equal(metadata.LensModel, vm.Lens);
        Assert.Equal(metadata.FocalLengthMm, vm.FocalLengthValue); Assert.Equal(metadata.FNumber, vm.ApertureValue);
        Assert.Equal(metadata.ShutterDisplay, vm.Shutter); Assert.Equal((double)metadata.Iso!.Value, vm.IsoValue); Assert.Equal(metadata.CapturedAt, vm.SelectedDate);
        var export = Path.Combine(root, "real-raw-export.jpg"); await vm.ExportAsync(export); Assert.True(File.Exists(export));
        Assert.Equal(rawHash, await Hash(raw)); Assert.Equal(jpegHash, await Hash(jpeg));
        Assert.Equal(new PhotoMetadata(), new ExifReader().Read(export));
        var proof = Environment.GetEnvironmentVariable("PHOTOMARGIN_RAW_MERGE_PROOF");
        if (!string.IsNullOrWhiteSpace(proof)) await File.WriteAllTextAsync(proof,
            JsonSerializer.Serialize(new { SourceExif = sourceExif, RawExif = metadata, PhotoInstanceUnchanged = true,
                OriginalFilesUnchanged = true, ExportSucceeded = true }, new JsonSerializerOptions { WriteIndented = true }));
        await vm.FlushSettingsAsync();
    });
}
