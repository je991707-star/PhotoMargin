# Data、設定集與色彩模式

## 儲存位置

預設使用 `AppContext.BaseDirectory/Data`，不使用目前工作目錄。有效路徑由 `DataLocation.Current` 在同一次執行中只選擇一次；一般設定頁的唯讀欄位會顯示完整路徑。

本次發布版的首次啟動位置為 `artifacts/PhotoMargin-data-0.0.3-win-x64/Data`；解壓到其他位置後，則使用該執行檔旁的 Data。程式目錄沒有寫入權限或為封裝程式時，使用 `%LOCALAPPDATA%/PhotoMargin/Data`。磁碟已滿等一般寫入錯誤不會觸發切換位置。先前實際資料位置已確認為 `C:/Users/User/AppData/Local/PhotoMargin/settings.json`。

Data 內容：

- `settings.json`：色彩模式、照片輸出字體／字級、匯出偏好、選取狀態與其他既有偏好。保留本機作品 metadata 快照，不複製來源照片／RAW 的二進位檔案。
- `presets.json`：穩定 ID、名稱、完整四比例 × 兩排列配置、內建修訂及已刪除內建 ID。內建修改保存為完整個人快照，正常啟動不重新覆寫。空清單與刪除標記不會在重啟後復活。
- `Backups/`：更新前的成對備份，最近五組。非本程式建立的資料夾不參與輪替，也不會被遞迴刪除。
- `.data.lock`、必要時的交易暫存／journal：只供序列化及當機復原使用，不是可攜設定集內容。

原始內建八組值移至 `Assets/BuiltInMarginPresets.json` 嵌入唯讀組件資源，`BuiltInMarginPresets.Create()` 沿用原有 API。發布包不帶個人 Data；Git 也忽略 Data。發行腳本先產生不含 Data 的 ZIP，再複製既有本機安裝的 Data 供原子更新；舊資料目錄另保留於 previous 發布目錄。

## schema 與遷移

兩個資料檔均使用 `schemaVersion: 1` 及相同 `transactionId`。settings 內的既有偏好 DTO 保留版本 10，支援舊設定版本 2–10 的一次性遷移。資料格式版本、內建模板修訂 2 與程式版本 0.0.3 分開管理。

僅在新 Data 沒有設定／預設與備份時，讀取舊 `%LOCALAPPDATA%/PhotoMargin/settings.json`。保留 ID、名稱、八組值、字級、匯出與既有 RAW metadata 快照；不重新刷新已修改的舊內建模板。舊單組／四組資料沿用既有深複製遷移。原檔不刪除，已有有效 Data 後不再次載入舊資料。

## 交易與失敗保護

所有磁碟工作在背景執行，以每路徑 SemaphoreSlim 與跨程序 `.data.lock` 排序。相同保存快照會略過重寫，避免滑條大量事件輪替掉有效備份；已確認的匯入仍強制備份。

先寫入且 Flush 完整的新檔與舊檔復原副本，再發布 journal，最後逐一安全替換 settings／presets。失敗則回復原組；若程序中斷，下一次啟動依 journal 判斷完成或回復，絕不混用不同 transactionId。備份本身先寫入臨時資料夾，全部複製成功才公布為一組。

JSON 損壞時嘗試最近有效備份，原始內容先保存在 Backups 再恢復成對資料。沒有可用備份、無法備份、鎖檔或遇到較新格式時，保留原資料並禁止保存／提示錯誤，不改用空目錄或清空預設。

## 設定集

單一 ZIP，預設檔名 `PhotoMargin_Settings_yyyyMMdd_HHmmss.zip`，只含：

1. `manifest.json`：設定集 schema、兩個資料 schema、程式版本、UTC 匯出時間、兩個資料檔 SHA-256。
2. `settings.json`：可攜偏好；移除作品快照、RAW 關聯、最近作品、本機輸出路徑與作品專用版面。輸出字體若指定本機字體檔路徑則改用可攜預設；自訂檔名規則清理路徑字元。
3. `presets.json`：完整已保存預設與刪除狀態，不含編輯草稿。

匯出從已提交的成對資料取一致快照，不直接壓縮 Data。目的 ZIP 在完整寫入後才原子替換，取消／失敗清理暫存。使用只回傳路徑的新原生 SavePicker，不會先建立空檔。

匯入使用有界記憶體暫存，不解壓至檔案系統：只允許上述三個精確檔名，拒絕多餘／重複／巢狀／絕對路徑。ZIP 與總解壓大小上限 8 MiB，每個項目上限 4 MiB；再驗證 hash、版本、相同 transactionId、唯一有效 ID、刪除狀態及所有配置數值。已知舊偏好 DTO 可遷移，未知較新格式拒絕。

顯示原生「匯入設定集？」確認，預設取消。有未儲存草稿時先選擇儲存／放棄／取消；直到最終確認才執行儲存，放棄則等匯入成功才丟棄草稿。匯入為完整取代，不合併預設；保留目的電腦的路徑及目前照片／RAW／手動資訊。匯入的預設選取不觸發套用。

有目前作品時，匯入偏好與本機 `workLayout` 分開：主題及匯出偏好立即更新，既有作品的字體／字級／邊距／排列維持；輸出尺寸依新偏好等比渲染，不更動構圖。作品 metadata／版面只在重新載入同一路徑、大小及修改時間相符的照片時還原，不自動開檔。

## 色彩模式

唯一入口為一般設定的原生 ComboBox：跟隨系統／淺色／深色。保留舊 ThemePreference 的數值對應；新的選單順序不改寫舊使用者選擇。透過 WinUI RequestedTheme／ThemeResource 更新主畫面、設定及 ContentDialog；標題列使用原生 AppWindow 色彩 API，高對比時回復系統色彩。

使用 UISettings 色彩通知與本視窗的 WM_SETTINGCHANGE／WM_SYSCOLORCHANGE／WM_THEMECHANGED，切回 UI 執行緒更新。此環境未封裝程式訂閱 AccessibilitySettings.HighContrastChanged 會失敗，已改用桌面可用的 SystemParametersInfo 與視窗通知。手動模式不會被系統變更覆寫；白色成品畫布不受主題影響。主題偵測參考 [Microsoft 桌面主題文件](https://learn.microsoft.com/windows/apps/desktop/modernize/apply-windows-themes)。

## 驗證

- 整體 223 項測試通過（Core 59、Imaging 47、App Logic 117），包含實際 GH5 RW2／JPG、民國曆下的日期命名與輪替，以及現有使用者設定檔的唯讀遷移與原檔 hash 保持不變。
- 覆蓋一次性遷移、內建修改／刪除、八組配置、四個交易故障點、journal 中斷復原、損壞備份復原、保留五組及不刪無關資料夾、ZIP 內容／路徑／大小／ID／數值／新舊版本、取消與儲存草稿、匯入失敗、RAW／手動值與作品版面保持。
- Windows 原生首輪 16、跨程序還原 15、原有導覽 149 項檢查通過。1344×892、DPI 120；測試從不同工作目錄啟動，使用獨立 Data，不改動正式設定。結果與實際截圖位於 `artifacts/PhotoMargin-data-debug-win-x64`。
- 實際 Program Files ACL／MSIX、人工操作系統 ZIP 選檔／儲存／開啟資料夾、使用者真正切換系統主題／高對比、其他 DPI 與完整螢幕閱讀器尚未實機驗收。位置權限與封裝分支以隔離測試驗證，沒有修改 OS 全域偏好冒充人工驗收。
