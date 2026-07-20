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
  - Phone Link 自動開始：追加裏取りで解決済み（2026-07-05）→「自動起動のみ」は既存 Run キー管理（起動・スタートアップ）で対応可能・追加実装不要。「機能全体の無効化」（`Mobility\PhoneLinkEnabled`）は実装可能だが、のんさん自身が Phone Link を実運用中のため優先度低・見送り
  - フォーカスアシスト自動条件（実装複雑度高で保留）
- **v0.4.x Gaming 拡充**：プリセット機能（複数設定の一括切替）は設計ドラフト作成済み（[v0.4.x_Gamingプリセット機能_設計ドラフト.md](docs/v0.4.x_Gamingプリセット機能_設計ドラフト.md)・実装は承認後）
- **v0.4.x ネットワーク細分化**（2026-07-05・格闘ゲーム遠距離対戦＋4K ストリーミング向け）で調査し**見送った**候補：
  - **受信/送信バッファ最大化**：実機の Intel I219-V で `*ReceiveBuffers`/`*TransmitBuffers` の `ValidRegistryValues` が空（ドライバがレンジを公開しない個体）で「実機から動的に最大値を検出」ができず、ハードコードは他 NIC で無効値になるリスクがあるため見送り（本機は現在値 2048 で既に上限相当の可能性）
  - **QoS パケットスケジューラの予約帯域解除**（`NonBestEffortLimit`）：WebSearch 裏取りで「未使用の 20% を常時確保している」という前提自体が誤りと判明（QoS 要求時のみ有効になる予約上限であり、通常のゲーム/配信シナリオでは体感効果なしとの分析が定説）→ 実装見送り
- **M-1 リファクタ**（v0.2 期間中漸進対応）：HKLM 3 択フォールバックのヘルパー化
- **Public 化前残作業**（[Public化前準備物 v1](docs/Public化前準備物_v1.md)）：v0.1.6 タグ / Releases / Sponsors / Dependabot / Discussions / Security Advisory / Branch Protection / Public 化

---

## [0.4.9] - 2026-07-21（プライバシーノブ2本・診断データ最小化＋広告ID無効化）

> 📝 privacy.telemetryMinimized（HKLM Policies\DataCollection・厳密1判定・既定=値削除）＋privacy.advertisingIdDisabled（HKCU AdvertisingInfo・厳密0判定）。plan-validator 精査済み設計を反映。

### Added
- **プライバシーノブ2本を追加**（プライバシー・🟡/🟢）：
  - **privacy.telemetryMinimized**：`HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry`（REG_DWORD・厳密に1の時のみ最小化済み・0/2/3/未設定は「既定に戻す」扱い・RestartScope=System・要管理者）。Win11 Home/Pro で選べる実質下限（0 は Enterprise/Education 限定のため不採用）。「既定に戻す」は Copilot/BackgroundApps の「既定=0書込」とは異なり値の削除（ポリシー未構成へ）が正しい（ポリシー不在が Windows 既定・plan-validator 指摘反映）。
  - **privacy.advertisingIdDisabled**：`HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo\Enabled`（REG_DWORD・厳密に0の時のみ無効化済み・未設定/1/その他は「既定に戻す」扱い・RestartScope=None・管理者不要）。plan-validator 指摘により Boolean 型は不採用：Boolean の復元はキー削除マーカー経由で同居する広告ID実体 `Id` 値を巻き添えにする（2026-06-10 事例の再発防止）。Command 2択＋値単位（Enabled 単体）のトークン復元とし、Id 値には触れない。
  - 新規 `TelemetryMinimizeHelper`/`AdvertisingIdHelper`（純粋関数・DisableCopilotHelper と同型）＋xUnit 50件追加（全1213件 green）。

---

## [0.4.8] - 2026-07-21（バックグラウンドアプリ一括無効化・システム）

> 📝 system.disableBackgroundApps（HKCU BackgroundAccessApplications・厳密1判定）。管理者不要・自走開発ループ初回サイクル（バックログ#1）。

### Added
- **バックグラウンドアプリを一括無効化**（システム・🟡・HKCU・管理者不要）：
  - **system.disableBackgroundApps**：`HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications\GlobalUserDisabled`（REG_DWORD・厳密に1の時のみ無効化・0/未設定/1以外は「有効にする（既定）」扱い・RestartScope=Logoff）。UWP/Store アプリ全体のバックグラウンド実行を一括停止（Win32 アプリは対象外）。個別アプリごとの許可設定より優先して一律停止・通知/新着メール等が届かなくなる場合あり。反映と復元のいずれもログオフ・再ログオンが必要。企業のグループポリシー管理下では上位ポリシー（LetAppsRunInBackground）が優先される場合がある。
  - 新規 `DisableBackgroundAppsHelper`（純粋関数・DisableCopilotHelper と同型）＋xUnit 26件追加（全1163件 green）。競合差分ドキュメント（[競合差分_2026-07-06_LINE_Harness動画手法.md](docs/競合差分_2026-07-06_LINE_Harness動画手法.md)#4）の差分を実装済みに更新。

---

## [0.4.7] - 2026-07-15（Copilot 無効化 2 項目・プライバシー/デスクトップ）

> 📝 privacy.disableCopilot（HKCU Policies・厳密1判定）＋desktop.hideCopilotButton（HKCU Explorer\Advanced）。いずれも管理者不要。

### Added
- **Copilot を無効化する 2 項目を追加**（プライバシー/デスクトップ・🟢・HKCU・管理者不要）：
  - **privacy.disableCopilot**：`HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot\TurnOffWindowsCopilot`（REG_DWORD・厳密に1の時のみ無効化・0/未設定/1以外は「有効にする（既定）」扱い・RestartScope=Logoff）。企業のグループポリシー管理下では上位ポリシーが優先される場合がある。
  - **desktop.hideCopilotButton**：`HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\ShowCopilotButton`（REG_DWORD・0=非表示／未設定・1=表示する（既定）・RestartScope=Explorer）。タスクバーの Copilot ボタンを非表示にする。
  - 新規 `DisableCopilotHelper`/`HideCopilotButtonHelper`（純粋関数・TaskManagerHelper と同型）＋xUnit追加。競合差分ドキュメント（[競合差分_2026-07-06_LINE_Harness動画手法.md](docs/競合差分_2026-07-06_LINE_Harness動画手法.md)#2）の差分を実装済みに更新。

---

## [0.4.6] - 2026-07-05（割り込み加減（Interrupt Moderation）無効化・ネットワーク）

> 📝 `*InterruptModeration`（NDIS標準・PowerShell経由・物理アダプタ全台）。実機読取コマンドで動作確認済み。

### Added
- **割り込み加減（Interrupt Moderation）を無効化**（ネットワーク・🟡・要管理者・物理アダプタ全台一括）：
  - `*InterruptModeration`（NDIS標準キーワード・`ValidRegistryValues={0,1}`実機確認済み・0=無効/1=有効(既定)）。NIC「詳細設定」のプロパティは固定レジストリパスを持たずドライバ内部で管理されるため、`Get/Set-NetAdapterAdvancedProperty`（PowerShell）経由（NIC省電力プリセット v0.2.2 と同じ理由）。
  - 新規 `InterruptModerationHelper`（純粋関数・xUnit 15件）＋`InterruptModerationService`（-EncodedCommand実行・60秒タイムアウト）。単一の標準化キーワード・固定値域のため NicPowerService のような GUID単位スナップショットストアは持たず、退避はアプリ標準の SettingItem Backup/Restore（↩戻す）に委譲する軽量版。
  - 無効化＝低遅延化する場合がある一方 CPU 負荷が増えるトレードオフのため中立表記。実機で読取コマンドの動作を確認済み（書込は実機ネットワーク切断リスクのため今回は自動テストのみ・在席実機検証は次回）。

---

## [0.4.5] - 2026-07-05（配信の最適化：ピアツーピア共有を無効化・ネットワーク）

> 📝 DODownloadMode（HKLM Policies・要管理者）。Microsoft Learn 公式リファレンスで裏取り済み。

### Added
- **配信の最適化（Delivery Optimization）のピアツーピア共有を無効化**（ネットワーク・🟡・要管理者）：
  - `HKLM\Software\Policies\Microsoft\Windows\DeliveryOptimization\DODownloadMode`（REG_DWORD・0=HTTP Only／未設定=LAN(1)既定）。既定では同一ネットワーク内の他 PC とアップデート等を共有（アップロード）し得るため、上り回線が細い環境では 4K ストリーミング視聴中の帯域圧迫要因になり得る（Windows Update 等が裏で動いている時のみ効果・常時ではない点は中立表記）。
  - ConfirmFileDelete（v0.3.19）と同型：Policies 配下でキー未存在から新規作成され得るため、値単位の Command 型2択＋DeleteOrThrow で巻き添え回避。実機確認：Pro edition でキー未作成＝既定状態。

---

## [0.4.4] - 2026-07-05（TCP Nagle アルゴリズム無効化・ネットワーク）

> 📝 TCPNoDelay（HKLM・物理アダプタ全台・要管理者）。TCP即時ACK（v0.3.21）の相棒設定。

### Added
- **TCP Nagle アルゴリズムを無効化**（ネットワーク・🟡・要管理者・物理アダプタ全台一括）：
  - `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}\TCPNoDelay`（REG_DWORD・1=無効化／未設定=既定）。TcpAckFrequency（v0.3.21・即時ACK）と両方設定して初めて Nagle アルゴリズムを完全に無効化できる（片方だけでは不完全）。パス生成は既存 `TcpAckFrequencyHelper.BuildInterfacePath` を再利用（新規 Helper 不要）。
  - ⚠️ 格闘ゲーム等の対戦データ自体は多くが UDP のため恩恵は限定的（ロビー・チャット等 TCP 部分向け）と中立に明記。

