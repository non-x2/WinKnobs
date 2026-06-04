# Changelog

All notable changes to **WinKnobs** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> 📝 **Note**: csproj 上の AssemblyVersion はメジャー・マイナー・パッチを反映します
> （例: v0.1.4-dev → AssemblyVersion 0.1.4.0）。UI ステータスバー表示は dev 接尾辞付きで表示されます。

---

## [Unreleased]

### Planning
- **v0.2.x 候補**（32 項目・[v0.2 候補項目集 v2](docs/v0.2_候補項目集_v2.md)）：
  - v0.2.0：Quick Win 5 項目（メモリ圧縮 / Power Throttling / Sysmain / 夜間モード / 透明効果）
  - v0.2.1：LDPlayer 解消・WSL・ゲーマー向け 6 項目
  - v0.2.2：IME / 音声 / Network 完成 6 項目
  - v0.3.0：Gaming カテゴリ + プリセット機能 14 項目
- **M-1 リファクタ**（v0.2 期間中漸進対応）：HKLM 3 択フォールバックのヘルパー化
- **Public 化前残作業**（[Public化前準備物 v1](docs/Public化前準備物_v1.md)）：v0.1.6 タグ / Releases / Sponsors / Dependabot / Discussions / Security Advisory / Branch Protection / Public 化

---

## [0.1.15] - 2026-06-04

### Added
- **🛠 システム・メンテナンス「システムの保護（システムの復元）ON/OFF」**（Phase 2・メニューコマンド）
  - 「ファイル」メニューの「復元ポイントを作成」の隣に「システムの保護を有効にする / 無効にする (要管理者)」を追加。既存 `RestorePointService` と同じ作法（PowerShell・結果ダイアログ）を再利用
  - 実行：PowerShell `Enable-ComputerRestore` / `Disable-ComputerRestore -Drive 'C:\'`（**要管理者権限**）。管理者なしで失敗時は「管理者として実行し直して」と案内
  - ⚠️ 無効化の確認ダイアログに「既存の復元ポイントが削除される」旨を明記。確認必須
  - 新規 `Services/SystemProtectionHelper.cs`（純粋関数：NormalizeDrive / BuildCommand / Classify ＋ `SystemProtectionOutcome` enum）＋ `Services/SystemProtectionService.cs`（RestorePointService 同型・PowerShell 60秒タイムアウト）
  - 案A（メニューコマンド）採用＝のんさん選択。実行系を最小の未検証コードで・システム復元の操作（保護ON/OFF＋ポイント作成）を1か所に集約

### Notes
- バージョン bump：v0.1.14 → v0.1.15（csproj 3箇所 / MainWindow / MainViewModel）。生成 DLL FileVersion=**0.1.15.0** 実測
- **テスト**：xUnit **401 PASS / 0 FAIL**（v0.1.14 の 368 + SystemProtectionHelper 33）。dotnet build -c Release **0 警告 0 エラー**
- 🛠 **堅牢化（2026-06-05・@cross-reviewer 重要3＋監視）**：`BuildCommand` に `-ErrorAction Stop` 付与（非終了エラーも terminating 化し ExitCode!=0 を保証＝PowerShell 5.1/7 どちらでも権限不足を取りこぼさず ExitCode 主判定にできる）。`SystemProtectionService` に「大量出力時はパイプバッファ充填でデッドロックしうる（小出力前提）」注意コメント追記
- ✅ **@cross-reviewer 再実施完了**（前回 parse error 解消）：指摘＝致命1／重要2／重要3。🎯 **致命1（日本語 stderr 文字化けで Classify が機能しない）は実機検証で誤検知と確定** — WinKnobs 同条件（WinExe・.NET10）で `Process.StandardError.CurrentEncoding=CP0`（システム ANSI）＝日本語は正常デコード。サブエージェントの「.NET 既定=UTF-8」想定は WinExe では当たらず。重要2（昇格設計）は致命1前提のため自動解消（設計自体は妥当）
- ✅ **実機検証（2026-06-05・ホスト非昇格で安全に）**：`Enable-ComputerRestore -Drive 'C:\' -ErrorAction Stop` を非昇格実行 → 「アクセスは拒否されました」→ `Classify`=**AccessDenied**・ExitCode=1。非昇格運用の最重要経路（権限不足→「管理者として実行し直して」案内）が動作確認済（VM 管理者操作なしで安全に確認）。本物文言を xUnit リグレッションに固定。**残＝管理者での Success 経路（ExitCode=0）の VM 目視のみ（任意・低リスク）**
- **残（次セッション）**：Phase 3 メモリ圧縮（MMAgent・v0.2.0 前倒し）／ Phase 4 リモートデスクトップ（🔴 最高リスク）。設計：docs/v0.1.14_システムメンテナンス_設計ドラフト.md

---

## [0.1.14] - 2026-06-04

### Added
- **🛠 システム・メンテナンス カテゴリ（新設）＋「タスクマネージャを使えるようにする」つまみ**（Phase 1）
  - 大型機能「システム・メンテナンス」（RDP / システム保護 / タスクマネージャ / メモリ圧縮 のフルトグル）の第1弾。**Phase 1 は HKCU・管理者不要・自己検証可で先行出荷**
  - 新カテゴリ `CategoryType.Maintenance`（🛠・enum 末尾追加）。Startup/Network 以外なので `IsStandardCategorySelected` で標準 DataGrid に自動表示（専用ペイン不要）
  - 「タスクマネージャを使えるようにする / 無効にする」を Command 型2択で切替。レジストリ `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System\DisableTaskMgr`(REG_DWORD・0/未設定=使える / 1=無効)。**管理者不要・危険度🟡・即戻せる**
  - 新規 `Services/TaskManagerHelper.cs`（ScrollbarHelper 同型・純粋関数）。Read/ラベルは Choices 同一参照／ CoerceInt で REG_SZ 混在対応／未設定は Restore で DeleteValue（未設定＝使える へ忠実復元）
  - ⚠️ 自己ロックアウト警告を Description に明記（無効化すると Ctrl+Shift+Esc も右クリックメニューも開けなくなる・WinKnobs から戻せる・gpedit 競合注意）。中立表示（既定＝使える）

