# RAW 逐欄來源確認（2026-10-19）

依 `RAW Manual.png` 修改既有原生 ContentDialog：標題、選取檔名、各欄「目前值／RAW」
兩個單選項，底部「套用／取消」維持原生樣式，取消為預設焦點。
保留必要的來源照片核對提醒；八個既有欄位都沿用同樣結構，不只展示機身範例。

- 移除原本全域「補缺值／覆蓋」選擇 UI，不搬到其他頁面。
- 每欄使用獨立 RadioButtons／RadioButton 群組，長內容換行。
  目前值依真實來源標成 JPG、PNG、手動修改、RAW 或建立日期回退。
- 預設保留已有／手動資訊，僅缺值或建立日期回退預選有效 RAW；RAW 缺值反灰。
- 內容區使用原生垂直捲軸，隨視窗高度調整；底部操作區不隨欄位捲走。
- `RawInformationSelection` 為不可變欄位集合；合併僅複製選取且 RAW 確實提供的值。
  其他欄位與來源不變。既有程式化補缺值／覆蓋策略轉成同一集合，沒有第二套合併演算法。
- 全部保留目前值時不建立／轉移 RAW 關聯；取消、缺值、未知欄位或過期確認不覆蓋資料。
- 延續來源／資訊／請求版本檢查、離頁取消、預覽更新與格式 9 快照保存。
  不更動照片、方向、版面或輸出設定，沒有新增套件或設定遷移。

## 修改檔案

`Services/PhotoInformation.cs`、`ViewModels/MainViewModel.RawInformation.cs`、
`Views/SettingsPage.RawInformation.cs`、`RawInformationTests.cs`、`RawInformationRegression.cs`。

## 驗證與發布

- Debug／Release 各 188 項（Core 59、Imaging 34、App Logic 95），結果見 TRX。
- 原生 WinUI：36 項流程與 7 項新程序還原，DPI 120（125%）。
  測試包含逐欄混合選取、群組互不干擾、缺值反灰、取消、預設補缺值、全欄採 RAW、
  窄視窗與末列捲動、主畫面同步、快照還原與原始 JPG／RW2 雜湊不變。
- 圖片參考中沒有逐欄完整內容及來源核對提醒；實作保留八個既有欄位與必要提醒，
  不留大量無功能空白，也不改原生控制項模板。
- 未全面驗證其他 DPI、高對比或實體滑鼠／鍵盤；互動驗證使用原生 AutomationPeer。

證據：`artifacts/raw-field-verification`；新版執行包：
`artifacts/PhotoMargin-raw-selection-0.0.3-win-x64.zip`。
版本維持 0.0.3，未 Commit／Push，未修改使用者目前的設定檔。
