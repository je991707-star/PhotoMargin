# 相機資訊純文字頁（2026-10-18）

依 `SettingUI Info.png` 保留既有導覽、標題與左上操作結構，不改主畫面版面。

## 本次修改

- 上方依序為「從 RAW 檔讀取 EXIF」、目前套用檔案，以及同行的
  「重新讀取 RAW 檔 EXIF」與「還原為 JPG 原本 EXIF」。使用原生按鈕與強調樣式。
  PNG 作品會顯示 PNG，不誤標成 JPG；窄視窗空間不足時按鈕自動分行。
- 移除相機設定頁全部 TextBox、NumberBox、日期與時間選擇器，改為原生 TextBlock
  「欄位／目前內容／來源」表；值可選取複製，長文字換行，缺值明確顯示「未提供」。
- 八欄沿用目前作品資料與來源，不另存第二份表單；來源區分 JPG／PNG EXIF、RAW 補入、
  主畫面手動修改，以及非 EXIF 的檔案建立日期。手動修改只留在主畫面，更新會立即同步表格。
- 還原是獨立語意：ContentDialog 預設取消，確認後回到作品照片已讀取的 EXIF 快照，
  清除 RAW 關聯與手動覆寫；拍攝日期缺值時保留明確標示的建立日期回退。
  不重新解碼照片，不改像素、方向、構圖、預設、字體或輸出設定。
  確認期間資訊變動或離頁時不套用過期結果。主畫面原有 Reload 仍保留 RAW 層。
- 修正既有內容容器的 ActualWidth 相互繫結：縮小後曾保留過寬尺寸，導致來源欄被截掉。
  現在依 ScrollViewer 寬度扣掉既有 Padding 計算，維持 720 上限、靠左及原生垂直捲軸。
- 設定格式保持 9，沒有重置／遷移預設。套用與還原照既有快照流程保存。

## 檔案

- `Views/SettingsPage.xaml`、`Views/SettingsPage.RawInformation.cs`：版面、原生確認與容器寬度。
- `ViewModels/MainViewModel.RawInformation.cs`、`MainViewModel.cs`：文字列、來源、原照片快照還原。
- `Services/PhotoInformation.cs`：唯讀列資料型別。
- `RawInformationTests.cs`、`RawInformationRegression.cs`：邏輯與實際 WinUI 驗證。

## 驗證

- Debug 建置零警告、零錯誤；Release 自包含發布成功。
- Debug／Release 各 **185 項**全部通過（Core 59、Imaging 34、App Logic 92），沒有略過。
  包含使用者提供的 GH5 RW2 與 JPG 唯讀測試。
- 原生 WinUI 流程 **28 項**、新程序還原 **7 項**通過；DPI 120（125%），
  含 1344×1000／1344×892、800×700 深淺色與小視窗來源欄實際邊界檢查。
- 確認無輸入框、可見文字與資料同步、RAW/JPG 來源、取消／確認還原、
  RAW 移走後快照可用、匯出及原始兩檔 SHA-256 不變。
- 尚未全面驗證其他 DPI／高對比或實體滑鼠與鍵盤；原生互動使用程式內 AutomationPeer，
  不以編譯結果代替上述 UI 驗證。

證據：`artifacts/camera-info-verification`；截圖在 `Native`，TRX 在 `FinalDebug`／`FinalRelease`。
新版執行包：`artifacts/PhotoMargin-camera-info-0.0.3-win-x64.zip`。
版本維持 0.0.3，本次沒有 Commit／Push，也沒有更動使用者正在使用的設定檔。
