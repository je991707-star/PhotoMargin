using System.Globalization;
using PhotoMargin.Core.Models;

namespace PhotoMargin.App.ViewModels;

public sealed partial class MainViewModel
{
    internal void ReportExportPickerFailure(Exception error) => ErrorText = "無法選擇儲存資料夾：" + error.Message;
    private ExportPreferences exportPreferences = new();
    public IReadOnlyList<string> ExportFormats { get; } = ["JPG", "PNG", "WebP"];
    public IReadOnlyList<string> ExportSizes { get; } = ["1080", "1440", "2048", "2160", "自訂"];
    public IReadOnlyList<string> FileNameModes { get; } = ["原檔名_framed", "自訂"];
    public IReadOnlyList<string> ConflictModes { get; } = ["自動編號", "詢問", "覆寫"];
    public int ExportFormatIndex { get => (int)exportPreferences.Format; set { if (value is >= 0 and <= 2) SetExport(exportPreferences with { Format = (ExportFormat)value }); } }
    public int ExportSizeIndex { get => customExportSize ? 4 : OutputSize switch { 1080 => 0, 1440 => 1, 2048 => 2, 2160 => 3, _ => 4 }; set { if (value is >= 0 and < 4) { customExportSize = false; OutputSize = new[] { 1080, 1440, 2048, 2160 }[value]; } else if (value == 4) { customExportSize = true; NotifyExport(); } } }
    private bool customExportSize;
    public bool ShowCustomExportSize => customExportSize || ExportSizeIndex == 4;
    public bool ShowExportQuality => !exportPreferences.Lossless;
    public bool ShowExportLossless => exportPreferences.Lossless;
    public bool ShowWebpOptions => exportPreferences.Format == ExportFormat.WebP;
    public bool WebpLossless { get => exportPreferences.WebpLossless; set => SetExport(exportPreferences with { WebpLossless = value }); }
    public double ExportQuality { get => exportPreferences.Format == ExportFormat.WebP ? exportPreferences.WebpQuality : JpegQuality; set { if (!double.IsFinite(value)) return; if (exportPreferences.Format == ExportFormat.WebP) SetExport(exportPreferences with { WebpQuality = (int)Math.Clamp(Math.Round(value), 1, 100) }); else JpegQuality = value; } }
    public string ExportQualityText => ShowExportQuality ? $"{ExportQuality:0}" : "";
    public string ExportDirectory { get => exportPreferences.Directory ?? ""; set => SetExport(exportPreferences with { Directory = string.IsNullOrWhiteSpace(value) ? null : value }); }
    public int FileNameMode { get => exportPreferences.CustomFileName ? 1 : 0; set => SetExport(exportPreferences with { CustomFileName = value == 1 }); }
    public bool ShowCustomFileName => exportPreferences.CustomFileName;
    public string FileNamePattern { get => exportPreferences.FileNamePattern; set => SetExport(exportPreferences with { FileNamePattern = value }); }
    public string FileNameExample => exportPreferences.GetFileName(SourcePath, SelectedDate ?? DateTimeOffset.Now);
    public int ConflictMode { get => (int)exportPreferences.ConflictPolicy; set { if (value is >= 0 and <= 2) SetExport(exportPreferences with { ConflictPolicy = (FileConflictPolicy)value }); } }
    public bool IncludeExportMetadata { get => exportPreferences.IncludeMetadata; set => SetExport(exportPreferences with { IncludeMetadata = value }); }
    public bool OpenExportFolder { get => exportPreferences.OpenFolder; set => SetExport(exportPreferences with { OpenFolder = value }); }
    public bool RememberExportLocation { get => exportPreferences.RememberLocation; set => SetExport(exportPreferences with { RememberLocation = value }); }
    private void SetExport(ExportPreferences value)
    {
        value = value.Validate();
        if (value == exportPreferences) return;
        exportPreferences = value; NotifyExport();
        if (!applyingValues) _ = SaveSettingsQuietlyAsync();
    }
    private void NotifyExport()
    {
        foreach (var name in new[] { nameof(ExportFormatIndex), nameof(ExportSizeIndex), nameof(ShowCustomExportSize), nameof(ShowExportQuality), nameof(ShowExportLossless), nameof(ShowWebpOptions), nameof(WebpLossless), nameof(ExportQuality), nameof(ExportQualityText), nameof(ExportDirectory), nameof(FileNameMode), nameof(ShowCustomFileName), nameof(FileNamePattern), nameof(FileNameExample), nameof(ConflictMode), nameof(IncludeExportMetadata), nameof(OpenExportFolder), nameof(RememberExportLocation) }) OnPropertyChanged(name);
    }
    public Task ExportWithDialogsAsync(Func<ExportFormat, string, Task<string?>> chooseFile,
        Func<string, Task<bool>> confirmOverwrite, Func<string, Task> openFolder) =>
        RunExportAsync(null, chooseFile, confirmOverwrite, openFolder);

