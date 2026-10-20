# 預設編輯模式與更新前備份修正（2026-10-18）

## 原因與最小修正

`SettingsPage.RuleMode_Changed` 原先從 `RuleModeOptions.SelectedIndex` 判斷操作目標。
在目前 WinUI 執行環境，透過原生子 `RadioButton` 選取「編輯預設內容」時，
`SelectionChanged.AddedItems` 已包含新項目，但讀到的 `SelectedIndex` 可仍為暫態 `-1`。
舊程式將 `-1` 傳給 `ChangeModeAsync`，被有效模式驗證拒絕，再把畫面還原為套用模式。
因此編輯、更新與刪除均無法從實際單選操作進入。

改以事件 `AddedItems` 在既有項目集合中的索引取得目標，忽略空選取及非 0／1 索引。
保留非同步離開確認、草稿、儲存及刪除授權，不改控制項、版面或原生模板。

先前原生測試只設定父控制項 `SelectedIndex`，未走實際子單選流程，因此漏掉此問題。
本次改用子 `RadioButtonAutomationPeer` 的 `ISelectionItemProvider.Select`；
原程式實際重現失敗，修正後完整流程通過。

## 指定備份清除

使用者關閉 PhotoMargin 後，先複製完整本機設定檔，再以正式 `SettingsStore` 儲存移除
已確認 ID `95ab2e35f43f4dcea13344e34126e1dd` 的「預設規則（更新前備份）」快照。
清除工具僅在 `artifacts/preset-cleanup`，不編入正式程式、不新增 UI 入口。

重新讀取確認只移除此項，其餘設定與選取不變。目前保留「預設規則」及「預設規則 2」。
內建預設已是目錄 revision 2；正常再次載入不會重建此備份。
增加邏輯測試驗證從舊版遷移、刪除備份、重啟後仍保持刪除，且自訂預設與作品值不變。
不以名稱批次刪除其他使用者資料，不改遷移時保護舊自訂值的備份機制。

## 驗證

- Debug 與 Release：各 182 項（Core 59、Imaging 34、App Logic 89）回歸測試；結果見 TRX。
- 原生 WinUI 預設流程 27 項及新程序還原 4 項，均通過；DPI 120（125%）。
- 包含新增不回寫、子單選進入編輯、取消保留草稿、確認放棄、穩定 ID 更新、
  刪除確認每次重設、未勾選不可刪除、勾選後紅色操作、取消不刪除、成功持久化，
  以及返回首頁保留作品。
- 原生畫面含深／淺色及 800×700 小視窗，截圖位於 `artifacts/preset-fix-verification/Native`。
- Windows 視窗外部控制工具發生應用識別錯誤，未透過它執行滑鼠操作；
  以上互動驗證為程式內原生 AutomationPeer，不宣稱完成外部實體滑鼠／鍵盤全面驗收。

## 發布

`artifacts/PhotoMargin-preset-fix-0.0.3-win-x64/PhotoMargin.App.exe`
及同名 ZIP。版本保持 0.0.3，包含先前尚未提交的 RAW 功能；沒有 Commit／Push。
使用新執行包，不覆蓋仍在執行的舊資料夾。設定檔備份位於
`artifacts/preset-cleanup/settings-before-20261018-230419.json`，未放入執行包。