### Notes
- バージョン bump：v0.1.13 → v0.1.14（csproj 3箇所 / MainWindow / MainViewModel）。生成 DLL FileVersion=**0.1.14.0** 実測
- **テスト**：xUnit **368 PASS / 0 FAIL**（v0.1.13 の 343 + TaskManagerHelper 25）。dotnet build -c Release **0 警告 0 エラー**
- 設計：[docs/v0.1.14_システムメンテナンス_設計ドラフト.md](docs/v0.1.14_システムメンテナンス_設計ドラフト.md)。@cross-reviewer（**致命0**・enum 末尾追加のリグレッションなし／新カテゴリは標準 DataGrid に正しく吸収／値の向きが Helper・Build・テストで三者一致）反映済み（UX 警告強化）
- **残（次セッション・管理者+VM必須）**：Phase 2 システムの保護 ON/OFF ／ Phase 3 メモリ圧縮（v0.2.0 前倒し）／ Phase 4 リモートデスクトップ（🔴 セキュリティ最高リスク・強確認 UI）
- **VM/実機の最終確認（出荷前推奨）**：`Policies\System` は新規パスのため「未設定→無効→使える→未設定」の1往復を VM で確認（HKCU・即戻せる・ホストでは のんさんのタスクマネージャを触らない）。制限系ポリシーキーは配布時 VirusTotal 確認候補（cross-reviewer 監視）

---

## [0.1.13] - 2026-06-04

### Added
- **アクセシビリティ「通知を表示する時間」つまみ**（♿アクセシビリティ・第5項目）
  - 画面に出る通知（トースト）を自動で消すまでの時間を 5秒（既定）/7秒/15秒/30秒/1分/5分 から1クリックで変更（設定 > アクセシビリティ > 視覚効果 >「通知を表示する時間」と同じ）。通知を読み切れず消える時に
  - レジストリ `HKCU\Control Panel\Accessibility\MessageDuration`(REG_DWORD・秒)。**管理者不要・危険度🟢・即戻せる**
  - 新規 `Services/MessageDurationHelper.cs`（純粋関数）。**ラベルに「1分」「5分」が混在するため、ラベル⇔値は CaretWidth の「先頭数字読み」ではなく PresetSeconds とのインデックス明示マッピング**（"1分"→60秒）。Choices/PresetSeconds を同一配列で共有し表記ゆれ事故を構造排除
  - Read=最近接プリセット（必ず ComboBox 候補内）／ Backup=生値（REG_SZ 混在は CoerceInt で数値化・異常型は既定5秒へ正規化）／ Restore=書戻し or 削除（未設定へ忠実復元）
  - RestartScope=Logoff（次の通知から反映・完全反映は再ログイン。「即時反映」と断定しない＝plan-validator 致命1反映）
  - 中立表示：時間を一律推奨しない（既定＝5秒）

### Notes
- バージョン bump：v0.1.12 → v0.1.13（csproj AssemblyVersion=0.1.13.0 / FileVersion=0.1.13.0 / MainWindow ステータスバー / MainViewModel 環境情報）。生成 DLL FileVersion=**0.1.13.0** 実測
- **テスト**：xUnit **343 PASS / 0 FAIL**（v0.1.12 の 298 + MessageDurationHelper 45）。dotnet build -c Release **0 警告 0 エラー**
- @plan-validator（致命1＝RestartScope の None「即時反映」断定回避→Logoff、中2＝Choices/SecondsForLabel 同一配列共有・往復/invariant テスト、エッジ3＝端値ラベル非依存・タイブレーク45→30秒/11→7秒/180→1分・範囲外生値）→ @cross-reviewer（**致命0**・明示マッピングで表記ゆれ構造排除を確認・異常型コメント1行補足）反映済み
- 既知の軽微（監視のみ）：同型 Backup トークンが5クラス目（将来の共通化候補）／Description と Logoff フッターの二重表示は安全側
- VM 実機検証は次の一手（HKCU・即戻せる低リスク／非プリセット値・範囲外生値の往復を1度確認）

---

## [0.1.12] - 2026-06-04

### Added
- **アクセシビリティ「テキストカーソルの太さ」つまみ**（♿アクセシビリティ・第4項目）
  - 文字入力時に点滅する縦棒（テキストカーソル｜）の太さを 1px（既定）〜20px のプリセットから1クリックで変更（設定 > アクセシビリティ > テキスト カーソル > テキスト カーソルの太さ と同じ）。入力位置を見失いやすい時に
  - レジストリ `HKCU\Control Panel\Desktop\CaretWidth`(REG_DWORD・1-20)。**管理者不要・危険度🟢・即戻せる**
  - 実装：プリセット範囲型は TextScaleFactor(v0.1.9) と同型の Command 型。Read は最近接プリセット（必ず ComboBox 候補内）＋ REG_SZ 混在対応（Scrollbar 型）／ Backup=生値（`existed = v != null`・DWORD 正規化）／ Restore=書戻し or 削除（未設定へ忠実復元）
  - 新規 `Services/CaretWidthHelper.cs`（純粋関数：ClampToRange/TryParseLabel/NearestPresetLabel/CoerceInt）
  - 中立表示：太さを一律推奨しない（既定＝1px）。点滅速度（CaretBlinkRate）ではなく「太さ」であることを Description に明記
  - ⚠️ 既知制約：完全反映はサインアウト→再ログイン。設定アプリを開いている場合は閉じてから適用

### Changed
- **CoerceInt（レジストリ生値→int 変換）を共通ユーティリティ `Services/RegistryValueCoercion.cs` に集約**（Rule of Three・4箇所目）
  - CursorSizeHelper(v0.1.10) / ScrollbarHelper(v0.1.11) / CaretWidthHelper(v0.1.12) の3 Helper は本ユーティリティへ委譲する**前方互換ラッパ**へ変更（公開 API・挙動・シグネチャは不変＝既存呼び出し側・xUnit はそのまま緑）
  - ScrollbarHelper / CursorSizeHelper に残っていた「4箇所目が出たら集約検討」の申し送りコメントを「集約済」へ同時更新

