using CommunityToolkit.Mvvm.ComponentModel;
using System.Runtime.CompilerServices;
using PhotoMargin.App.Services;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;
using System.Globalization;

namespace PhotoMargin.App.ViewModels;

public enum PresetMode { Apply, Edit }

// The dialog owns an identity-bound, single-use request. Acknowledgement alone
// never authorizes deletion: mode, selection and saved snapshot are rechecked.
public sealed class PresetDeletionRequest
{
    internal PresetDeletionRequest(MarginRule preset) => Preset = preset with { };
    internal MarginRule Preset { get; }
    public string Id => Preset.Id;
    public string Name => Preset.Name;
    public bool Acknowledged { get; set; }
}

public sealed class RuleEditorViewModel(MainViewModel owner) : ObservableObject
{
    private string? selectedRuleId;
    private string name = "", message = "";
    private double side = 48, top = 36, gap = 24, bottom = 72, extra = 24;
    private double exposureItemGap = 18;
    private bool protectPortrait = true, protectLandscape;
    private bool loading, isSaving, isTransitioning;
    private PresetMode mode;
    private MarginRule? original;
    private MarginRule draft = new MarginRule().Validate();
    private SourceAspect aspect = SourceAspect.FourByThree;
    private InformationArrangement arrangement = InformationArrangement.TwoColumns;
    private byte[]? previewBytes;
    private string previewError = "";
    private long previewVersion;
    private long draftEpoch;
    private bool previewRunning, previewActive;
    private CancellationTokenSource? previewCancellation;
    private Task previewTask = Task.CompletedTask;
    private object? previewOwner;
    public IReadOnlyList<string> AspectChoices { get; } = ["16:9", "5:4", "4:3", "3:2"];
    public IReadOnlyList<string> ArrangementChoices { get; } = ["左右排列", "垂直排列"];
    public int ArrangementIndex
    {
        get => arrangement == InformationArrangement.Stacked ? 1 : 0;
        set
        {
            if (!CanEdit || value is not (0 or 1) || value == ArrangementIndex) return;
            arrangement = value == 1 ? InformationArrangement.Stacked : InformationArrangement.TwoColumns;
            draftEpoch++; owner.SetDemonstrationArrangement(arrangement);
            OnPropertyChanged(); LoadActiveConfiguration(); RequestPreview();
        }
    }
    public int AspectIndex
    {
        get => (int)aspect;
        set
        {
            if (!CanEdit || !Enum.IsDefined((SourceAspect)value) || value == (int)aspect) return;
            aspect = (SourceAspect)value; draftEpoch++; owner.SetDemonstrationAspect(aspect);
            OnPropertyChanged(); LoadActiveConfiguration(); RequestPreview();
        }
    }
    public byte[]? PreviewBytes { get => previewBytes; private set => SetProperty(ref previewBytes, value); }
    public string PreviewError { get => previewError; private set => SetProperty(ref previewError, value); }
    public LayoutResult? PreviewLayout { get; private set; }
    public SizeD PreviewSourceSize { get; private set; }
    public long CompletedPreviewVersion { get; private set; }
    public long PreviewVersion => previewVersion;
    public string SideText => Pixels(Side);
    public string TopText => Pixels(Top);
    public string BottomText => Pixels(Bottom);
    public string GapText => Pixels(Gap);
    public string ExtraText => Pixels(Extra);
    public string ExposureItemGapText => Pixels(ExposureItemGap);
    private static string Pixels(double value) => value.ToString("0", CultureInfo.CurrentCulture) + " px";
    private PresetDeletionRequest? deletion;
    private string? savingId;