---

## [0.4.3] - 2026-07-05（スタートメニュー「おすすめ」非表示・デスクトップ・UI）

> 📝 HideRecommendedSection（HKCU・管理者不要）。Windows 11 Home では効果が不安定という報告があり中立表記。

### Added
- **スタートメニューの「おすすめ」セクションを非表示**（デスクトップ・UI・🟢・管理者不要）：
  - `HKCU\Software\Policies\Microsoft\Windows\Explorer\HideRecommendedSection`（REG_DWORD・1=非表示／未設定=表示）。ConfirmFileDelete（v0.3.19）と同型：Policies 配下でキー自体が未存在から新規作成され得るため、値単位の Command 型2択＋DeleteOrThrow で巻き添え回避。
  - 実機確認：Pro edition（本機）でキー未作成＝既定表示状態を確認済み。

---

## [0.4.2] - 2026-07-04（ハードウェアアクセラレータ GPU スケジューリング・ゲーミング）

> 📝 設定アプリの同名トグルと同じ機能。HwSchMode（HKLM・要管理者・要システム再起動）。効果はGPU/ドライバ依存のため中立表記。

### Added
- **ハードウェアアクセラレータ GPU スケジューリング（HAGS）**（ゲーミング・🟡・要管理者・Windows再起動で反映）：
  - GPU の VRAM キュー管理を GPU 自身が主導するモードを ON にする（`HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode`・REG_DWORD・2=有効／未設定=ドライバ・システム判断）。低遅延化の報告がある一方、VRAM を追加で最大1GB程度使うため VRAM の少ない環境や非対応GPUではかえって不安定になる報告もあり中立表記。
  - 「無効(1)を明示選択」は選択肢に出さず「既定/有効化」の2択に絞った（実用上は有効化するかどうかだけが焦点のため）。GraphicsカテゴリはGraphicsListView専用ペインを持つため、標準DataGridで確実に表示できるGamingカテゴリへ配置。

---

## [0.4.1] - 2026-07-04（ゲーム用タスクのスケジューリング優先度・ゲーミング）

> 📝 定番tweak。Scheduling Category（HKLM・要管理者・既定 Medium）。GPU Priority は既定で高め設定済み。

### Added
- **ゲーム用タスクのスケジューリング優先度**（ゲーミング・🟡・要管理者・次回ゲーム起動で反映）：
  - MMCSS がゲームプロセスに割り当てる CPU スケジューリング優先度を「Medium」→「High」に引き上げ（`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games\Scheduling Category`・REG_SZ）。
  - 値が DWORD でなく REG_SZ 文字列である点が他の Command 型と異なる。キーは Windows 標準で常設のため通常は ValueExisted=true（万一の消失環境も DeleteValueOrThrow で保険）。

## [0.4.0] - 2026-07-04（全画面最適化 無効化・ゲーミング）

> 📝 有名な定番tweak。GameDVR_FSEBehaviorMode + HonorUserFSEBehaviorMode（HKCU・管理者不要・既定 OFF＝最適化する）。効果は環境依存のため中立表記。

### Added
- **全画面最適化（フルスクリーン最適化）無効化**（ゲーミング・🟢・管理者不要・次回ゲーム起動で反映）：
  - ゲームの全画面表示を常に排他モードで動かすかを ON/OFF（`HKCU\System\GameConfigStore` の `GameDVR_FSEBehaviorMode`=2 と `GameDVR_HonorUserFSEBehaviorMode`=1 を同時書込）。Game DVR（v0.3.8）と同じ2値同時パターン（`ReadDwordManaged`/`TryRestoreOne` 共通ヘルパー再利用）。
  - 入力遅延改善の報告がある一方、アルトタブ復帰が遅くなる等の副作用報告もあり中立表記（WebSearch 複数ソースで裏取り）。

---

## [0.3.22] - 2026-07-04（手書き入力パッドの自動起動・キーボード/マウス）

> 📝 ペンタップ時の定番。EnableDesktopModePenAutoInvoke（HKCU・管理者不要・既定 ON）。

### Added
- **手書き入力パッドの自動起動（ペンタップ時）**（キーボード/マウス・🟢・管理者不要・即時反映）：
  - ペンでテキスト欄をタップしたときに手書き入力パッドを自動的に開くかを ON/OFF（`HKCU\Software\Microsoft\TabletTip\1.7\EnableDesktopModePenAutoInvoke`・REG_DWORD・1=自動的に開く/0=開かない）。
  - **既定 ON（未設定＝自動的に開く）** のため ShowRecent 同型（Command 型2択＋`ValueExisted`）。候補項目集の元記載キー（`InputPersonalization\TrainedDataStore`）は誤り（実際は連絡先収集用途）と実機裏取りで判明・修正。

## [0.3.21] - 2026-07-04（TCP 即時 ACK・ネットワーク）

> 📝 物理アダプタ個別のレイテンシ tweak。TcpAckFrequency（HKLM・要管理者・既定 OFF）。効果に賛否あり中立表記。

### Added
- **TCP 即時 ACK（TcpAckFrequency）**（ネットワーク・🟡・要管理者・再起動反映）：
  - TCP の遅延確認応答を無効化し、パケット受信のたびに即座に ACK を返す（`HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}\TcpAckFrequency`・REG_DWORD・アダプタ個別・1=即時 ACK）。RDP/SSH 等の対話的通信でレイテンシ改善の報告がある一方、大量データ転送では逆効果の報告もあり中立表記（Microsoft Learn で裏取り）。
  - Network Throttling Index と異なり単一パスでなく**物理アダプタ全台が対象**。アダプタ列挙は既存 `NetworkService.GetActivePhysicalAdapters`（DNS/DoH と同じ物理判定基準）を再利用。新規 `TcpAckFrequencyHelper`（パス生成のみ・xUnit3件）。

---

## [0.3.20] - 2026-06-30（クイックアクセスに「最近使ったファイル」を表示・シェル）

> 📝 エクスプローラ定番。ShowRecent（HKCU・管理者不要・**既定 ON**）。ShowFrequent（よく使うフォルダー）は別項目。

### Added
- **クイックアクセスに「最近使ったファイル」を表示**（シェル・🟢・管理者不要・エクスプローラ再起動で反映）：
  - クイックアクセス（ホーム）の「最近使ったファイル」表示を ON/OFF（`HKCU\…\Explorer\ShowRecent`・REG_DWORD・1=表示/0=非表示）。
  - **既定 ON（未設定＝表示）** のため **Command 型2択＋`ValueExisted`** で実装（OFF→戻すで未設定＝表示へ正しく復元・DeleteOrThrow）。

### Fixed
- **Command 型「戻す」の DeleteValue 誤判定を修正**（共通 `DeleteValueOrThrow` 化）：`DeleteValue=false` には「削除失敗」と「元から値が無い（他ツールが先に削除済み＝望む状態）」の2通りがあり、後者で誤って例外を投げていた。削除後に値が残存する時だけ throw するよう精緻化し、LaunchTo(v0.3.14)/TaskbarGlom(v0.3.15)/Start_TrackProgs(v0.3.17)/ConfirmFileDelete/ShowRecent の全 Command Restore に適用（cross-reviewer 2026-06-30・見落とし事例ログ 2026-06-10 の逆パターン）。

## [0.3.19] - 2026-06-30（ファイル削除時の確認ダイアログ・シェル）

> 📝 うっかり削除防止の定番。ConfirmFileDelete（HKCU\…\Policies\Explorer・管理者不要・既定 OFF）。

### Added
- **ファイル削除時に確認ダイアログを表示**（シェル・🟢・管理者不要・エクスプローラ再起動で反映）：
  - ごみ箱へ移動するときの確認ダイアログを ON/OFF（`HKCU\…\Policies\Explorer\ConfirmFileDelete`・REG_DWORD・1=確認する/0=確認しない）。推奨＝確認する（誤削除防止）。
  - `Policies\Explorer` は新規作成され得るため、宣言的 Boolean（reg import でキーごと削除＝他ポリシー値を巻き添え）でなく **Command 型2択＋ValueExisted の値単位 DeleteOrThrow** で実装（巻き添え回避）。

## [0.3.18] - 2026-06-30（ナビゲーションウィンドウにすべてのフォルダーを表示・シェル）

> 📝 エクスプローラ定番（Win10 風の詳しい左一覧）。NavPaneShowAllFolders（HKCU・管理者不要・既定 OFF）。

### Added
- **ナビゲーションウィンドウにすべてのフォルダーを表示**（シェル・🟢・管理者不要・エクスプローラ再起動で反映）：
  - 左のナビゲーションウィンドウに「すべてのフォルダー」（ごみ箱/コントロールパネル/ライブラリ等）を表示（`HKCU\…\Explorer\Advanced\NavPaneShowAllFolders`・REG_DWORD・1=表示/0=非表示）。値 1=ON がユーザー視点と一致＝宣言的 Boolean（既定 OFF＝復元無害）。

## [0.3.17] - 2026-06-30（「よく使うアプリ」を表示／アプリ起動の記録・プライバシー）

> 📝 プライバシー寄りの定番。Start_TrackProgs＝アプリ起動回数の記録（「よく使うアプリ」表示）。HKCU・管理者不要・**既定 ON**。