### Notes
- バージョン bump：v0.1.11 → v0.1.12（csproj AssemblyVersion=0.1.12.0 / FileVersion=0.1.12.0 / MainWindow ステータスバー / MainViewModel 環境情報）。生成 DLL FileVersion=**0.1.12.0** 実測
- **テスト**：xUnit **298 PASS / 0 FAIL**（v0.1.11 の 238 + CaretWidthHelper 43 + RegistryValueCoercion 17＝60）。dotnet build -c Release **0 警告 0 エラー**
- @plan-validator（致命2＝CHANGELOG テスト数は実測値で記載／Rule of Three コメント同時更新、高2＝Description「太さ≠点滅速度」明記・端値一致テスト、エッジ3＝REG_BINARY 異常型→既定1px 正規化・中間値最近接・設定アプリ競合）→ @cross-reviewer（**致命0**・リグレッションなし／3ラッパの委譲一致をテストで担保／REG_BINARY 実機経路〔16進文字列〕テスト1件追加で経路乖離を解消）反映済み
- 設計判断：live-apply（SystemParametersInfo SPI_SETCARETWIDTH=0x2007）は存在するが**未検証ゆえ見送り**・v0.1.13 検証候補（v0.1.9 テキストサイズ・v0.1.11 スクロールバーと同方針＝「API 無し」と断定しない）
- VM 実機検証は次の一手（HKCU・即戻せる低リスク／アクセシビリティ4項目まとめて）

---

## [0.1.11] - 2026-06-04

### Added
- **アクセシビリティ「スクロールバーを常に表示」つまみ**（♿アクセシビリティ・第3項目）
  - Windows 11 で普段は隠れるスクロールバーを常に表示（設定 > アクセシビリティ > 視覚効果 と同じ）。現在位置が分かりやすくなる
  - レジストリ `HKCU\Control Panel\Accessibility\DynamicScrollbars`(DWORD・0=常に表示 / 1 or 未設定=既定)。**管理者不要・危険度🟢・即戻せる**
  - **Command 型2択**（宣言的 Boolean ではない）。理由：この値は既定で未設定のことが多く、.reg エクスポート退避だと「新規作成→戻す」で値が消えず戻せない → JSON トークン退避（`existed = v != null`／未設定なら Restore で DeleteValue＝未設定へ忠実復元）
  - 新規 `Services/ScrollbarHelper.cs`（純粋関数：IsAlwaysShow/ValueForLabel/LabelForValue/CoerceInt）。Read/ラベルは必ず `Choices[n]` 同一参照（リテラル直書き禁止＝ComboBox 黙って既定化の事故防止）。REG_SZ "0"/"1" 混在環境も CoerceInt で対応
  - 中立表示：一律「常に表示」を推さない（既定＝使う時だけ表示）
  - ⚠️ 既知制約：設定／ストアアプリには効くが、エクスプローラなど一部の従来型(Win32)アプリには反映されない。完全反映はサインアウト→再ログイン
  - **テスト**：xUnit **238 PASS**（v0.1.10 の 205 + ScrollbarHelper 純粋関数 33＝IsAlwaysShow / ValueForLabel / LabelForValue invariant〔必ず Choices 内・同一参照〕/ CoerceInt の REG_SZ・境界）

### Notes
- バージョン bump：v0.1.10 → v0.1.11（csproj AssemblyVersion=0.1.11.0 / FileVersion=0.1.11.0 / MainWindow ステータスバー / MainViewModel 環境情報）
- @plan-validator（致命1＝Read/ラベルの Choices 同一参照保証なしで黙って既定化／重要＝REG_SZ "0" 混在・live-apply は「API無し」断定でなく未検証ゆえ見送り）→ @cross-reviewer（**致命0**・手本と完全同型を確認）反映済み
- 設計判断：①宣言的 Boolean でなく Command＋JSON 退避（未設定値を正しく戻すため）②型正規化（REG_SZ→REG_DWORD）は値の意味が保たれ Windows が期待する型に揃うため望ましい（CursorBaseSize v0.1.10 に続き2回目）③live-apply（WM_SETTINGCHANGE ブロードキャスト）は未検証ゆえ見送り・v0.1.12 検証候補
- VM 実機検証は次の一手（HKCU・即戻せる低リスク）

---

## [0.1.10] - 2026-06-04

### Added
- **アクセシビリティ「マウスポインタのサイズ」つまみ**（♿アクセシビリティ・第2項目）
  - マウスポインタ（矢印）を 標準(1)〜最大(15) のプリセットから1クリックで拡大（設定 > アクセシビリティ > マウスポインターとタッチ と同じ）
  - **2値書込**：`HKCU\Software\Microsoft\Accessibility\CursorSize`(1-15・設定アプリのスライダー値) ＋ `HKCU\Control Panel\Cursors\CursorBaseSize`(32-256・実描画)。対応式 `CursorBaseSize = 32 + (CursorSize-1)×16`
  - **即時反映**：書込後に `SystemParametersInfo(SPI_SETCURSORS)` でカーソルをレジストリから再読込（best-effort。反映しない場合はサインアウト→再ログイン）。**管理者不要・危険度🟢・即戻せる**
  - 実装：TaskbarSi / TextScaleFactor と同型の Command 型（プリセット選択式）。Read=最近接プリセット（必ず ComboBox 候補内）／ Backup=2値の生値（型混在環境でも `existed = v != null` で判定し DWORD 正規化）／ Restore=各値 書戻し or 削除＋再読込
  - 新規 `Services/CursorSizeHelper.cs`（純粋関数：ClampSize/ToBaseSize/TryParseLabel/NearestPresetLabel/CoerceInt）＋ `Services/SystemCursorService.cs`（P/Invoke・`EntryPoint="SystemParametersInfoW"` 明示）
  - 中立表示：サイズを一律推奨しない（既定＝標準）
  - **テスト**：xUnit **205 PASS**（v0.1.9 の 150 + CursorSizeHelper 純粋関数 55＝範囲丸め境界 int.Min/Max / 2値マッピング / ラベル解析 / 最近接プリセット invariant / REG_SZ 混在の CoerceInt）

