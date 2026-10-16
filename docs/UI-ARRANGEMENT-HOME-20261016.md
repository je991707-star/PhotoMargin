# 排列選單與設定返回首頁（2026-10-16）

## 排列選項與既有核心的對應

檢查前的核心沒有可選排列 enum，但 `LayoutEngine.BuildInformation` 已具備左右分欄與置中堆疊兩條路徑，原本先嘗試分欄，放不下才堆疊。本次只暴露並選擇這些既有資訊排法，不新增照片方向、照片排序或文字對齊模式。

| 選項 | 模型 | 行為 |
| --- | --- | --- |
| 自動（預設） | `InformationArrangement.Auto = 0` | 保留原有自適應：先分欄，不適合時改為置中堆疊／換行 |
| 左右分欄 | `TwoColumns = 1` | 左欄機身／鏡頭，右欄拍攝參數／時間；放不下時提示並停用匯出，不悄悄改成其他排列 |
| 置中堆疊 | `Stacked = 2` | 直接使用既有堆疊路徑；依序為機身、鏡頭、拍攝參數、時間，必要時安全換行 |

三種排列均使用相同的完整照片 contain 算法、1:1 畫布、字形量測、安全矩形及重疊檢查。不可安全排版時保留既有驗證錯誤處理，不輸出越界成品。

## 介面與保存

- 只在主畫面「照片版面」區塊、「字體設定」正上方加入一個原生 ComboBox，標籤為「排列設定」。兩者同屬既有 StackPanel，沿用相同 Stretch、原生標籤／高度與 12 DIP 項目間距，其他控制項只依序下移。
- 下拉項目以型別化 `ArrangementChoice` 和 `SelectedArrangement` 雙向繫結，不依賴 enum 穿越 XAML SelectedValue 時的裝箱型別轉換。
- 切換沿用 `SetLayout → RequestPreview`，先讓舊版面失效，再完成背景渲染；匯出使用同一個已驗證的 LayoutResult，不是只換顯示文案。
- `LayoutSettings.Arrangement` 由既有 SettingsStore 的 `PersistedSettings.Layout` 一起保存及還原，無新增另一份偏好檔案。
- 保留設定版本 3 的相容新增欄位；版本 2／3 缺少此欄位時，模型預設 Auto。未知數值也回到 Auto，其他既有偏好不重設。
- 未載入時與其餘右側設定一樣原生停用；載入後恢復操作。未新增額外按鈕或改動設定頁內容。

## 返回首頁

- 移除 SettingsPage 原本的 `Stack<SettingsSection>` 與紀錄歷史參數；切換子頁只更新選取與內容，不堆疊返回目標。
- 左上自訂按鈕、原生 NavigationView.BackRequested、Alt+Left 原有入口均呼叫同一個 RequestBack；現在只發出返回主畫面的事件，不呼叫 GoBack。
- 每個設定頁實例用 `returningHome` 防止重複觸發；MainWindow 也確認事件來自目前設定頁，才卸載事件並返回。
- 使用原本的 WorkspaceGrid、MainViewModel、照片及預覽；不開新視窗、不建立空白作品。只清理 SettingsFrame 的 BackStack／ForwardStack，不清除其他框架的導覽紀錄。
- 「新增規則」仍明確傳入 SettingsSection.Layout；返回一次回到原工作頁，保留選取規則、排列、手動 EXIF 與輸出設定。

## 修改檔案

- `src/PhotoMargin.App/MainWindow.xaml`：新增一個排列 ComboBox。
- `src/PhotoMargin.App/ViewModels/MainViewModel.cs`：選項、選取、預覽更新與偏好保存／還原。
- `src/PhotoMargin.Core/Models/LayoutModels.cs`：排列 enum、模型預設與驗證。
- `src/PhotoMargin.Core/LayoutEngine.cs`：選用既有排法，明確分欄不適合時阻止匯出。
- `src/PhotoMargin.App/Views/SettingsPage.xaml.cs`：移除子頁返回歷程，統一一次返回首頁。
- `src/PhotoMargin.App/MainWindow.xaml.cs`：目前設定頁事件保護及專屬 Frame 歷程清理。
- `src/PhotoMargin.App/App.xaml.cs`：僅 Debug 增加獨立程序的偏好還原檢查入口，Release 不包含此測試流程。
- `tests/PhotoMargin.Core.Tests/LayoutEngineTests.cs`：排列、方向比例、尺寸、安全區、舊設定及過長文字測試。
- `tests/PhotoMargin.Imaging.Tests/ImagingFlowTests.cs`：三種排列的預覽與實際渲染／匯出一致性及墨跡安全邊界。
- `tests/PhotoMargin.App.SmokeTests/SettingsNavigationRegression.cs`：原生選單、實際渲染／匯出、所有設定子頁返回、重複返回、偏好重新載入及舊設定相容檢查。
- `README.md`、`docs/ARCHITECTURE.md` 與本文件：更新功能及驗證紀錄。