### Added
- **「よく使うアプリ」を表示（アプリ起動の記録）**（プライバシー・🟢・管理者不要・スタート開き直しで反映）：
  - スタート/検索の「よく使うアプリ」表示＝アプリ起動の記録を ON/OFF（`HKCU\…\Explorer\Advanced\Start_TrackProgs`・REG_DWORD・1=記録する/0=記録しない）。
  - **既定 ON（未設定＝記録する）** のため宣言的 Boolean（reg import マージで未設定へ戻せない）ではなく **Command 型2択＋`ValueExisted` 退避**で実装。「記録しない」適用後の「↩戻す」は、元が未設定なら値を**削除**して OS 既定（記録する）へ正しく復元（DeleteOrThrow）。

## [0.3.16] - 2026-06-30（マウスホイールのスクロール行数・キーボード/マウス）

> 📝 定番の数値調整。WheelScrollLines（HKCU・管理者不要）。MenuShowDelay と同じ String 数値型。

### Added
- **マウスホイールのスクロール行数**（キーボード・マウス・🟢・管理者不要・再ログオンで反映）：
  - ホイール1ノッチのスクロール行数を **1〜100**（既定 3）で設定（`HKCU\Control Panel\Desktop\WheelScrollLines`・REG_SZ・`NumericStringRange=(1,100)`）。
  - 「0（スクロールしない）」「-1（1画面ずつ）」は非対象（数値モデル維持）。範囲外の既存値は現在値を表示しつつ保存時に 1〜100 で検証。
  - 反映タイミングは再ログオン（各アプリが起動時にキャッシュするシステムメトリックのため・WinKnobs は自動サインアウトしない）。

## [0.3.15] - 2026-06-30（タスクバー ボタンの結合・デスクトップ）

> 📝 いじくる定番。Win11 設定にもあるが、24H2 で主/他モニタが分割（TaskbarGlomLevel/MMTaskbarGlomLevel）。HKCU・管理者不要。

### Added
- **タスクバー ボタンの結合**（デスクトップ・🟢・管理者不要・エクスプローラ再起動で反映）：
  - 同一アプリの窓をタスクバーで結合するかを Command 型3択（`HKCU\…\Explorer\Advanced\TaskbarGlomLevel`・REG_DWORD・**0=常に結合/1=入りきらないとき/2=結合しない**・値=index の identity）。
  - 24H2 のマルチモニタ分割に対応し **主（TaskbarGlomLevel）と他モニタ（MMTaskbarGlomLevel）の両キーに同値を書く**。Backup は両キーの existed＋値（4 フィールド）、Restore は各キー existed=false で DeleteOrThrow。
  - 未設定/範囲外は「常に結合（OS 既定）」として表示（新規 `TaskbarGlomHelper`＋テスト）。

### Tests
- `TaskbarGlomHelperTests`（identity 変換・管理外/未設定判定・REG_SZ 解釈・Read fallback 等）。

## [0.3.14] - 2026-06-30（エクスプローラの起動先・シェル）

> 📝 いじくるツール代替の定番。「ダウンロード」は Windows の設定 GUI に無くレジストリ限定＝差別化。HKCU・管理者不要。

### Added
- **エクスプローラを開いたときの場所**（シェル・🟢・管理者不要・次回起動から反映）：
  - エクスプローラの初期表示を「ホーム（クイックアクセス）/ この PC / ダウンロード」から選ぶ Command 型3択（`HKCU\…\Explorer\Advanced\LaunchTo`・REG_DWORD・1=このPC/2=ホーム/3=ダウンロード）。
  - 値≠index（新規 `LaunchToHelper` が吸収）。**0 と 2 はともにホーム**として読取り、書込はホーム=2 に正規化（GUI と同じ）。OneDrive 等（値 4）は3択外＝「変更しない」センチネル表示で no-op。Restore は管理外なら DeleteValue。

### Tests
- `LaunchToHelperTests`（+41・値↔index／0=ホーム／3択外センチネル／管理対象判定）。合計 **1029 PASS**。

---

## [0.3.13] - 2026-06-30（項目のチェックボックスを表示・シェル）

> 📝 いじくるツール代替の定番。タッチ操作や Ctrl なし複数選択に便利。HKCU・管理者不要・宣言的 Boolean。

### Added
- **項目のチェックボックスを表示**（シェル・🟢・管理者不要・エクスプローラ再起動で反映）：
  - エクスプローラの「項目チェック ボックス」（ホバーでチェックボックス表示→複数選択）を ON/OFF（`HKCU\…\Explorer\Advanced\AutoCheckSelect`・REG_DWORD・1=表示/0=非表示）。
  - 値 1=ON がユーザー視点と一致＝InvertDisplay 不要。秒表示/タスクバーボタン同型の宣言的 HKCU Boolean（新ロジック0）。

---

## [0.3.12] - 2026-06-30（隠しファイル・フォルダーを表示・シェル）

> 📝 いじくるツール代替の超定番。既存「ファイルの拡張子を表示」と対になるシェル項目。HKCU・管理者不要・現行 Windows 11（24H2/25H2）で有効。

### Added
- **隠しファイル・フォルダーを表示**（シェル・🟢・管理者不要・エクスプローラ再起動で反映）：
  - 隠し属性のファイル/フォルダーの表示を「表示する / 表示しない（既定）」で切り替える Command 型2択（`HKCU\…\Explorer\Advanced\Hidden`・REG_DWORD）。
  - **値が 0/1 でなく 1=表示 / 2=非表示** のため Boolean 型でなく Command 型で実装（新規 `HiddenFilesHelper` が value↔index 1/2↔0/1 を吸収）。
  - 未設定・1/2 以外（非数値/範囲外）は「変更しない・現在のまま」センチネル表示＝既定を誤表示せず何も書き換えない。「↩戻す」は管理外なら値削除で OS 既定へ。
  - システム保護ファイル（hiberfil.sys 等）は対象外＝別項目「システム保護ファイルを表示（上級者向け）」（`ShowSuperHidden`）。

### Tests
- `HiddenFilesHelperTests`（+36・value↔index 1/2↔0/1 マッピング／センチネル／管理対象判定の網羅）。合計 **988 PASS**。

---

## [0.3.11] - 2026-06-29（最大のプロセッサの状態・カーネル）

> 📝 候補項目集 v2 §1-B（`system.processorPowerState`・P6.7・AI バッチ時の温度/電源制御）。のんさんの 8700K 熱対策に直結。

### Added
- **最大のプロセッサの状態（CPU 上限クロック %）**（カーネル・🟡・要管理者・即時反映）：
  - CPU の最大クロック割合（100/90/80/70/60/50%）をプリセットで制限する Command 型（powercfg `PROCTHROTTLEMAX`）。下げると発熱・騒音・消費電力が下がる。
  - `processorBoost` と同型：`/getactivescheme` で実 GUID を解決し Read/Backup/Apply を同一 GUID に固定（TOCTOU 対策）。AC/DC 両方に書込・`/setactive` で即時反映。プリセット外/読取不可はセンチネル表示（誤丸め防止）。退避→復元対応。
  - 純粋ロジックは `ProcMaxStateHelper`（percent↔ラベル・xUnit）／GUID は `PowerCfgHelper.ProcThrottleMaxGuid`。

---

## [0.3.10] - 2026-06-29（タスクバーのボタン表示2種・デスクトップ）

### Added
- **タスクバーのボタン表示**（デスクトップ・UI・🟢・HKCU・管理者不要・いずれも `Explorer\Advanced` DWORD 1=表示/0=非表示）：
  - **タスクビュー**ボタン（`ShowTaskViewButton`）／**チャット**ボタン（`TaskbarMn`）の表示/非表示を個別に切替。タスクバーをすっきりさせる定番。
  - 値 1=ON がユーザー視点と一致＝`InvertDisplay` 不要。専用 service 不要＝汎用 HKCU Boolean 書込（バックアップ）フローを再利用。

### Notes
- 「ウィジェット」ボタン（`TaskbarDa`）は当初含めたが、Win11 24H2/25H2（build 26200）で値単位の ACL 保護により HKCU 書込が拒否される（自動レジストリ検証で実証）ため**非提供**。偽トグルを避ける方針。将来は HKLM ポリシー（要管理者）で別途検討。

---

## [0.3.9] - 2026-06-29（タスクバー時計に秒を表示・デスクトップ）

### Added
- **タスクバー時計に秒を表示**（デスクトップ・UI・🟢・HKCU・管理者不要）：
  - タスクバーの時計を hh:mm:ss 表示にする Boolean トグル（`HKCU\…\Explorer\Advanced\ShowSecondsInSystemClock`・1=表示/0=非表示）。Windows 11 build 22621.1776 以降が必要。
  - 値 1=ON がユーザー視点と一致＝`InvertDisplay` 不要。専用 service 不要＝汎用 HKCU Boolean 書込（バックアップ）フローを再利用。

---

## [0.3.8] - 2026-06-29（Game DVR ON/OFF・🕹️ゲーミング第2項目）

### Added
- **Game DVR（ゲームの録画機能）**（🕹️ゲーミング・🟢・HKCU・管理者不要）：
  - Xbox Game Bar の録画機能を一括オン/オフする Command 型2択。2つの HKCU 値を同時切替（`System\GameConfigStore\GameDVR_Enabled` ＋ `…\GameDVR\AppCaptureEnabled`・各 DWORD 1/0）。
  - Read は GameDVR_Enabled 基準（明示 0 のみ OFF）。Backup/Restore は両値を existed+値で退避。
  - @cross-reviewer 反映（致命2＋重要2）：F-1 Restore は片方失敗でも両値を必ず試行し集計 throw（2値の不整合防止）／F-2 削除は再読込で実値残存を確認（キー無し環境の偽 throw 防止）／M-2 非数値（REG_BINARY 等）は退避を「管理外」扱いに（`RegistryValueCoercion.TryCoerceInt` 新設＋xUnit）／M-1 説明文に片側乖離の回復手順を追記。