### Notes
- バージョン bump：v0.1.9 → v0.1.10（csproj AssemblyVersion=0.1.10.0 / FileVersion=0.1.10.0 / MainWindow ステータスバー / MainViewModel 環境情報）
- @plan-validator（致命1＝CursorBaseSize の REG_SZ 混在で Restore がデータ消失／重要＝SystemParametersInfo の EntryPoint 明示・NearestPresetLabel の long キャスト）→ @cross-reviewer（**致命0**・P/Invoke シグネチャ/定数を公式と照合・重要1＝Description 文言）反映済み
- 設計判断：①2値とも書く（設定アプリのスライダー表示と実描画を一致）②SPI は best-effort・失敗は再ログインで反映（TextScaleFactor の broadcast 見送りとは逆＝SPI は実際に反映する“生きたコード”）③Backup は Clamp せず忠実復元（異常値も元に戻す＝バックアップの正しい契約）
- 既知の軽微（割り切り・cross-reviewer 監視のみ）：プリセットは8段（中間値は Windows 設定アプリで）／SPIF_SENDCHANGE は無応答ウィンドウ次第で最大数秒ブロックの可能性（適用時のみ・稀）
- VM 実機検証は次の一手（HKCU・管理者不要・即戻せる低リスク。ポインタが実際に大きくなる目視＋EntryPointNotFound が出ないこと）

---

## [0.1.9] - 2026-06-04

### Added
- **アクセシビリティ「テキストのサイズ」つまみ**（新カテゴリ ♿アクセシビリティ・第1項目）
  - Windows の文字サイズ（設定 > アクセシビリティ > テキストのサイズ）を 100%（既定）〜225% のプリセットから1クリックで切替。**画面拡大率（DPI）とは別で、文字だけを拡大**
  - レジストリ `HKCU\Software\Microsoft\Accessibility\TextScaleFactor`（REG_DWORD）。**管理者不要・危険度🟢・即戻せる**。完全反映はサインアウト→再ログイン（`RestartScope.Logoff`）
  - 実装は TaskbarSi と同じ Command 型（プリセット選択式）。Read＝最近接プリセット（必ず ComboBox 候補内）／ Apply＝範囲 100-225 に丸めて書込／ Backup＝適用前の生値を保存し Restore で忠実復元（未設定なら値削除）
  - **中立表示**：拡大率を一律推奨しない（既定 100%・「推奨」マークなし）。135% はプリセットに含めるのみ
  - 新カテゴリは enum 末尾追加（既存値の数値シフト回避）。UI 表示順は `InitializeCategories` の Add 位置（デスクトップ・UI とプライバシーの間）で制御
  - **テスト**：xUnit **150 PASS**（v0.1.8 の 110 + TextScaleHelper 純粋関数 40＝範囲丸め境界 / ラベル解析 / 最近接プリセット invariant〔戻り値は必ず Choices 内〕/ const と Choices 端値整合）
  - UIA：`tc_visual_ui.ps1` のカテゴリ件数を 12→13（実装済 12 + SearchResults）に更新

### Notes
- バージョン bump：v0.1.8 → v0.1.9（csproj AssemblyVersion=0.1.9.0 / FileVersion=0.1.9.0 / MainWindow ステータスバー / MainViewModel 環境情報）
- @plan-validator（致命2：enum 途中挿入の数値シフト / NearestPresetLabel の Choices 整合・ReadValue 型判定）→ @cross-reviewer（**致命0**・重要2：const/Choices 端値テスト・.ps1 BOM 確認）反映済み
- 設計判断：WM_SETTINGCHANGE ブロードキャストは見送り（TextScaleFactor は再ログインで反映＝死にコード回避・TaskbarSi と同流儀で案内のみ）
- 既知の軽微（割り切り）：未設定キーへの初書込後 Restore は値のみ削除し空キーが残る（HKCU・無害）／既存の非プリセット値（例 140%）は最も近いプリセット表示になる（戻すと生値に忠実復元）
- VM 実機検証は次の一手（HKCU・管理者不要・即戻せるため低リスク。アクセシビリティ用 UIA テストケースは未整備）

---

## [0.1.8] - 2026-06-04

### Added
- **DoH（暗号化DNS・DNS over HTTPS）ON/OFF**（ネットワーク・カテゴリの「くわしい設定」）
  - DNS の問い合わせを暗号化し、盗み見されにくくする。**安全側デフォルト**：`DohFlags=1`（auto）＋ `udpfallback=yes` ＝暗号化に失敗してもふつうの通信に自動フォールバック（ネットが切れない）
  - **対象**：選択アダプタの現行 DNS のうち既知 DoH プロバイダ（Cloudflare / Google / Quad9）の IP。それ以外は「先に Cloudflare/Google へ」と案内（壊さず順序を導く）
  - **有効化の本命**：インターフェース別レジストリ `…\Dnscache\InterfaceSpecificParameters\{GUID}\DohInterfaceSettings\{Doh|Doh6}\{IP}` の `DohFlags`(REG_QWORD)。netsh テンプレ登録は補助（best-effort・IP 重複排除・`dnsclient`/`dns` 両コンテキスト対応）
  - **退避/復元**：適用直前の (GUID×family×IP) 原状を first-touch でマージ保存（`MergeDohBackups`）。`RevertAll` を明示順 **DoH→DNS→配信最適化** ＋ ループ後一括削除に改修（列挙中削除の回避）
  - **OS ビルド判定**：`GetOsBuildNumber()` ＋ `IsDohSupported(build>=22000)`。非対応 OS ではトグル無効
  - UI：詳細層プレースホルダ「（DoH ON/OFF は今後追加予定）」を実トグルへ置換（AutomationId：`NetworkDohSummary` / `NetworkDohOnButton` / `NetworkDohOffButton`）
  - **テスト**：xUnit **110 PASS**（v0.1.7 の 84 + DoH 純粋関数 26＝ビルド判定境界 / 既知プロバイダ / テンプレ / レジストリパス Doh・Doh6 / 退避マージ）
  - **VM 実機検証**（build 26200/25H2・★2方式）：本命の `DohFlags=1` QWORD 書込→読戻し（値1/型QWord）→DNS 解決不変（非破壊）→削除で原状復帰 が全て成立。`netsh dns add encryption` はグローバル `DohWellKnownServers` のみ書き IF別は作らないことを実機で確認（＝IF別 DohFlags が必須という設計前提を裏付け）

