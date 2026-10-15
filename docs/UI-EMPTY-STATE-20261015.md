# 未載入照片介面調整（2026-10-15）

本次依 `Main UI (1).png` 修改未載入狀態，不改動 Main Setting UI 的欄位、文案、順序、繫結或版面參數。

## 行為

- 未載入時隱藏白色成品畫布，顯示原生照片集合圖示、「拖入 PNG / JPG 照片」與可操作的「匯入照片」按鈕。
- 匯入按鈕重用既有選檔處理；預覽容器的拖曳匯入不變。參考圖的空白藍色按鈕補上動作文字及無障礙名稱，避免無標示按鈕。
- 右側設定外層加入 `ContentControl.IsEnabled ← HasPhoto`，所有選單、滑條、輸入欄位及按鈕使用 WinUI 原生 Disabled 狀態。外層 ScrollViewer 未停用，仍可捲動查看原有內容。
- 左上齒輪與中央匯入按鈕保留可用；未載入時右下只顯示停用的匯出按鈕，不重複顯示更換照片。
- 狀態列顯示「尚未匯入照片」。初次匯入失敗時保留空白狀態、反灰設定及錯誤提示，可再次選圖。
- 成功載入後，自動隱藏空白提示、顯示真實成品預覽、恢復右側設定與更換照片按鈕。品牌圖原本的停用狀態不變。
- 未修改圖片核心、EXIF 解析、匯出規則或設定頁內容。深淺色主題皆沿用原生資源。

## 位置

- `src/PhotoMargin.App/MainWindow.xaml`：空白預覽內容、右側停用容器。
- `src/PhotoMargin.App/MainWindow.xaml.cs`：初始與 HasPhoto／錯誤狀態改變時切換畫面。
- `src/PhotoMargin.App/ViewModels/MainViewModel.cs`：只有未載入時的檔名提示文字調整。
- `tests/PhotoMargin.App.SmokeTests/SettingsNavigationRegression.cs`：補驗未載入、淺色主題、失敗保留與成功載入後恢復的狀態。

圖示採 [Microsoft Segoe Fluent Icons](https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font) 的 PhotoCollection（E7AA），經由 WinUI 的 `SymbolThemeFontFamily` 使用原生圖示字體。

## 交付

新版發布至 `artifacts/PhotoMargin-empty-state-win-x64/`，完整 ZIP 為 `artifacts/PhotoMargin-empty-state-win-x64.zip`。保留上一版資料夾。

實際截圖與測試結果另存於 `artifacts/PhotoMargin-empty-state-verification/`，請完整解壓執行包後啟動 `PhotoMargin.App.exe`。

## 驗證

- Debug 建置成功，0 警告、0 錯誤；Release self-contained 發布成功，已實際開啟 PhotoMargin 視窗並正常關閉。
- Core 29 項、Imaging 11 項單元測試通過；實際 WinUI 整合檢查 116 項通過。
- 實測 120 DPI（125%），查看未載入深色／淺色與載入後實際截圖。所有右側 Control 的原生 IsEnabled 均為 false；載入後應啟用者恢復，品牌功能仍停用。
- 檢查失敗初次匯入保留空白畫面與錯誤；既有 Reload、規則、導覽、縮小視窗、捲軸避讓與真實 JPG 匯出回歸均通過。
- 本輪未測其他實際 DPI、Windows 10 或所有輔助技術；未自動操作 Windows 選檔對話框或實體拖放，兩個入口沿用原有實作。

[未載入深色實際截圖](../artifacts/PhotoMargin-empty-state-verification/empty-dark.png) · [未載入淺色實際截圖](../artifacts/PhotoMargin-empty-state-verification/empty-light.png) · [載入後實際截圖](../artifacts/PhotoMargin-empty-state-verification/loaded.png) · [WinUI 測試結果](../artifacts/PhotoMargin-empty-state-verification/ui-regression-result.json)