    private async Task RunExportAsync(string? explicitPath, Func<ExportFormat, string, Task<string?>>? chooseFile = null,
        Func<string, Task<bool>>? confirmOverwrite = null, Func<string, Task>? openFolder = null)
    {
        if (!CanExport || disposed) return;
        var preferences = exportPreferences with { JpegQuality = (int)Math.Round(JpegQuality) };
        var settings = BuildLayoutSettings(); var display = GetDisplayMetadata();
        var source = photo!; var rawPath = confirmedRaw?.SourcePath;
        IsBusy = true; ErrorText = ""; StatusText = "正在準備匯出…";
        try
        {
            var metadata = preferences.IncludeMetadata ? ExportMetadata() : null;
            var name = preferences.GetFileName(SourcePath, SelectedDate ?? DateTimeOffset.Now);
            var destination = explicitPath ?? (string.IsNullOrWhiteSpace(preferences.Directory)
                ? chooseFile is null ? null : await chooseFile(preferences.Format, name)
                : Path.Combine(preferences.Directory, name));
            if (destination is null) { StatusText = "已取消匯出"; return; }
            // Ask mode never silently overwrites. Hold a read/delete-exclusive handle
            // while confirming, then use a fresh identity check in ExportService.
            FileStream? confirmationLock = null;
            try
            {
                if (preferences.ConflictPolicy == FileConflictPolicy.Ask && File.Exists(destination))
                {
                    confirmationLock = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (confirmOverwrite is null || !await confirmOverwrite(destination)) { StatusText = "已取消匯出"; return; }
                }
                await renderGate.WaitAsync(lifetime.Token);
                string result;
                try
                {
                    var layout = await Task.Run(() => layoutEngine.Calculate(source.OrientedSize, display, settings), lifetime.Token);
                    StatusText = $"正在匯出 {ExportFormats[(int)preferences.Format]}…";
                    result = await exportService.ExportAsync(source, layout, destination, preferences, metadata, rawPath, lifetime.Token, confirmationLock);
                }
                finally { renderGate.Release(); }
                var size = new FileInfo(result).Length;
                var fileSize = size >= 1048576 ? $"{size / 1048576d:0.0} MB" : $"{size / 1024d:0.0} KB";
                StatusText = $"已匯出 {ExportFormats[(int)preferences.Format]} · {settings.OutputSize}×{settings.OutputSize} · {fileSize}";
                if (preferences.RememberLocation && exportPreferences.RememberLocation && exportPreferences.Directory == preferences.Directory)
                { exportPreferences = exportPreferences with { Directory = Path.GetDirectoryName(result) }; lastOutputDirectory = exportPreferences.Directory; NotifyExport(); await SaveSettingsQuietlyAsync(); }
                if (preferences.OpenFolder && openFolder is not null)
                    try { await openFolder(Path.GetDirectoryName(result)!); } catch (Exception e) { ErrorText = "已匯出，無法開啟資料夾：" + e.Message; }
            }
            finally { confirmationLock?.Dispose(); }
        }
        catch (OperationCanceledException) { if (!disposed) StatusText = "已取消匯出"; }
        catch (Exception e) { if (!disposed) { ErrorText = e.Message; StatusText = "匯出失敗"; } }
        finally { if (!disposed) IsBusy = false; }
    }
    private PhotoMetadata ExportMetadata()
    {
        double? Number(double value) => double.IsFinite(value) && value > 0 ? value : null;
        var shutterParts = Shutter.Trim().TrimEnd('s', 'S').Split('/');
        double? seconds = null;
        if (shutterParts.Length == 1 && double.TryParse(shutterParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var single)) seconds = Number(single);
        else if (shutterParts.Length == 2 && double.TryParse(shutterParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.TryParse(shutterParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) seconds = Number(n / d);
        if (SelectedDate.HasValue != SelectedTime.HasValue) throw new InvalidOperationException("保留拍攝資訊需要完整日期與時間。");
        var iso = Number(IsoValue);
        return new PhotoMetadata
        {
            Model = Camera.Trim(), LensModel = Lens.Trim(), FocalLengthMm = Number(FocalLengthValue), FNumber = Number(ApertureValue),
            ExposureSeconds = seconds, Iso = iso is null ? null : checked((int)iso.Value),
            CapturedAt = SelectedDate is { } date && SelectedTime is { } time ? new DateTimeOffset(date.Date + time, date.Offset) : null
        };
    }
}
