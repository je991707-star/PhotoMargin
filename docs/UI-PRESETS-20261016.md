# 版面設定與預設模式 — 2026-10-16

依附件 `SettingUI Page.png` 實作，應用程式版本維持 0.0.2；本輪沒有 commit／push。沒有新增主畫面入口或其他導覽項目。

## 介面

- 保留既有 NavigationView、搜尋、四個導覽項目、返回與展開按鈕。
- 版面設定為左側實際正方形成品預覽、右側原生 ComboBox、規則編輯模式 RadioButtons、規則名稱、五個 Slider、儲存預設／刪除預設。
- 順序為左右邊距、上方邊距、底部安全距離、照片資訊間距、裁切文字額外留白。
- 不保留舊的「新增規則」頁內按鈕或兩個裁切保護 CheckBox；保護值仍在模型中完整複製與保存，不改圖片核心及安全規則。
- 預覽重用目前照片的真實渲染及目前字體、排列、EXIF，不使用設計圖作背景或硬編碼示範資料。沒有來源時為空白畫布。
- 右側原生垂直捲動，無水平捲軸；較窄時預覽在上、同一組控制項在下，文字及控制項不縮小。

## 三份資料

| 資料 | 責任與保存 |
| --- | --- |
| 已保存預設 | MarginRules 中的不可變 record；保存／刪除磁碟成功後才替換清單 |
| 目前作品 | MainViewModel 的獨立數值，隨既有偏好保存至 Layout；選取預設只複製，不建立連動 |
| 編輯草稿 | RuleEditorViewModel 的獨立欄位／原始快照，只做暫時預覽；不進入 BuildPersisted 或匯出 |

現有 MarginRule 沒有可變巢狀物件，僅包含 init 字串、數值與布林；讀入、套用、保存及交易快照都另建 record／清單，不共用可變資料。沒有引入 JSON 深複製或新套件。

## 行為

- 套用：選取預設時複製全部七個版面／保護值到作品及欄位，立即預覽。滑條只改作品。依使用者補充，儲存以不同名稱另存新 ID；同名拒絕，不能用此模式覆寫預設。刪除在 UI、Editor 與 MainViewModel 交易層均不允許。
- 編輯：載入選取預設的獨立草稿，滑條先更新草稿及暫時預覽。儲存更新原 ID，名稱相同也不新增重複項目。儲存失敗保留原預設與草稿，可重試。
- 編輯預設本身不自動套到作品；返回／放棄草稿時恢復作品原本數值及預覽。編輯儲存後，明確切到套用模式會複製已保存預設到作品，即使只有一個預設也可套用；若是放棄未儲存草稿才切模式，則保留原作品，不套用草稿或替代預設。
- 未儲存的編輯在切模式、換預設、換設定子頁、返回首頁及正常關閉時確認放棄。取消保留草稿與選取，不默默儲存。不新增取消按鈕。
- 只有編輯模式、有效且非內建的預設能刪除。內建以 DefaultId 保護，不以可修改的名稱判定。

## 永久刪除

ContentDialog 標題、說明、勾選及兩個按鈕採需求文案。每次建立新的未勾選請求，預設焦點及 DefaultButton 均為取消。未勾選時邏輯禁用且使用原生 Disabled 反灰；勾選才轉為紅色並可提交，取消勾選即撤回授權。

對話框顯示保存快照的名稱，請求包含同一個 ID，最後確認重新驗證模式、存在性、保護、快照及勾選。對話框期間不能切換預設／模式，提交時兩個按鈕及勾選均暫停，阻止重複提交。取消不改預設、作品或草稿；失敗保留資料並在對話框顯示錯誤。

先完成持久化刪除才更新清單與選取。刪除目前來源預設不改作品數值，選其他有效項目但不套用；沒有項目時維持空選取。設定格式升為 4，舊版 2／3 依既有資料遷移，格式 4 不再自動重建空清單。一般偏好保存也經過同一交易閘門，避免舊排隊快照把預設寫回。

