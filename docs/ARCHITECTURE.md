# PhotoMargin 架構與技術選型

## 分層

```text
PhotoMargin.Core     幾何、方向政策、資料模型、排版與驗證；不依賴 WinUI 或影像函式庫
PhotoMargin.Imaging  解碼、EXIF、色彩、字形量測、合成與匯出；封裝 Magick.NET／MetadataExtractor
PhotoMargin.App      WinUI 3、MVVM、對話框、背景工作、設定與預覽呈現
```

預覽與匯出都使用 `LayoutEngine` 產生的 `LayoutResult`，固定顯示完整正方形畫布。安全線由預覽渲染路徑額外繪製，正式 JPG 不會包含輔助線。CalendarDatePicker／TimePicker 綁定可空的 SelectedDate／SelectedTime；優先帶入 EXIF，缺少拍攝日期時使用來源檔案建立時間並標明來源。成品使用 yyyy.MM.dd HH:mm；原始秒數仍保留於來源中繼資料。

主畫面與設定頁共用同一個 MainViewModel。SettingsPage 明確接收目的區塊，NavigationView 的選取狀態與內容同步；設定內返回有自己的歷程，從主畫面直接進入則返回保留中的工作頁。邊距規則由 MarginRule／RuleEditorViewModel 管理；編輯草稿不會立即改變作品，儲存後才更新規則集合。設定 JSON 版本 3 接受版本 2，將原有邊距轉成預設規則。

Reload 在確認後以背景工作重新讀取 EXIF，不重載圖片或重設輸出設定。成功才提交資訊；來源版本、資訊編輯版本及生命週期檢查阻止過期結果回寫。

## 影像函式庫驗證

| 項目 | 結果 |
| --- | --- |
| 套件 | Magick.NET-Q8-x64 14.16.0 |
| 授權 | Apache-2.0；內含 ImageMagick，採 ImageMagick License |
| 原生依賴 | 套件內含 Windows x64 原生元件，封裝於 Imaging 專案 |
| JPG／PNG／灰階／CMYK | 由整合測試實際解碼；CMYK 無 ICC 時使用 US Web Coated SWOP 作輸入回退 |
| EXIF Orientation | `AutoOrient()`；另以 Orientation 6 的原始直向樣本驗證校正後為橫向 |
| ICC | 有嵌入描述檔時以該描述檔轉至 `ColorProfiles.SRGB`；輸出重新附加 sRGB 描述檔 |
| 透明合成 | 移除 alpha 前設定白底；透明 PNG 整合測試確認輸出為無 alpha JPG |
| JPG 編碼 | 品質 50–100；輸出畫布尺寸由整合測試驗證 |
| 中文文字 | TextGeometry 將內建字體選項解析為實體字體檔；中文回退至系統微軟正黑體，量測實際墨跡並與繪製共用字體與基線；整合測試檢查中文及拉丁字形的安全邊界 |

MetadataExtractor 2.9.3（Apache-2.0）只負責讀取中繼資料，不寫回來源檔。數值使用 rational 原值轉換，不由描述字串反推。輸出畫布為新影像，寫入前移除 metadata，再附加 sRGB profile，因此不轉存 GPS 或完整 EXIF。

## 版面流程

1. 將啟用的 1:1／4:5／5:4 可見矩形取交集，依輸出尺寸縮放額外留白與底部安全距離。
2. 以最終輸出字級量測實際文字外框。
3. 優先嘗試左右分欄；欄寬不足時改為置中堆疊並安全換行，不截斷原始資料。
4. 依剩餘寬高以 `min(availableWidth/W, availableHeight/H)` 計算照片大小。
5. 驗證照片在畫布內、文字在安全矩形內、文字不碰照片且各文字框不重疊。
6. 驗證失敗時 ViewModel 清除可匯出狀態並顯示原因。

## 背景工作與一致性

大圖解碼、預覽和匯出在背景執行。每次輸入變更會取消前一個預覽，並以遞增版本號阻止已過期的結果回寫。替換照片前會等待渲染鎖，避免釋放仍被使用的影像。任何設定變更都會立即使舊版面失效，直到新安全驗證完成才重新允許匯出。

## 部署

App 為 unpackaged WinUI 3 x64 應用程式。`WindowsAppSDKSelfContained=true` 並在發布時使用 `.NET self-contained`，所以發布資料夾包含 .NET 與 Windows App SDK 執行相依；不追求單檔案。最低宣告版本是 Windows 10 1809，仍需在實際目標 Windows 10 裝置進行最終驗證。