    public string? SelectedRuleId => selectedRuleId;
    public int ModeIndex => (int)mode;
    public PresetMode Mode => mode;
    public bool IsEditMode => mode == PresetMode.Edit;
    public string Name { get => name; set { if (CanAdjustValues && SetProperty(ref name, value ?? "")) NotifyState(); } }
    public double Side { get => side; set => SetValue(ref side, value); }
    public double Top { get => top; set => SetValue(ref top, value); }
    public double Gap { get => gap; set => SetValue(ref gap, value); }
    public double ExposureItemGap { get => exposureItemGap; set => SetValue(ref exposureItemGap, value); }
    public double Bottom { get => bottom; set => SetValue(ref bottom, value); }
    public double Extra { get => extra; set => SetValue(ref extra, value); }
    public bool ProtectPortrait { get => protectPortrait; set => SetValue(ref protectPortrait, value); }
    public bool ProtectLandscape { get => protectLandscape; set => SetValue(ref protectLandscape, value); }
    public string Message { get => message; private set => SetProperty(ref message, value); }
    public bool IsSaving { get => isSaving; private set { if (SetProperty(ref isSaving, value)) NotifyState(); } }
    public bool CanEdit => !IsSaving && !isTransitioning && deletion is null;
    public bool CanAdjustValues => CanEdit && (!IsEditMode || SelectedPreset is not null);
    public bool CanSave => !owner.IsSettingsReadOnly && CanAdjustValues && !string.IsNullOrWhiteSpace(Name) &&
        !owner.MarginRules.Any(rule => (!IsEditMode || rule.Id != selectedRuleId) && string.Equals(rule.Name, Name.Trim(), StringComparison.OrdinalIgnoreCase));
    public bool CanDelete => !owner.IsSettingsReadOnly && CanEdit && IsEditMode && SelectedPreset is { Id: not MarginRule.DefaultId };
    public bool HasDirtyChanges => IsEditMode && original is not null && Snapshot != original;
    private MarginRule? SelectedPreset => owner.MarginRules.FirstOrDefault(rule => rule.Id == selectedRuleId);
    private MarginRule Snapshot => draft with { Id = selectedRuleId ?? "", Name = Name };
    private MarginRule WorkDraft() => (SelectedPreset ?? owner.WorkingMargins).Validate()
        .WithConfiguration(owner.WorkAspect, owner.Arrangement, MarginConfiguration.From(owner.WorkingMargins))
        with { Id = selectedRuleId ?? "", Name = SelectedPreset?.Name ?? "" };