SettingsStore 與影像渲染服務沿用，沒有改動 EXIF 解析、圖片解碼、選檔、品牌圖或其他設定功能。版本保留 0.0.1；本次未建立 Git 提交、未推送 GitHub。

## 驗證結果

- Debug XAML／C# 建置成功，0 警告、0 錯誤。
- Release solution 建置與 self-contained x64 發布成功；已實際啟動發布版 PhotoMargin 視窗並關閉本次測試視窗，沒有 startup-error.log。
- Core 35 項、Imaging 14 項，共 49 項單元／影像測試全部通過。
- 真實 Windows WinUI 程式執行 147 項整合檢查，加上另一個新程序的 4 項重啟檢查，共 151 項全部通過，實測 120 DPI（125%）；涵蓋 1344×892、1280×820、1024×768 與原有最小視窗。
- 三種排列的實際 ViewModel 預覽與指定核心 LayoutResult 逐像素相符；實際匯出 JPG 與相同版型的核心渲染位元組相同。分欄與堆疊的成品確實不同，預覽輔助線仍不進成品。
- 檢查排列位於字體上一個項目，兩個 ComboBox 的左緣、寬度及高度一致；各尺寸下仍避開原生展開捲軸。
- 連續切換多個子頁後，實際點擊自訂返回一次即回首頁；四個子頁逐一驗證。窄視窗的原生 NavigationView 返回按鈕也實際點擊驗證。
- 驗證返回前後照片路徑、手動資訊、排列／輸出設定及預覽物件保留；重複返回不會再觸發，設定專用歷程已清空。
- 偏好寫入後先以新的 ViewModel 重新載入，再啟動另一個真正獨立的 WinUI 程序（Debug 的 `--verify-arrangement-restore`），不刪除或重建該獨立測試偏好，確認原生 ComboBox 選回置中堆疊，並保留邊距、品質、尺寸與字級。版本 2／3 缺欄位測試均選 Auto 並保留原值；測試完全不修改正式使用者偏好檔。
- 已查看三種排列的真實 WinUI 視窗截圖，並保存檢查 JSON 與 JPG。

驗證資料在 `artifacts/PhotoMargin-arrangement-home-verification/`：

- [自動排列實際畫面](../artifacts/PhotoMargin-arrangement-home-verification/arrangement-Auto.png)
- [左右分欄實際畫面](../artifacts/PhotoMargin-arrangement-home-verification/arrangement-TwoColumns.png)
- [置中堆疊實際畫面](../artifacts/PhotoMargin-arrangement-home-verification/arrangement-Stacked.png)
- [147 項 WinUI 結果](../artifacts/PhotoMargin-arrangement-home-verification/ui-regression-result.json)
- [獨立新程序的 4 項重啟結果](../artifacts/PhotoMargin-arrangement-home-verification/arrangement-restore-result.json)
- [重啟後的真實原生選單](../artifacts/PhotoMargin-arrangement-home-verification/arrangement-restarted.png)

更新版執行包：[PhotoMargin-arrangement-home-win-x64.zip](../artifacts/PhotoMargin-arrangement-home-win-x64.zip)。完整解壓後執行 `PhotoMargin.App.exe`，不要使用先前的舊版資料夾。

## 尚未完成的實機項目

尚未在其他實際 DPI（100%、150%、200%）、跨螢幕、Windows 10、螢幕閱讀器或高對比模式進行本輪人工驗收；也未自動操作所有實體鍵盤路徑、Alt+Left 按鍵、Windows 選檔視窗及實體拖放。現有 Alt+Left 處理函式已與兩種返回按鈕共用同一操作，但未宣稱實體鍵盤驗收完成。
