using PhotoMargin.App.Services;
using PhotoMargin.Core.Models;
using PhotoMargin.Imaging;

namespace PhotoMargin.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly IRawMetadataReader rawMetadataReader;
    private PhotoMetadata photoExif = new();
    private (DateTimeOffset Time, bool Fallback, string Source)? photoCapture;
    private RawInformationSnapshot? confirmedRaw;
    private WorkInformationSnapshot? pendingInformation;
    private readonly Dictionary<InformationField, InformationOrigin> informationOrigins = [];
    private bool isRawReading;
    private long rawRequestVersion;
    public PhotoMetadata PhotoExif => photoExif;
    public RawInformationSnapshot? ConfirmedRaw => confirmedRaw;
    public bool IsRawReading { get => isRawReading; private set { if (SetProperty(ref isRawReading, value)) NotifyRawAvailability(); } }
    public bool CanReadRaw => HasPhoto && !IsBusy && !IsRawReading && !IsDataBusy;
    public bool CanReloadRaw => CanReadRaw && confirmedRaw is not null;
    public bool CanRestorePhotoExif => CanReadRaw && (confirmedRaw is not null || HasInformationOverrides);
    private string PhotoFileFormat => SourcePath is null ? "照片" : Path.GetExtension(SourcePath).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "PNG" : "JPG";
    public string RestorePhotoExifLabel => $"還原為 {PhotoFileFormat} 原本 EXIF";
    public string RawSourceDescription => !HasPhoto ? "目前套用檔案：尚未載入 JPG／PNG" :
        confirmedRaw is { } raw ? $"目前套用檔案：{Path.GetFileName(raw.SourcePath)}（RAW）\n作品照片：{SourceFileName}（{PhotoFileFormat}）" :
        $"目前套用檔案：{SourceFileName}（{PhotoFileFormat}）";
    public IReadOnlyList<PhotoInformationRow> InformationRows => Enum.GetValues<InformationField>().Select(item =>
        new PhotoInformationRow(item, RawInformationReview.Name(item), Provided(CurrentInformation().Display(item)), SourceLabel(item))).ToArray();
    private static string Provided(string value) => string.IsNullOrWhiteSpace(value) ? "未提供" : value;
    public string CameraInformationSource => SourceLabel(InformationField.Camera);
    public string LensInformationSource => SourceLabel(InformationField.Lens);
    public string DateInformationSource => SourceLabel(InformationField.Date);
    public string TimeInformationSource => SourceLabel(InformationField.Time);
    public string FocalLengthInformationSource => SourceLabel(InformationField.FocalLength);
    public string ApertureInformationSource => SourceLabel(InformationField.Aperture);
    public string ShutterInformationSource => SourceLabel(InformationField.Shutter);
    public string IsoInformationSource => SourceLabel(InformationField.Iso);
    public InformationOrigin InformationSource(InformationField field) => informationOrigins.GetValueOrDefault(field);
    private InformationValues CurrentInformation() => new()
    {
        Camera = Camera, Lens = Lens, FocalLength = FocalLength, Aperture = Aperture, Shutter = Shutter, Iso = Iso,
        Date = SelectedDate, Time = SelectedTime
    };
    private string SourceLabel(InformationField field) => string.IsNullOrWhiteSpace(CurrentInformation().Display(field))
        ? InformationSource(field) == InformationOrigin.Manual ? "未提供（手動修改）" : "未提供"
        : InformationSource(field) switch
        {
            InformationOrigin.PhotoExif => PhotoFileFormat + " EXIF",
            InformationOrigin.FileCreation => PhotoFileFormat + " 檔案建立日期（非 EXIF）",
            InformationOrigin.Manual => "手動修改（主畫面）",
            _ => RawInformationReview.OriginLabel(InformationSource(field))
        };
    private void NotifyRawAvailability()
    {
        foreach (var property in new[] { nameof(CanManageData), nameof(CanExport), nameof(CanReadRaw), nameof(CanReloadRaw), nameof(CanReload), nameof(CanRestorePhotoExif),
            nameof(RawSourceDescription), nameof(RestorePhotoExifLabel) }) OnPropertyChanged(property);
    }
    private void NotifyInformationSources()
    {
        var dateOrigin = InformationSource(InformationField.Date);
        CaptureTimeSource = dateOrigin switch
        {
            InformationOrigin.Raw => "RAW 補入：" + (confirmedRaw?.Metadata.DateSource ?? "EXIF"),
            InformationOrigin.Manual => "手動修改", InformationOrigin.FileCreation => "檔案建立日期",
            InformationOrigin.PhotoExif => photoExif.DateSource ?? "EXIF", _ => "未提供"
        };
        foreach (var property in new[] { nameof(CameraInformationSource), nameof(LensInformationSource), nameof(DateInformationSource),
            nameof(TimeInformationSource), nameof(FocalLengthInformationSource), nameof(ApertureInformationSource),
            nameof(ShutterInformationSource), nameof(IsoInformationSource), nameof(ConfirmedRaw), nameof(PhotoExif), nameof(HasInformationOverrides),
            nameof(InformationRows) }) OnPropertyChanged(property);
        NotifyRawAvailability();
    }
    private void ResetRawAssociation() { rawRequestVersion++; confirmedRaw = null; }
    private void ResetPhotoOrigins(bool fallback)
    {
        informationOrigins.Clear();
        foreach (var field in Enum.GetValues<InformationField>())
            informationOrigins[field] = string.IsNullOrWhiteSpace(CurrentInformation().Display(field)) ? InformationOrigin.Missing :
                fallback && field is InformationField.Date or InformationField.Time ? InformationOrigin.FileCreation : InformationOrigin.PhotoExif;
        NotifyInformationSources();
    }
    private void MarkInformationManual(string? property)
    {
        InformationField? field = property switch
        {
            nameof(Camera) => InformationField.Camera, nameof(Lens) => InformationField.Lens,
            nameof(SelectedDate) => InformationField.Date, nameof(SelectedTime) => InformationField.Time,
            nameof(FocalLength) => InformationField.FocalLength, nameof(Aperture) => InformationField.Aperture,
            nameof(Shutter) or nameof(ShutterInput) => InformationField.Shutter, nameof(Iso) => InformationField.Iso, _ => null
        };
        if (field is { } selected) informationOrigins[selected] = InformationOrigin.Manual;
        NotifyInformationSources();
    }
    private void SetInformationValues(InformationValues values)
    {
        var keepInvalidShutter = InputError.Length > 0 && InformationSource(InformationField.Shutter) == InformationOrigin.Manual && values.Shutter == Shutter;
        applyingValues = true;
        try
        {
            Camera = values.Camera; Lens = values.Lens; FocalLength = values.FocalLength;
            Aperture = values.Aperture; Shutter = values.Shutter; Iso = values.Iso;
            SelectedDate = values.Date; SelectedTime = values.Time;
            if (!keepInvalidShutter)
            {
                shutterInput = shutter.TrimEnd('s', 'S').Trim(); OnPropertyChanged(nameof(ShutterInput)); InputError = "";
            }
        }
        finally { applyingValues = false; }
    }
    private void ReapplyRawInformation()
    {
        if (confirmedRaw is not { } raw) return;
        var values = CurrentInformation();
        foreach (var field in raw.AppliedFields)
        {
            values = values.CopyField(field, raw.AppliedValues);
            informationOrigins[field] = InformationOrigin.Raw;
        }
        SetInformationValues(values); NotifyInformationSources();
    }
    public Task<bool> ReloadRawInformationAsync(Func<RawInformationReview, Task<RawMergeMode?>> confirm, CancellationToken token = default) =>
        confirmedRaw is { } raw ? ImportRawInformationAsync(raw.SourcePath, confirm, token) : Task.FromResult(false);
    public Task<bool> ReloadSelectedRawInformationAsync(Func<RawInformationReview, Task<RawInformationSelection?>> confirm, CancellationToken token = default) =>
        confirmedRaw is { } raw ? ImportSelectedRawInformationAsync(raw.SourcePath, confirm, token) : Task.FromResult(false);
    public async Task<bool> RestorePhotoExifAsync(Func<Task<bool>> confirm)
    {
        if (!CanRestorePhotoExif || disposed || photoCapture is not { } capture) return false;
        var source = sourceVersion; var information = informationVersion;
        IsBusy = true;
        try
        {
            if (!await confirm() || disposed || source != sourceVersion) return false;
            if (information != informationVersion)
            { StatusText = "確認期間資訊已變更，未套用過期還原操作，請重試。"; return false; }
            // Restore the loaded photo's metadata snapshot. Never reopen RAW or
            // replace/redecode image pixels; missing capture dates retain the
            // explicitly labelled, cached file-creation fallback.
            ResetRawAssociation(); ApplyPhotoInformation(photoExif, capture); informationVersion++;
            ErrorText = ""; StatusText = $"已還原為 {PhotoFileFormat} 原本 EXIF；RAW 關聯與手動修改已清除。";
            RequestPreview(true); RuleEditor.RefreshPreview();
            await SaveSettingsQuietlyAsync(); return true;
        }
        finally { if (!disposed) IsBusy = false; }
    }
    // Programmatic policies share this field-based commit path; no second merge
    // algorithm or hidden global-mode UI is retained.
    public Task<bool> ImportRawInformationAsync(string path, Func<RawInformationReview, Task<RawMergeMode?>> confirm,
        CancellationToken token = default) => ImportSelectedRawInformationAsync(path, async review =>
        {
            var mode = await confirm(review);
            return mode is { } selected && Enum.IsDefined(selected) ? RawInformationSelection.FromMode(review, selected) : null;
        }, token);
    public async Task<bool> ImportSelectedRawInformationAsync(string path, Func<RawInformationReview, Task<RawInformationSelection?>> confirm,
        CancellationToken token = default)
    {
        if (!HasPhoto) { StatusText = "請先匯入修好的 JPG／PNG，再選擇同張照片的 RAW。"; return false; }
        if (!CanReadRaw || disposed) return false;
        var source = sourceVersion; var information = informationVersion; var request = ++rawRequestVersion;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
        IsRawReading = true;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var metadata = await rawMetadataReader.ReadAsync(fullPath, cancellation.Token);
            if (Stale()) return false;
            var review = new RawInformationReview(fullPath, metadata, CurrentInformation(), informationOrigins, photoExif, PhotoFileFormat);
            var choice = await confirm(review);
            if (choice is null || choice.RawFields.Any(field => !Enum.IsDefined(field)) || Stale()) return false;
            var selected = review.Differences.Where(row => choice.RawFields.Contains(row.Field) && !string.IsNullOrWhiteSpace(row.Incoming)).ToArray();
            if (selected.Length == 0)
            { ErrorText = ""; StatusText = "已保留所有目前資訊，未套用 RAW 欄位。"; return true; }
            var values = CurrentInformation();
            var appliedValues = confirmedRaw?.AppliedValues ?? new();
            var fields = confirmedRaw?.AppliedFields.ToHashSet() ?? [];
            foreach (var row in selected)
            {
                values = values.CopyField(row.Field, review.Incoming);
                appliedValues = appliedValues.CopyField(row.Field, review.Incoming);
                fields.Add(row.Field); informationOrigins[row.Field] = InformationOrigin.Raw;
            }
            confirmedRaw = new(fullPath, metadata, appliedValues, fields.ToArray());
            SetInformationValues(values); informationVersion++; NotifyInformationSources();
            RequestPreview(true); RuleEditor.RefreshPreview();
            ErrorText = "";
            StatusText = "RAW 拍攝資訊已套用；作品照片、方向與構圖未更動。";
            await SaveSettingsQuietlyAsync(); return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception)
        { if (!disposed && source == sourceVersion) ErrorText = "RAW 讀取失敗，保留目前資訊：" + exception.Message; return false; }
        finally { if (!disposed) IsRawReading = false; }
        bool Stale()
        {
            if (disposed || cancellation.IsCancellationRequested || source != sourceVersion || request != rawRequestVersion) return true;
            if (information == informationVersion) return false;
            StatusText = "照片資訊在讀取／確認期間已變更，未套用過期 RAW 結果，請重試。"; return true;
        }
    }
    private WorkInformationSnapshot? CaptureWorkInformation()
    {
        if (photo is null) return pendingInformation;
        // Identity captured at load: rendering/export need not reopen either source.
        return new(photo.SourcePath, loadedInformationLength, loadedInformationWriteTicks, photoExif,
            confirmedRaw, CurrentInformation(), new(informationOrigins));
    }
    private long loadedInformationLength, loadedInformationWriteTicks;
    private void RestoreWorkInformation(string path)
    {
        var info = new FileInfo(path); loadedInformationLength = info.Length; loadedInformationWriteTicks = info.LastWriteTimeUtc.Ticks;
        var saved = pendingInformation; pendingInformation = null;
        if (saved is null || !string.Equals(saved.SourcePath, path, StringComparison.OrdinalIgnoreCase) ||
            saved.Length != info.Length || saved.LastWriteUtcTicks != info.LastWriteTimeUtc.Ticks)
        { if (importedLayoutDefaults is { } defaults) ApplyDefaultTypography(defaults); return; }
        // Restoring this exact work never opens RAW. Its absence blocks only re-read.
        confirmedRaw = saved.Raw; informationOrigins.Clear();
        foreach (var field in Enum.GetValues<InformationField>()) informationOrigins[field] = saved.Origins.GetValueOrDefault(field);
        SetInformationValues(saved.Current); NotifyInformationSources();
        if (persisted.WorkLayout is { } work) ApplyComposition(work);
    }
}
