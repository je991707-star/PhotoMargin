# 相機資訊設定與 RAW EXIF 補入

## 實作

- 恢復 NavigationView「相機資訊設定」，順序為一般、版面、相機資訊、匯出。沿用原生控制項與同一 MainViewModel，沒有另開視窗；返回直接回既有首頁。
- 相機頁呈現機身、鏡頭、日期、時間、焦距、光圈、快門及 ISO，附逐欄來源；空值呈現未提供。2026-10-18 依新版設計改為純文字表，編輯僅留在主畫面；詳見 `UI-CAMERA-INFO-20261018.md`。照片既有的建立日期回退會明示「非 EXIF」，不是虛構拍攝資訊。
- `FileDialogService.PickRawInformationAsync` 的提示與確認文字為「請選擇同一張照片的 RAW」，格式清單只來自 `RawMetadataReader.SupportedExtensions` 的 `.rw2`。
- 背景讀取 → 原生 ContentDialog 差異確認 → 預設補缺值／可選覆蓋 RAW 有提供欄位。RAW 缺值永遠不清空目前值；手動清空也視為手動修改，不在補缺值模式默默改回。日期的檔案建立回退不阻擋真正 RAW 拍攝時間補入。
- 照片 EXIF、已確認 RAW 層、手動覆寫有獨立資料。照片 Reload 提示手動覆蓋範圍並保留 RAW 層；RAW Reload 另行讀取及確認。RAW 缺檔／失敗保留既有值與快照。快門無效輸入不會因補 RAW 而被隱藏或解除匯出驗證。
- 來源版本、資訊編輯版本、請求版本與生命週期／取消權杖在讀取及確認後再次檢查；切換照片、手動修改或取消後不提交舊結果。離開相機頁卸載時取消讀取／差異對話框。
- 格式 9 保存單一目前作品快照（路徑＋長度＋最後寫入時間），包含白名單 metadata、RAW 來源與已套用欄位、目前值及逐欄來源。重啟不自動載入照片，重新載入相符作品時才還原；不需重開 RAW。換照片會清除上一張 RAW 關聯。不是保存完整 RAW／EXIF／GPS，也不是一個多作品專案管理功能。

## 函式庫與支援範圍

沿用已部署的 **MetadataExtractor 2.9.3、Apache-2.0**，沒有新增 NuGet、外部工具、Python 或 RAW 顯影依賴。RW2 以唯讀 FileStream／取消支援呼叫 metadata parser，檢查副檔名及真實檔頭；不將 RAW 交給 MagickImage／ImageLoader。解析其內嵌 JPEG 的 EXIF 不等同載入縮圖作成品，感光元件像素不顯影、不替換作品。Panasonic 根 IFD 與標準／內嵌 EXIF 共同補足欄位，必要時讀 Panasonic maker-note 的鏡頭名稱。數值使用 rational；RAW 拍攝日期限 DateTimeOriginal／DateTimeDigitized，不以檔案時間或 ModifyDate 冒充。

官方實作依據：[2.9.3 ImageMetadataReader 的 RW2 metadata 分派](https://github.com/drewnoakes/metadata-extractor-dotnet/blob/2.9.3/MetadataExtractor/ImageMetadataReader.cs)、[ExifTiffHandler 的 Panasonic IFD 與內嵌 EXIF 解析](https://github.com/drewnoakes/metadata-extractor-dotnet/blob/2.9.3/MetadataExtractor/Formats/Exif/ExifTiffHandler.cs)。

**僅開放 `.RW2`，實檔驗證為 Panasonic GH5，不宣稱所有 RAW、所有 RW2 機型或未測格式皆可讀。** 不增加其他 RAW 副檔名到選檔清單。

## 使用者提供的實檔驗證

唯讀測試使用使用者提供的 `P1077581.RW2` 與修好的同名 JPG；本機完整路徑只經環境變數傳入，不寫入測試源碼或公開範例。來源照片／RAW 不複製進發布包或 Git。

實際 RAW 讀取結果：Panasonic DC-GH5、OLYMPUS M.12-40mm F2.8、2026-04-15 16:19:51、31mm、f/3.2、1/60s、ISO 500。JPG 保留多數參數與另一個鏡頭名稱表示法，但缺少 EXIF 拍攝時間；預設補缺值保留 JPG 鏡頭名稱與手動機身／ISO，只補時間；覆蓋模式可明確採 RAW 的鏡頭名稱。兩個使用者原檔測試前後 SHA-256 相同，載入中的 JPEG 照片物件保持同一實例，方向與尺寸摘要不變，正式匯出成功且不附帶 EXIF／GPS。

原生測試只搬移工作區內的 RW2 複本來驗證缺檔情境，不搬動使用者原檔。已確認資訊在缺檔後可預覽及匯出，另一個程序重新載入作品也可從保存快照還原。

## 修改位置與重跑

Imaging：`RawMetadataReader.cs`、`ExifReader.cs`。App：`Services/PhotoInformation.cs`、`SettingsStore.cs`、`FileDialogService.cs`、`ViewModels/MainViewModel.cs`／`.RawInformation.cs`、`Views/SettingsPage.xaml`／`.xaml.cs`／`.RawInformation.cs`、`MainWindow.xaml/.cs`。

可選實檔測試以 `PHOTOMARGIN_RW2_SAMPLE`、`PHOTOMARGIN_EDITED_SAMPLE` 指定自備檔案；不提供時明確略過實檔檢查，合成 metadata／合併與狀態測試仍會執行。Debug 原生入口為 `--verify-raw-information`、`--verify-raw-information-restore`（後者接續前者的獨立設定檔）；Release 不包含此入口。

實際截圖、JSON、TRX、匯出與雜湊檢查證據在 `artifacts/PhotoMargin-raw-verification/`。發布包為 `artifacts/PhotoMargin-raw-0.0.3-win-x64.zip`，完整解壓後執行 `PhotoMargin.App.exe`。應用程式版本維持 0.0.3，本次不 Commit、不 Push。

## 完成驗證結果

- Debug／Release 各 **181 項**測試通過（Core 59、Imaging 34、App Logic 88），無失敗或略過。包含使用者提供的 GH5 RW2／JPG 實檔；未以合成 metadata fixture 冒充實際相機檔案。
- Debug WinUI 建置及 Release 自包含發布成功，零警告、零錯誤；XAML 由 WinUI 編譯器檢查，`git diff --check` 通過。
- Windows 原生 RAW／重啟還原 **24 項**、設定與導覽 **149 項**，共 **173 項**檢查通過，包含實際 ContentDialog 的取消、補缺值、覆蓋與兩頁綁定。
- 已檢視相機資訊頁、原生差異確認與成品畫面。RAW 秒數保留於 metadata 快照，原生 TimePicker 與成品沿用既有 `HH:mm` 顯示，不新增秒數控制項。
- 發布包僅部署原有依賴與應用程式資源；不含使用者的 JPG／RW2、測試快照或個人路徑。壓縮包核對 EXE、PRI／XBF 與 SHA-256，確保 DLL 和發布目錄相同。

## 尚待人工補測項目

Windows 系統檔案選擇器的手動選檔與取消、其他 DPI／Windows 10、高對比／螢幕閱讀器、完整鍵盤巡覽、未安裝開發環境的乾淨系統部署，以及其他相機／RAW 格式仍待驗證。原生 GUI 測試使用 Windows、DPI 120（125%）；不以編譯通過代替上述驗收。