### Notes
- バージョン bump：v0.1.7 → v0.1.8（csproj AssemblyVersion=0.1.8.0 / FileVersion=0.1.8.0 / MainWindow ステータスバー / MainViewModel 環境情報）
- @plan-validator（致命2＋重要1）→ @cross-reviewer（致命0・重要3）反映済み
- 既知の軽微（v0.2 候補）：DoH OFF 後に空の `DohInterfaceSettings\Doh` 容器キーが残る（値なし＝機能影響ゼロ）。カスタム IP＋テンプレ手入力・strict モード（`DohFlags=2`）・`DescribeDnsProfile` への Quad9 追加は未対応
- 「暗号化が実際に走った」最終目視は Windows 設定の対象アダプタ「DNS over HTTPS: On」表示で確認可能（CLI は書込まで確定・機構は Microsoft 公式手順どおり）

---

## [0.1.7] - 2026-06-03

### Added
- **ネットワーク カテゴリ新設（初心者層 + くわしい設定）**（[設計ドラフト](docs/v0.1.7_ネットワーク実用_設計ドラフト.md)・plan-validator / cross-reviewer 反映済・実装順は危険度の低い順 0→3→1→戻す→2→詳細層）
  - **機能0：Windows 純正画面ランチャー**（`ExternalLauncherService.OpenNetworkSettings`・`ms-settings:` / `ncpa.cpl` / 共有センター）＝管理者不要・退避不要の安全弁（抱え込まず純正へ逃がす）
  - **機能1：DNS 切替**（`NetworkService`・netsh・Cloudflare / Google / 自動(DHCP)・物理アダプタ判定は .NET NetworkInterface・退避はレジストリ NameServer・部分適用ロールバック・対象0枚は Info で案内）
  - **機能2：軽いリセット**（flushdns のみ・再起動不要）
  - **機能3：バックグラウンド帯域抑制**（配信最適化 `DODownloadMode` の取得 / 適用 / 復元・read-back・ポリシー検出）
  - **共通：ぜんぶ元に戻す**（`RevertAll`・適用前の値を `backup\network\originals.json` に自動退避・成功分のみ退避削除）
  - **詳細層：DNS 手入力**（IPv4 優先 / 代替・IPv6 のインライン TextBox + 適用ボタン）と **対象アダプタ個別指定**（CheckBox リスト・既定全選択＝従来の全件挙動）
  - アーキ：`NetworkService.ApplyDnsCore(targets, log)` を profile / custom / 個別の共通コアに集約（退避 read-modify-write のアダプタ単位 first-touch + 部分ロールバック）。`BackupService.SaveNetworkOriginal`（無条件上書き）新設。純粋関数 `MergeDnsBackups` / `BuildDnsServers`
  - `MainWindow.xaml`：Visibility を `IsNetworkCategorySelected` で 3 者択一に改修（既存 AutomationId 不変＝UIA 温存）。既存ネットワーク 2 項目（HTTP 同時接続数 / TCP Auto-Tuning）は「くわしい設定」に独立ビューで表示（項目定義は不変）
  - **テスト**：xUnit **84 PASS**（v0.1.6 の 42 + ネットワーク 42＝`NetworkServiceTests` の DNS 引数生成 / IP 検証 / 物理判定 34 + `MergeDnsBackups` / `BuildDnsServers` 8）
  - **VM-UIA**：`tc_network_smoke`（ネットワークペイン全要素の表示確認・新規 12 ブロック）+ 既存 smoke で **OK 41 / FAIL 0**（リグレッションなし・既存項目 Grid 2 件・ホーム復帰 OK・★2方式で非昇格自動実行）

### Notes
- バージョン bump：v0.1.6 → v0.1.7（csproj AssemblyVersion=0.1.7.0 / FileVersion=0.1.7.0 / MainWindow ステータスバー / MainViewModel 環境情報）
- 残（v0.1.8 候補）：**DoH ON/OFF**（暗号化 DNS・OS ビルド判定 + `netsh dns add encryption`・退避はレジストリ直読み）。UI プレースホルダ「（DoH ON/OFF は今後追加予定）」を残置済
- 実機 DNS 適用の往復は VM（仮想 NIC のみ）では検証不可のため、ロジックは xUnit で担保。のんさん実機での目視は任意

---

## [0.1.6] - 2026-06-01

### Added
- **v0.1.6 機能 C：スタートアップ snapshot 復元前プレビュー UI（3 ペイン）**（[設計ドラフト v2](docs/v0.1.6_候補設計ドラフト_v2.md) §C 実装・2026-05-31）
  - `Views/Startup/RestorePreviewDialog.xaml(.cs)` + `ViewModels/Startup/RestorePreviewViewModel.cs`（新規）：🗑 削除予定 / ✏ 変更予定 / ➕ 追加予定 を 3 ペイン（DataGrid・ダークテーマ適用・各ペイン AutomationId 付与）で表示し、「内容を確認しました」チェック後のみ復元ボタンを有効化
  - `StartupListViewModel.RestoreSnapshot()`：機能 B のテキスト Confirm をプレビューダイアログに置換（差分 0 件→案内して終了 / 解析失敗→既定いいえの ConfirmDestructive）。機能 A の整合性検証（差分算出の前）と import 直前の再 Verify は維持
  - `Services/StartupSnapshotDiffer.cs`：変更予定エントリに種別タグ（コマンド変更 / 有効⇔無効 / コマンド・状態変更）を付与（名前だけでは機能 B の件数警告と情報量が変わらないため）
  - `DialogService.OpenRestorePreview(path, diff)`（新規）：DI で VM/Dialog を構築・Init 後注入
  - **テスト**：`RestorePreviewViewModelTests`（チェック連動の OK enable・差分反映・OK/Cancel の 10 ケース）+ Differ 変更種別タグ 3 ケース追加（計 **42 PASS**）
  - plan-validator 反映：ListView→DataGrid（ダーク未定義による白地白文字を回避）/ Modified に変更種別タグ / DialogResult プロパティ方式に統一 / `NotifyPropertyChangedFor(CanRestore)`
  - cross-reviewer 反映：キャンセル時の StatusMessage 更新（重要1）/ Init の空振り通知を除去し順序契約をコメント化（重要2）。見送り項目は設計ドラフト §C 実装後レビューに記録
