# PhotoMargin

目前版本：**0.0.3**。組件版本集中於 `Directory.Build.props`，Windows 應用程式 manifest 同步標註；主畫面狀態列從實際組件版本讀取。

PhotoMargin 是 Windows 攝影作品白框工具。第一版以橫向 JPG／PNG 為主，將照片完整放入 1:1 白色畫布，在底部加入可編輯的機身、鏡頭與 EXIF 資訊；預設只保護直向 4:5 的左右裁切，核心仍支援明確選用的 5:4 保護。

正式流程為純 C#／.NET＋WinUI 3，不依賴 Python、WebView 或背景服務。

## 已完成功能

- 橫向 3:2、4:3、16:9、2:1 與方形來源的 contain 排版，不裁切、不拉伸。
- 套用 EXIF Orientation 1–8 後再判斷方向；校正後為直向時保留目前作品並提示稍後支援。
- 讀取 Make、Model、LensModel、焦距、光圈、快門、ISO 與拍攝時間；支援手動覆寫及重新讀取。日期／時間使用原生 CalendarDatePicker／24 小時 TimePicker，EXIF 缺少拍攝日期時使用來源檔案建立日期。
- 字體設定上方的「排列設定」提供自動（預設）、左右分欄、置中堆疊；選擇影響預覽與匯出並保存偏好。自動保留先分欄、放不下再堆疊的既有行為；指定左右分欄時若文字超寬，改為置中堆疊、同步下拉選單並顯示「安全檢查失敗，無法使用左右分欄」。置中重新驗證通過後可預覽及匯出；若仍超出安全範圍，繼續禁止匯出。
- 預覽固定顯示完整 1:1 成品，裁切與文字安全線可切換顯示；輔助線不會寫進 JPG。
- 1080、2048 與 320–8192 自訂正方形尺寸；JPG 品質 50–100。
- RGB、灰階、CMYK、ICC 與透明 PNG 解碼；輸出白底 sRGB JPG，不複製來源 EXIF／GPS。
- 設定版本化保存、Unicode 路徑、背景解碼／渲染、取消權杖與版本序號防止舊預覽覆蓋新狀態。
- 設定讀取失敗時顯示原生重試提示並阻止保存，不以預設值覆蓋原設定；成功重新讀取後才恢復保存。另存預設時同步主畫面／編輯器的選取 ID，與預設內容一併提交。
- 依 UI.rar 三張 Figma 畫面重建主畫面、右側面板與同視窗設定頁。齒輪進入一般設定；「新增規則」直接進入版面設定，不自動建立規則。
- 設定使用 NavigationView，依序提供一般設定、版面設定、匯出設定；搜尋可篩選這三個項目。相機資訊設定子頁已移除，照片資訊編輯與重新讀取仍在主畫面。所有設定返回入口一律一次回到既有主畫面，不逐一退回子頁；來源照片、手動資訊、排列及其他設定保持不變。一般設定包含系統預設、深色、淺色主題。
- 未載入時顯示原生照片圖示、「拖入 PNG / JPG 照片」與匯入按鈕，右側設定原樣保留但全部停用反灰；齒輪與匯入可用，匯出不可用。載入後顯示實際白色正方形成品、恢復右側設定及右下更換照片／匯出圖片；預覽區持續支援拖曳匯入。
- 版面設定採左側成品預覽、右側預設與六個滑條。套用模式只複製到作品，可用不同名稱另存新預設；編輯模式使用獨立草稿並以原 ID 儲存，離開未儲存草稿時確認放棄。刪除僅在編輯模式開放，內建預設受保護；每次永久刪除需重新勾選確認，寫檔成功才更新清單，並保留作品已套用的數值。
- 版面設定即使無照片也顯示離線內建的 4:3 示範成品；圖片比例可選 16:9、5:4、4:3、3:2，比例下方的排列設定可選左右排列／垂直排列，畫布始終 1:1。每個預設保存 4×2 八組獨立配置，滑條即時渲染並持續顯示 1080 基準 px 數值。「文字與左右邊界」只內縮裁切可見區左右，不改上下；「下方邊距」直接定位可見文字底邊，0 時只保留最小字形繪製餘量；「照片與文字間隔」控制照片底部到文字頂部的垂直距離；「焦距、曝光三要素間隔」控制四個可見資訊項目的水平距離，不改字距或行距。缺值不留空位。設定頁裁切只用於演示副本，不污染照片／EXIF，也不裁切正式匯出。
- 主畫面可選擇規則。機身、鏡頭、資訊、時間各自設定輸出字級；照片字體不影響應用程式 UI 字體。品牌圖設定明確停用。
- 內建八組預設由 `BuiltInMarginPresets.Create()` 統一建立，數值依比例／左右或垂直排列獨立保存。鏡頭字級新預設 16px、可選／輸入 8–100px；其他字級仍為 18–100px。沒有自動縮字，長名稱沿用換行與置中回退；已保存字級不強制改成新預設。
- 照片資訊標題列只有一個圖示 Reload；手動資訊被覆蓋前確認，取消或讀取失敗均保留原值。Reload 不重設版面、字體、輸出或安全線。
- 工作頁左側及底部留白為 12 DIP、右側為 4 DIP、預覽與右側面板間距為 16 DIP；右側內容使用 12 DIP 右邊距及 4 DIP ScrollViewer 右內距，兼顧水平空間與原生捲軸展開時的安全間距。這些介面間距不影響照片輸出的白框設定。
- 禁止直接選用來源路徑作為輸出；一般輸出覆寫由 Windows 儲存對話框確認。