    internal void Begin(string? id)
    {
        if (!CanEdit) return;
        mode = PresetMode.Apply;
        selectedRuleId = owner.MarginRules.Any(rule => rule.Id == id) ? id : null;
        original = null; aspect = owner.DemonstrationAspect; arrangement = owner.DemonstrationArrangement;
        OnPropertyChanged(nameof(AspectIndex)); OnPropertyChanged(nameof(ArrangementIndex));
        LoadValues(WorkDraft());
    }
    private void LoadValues(MarginRule values)
    {
        draftEpoch++; draft = values.Validate(); name = values.Name;
        LoadActiveConfiguration(); Message = ""; RequestPreview();
    }
    private void LoadActiveConfiguration()
    {
        var values = draft.ForAspect(aspect, arrangement);
        loading = true;
        side = values.Side; top = values.Top; gap = values.Gap; exposureItemGap = values.ExposureItemGap; bottom = values.Bottom; extra = values.Extra;
        protectPortrait = values.ProtectPortrait; protectLandscape = values.ProtectLandscape;
        foreach (var property in new[] { nameof(Name), nameof(Side), nameof(Top), nameof(Gap), nameof(ExposureItemGap), nameof(Bottom), nameof(Extra), nameof(ProtectPortrait), nameof(ProtectLandscape) }) OnPropertyChanged(property);
        loading = false; NotifyNumbers(); NotifyState();
    }
    private void NotifyState()
    {
        foreach (var property in new[] { nameof(SelectedRuleId), nameof(Mode), nameof(ModeIndex), nameof(IsEditMode), nameof(CanEdit), nameof(CanAdjustValues), nameof(CanSave), nameof(CanDelete), nameof(HasDirtyChanges) }) OnPropertyChanged(property);
    }
    internal void RefreshSettingsAccess() => NotifyState();
    private void SetValue<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (loading || !CanAdjustValues || !SetProperty(ref field, value, property)) return;
        var configuration = new MarginConfiguration { Side = Side, Top = Top, Gap = Gap, ExposureItemGap = ExposureItemGap, Bottom = Bottom,
            Extra = Extra, ProtectPortrait = ProtectPortrait, ProtectLandscape = ProtectLandscape };
        draft = draft.WithConfiguration(aspect, arrangement, configuration);
        if (!IsEditMode) owner.ApplyWorkingConfiguration(configuration, arrangement);
        NotifyNumbers(); NotifyState(); RequestPreview();
    }
    private void NotifyNumbers()
    { foreach (var property in new[] { nameof(SideText), nameof(TopText), nameof(BottomText), nameof(GapText), nameof(ExposureItemGapText), nameof(ExtraText) }) OnPropertyChanged(property); }
    internal void StartPreview(object? ownerToken = null)
    { previewOwner = ownerToken; previewActive = true; RequestPreview(); }
    internal void StopPreview(object? ownerToken = null)
    {
        // A departing page may unload after its replacement has already loaded.
        if (ownerToken is not null && !ReferenceEquals(ownerToken, previewOwner)) return;
        previewOwner = null; previewActive = false; previewVersion++; previewCancellation?.Cancel();
    }
    internal async Task StopPreviewAsync() { StopPreview(); await previewTask; }
    internal void RefreshPreview() { if (previewActive) RequestPreview(); }
    private void RequestPreview()
    {
        previewVersion++;
        if (previewActive && !previewRunning) previewTask = UpdatePreviewAsync();
    }
    private async Task UpdatePreviewAsync()
    {
        previewRunning = true;
        try
        {
            // Coalesce input while a render is in flight, without restarting a trailing
            // debounce on every pointer tick. Continuous dragging still produces frames.
            while (previewActive && CompletedPreviewVersion != previewVersion)
            {
                await Task.Delay(35);
                if (!previewActive) break;
                var version = previewVersion; var selectedAspect = aspect;
                var epoch = draftEpoch; var sourceRevision = owner.SourceRevision;
                var selectedArrangement = arrangement;
                var configuration = draft.ForAspect(selectedAspect, selectedArrangement) with { };
                previewCancellation?.Dispose(); previewCancellation = new();
                try
                {
                    var rendered = await owner.RenderRuleDemoAsync(selectedAspect, configuration, previewCancellation.Token, selectedArrangement);
                    if (!previewActive) break;
                    if (epoch != draftEpoch || sourceRevision != owner.SourceRevision || version <= CompletedPreviewVersion) continue;
                    // One serial renderer publishes progressive frames during a drag.
                    // No older completion can replace a newer frame. A ratio/preset/source
                    // change invalidates the entire generation, not merely the last frame.
                    PreviewLayout = rendered.Layout; PreviewSourceSize = rendered.SourceSize;
                    CompletedPreviewVersion = version;
                    PreviewError = rendered.Layout.FellBackFromColumns ? LayoutEngine.ColumnsFallbackWarning : "";
                    PreviewBytes = rendered.Bytes;
                }
                catch (OperationCanceledException) { if (!previewActive) break; }
                catch (Exception exception)
                {
                    if (version == previewVersion) { CompletedPreviewVersion = version; PreviewError = exception.Message; }
                }
            }
        }
        finally { previewRunning = false; }
    }
    private async Task<bool> ConfirmDiscardAsync(Func<Task<bool>>? confirm)
    {
        if (!HasDirtyChanges) return true;
        if (confirm is null) return false;
        isTransitioning = true; NotifyState();
        try { return await confirm(); }
        finally { isTransitioning = false; NotifyState(); }
    }
    public async Task<bool> ChangeModeAsync(PresetMode target, Func<Task<bool>>? confirmDiscard = null)
    {
        if (!Enum.IsDefined(target) || !CanEdit) return false;
        if (target == mode) return true;
        var discardingDraft = HasDirtyChanges;
        if (!await ConfirmDiscardAsync(confirmDiscard)) return false;
        mode = target;
        if (IsEditMode)
        {
            original = SelectedPreset is { } rule ? rule with { } : null;
            LoadValues(original ?? owner.WorkingMargins with { Id = "", Name = "" });
        }
        else
        {
            original = null;
            // An explicit clean switch to Apply can use the just-saved preset even
            // when it is the only item. Discarding a dirty edit never changes work.
            if (!discardingDraft && SelectedPreset is { } saved) owner.ApplyPresetToWork(saved);
            LoadValues(WorkDraft());
        }
        return true;
    }
    public async Task<bool> SelectPresetAsync(string? id, Func<Task<bool>>? confirmDiscard = null)
    {
        if (!CanEdit || (id is not null && !owner.MarginRules.Any(rule => rule.Id == id))) return false;
        if (id == selectedRuleId) return true;
        if (!await ConfirmDiscardAsync(confirmDiscard)) return false;
        selectedRuleId = id;
        original = IsEditMode && SelectedPreset is { } selected ? selected with { } : null;
        var values = SelectedPreset ?? owner.WorkingMargins with { Id = "", Name = "" };
        LoadValues(values with { });
        if (!IsEditMode && SelectedPreset is { } preset) owner.ApplyPresetToWork(preset);
        return true;
    }
    public async Task<bool> LeaveAsync(Func<Task<bool>>? confirmDiscard = null)
    {
        if (!CanEdit || !await ConfirmDiscardAsync(confirmDiscard)) return false;
        Begin(owner.SelectedMarginRuleId); return true;
    }
    public async Task<bool> SaveAsync()
    {
        if (owner.IsSettingsReadOnly) { Message = "設定尚未成功讀取，為保護既有預設，目前無法儲存。請重新讀取設定後再試。"; return false; }
        if (!CanAdjustValues) return false;
        if (string.IsNullOrWhiteSpace(Name)) { Message = "請輸入規則名稱。"; return false; }
        if (!CanSave) { Message = "已有同名預設；套用模式請使用不同名稱另存，編輯模式才能更新原預設。"; return false; }
        if (new[] { Side, Top, Gap, ExposureItemGap, Bottom, Extra }.Any(value => !double.IsFinite(value))) { Message = "請填入有效的邊距數值。"; return false; }
        var expected = IsEditMode ? original : null;
        if (IsEditMode && (expected is null || SelectedPreset != expected)) { Message = "預設已變更，請重新選取後再編輯。"; return false; }
        var rule = Snapshot.Validate() with { Id = IsEditMode ? selectedRuleId! : Guid.NewGuid().ToString("N") };
        savingId = rule.Id; IsSaving = true;
        try
        {
            if (!await owner.SaveRuleAsync(rule, expected)) { Message = "預設尚未儲存。" + owner.PersistenceError; return false; }
            selectedRuleId = rule.Id;
            original = IsEditMode ? rule with { } : null;
            LoadValues(rule); Message = "預設已儲存。"; return true;
        }
        finally { savingId = null; IsSaving = false; }
    }
    internal bool AuthorizesSave(string id, MarginRule? expected) => IsSaving && savingId == id &&
        (IsEditMode ? expected is not null && expected == original && id == selectedRuleId : expected is null);

    public PresetDeletionRequest? BeginDeletion()
    {
        if (!CanDelete || SelectedPreset is not { } preset) return null;
        deletion = new PresetDeletionRequest(preset); NotifyState(); return deletion;
    }
    public void CancelDeletion(PresetDeletionRequest request)
    {
        if (IsSaving || !ReferenceEquals(deletion, request)) return;
        deletion = null; NotifyState();
    }
    internal bool AuthorizesDeletion(MarginRule preset) => IsSaving && IsEditMode && deletion is { Acknowledged: true } request &&
        selectedRuleId == request.Id && preset == request.Preset && preset.Id != MarginRule.DefaultId;
    public async Task<bool> DeleteAsync(PresetDeletionRequest? request)
    {
        if (IsSaving || !IsEditMode || request is null || !ReferenceEquals(deletion, request) || !request.Acknowledged ||
            selectedRuleId != request.Id || SelectedPreset != request.Preset || request.Id == MarginRule.DefaultId)
        { Message = "目前模式不允許刪除，或預設已變更；請取消後重新選取並確認。"; return false; }
        IsSaving = true;
        try
        {
            if (!await owner.DeleteRuleAsync(request.Id)) { Message = "預設未刪除。" + owner.PersistenceError; return false; }
            deletion = null;
            selectedRuleId = owner.MarginRules.Any(rule => rule.Id == owner.SelectedMarginRuleId) ? owner.SelectedMarginRuleId : owner.MarginRules.FirstOrDefault()?.Id;
            original = SelectedPreset is { } rule ? rule with { } : null;
            LoadValues(original ?? owner.WorkingMargins with { Id = "", Name = "" });
            Message = "預設已永久刪除。"; return true;
        }
        finally { IsSaving = false; }
    }
}
