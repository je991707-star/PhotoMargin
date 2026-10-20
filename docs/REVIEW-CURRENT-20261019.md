# 目前修改檢查與執行包（2026-10-19）

## 結論

未發現本次修改引入的新錯誤，不需要還原使用者修改，也沒有為了通過測試改動正式程式碼。
本次改動主要為設定頁提示文字精簡與刪除一行說明，既有繫結、事件、資料合併與排版流程保持。

除了 Git 差異，也以最後交付的 `PhotoMargin-raw-selection-0.0.3-win-x64` 中 Portable PDB
來源雜湊比對目前原始碼，避免把先前尚未提交的 RAW 功能誤當成這次修改：

- 33 個來源檔一致；有改動的是 `SettingsPage.xaml`、`SettingsPage.xaml.cs`、
  `SettingsPage.RawInformation.cs`。
- 檢查 Tooltip、確認對話框文字、已移除說明與相關綁定，沒有事件失聯、XAML 語法錯誤，
  也沒有為精簡文案而取消確認／改變刪除與還原的執行條件。
- 新發布包再與當前原始碼比對：36 個來源檔雜湊一致，沒有舊編譯檔混入。

## 驗證

- WinUI Debug 建置：零警告、零錯誤；Release 自包含發布成功。
- Debug／Release 各 188 項全部通過：Core 59、Imaging 34、App Logic 95，沒有略過。
- 原生 WinUI 共 223 項通過：一般設定與主畫面 149、預設 27＋重啟 4、RAW 36＋重啟 7。
- 包含深淺色、視窗縮放、單次返回首頁、預設編輯／取消／儲存／刪除、RAW 逐欄套用／取消／
  還原原照片資訊、缺值、來源標示、快照保存、預覽及實際匯出。
- 使用已提供的 GH5 RW2／JPG 唯讀驗證，原檔雜湊不變；所有測試採獨立設定，未改使用者設定檔。
- 證據：`artifacts/review-current-verification`，含 TRX、原生 JSON 與實際截圖。

上述原生測試在 DPI 120（125%）執行；其他 DPI、高對比、外部實體滑鼠／鍵盤全流程，
以及全新 Windows 電腦部署尚未全面驗證。測試通過不是宣稱所有情境都不存在問題。

## 執行檔

版本維持 0.0.3，未 Commit／Push，未覆寫舊發布資料夾。

- `artifacts/PhotoMargin-reviewed-0.0.3-win-x64/PhotoMargin.App.exe`
- `artifacts/PhotoMargin-reviewed-0.0.3-win-x64.zip`

這是 WinUI 3 自包含資料夾部署，不是可單獨搬走的單一 EXE。請完整解壓 ZIP，再執行其中的
`PhotoMargin.App.exe`；DLL、WinUI XBF／PRI 與其他資源須保持在原資料夾結構。
