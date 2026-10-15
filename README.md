# PhotoMargin

目前版本：**0.0.1**。版本集中於 `Directory.Build.props`，主畫面狀態列從實際組件版本讀取。

PhotoMargin 是 Windows 攝影作品白框工具。第一版以橫向 JPG／PNG 為主，將照片完整放入 1:1 白色畫布，在底部加入可編輯的機身、鏡頭與 EXIF 資訊，並保護 4:5、5:4 置中裁切安全區。

正式流程為純 C#／.NET＋WinUI 3，不依賴 Python、WebView 或背景服務。

## 已完成功能

- 橫向 3:2、4:3、16:9、2:1 與方形來源的 contain 排版，不裁切、不拉伸。
- 套用 EXIF Orientation 1–8 後再判斷方向；校正後為直向時保留目前作品並提示稍後支援。
- 讀取 Make、Model、LensModel、焦距、光圈、快門、ISO 與拍攝時間；支援手動覆寫及重新讀取。日期／時間使用原生 CalendarDatePicker／24 小時 TimePicker，EXIF 缺少拍攝日期時使用來源檔案建立日期。
- 左右資訊欄自適應；過長時改為置中堆疊與換行，仍溢出時禁止匯出。
- 預覽固定顯示完整 1:1 成品，裁切與文字安全線可切換顯示；輔助線不會寫進 JPG。
- 1080、2048 與 320–8192 自訂正方形尺寸；JPG 品質 50–100。
- RGB、灰階、CMYK、ICC 與透明 PNG 解碼；輸出白底 sRGB JPG，不複製來源 EXIF／GPS。
- 設定版本化保存、Unicode 路徑、背景解碼／渲染、取消權杖與版本序號防止舊預覽覆蓋新狀態。
- 依 UI.rar 三張 Figma 畫面重建主畫面、右側面板與同視窗設定頁。齒輪進入一般設定；「新增規則」直接進入版面設定，不自動建立規則。
- 設定使用 NavigationView，依序提供一般設定、版面設定、相機資訊設定、匯出設定；搜尋可篩選這四個項目。返回保留來源照片及手動資訊。一般設定包含系統預設、深色、淺色主題。
- 未載入時顯示原生照片圖示、「拖入 PNG / JPG 照片」與匯入按鈕，右側設定原樣保留但全部停用反灰；齒輪與匯入可用，匯出不可用。載入後顯示實際白色正方形成品、恢復右側設定及右下更換照片／匯出圖片；預覽區持續支援拖曳匯入。
- 邊距規則可在版面設定內新增、編輯、刪除與保存；主畫面可選擇規則。機身、鏡頭、資訊、時間各自設定輸出字級；照片字體不影響應用程式 UI 字體。品牌圖設定明確停用。
- 照片資訊標題列只有一個圖示 Reload；手動資訊被覆蓋前確認，取消或讀取失敗均保留原值。Reload 不重設版面、字體、輸出或安全線。
- 工作頁左側及底部留白為 12 DIP、右側為 4 DIP、預覽與右側面板間距為 16 DIP；右側內容使用 12 DIP 右邊距及 4 DIP ScrollViewer 右內距，兼顧水平空間與原生捲軸展開時的安全間距。這些介面間距不影響照片輸出的白框設定。
- 禁止直接選用來源路徑作為輸出；一般輸出覆寫由 Windows 儲存對話框確認。

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

Figma 介面及操作流程的 WinUI 回歸檢查（僅 Debug 建置）：

```powershell
dotnet build .\src\PhotoMargin.App\PhotoMargin.App.csproj -c Debug
& .\src\PhotoMargin.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\PhotoMargin.App.exe --verify-settings
```

此檢查使用獨立設定檔與照片，在實際 WinUI 視窗驗證 Reload 確認／取消／失敗、EXIF 日期時間及缺值回退、規則管理與直達導覽、搜尋、主題、資料保留、字級及尺寸綁定、設定保存與舊版遷移。另實際匯出 JPG，確認正方形尺寸、移除 EXIF、來源未變與輔助線不進入成品。結果寫入同目錄 `ui-regression-result.json`，另產生實際 PNG 畫面。Release 不包含此測試入口。

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

本次 Figma 版另交付 `artifacts/PhotoMargin-figma-win-x64/` 及同名 ZIP，未覆寫既有發布包。執行時必須保留整個資料夾，不能只複製 EXE。

後續未載入畫面更新版位於 `artifacts/PhotoMargin-empty-state-win-x64/` 及同名 ZIP，調整與驗證見 [未載入介面紀錄](docs/UI-EMPTY-STATE-20261015.md)。

## 已知範圍

- 直向來源目前只提示稍後支援，不提供直向版型。
- 尚未支援批次、RAW、HEIC、自訂 logo、Instagram 登入或自動發布。
- Windows 10 1809、不同 DPI／中文字體以及正式發布包仍應在目標實機做最終視覺驗收；自動測試不代表所有 GPU、螢幕與字體組合皆已驗證。