## 內建預設模板參數

同一個「預設規則」包含四種來源圖片比例 × 左右／垂直兩種資訊排列，共 **八組獨立配置**。以下數值均為 **1080×1080 成品的基準 px**；其他輸出尺寸依 `OutputSize / 1080` 等比例換算，不受視窗 DPI 影響。圖片比例是來源照片比例，成品畫布始終為 1:1，正式照片完整顯示、不裁切或拉伸。

### 垂直排列

| 設定 | 16:9 | 5:4 | 4:3 | 3:2 |
| --- | ---: | ---: | ---: | ---: |
| 左右邊距 | 40 | 48 | 48 | 48 |
| 上方邊距 | 100 | 50 | 100 | 100 |
| 下方邊距 | 150 | 70 | 100 | 106 |
| 照片與文字間隔 | 50 | 50 | 32 | 50 |
| 焦距、曝光三要素間隔 | 20 | 20 | 20 | 18 |
| 文字與左右邊界 | 24 | 24 | 24 | 24 |

### 左右排列

| 設定 | 16:9 | 5:4 | 4:3 | 3:2 |
| --- | ---: | ---: | ---: | ---: |
| 左右邊距 | 40 | 48 | 48 | 48 |
| 上方邊距 | 100 | 50 | 100 | 100 |
| 下方邊距 | 170 | 90 | 112 | 136 |
| 照片與文字間隔 | 50 | 50 | 50 | 50 |
| 焦距、曝光三要素間隔 | 18 | 18 | 18 | 18 |
| 文字與左右邊界 | 24 | 24 | 24 | 24 |

- 「左右邊距」是照片的最小左右留白；「上方邊距」是照片的最小上方留白，實際照片位置仍受比例與資訊區配置影響。
- 「下方邊距」控制最下方可見文字至畫布底邊的距離；明確啟用的上下裁切保護仍優先。
- 「照片與文字間隔」控制照片底邊與最上方可見文字之間的垂直距離。
- 「焦距、曝光三要素間隔」控制焦距、光圈、快門、ISO 可見項目間的水平距離，不是文字內部字距。
- 「文字與左右邊界」是裁切可見區左右邊緣向內的文字留白，不與照片邊距合併，也不內縮上下。

