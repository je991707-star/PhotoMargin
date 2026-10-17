# 八組比例／資訊排列配置與圖片資訊間隔

> 歷史交付記錄：後續四個名稱、底部定位及格式 7 遷移以 [文字邊界、下方邊距與間隔定位](UI-BOTTOM-MARGINS-20261017.md) 為準；下文保留當時的名稱與驗證數字。

## 修改摘要

- 版面設定在「圖片比例」正下方增加原生「排列設定」ComboBox，只有「左右排列」「垂直排列」，分別對應既有 TwoColumns／Stacked 渲染。
- 每個預設 ID 保存四比例乘以兩排列的八份 MarginConfiguration。六個 px 數值為左右邊距、上方邊距、照片資訊間距、圖片資訊間隔、底部安全距離、裁切文字額外留白，另保留兩個裁切安全保護值。不新增不存在的高度或字級設定。
- 原有「照片資訊間距」仍控制照片與資訊區之間的距離；新增的「圖片資訊間隔」只控制焦距／光圈／快門／ISO 相鄰可見項目的水平距離。Slider 範圍 0–200，步進 1，固定 80 DIP 右對齊數值顯示，單位為 1080 基準 px。
- 比例與排列切換只變更編輯對象及示範選取偏好，不儲存預設草稿；切換會使舊渲染世代失效。套用模式的數值調整只更新作品副本；編輯模式只更新八配置草稿。儲存、放棄、確認刪除均沿用既有流程。
- 主畫面正式套用預設使用校正 Orientation 後的來源比例匹配及目前資訊排列；Auto 取左右配置並沿用原有自適應渲染。明確切換主畫面排列時載入該預設的對應配置。重啟仍還原作品自身快照，不以預設蓋掉手動數值。

## 模型與遷移

新版設定版本為 **6**，仍接受版本 2／3／4／5。

```text
MarginRule（ID、名稱）
└─ Arrangements: RatioArrangementConfigurations
   ├─ SixteenByNine
   ├─ FiveByFour
   ├─ FourByThree
   └─ ThreeByTwo
      ├─ TwoColumns: MarginConfiguration
      └─ Stacked: MarginConfiguration
```

每個比例都有上述兩份配置。所有節點為不可變、值相等的 record；每份配置在正規化時獨立複製，不持有可變字典或共用可變子物件。渲染排列由鍵決定，不把排列識別塞入邊距副本。

舊平面資料先複製為四種比例；舊四比例值各複製成兩種排列，保留 ID、名稱、原數值及作品原有排列。已有排列資料時保留，僅補缺失部分；只有一種排列時用它的獨立副本補另一種。載入正規化是冪等的，不會用相容快照覆寫已保存的新配置。

`Arrangements` 為新版權威資料；舊 `Configurations` 保留各比例左右配置相容快照，根層數值保留 4:3 左右快照。首次成功保存寫出版本 6；讀取失敗時的保護機制不變。新欄位缺值預設 18px，約還原舊版預設 22px 字體下三個空白的視覺間隔，舊字體與字級不重設。

## 渲染

四項資訊先過濾缺值，分別使用實際字形量測。每行寬度為字形寬度總和加上 `(項目數−1) × ExposureItemGap × OutputSize/1080`，依既有資訊欄位右對齊或整列置中；各文字項目基線一致，字內字距不變。原實作沒有分隔符號，本次不新增符號。

左右分欄不足時維持原有警告與置中回退，保留目前指定間隔。垂直排列按完整項目換行，每行仍使用同一間隔，行距不變；單項文字太寬、整組高度不足或幾何驗證失敗時拒絕匯出。設定示範與正式輸出共用 LayoutEngine／ImageRenderer，不拉伸或裁切正式來源，不輸出安全線。

## 主要檔案

- App：`Services/MarginRule.cs`、`RatioConfigurations.cs`、`SettingsStore.cs`，`ViewModels/MainViewModel.cs`、`RuleEditorViewModel.cs`，`Views/SettingsPage.xaml`。
- 共用核心：`Core/Models/LayoutModels.cs`、`Core/LayoutEngine.cs`、`Imaging/MagickTextMeasurer.cs`。
- 驗證：新增 `ArrangementPresetTests.cs`、`ExposureSpacingTests.cs`；更新既有版本／整列對齊斷言與原生 WinUI 回歸。

## 驗證範圍

涵蓋八組草稿來回切換、整份儲存與新實例還原、取消不修改、套用不回寫、刪除模式限制、舊版及不完整配置遷移、最新預覽版本、缺值間隔、字形寬與基線、1080／2048 縮放、實際 JPG 與同核心計算結果一致、過大間隔換行／失敗保護及來源不變。

Windows 原生回歸使用獨立測試照片及偏好，檢查兩個下拉選單、六個滑條、八組新程序還原、刪除確認、主畫面返回與 EXIF／匯出流程；DPI 120（125%）。未完成其他 DPI、Windows 10、高對比、螢幕閱讀器及人工鍵盤逐項檢查。

最終結果：Debug／Release 建置均零警告、零錯誤；兩種組態各通過 118 項自動測試（Core 46、Imaging 17、App Logic 55）。原生 WinUI 共 224 項通過：八組預覽 28、新程序還原 9、預設／刪除工作流 26、主畫面／導覽／EXIF／匯出 148、分欄溢出回退 13。實際檢視 `eight-configurations-layout.png`，確認新增選單與滑條位置、數值及左右排列預覽。

證據存於 `artifacts/PhotoMargin-eight-configurations-verification/`，包含五份原生測試 JSON 與實際設定頁截圖。

發布：`artifacts/PhotoMargin-eight-configurations-0.0.2-win-x64.zip`。完整解壓後執行 `PhotoMargin.App.exe`，版本維持 0.0.2；本次未 Commit、未 Push。
