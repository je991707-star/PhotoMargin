using CommunityToolkit.Mvvm.ComponentModel;
using PhotoMargin.App.Services;

namespace PhotoMargin.App.ViewModels;

public sealed class RuleEditorViewModel(MainViewModel owner) : ObservableObject
{
    private string? selectedRuleId;
    private string name = "", message = "";
    private double side = 48, top = 36, gap = 24, bottom = 72, extra = 24;
    private bool protectPortrait = true, protectLandscape = true;
    private bool isSaving;
    public string? SelectedRuleId { get => selectedRuleId; set { if (value is not null && value != selectedRuleId) Begin(value); } }
    public string Name { get => name; set => SetProperty(ref name, value); }
    public double Side { get => side; set => SetProperty(ref side, value); }
    public double Top { get => top; set => SetProperty(ref top, value); }
    public double Gap { get => gap; set => SetProperty(ref gap, value); }
    public double Bottom { get => bottom; set => SetProperty(ref bottom, value); }
    public double Extra { get => extra; set => SetProperty(ref extra, value); }
    public bool ProtectPortrait { get => protectPortrait; set => SetProperty(ref protectPortrait, value); }
    public bool ProtectLandscape { get => protectLandscape; set => SetProperty(ref protectLandscape, value); }
    public string Message { get => message; private set => SetProperty(ref message, value); }
    public bool IsSaving { get => isSaving; private set { if (SetProperty(ref isSaving, value)) { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanDelete)); } } }
    public bool CanEdit => !IsSaving;
    public bool CanDelete => CanEdit && selectedRuleId is not null and not MarginRule.DefaultId;
    internal void Begin(string id)
    {
        var rule = owner.MarginRules.FirstOrDefault(item => item.Id == id);
        if (rule is null) return;
        selectedRuleId = id;
        Name = rule.Name; Side = rule.Side; Top = rule.Top; Gap = rule.Gap; Bottom = rule.Bottom; Extra = rule.Extra;
        ProtectPortrait = rule.ProtectPortrait; ProtectLandscape = rule.ProtectLandscape; Message = "";
        OnPropertyChanged(nameof(SelectedRuleId)); OnPropertyChanged(nameof(CanDelete));
    }
    public void New()
    {
        Begin(owner.SelectedMarginRuleId); selectedRuleId = null; Name = ""; Message = "輸入名稱並儲存後才會建立規則。";
        OnPropertyChanged(nameof(SelectedRuleId)); OnPropertyChanged(nameof(CanDelete));
    }
    public async Task<bool> SaveAsync()
    {
        if (IsSaving) return false;
        if (string.IsNullOrWhiteSpace(Name)) { Message = "請輸入規則名稱。"; return false; }
        if (owner.MarginRules.Any(rule => rule.Id != selectedRuleId && string.Equals(rule.Name, Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        { Message = "已有同名規則，請使用其他名稱。"; return false; }
        if (new[] { Side, Top, Gap, Bottom, Extra }.Any(value => !double.IsFinite(value)))
        { Message = "請填入有效的邊距數值。"; return false; }
        var rule = new MarginRule
        {
            Id = selectedRuleId ?? Guid.NewGuid().ToString("N"), Name = Name, Side = Side, Top = Top,
            Gap = Gap, Bottom = Bottom, Extra = Extra, ProtectPortrait = ProtectPortrait, ProtectLandscape = ProtectLandscape
        }.Validate();
        IsSaving = true;
        try
        {
            // Retain the draft identity on failure so retry updates, rather than duplicates, it.
            selectedRuleId = rule.Id; OnPropertyChanged(nameof(SelectedRuleId));
            if (!await owner.SaveRuleAsync(rule)) { Message = "規則尚未儲存。" + owner.PersistenceError; return false; }
            Begin(rule.Id); Message = "規則已儲存。"; return true;
        }
        finally { IsSaving = false; }
    }
    public async Task DeleteAsync()
    {
        if (!CanDelete) return;
        IsSaving = true;
        try
        {
            if (!await owner.DeleteRuleAsync(selectedRuleId!)) { Message = "規則未刪除。" + owner.PersistenceError; return; }
            Begin(owner.SelectedMarginRuleId); Message = "規則已刪除。";
        }
        finally { IsSaving = false; }
    }
}