八組均預設啟用直向 **4:5 左右裁切保護**，不啟用橫向 5:4 上下裁切保護。新安裝的照片文字字級為：機身 **28px**、鏡頭 **16px**、資訊 **22px**、時間 **22px**。鏡頭可設定 8–100px，其他字級為 18–100px；不自動縮字，已有偏好保留。

正式資料來源是 [`BuiltInMarginPresets.Create()`](src/PhotoMargin.App/Services/BuiltInMarginPresets.cs)。主畫面按校正 Orientation 後的照片比例及目前資訊排列選取配置；非標準比例沿用最接近比例匹配，不改變原照片比例。套用只複製數值，不回寫預設。

### 0.0.3 更新摘要

- 完成八組配置、獨立草稿、即時示範預覽及固定數值顯示。
- 調整文字左右留白、可見文字底邊定位、照片與文字間隔，以及曝光資訊項目間距。
- 修復既有格式 8／內建修訂 1 未套用新表：先完整保留「更新前備份」，再更新內建修訂 2；不重複建立備份，不覆寫其他自訂預設或手動作品。
- 鏡頭預設 16px、下限 8px，不加入自動縮字；保留既有有效字級與安全溢出處理。
- 加強設定讀取失敗防覆寫、預設選取同步，並移除相機資訊設定子頁；主畫面 EXIF 編輯與 Reload 保留。

設定格式仍為 **8**、內建模板修訂為 **2**，兩者與應用程式版本 **0.0.3** 分別管理。更新時先關閉舊版，避免舊程序保存過期快照。詳細原因與驗證見 [內建配置修復紀錄](docs/UI-BUILTIN-FIX-20261017.md)。

## 系統與工具版本

- Windows 10 1809（build 17763）以上，x64；目前實際建置環境為 Windows 11 build 26100。
- .NET SDK 10.0.401。
- Windows App SDK 2.5.1，self-contained unpackaged 部署。
- Magick.NET-Q8-x64 14.16.0、MetadataExtractor 2.9.3。

套件版本集中鎖定於 `Directory.Packages.props`。影像套件的選型與授權記錄見 `docs/ARCHITECTURE.md`。

## 建置與執行

在 x64 Developer PowerShell 或一般 PowerShell 執行：

```powershell
dotnet restore .\PhotoMargin.sln
dotnet build .\PhotoMargin.sln -c Debug
dotnet run --project .\src\PhotoMargin.App\PhotoMargin.App.csproj
```

如果程式在啟動階段發生未處理例外，Debug／Release 輸出目錄會留下 `startup-error.log`。

## 測試

```powershell
dotnet test .\PhotoMargin.sln -c Debug
```

測試覆蓋裁切幾何、縮放、安全區、方向政策、長文字自適應、EXIF 顯示格式、透明 PNG、CMYK、Orientation 旋轉與來源檔防覆寫。

`PhotoMargin.sln` 與 `PhotoMargin.slnx` 均包含 Core、Imaging 及 App.LogicTests；使用任一方案都會執行完整的三個測試專案。

設定讀取失敗防覆寫與另存選取同步的實作／驗證見 [設定保護修正紀錄](docs/SETTINGS-RECOVERY-20261017.md)。

另包含直接連結正式 ViewModel／保存服務的邏輯測試，覆蓋預覽失敗後恢復、保存錯誤、規則保存／刪除重試、匯出部分成功、關閉時待寫設定、多個儲存實例及小數逗號地區。來源防覆寫測試包含 NTFS junction 與 hard link，測試只使用自行產生的圖片。

發布失敗與替換回復測試（隔離測試目錄、模擬建置結果，不動正式發布包）：

```powershell
powershell.exe -NoProfile -File .\tests\packaging\PublishRegression.ps1
```

Figma 介面及操作流程的 WinUI 回歸檢查（僅 Debug 建置）：

