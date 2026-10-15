using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PhotoMargin.App.Services;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;
using PhotoMargin.Imaging;

namespace PhotoMargin.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ImageLoader imageLoader;
    private readonly LayoutEngine layoutEngine;
    private readonly PreviewService previewService;
    private readonly ExportService exportService;
    private readonly SettingsStore settingsStore;
    private readonly SemaphoreSlim renderGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? previewCancellation;
    private LoadedPhoto? photo;
    private LayoutResult? currentLayout;
    private PersistedSettings persisted = new();
    private DisplayMetadata originalDisplay = new("", "", "", "", "", "", "");
    private int renderVersion, sourceVersion, informationVersion;
    private bool applyingValues, disposed;
    private string camera = "", lens = "", focalLength = "", aperture = "", shutter = "", iso = "";
    private string shutterInput = "", inputError = "", captureTimeSource = "尚未載入照片";
    private DateTimeOffset? selectedDate;
    private TimeSpan? selectedTime;
    private int outputSize = 1080;
    private double jpegQuality = 95, photoSideMargin = 48, photoTopMargin = 36, photoInfoGap = 24;
    private double bottomSafeDistance = 72, cropExtraPadding = 24, primaryFontSize = 28, secondaryFontSize = 22;
    private double lensFontSize = 22, dateFontSize = 22;
    private string fontFamily = "Microsoft JhengHei UI", selectedMarginRuleId = MarginRule.DefaultId;
    private bool protectFourByFive = true, protectFiveByFour = true, showGuides = true, isBusy;
    private int themeChoice;
    private string statusText = "", errorText = "", sourceSummary = "尚未載入照片";
    private byte[]? previewBytes;

    public MainViewModel(ImageLoader imageLoader, LayoutEngine layoutEngine, PreviewService previewService,
        ExportService exportService, SettingsStore settingsStore)
    {
        this.imageLoader = imageLoader; this.layoutEngine = layoutEngine; this.previewService = previewService;
        this.exportService = exportService; this.settingsStore = settingsStore;
        RuleEditor = new RuleEditorViewModel(this);
    }
    public ObservableCollection<MarginRule> MarginRules { get; } = [];
    public RuleEditorViewModel RuleEditor { get; }
    public IReadOnlyList<double> FontSizes { get; } = Enumerable.Range(18, 83).Select(value => (double)value).ToArray();
    public IReadOnlyList<string> FontChoices { get; } =
        ["Microsoft JhengHei UI", "Microsoft JhengHei", "Segoe UI", "Arial", "Calibri", "Consolas", "Times New Roman"];
    public string ApplicationIdentity { get; } = $"{Assembly.GetExecutingAssembly().GetName().Name?.Replace(".App", "")} WinUI {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()} Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
    public string Camera { get => camera; set => SetInformation(ref camera, value); }
    public string Lens { get => lens; set => SetInformation(ref lens, value); }
    public string FocalLength { get => focalLength; set { if (SetInformation(ref focalLength, value)) OnPropertyChanged(nameof(FocalLengthValue)); } }
    public string Aperture { get => aperture; set { if (SetInformation(ref aperture, value)) OnPropertyChanged(nameof(ApertureValue)); } }
    public string Iso { get => iso; set { if (SetInformation(ref iso, value)) OnPropertyChanged(nameof(IsoValue)); } }
    public string Shutter
    {
        get => shutter;
        private set
        {
            if (SetInformation(ref shutter, value))
            { shutterInput = value.Trim().TrimEnd('s', 'S').Trim(); OnPropertyChanged(nameof(ShutterInput)); }
        }
    }
    public double FocalLengthValue { get => ReadNumber(FocalLength, "mm"); set => FocalLength = NumberText(value, "mm"); }
    public double ApertureValue { get => ReadNumber(Aperture, "f/"); set => Aperture = NumberText(value, "", "f/"); }
    public double IsoValue { get => ReadNumber(Iso, "ISO"); set => Iso = NumberText(value, "", "ISO ", whole: true); }
    public string ShutterInput
    {
        get => shutterInput;
        set
        {
            value = (value ?? "").Trim();
            if (!SetProperty(ref shutterInput, value)) return;
            if (!applyingValues) informationVersion++;
            var numeric = value.TrimEnd('s', 'S').Trim();
            var parts = numeric.Split('/');
            var valid = numeric.Length == 0 || (parts.Length == 1 && PositiveNumber(parts[0])) ||
                (parts.Length == 2 && PositiveNumber(parts[0]) && PositiveNumber(parts[1]));
            InputError = valid ? "" : "快門請輸入有效秒數或分數，例如 0.8、1/250。";
            if (valid) Shutter = numeric.Length == 0 ? "" : numeric + "s";
        }
    }
    public string InputError { get => inputError; private set { if (SetProperty(ref inputError, value)) { OnPropertyChanged(nameof(CanExport)); OnPropertyChanged(nameof(PreviewCaption)); } } }
    public DateTimeOffset? SelectedDate { get => selectedDate; set { if (SetProperty(ref selectedDate, value)) InformationChanged(); } }
    public TimeSpan? SelectedTime { get => selectedTime; set { if (SetProperty(ref selectedTime, value)) InformationChanged(); } }
    public string CaptureTimeSource { get => captureTimeSource; private set => SetProperty(ref captureTimeSource, value); }
    public int OutputSize { get => outputSize; set => SetLayout(ref outputSize, Math.Clamp(value, 320, 8192)); }
    public double JpegQuality { get => jpegQuality; set => SetLayout(ref jpegQuality, Finite(value, 95, 50, 100)); }
    public double PrimaryFontSize { get => primaryFontSize; set => SetLayout(ref primaryFontSize, Finite(value, 28, 18, 100)); }
    public double LensFontSize { get => lensFontSize; set => SetLayout(ref lensFontSize, Finite(value, 22, 18, 100)); }
    public double SecondaryFontSize { get => secondaryFontSize; set => SetLayout(ref secondaryFontSize, Finite(value, 22, 18, 100)); }
    public double DateFontSize { get => dateFontSize; set => SetLayout(ref dateFontSize, Finite(value, 22, 18, 100)); }
    public string FontFamily { get => fontFamily; set => SetLayout(ref fontFamily, string.IsNullOrWhiteSpace(value) ? "Microsoft JhengHei UI" : value.Trim()); }
    public double PhotoSideMargin => photoSideMargin;
    public double PhotoTopMargin => photoTopMargin;
    public double PhotoInfoGap => photoInfoGap;
    public double BottomSafeDistance => bottomSafeDistance;
    public double CropExtraPadding => cropExtraPadding;
    public bool ProtectFourByFive => protectFourByFive;
    public bool ProtectFiveByFour => protectFiveByFour;
    public bool ShowGuides { get => showGuides; set => SetLayout(ref showGuides, value); }
    public string SelectedMarginRuleId
    {
        get => selectedMarginRuleId;
        set
        {
            var rule = MarginRules.FirstOrDefault(item => item.Id == value);
            if (rule is null || !SetProperty(ref selectedMarginRuleId, value)) return;
            ApplyRule(rule);
            if (!applyingValues) { RequestPreview(); _ = SaveSettingsQuietlyAsync(); }
        }
    }
    public int ThemeChoice
    {
        get => themeChoice;
        set { if (SetProperty(ref themeChoice, Math.Clamp(value, 0, 2)) && !applyingValues) _ = SaveSettingsQuietlyAsync(); }
    }
    public bool IsBusy
    {
        get => isBusy;
        private set { if (SetProperty(ref isBusy, value)) { OnPropertyChanged(nameof(CanExport)); OnPropertyChanged(nameof(CanReload)); OnPropertyChanged(nameof(CanSelectPhoto)); } }
    }
    public bool CanSelectPhoto => !IsBusy;
    public bool CanReload => HasPhoto && !IsBusy;
    public bool CanExport => HasPhoto && currentLayout?.IsValid is true && !IsBusy && InputError.Length == 0;
    public bool HasPhoto => photo is not null;
    public bool HasInformationOverrides => GetDisplayMetadata() != originalDisplay || InputError.Length > 0;
    public string? SourcePath => photo?.SourcePath;
    public string SourceFileName => photo is null ? "尚未匯入照片" : Path.GetFileName(photo.SourcePath);
    public string SourceFileSizeText { get; private set; } = "";
    public string SourceSummary { get => sourceSummary; private set { if (SetProperty(ref sourceSummary, value)) OnPropertyChanged(nameof(PreviewCaption)); } }
    public string StatusText { get => statusText; private set { if (SetProperty(ref statusText, value)) OnPropertyChanged(nameof(PreviewCaption)); } }
    public string ErrorText { get => errorText; private set { if (SetProperty(ref errorText, value)) { OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(PreviewCaption)); } } }
    public bool HasError => ErrorText.Length > 0;
    public string PreviewCaption => string.Join(" · ", new[] { SourceSummary, InputError.Length > 0 ? InputError : ErrorText.Length > 0 ? ErrorText : StatusText }.Where(text => text.Length > 0));
    public byte[]? PreviewBytes { get => previewBytes; private set => SetProperty(ref previewBytes, value); }

    public async Task InitializeAsync()
    {
        persisted = await settingsStore.LoadAsync();
        var layout = persisted.Layout.Validate(); applyingValues = true;
        OutputSize = layout.OutputSize; JpegQuality = layout.JpegQuality;
        PrimaryFontSize = layout.PrimaryFontSize; SecondaryFontSize = layout.SecondaryFontSize;
        LensFontSize = layout.LensFontSize; DateFontSize = layout.DateFontSize; FontFamily = layout.FontFamily;
        ShowGuides = persisted.ShowGuides; ThemeChoice = (int)persisted.Theme;
        foreach (var rule in persisted.MarginRules) MarginRules.Add(rule.Validate());
        if (!MarginRules.Any(rule => rule.Id == MarginRule.DefaultId)) MarginRules.Insert(0, MarginRule.FromLayout(layout));
        selectedMarginRuleId = MarginRules.Any(rule => rule.Id == persisted.SelectedMarginRuleId) ? persisted.SelectedMarginRuleId : MarginRule.DefaultId;
        ApplyRule(MarginRules.First(rule => rule.Id == selectedMarginRuleId));
        OnPropertyChanged(nameof(SelectedMarginRuleId)); applyingValues = false; RuleEditor.Begin(selectedMarginRuleId);
    }
    public async Task LoadPhotoAsync(string path)
    {
        if (IsBusy || disposed) return;
        IsBusy = true; ErrorText = ""; StatusText = "正在讀取照片與 EXIF…";
        LoadedPhoto? candidate = null;
        try
        {
            candidate = await imageLoader.LoadAsync(path, lifetime.Token);
            if (candidate.Orientation == SourceOrientation.Portrait)
            { ErrorText = OrientationPolicy.PortraitUnsupportedMessage; StatusText = ""; return; }
            var captured = ReadCaptureTime(candidate.SourcePath, candidate.Metadata);
            var bytes = new FileInfo(candidate.SourcePath).Length;
            CancelPreview(); await renderGate.WaitAsync(lifetime.Token);
            try
            {
                if (disposed) return;
                photo?.Dispose(); photo = candidate; candidate = null;
                sourceVersion++; currentLayout = null; PreviewBytes = null;
                ApplyPhotoInformation(photo.Metadata, captured); UpdateSourceSummary(photo.Metadata);
                SourceFileSizeText = bytes >= 1048576 ? $"{bytes / 1048576d:0.##} MB" : $"{bytes / 1024d:0.#} KB";
                foreach (var property in new[] { nameof(HasPhoto), nameof(SourcePath), nameof(SourceFileName), nameof(SourceFileSizeText), nameof(CanReload), nameof(CanExport) }) OnPropertyChanged(property);
            }
            finally { renderGate.Release(); }
            StatusText = captured.Fallback ? "拍攝時間使用檔案建立日期" : ""; RequestPreview(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
        { if (!disposed) { ErrorText = exception.Message; StatusText = photo is null ? "匯入失敗" : "保留目前作品"; } }
        finally { candidate?.Dispose(); if (!disposed) IsBusy = false; }
    }
    public async Task<bool> ReloadExifAsync(Func<Task<bool>>? confirmOverwrite = null)
    {
        if (!CanReload || disposed) return false;
        var path = photo!.SourcePath; var source = sourceVersion; IsBusy = true;
        try
        {
            if (HasInformationOverrides && (confirmOverwrite is null || !await confirmOverwrite())) return false;
            var informationRevision = informationVersion;
            var result = await Task.Run(() =>
            {
                lifetime.Token.ThrowIfCancellationRequested();
                var read = new ExifReader().Read(path);
                return (Metadata: read, Captured: ReadCaptureTime(path, read));
            }, lifetime.Token);
            if (disposed || source != sourceVersion) return false;
            if (informationRevision != informationVersion)
            { StatusText = "讀取期間資訊已變更，保留目前編輯；請再次重新讀取。"; return false; }
            ApplyPhotoInformation(result.Metadata, result.Captured); UpdateSourceSummary(result.Metadata); ErrorText = "";
            StatusText = result.Metadata == new PhotoMetadata() ? "缺值留白；拍攝時間使用檔案建立日期" :
                result.Captured.Fallback ? "照片資訊已更新；拍攝時間使用檔案建立日期" : "照片資訊已更新";
            RequestPreview(true); return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception)
        { if (!disposed) { ErrorText = $"重新讀取失敗，保留原有資訊：{exception.Message}"; StatusText = ""; } return false; }
        finally { if (!disposed) IsBusy = false; }
    }
    public async Task ExportAsync(string destinationPath)
    {
        if (!CanExport || disposed) return;
        var layout = currentLayout!; var quality = (int)Math.Round(JpegQuality);
        IsBusy = true; ErrorText = ""; StatusText = "正在匯出 JPG…";
        try
        {
            await renderGate.WaitAsync(lifetime.Token);
            try { await exportService.ExportJpegAsync(photo!, layout, destinationPath, quality, lifetime.Token); }
            finally { renderGate.Release(); }
            persisted = BuildPersisted() with { LastOutputDirectory = Path.GetDirectoryName(destinationPath) };
            await settingsStore.SaveAsync(persisted, lifetime.Token);
            StatusText = $"已匯出：{Path.GetFileName(destinationPath)}";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!disposed) { ErrorText = exception.Message; StatusText = "匯出失敗"; } }
        finally { if (!disposed) IsBusy = false; }
    }
    internal void SaveRule(MarginRule rule)
    {
        rule = rule.Validate();
        var index = MarginRules.ToList().FindIndex(item => item.Id == rule.Id);
        if (index >= 0) MarginRules[index] = rule; else MarginRules.Add(rule);
        if (selectedMarginRuleId == rule.Id) { ApplyRule(rule); RequestPreview(true); }
        OnPropertyChanged(nameof(SelectedMarginRuleId)); _ = SaveSettingsQuietlyAsync();
    }
    internal void DeleteRule(string id)
    {
        var rule = MarginRules.FirstOrDefault(item => item.Id == id);
        if (rule is null || id == MarginRule.DefaultId) return;
        MarginRules.Remove(rule);
        if (selectedMarginRuleId == id) SelectedMarginRuleId = MarginRule.DefaultId;
        OnPropertyChanged(nameof(SelectedMarginRuleId)); _ = SaveSettingsQuietlyAsync();
    }
    internal Task FlushSettingsAsync() => settingsStore.SaveAsync(BuildPersisted());
    private void ApplyRule(MarginRule rule)
    {
        photoSideMargin = rule.Side; photoTopMargin = rule.Top; photoInfoGap = rule.Gap;
        bottomSafeDistance = rule.Bottom; cropExtraPadding = rule.Extra;
        protectFourByFive = rule.ProtectPortrait; protectFiveByFour = rule.ProtectLandscape;
        foreach (var property in new[] { nameof(PhotoSideMargin), nameof(PhotoTopMargin), nameof(PhotoInfoGap), nameof(BottomSafeDistance), nameof(CropExtraPadding), nameof(ProtectFourByFive), nameof(ProtectFiveByFour) }) OnPropertyChanged(property);
    }
    private bool SetInformation(ref string field, string value, [CallerMemberName] string? name = null)
    { if (!SetProperty(ref field, value ?? "", name)) return false; InformationChanged(); return true; }
    private void InformationChanged()
    { if (!applyingValues) { informationVersion++; OnPropertyChanged(nameof(HasInformationOverrides)); RequestPreview(); } }
    private void SetLayout<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (SetProperty(ref field, value, name) && !applyingValues) { RequestPreview(); _ = SaveSettingsQuietlyAsync(); } }
    private void RequestPreview(bool immediate = false)
    {
        if (photo is null || disposed) return;
        currentLayout = null; OnPropertyChanged(nameof(CanExport)); CancelPreview();
        previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var version = Interlocked.Increment(ref renderVersion);
        _ = UpdatePreviewAsync(version, previewCancellation.Token, immediate);
    }
    private async Task UpdatePreviewAsync(int version, CancellationToken token, bool immediate)
    {
        try
        {
            if (!immediate) await Task.Delay(160, token);
            await renderGate.WaitAsync(token);
            try
            {
                if (photo is null || disposed) return;
                var layout = layoutEngine.Calculate(photo.OrientedSize, GetDisplayMetadata(), BuildLayoutSettings());
                if (!layout.IsValid)
                {
                    if (version == renderVersion) { ErrorText = string.Join("；", layout.Errors); StatusText = "安全檢查失敗，無法匯出"; }
                    return;
                }
                var bytes = await previewService.RenderAsync(photo, layout, ShowGuides, token);
                if (disposed || version != renderVersion || token.IsCancellationRequested) return;
                currentLayout = layout; PreviewBytes = bytes; ErrorText = "";
                if (StatusText.StartsWith("正在", StringComparison.Ordinal)) StatusText = "";
                OnPropertyChanged(nameof(CanExport));
            }
            finally { renderGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!disposed && version == renderVersion) ErrorText = $"預覽失敗：{exception.Message}"; }
    }
    private LayoutSettings BuildLayoutSettings() => new LayoutSettings
    {
        OutputSize = OutputSize, JpegQuality = (int)Math.Round(JpegQuality), FontFamily = FontFamily,
        PhotoSideMargin = photoSideMargin, PhotoTopMargin = photoTopMargin, PhotoInfoGap = photoInfoGap,
        BottomSafeDistance = bottomSafeDistance, CropExtraPadding = cropExtraPadding,
        PrimaryFontSize = PrimaryFontSize, LensFontSize = LensFontSize, SecondaryFontSize = SecondaryFontSize, DateFontSize = DateFontSize,
        ProtectFourByFive = protectFourByFive, ProtectFiveByFour = protectFiveByFour,
        Visibility = new FieldVisibility { Camera = true, Lens = true, Exposure = true, Date = true }
    }.Validate();
    private DisplayMetadata GetDisplayMetadata() => new(Camera.Trim(), Lens.Trim(), FocalLength.Trim(), Aperture.Trim(), Shutter.Trim(), Iso.Trim(),
        SelectedDate is { } date ? date.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) + (SelectedTime is { } time ? $" {time.Hours:00}:{time.Minutes:00}" : "") : "");
    private static (DateTimeOffset Time, bool Fallback, string Source) ReadCaptureTime(string path, PhotoMetadata metadata) =>
        metadata.CapturedAt is { } captured ? (captured, false, metadata.DateSource ?? "EXIF") : (new DateTimeOffset(File.GetCreationTime(path)), true, "檔案建立日期");
    private void ApplyPhotoInformation(PhotoMetadata metadata, (DateTimeOffset Time, bool Fallback, string Source) capture)
    {
        applyingValues = true; Camera = metadata.CameraDisplay; Lens = metadata.LensModel ?? "";
        FocalLength = metadata.FocalLengthDisplay; Aperture = metadata.ApertureDisplay; Shutter = metadata.ShutterDisplay; Iso = metadata.IsoDisplay;
        shutterInput = shutter.TrimEnd('s', 'S').Trim(); OnPropertyChanged(nameof(ShutterInput)); InputError = "";
        SelectedDate = capture.Time; SelectedTime = new TimeSpan(capture.Time.Hour, capture.Time.Minute, 0);
        CaptureTimeSource = capture.Source; originalDisplay = GetDisplayMetadata(); applyingValues = false;
        OnPropertyChanged(nameof(HasInformationOverrides));
    }
    private void UpdateSourceSummary(PhotoMetadata metadata) => SourceSummary = photo is null ? "尚未載入照片" :
        $"{photo.OrientedSize.Width:0} × {photo.OrientedSize.Height:0} · {(photo.Orientation == SourceOrientation.Landscape ? "橫向" : "方形")} · {(metadata == new PhotoMetadata() ? "無 EXIF" : "EXIF 已載入")}";
    private PersistedSettings BuildPersisted() => new()
    {
        Layout = BuildLayoutSettings(), ShowGuides = ShowGuides, Theme = (ThemePreference)ThemeChoice,
        LastOutputDirectory = persisted.LastOutputDirectory, MarginRules = MarginRules.ToList(), SelectedMarginRuleId = SelectedMarginRuleId
    };
    private async Task SaveSettingsQuietlyAsync()
    {
        try { persisted = BuildPersisted(); await settingsStore.SaveAsync(persisted, lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { if (!disposed) ErrorText = $"無法儲存設定：{exception.Message}"; }
    }
    private void CancelPreview() { previewCancellation?.Cancel(); previewCancellation?.Dispose(); previewCancellation = null; }
    private static double Finite(double value, double fallback, double min, double max) => Math.Clamp(double.IsFinite(value) ? value : fallback, min, max);
    private static bool PositiveNumber(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number > 0;
    private static double ReadNumber(string value, string decoration) => double.TryParse(value.Replace(decoration, "", StringComparison.OrdinalIgnoreCase).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : double.NaN;
    private static string NumberText(double value, string suffix, string prefix = "", bool whole = false) =>
        double.IsFinite(value) && value > 0 ? prefix + (whole ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.###", CultureInfo.InvariantCulture)) + suffix : "";
    public void Dispose()
    { if (disposed) return; disposed = true; lifetime.Cancel(); CancelPreview(); _ = DisposePhotoAsync(); }
    private async Task DisposePhotoAsync()
    { await renderGate.WaitAsync().ConfigureAwait(false); try { photo?.Dispose(); photo = null; } finally { renderGate.Release(); } }
}
