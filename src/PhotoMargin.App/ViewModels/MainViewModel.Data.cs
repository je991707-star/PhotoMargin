using PhotoMargin.App.Services;
using PhotoMargin.Core.Models;
using System.Globalization;

namespace PhotoMargin.App.ViewModels;

public enum ImportDraftChoice { Cancel, Save, Discard }

public sealed partial class MainViewModel
{
    private bool isDataBusy;
    private string dataStatus = "";
    private LayoutSettings? importedLayoutDefaults;
    private bool savingDraftForImport;
    public string DataDirectory => settingsStore.DataDirectory;
    public string DataStatus { get => dataStatus; private set => SetProperty(ref dataStatus, value); }
    public bool IsDataBusy
    {
        get => isDataBusy;
        private set
        {
            if (!SetProperty(ref isDataBusy, value)) return;
            foreach (var name in new[] { nameof(CanManageData), nameof(CanSelectPhoto), nameof(CanReload), nameof(CanExport) }) OnPropertyChanged(name);
            NotifyRawAvailability();
        }
    }
    public bool CanManageData => settingsLoaded && !IsDataBusy && !IsBusy && !IsRawReading;
    public IReadOnlyList<string> ColorModes { get; } = ["跟隨系統", "淺色", "深色"];
    public int ColorModeIndex { get => ThemeChoice switch { 1 => 2, 2 => 1, _ => 0 }; set { if (value is >= 0 and <= 2) ThemeChoice = value switch { 1 => 2, 2 => 1, _ => 0 }; } }
    public void ReportDataFailure(Exception error) => DataStatus = "操作失敗：" + error.Message;

    public async Task ExportSettingsBundleAsync(Func<string, Task<string?>> chooseFile)
    {
        if (!CanManageData || disposed) return;
        IsDataBusy = true; DataStatus = "正在準備設定集…";
        try
        {
            var destination = await chooseFile("PhotoMargin_Settings_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".zip");
            if (destination is null) { DataStatus = "已取消匯出"; return; }
            // Do not serialize the editor draft or current unsaved UI state. Read
            // only the repository's committed settings/presets pair under its lock.
            await preferenceGate.WaitAsync(lifetime.Token);
            try { await settingsStore.Repository.ExportBundleAsync(destination, lifetime.Token); }
            finally { preferenceGate.Release(); }
            DataStatus = "已匯出設定集";
        }
        catch (OperationCanceledException) { if (!disposed) DataStatus = "已取消匯出"; }
        catch (Exception error) { if (!disposed) ReportDataFailure(error); }
        finally { if (!disposed) IsDataBusy = false; }
    }

    public async Task<bool> ImportSettingsBundleAsync(Func<Task<string?>> chooseFile,
        Func<Task<bool>> confirmImport, Func<Task<ImportDraftChoice>> resolveDraft)
    {
        if (!CanManageData || disposed) return false;
        IsDataBusy = true; DataStatus = "正在驗證設定集…";
        try
        {
            var path = await chooseFile();
            if (path is null) { DataStatus = "已取消匯入"; return false; }
            var candidate = await settingsStore.Repository.ReadBundleAsync(path, lifetime.Token);
            var draftChoice = RuleEditor.HasDirtyChanges ? await resolveDraft() : ImportDraftChoice.Discard;
            if (draftChoice == ImportDraftChoice.Cancel || !await confirmImport()) { DataStatus = "已取消匯入"; return false; }
            if (draftChoice == ImportDraftChoice.Save)
            {
                savingDraftForImport = true;
                try { if (!await RuleEditor.SaveAsync()) { DataStatus = "預設尚未儲存，未匯入"; return false; } }
                finally { savingDraftForImport = false; }
            }
            await preferenceGate.WaitAsync(lifetime.Token);
            try
            {
                // Capture current work only after confirmation, without replacing
                // photo, RAW, manual values, or applying the imported selected rule.
                var work = BuildLayoutSettings(); var information = CaptureWorkInformation(); var hasWork = HasPhoto;
                var localDirectory = ExportDirectory; var pendingLayout = persisted.WorkLayout;
                var imported = await settingsStore.Repository.ImportBundleAsync(candidate, (_, incoming) => incoming with
                {
                    WorkInformation = information, WorkLayout = hasWork ? work : pendingLayout,
                    Export = (incoming.Export ?? new()) with { Directory = string.IsNullOrWhiteSpace(localDirectory) ? null : localDirectory },
                    LastOutputDirectory = string.IsNullOrWhiteSpace(localDirectory) ? null : localDirectory
                }, lifetime.Token);
                persisted = imported; saveRevision++;
                importedLayoutDefaults = imported.WorkLayout is not null ? imported.Layout : null;
                applyingValues = true;
                try
                {
                    exportPreferences = imported.Export ?? new(); customExportSize = false;
                    OutputSize = imported.Layout.OutputSize; JpegQuality = exportPreferences.JpegQuality;
                    if (!hasWork) ApplyComposition(imported.Layout);
                    ThemeChoice = (int)imported.Theme; ShowGuides = imported.ShowGuides;
                    demonstrationAspect = imported.DemonstrationAspect;
                    demonstrationArrangement = imported.DemonstrationArrangement ?? InformationArrangement.TwoColumns;
                    MarginRules.Clear(); foreach (var rule in imported.MarginRules) MarginRules.Add(rule.Validate());
                    selectedMarginRuleId = MarginRules.Any(rule => rule.Id == imported.SelectedMarginRuleId) ? imported.SelectedMarginRuleId : MarginRules.FirstOrDefault()?.Id;
                    OnPropertyChanged(nameof(SelectedMarginRuleId)); NotifyExport();
                }
                finally { applyingValues = false; }
                RuleEditor.Begin(selectedMarginRuleId); SetPersistenceError("");
                RequestPreview(true); RuleEditor.RefreshPreview(); DataStatus = "已匯入設定集";
                return true;
            }
            finally { preferenceGate.Release(); }
        }
        catch (OperationCanceledException) { if (!disposed) DataStatus = "已取消匯入"; return false; }
        catch (Exception error) { if (!disposed) ReportDataFailure(error); return false; }
        finally { if (!disposed) IsDataBusy = false; }
    }
    private void ApplyComposition(LayoutSettings layout)
    {
        var previous = applyingValues; applyingValues = true;
        try
        {
            PrimaryFontSize = layout.PrimaryFontSize; LensFontSize = layout.LensFontSize;
            SecondaryFontSize = layout.SecondaryFontSize; DateFontSize = layout.DateFontSize;
            FontFamily = layout.FontFamily; Arrangement = layout.Arrangement;
            ApplyRule(MarginRule.FromLayout(layout));
        }
        finally { applyingValues = previous; }
    }
    private void ApplyDefaultTypography(LayoutSettings layout)
    {
        var previous = applyingValues; applyingValues = true;
        try { PrimaryFontSize = layout.PrimaryFontSize; LensFontSize = layout.LensFontSize; SecondaryFontSize = layout.SecondaryFontSize; DateFontSize = layout.DateFontSize; FontFamily = layout.FontFamily; }
        finally { applyingValues = previous; }
    }
}