```powershell
dotnet build .\src\PhotoMargin.App\PhotoMargin.App.csproj -c Debug
& .\src\PhotoMargin.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\PhotoMargin.App.exe --verify-settings
```

此檢查使用獨立設定檔與照片，在實際 WinUI 視窗驗證 Reload 確認／取消／失敗、EXIF 日期時間及缺值回退、規則管理與直達導覽、搜尋、主題、資料保留、字級及尺寸綁定、設定保存與舊版遷移。另實際匯出 JPG，確認正方形尺寸、移除 EXIF、來源未變與輔助線不進入成品。結果寫入同目錄 `ui-regression-result.json`，另產生實際 PNG 畫面。Release 不包含此測試入口。

版面預設的隔離 WinUI 測試提供 `--verify-presets`、`--verify-presets-restore`；八組配置提供 `--verify-aspects`、`--verify-aspects-restore`；底部定位使用 `--verify-bottom-margins`；新內建表與鏡頭字級使用 `--verify-builtin-lens`、`--verify-builtin-lens-restore`。設定格式 8 接受 2–7，revision 0 內建資料只升級未修改的配置。修復版的內建 revision 2 不再依賴 JSON 格式版本：revision 1 的異動內容先完整另存為自訂備份，再更新內建八組表，後續啟動不重複備份／覆寫。自訂預設及手動作品保留；測試使用 `--verify-builtin-update`、`--verify-builtin-update-restore`。舊版雙重裁切遷移規則維持，仍不自動啟用 5:4。格式 4 以上的空清單不重建。見 [新內建預設與鏡頭字級交付紀錄](docs/UI-BUILTIN-LENS-20261017.md) 與 [既有內建資料修復](docs/UI-BUILTIN-FIX-20261017.md)。

另檢查預設／較窄視窗、原生捲軸展開時不遮住控制項、固定操作區、正方形預覽、原生時間選擇器完整顯示，以及設定側欄的收合／展開。DPI 與實際驗證範圍見 [本次交付紀錄](docs/UI-FIGMA-20261015.md)。

重新建立範例成品：

```powershell
dotnet run --project .\tools\PhotoMargin.SampleGenerator\PhotoMargin.SampleGenerator.csproj -c Release
```

輸入與成品會放在 `samples/input`、`samples/output`。

## 發布

```powershell
.\packaging\publish.ps1
```

輸出為：

- `artifacts/PhotoMargin-win-x64/`
- `artifacts/PhotoMargin-win-x64.zip`

發布採 x64 self-contained 資料夾，不承諾單一 EXE。解壓後執行 `PhotoMargin.App.exe`。來源照片不會被修改。

發布腳本先在獨立 staging 目錄完成建置、檢查 EXE 並壓縮，成功後才替換正式輸出。上一版保留為 `PhotoMargin-win-x64.previous-<id>` 及同名 ZIP；替換失敗會嘗試回復，不先刪除上一版。

2026-10-16 邏輯修復及驗證見 [Bug 修復紀錄](docs/BUGFIX-20261016.md)，修復版執行包為 `artifacts/PhotoMargin-bugfix-win-x64.zip`。

本次 Figma 版另交付 `artifacts/PhotoMargin-figma-win-x64/` 及同名 ZIP，未覆寫既有發布包。執行時必須保留整個資料夾，不能只複製 EXE。

後續未載入畫面更新版位於 `artifacts/PhotoMargin-empty-state-win-x64/` 及同名 ZIP，調整與驗證見 [未載入介面紀錄](docs/UI-EMPTY-STATE-20261015.md)。

## 已知範圍

- 直向來源目前只提示稍後支援，不提供直向版型。
- 尚未支援批次、RAW、HEIC、自訂 logo、Instagram 登入或自動發布。
- Windows 10 1809、不同 DPI／中文字體以及正式發布包仍應在目標實機做最終視覺驗收；自動測試不代表所有 GPU、螢幕與字體組合皆已驗證。
