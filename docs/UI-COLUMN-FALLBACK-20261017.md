# 文字超寬的左右分欄回退

## 行為

- 原本選左右分欄且任一資訊欄文字超寬時，重新使用置中堆疊／換行計算完整版面。
- 主畫面下拉選單同步改成「置中堆疊」，沿用偏好保存流程；預覽下方顯示「安全檢查失敗，無法使用左右分欄；已改為置中堆疊。」
- 原本已選置中時不切回分欄、不產生錯誤的分欄警告；Auto 維持原本的自適應行為。放得下的左右分欄保持不變。
- 分欄失敗是可恢復警告，不是忽略最終安全檢查。置中結果通過照片 contain、文字安全區、碰撞及畫布驗證後才允許匯出；仍無法容納時保持禁止匯出。
- 保留原 EXIF、照片、字體、字級與邊距。不裁切／拉伸照片，不截斷文字，不偷偷修改預設。
- 警告在後續有效渲染時保留；手動改選其他排列或成功更換來源照片時清除。警告本身不持久化，修正後的排列選擇會保存。

## 實作位置

- `Core/LayoutEngine.cs`、`Core/Models/LayoutModels.cs`：文字量測超出欄寬才回退，使用 `FellBackFromColumns` 與最終 `Errors` 區分「分欄不可用」和「置中仍不安全」。
- `App/ViewModels/MainViewModel.cs`：主畫面只接受最新預覽版本；直接同步已重新驗證的排列，不呼叫會取消目前結果的公開 setter。既有狀態列顯示警告，沒有增加對話框或功能入口。
- 排版量測移至背景，以 UI 當下的不可變設定／資訊快照計算；版本及取消檢查防止較早的回退覆蓋使用者剛改選的排列。
- `RuleEditorViewModel.cs`：設定頁演示沿用相同回退與警告，但不把演示草稿的回退寫入主畫面排列。
- 新增 Core、ViewModel 及隔離 WinUI 回歸測試；Debug 的 `--verify-column-fallback` 僅供測試，不包含於 Release。

## 驗證結果

- Debug／Release 建置成功，0 警告、0 錯誤。
- Release 全套 **92 項通過**：Core 41、Imaging 17、App 邏輯 34。原始 TRX 位於 `artifacts/PhotoMargin-column-fallback-verification/test-results/`。
- WinUI 實際執行 **13 個檢查通過**，Windows 11、DPI 120（125%）、1344×892。
- 以獨立 4:3 樣本及長鏡頭 EXIF，配合 Consolas、26／20 字級及本次截圖的邊距配置，驗證首次匯入會產生可見預覽，選單同步、警告可見、有效置中 JPG 可匯出、來源位元組不變。
- 驗證再次選取不適用的左右分欄不會形成渲染迴圈；Auto／已置中不誤報；可容納的分欄仍保留；置中後仍因文字／邊距無法容納時，匯出維持 Disabled。
- 使用可暫停的文字量測器，確認背景計算尚未完成時 UI 仍能改選 Auto，舊分欄回退結果不會覆蓋新選取。
- 實際匯出與同設定的置中核心結果比較，像素誤差為 0；不修改來源檔。

本次未直接取得使用者原始 JPG；使用自行產生的樣本驗證相同邏輯。其他 DPI、Windows 10、螢幕閱讀器與高對比未重做實機檢查；GUI 證據來自 Debug 程序，不宣稱在乾淨電腦完成 Release 部署驗收。

[實際畫面](../artifacts/PhotoMargin-column-fallback-verification/column-fallback-centered.png) · [WinUI 檢查結果](../artifacts/PhotoMargin-column-fallback-verification/column-fallback-result.json)

## 執行包

`artifacts/PhotoMargin-column-fallback-0.0.2-win-x64.zip`：完整解壓後執行 `PhotoMargin.App.exe`，不要只搬出 EXE。保留版本 0.0.2，沒有自動 Commit 或 Push。