---

## [0.3.7] - 2026-06-29（ゲームモード ON/OFF・新🕹️ゲーミングカテゴリ）

### Added
- **ゲームモード（Game Mode）**（新🕹️ゲーミング カテゴリの第1項目・🟢・HKCU・管理者不要）：
  - ゲーム中にバックグラウンド動作を抑えてゲームを優先する機能のオン/オフ Boolean トグル（`HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled`・1=ON/0=OFF）。
  - 値 1=ON がユーザー視点と一致＝`InvertDisplay` 不要。専用 service 不要＝汎用 HKCU Boolean 書込（バックアップ）フローを再利用。

### Changed
- `CategoryType.Gaming` を enum 末尾追加（専用ペイン無し＝標準 DataGrid 表示・グラフィックとは別カテゴリ）。

---

## [0.3.6] - 2026-06-29（ストレージセンサー ON/OFF・システム）

### Added
- **ストレージセンサー（自動ディスク クリーンアップ）**（システム・🟢・HKCU・管理者不要）：
  - 一時ファイル/ごみ箱の自動クリーンアップをオン/オフする Boolean トグル（`HKCU\…\StorageSense\Parameters\StoragePolicy` 値名 `01`・1=ON/0=OFF）。
  - 値 1=ON がユーザー視点と一致＝`InvertDisplay` 不要。専用 service 不要＝汎用 HKCU Boolean 書込（バックアップ）フローを再利用。

---

## [0.3.5] - 2026-06-29（スナップアシスト ON/OFF・デスクトップ）

### Added
- **スナップ時の候補表示（スナップアシスト）**（デスクトップ・UI・🟢・HKCU・管理者不要）：
  - ウィンドウをスナップした時の候補ポップアップ表示をオン/オフする Boolean トグル（`HKCU\…\Explorer\Advanced\SnapAssist`・1=ON/0=OFF）。スナップ操作自体は無効化しない。
  - 値 1=ON がユーザー視点と一致＝`InvertDisplay` 不要。専用 service 不要＝汎用 HKCU Boolean 書込（バックアップ）フローを再利用（`desktop.darkModeApps` 同型）。

---

## [0.3.4] - 2026-06-29（Windows 起動音 ON/OFF・🔊サウンド第2項目）

### Added
- **Windows の起動音（起動時のサウンド）**（🔊サウンド・🟡・HLKM・要管理者・次回起動で反映）：
  - サインイン/起動時の「起動音」をオン/オフする Boolean トグル（`HKLM\…\Authentication\LogonUI\BootAnimation\DisableStartupSound`）。設定の『Windows スタートアップのサウンドを再生する』と同じ。
  - 値 0=オン/1=オフ を `InvertDisplay` でユーザー視点「鳴らす ON / 鳴らさない OFF」に。専用 service 不要＝汎用 HKLM Boolean 書込（バックアップ＋UAC 誘導）フローを再利用（`desktop.darkModeApps` 同型）。
  - 候補リストは HKCU と記載していたが実際は HKLM（要管理者）と裏取りで判明し修正。

---

## [0.3.3] - 2026-06-29（システム効果音 ON/OFF・新🔊サウンドカテゴリ）

> 📝 設計：[v0.3.3 設計ドラフト](docs/v0.3.3_システム効果音ONOFF_設計ドラフト.md)（@plan-validator 条件付きNO-GO→反映でGO・@cross-reviewer 反映）。

### Added
- **システム効果音 オン/オフ**（新🔊サウンド カテゴリの第1項目・🟢・HKCU・管理者不要）：
  - Windows のイベントサウンド（通知音・警告音など）をまとめてオン/オフする Command 型2択（`HKCU\AppEvents\Schemes`）。
  - **実際に消音される正しい実装（Scope B）**：トップの `(Default)` を `.None` にするだけでは鳴り続けるため、各イベントの `.Current\(Default)` を直接書き換える（OFF＝空文字／ON＝兄弟 `.Default` から標準音を復元）。
  - **REG_EXPAND_SZ（`%SystemRoot%` 入り）の型を退避・復元**して無音バグを防止。退避は値+型+有無（`existed`）を保持し「空文字の無音」と「値なし未設定」を厳密区別。
  - 退避は `BackupService` の maintenance ストア（FeatureKey=`systemsounds`）に first-touch-wins で1度だけ（GraphicsService 同型）。個別「↩ 戻す」でカスタム音を含む原状へ厳密復元。
  - `DeleteValue`/`WriteValue` のサイレント失敗は集計して throw（先頭3件をメッセージに表示）。退避後に消えたキー（WU 競合等）はスキップ。

### Changed
- `RegistryService` に `GetSubKeyNames`（サブキー名列挙）を追加（`GetValueNames` と対称の薄いラッパー）。
- `CategoryType.Sound` を enum 末尾追加（専用ペイン無し＝標準 DataGrid 表示）。

---

## [0.3.2] - 2026-06-22（タスクバー検索ボックス表示）

> 📝 設計：[v0.3.2 設計ドラフト](docs/v0.3.2_タスクバー検索ボックス_設計ドラフト.md)（@cross-reviewer 致命1＋重要3 反映）。

### Added
- **タスクバーの検索ボックスの表示**（デスクトップ・UI・🟢・HKCU・管理者不要）：
  - 「表示しない / 検索アイコンのみ / 検索ボックス（広い）/ 検索ボタン」の 4 択を Command 型プリセットで切替（`HKCU\…\CurrentVersion\Search\SearchboxTaskbarMode`）。「表示しない」でタスクバーを広く使える。
  - 現在値が未設定・範囲外・非数値（WinKnobs 管理外）の場合は「（変更しない・現在のまま）」と表示し、何も書き換えない。エクスプローラ再起動で反映（WinKnobs は自動再起動しない）。
  - 退避（first-touch 相当の JSON）＋「元に戻す」。**管理対象（0..3）のみ退避し、非管理値は復元時に WinKnobs の書込みを削除して OS 既定へ戻す**（誤って「非表示(0)」を書かない）。復元の削除失敗は無音にせず明示エラー。
  - 新規 `SearchBoxModeHelper`（純粋関数：4択・値↔ラベル・`TryGetManagedMode` 管理対象判定）＋ xUnit 42 件。

### Tests
- 838 → **880 PASS**（+42）。build 0 警告 0 エラー。

---

## [0.3.1] - 2026-06-22（単一インスタンス化）

> 📝 設計：[v0.3.1 設計ドラフト](docs/v0.3.1_単一インスタンス化_設計ドラフト.md)（@plan-validator 致命3＋重要4＋エッジ4／@cross-reviewer 致命1＋重要4＋軽微3 反映）。
> ⚠️ GUI 起動スモーク（実窓表示・2 個目の前面化目視）は在席で実施予定（コード層は build 0/0・xUnit 838 PASS）。

