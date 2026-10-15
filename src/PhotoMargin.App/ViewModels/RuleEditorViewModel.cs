using CommunityToolkit.Mvvm.ComponentModel;
using PhotoMargin.App.Services;

namespace PhotoMargin.App.ViewModels;

public sealed class RuleEditorViewModel(MainViewModel owner) : ObservableObject
{
    private string? selectedRuleId;
    private string name = "", message = "";
    private double side = 48, top = 36, gap = 24, bottom = 72, extra = 24;
    private bool protectPortrait = true, protectLandscape = true;
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
    public bool CanDelete => selectedRuleId is not null and not MarginRule.DefaultId;
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
    public bool Save()
    {
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
        owner.SaveRule(rule); Begin(rule.Id); Message = "規則已儲存。"; return true;
    }
    public void Delete()
    {
        if (!CanDelete) return;
        owner.DeleteRule(selectedRuleId!); Begin(owner.SelectedMarginRuleId); Message = "規則已刪除。";
    }
}
