# 設定保護、另存選取與方案測試入口修正

本次只處理確認的前三項問題。保留上一輪未提交的文字超寬修正；未調整滑條保存合併、FileDialogService 或 LastOutputDirectory 接線，未 Commit／Push，版本仍為 0.0.2。

## 修改

1. `Services/SettingsStore.cs:50`：直接開檔，只有真正首次缺檔／缺目錄才回傳初始設定。鎖檔、存取錯誤、損壞 JSON、null、未知版本與無效父目錄均明確失敗；不把它們當成首次啟動。讀取與保存共用閘門，讀取失敗後 SaveAsync 拒絕覆寫。解除檔案鎖定本身不解除保護，必須成功重新讀取；失敗後檔案消失也不放行。
2. `ViewModels/MainViewModel.cs:206`：初始化／重試在偏好閘門內還原完整快照，尚未成功載入時，所有自動保存、預設保存／刪除及關閉 flush 都不寫入。成功重試取代清單、不重複追加，不將選取預設重新套到作品。`MainWindow.xaml.cs` 提供原生「重試／暫不重試」提示；暫不重試可繼續本次使用，但不保存設定，並提示重新啟動再試。設定頁的保存／刪除按鈕在此狀態停用。
3. `ViewModels/MainViewModel.cs:330`：同一份原子保存快照同時包含新預設和新 SelectedMarginRuleId；磁碟成功後才同步主畫面 ID 與編輯器。直接同步選取、不呼叫會重套版面的公開 setter，因此作品數值、照片、手動資訊及預覽保持。保存失敗時兩側 ID 及原檔都不變。
4. `PhotoMargin.slnx`：補入 App.LogicTests；已比較兩種方案清單，均為相同的 7 個專案。

## 驗證

- `.slnx` Debug 建置、`.sln` Release 建置：0 警告、0 錯誤。
- 兩種方案分別執行完整測試：各 **105 項通過**（Core 41、Imaging 17、App 邏輯 47），無遺漏應用程式邏輯測試。TRX 位於 `artifacts/PhotoMargin-settings-recovery-verification/final-slnx-tests/` 及 `final-sln-tests/`。
- 新增 13 個邏輯案例：鎖檔解除後仍防覆寫、損壞／null／未知版本、失敗後缺檔、正常首次啟動、直接／巢狀無效父路徑、讀取失敗時各保存入口被阻止、初始化中排隊保存不覆寫初始值、另存 ID 原子同步、另存失敗重試、實際照片／不同演示比例下不重套作品。
- 原生 WinUI Debug：重試／還原／另存同步 10、暫不重試保護 5、原預設流程 26、原完整 UI 流程 147，共 **188 個檢查通過**。使用隔離設定與樣本，不修改正式偏好。
- 已實際檢視重試提示截圖；Windows 11、120 DPI（125%）。其他 DPI、Windows 10、實體鍵盤與螢幕閱讀器未逐項人工驗證；沒有變更 NTFS ACL 進行人工存取拒絕測試。Release 執行包未在另一台乾淨電腦驗收。

[原生提示實際畫面](../artifacts/PhotoMargin-settings-recovery-verification/settings-retry-prompt.png)

原始 WinUI 結果位於 `artifacts/PhotoMargin-settings-recovery-verification/`。

## 執行包

`artifacts/PhotoMargin-settings-recovery-0.0.2-win-x64.zip`：完整解壓後執行 `PhotoMargin.App.exe`，不可只搬出 EXE。沒有覆蓋先前發布資料夾。
