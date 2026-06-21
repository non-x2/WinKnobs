# Security Policy / セキュリティポリシー

## 🛡️ サポート対象バージョン / Supported Versions

| バージョン / Version | サポート状態 / Support |
|---|---|
| 0.3.x (最新 dev) | ✅ アクティブ開発中 |
| 0.2.x / 0.1.x | ⚠️ セキュリティ修正のみ |
| < 0.1.0 | ❌ サポート対象外 |

リリースされた安定版（v1.0.0 以降）が出るまでは、最新 dev 版のみセキュリティ修正対象です。

Until a stable release (v1.0.0+) is published, only the latest dev version receives security fixes.

---

## 🚨 脆弱性の報告 / Reporting Vulnerabilities

### ❌ してはいけないこと / Do NOT

- **公開 Issue で脆弱性を報告する**
- **Pull Request に脆弱性の詳細を含める**
- **SNS・ブログ等で脆弱性詳細を公開する**（修正版リリース前）

### ✅ するべきこと / What to Do

1. **GitHub Security Advisory（プライベート）から報告**
   - [https://github.com/non-x2/WinKnobs/security/advisories/new](https://github.com/non-x2/WinKnobs/security/advisories/new)
   - GitHub アカウント保有者であれば誰でも報告可能
   - 報告内容は管理者にのみ通知される

2. **報告に含めてほしい内容**
   - 脆弱性の概要（PoC を含めない場合は概念的説明で OK）
   - 影響範囲（バージョン・OS・実行条件）
   - 再現手順（可能なら）
   - 想定される被害
   - 報告者の連絡先（任意）

---

## ⏱️ 対応 SLA / Response Timeline

このプロジェクトは個人開発のため、企業 OSS のような厳密な SLA はありません。
ただし、以下の努力目標として対応します：

This is a personal project. We cannot guarantee enterprise-grade SLA, but aim for:

| 対応 / Action | 目標 / Target |
|---|---|
| 初回返信 / Initial Response | 7 日以内 / Within 7 days |
| 深刻度評価 / Severity Assessment | 14 日以内 / Within 14 days |
| 修正版リリース / Fix Release | 重大度に応じる（最大 90 日）/ Depends on severity (max 90 days) |
| 公開（CVE 申請等）/ Disclosure | 修正版リリース後 / After fix release |

**重大度の判断基準（CVSS v3.1 ベース）**：
- 🔴 Critical (9.0-10.0)：即座対応（24-48 時間）
- 🟠 High (7.0-8.9)：14 日以内
- 🟡 Medium (4.0-6.9)：30 日以内
- 🟢 Low (0.1-3.9)：次回マイナーリリース

---

## 🎯 想定される脅威 / Known Threat Surface

WinKnobs は **レジストリ操作を含むデスクトップアプリ**です。
以下の領域で脆弱性が発生し得ます：

### 1. 権限昇格 / Privilege Escalation
- HKLM 書込時に管理者再起動を要求
- runas Verb の悪用可能性 → `PrivilegeService` で制御
- 想定リスク：UAC バイパス・任意コード実行

### 2. レジストリ破壊 / Registry Damage
- バックアップなしでの書込ミス
- バックアップファイル（.reg）の改ざん
- 想定リスク：システム不安定化・起動不能

### 3. コマンド実行系の悪用 / Command Injection
- `fsutil` / `netsh` 等の外部コマンド呼び出し
- 引数のサニタイズ不足
- 想定リスク：任意コマンド実行・データ破壊

### 4. バックアップファイルの改ざん / Backup Tampering
- `%APPDATA%\WinKnobs\backup\` 配下の .reg ファイル
- 改ざんされた .reg を Restore すると任意レジストリ書込
- 想定リスク：永続化・特権奪取

### 5. 依存パッケージの脆弱性 / Supply Chain
- CommunityToolkit.Mvvm / Microsoft.Extensions.* 等
- Dependabot 自動更新（GitHub Public 化後に有効化予定）

### 6. リモートデスクトップ有効化（攻撃面拡大）/ Remote Desktop Enable (v0.1.19・🔴Red)
- 「リモートデスクトップを有効にする」は **3389 番ポートを開けて外部ログイン受付を増やす唯一の操作**＝攻撃面を意図的に広げる。押し間違い・理解不足が「ランサムウェア初期侵入のトップ経路を自分で開ける」事故に直結する。
- 緩和策：① 🔴Red 専用ダイアログでチェックボックス同意（明示同意ジェスチャ）が無いと [有効化] は非活性 ② NLA（ネットワークレベル認証）を強制 ON ③ 適用は内→外の done-stack で原子化し、巻き戻し失敗で攻撃口が開いたままなら `LeftExposed` 強警告 ④ 操作前に現状（ポート/NLA/自分がリモートか/LAN IP）を提示 ⑤ first-touch 退避で原状復帰可能。
- **WAN 露出は WinKnobs では制御不能**（ルータの UPnP/ポート開放次第で 3389 が WAN に出る）。「同一 LAN 内のみ・信頼できるネットワークのみ」を推奨。
- 想定リスク：3389 への総当たり攻撃・RDP 経由の侵入。**推奨は常に「無効（既定）」**。

### 7. セキュリティ機能の無効化（防御後退）/ Core Isolation & Hyper-V Disable (v0.2.1・🔴Red)
- **コア分離（メモリ整合性・HVCI）OFF**：ドライバ悪用攻撃（BYOVD）への防壁を外す操作。ゲーム互換/性能目的の一時 OFF を想定し、ConfirmDestructive（既定いいえ）＋「用が済んだら有効へ戻す」を明示。GPO/MDM 管理下はグレーアウト。
- **Hyper-V（ハイパーバイザー起動）OFF**：コア分離・Credential Guard など仮想化ベースのセキュリティ（VBS）の土台ごと止める操作（LDPlayer 等エミュレータ用途）。🔴Red 専用ダイアログで①連鎖して止まる機能の列挙 ②コア分離 ON 検知時の依存ガイド ③ **BitLocker 回復キー警告の無条件常時表示** ④チェックボックス同意ゲート ⑤first-touch 退避＋「元に戻す」。
- 方式は `bcdedit /set hypervisorlaunchtype` 一本（機能削除しない＝完全可逆）。退避値は Auto/Off ホワイトリストで検証（改ざん JSON によるコマンド注入と「戻せない退避」を構造的に排除）。
- 想定リスク：セキュリティ後退状態の放置。**推奨は用が済んだら再有効化**。

### 8. NIC 設定変更による瞬断と退避ストア整合性 / NIC Power Saving (v0.2.2・🟡)
- NIC の省電力プリセット適用時、Set-NetAdapterAdvancedProperty / Set-NetAdapterPowerManagement で **数秒の通信瞬断**が発生し得る。リモートデスクトップ経由のセッションを切ってしまうと自分のアクセス権を失う恐れがあるため、適用時の `IsCurrentSessionRemote()` 検知時は破壊的確認（既定キャンセル）に切り替えて締め出しガード。
- 退避は GUID 単位 first-touch（`backup\maintenance\originals.json`・NetworkService の DNS 退避とは別系統）で、適用前の状態を保持。退避ファイル改ざんは「適用前状態と異なる値で書き戻し可能」になるため、Restore 時は値域外（ValidValues に無い）の値は書き戻さない（破損・他ツール改変からの防衛）。
- 想定リスク：誤適用による通信断・退避ファイル改ざんによる任意 NIC プロパティ書込。
- 緩和策：① GUID 識別＋適用直前 GUID 再解決（TOCTOU 検知）② -EncodedCommand（Base64 UTF-16LE）で日本語アダプタ名の CP932 化け排除 ③ 個別 `try/catch` 集計で部分失敗を正直に報告（`PartiallyApplied`）④ 値域ガードでの選別書込 ⑤ 「自動ロールバックしない」設計（NIC は値単位ゆえ誤発火の二次破壊を避ける）。

---

## 🔒 セキュリティ機能 / Built-in Security Features

WinKnobs は以下の安全機能を提供します：

- ✅ **自動バックアップ**：レジストリ変更前に必ず .reg バックアップ生成
- ✅ **システム復元ポイント**（重大変更時・実装予定）
- ✅ **危険度マーキング**：🟢🟡🔴 で操作前にユーザー確認
- ✅ **🔴Red 操作の明示同意ゲート**：リモートデスクトップ有効化など最高リスク操作はチェックボックス同意が無いと実行ボタンが活性化しない（誤爆防止・既定キャンセル）
- ✅ **管理者権限要求**：HKLM 系は必ず再起動 → UAC 経由
- ✅ **可逆性保証**：Backup と Restore がペアで実装される（CanRestore=false 例外あり）
- ✅ **単一インスタンス化（v0.3.1）**：名前付き Mutex で同一セッション内の二重起動を防止し、2 窓同時操作による退避（バックアップ）取得の競合（first-touch の TOCTOU）を構造的に閉じる
- ✅ **オフライン動作**：ネットワーク通信なし（外部送信ゼロ）

---

## 🙏 謝辞 / Acknowledgments

報告者には希望に応じて、リリースノート・README・CHANGELOG での謝辞掲載を行います。

We acknowledge reporters in release notes, README, and CHANGELOG (with consent).

---

## 📜 開示ポリシー / Disclosure Policy

WinKnobs は **Coordinated Disclosure** を採用しています：

1. 報告者 → メンテナへ非公開報告
2. メンテナ評価・修正開発
3. 修正版リリース
4. リリース後、報告者と協議の上で公開（GitHub Security Advisory）
5. CVE 申請（重大度に応じる）

---

📅 最終更新 / Last updated: 2026-06-22
