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

public sealed record ArrangementChoice(InformationArrangement Value, string Name);

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ImageLoader imageLoader;
    private readonly LayoutEngine layoutEngine;
    private readonly PreviewService previewService;
    private readonly ExportService exportService;
    private readonly SettingsStore settingsStore;
    private readonly SemaphoreSlim renderGate = new(1, 1);
    private readonly SemaphoreSlim preferenceGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? previewCancellation;
    private LoadedPhoto? photo;
    private LayoutResult? currentLayout;
    private PersistedSettings persisted = new();
    private string? lastOutputDirectory;
    private readonly SemaphoreSlim demoGate = new(1, 1);
    private LoadedPhoto? demoSource;
    private int demoSourceVersion = -1;
    private SourceAspect demonstrationAspect = SourceAspect.FourByThree;
    private InformationArrangement demonstrationArrangement = InformationArrangement.TwoColumns;
    internal SourceAspect WorkAspect => photo is null ? SourceAspect.FourByThree : SourceAspects.Closest(photo.OrientedSize);
    internal SourceAspect DemonstrationAspect => demonstrationAspect;
    internal InformationArrangement DemonstrationArrangement => demonstrationArrangement;
    internal int SourceRevision => sourceVersion;
    internal void SetDemonstrationAspect(SourceAspect value)
    { demonstrationAspect = value; _ = SaveSettingsQuietlyAsync(); }
    internal void SetDemonstrationArrangement(InformationArrangement value)
    { demonstrationArrangement = value; _ = SaveSettingsQuietlyAsync(); }
    private DisplayMetadata originalDisplay = new("", "", "", "", "", "", "");
    private int renderVersion, sourceVersion, informationVersion;
    private bool applyingValues, disposed;
    private long saveRevision;
    private bool settingsLoaded, settingsLoadFailed;
    public bool IsSettingsReadOnly => !settingsLoaded;
    public bool HasSettingsLoadFailure => settingsLoadFailed;
    private string previewError = "", persistenceError = "";
    private string arrangementWarning = "";
    public string ArrangementWarning => arrangementWarning;
    private void SetArrangementWarning(string value)
    {
        if (SetProperty(ref arrangementWarning, value, nameof(ArrangementWarning))) OnPropertyChanged(nameof(PreviewCaption));
    }
    private string camera = "", lens = "", focalLength = "", aperture = "", shutter = "", iso = "";
    private string shutterInput = "", inputError = "", captureTimeSource = "尚未載入照片";
    private DateTimeOffset? selectedDate;
    private TimeSpan? selectedTime;
    private int outputSize = 1080;
    private double jpegQuality = 95, photoSideMargin = 48, photoTopMargin = 36, photoInfoGap = 24;
    private double exposureItemGap = 18;
    private double bottomSafeDistance = 72, cropExtraPadding = 24, primaryFontSize = 28, secondaryFontSize = 22;
    private double lensFontSize = 16, dateFontSize = 22;
    private InformationArrangement arrangement = InformationArrangement.Auto;
    private string fontFamily = "Microsoft JhengHei UI";
    private string? selectedMarginRuleId = MarginRule.DefaultId;
    private bool protectFourByFive = true, protectFiveByFour, showGuides = true, isBusy;
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
    public ObservableCollection<double> LensFontSizes { get; } = new(Enumerable.Range(8, 93).Select(value => (double)value));
    public IReadOnlyList<string> FontChoices { get; } =
        ["Microsoft JhengHei UI", "Microsoft JhengHei", "Segoe UI", "Arial", "Calibri", "Consolas", "Times New Roman"];
    public IReadOnlyList<ArrangementChoice> ArrangementChoices { get; } =
        [new(InformationArrangement.Auto, "自動（預設）"), new(InformationArrangement.TwoColumns, "左右分欄"), new(InformationArrangement.Stacked, "置中堆疊")];
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
    public double LensFontSize
    {
        get => lensFontSize;
        set
        {
            var valid = Finite(value, 16, 8, 100);
            // Keep editable ComboBox text/selection consistent for fractional
            // values too, including values restored from existing preferences.
            if (!LensFontSizes.Contains(valid))
            {
                var index = 0; while (index < LensFontSizes.Count && LensFontSizes[index] < valid) index++;
                LensFontSizes.Insert(index, valid);
            }
            SetLayout(ref lensFontSize, valid);
        }
    }
    public bool TrySetLensFontSizeText(string text)
    {
        if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
             !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) || !double.IsFinite(value)) return false;
        LensFontSize = value; return true;
    }
    public double SecondaryFontSize { get => secondaryFontSize; set => SetLayout(ref secondaryFontSize, Finite(value, 22, 18, 100)); }
    public double DateFontSize { get => dateFontSize; set => SetLayout(ref dateFontSize, Finite(value, 22, 18, 100)); }
    public string FontFamily { get => fontFamily; set => SetLayout(ref fontFamily, string.IsNullOrWhiteSpace(value) ? "Microsoft JhengHei UI" : value.Trim()); }
    public InformationArrangement Arrangement
    {
        get => arrangement;
        set
        {
            var valid = Enum.IsDefined(value) ? value : InformationArrangement.Auto;
            if (arrangement == valid) return;
            SetArrangementWarning("");
            if (!applyingValues && MarginRules.FirstOrDefault(rule => rule.Id == selectedMarginRuleId) is { } preset)
                ApplyRule(preset.ForAspect(WorkAspect, valid).ToRule());
            SetLayout(ref arrangement, valid);
            OnPropertyChanged(nameof(SelectedArrangement));
        }
    }
    public ArrangementChoice? SelectedArrangement
    {
        get => ArrangementChoices.First(choice => choice.Value == Arrangement);
        set { if (value is not null) Arrangement = value.Value; }
    }
    public double PhotoSideMargin => photoSideMargin;
    public double PhotoTopMargin => photoTopMargin;
    public double PhotoInfoGap => photoInfoGap;
    public double ExposureItemGap => exposureItemGap;
    public double BottomSafeDistance => bottomSafeDistance;
    public double CropExtraPadding => cropExtraPadding;
    public bool ProtectFourByFive => protectFourByFive;
    public bool ProtectFiveByFour => protectFiveByFour;
    public bool ShowGuides { get => showGuides; set => SetLayout(ref showGuides, value); }
    public string? SelectedMarginRuleId
    {
        get => selectedMarginRuleId;
        set
        {
            if (RuleEditor.IsSaving) return;
            if (value is null && MarginRules.Count > 0) return;
            var rule = MarginRules.FirstOrDefault(item => item.Id == value);
            if (value is not null && rule is null) return;
            if (!SetProperty(ref selectedMarginRuleId, value)) return;
            if (rule is not null) ApplyRule(rule.ForAspect(WorkAspect, Arrangement).ToRule());
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
    public string ErrorText { get => string.Join("；", new[] { previewError, errorText, persistenceError }.Where(value => value.Length > 0)); private set { errorText = value; NotifyErrors(); } }
    public string PersistenceError => persistenceError;
    private void NotifyErrors()
    { OnPropertyChanged(nameof(ErrorText)); OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(PreviewCaption)); }
    private void SetPreviewError(string value) { previewError = value; NotifyErrors(); }
    private void SetPersistenceError(string value) { persistenceError = value; OnPropertyChanged(nameof(PersistenceError)); NotifyErrors(); }
    public bool HasError => ErrorText.Length > 0;
    public string PreviewCaption => string.Join(" · ", new[]
    {
        SourceSummary, InputError.Length > 0 ? InputError : ErrorText,
        ArrangementWarning, StatusText
    }.Where(text => text.Length > 0));
    public byte[]? PreviewBytes { get => previewBytes; private set => SetProperty(ref previewBytes, value); }

    public async Task InitializeAsync()
    {
        await RetryLoadSettingsAsync();
    }
    public async Task<bool> RetryLoadSettingsAsync()
    {
        await preferenceGate.WaitAsync();
        try
        {
            settingsLoaded = false;
            OnPropertyChanged(nameof(IsSettingsReadOnly)); RuleEditor.RefreshSettingsAccess();
            PersistedSettings loaded;
            try { loaded = await settingsStore.LoadAsync(); }
            catch (SettingsLoadException exception)
            {
                settingsLoadFailed = true; OnPropertyChanged(nameof(HasSettingsLoadFailure));
                SetPersistenceError(exception.Message); return false;
            }
            persisted = loaded;
            lastOutputDirectory = persisted.LastOutputDirectory;
            demonstrationAspect = persisted.DemonstrationAspect;
            demonstrationArrangement = persisted.DemonstrationArrangement ?? InformationArrangement.TwoColumns;
            var layout = persisted.Layout.Validate(); applyingValues = true;
            OutputSize = layout.OutputSize; JpegQuality = layout.JpegQuality;
            PrimaryFontSize = layout.PrimaryFontSize; SecondaryFontSize = layout.SecondaryFontSize;
            LensFontSize = layout.LensFontSize; DateFontSize = layout.DateFontSize; FontFamily = layout.FontFamily;
            Arrangement = layout.Arrangement;
            ShowGuides = persisted.ShowGuides; ThemeChoice = (int)persisted.Theme;
            MarginRules.Clear();
            foreach (var rule in persisted.MarginRules) MarginRules.Add(rule.Validate() with { });
            selectedMarginRuleId = MarginRules.Any(rule => rule.Id == persisted.SelectedMarginRuleId) ? persisted.SelectedMarginRuleId : MarginRules.FirstOrDefault()?.Id;
            // Work is a persisted value snapshot, not a live link back to a selected preset.
            ApplyRule(MarginRule.FromLayout(layout));
            settingsLoaded = true; settingsLoadFailed = false;
            OnPropertyChanged(nameof(IsSettingsReadOnly)); OnPropertyChanged(nameof(HasSettingsLoadFailure));
            SetPersistenceError("");
            OnPropertyChanged(nameof(SelectedMarginRuleId)); applyingValues = false; RuleEditor.Begin(selectedMarginRuleId);
            RequestPreview(); return true;
        }
        finally { applyingValues = false; preferenceGate.Release(); }
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
                var previousAspect = WorkAspect;
                var selectedPreset = MarginRules.FirstOrDefault(rule => rule.Id == selectedMarginRuleId);
                var workIsUnmodifiedPresetCopy = selectedPreset is not null &&
                    MarginConfiguration.From(WorkingMargins) == selectedPreset.ForAspect(previousAspect, Arrangement);
                photo?.Dispose(); photo = candidate; candidate = null;
                if (workIsUnmodifiedPresetCopy)
                    ApplyRule(selectedPreset!.ForAspect(WorkAspect, Arrangement).ToRule());
                sourceVersion++; currentLayout = null; PreviewBytes = null; SetPreviewError(""); SetArrangementWarning("");
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
            StatusText = $"已匯出：{Path.GetFileName(destinationPath)}";
            lastOutputDirectory = Path.GetDirectoryName(destinationPath);
            // The image is already committed. Preference failure is a separate warning,
            // never a failed export or a reason to retry writing the same image.
            await SaveSettingsQuietlyAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!disposed) { ErrorText = exception.Message; StatusText = "匯出失敗"; } }
        finally { if (!disposed) IsBusy = false; }
    }
    internal async Task<bool> SaveRuleAsync(MarginRule rule, MarginRule? expectedOriginal)
    {
        await preferenceGate.WaitAsync();
        try
        {
            if (!RuleEditor.AuthorizesSave(rule.Id, expectedOriginal)) return false;
            var index = MarginRules.ToList().FindIndex(item => item.Id == rule.Id);
            if ((expectedOriginal is null && index >= 0) || (expectedOriginal is not null && (index < 0 || MarginRules[index] != expectedOriginal))) return false;
            if (MarginRules.Any(item => item.Id != rule.Id && string.Equals(item.Name, rule.Name, StringComparison.OrdinalIgnoreCase))) return false;
            var rules = MarginRules.Select(item => item with { }).ToList();
            if (index >= 0) rules[index] = rule.Validate(); else rules.Add(rule.Validate());
            var snapshot = BuildPersisted() with { MarginRules = rules, SelectedMarginRuleId = rule.Id };
            if (!await WriteSettingsSnapshotAsync(snapshot, ++saveRevision)) return false;
            selectedMarginRuleId = rule.Id;
            if (index >= 0) MarginRules[index] = rule.Validate(); else MarginRules.Add(rule.Validate());
            OnPropertyChanged(nameof(SelectedMarginRuleId));
            return true;
        }
        finally { preferenceGate.Release(); }
    }
    internal async Task<bool> DeleteRuleAsync(string id)
    {
        await preferenceGate.WaitAsync();
        try
        {
            var rule = MarginRules.FirstOrDefault(item => item.Id == id);
            if (rule is null || id == MarginRule.DefaultId || !RuleEditor.AuthorizesDeletion(rule)) return false;
            var rules = MarginRules.Where(item => item.Id != id).Select(item => item with { }).ToList();
            var selected = selectedMarginRuleId == id ? rules.FirstOrDefault()?.Id : selectedMarginRuleId;
            var snapshot = BuildPersisted() with { MarginRules = rules, SelectedMarginRuleId = selected };
            if (!await WriteSettingsSnapshotAsync(snapshot, ++saveRevision)) return false;
            // Commit visible state only after disk success; do not apply the fallback preset.
            selectedMarginRuleId = selected;
            MarginRules.Remove(rule);
            OnPropertyChanged(nameof(SelectedMarginRuleId));
            return true;
        }
        finally { preferenceGate.Release(); }
    }
    internal async Task FlushSettingsAsync()
    {
        if (!await SaveSettingsQuietlyAsync()) throw new IOException(PersistenceError);
    }
    private void ApplyRule(MarginRule rule)
    {
        photoSideMargin = rule.Side; photoTopMargin = rule.Top; photoInfoGap = rule.Gap; exposureItemGap = rule.ExposureItemGap;
        bottomSafeDistance = rule.Bottom; cropExtraPadding = rule.Extra;
        protectFourByFive = rule.ProtectPortrait; protectFiveByFour = rule.ProtectLandscape;
        foreach (var property in new[] { nameof(PhotoSideMargin), nameof(PhotoTopMargin), nameof(PhotoInfoGap), nameof(ExposureItemGap), nameof(BottomSafeDistance), nameof(CropExtraPadding), nameof(ProtectFourByFive), nameof(ProtectFiveByFour) }) OnPropertyChanged(property);
    }
    internal MarginRule WorkingMargins => MarginRule.FromLayout(BuildLayoutSettings());
    internal void ApplyWorkingMargins(MarginRule snapshot)
    {
        ApplyRule(snapshot.ValidateValues()); RequestPreview(); _ = SaveSettingsQuietlyAsync();
    }
    internal void ApplyWorkingConfiguration(MarginConfiguration configuration, InformationArrangement selectedArrangement)
    {
        arrangement = selectedArrangement; SetArrangementWarning("");
        OnPropertyChanged(nameof(Arrangement)); OnPropertyChanged(nameof(SelectedArrangement));
        ApplyWorkingMargins(configuration.ToRule());
    }
    internal void ApplyPresetToWork(MarginRule preset)
    {
        selectedMarginRuleId = preset.Id; OnPropertyChanged(nameof(SelectedMarginRuleId));
        ApplyWorkingMargins(preset.ForAspect(WorkAspect, Arrangement).ToRule());
    }
    internal async Task<(byte[] Bytes, LayoutResult Layout, SizeD SourceSize)> RenderRuleDemoAsync(
        SourceAspect aspect, MarginConfiguration configuration, CancellationToken token,
        InformationArrangement? selectedArrangement = null)
    {
        // Capture all mutable UI state before awaiting; serialize only our preview copies.
        var settings = configuration.ApplyTo(BuildLayoutSettings()) with { OutputSize = 720, Arrangement = selectedArrangement ?? Arrangement };
        var metadata = HasPhoto ? GetDisplayMetadata() : LayoutPreviewSource.DemoMetadata;
        await demoGate.WaitAsync(token);
        try
        {
            await renderGate.WaitAsync(token);
            try
            {
                if (demoSourceVersion != sourceVersion)
                {
                    var copy = await Task.Run(() => LayoutPreviewSource.Create(photo), token);
                    demoSource?.Dispose(); demoSource = copy; demoSourceVersion = sourceVersion;
                }
            }
            finally { renderGate.Release(); }
            using var cropped = await Task.Run(() => LayoutPreviewSource.Crop(demoSource!, aspect), token);
            var layout = await Task.Run(() => layoutEngine.Calculate(cropped.OrientedSize, metadata, settings), token);
            if (!layout.IsValid) throw new InvalidOperationException("此配置無法通過安全檢查：" + string.Join("；", layout.Errors));
            var bytes = await previewService.RenderAsync(cropped, layout, false, token);
            return (bytes, layout, cropped.OrientedSize);
        }
        finally { demoGate.Release(); }
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
                var sourceSize = photo.OrientedSize;
                var metadata = GetDisplayMetadata(); var settings = BuildLayoutSettings();
                var layout = await Task.Run(() => layoutEngine.Calculate(sourceSize, metadata, settings), token);
                if (disposed || version != renderVersion || token.IsCancellationRequested) return;
                if (!layout.IsValid)
                {
                    if (!disposed && version == renderVersion && !token.IsCancellationRequested)
                    {
                        ApplyColumnsFallback(layout);
                        SetPreviewError("安全檢查失敗，無法匯出：" + string.Join("；", layout.Errors));
                    }
                    return;
                }
                var bytes = await previewService.RenderAsync(photo, layout, ShowGuides, token);
                if (disposed || version != renderVersion || token.IsCancellationRequested) return;
                ApplyColumnsFallback(layout);
                currentLayout = layout; PreviewBytes = bytes; SetPreviewError("");
                OnPropertyChanged(nameof(CanExport));
            }
            finally { renderGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!disposed && version == renderVersion) SetPreviewError($"預覽失敗：{exception.Message}"); }
    }
    private void ApplyColumnsFallback(LayoutResult layout)
    {
        if (!layout.FellBackFromColumns || arrangement != InformationArrangement.TwoColumns) return;
        // This result already contains a fully revalidated centered layout. Do not
        // invoke the public setter here: it would cancel the result being accepted.
        arrangement = InformationArrangement.Stacked;
        SetArrangementWarning(LayoutEngine.ColumnsFallbackWarning);
        OnPropertyChanged(nameof(Arrangement)); OnPropertyChanged(nameof(SelectedArrangement));
        _ = SaveSettingsQuietlyAsync();
    }
    private LayoutSettings BuildLayoutSettings() => new LayoutSettings
    {
        OutputSize = OutputSize, JpegQuality = (int)Math.Round(JpegQuality), FontFamily = FontFamily,
        Arrangement = Arrangement,
        PhotoSideMargin = photoSideMargin, PhotoTopMargin = photoTopMargin,
        PhotoInfoGap = photoInfoGap, ExposureItemGap = exposureItemGap, BottomSafeDistance = bottomSafeDistance, CropExtraPadding = cropExtraPadding,
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
        LastOutputDirectory = lastOutputDirectory, MarginRules = MarginRules.Select(rule => rule with { }).ToList(),
        SelectedMarginRuleId = SelectedMarginRuleId, DemonstrationAspect = demonstrationAspect, DemonstrationArrangement = demonstrationArrangement
    };
    private async Task<bool> SaveSettingsQuietlyAsync()
    {
        await preferenceGate.WaitAsync();
        try { return await WriteSettingsSnapshotAsync(BuildPersisted(), ++saveRevision); }
        finally { preferenceGate.Release(); }
    }
    private async Task<bool> WriteSettingsSnapshotAsync(PersistedSettings snapshot, long revision)
    {
        // Neither auto-save, preset commands nor close-time flush may serialize
        // temporary defaults over a document that has not been successfully read.
        if (!settingsLoaded) return false;
        try
        {
            // Rendering cancellation must not cancel a user's already queued preferences.
            await settingsStore.SaveAsync(snapshot);
            persisted = snapshot;
            if (!disposed && revision == saveRevision) SetPersistenceError("");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { if (!disposed && revision == saveRevision) SetPersistenceError($"無法儲存設定：{exception.Message}"); return false; }
    }
    private void CancelPreview() { previewCancellation?.Cancel(); previewCancellation?.Dispose(); previewCancellation = null; }
    private static double Finite(double value, double fallback, double min, double max) => Math.Clamp(double.IsFinite(value) ? value : fallback, min, max);
    private static bool PositiveNumber(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number > 0;
    private static double ReadNumber(string value, string decoration) => double.TryParse(value.Replace(decoration, "", StringComparison.OrdinalIgnoreCase).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : double.NaN;
    private static string NumberText(double value, string suffix, string prefix = "", bool whole = false) =>
        double.IsFinite(value) && value > 0 ? prefix + (whole ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.###", CultureInfo.InvariantCulture)) + suffix : "";
    public void Dispose()
    { if (disposed) return; disposed = true; lifetime.Cancel(); CancelPreview(); RuleEditor.StopPreview(); _ = DisposePhotoAsync(); _ = DisposeDemoAsync(); }
    private async Task DisposeDemoAsync()
    { await demoGate.WaitAsync().ConfigureAwait(false); try { demoSource?.Dispose(); demoSource = null; } finally { demoGate.Release(); } }
    private async Task DisposePhotoAsync()
    { await renderGate.WaitAsync().ConfigureAwait(false); try { photo?.Dispose(); photo = null; } finally { renderGate.Release(); } }
}