- **v0.1.6 機能 A：スタートアップ snapshot 改ざん対策（SHA256 + ACL）**（[設計ドラフト v2](docs/v0.1.6_候補設計ドラフト_v2.md) §A 実装・2026-05-31）
  - `Services/StartupSnapshotIntegrity.cs`（新規・純粋静的クラス）：SHA256 サイドカー（`<snapshot>.sha256`・sha256sum 互換）の生成・検証・ACL 保護。`ComputeSha256(Stream)` オーバーロードでファイル無しの単体テストも可能（機能 B の StartupSnapshotDiffer と同じ流儀）
  - `BackupService.BackupStartupSnapshot()`：snapshot 作成時に SHA256 サイドカーを自動生成（失敗しても snapshot 自体は止めない）。ファイル名をミリ秒精度（`_fff`）化し管理者版/非管理者版の同名衝突を回避
  - `BackupService.VerifyStartupSnapshotIntegrity(path)`（新規公開）：一致=true / 改ざん疑い or サイドカー破損=false / サイドカーなし（v0.1.5 以前）=null
  - `BackupService.CleanupOldStartupSnapshots()`：ローテ削除時に `.reg` と対の `.sha256` を個別削除（孤児化防止）。並びキーを LastWriteTime→ファイル名に統一
  - `StartupListViewModel.RestoreSnapshot()`：差分算出より前に整合性検証（改ざん検知で既定いいえの警告 → 中止）。import 直前に再検証して TOCTOU 窓を封鎖
  - `DialogService.ConfirmDestructive()`（新規）：既定ボタン＝いいえ・警告アイコン（破壊側に倒れてはいけない確認用）
  - ACL：app.manifest が asInvoker のため Owner は触らず DACL のみ操作（現ユーザー Modify + SYSTEM/管理者 Full + 継承遮断で他ユーザー上書き防止・`.sha256` のみ・失敗は無害）
  - **テスト**：`StartupSnapshotIntegrityTests`（既知ハッシュベクトル・改ざん検知・fail-safe・ファイル名欄無視・境界 の **17 ケース全 PASS**／機能 B 15 ケースと合わせ計 32 PASS）
  - plan-validator 反映：ACL 自己ロックアウト回避（現ユーザー Modify 維持）/ 同名衝突対策（ミリ秒）/ Verify を差分算出の前に配置 / テストは一時ディレクトリで %APPDATA% 非汚染
  - cross-reviewer 反映：整合性 Confirm を既定いいえ化（軽微1）/ import 直前の再 Verify で TOCTOU 封鎖（重要3）/ ローテ並びをファイル名基準に（軽微3）。見送り項目（ACL 孤児累積 / 健全 snapshot へのフォールバック導線 / エンコード正規化）は設計ドラフト §A 実装後レビューに記録
- **v0.1.6 機能 B：スタートアップ snapshot 復元前の差分件数警告**（[設計ドラフト v2](docs/v0.1.6_候補設計ドラフト_v2.md) §B 実装・2026-05-30）
  - `Services/StartupSnapshotDiffer.cs`（新規・純粋関数）：snapshot(.reg) と現在のレジストリ状態を比較し追加/削除/変更件数を算出（snapshot は読込のみ＝副作用なし）
  - `Models/StartupDiffResult.cs`（新規 record）：差分結果（件数 + エントリ内訳・将来の C プレビュー UI で再利用）
  - `StartupListViewModel.RestoreSnapshot()`：復元確認ダイアログに差分サマリ（追加N/削除N/変更N・10 件以上で大規模警告・解析失敗時は「差分なし」誤表示を回避）を表示
  - 設計改善：設計書の `BackupService.AnalyzeSnapshotDiff(path)` を現在エントリ引数の純粋関数化（I/O 層→VM 層の逆依存を回避・StartupApprovedCodec と同じ static 流儀）
  - 突合は `StartupItemsProvider.MergeApprovedState` と整合（RawName OrdinalIgnoreCase・StartupApproved\Run→HKLM_Run / Run32→Wow64_Run / HKCU→HKCU_Run・RunOnce 系と未登録は有効扱い）
  - **テスト基盤新設**：`tests/WinKnobs.Tests`（xUnit・本リポジトリ初の Unit test プロジェクト）+ `StartupSnapshotDifferTests`（差分 0/1/5/10+・REG_EXPAND_SZ・hex 継続行・削除マーカー・プレフィックス・Wow64・破損 reg の **15 ケース全 PASS**）
  - cross-reviewer 反映：算出失敗時の誤表示回避（重要1）/ プレフィックス・Wow64・Empty テスト追加（重要2）/ 同期実行コメント明示（重要3）/ 閾値 const 化（軽微）

### Notes
- バージョン bump：v0.1.5-dev → v0.1.6（csproj AssemblyVersion=0.1.6.0 / FileVersion=0.1.6.0 / MainWindow ステータスバー / MainViewModel 環境情報）
- 裏付け：xUnit **42 PASS**（B15 + A17 + C10）/ build 0 警告 0 エラー / VM-UIA **65 OK・0 FAIL**（リグレッションなし）/ plan-validator・cross-reviewer 致命 0
- 残（任意）：VM 実機目視（A 改ざん警告 / C プレビュー UI）。Public 化前残作業は Unreleased の Planning を参照

---

## [0.1.5-dev] - 2026-05-27

