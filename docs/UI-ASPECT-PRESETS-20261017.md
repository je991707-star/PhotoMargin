# 四比例版面設定與離線預覽交付

本次延續現有未提交的預設套用／編輯功能，不變更版本號（0.0.2），未執行 Commit／push。

## 修改摘要

- `Views/SettingsPage.xaml`：在邊距規則正下方新增原生圖片比例 ComboBox；五個滑條各有固定 80 DIP、右對齊的唯讀 px 數值。仍維持預覽在左、編輯區在右；小視窗沿用既有上下排列及垂直捲動。
- `RuleEditorViewModel`：同一份草稿包含四配置，比例切換不丟棄其他配置。滑條直接更新目前配置，儲存不是預覽的觸發條件。套用／編輯／同名拒絕／放棄／刪除限制沿用。
- `MarginRule`、`RatioConfigurations`：以不可變 record 表達 ID、名稱、四份獨立配置。每份保存左右、上方、照片資訊間距、底部安全距離、裁切文字額外留白，以及兩個裁切安全保護值。既有字體、字級、排列及輸出設定不屬於邊距預設，仍使用既有作品偏好。
- `SourceAspects`：對已校正 Orientation 的來源使用寬高比距離選最近配置；常見比例的像素四捨五入仍落在相同比例。其他比例不改動照片像素或原始比例。
- `LayoutPreviewSource`：使用現有自有示範 PNG（`samples/input/sample_4x3.png`），以 EmbeddedResource 內嵌於 Imaging 組件，部署後不需網路／原始碼資料夾。使用者照片只建立縮小副本，中心裁切只存在設定預覽。
- `MainViewModel`、`SettingsPage.xaml.cs`、`MainWindow.xaml.cs`：設定預覽與主畫面影像來源分開。設定頁的 720×720 合成使用相同 LayoutEngine／ImageRenderer；主畫面及匯出仍使用原始校正照片的 contain 排版。示範 metadata 不寫入實際 Camera／Lens／日期等欄位。
- 預覽使用單一序列工作合併輸入，拖曳時產生漸進畫格；比例、預設、來源的世代檢查排除過期結果，最後一定處理最新值。來源副本按來源版本快取，不逐次解碼大圖。錯誤保留上次有效預覽並顯示原因。
- 修正快速返回再進設定時，舊頁面延後 Unloaded 停止新頁面預覽的競態，並加入回歸測試。

## 資料遷移

設定格式升為 **5**，支援讀取 2、3、4、5。

舊預設沒有 `configurations` 時，將原五個數值及兩個保護選項分別複製到 `sixteenByNine`、`fiveByFour`、`fourByThree`、`threeByTwo`；保留 ID、名稱及原值。首次保存寫入新版；日後優先使用已保存的分比例配置，不再以舊平面欄位覆蓋。平面欄位保留為 4:3 相容快照。每組為獨立不可變 record，非共用可變字典。

`demonstrationAspect` 獨立保存演示選取；缺值／未知值回到 4:3。變更此偏好只保存現有已保存預設清單，不偷偷保存編輯草稿。格式 4／5 的空清單維持空白，不復活已刪除預設。正式作品數值仍由 `Layout` 還原，不在啟動時自動重套選取預設。

套用模式手動調整只複製數值至作品；比例選單只切換演示。正式從主畫面／設定選取預設時，按實際來源比例選配置。編輯草稿不進入主畫面預覽、正式匯出或一般偏好自動保存；明確儲存才更新同一 ID 的整份四比例預設。刪除移除整個 ID，原生確認與每次重新勾選保持不變。

## 驗證

- Debug／Release 建置成功，0 警告、0 錯誤。
- 自動測試：Core 39、Imaging 17、App 邏輯 30，共 **86 項通過**。
- 新增覆蓋：離線示範四種精確裁切比例／正方形合成、完整四配置草稿與跨重啟保存、整份放棄、來源 Orientation／四捨五入與最近比例、持續滑動期間出現預覽畫格、快速變更最終一致、未儲存幾何變化、主畫面／EXIF／來源檔不污染，以及舊頁面延後卸載競態。
- Windows 11 原生 WinUI Debug 實際執行：新比例流程 19、另開程序還原 5、原預設流程 26、預設重啟 4、原完整 UI／匯出流程 147，共 **201 個檢查通過**。
- 原生滑條以 UI Automation RangeValue 路徑操作，確認雙向資料、固定數值及未儲存渲染；未將此稱為實體鍵盤操作測試。
- DPI **120（125%）**；新畫面 1344×892、800×700，另回歸既有 1596×1256、1280×820、1024×768／最小視窗。已檢視深／淺色實際截圖。右側保留原生垂直捲動、底部按鈕不另搬位置。
- 實際 JPG 匯出確認正方形、來源不變、EXIF 不轉存、輔助線不進入成品。獨立示範在主畫面仍無照片時可渲染，無需任何網路服務。

原始 JSON 證據與截圖：`artifacts/PhotoMargin-aspect-verification/`。

- [無照片 4:3 深色畫面](../artifacts/PhotoMargin-aspect-verification/aspect-empty-4x3-dark.png)
- [16:9 示範畫面](../artifacts/PhotoMargin-aspect-verification/aspect-16x9-dark.png)
- [淺色畫面](../artifacts/PhotoMargin-aspect-verification/aspect-light.png)
- [小視窗](../artifacts/PhotoMargin-aspect-verification/aspect-small.png)
- [新程序還原](../artifacts/PhotoMargin-aspect-verification/aspect-restarted.png)

## 尚未完成的實機檢查

其他 DPI／跨螢幕縮放、Windows 10 裝置、實體鍵盤完整 Tab／方向鍵／Enter／Esc 操作、螢幕閱讀器及高對比主題尚未逐項人工驗證。保留原生控制項、焦點、Disabled 與主題資源不等於已完成上述驗收。

Release 發布包不包含 Debug 自動化入口；本次 GUI 行為證據來自同份正式 ViewModel／XAML 的 Debug 程序，不宣稱已在另一台乾淨機器測試 Release 部署。

## 可執行包

`artifacts/PhotoMargin-aspect-presets-0.0.2-win-x64.zip` 為獨立 x64 self-contained 發布包。解壓後執行 `PhotoMargin.App.exe`，不要只搬出 EXE；不覆蓋既有發布資料夾。
