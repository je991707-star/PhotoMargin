using ImageMagick;
using PhotoMargin.App.Services;
using PhotoMargin.Core.Models;

namespace PhotoMargin.App.LogicTests;

public sealed partial class StateAndPersistenceTests
{
    [Fact]
    public void ExportMetadataUsesConfirmedRawAndManualValuesWithoutReopeningRaw() => Ui.Run(async () =>
    {
        using var vm = RawVm(new RawReader(_ => Task.FromResult(RawExample))); await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source());
        Assert.True(await vm.ImportSelectedRawInformationAsync(FakeRaw, _ => Task.FromResult<RawInformationSelection?>(new([InformationField.Lens, InformationField.FocalLength]))));
        vm.Camera = "Manual camera"; vm.IncludeExportMetadata = true; vm.ExportFormatIndex = 2; vm.WebpLossless = true;
        await Until(() => vm.CanExport);
        var source = vm.SourcePath; var destination = Path.Combine(root, "raw-effective.webp"); await vm.ExportAsync(destination);
        using var decoded = new MagickImage(destination);
        Assert.Equal("Manual camera", decoded.GetExifProfile()!.GetValue(ExifTag.Model)!.Value);
        Assert.Equal(RawExample.LensModel, decoded.GetExifProfile()!.GetValue(ExifTag.LensModel)!.Value);
        Assert.Equal(31, decoded.GetExifProfile()!.GetValue(ExifTag.FocalLength)!.Value.ToDouble());
        Assert.Equal(source, vm.SourcePath); Assert.False(File.Exists(FakeRaw)); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void ExportPreferencesMigrateAndKeepSeparateQuality() => Ui.Run(async () =>
    {
        var path = Path.Combine(root, "old-export.json");
        await File.WriteAllTextAsync(path, """{"version":9,"layout":{"outputSize":1440,"jpegQuality":83},"lastOutputDirectory":"E:\\Saved"}""", TestContext.Current.CancellationToken);
        using (var vm = Create(path))
        {
            await vm.InitializeAsync(); Assert.Equal(1440, vm.OutputSize); Assert.Equal(83, vm.ExportQuality); Assert.Equal("E:\\Saved", vm.ExportDirectory);
            Assert.Equal(0, vm.ExportFormatIndex); Assert.True(vm.RememberExportLocation); Assert.False(vm.IncludeExportMetadata);
            vm.ExportFormatIndex = 2; vm.ExportQuality = 27; vm.WebpLossless = true;
            Assert.False(vm.ShowExportQuality); Assert.True(vm.ShowExportLossless);
            vm.ExportFormatIndex = 0; Assert.Equal(83, vm.ExportQuality); vm.ExportQuality = 1;
            vm.ExportFormatIndex = 1; Assert.False(vm.ShowExportQuality); Assert.True(vm.ShowExportLossless);
            vm.ExportFormatIndex = 2; Assert.Equal(27, vm.ExportQuality);
            vm.FileNameMode = 1; vm.FileNamePattern = "{name}_{date}.jpg.webp"; vm.OutputSize = 2160;
            await vm.FlushSettingsAsync();
        }
        using var restored = Create(path); await restored.InitializeAsync();
        Assert.Equal(2160, restored.OutputSize); Assert.Equal(27, restored.ExportQuality); Assert.True(restored.WebpLossless);
        restored.ExportFormatIndex = 0; Assert.Equal(1, restored.JpegQuality);
        Assert.Equal(0, restored.ConflictMode); Assert.EndsWith(".jpg", restored.FileNameExample);
        restored.RememberExportLocation = false; await restored.FlushSettingsAsync();
        var saved = await new SettingsStore(path).LoadAsync(TestContext.Current.CancellationToken);
        Assert.Null(saved.Export!.Directory); Assert.Null(saved.LastOutputDirectory);
    });

    [Fact]
    public void ExportSnapshotIncludesEffectiveInformationAndDoesNotChangeWithUi() => Ui.Run(async () =>
    {
        using var vm = Create(); await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source()); await Until(() => vm.CanExport);
        vm.Camera = "Manual camera"; vm.Lens = "Manual lens"; vm.FocalLengthValue = 35; vm.ApertureValue = 2.8; vm.IsoValue = 200;
        vm.ShutterInput = "1/250"; vm.IncludeExportMetadata = true; vm.ExportFormatIndex = 1; vm.OutputSize = 1440; await Until(() => vm.CanExport);
        var path = Path.Combine(root, "snapshot.png");
        await vm.ExportWithDialogsAsync((format, name) =>
        {
            Assert.Equal(ExportFormat.Png, format); Assert.EndsWith(".png", name); Assert.False(vm.CanExport); Assert.False(vm.CanSelectPhoto);
            vm.ExportFormatIndex = 2; vm.OutputSize = 1080; vm.IncludeExportMetadata = false;
            return Task.FromResult<string?>(path);
        }, _ => Task.FromResult(false), _ => Task.CompletedTask);
        using var decoded = new MagickImage(path); Assert.Equal(MagickFormat.Png, decoded.Format); Assert.Equal(1440u, decoded.Width);
        Assert.Equal("Manual lens", decoded.GetExifProfile()!.GetValue(ExifTag.LensModel)!.Value);
        Assert.StartsWith("已匯出 PNG · 1440×1440", vm.StatusText);
        await Until(() => vm.CanExport); await vm.FlushSettingsAsync();
    });

    [Fact]
    public void AskAndCancelLeaveExistingFileUntouched() => Ui.Run(async () =>
    {
        using var vm = Create(); await vm.InitializeAsync(); await vm.LoadPhotoAsync(Source()); await Until(() => vm.CanExport);
        var path = Path.Combine(root, "source_framed.jpg"); await File.WriteAllTextAsync(path, "existing", TestContext.Current.CancellationToken);
        vm.ExportDirectory = root; vm.ConflictMode = 1;
        var calls = 0;
        await vm.ExportWithDialogsAsync((_, _) => throw new InvalidOperationException(), p => { calls++; Assert.Equal(path, p); return Task.FromResult(false); }, _ => Task.CompletedTask);
        Assert.Equal(1, calls); Assert.Equal("existing", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        await vm.ExportWithDialogsAsync((_, _) => throw new InvalidOperationException(), _ => Task.FromResult(true), _ => Task.CompletedTask);
        using var decoded = new MagickImage(path); Assert.Equal(MagickFormat.Jpeg, decoded.Format);
        Assert.Empty(Directory.GetFiles(root, "*.tmp")); await vm.FlushSettingsAsync();
    });
}
