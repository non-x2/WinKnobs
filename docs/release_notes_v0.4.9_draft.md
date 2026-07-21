# WinKnobs v0.4.9 リリースノート（下書き）

> `gh release create v0.4.9 --notes-file docs\release_notes_v0.4.9_draft.md` 用の下書き。
> v0.4.0〜v0.4.9（2026-07-04〜2026-07-21）の要約。詳細は [CHANGELOG.md](../CHANGELOG.md) 参照。

## 🎛️ WinKnobs v0.4.9 — ゲーミング/ネットワーク拡充＋プライバシー強化

v0.4.0 から v0.4.9 まで、ゲーミング向けチューニング・ネットワーク細分化・プライバシー保護の3系統を中心に、10バージョンで計13個の設定項目を追加しました。

### 🌟 主要機能ハイライト

- 🕹️ **ゲーミング系（v0.4.0〜v0.4.2）**
  - 全画面最適化 無効化（v0.4.0）：ゲームの排他フルスクリーン挙動を制御
  - ゲーム用タスクのスケジューリング優先度 Medium→High（v0.4.1）
  - ハードウェアアクセラレータ GPU スケジューリング（HAGS）ON/OFF（v0.4.2）
- 🖥️ **デスクトップ/UI**
  - スタートメニュー「おすすめ」非表示（v0.4.3）
- 🌐 **ネットワーク系（v0.4.4〜v0.4.6）**
  - TCP Nagle アルゴリズム無効化（v0.4.4）／配信の最適化：ピアツーピア共有を無効化（v0.4.5）
  - 割り込み加減（Interrupt Moderation）無効化（v0.4.6・物理アダプタ全台一括）
- 🔒 **プライバシー系（v0.4.7〜v0.4.9）**
  - Copilot 無効化 2 項目：ポリシー無効化＋タスクバーボタン非表示（v0.4.7）
  - バックグラウンドアプリ一括無効化（v0.4.8・UWP/Store アプリ対象）
  - 診断データ最小化＋広告ID無効化（v0.4.9・厳密判定＋既定復元の安全設計）

各項目は plan-validator による設計精査済み・純粋関数ヘルパー＋xUnit テストで裏付け。全1213件 xUnit green（v0.4.9時点）。

### 📦 ダウンロード

`WinKnobs.exe`（自己完結型・単一 EXE）をダウンロードして任意のフォルダに配置 → ダブルクリックで起動。

### 🎯 動作環境

- Windows 11（推奨）/ Windows 10
- .NET 10 ランタイム（exe 同梱・別途インストール不要）
- 一部項目（HKLM 系・ネットワークアダプタ設定等）で管理者権限を要求

### ⚠️ 免責事項

このソフトウェアは Windows のレジストリ・ネットワークアダプタ設定を変更します。自動バックアップ機能を備えていますが、最終的なシステム変更の責任はユーザー本人にあります。重要な変更前には**システムの復元ポイント**を作成することを強く推奨します。

### 🔗 リンク

- リポジトリ：[https://github.com/non-x2/WinKnobs](https://github.com/non-x2/WinKnobs)
- ライセンス：[MIT](../LICENSE)
- 貢献ガイド：[CONTRIBUTING.md](../CONTRIBUTING.md)
- 脆弱性報告：[SECURITY.md](../SECURITY.md)
- 詳細変更履歴：[CHANGELOG.md](../CHANGELOG.md)

### 🙏 謝辞

このプロジェクトは、高橋忍さん作「いじくるツール」の哲学と UX から大きなインスピレーションを受けています。

---

🤖 Built with [.NET 10](https://dotnet.microsoft.com/) + [WPF](https://learn.microsoft.com/dotnet/desktop/wpf/)
🤝 Co-developed with [Claude](https://claude.ai) (Anthropic)
