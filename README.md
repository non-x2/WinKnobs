# WinKnobs

> 🎛️ A modern Windows 11 tweaking tool, inspired by 「いじくるツール」(Ijikuru Tool) by Shinobu Takahashi.
>
> 🎛️ 高橋忍さん作「いじくるツール」にインスパイアされた、最新Windows 11対応の独自実装ツール。

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Windows 11](https://img.shields.io/badge/Windows-11-blue?logo=windows)](https://www.microsoft.com/windows/windows-11)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple?logo=dotnet)](https://dotnet.microsoft.com/)

---

## 🌐 Languages / 言語

- [日本語](#日本語)
- [English](#english)

---

## 日本語

### 概要

WinKnobs は、Windows 11 の隠し設定や深い部分を、安全に・分かりやすく変更できる無料のチューニングツールです。

「いじくるツール」（高橋忍さん作・2018年で開発停止）の使い勝手と項目分類を**リスペクト**しつつ、最新の Windows 11 / 10 に対応した完全独自実装です。

### 特徴

- 🔒 **安全第一**：レジストリ変更前に自動バックアップ（.regファイル出力）
- 🛡️ **ワンクリック復元**：変更前の状態にすぐ戻せる
- 📦 **システム復元ポイント自動作成**：重大な変更時に保険を作る
- ⚠️ **危険度マーク**：項目ごとに緑（安全）／黄（注意）／赤（上級者向け）を表示
- 📝 **日本語の親切な説明**：何が変わるか・どう影響するかを項目ごとに記載
- 🎨 **モダンなWPF UI**：Windows 11デザインに馴染むネイティブGUI
- 🔓 **オープンソース（MIT License）**：誰でも使える・改造できる・配布できる

### スクリーンショット

（v0.1.5 公開リリース時に追加予定）

### 動作環境

- Windows 11（推奨）/ Windows 10
- .NET 10 ランタイム（exe同梱・別途インストール不要）
- 管理者権限（一部機能で必要）

### インストール

1. [Releases](../../releases) ページから最新版の `WinKnobs.exe` をダウンロード
2. 任意のフォルダに置く（インストーラ不要・ポータブル）
3. ダブルクリックで起動

### 使い方

1. 起動したら、左メニューからカテゴリを選択
2. 変更したい項目を見つけてクリック
3. 説明文を読んで、推奨設定または独自値を選択
4. 「適用」を押すと**自動でバックアップが取られて**から変更される
5. 万一不具合が出たら「ツール」メニュー → 「変更を元に戻す」

### 開発状況

- ⏳ **v0.1.5-dev**：開発中（**30 項目 + スタートアップ管理 UI 完成**・OSS 公開準備中）
- 📋 詳細：[CHANGELOG.md](CHANGELOG.md) / [CONTRIBUTING.md](CONTRIBUTING.md)

### ロードマップ

| バージョン | 内容 | 状態 |
|---|---|---|
| v0.1.0 | コア機能・基礎 9 項目 + Win11 価値項目 7 項目 + A3-2-c 型拡張 | ✅ 完了 |
| v0.1.2 | プライバシー 3 + ネットワーク 1 + シェル 2 項目追加（計 28 項目）| ✅ 完了 |
| v0.1.4 | HKLM 書込時の管理者権限ハンドリング（PrivilegeService）| ✅ 完了 |
| v0.1.5 | スタートアップ管理 GUI（Run x6 + StartupApproved・追加 / 編集 / 削除 / トグル / snapshot 復元）+ OSS 化整備 | 🚧 公開準備中 |
| v0.1.6 | snapshot SHA256 改ざん対策・差分件数警告・復元前プレビュー UI | 📅 設計済 |
| v0.2.0 | Quick Win（Power Throttling 無効化 / Sysmain サービス）※メモリ圧縮 v0.1.17・透明効果/クリップボード履歴は実装済だった | ✅ 完了 |
| v0.2.1 | LDPlayer 解消スプリント（マウス加速 / Network Throttling / Boost / WSL / コア分離 / Hyper-V） | ✅ 完了 |
| v0.2.2 | NIC 省電力プリセット + 夜間モード色温度 | ✅ 完了 |
| v0.3.0 | グラフィック設定（アプリ別 GPU・全体トグル）+ 仮想メモリ | ✅ 完了 |
| v0.3.1 | 単一インスタンス化（二重起動防止・Mutex）| ✅ 完了 |
| v0.3.2 | タスクバーの検索ボックス表示（4 択・HKCU）| ✅ 完了 |
| v0.3.3 | システム効果音 ON/OFF（🔊サウンド カテゴリ・HKCU）| ✅ 完了 |
| v0.3.4 | Windows 起動音 ON/OFF（🔊サウンド・HKLM・要管理者）| ✅ 完了 |
| v0.3.5 | スナップアシスト ON/OFF（デスクトップ・HKCU）| ✅ 完了 |
| v0.3.6 | ストレージセンサー ON/OFF（システム・HKCU）| ✅ 完了 |
| v0.3.7 | ゲームモード ON/OFF（🕹️ゲーミング カテゴリ・HKCU）| ✅ 完了 |
| v0.3.8 | Game DVR ON/OFF（🕹️ゲーミング・HKCU・2値同時）| ✅ 完了 |
| v0.3.9 | タスクバー時計に秒を表示（デスクトップ・HKCU）| ✅ 完了 |
| v0.3.10 | タスクバーのボタン表示2種（タスクビュー/チャット）| ✅ 完了 |
| v0.3.11 | 最大のプロセッサの状態（CPU 上限クロック %・powercfg）| ✅ 完了 |
| v0.3.12 | 隠しファイル・フォルダーを表示（シェル・HKCU）| ✅ 完了 |
| v0.3.13 | 項目のチェックボックスを表示（シェル・HKCU）| ✅ 完了 |
| v0.3.14 | エクスプローラの起動先（シェル・HKCU・3択）| ✅ 完了 |
| v0.3.15 | タスクバーのボタンを結合（デスクトップ・HKCU）| ✅ 完了 |
| v0.3.16 | マウスホイールのスクロール行数（キーボード/マウス・HKCU）| ✅ 完了 |
| v0.3.17 | 「よく使うアプリ」の表示/記録（プライバシー・HKCU）| ✅ 完了 |
| v0.3.18 | ナビゲーションウィンドウにすべてのフォルダーを表示（シェル・HKCU）| ✅ 完了 |
| v0.3.19 | ファイル削除時の確認ダイアログ（シェル・HKCU）| ✅ 完了 |
| v0.3.20 | クイックアクセスに「最近使ったファイル」を表示（シェル・HKCU）| ✅ 完了 |
| v0.3.21 | TCP 即時 ACK（ネットワーク・HKLM・要管理者・物理アダプタ全台）| ✅ 完了 |
| v0.3.22 | 手書き入力パッドの自動起動（キーボード/マウス・HKCU）| ✅ 完了 |
| v0.4.0 | 全画面最適化 無効化（ゲーミング・HKCU）| ✅ 完了 |
| v0.4.1 | ゲーム用タスクのスケジューリング優先度（ゲーミング・HKLM・要管理者）| ✅ 完了 |
| v0.4.2 | ハードウェアアクセラレータ GPU スケジューリング（ゲーミング・HKLM・要管理者）| ✅ 完了 |
| v0.4.3 | スタートメニューの「おすすめ」セクションを非表示（デスクトップ・UI・HKCU）| ✅ 完了 |
| v0.4.4 | TCP Nagle アルゴリズムを無効化（ネットワーク・HKLM・要管理者）| ✅ 完了 |
| v0.4.5 | 配信の最適化のピアツーピア共有を無効化（ネットワーク・HKLM・要管理者）| ✅ 完了 |
| v0.4.x | Gaming プリセット機能（複数設定の一括切替・設計ドラフト作成済）| 📅 設計済 |
| v1.0.0 | 全項目完備・多言語対応 | 📅 将来 |

### 謝辞

このプロジェクトは、**高橋忍さん**が作られた「**いじくるツール**」の哲学とユーザー体験から大きなインスピレーションを受けています。
何百項目もの Windows 設定を整理して、誰でも使えるようにしてくださった先駆者に深く感謝します。

WinKnobs は完全独自実装であり、いじくるツールのソースコード・アイコン・固有のテキストは一切含みません。
タブ構成や項目選定の**アイデア**を参考にしています。

### ライセンス

[MIT License](LICENSE)

### 開発者向け

開発に参加したい方は [CONTRIBUTING.md](CONTRIBUTING.md) をご覧ください。

---

## English

### Overview

WinKnobs is a free, modern Windows tweaking tool that lets you safely modify hidden Windows 11 settings.

It is inspired by the legendary Japanese freeware **「いじくるツール」(Ijikuru Tool)** by Shinobu Takahashi, which stopped development around 2018. WinKnobs is a complete clean-room reimplementation with full Windows 11 support.

### Features

- 🔒 **Safety first**: Automatic registry backup before any change
- 🛡️ **One-click restore**: Roll back any change instantly
- 📦 **System restore point** auto-creation for critical changes
- ⚠️ **Risk indicators**: Green (safe) / Yellow (caution) / Red (advanced) per setting
- 📝 **Detailed descriptions**: What each tweak does and its impact
- 🎨 **Modern WPF UI**: Native Windows 11 look and feel
- 🔓 **Open source (MIT License)**

### Requirements

- Windows 11 (recommended) / Windows 10
- .NET 10 Runtime (bundled with exe)
- Administrator privileges (for some features)

### Installation

1. Download the latest `WinKnobs.exe` from [Releases](../../releases)
2. Place in any folder (portable, no installer)
3. Double-click to launch

### Acknowledgments

This project is deeply inspired by the philosophy and UX of **Ijikuru Tool** by Shinobu Takahashi, a pioneer in Windows tweaking utilities. WinKnobs is an independent clean-room implementation; it contains no original source code, icons, or proprietary text from Ijikuru Tool. Only the high-level concepts of category organization and setting selection serve as references.

### License

[MIT License](LICENSE)

---

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for the contribution guidelines.

## Author

- のんラボ - Project owner
- Co-developed with Claude (Anthropic)

## Disclaimer / 免責事項

This software modifies Windows registry settings. While WinKnobs includes safety mechanisms (auto-backup, system restore points), the user is ultimately responsible for any system changes. Always create a system restore point before making significant changes.

このソフトウェアは Windows のレジストリを変更します。自動バックアップ機能を備えていますが、最終的なシステム変更の責任はユーザー本人にあります。重要な変更前には必ずシステム復元ポイントを作成してください。
