# 移除相機資訊設定頁（2026-10-17）

## 修改範圍

- `Views/SettingsPage.xaml` 移除相機資訊設定的 NavigationViewItem 與整段內容，不是隱藏或搬到其他位置。
- `Views/SettingsPage.xaml.cs` 移除 Camera 導覽列舉值與內容切換程式碼。
- 剩下的設定依序為一般設定、版面設定、匯出設定，沿用現有搜尋、主題與直接返回首頁行為。
- 主畫面照片資訊、EXIF 讀取／Reload、缺少日期時的來源建立時間回退及其來源狀態保持不變，無設定資料遷移或清除。
- 更新 README 與原生 WinUI 回歸檢查，確認已刪控制項不存在、搜尋不會找到舊入口，並驗證三個頁面的返回行為。

## 驗證

- Debug（PhotoMargin.slnx）及 Release（PhotoMargin.sln）建置成功：零警告、零錯誤。
- Debug 自動測試：105 項通過（Core 41、Imaging 17、App Logic 47）。
- Windows 原生 `--verify-settings`：148 項通過；DPI 120（125%），包含三項導覽的順序／刪除檢查、搜尋、返回首頁、作品狀態保留、EXIF Reload、不同視窗尺寸及實際 JPG 匯出。
- 實際檢視設定頁截圖，確認側欄僅有三個設定項目。
- 未另驗證其他 DPI、Windows 10、高對比與螢幕閱讀器；不將上述結果視為所有環境驗收。

## 執行

發布包：`artifacts/PhotoMargin-no-camera-settings-0.0.2-win-x64.zip`。
完整解壓後執行 `PhotoMargin.App.exe`，不要僅複製 EXE；版本維持 0.0.2。
此發布包包含目前工作樹先前已完成的修正，本次沒有 Commit 或 Push。