### Added
- **Phase R-3：スタートアップ系の追加・削除・snapshot 完成**
  - `StartupItemsProvider.Delete()`：Run キーと StartupApproved 両方からエントリ削除（RunOnce 系は StartupApproved スキップ・best-effort）
  - `StartupItemsProvider.AddNew()`：新規 Run 値書込 + 同一ハイブ名重複ブロック + Name 長さバリデーション（255 文字上限）
  - `BackupService.BackupStartupSnapshot()`：全 6 Run ハイブ + 3 StartupApproved を 1 つの統合 .reg にエクスポート
  - `BackupService.GetLatestStartupSnapshotPath()` / `ListStartupSnapshots()`：snapshot 一覧 / 最新パス取得
  - StartupListViewModel に `AddCommand` / `DeleteCommand` / `RestoreSnapshotCommand` 追加
  - StartupListView：「+ 追加」「🗑 削除」を enable + 新規「🔄 復元」ボタン追加（AutomationId 整備）
  - StartupEntryEditViewModel：`IsNewMode` / `EditableName` / `SelectableHives` / `SelectedHive` / `LoadNew()` / `CreateNewEntry()` で Add モード対応
  - StartupEntryEditDialog：Hive ComboBox / Name TextBox を Visibility 切替で Add/Edit モード分岐
  - UI Automation テスト 3 件追加：`tc_startup_add.ps1` / `tc_startup_delete.ps1` / `tc_startup_admin.ps1`（GUID 8 文字でユニーク化・try/finally cleanup）
- **Phase R-2：スタートアップ系の編集・トグル・楽観ロック完成**（commit 75d0c75）
  - CheckBox 即時トグル + 編集ダイアログ + 保存ボタン + 楽観ロック
  - StartupEntryEditViewModel / StartupEntryEditDialog 新規実装
  - 致命 6 件 + 依存 UX バグの対応反映（HKLM_Wow64 View 統一・列ヘッダ「即時」明示・SaveDirty ループ内 try-catch・Original 保持と RevertChanges・CanToggleEnabled で IsDirty 中 disable）
  - UI Automation テスト 2 件追加（tc_startup_smoke / tc_startup_edit）
- **R-2 hotfix 3 件**（commit 5391ec6）
  - IsDirty 残留バグ（無変更 OK で IsDirty=true 残留）→ StartupEntryEditViewModel.Ok() で IsModified=true 時のみ書戻し
  - 楽観ロック比較対象バグ → StartupItemsProvider.Save() の比較対象を `entry.Command` → `entry.OriginalCommand`
  - StartupListViewModel.Edit() で IsDirty 連動ステータス文言
- **UIA × WPF CheckBox 制約調査**（commit 6731b66）
  - 真因確定：WPF CheckBox は UIA `InvokePattern` 非実装・`TogglePattern.Toggle` は WPF Command 非発火
  - 本体コード問題なし（実機マウスクリックでは PASS）
  - tc_startup_edit.ps1 の toggle 3 件を INFO 化（誤検知の塗り重ね回避）
  - run_ui_test.ps1 に `LimitationsFlagged` カウンタ追加（CI 緑化副作用防止）
- **OSS化 Week 3 完遂**：CONTRIBUTING.md / SECURITY.md / CODE_OF_CONDUCT.md / CHANGELOG.md / .github/ ディレクトリ整備
- バージョン bump：v0.1.4-dev → v0.1.5-dev（csproj AssemblyVersion=0.1.5.0 / MainWindow ステータスバー / MainViewModel 環境情報）