### Added
- **単一インスタンス化（二重起動防止・Mutex）**：
  - WinKnobs を 2 個目に起動すると、最初のウィンドウが前面化（最小化なら復元）し、2 個目は何も表示せず即終了する。同一セッション内の 2 プロセス併存（v0.3.0 検証中に観測）を解消し、Graphics/Network/Maintenance の **first-touch 退避（バックアップ）の単一インスタンス前提を保証**。
  - 名前付き Mutex は **存在ベース判定**（`createdNew`・所有権を取らないため AbandonedMutexException を回避・プライマリ異常終了でもハンドル全閉で OS が破棄→次回起動が自己修復）。スコープは `Local\`（ログオンセッション内）。2 個目→1 個目の通知は名前付き EventWaitHandle、終了は `Environment.Exit(0)`（ShutdownMode 非依存）。
  - 前面化は自プロセスの自窓を Activate（前景ロック制限の対象外）。子ダイアログが開いている時は Topmost ナッジを抑止（子窓が背後に沈む事例の回避）。
  - 「管理者として再起動」（runas）は単一化の例外＝コマンドライン引数 `--winknobs-restarted` ＋ 旧プロセスの Mutex 解放待ち（`waitForRelease`）で「再起動したのに窓が出ない」を防止。
  - 単一化の初期化に失敗してもアプリ起動は妨げない（フェイルオープン）。
  - 新規 `SingleInstanceNames`（純粋関数・名前生成）＋ `SingleInstanceManager`（IDisposable・WPF 非依存＝コールバック注入で headless テスト可）＋ xUnit 13 件（名前生成 7・プロセス内 OS 往復 6）。

### Tests
- 825 → **838 PASS**（+13）。build 0 警告 0 エラー。

---

## [0.3.0] - 2026-06-21（グラフィック設定 + 仮想メモリ）

> 📝 設計：[v0.3.0 設計ドラフト](docs/v0.3.0_設計ドラフト.md)（@plan-validator 致命3＋重要4＋エッジ5 反映・「🔧反映」節が本文より優先）。

### Added
- **グラフィック カテゴリ（🎮・専用ペイン `GraphicsListView`・HKCU・管理者不要）**：
  - **アプリ別GPU設定**：`HKCU\Software\Microsoft\DirectX\UserGpuPreferences` の各アプリ（値名＝exe フルパス）に GPU（自動 / 省電力＝内蔵 / 高パフォーマンス＝外付け）と Auto HDR を設定。`.exe` を選んで追加・🗑リセット（値削除）。SpecificAdapter 等の未知トークンは保持。変更は対象アプリの再起動で反映。
  - **全体トグル**：ウィンドウゲームの最適化（`SwapEffectUpgradeEnable`）・Auto HDR（`AutoHDREnable`）を `DirectXUserGlobalSettings` で。Auto HDR は HDR 非対応ディスプレイでは無効の注記つき。
  - 「↩ グラフィック設定をぜんぶ元に戻す」（first-touch 退避から復元）＋「🪟 Windows のグラフィック設定を開く」。
  - 新規 `GpuPreferenceHelper`（純粋関数：トークン パース/再構築/未知トークン保持・値名判定）＋ `GraphicsService`（first-touch 退避・DeleteOrThrow・大小無視）。
- **仮想メモリ（ページファイル）設定（🔴Red・要管理者・要再起動・ツールメニュー）**：
  - 🔴 専用ダイアログ `VirtualMemoryDialog` で「自動管理（推奨）/ システム管理 / 手動サイズ」を選択。理解チェック ON で初めて [適用]。
  - PowerShell/CIM（`Win32_ComputerSystem.AutomaticManagedPagefile`）＋レジストリ `PagingFiles`（MultiString・**明示1エントリ**で複数 pagefile 生成バグを構造的に封じ込め）を原子的に書込。
  - 現状を first-touch 退避し「仮想メモリ設定を元に戻す」で復帰。「Windows 純正の仮想メモリ設定を開く」（SystemPropertiesPerformance）安全弁。"無効化（なし）" プリセットは非提供。手動サイズは検証（0/0 拒否・下限 16MB・最大≥初期・上限 1TB）。
  - 新規 `VirtualMemoryHelper`（純粋関数：CIM JSON パース・検証・コマンド構築・分類・退避）＋ `VirtualMemoryService`（PowerShell `-EncodedCommand`・60 秒）＋ `VirtualMemoryViewModel`。共通 `IsSystemFeatureBusy` で再起動系メンテと直列化。

### Fixed（実機検証 2026-06-21 で発見・同日修正）
- **アプリ別GPU一覧への Windows 予約値（REG_DWORD）混入**：`GraphicsFeaturesNotificationConfig`（グラフィック通知設定・REG_DWORD）がアプリ別GPU設定の 1 行として列挙され、操作すると REG_DWORD が `GpuPreference=…;`（REG_SZ）で上書きされ破壊される問題。`GpuPreferenceHelper.IsAppValueName` の予約名除外に追加（`ReservedValueNames` 配列化）＋`RegistryService.GetValueKind` を新設し `GraphicsService` が列挙・退避・復元を **REG_SZ 値のみ**に限定（名前＋型の二重防御）。
- **「↩ ぜんぶ元に戻す」での型変換**：退避が `ReadValue<string>`（内部で `.ToString()`）で REG_DWORD を `"1"` 文字列化 →`WriteValue(String)` で REG_SZ 書き戻しし、予約値の型を DWORD→SZ に変えてしまう問題。退避・復元を REG_SZ 値のみとし、非SZ値は WinKnobs 管理外として温存（読まない・退避しない・復元時も触らない）。「完全復元」の保証範囲を REG_SZ（アプリ別GPU・全体トグル）に明示。
- **`RestoreAll` の `DeleteValue` サイレント失敗**（@plan-validator 指摘）：削除失敗を集計し、主要復元（書戻し・退避クリア）の完了後に件数を明示報告（throw）。

### Tests
- xUnit **825 PASS**（738 → 825・+87：`GpuPreferenceHelperTests` +40 / `VirtualMemoryHelperTests` +47）。build 0 警告 0 エラー。

### Notes
- ✅ **グラフィック実機検証完了（2026-06-21・ホスト Z370 / GTX1080+UHD630）**：GUI 起動スモーク（XamlParseException 無し・全 XAML/DI 解決）／グラフィックカテゴリ列挙（UWP 形式値名・空データ・予約 DWORD 混在の実機 20+ 件でクラッシュ無し）／全体トグル書込（未関与トークン保持・順序保持）／退避→復元（REG_SZ 全件ベースライン復帰・既存設定無傷・予約 DWORD 型保持・退避クリア）。
- 仮想メモリ（🔴・要管理者・要再起動）の実機トグルと、両機能の UIA 非破壊テスト（`tc_graphics_*`／`tc_virtualmemory_*`）は在席時に別途。※グラフィックのアプリ別行は AutomationId 未付与・「ぜんぶ戻す」は確認 MessageBox（Win32）で UIA からの自動操作が難しいため、UIA テスト整備時に AutomationId 付与とダイアログ処理が必要。

---

## [0.2.2] - 2026-06-20（NIC 省電力プリセット + 夜間モード色温度）

> 📝 設計：[v0.2.2 設計ドラフト](docs/v0.2.2_設計ドラフト.md)（@plan-validator 致命3＋重要2＋エッジ4 反映・「🔧反映」節が本文より優先）。
> 実機検証で発覚した NIC 省電力固有バグの詳細：[NIC 省電力 実機バグ診断 2026-06-20](docs/v0.2.2_NIC省電力_実機バグ診断_2026-06-20.md)。

### Added
- **NIC 省電力プリセット（安定重視）**（Network ペイン「くわしい設定」・🟡・要管理者・即時反映で数秒断あり）：選択した物理 NIC の EEE 系（`*EEE`/`EeePhyEnable`/`EEELinkAdvertisement`/`ULPMode`/`GreenEthernet`/`PowerSavingMode` 等・8 種固定リスト）を 0 に・`AllowComputerToTurnOffDevice` を Disabled に。ラグや突然の切断対策。GUID 単位 first-touch 退避＋「↩ 元に戻す」で原状復帰。新規 `NicPowerHelper`（純粋関数・PS 5.1 単一要素展開吸収＋ICollection 特殊形式 `{value:[...],Count:N}` 吸収＋CLIXML 進捗連結対策のブレース・バランス JSON 抽出）＋ `NicPowerService`（PowerShell `-EncodedCommand` 経由・120 秒・GUID 単位退避）。リモート（RDP）経由時は破壊的確認（既定キャンセル）で締め出し防止。
- **夜間モード色温度**（Desktop カテゴリ・`desktop.nightLightStrength`・🟡・昇格不要）：実機 blob 解析で確定したフォーマット（マーカー `CF 28` 直後 2 バイト・式 `high=K/64`/`low=(K%64)*2+128`）の **2 バイトだけ差替**＋自己検証で再パース一致を確認＋blob 丸ごと Base64 退避。Apply は失敗で即中止＋ms-settings:nightlight 誘導（書込中止＝壊さない）。反映には夜間モード OFF→ON か再ログオンが必要（CloudStore はライブ監視されない）。新規 `NightLightHelper`（マーカー一意性検査＋範囲外弾き）＋ `RegistryService.ReadBinary`。

### Fixed
- **`NetworkService.GetActivePhysicalAdapters` の列挙ソース不一致**（実機検証発覚）：.NET `NetworkInterface` の Description ヒューリスティックのみだと LDPlayer 等の擬似 NIC を拾い、Read/Apply（`Get-NetAdapter -Physical` 基準）と GUID 集合が一致せず「現在設定を読み取れない」失敗が発生していた。レジストリ `Class\{4d36e972...}\Characteristics` の `NCF_PHYSICAL(0x4)` 集合で突合して幻アダプタを構造的に排除（PS 不使用・取得不能時は従来ヒューリスティックにフォールバック）。**DNS 機能（同関数を共用）も恩恵**。
- **`NicPowerHelper.TryParseSnapshot` の堅牢化**（実機検証発覚）：(a) PS 5.1 の ICollection 特殊シリアライズ `{value:[...],Count:N}` を `value` 配列として読む・(b) 昇格セッション特有の CLIXML 進捗（中括弧含む）が stderr に連結されるケースで本物 JSON 境界をブレース・バランスで正しく特定。
- **`Set-NetAdapterPowerManagement -AllowComputerToTurnOffDevice` PSコマンド構文誤り**（実機検証発覚）：Microsoft 仕様では当該パラメーターは存在しない。`Get-NetAdapterPowerManagement` の戻り値オブジェクトでプロパティ書換→パイプ `Set-NetAdapterPowerManagement` に修正。
- **`-EncodedCommand` 経由の進捗ノイズ**：`$ProgressPreference='SilentlyContinue'` を BuildReadCommand/BuildApplyCommand/BuildRestoreCommand 冒頭に追加し、昇格セッションでの CLIXML 進捗出力を抑止。

### Tests
- xUnit 735→738（+3：CLIXML 連結耐性・ICollection 特殊形式・stderr 警告混入耐性／既存 PM コマンド期待値の Get→pipe→Set 仕様への更新）。

### Known limitations
- マルチモニタ/HDR 環境の夜間モード blob はサンプル不足のためマーカー一意性の保証が未実証（単一構成では検証済み・複数マーカー時は Apply 自動中止＝安全側）。
- NIC 実トグルは VM の仮想 NIC では検証不可（省電力プロパティ無し）。本リリースは Z370 Intel I219-V ホスト実機で Apply→Restore 1/1 ラウンドトリップ確認済み（NIC 実値 Enabled→Disabled→Enabled 完全一致・ネット瞬断は数秒以内）。

---

## [0.2.1] - 2026-06-11（LDPlayer ユーザー獲得スプリント：6 項目）

> 📝 設計：[v0.2.1 設計ドラフト](docs/v0.2.1_設計ドラフト.md)（@plan-validator 12 点反映済・「🔧反映」節が本文より優先）。
> 夜間モード色温度（最難関・CloudStore バイナリ）は **v0.2.2 へ送り**（安全設計は v0.2.0 ドラフト §3 完成済）。

### Added
- **マウス加速 OFF**（`gaming.mouseAcceleration`・KeyboardMouse・🟢・昇格不要・即時反映）：`HKCU\Control Panel\Mouse` の 3 値セット（`MouseSpeed`/`MouseThreshold1`/`MouseThreshold2`）を「既定（加速あり）／OFF（1:1・ゲーマー向け）」で切替。`SystemParametersInfo(SPI_SETMOUSE)` P/Invoke で即時反映＋失敗時はレジストリのみ＋再ログオン案内（フェイルソフト）。新規 `MouseAccelHelper`（SPI 配列並び `{T1,T2,Speed}` の変換を純粋関数化・plan-validator 重要3）＋ `MouseParamsService`。
- **Network Throttling Index**（`network.throttlingIndex`・Network ペイン「くわしい設定」・🟡・要管理者・再起動反映）：「既定（10）／無効（0xffffffff）」の 2 択・中立説明（配信/DAW 併用時は既定が安定する場合あり）。既定側は値削除でなく 0xa を書く（OS 既定は値が存在するため・Power Throttling と逆）。
- **プロセッサー Boost モード**（`system.processorBoost`・Kernel・🟡・要管理者・即時反映）：PERFBOOSTMODE を「無効／有効／アグレッシブ（既定）」の 3 択で AC/DC 両方に書込（4/5=効率的系はハイブリッド CPU 専用のため出さない・plan-validator 重要7）。**実 GUID 固定**（`scheme_current` 文字列は TOCTOU のため不使用・致命1）＋ロケール非依存パース（新規 `PowerCfgHelper`）。
- **🛠 WSL ON/OFF**（ツールメニュー・🟡・要管理者・再起動反映・`MenuEnableWsl`/`MenuDisableWsl`）：ON＝WSL 本体＋VirtualMachinePlatform の 2 機能を C# done-stack（片方失敗で逆順ロールバック・両失敗は **`PartiallyEnabled`** を正直に報告）。OFF＝WSL 本体のみ（VMP は WSA 等の巻き添え防止で残す）。タイムアウト 300 秒（DISM 系）。新規 `WslHelper`/`WslService`。
- **🛠 コア分離（メモリ整合性・HVCI）ON/OFF**（ツールメニュー・🔴・要管理者・再起動反映・`MenuEnableCoreIsolation`/`MenuDisableCoreIsolation`）：HVCI `Enabled` の値単位書込（`WasEnabledBy` 等同居値は不触）。OFF＝ConfirmDestructive（既定いいえ）。GPO/MDM 管理下はグレーアウト。UEFI ロック環境を考慮し成功表示は「設定しました＋再起動後に Windows セキュリティで確認」止まり。新規 `CoreIsolationHelper`/`CoreIsolationService`。
- **🛠 Hyper-V（ハイパーバイザー起動）ON/OFF**（ツールメニュー・🔴・要管理者・再起動反映・`MenuEnableHyperV`/`MenuDisableHyperV`/`MenuRestoreHyperV`＋常設状態ラベル `MenuHyperVStatus`）：`bcdedit /set hypervisorlaunchtype off|auto` 一本（DISM 機能削除は VM 構成消失リスクで不採用＝完全可逆）。OFF＝**専用 Red 強確認ダイアログ**（`HyperVDisableDialog`・連鎖影響列挙＋**コア分離 ON 検知時の依存ガイド**＋**BitLocker 回復キー警告の無条件常時表示**＝plan-validator 致命2）。適用前 first-touch 退避（maintenance ストア）＋「元に戻す」。新規 `HyperVHelper`/`HyperVService`。

### Changed
- 再起動系メンテ機能（WSL/コア分離/Hyper-V）を**共通 `IsSystemFeatureBusy` で直列化**（DISM グローバル mutex 衝突防止・plan-validator エッジ3）。
- `ExternalLauncherService.RunExe`：stderr 非同期読み＋60 秒タイムアウト＋`Kill(entireProcessTree)`（パイプデッドロック・ハング経路を閉鎖・cross-reviewer I-2）。
- `ElevationClassifier.IsRunningAsAdministrator()` 新設：読取が非昇格で通る系（bcdedit/DISM）の書込前に早期 AccessDenied（cross-reviewer I-4）。

### Fixed
- **Boost モードの powercfg クエリを `/query` → `/qh`（隠し設定込み）に修正**：PERFBOOSTMODE は既定で hidden 属性のため `/query` では設定ブロックが出力されず、Read=常に既定表示・Backup=常に失敗→Apply 常に中止（機能が事実上死んでいた）。VM 実機検証（2026-06-11）で発見・ホストでも再現確認・/qh で AC/DC 読取/書込/復元の往復を VM 実証。
- Hyper-V 退避トークンを Auto/Off ホワイトリスト化＋想定外値（Other）検出時は変更自体を中止（「保存したら必ず戻せる」原子性・cross-reviewer C-1 致命）。
- Boost の Backup→Apply で電源プラン GUID を使い回し（プラン切替 TOCTOU・cross-reviewer I-1）。
- `DeleteOrThrow`（Power Throttling / Network Throttling 復元）：「値の不在を正の確認で示せた時だけ成功」に強化（読取例外の null 化によるサイレント成功を閉鎖・cross-reviewer I-3）。

---

## [0.2.0] - 2026-06-10（Quick Win：Power Throttling ＋ Sysmain サービス）

> 📝 **スコープ訂正**：候補集 v2 の「Quick Win 4 項目」のうち**透明効果とクリップボード履歴は実装済だった**
> （2026-05-25 に `desktop.transparencyEffects` / `system.clipboardHistory` として追加済・二重計上を訂正）。
> 夜間モード強度・色温度は非公式フォーマット（CloudStore バイナリ）のリスクを考慮し **v0.2.1 へ送り**。
> 設計：[v0.2.0 Quick Win 設計ドラフト](docs/v0.2.0_QuickWin_設計ドラフト.md)（@plan-validator 済・致命2件反映）。

### Added
- **Power Throttling 無効化**（`kernel.powerThrottlingOff`・Kernel カテゴリ・⚠️Yellow・要管理者・再起動反映）：バックグラウンドアプリの CPU クロック抑制（省電力機能）を「既定／無効化」の 2 択で切替。`HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling` の `PowerThrottlingOff`（DWORD）。
  - **Command 型＋値単位復元を採用**（@plan-validator 致命指摘）：Boolean 型だと復元が「.reg キー削除マーカー＝キーごと削除」になり、`PowerThrottling` キーに同居する他の値を巻き添えにするため、`StringBackup`（ValueExisted）＋`DeleteValue` で**値だけ**書き戻す／消す方式に。「既定」選択時は 0 を書かず値を削除（原状＝値なしに一致）。
- **🛠 Sysmain サービス 有効/無効**（ツールメニュー・要管理者・AutomationId `MenuEnableSysmain` / `MenuDisableSysmain`）：先読み（旧 Superfetch）サービスを SCM 経由（PowerShell `Set-Service` / `Stop-Service` / `Start-Service`）で切替。レジストリ直叩き（`Start=4`）は実状態と乖離するため不採用。
  - 新規 `SysmainHelper`（純粋関数・xUnit）＋ `SysmainService`（MemoryCompressionService 同型・60秒タイムアウト・`Kill(entireProcessTree)`）。
  - **第4分類 `ServiceNotFound`**：SysMain が存在しないエディション/構成を AccessDenied と誤分類せず「サービスなし」と案内（権限不足判定より先に評価＝誤分類防止）。
  - タイムアウト時は powershell プロセスのみ Kill し、サービスの強制停止はしない（中途半端な状態を残さない・「再起動で確定」案内）。
  - 有効化＝種別 Automatic→Start／無効化＝Stop→Disabled の順序固定。可逆操作＝通常 Confirm（説明は中立：HDD では軽くなる報告・SSD は既定のまま無難）。

### Fixed
- **HKLM 値削除のサイレント失敗防止**（@cross-reviewer 致命1）：`RegistryService.DeleteValue` は例外を握りつぶして false を返すため、Power Throttling「既定」適用/復元が非昇格時に昇格ダイアログ経路（`HandleElevationNeeded` の `UnauthorizedAccessException` catch）をすり抜けて「戻したつもりが何も変わっていない」になる穴を、削除失敗かつ値残存時に明示 throw する `DeleteOrThrow` で閉じた（app.manifest は asInvoker＝標準権限起動あり）。
- Sysmain タイムアウト時メッセージに現状確認導線（services.msc）を追加（@cross-reviewer 重要1）。

### Notes
- 新規 `SysmainHelperTests`。バージョン bump 0.1.20 → 0.2.0（csproj 3 行＋UI 2 箇所）。
- Sysmain 状態ラベル（メニュー常設）は今回見送り＝fire-and-forget リフレッシュのレース複雑性を避けスコープ維持（必要なら v0.1.20 の世代カウンタパターンで追加可）。

---

## [0.1.20] - 2026-06-09（リモートデスクトップ：状態表示／Home・GPO グレーアウト常設ラベル）

> ℹ️ もとは「v0.1.18.1」として計画した積み残し（v0.1.18 で先送りした状態表示／グレーアウト）。
> v0.1.19 / v0.1.19.1 が先に出荷済みのため、版番号を後退させず **v0.1.20** として前進リリース（機能内容は計画どおり）。

### Added
- **リモートデスクトップ「現在の状態」常設ラベル**：ツールメニューに読み取り専用の状態表示を追加（AutomationId `MenuRemoteDesktopStatus`）。`有効/無効/一部だけ有効（要注意）` ＋ `NLA：必須/不要/不明` ＋ 待ち受けポートを常時表示。起動時＋各 RDP 操作（有効化／無効化／元に戻す）完了時に自動更新。状態読み取り（`ReadStatus`・FW 読取で powershell 起動）は別スレッドで行い UI をブロックしない。

### Changed
- **Home／GPO 管理時は「無効化」もグレーアウト**：従来 v0.1.18 では「無効化」を Home/非対応エディションや GPO/MDM 管理下でも押せて「安全 no-op＋説明ダイアログ」を出していたが、状態が常時見える今は「有効化」と同条件でグレーアウトし、理由は常設ラベル（「非対応（Pro 以上が必要）」「ポリシー管理中（GPO/MDM）」）で提示する一貫 UX に変更。`DisableRemoteDesktop` 内の Home/GPO 分岐は defense-in-depth として残置（CanExecute をすり抜けても安全側）。
- 「無効化」メニューにグレーアウト理由の ToolTip を追加（「有効化」と同文）。

### Notes
- 純粋関数 `RemoteDesktopHelper.DescribeMenuStatusLabel`（状態ラベル文言）を新設し xUnit +6（**536 PASS / 0 FAIL**＝回帰なし）。`dotnet build` 0 警告 0 エラー。
- 攻撃面・実行ロジックは不変（表示とグレーアウトのみ）。SECURITY.md の脅威面に変更なし。

---

## [0.1.19.1] - 2026-06-09（RestorePointService 同型ハードニング・パッチ）

### Fixed
- **「復元ポイントを作成」のプロセスリーク防止**：`RestorePointService.Create` のタイムアウト（60秒）時に `powershell.exe` を `Kill(entireProcessTree: true)`（best-effort）で確実に終了。`using` の Dispose ではプロセスは Kill されないため、タイムアウト時に powershell が残存するリークを修正（v0.1.16 で `SystemProtectionService` に施した対策の横展開）。
- **「復元ポイントを作成」の UI フリーズ解消＋二重起動防止**：`MainViewModel.CreateRestorePoint` を同期コマンドから `async Task` ＋ `await Task.Run` に変更し、`Checkpoint-Computer`（最大60秒）実行中の UI フリーズを解消。`IsCreatingRestorePoint` Busy ガード（`CanExecute`）で実行中は再実行を封じ、PowerShell の二重起動を防止。失敗時に詳細が空でも「（詳細不明）」とフォールバック表示。`ToggleSystemProtection` / `ToggleMemoryCompression` と完全同型。

### Notes
- xUnit **530 PASS / 0 FAIL**（v0.1.19 と同数＝回帰なし）。`dotnet build -c Release` 0 警告 0 エラー。@cross-reviewer 致命 0。
- 既知の限界（既存同型と同じ・実害低）：`RedirectStandardOutput=true` のため、タイムアウト前に stdout/stderr を読まない（小出力前提）。`Create(string)` bool 旧 API は現在未使用（将来削除候補）。

---

## [0.1.19] - 2026-06-09（リモートデスクトップ**有効化 ON**・🔴最終弾／**VM 接続往復検証 PASS／@cross-reviewer 致命3件→修正完了**）

### Added
- **🛠 システム・メンテナンス Phase 4「リモートデスクトップ 有効化 (ON)」（🔴Red・段階リリース第2弾＝最終弾）**：ツールメニューに「リモートデスクトップを有効にする (要管理者・🔴)」「リモートデスクトップを元に戻す (要管理者)」を追加（AutomationId `MenuEnableRemoteDesktop` / `MenuRestoreRemoteDesktop`）。ON は **3389 ポートを開けて外部ログイン受付を増やす唯一の操作**＝WinKnobs 最高リスク機能。
  - **🔴 Red 専用 WPF 確認ダイアログ**（`Views/RemoteDesktopEnableDialog` ＋ `ViewModels/RemoteDesktopEnableViewModel`・AutomationId `RemoteDesktopEnableDialog`）：チェックボックス「リスクを理解しました」(`CheckUnderstandRisk`) を ON にして初めて [有効化](`BtnEnable`) が活性化（明示同意ジェスチャ・Enter 連打/誤クリック封じ・`IsDefault` 無し）。既定フォーカス＝[キャンセル](`BtnCancel`)。現在状態（ON/OFF・NLA・ポート・自分がリモートか・LAN IP・ユーザー名）と推奨対策・戻し方を提示。リモート接続中／GPO 管理中は警告を表示。
  - **原子性は C# 側 done-stack**（`RemoteDesktopService.Enable`）：適用順＝内→外（NLA=1 → fDeny=0 → FW-TCP Enable → FW-UDP best-effort）。途中失敗は成功分を逆順ロールバック。結果は4分岐 `Success / AccessDenied / Failed / **LeftExposed**`（巻き戻し後も到達可能＝攻撃口が開いたまま＝🔴強警告）。FW は TCP 必須・UDP は best-effort。
  - **状態読み取り** `ReadStatus`（非昇格可・各項目独立 try・NLA 不能環境は「不明」許容）。**first-touch 退避**を network ストアと別系統の `backup\maintenance\originals.json` に保存（`BackupService` に Maintenance 専用3メソッド・`NetworkService.RevertAll` の道連れを構造で遮断）＋専用「元に戻す」メニュー（退避時のみ活性）。
  - 新規純粋関数（`RemoteDesktopHelper`）：`BuildSetDwordCommand` / `BuildRemoveValueCommand` / `BuildFirewallCommand` / `IsRdpEnabled` / `DescribeRdpState` / `TryParseBackup`。xUnit **520 → 530 PASS**。

### Fixed（@cross-reviewer 致命3件・同日修正）
- **S-1/M-1 ロールバックの原子性の穴**：forward は PowerShell（ExitCode で失敗検知）なのに rollback は C# Registry API（`DeleteValue` が失敗を握って false を返し throw しない）＝非対称で「fDeny=0 を消せなかったのに巻き戻し済みと誤報告」しうる穴を修正。ロールバックの fDeny/NLA 書き込みを forward と同じ PowerShell 経路に統一（`BuildRemoveValueCommand`＝削除→残存なら throw）。事後検証の露出判定を「fDeny=0 単独残存」でも LeftExposed 警告に拡張（FW 別管理環境の fail-safe）。
- **E-1 退避 JSON 部分破損**：`JsonSerializer` が `{}` やフィールド欠落を全 default の record にして「正常」と誤認→意図せぬ無効化、を `TryParseBackup`（nullable DTO で全フィールド存在検査・破損は null→安全側）で修正。
- **E-2 事後検証不能時**：fDeny=0 書込後＆巻き戻し失敗の可能性があれば安全側で LeftExposed を返す（露出見逃しより過警告）。
- **S-2**：UDP 成功分を rollback に積まない理由（最終ステップ）をコメント明記＋将来ステップ追加時の指示。

### Changed
- バージョン bump：v0.1.18 → v0.1.19（csproj 3箇所 / MainWindow ステータスバー / MainViewModel バージョン情報）。

### Notes
- ✅ **VM 接続往復検証 完了（2026-06-09）**：Hyper-V `WinKnobs-Test`（Default Switch）で Enable→**ホスト→VM:3389 到達 OK**→Disable→**到達不可** を実 TCP 到達性で実証。done-stack 部分失敗ロールバックで fDeny 復帰・退避 JSON 厳密パース（`{}`破損検知）も実機確認。全7項目 PASS・baseline 復帰・CP 削除済（[テスト結果](docs/テスト結果_2026-06-09_v0.1.19_VM接続往復_Enable.md)・スクリプト `scripts/_vm_remotedesktop_enable_verify.ps1`）。
- ✅ **@cross-reviewer 完了（2026-06-09）**：🔴最高リスク機能を5観点＋RunPowerShell 重点でレビュー→致命3件（S-1/M-1/E-1）検出→同日修正→530 PASS（[レビュー結果](docs/レビュー結果_2026-06-09_v0.1.19_Step6_cross-reviewer.md)）。監視のみ（P-1 PS 起動回数 / R-1 WaitForExit 順 / U-1 ポリシー時 Restore）は据え置き（実害小・設計上正しい）。
- ✅ **起動スモーク OK**：メニュー3項目正常配線・「元に戻す」は退避無しでグレーアウト（CanExecute 正常）。
- ✅ **UIA 非破壊テスト追加**：`tc_maintenance_remotedesktop_enable`（Red ダイアログ出現＋チェック未投入で[有効化]非活性＋既定キャンセル＋fDeny 不変・非対応/GPO 時はメニュー非活性 INFO 降格）。ランナー登録済（VM 実機実行は v0.1.19 ビルド再配備後）。
- **plan-validator 反映（設計ドラフト）**：done-stack 方式・退避ストア分離・ShowDialog UI スレッド・NLA 不能許容・エディション判定を設計に織込済。

---

## [0.1.18] - 2026-06-06（リモートデスクトップ無効化・**VM 実機検証 PASS／@cross-reviewer 致命0 完了**）

### Added
- **🛠 システム・メンテナンス Phase 4「リモートデスクトップ ON/OFF」（🔴Red・段階リリース第1弾＝無効化のみ）**：ツールメニューに「リモートデスクトップを無効にする (要管理者・🔴)」を追加（メモリ圧縮の隣・AutomationId `MenuDisableRemoteDesktop`）。WinKnobs 初の 🔴Red トグル。**非対称設計＝v0.1.18 は無効化(OFF)のみ**（攻撃面を閉じる安全方向）。有効化(ON)は v0.1.19（強警告UI＋接続往復VM検証込み）。
  - 新規 `Services/RemoteDesktopHelper.cs`（純粋関数 `BuildDisableCommand` / `Classify`＝ElevationClassifier 再利用 / `IsRdpHostCapableEdition` ホワイトリスト ＋ `RemoteDesktopOutcome` enum）＋ `Services/RemoteDesktopService.cs`（MemoryCompressionService 同型・60秒タイムアウト・別スレッド・`IsCurrentSessionRemote`/`IsPolicyManaged`/`IsHostCapableEdition` の3判定）
  - MainViewModel：`DisableRemoteDesktopCommand`＋`IsRemoteDesktopBusy`＋`DisableRemoteDesktop`。①Home 等ホスト非対応エディションは安全 no-op＋説明 ②GPO/MDM 管理中は変更不可案内 ③**自己セッション判定（リモート接続中なら「⚠️あなたは今リモート接続中です」強警告・既定いいえ）** ④通常時も `ConfirmDestructive`（既定いいえ）。App.xaml.cs に DI 登録
  - 新規 `RemoteDesktopHelperTests`（+31）→ build 0/0・**xUnit 469 PASS**。起動スモーク（DI解決・クラッシュなし）OK

### Changed
- **実機照合で設計を修正（netsh → PowerShell）**：当初設計は FW を netsh `name=<安定ID>` で言語非依存に叩く想定だったが、**実機で netsh の `name=`/`group=` はローカライズされた表示名を取る**（安定IDでヒットしない）と判明。→ FW は PowerShell `Disable-NetFirewallRule -Name <安定ID>`（言語非依存）＋ fDeny を1つの `powershell.exe -Command` で実行（Phase 2/3 同型）。結果 ExitCode で分類でき `Classify(exitCode, output)` に統一（設計が避けようとした例外分類は不要に＝むしろ単純化）。順序は FW→fDeny の外→内・fail-closed
- バージョン bump：v0.1.17 → v0.1.18（csproj 3箇所）。**併せて v0.1.17 で更新漏れだった UI 版表示2箇所**（MainWindow ステータスバー・MainViewModel バージョン情報＝いずれも v0.1.16 のままだった）を v0.1.18 に修正

### Notes
- スコープ判断（のんさん承認 1-①/2-Ⓐ）：v0.1.18 は「無効化＋強確認」のみ（メモリ圧縮と同型の軽スコープ）。状態表示/Home グレーアウトは v0.1.18.1 へ。**退避/RevertAll は持たない**（OFF は安全方向・Windows 設定から可逆＝メモリ圧縮同様。退避は ON を足す v0.1.19 で導入）
- ✅ **VM 実機検証 完了（2026-06-06）**：Hyper-V `WinKnobs-Test` で実 Disable→`fDenyTSConnections` 0→1・FW 規則 True→False を実測。no-op も ExitCode=0（冪等）。全経路 PASS・baseline 復帰・CP 削除済（[テスト結果](docs/テスト結果_2026-06-06_v0.1.18_VMリモートデスクトップ.md)・スクリプト `scripts/_vm_remotedesktop_verify.ps1`）
- ✅ **@cross-reviewer 完了（2026-06-06）**：🔴最高リスク機能を5観点でレビュー → **致命0**。重要3点（GPO×FW別管理の限界／出力読み順デッドロック潜在／fail-closed 中途半端状態）はいずれもロジック修正不要（文言・コメント・実機実証で対応）。軽微1（Failed 文言）反映済
- ✅ **UIA 非破壊テスト追加**：`tc_maintenance_remotedesktop`（確認ダイアログ出現＋既定いいえ＋いいえで非実行＋fDeny 不変・🔴締め出し防止）。ランナー登録済
- 軽微 fix：Failed 時の文言を「FW は閉じた前提」→「一部または全部 適用されていない可能性」に緩和（cross-reviewer 軽微④）。`IsPolicyManaged` に GPO×FW 別管理の既知の限界コメントを追記（重要①）

---

## [0.1.17] - 2026-06-05

### Added
- **🛠 システム・メンテナンス Phase 3「メモリ圧縮 ON/OFF」**：ツールメニューに「メモリ圧縮を有効/無効にする (要管理者)」2項目を追加（システムの保護の隣・AutomationId 付）。PowerShell `Enable-MMAgent -mc` / `Disable-MMAgent -mc` を呼ぶ（要管理者）。`docs/v0.2_候補項目集` の Quick Win から前倒し実装（二重計上回避のため v0.2.0 リストから削除）
  - 新規 `Services/MemoryCompressionHelper.cs`（純粋関数 `BuildCommand` / `Classify` ＋ `MemoryCompressionOutcome` enum）＋ `Services/MemoryCompressionService.cs`（SystemProtectionService 同型・60秒タイムアウト・別スレッド実行・プロセスリーク防止）
  - メモリ圧縮は有効化/無効化とも**可逆（再トグルで戻せる・データ消失なし）**＝両方とも通常 `Confirm`（ConfirmDestructive 不要）。説明は中立（一律推奨しない）
  - MainViewModel：`Enable/DisableMemoryCompressionCommand` ＋ `IsMemoryCompressionBusy`（実行中は二重起動を封じる）＋ AccessDenied は「管理者で実行し直して」案内。App.xaml.cs に DI 登録
  - xUnit：`MemoryCompressionHelperTests`（BuildCommand / Classify ・SystemProtection 同型）

### Changed
- **♻️ 権限不足判定を `ElevationClassifier` に一元化**（Rule of Three 解消・@cross-reviewer 監視指摘の対応）：`SystemProtectionHelper` と `MemoryCompressionHelper` に同一の権限不足キーワード集（7種）がコピー存在していたのを `Services/ElevationClassifier.LooksLikeAccessDenied` に集約。両 `Classify` は委譲に変更（enum マッピングは各自保持）。「片方だけキーワード追加して齟齬が出る」リスクを解消。`RestorePointService` は独自の狭い判定（挙動互換）のため対象外。`ElevationClassifierTests`（+16）→ **438 PASS**

### Notes
- バージョン bump：v0.1.16 → v0.1.17（csproj 3箇所 / MainWindow / MainViewModel）
- @cross-reviewer 多角点検（セキュリティ/perf/UX/保守性/エッジ）：**致命・重要ゼロ**。ConfirmDestructive 不要判断（可逆性で分岐）・`-mc` スイッチ・冪等方式いずれも妥当と確認
- ✅ **VM 実機検証 完了**（2026-06-05・[docs/テスト結果_2026-06-05_v0.1.17_VMメモリ圧縮.md](docs/テスト結果_2026-06-05_v0.1.17_VMメモリ圧縮.md)）：VM `WinKnobs-Test` で実トグル 全4経路 PASS（無効化 ON→OFF / no-op / 有効化 OFF→ON / no-op、いずれも ExitCode=0）。`Get-MMAgent.MemoryCompression` の True↔False を実測。no-op も ExitCode=0＝Classify が実挙動と一致＝**コード修正不要**
- 残：Phase 4 リモートデスクトップ ON/OFF（🔴最高リスク・最後）

---

## [0.1.16] - 2026-06-05

### Fixed
- **🔒 システムの保護「無効化」の誤爆防止**（cross-reviewer 重要①）：無効化の確認を `Confirm`（既定＝はい）から `ConfirmDestructive`（既定＝いいえ・⚠アイコン）へ。Enter 連打等の誤操作で既存の復元ポイントが全削除される事故を防ぐ。設計ドラフト v0.1.14 の「ConfirmDestructive 必須」要件の実装漏れを是正（有効化は非破壊のため `Confirm` のまま）
- **⏳ UI フリーズ＋二重起動の防止**（cross-reviewer 重要②）：`SetProtection`（最大60秒ブロック）を `await Task.Run(...)` で別スレッド化し UI スレッドの「応答なし」を解消。`IsSystemProtectionBusy` ＋ `CanExecute` で実行中は Enable/Disable を封じ、PowerShell の二重起動を防止。処理中は「⏳ …化しています…」を表示
- **🧹 タイムアウト時のプロセスリーク防止**（cross-reviewer 軽微③）：`WaitForExit(60s)` 失敗時に `p.Kill(entireProcessTree: true)` で powershell.exe を確実終了（best-effort）
- **🈳 失敗ダイアログのフォールバック**（cross-reviewer 軽微④）：失敗時に出力が空でも「失敗しました：」だけにならないよう「（詳細不明）」を表示
- **📝 ドキュメント修正**（cross-reviewer 軽微⑤）：[0.1.15] のメニュー記載「ファイル」→正しくは「ツール」

### Notes
- バージョン bump：v0.1.15 → v0.1.16（csproj 3箇所 / MainWindow / MainViewModel）
- v0.1.15 の @cross-reviewer 再点検（観点別5並列＋敵対的検証・26指摘）の反映。**致命ゼロを確認**（前回「日本語 stderr 文字化け」は今回も誤検知と再確定）。重要2件＋軽微3件を本リリースで修正
- 監視のみ（今回スコープ外・記録）：Classify "administrator" 過広一致（exitCode!=0 前提で実害低）／ no-op 時 ExitCode（VM 管理者検証で確認）／ Contains 重複（Rule of Three 未達）／ `RestorePointService` も同型のタイムアウトリーク・同期ブロック（横展開候補）

---

## [0.1.15] - 2026-06-04

### Added
- **🛠 システム・メンテナンス「システムの保護（システムの復元）ON/OFF」**（Phase 2・メニューコマンド）
  - 「ツール」メニューの「復元ポイントを作成」の隣に「システムの保護を有効にする / 無効にする (要管理者)」を追加。既存 `RestorePointService` と同じ作法（PowerShell・結果ダイアログ）を再利用
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
