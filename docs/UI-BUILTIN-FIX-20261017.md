# 既有內建配置未更新：修復紀錄

## 原因與修正

實際設定檔已保存為格式 8／內建 revision 1，但八組都是下方邊距 50px，而非指定新表。上一版只在格式小於 8 時遷移，且完整比對歷史預設 48／36／72／24，因而將這份資料判定為修改過、不套用新表，仍標記為遷移完成。

- `BuiltInMarginPresets.cs`：內建表修訂號升至 2。revision 1 舊內建內容先完整保存成「預設規則（更新前備份）」自訂預設，再換成正確八組表；名稱碰撞會加序號。已相符者不建立備份；revision 2 不再次覆寫修改。
- `SettingsStore.cs`：依內建修訂號判斷，不受格式 8 阻擋。revision 0 舊配置延續逐組保留邏輯。只有仍相符的作品副本跟隨更新，手動數值及字體／EXIF／輸出偏好不變。已有自訂預設 ID 與內容保持。
- `BuiltInPresetTests.cs`：重現實際保存形狀，檢查備份、名稱碰撞、手動作品保留、正確排列套用、revision 0 在格式 8 可更新、revision 2 不再覆寫，以及保存重啟不重複。
- Debug 原生測試 `--verify-builtin-update`／`--verify-builtin-update-restore` 以獨立設定檔重現問題，逐一操作八組原生選單，驗證修復後數值及示範預覽。

不新增控制項、不修改 Figma 版面、不修改圖片排版演算法。數值表仍見 `UI-BUILTIN-LENS-20261017.md`。未直接寫入使用者 AppData；修復由新版啟動時正常讀取／保存流程完成。

## 使用新版

請先關閉舊版（避免它再保存舊快照），完整解壓修復包，執行其中的 `PhotoMargin.App.exe`。版面設定的「預設規則」是新內建表；「更新前備份」保留先前內容，可依既有流程選用。已手動調整的作品不會被強制重置；需要套用新內建時，依既有選單切換到預設規則／排列即可。

發布包：`artifacts/PhotoMargin-builtin-fix-0.0.2-win-x64.zip`，應用程式版本仍為 0.0.2；不 Commit、不 Push。

## 本次驗證

- Debug App 建置成功，零警告、零錯誤；`git diff --check` 通過。
- Debug App Logic **78 項**通過；Release 全部 **168 項**通過（Core 59、Imaging 31、App Logic 78），無失敗或略過。
- Windows 原生修復／重啟 **25 項**與內建表／鏡頭回歸 **56 項**，共 **81 項**通過。實際用原生比例／排列 ComboBox 切換全部八組，檢查六欄數值及非空示範預覽；重新啟動後備份仍只有一份且 ID 不變。
- 原生執行 DPI 120（125%）；已檢視 `builtin-update.png` 的實際設定頁。TRX、JSON、前後程序截圖位於 `artifacts/PhotoMargin-builtin-fix-verification/`。
- Release 自包含發布；封裝檢查 XBF／PRI 及依賴存在，並核對 ZIP 與發布 DLL 的 SHA-256 一致。
- 未測其他 DPI、Windows 10、高對比／螢幕閱讀器、完整人工鍵盤操作或乾淨系統部署；沒有宣稱完成這些驗收。
