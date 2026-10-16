# PhotoMargin 架構與技術選型

## 分層

```text
PhotoMargin.Core     幾何、方向政策、資料模型、排版與驗證；不依賴 WinUI 或影像函式庫
PhotoMargin.Imaging  解碼、EXIF、色彩、字形量測、合成與匯出；封裝 Magick.NET／MetadataExtractor
PhotoMargin.App      WinUI 3、MVVM、對話框、背景工作、設定與預覽呈現
```

預覽與匯出都使用 `LayoutEngine` 產生的 `LayoutResult`，固定顯示完整正方形畫布。安全線由預覽渲染路徑額外繪製，正式 JPG 不會包含輔助線。CalendarDatePicker／TimePicker 綁定可空的 SelectedDate／SelectedTime；優先帶入 EXIF，缺少拍攝日期時使用來源檔案建立時間並標明來源。成品使用 yyyy.MM.dd HH:mm；原始秒數仍保留於來源中繼資料。

主畫面與設定頁共用同一個 MainViewModel。SettingsPage 明確接收目的區塊，NavigationView 的選取狀態與內容同步；設定不保存子頁返回歷程，自訂返回、原生 NavigationView 返回與 Alt+Left 統一一次返回既有工作頁。退出只清理 SettingsFrame 的紀錄。編輯草稿未儲存時，模式／預設／子頁切換、返回與正常關閉均先確認放棄；取消保留草稿與目前頁面。

版面預設由 MarginRule／RuleEditorViewModel 管理。MarginRule、RatioConfigurations 及 MarginConfiguration 是不可變、值相等的 record；每個預設含 16:9、5:4、4:3、3:2 四組獨立配置（五個 1080 基準 px 數值及兩個安全保護值）。載入／草稿建立時複製巢狀配置，不共用可變物件。套用模式只複製數值到作品，可不同名稱另存新 ID；正式選取預設依 Orientation 校正後的來源寬高比選最近配置，不裁切來源。編輯模式只更新完整四配置草稿，儲存才更新原 ID；離開丟棄不修改作品。設定 JSON 格式 5 接受 2／3／4，舊平面配置複製為四組初值，首次保存寫入新版；根層邊距保留為 4:3 相容快照。格式 4／5 的空清單不重建。啟動只從 Layout 還原作品，不重新套用預設。

設定頁有獨立預覽，不再把編輯草稿覆蓋主畫面的預覽／可匯出版面。沒有來源時使用 Imaging 組件內嵌的自有 4:3 示範 PNG 與獨立示範 metadata；有來源時在渲染鎖內建立最大 1280px 的已校正副本，依來源版本快取，不逐次解碼原圖。比例選單僅中心裁切此副本。設定預覽以 720px 正方形使用相同 LayoutEngine／ImageRenderer，不畫假容器、不寫出示範資料。五個滑條持續雙向綁定當前配置，固定寬度唯讀數值使用相同資料。

設定預覽以序列合併工作、35ms 間隔產生拖曳過程中的漸進畫格；只有一個渲染作業，不讓較舊完成結果覆蓋較新畫格。比例／預設／來源變動使整個世代失效，停止操作後排空最新快照；失敗保留前一個有效畫面並顯示錯誤。頁面生命週期持有預覽識別，舊頁面延後卸載不能停止新頁面。比例選取偏好獨立保存，不寫入輸出畫布比例，也不持久化未儲存的預設草稿。

LayoutSettings.Arrangement 對應既有的分欄與堆疊算法：Auto 保留先分欄、容不下時堆疊的自適應行為；TwoColumns 明確分欄，無法容納時回報驗證錯誤；Stacked 直接使用原有置中堆疊／換行。三者共用 contain、文字量測、安全驗證及渲染。排列透過既有 Layout 偏好保存；舊設定缺值或未知數值時回到 Auto，不重設其他偏好。

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

## 狀態、偏好與來源保護

預覽錯誤、操作錯誤與偏好保存錯誤分開管理；預覽成功只清除自己的錯誤，不依賴訊息文案字首判斷生命週期。圖片寫入成功與保存最近輸出目錄分開回報。預設保存及刪除均先提交磁碟快照，成功後才改可見清單；失敗保留原資料與草稿。MainViewModel 的 preferenceGate 將預設交易與一般自動保存序列化，快照在取得閘門後才產生，避免排隊中的舊清單復活已刪預設。

永久刪除的請求綁定原 ID／名稱／完整已保存快照，確認時再檢查模式、存在性、內建保護、勾選及請求身分。取消的請求不可重用。刪除後只更新有效選取，不套用替代預設，作品數值保持。原生 ContentDialog 使用取消作預設焦點；危險色只覆寫按鈕啟用狀態資源，沿用原生模板、Disabled、Focus 與高對比。RadioButtons／ComboBox 的選取回寫延後至原生事件結束，忽略清單更新造成的暫時空選取。

SettingsStore 每次保存使用獨立暫存檔，並以 OS 檔案共享鎖協調跨實例／跨程序的最後替換；設定是完整快照，採最後完成提交者優先，不合併多個視窗的設定。鎖最長等待五秒，失敗保留既有設定。設定讀取允許舊檔案被原子替換；取消／失敗會清理自身暫存檔。空的 `.lock` 檔保留以避免 unlink／reopen 競態，程序結束即釋放 OS 鎖。

渲染的生命週期取消不再取消已排隊的偏好寫入。原生視窗關閉先取消關閉要求、停用編輯並等待最後快照；保存失敗時讓使用者選擇留在程式或明確放棄後關閉。強制終止程序、停電與 OS 強制結束不保證保存。

匯出透過 Windows FILE_ID_INFO 比較實體檔案身分，拒絕相同來源的路徑別名及 hard link。整段匯出期間持有不允許寫入／刪除共享的來源 handle，最終替換前再檢查一次；暫存檔採隨機名稱與 CreateNew。無法確認既有目的檔的身分時採保守拒絕，不冒險覆寫。參考 [Microsoft FILE_ID_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info)。

EXIF 數值顯示與目前數值輸入包裝統一使用 InvariantCulture，避免小數逗號被誤判缺值；照片輸出字體與 UI 字體仍分開。

## 部署與發布

App 為 unpackaged WinUI 3 x64 應用程式。`WindowsAppSDKSelfContained=true` 並在發布時使用 `.NET self-contained`，所以發布資料夾包含 .NET 與 Windows App SDK 執行相依；不追求單檔案。最低宣告版本是 Windows 10 1809，仍需在實際目標 Windows 10 裝置進行最終驗證。