### Changed
- BackupService にスタートアップ snapshot 専用サブディレクトリ `%APPDATA%\WinKnobs\backup\startup\` + 独自ローテーション 10 件（既存 28 項目バックアップの 10 件枠と完全分離）
- EnsureSnapshot に 10 秒スロットル（連続 Modify で reg.exe 9 連発による UI 固まり回避）
- StartupListViewModel コンストラクタに BackupService 追加（DI）
- DialogService.OpenStartupAdd 追加（Name 重複チェックコールバック受領）

### Fixed
- plan-validator 致命高1：snapshot 専用サブディレクトリ分離で個別バックアップ瞬殺リスク解消
- plan-validator 致命高2：RestoreSnapshot 前に IsDirty 中エントリ Confirm（編集中作業消失防止）
- cross-reviewer 致命高1：Add Name 重複をダイアログ内リアルタイム警告化（同一ハイブ重複は IsValid=false で OK 無効化・他ハイブ重複は StatusMessage 警告）
- cross-reviewer 致命高2：BackupStartupSnapshot の空 section / 無効 section 混入を検証強化（`[HKEY_` / `[-HKEY_` 含有 + section 数チェック）
- cross-reviewer 致命中4：EnsureSnapshot 10 秒スロットルで連続 Modify の体感固まり回避
- cross-reviewer 致命中7：tc_startup_delete の「はい」「Yes」ボタン検出をパターンマッチ強化（Windows 11 24H2 アクセスキー OFF 対応）

### Security
- パスワード生平文・VM 認証情報・ホスト固有ログ・引き継ぎメモを .gitignore 除外（OSS化 Week 3）

---

## [0.1.4-dev_fix] - 2026-05-20

### Fixed
- **旧プロセス残存バグ**：管理者として再起動後、旧プロセス（非管理者）が即座に終了しないケース対応
  - `PrivilegeService` に `Environment.Exit(0)` 1500ms フォールバック追加
  - `vm_test_full.ps1` のプロセス監視ロジック堅牢化

### Added
- VM 統合テスト第3波 PASS（PID 3248 / Memory 93.1MB）

---

## [0.1.4-dev] - 2026-05-20

### Added
- **BUG-3 対応：HKLM 書込時の管理者権限要求**
  - 新規 `PrivilegeService` クラス（IsRunningAsAdministrator / TryRestartAsAdministrator / ShutdownCurrentProcess / IsShutdownPending）
  - `DialogService.ConfirmElevation` 3 択ダイアログ（管理者として再起動 / このまま続行 / キャンセル）
  - `SettingDialogViewModel` の DoApply / RestoreSetting / LoadSetting で `UnauthorizedAccessException` / `SecurityException` / `InvalidOperationException` を捕捉して管理者再起動を誘導
- VM ステータスバー `v0.1.4-dev` 表示確認 PASS
- 標準権限 → 3 択ダイアログ → UAC → 管理者再起動フロー PASS
- 管理者起動後 PR1 適用成功 PASS

### Changed
- `using System.Security.Principal;` 削除（PrivilegeService に統一）
- `IsAdministrator()` private メソッド削除（DI 経由に統一）

### Reviewed
- @plan-validator 8 件 + エッジケース 6 件指摘 → 全反映
- @cross-reviewer M-2（LoadSetting への適用）+ U-1（ダイアログ文言改善）反映

---

## [0.1.3-dev] - 2026-05-18

### Fixed
- **BUG-1**：レジストリキー不在時のバックアップ失敗
  - `BackupService.ExportKey` にキー存在チェック追加
  - 不在時は `[-FullKeyPath]` 記法のマーカー .reg を生成
- **BUG-2**：K1（NTFS 8.3 名）が 2 択固定だったため、Windows 既定値 2 や 3 を扱えなかった問題
  - 4 択（0/1/2/3）に拡張
  - `BoolBackup` → `IntBackup` パターンに統一（K2 と同じパターン）
  - `ParseDisable8dot3Index` ヘルパー切り出し（英/日両環境対応）

### Discovered
- **BUG-3**：HKLM 書込時の Access denied → v0.1.4 で対応

---

## [0.1.2-dev] - 2026-05-17

### Fixed
- **N1（TCP Auto-Tuning）パースバグ**：netsh 出力の値マッチで他項目（ECN Capability）の値を誤拾い
  - `ParseAutoTuningFromNetsh` ヘルパーに切り出し
  - 行毎に分割 → "Auto-Tuning" を含む行に絞り込み → コロン後抽出

### Changed
- バージョン文字列を `v0.1.0-dev` から `v0.1.2-dev` に更新（MainViewModel.cs / MainWindow.xaml）

---

## [0.1.2] - 2026-05-16

### Added
- **プライバシー系 3 項目**：
  - PR1 Cortana 無効化（HKLM Policies）
  - PR2 Activity History 無効化
  - PR3 SmartScreen 動作モード（3択 Command）
- **ネットワーク系 1 項目**：
  - N1 TCP Auto-Tuning（5択 netsh CommandAction）
- **シェル系 2 項目**：
  - S1 アイコンキャッシュクリア（実行系・CanRestore=false）
  - S2 BagMRU クリア（実行系・CanRestore=false）
- テーマメニューに ✓ チェックマーク連動

### Changed
- 実装済み設定項目：22 → **28 項目**

---

## [0.1.1] - 2026-05-16

### Added
- **基礎カテゴリ 6 項目**：
  - L1 Fast Startup（HKLM）
  - L2 Prefetcher（4 択 Command）
  - K1 NTFS 8.3 名（fsutil CommandAction）
  - K2 NTFS LastAccess（fsutil CommandAction）
  - P1 Win32PrioritySeparation（3 択 Command）
  - P2 SystemResponsiveness

### Changed
- 実装済み設定項目：16 → **22 項目**

---

## [0.1.0] - 2026-05-15

### Added
- **初版リリース**：MVVM / DI / WPF ベースの基盤完成
- **基礎 9 項目** migration 完了（A3-2-c 型拡張）：
  - 既存項目を新 RestartScope（None / Explorer / Logoff / System）に migration
- **Win11 価値項目 W1-W6, W8（7 項目）**：
  - W1/W2 ダークモード（アプリ / システム）
  - W3 MenuShowDelay（数値入力）
  - W4 タスクバーサイズ（3 択 Command）
  - W5 クラシック右クリック（2 択 Command）
  - W6 Bing 検索無効化
  - W8 Link Binary（4 byte）
- **A3-2-c 型拡張**：
  - `RestartScope` enum（None / Explorer / Logoff / System）
  - `CommandAction` クラス（Read / Apply / Backup / Restore / Choices + CanRestore）
  - `SettingValueType` に Binary / Command 追加
  - `SettingItem` 拡張（RestartScope / Command / ExpectedByteLength / NumericStringRange）
- **インライン編集 UX**：
  - 「現在値（読込専用）」+「新しい値（編集可）」2 行 → 「値（クリックで変更）」1 行に統合
  - `OriginalValue / EditableValue / IsModified` 三者同期
  - 「戻す」3 段階（編集破棄 / 適用後復元 / 対象なし）
  - 編集中オレンジ枠 / 適用済み薄緑枠
- **ダーク UI 第 3-6 波修正**：
  - ComboBox / Button / MenuItem ControlTemplate 完全置換
  - チェックマーク復活・位置固定

### Reviewed
- @plan-validator V-1 〜 V-9 / VE-1 〜 VE-6 すべて反映
- @cross-reviewer 致命度高 5 件 + 中度 U-3 / M-2 反映

### Tested
- VM（Hyper-V Win11 クリーン）で T-1 〜 T-10 実機テスト PASS

---

## [0.0.x] - 2026-05-09 〜 2026-05-12

### Added
- Phase 0：プロジェクト準備（README / 開発計画 / 項目リスト / Hyper-V ガイド）
- Phase 1：WPF 骨格・MVVM・サービス層・テーマ・サブダイアログ
- Phase 1-3：仮想 Win11 で T-1 〜 T-10 実機テスト（7 件成功）

---

## バージョン番号方針 / Versioning Policy

- **Major (X.0.0)**：破壊的変更・データ移行が必要な場合
- **Minor (0.X.0)**：機能追加・後方互換維持
- **Patch (0.0.X)**：バグ修正・後方互換維持
- **-dev 接尾辞**：開発中の不安定版（CI/CD 未通過）
- **_fix 接尾辞**：dev 版に対する緊急修正（dev_fix）

### ⚠️ SemVer 自動化ツールとの互換性

`_fix` サフィックスは **SemVer 厳密準拠ではない**ため、`semantic-release` / `github-release-drafter`
等の自動化ツール導入時には注意が必要です。

正式リリース（v1.0.0 以降）では `_fix` サフィックスを使わず、通常の patch increment
（v1.0.1 → v1.0.2）で対応します。dev 期間中の暫定運用としてのみ使用しています。

---

## 関連リンク / Related Links

- [README.md](README.md)：プロジェクト概要
- [CONTRIBUTING.md](CONTRIBUTING.md)：貢献ガイド
- [SECURITY.md](SECURITY.md)：脆弱性報告
- [GitHub Releases](https://github.com/non-x2/WinKnobs/releases)：バイナリ配布（リリース後）