危險按鈕使用局部 ThemeDictionaries，只覆寫啟用／Hover／Pressed 的顏色，保留原生 Disabled、焦點與高對比。ContentDialog 動態附加樣式後重新套用原生 Disabled 狀態，不曾暫時啟用未勾選的按鈕。依據 [Microsoft 輕量樣式](https://learn.microsoft.com/windows/apps/design/style/xaml-styles) 與 [原生對話框行為](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs)。

## 修改位置

- `src/PhotoMargin.App/Views/SettingsPage.xaml`／`.xaml.cs`：Figma 結構、實際預覽、模式／選取確認、刪除彈窗、窄視窗。
- `src/PhotoMargin.App/Views/DangerButtonResources.xaml`：局部主題與危險操作顏色，沒有重寫模板。
- `src/PhotoMargin.App/ViewModels/RuleEditorViewModel.cs`：模式、獨立草稿、儲存／另存、ID 綁定刪除。
- `src/PhotoMargin.App/ViewModels/MainViewModel.cs`：作品／草稿隔離、原子預設交易、保存序列、啟動還原。
- `src/PhotoMargin.App/Services/SettingsStore.cs`：設定格式 4 與空清單遷移。
- `src/PhotoMargin.App/MainWindow.xaml.cs`：共用預覽影像、關閉前確認草稿。
- Debug 測試入口及正式邏輯／WinUI 測試；README、ARCHITECTURE 更新。

## 驗證

- Debug／Release solution 建置成功，0 警告、0 錯誤。
- 正式測試：Core 39、Imaging 17、App.LogicTests 21，共 **77 項通過**。涵蓋套用全部值不回寫、另存同名拒絕、草稿確認／取消、穩定 ID 保存與失敗重試、模式／內建保護、勾選撤回、請求重用／變更失效、先寫檔後移除、排隊保存不復活預設、最後項目刪除及重啟空清單、實際渲染與取消復原。
- 新 WinUI 預設流程 **26 項通過**；真正新程序的預設還原 **4 項通過**。確認兩種模式、原生控件繫結、取消後再次切換、保存後選取、紅色及 Disabled 實際背景筆刷、取消焦點、勾選後才可刪、重新開啟需重新勾選、刪除保留作品及重啟不重建。
- 既有 WinUI **147 項**及另一新程序排列還原 **4 項通過**，包含 Reload、返回首頁、主題、排列、匯出及來源保護。
- 本輪實測 **120 DPI／125%**；新版設定頁測試 1596×1256 與 800×700 實際視窗像素。這是記錄過 DPI 的原生尺寸比較，不宣稱與未知 DPI 的 Figma 圖逐像素相同。
- 已檢視深色設定頁、勾選前後刪除彈窗、淺色小視窗截圖，確認灰色／紅色與方形預覽，未發現重疊。

結果及截圖：`artifacts/PhotoMargin-presets-verification/`，包括 `preset-regression-result.json`、`preset-restore-result.json`、深淺主題及確認彈窗 PNG。測試只操作獨立偏好與生成照片，不更動正式使用者預設或照片。

[套用模式](../artifacts/PhotoMargin-presets-verification/preset-apply-dark.png) · [編輯模式](../artifacts/PhotoMargin-presets-verification/preset-edit-dark.png) · [未勾選反灰](../artifacts/PhotoMargin-presets-verification/preset-delete-unchecked.png) · [勾選後可刪除](../artifacts/PhotoMargin-presets-verification/preset-delete-checked.png) · [小視窗](../artifacts/PhotoMargin-presets-verification/preset-small-light.png)

執行 Debug 程式：先 `--verify-presets`，再用另一程序 `--verify-presets-restore`；Release 不含這些入口。

## 交付與未驗證項目

新執行包：`artifacts/PhotoMargin-presets-0.0.2-win-x64.zip`。完整解壓後執行 `PhotoMargin.App.exe`，不要只複製 EXE 或使用舊 ZIP。舊發布包未覆寫。

尚未在其他實際 DPI、Windows 10、跨螢幕、高對比系統模式或螢幕閱讀器做人工驗收；高對比資源及原生模板保留不等於已實測。Enter／Esc 採原生 DefaultButton 與鍵盤處理，已驗證預設／焦點／Disabled，但未模擬所有實體鍵盤路徑。磁碟失敗與重試已做邏輯注入，未人工操作所有磁碟錯誤彈窗。未以兩個完整程序同時編輯同一組偏好做壓力測試；沿用完整快照最後提交者優先政策。GUI 測試使用實際 Debug 程序；Release self-contained 發布及資源完整性已驗證，沒有宣稱再次人工操作過發布版。
