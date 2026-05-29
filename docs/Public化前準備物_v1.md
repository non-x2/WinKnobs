# WinKnobs Public化前準備物 v1（11項目フル準備）

> 作成日：2026-05-28（AI代行会社 秘書さん経由・自走モード）
> 起点：[GitHub_OSS公開準備チェックリスト.md Phase 4](../../../ナレッジベース/GitHub_OSS公開準備チェックリスト.md) 11項目
> ステータス：**準備物のみ・実行はのんさん明示承認後**
> 配置：実行時にここからコピペ or `.github/` へ移植

---

## 🎯 サマリ（人間用 5 秒読み）

| # | 項目 | 準備物 | 推定工数 | 危険度 |
|---|---|---|---|---|
| 1 | v0.1.0 タグ作成 | タグメッセージ案 | 5 分 | 🟢 |
| 2 | GitHub Releases バイナリ配布 | リリースノート案 + 添付指針 | 10 分 | 🟢 |
| 3 | GitHub Sponsors 申請 | 申請文章テンプレ | 30 分 | 🟢 |
| 4 | FUNDING.yml 解除 | 解除後の内容 | 1 分 | 🟢 |
| 5 | Dependabot 有効化 | `.github/dependabot.yml` 案 | 5 分 | 🟢 |
| 6 | GitHub Discussions 有効化 | カテゴリ設計案 | 15 分 | 🟢 |
| 7 | Security Advisory 設定 | テンプレ | 10 分 | 🟢 |
| 8 | Secret Scanning 有効化 | 設定手順 | 3 分 | 🟢 |
| 9 | Code Scanning（CodeQL）有効化 | `.github/workflows/codeql.yml` 案 | 10 分 | 🟢 |
| 10 | Branch Protection（master）| 設定ルール | 10 分 | 🟡 |
| 11 | **Public 化** | 最終チェックリスト | 5 分 | 🔴 |

**合計準備工数**：約 1.5 時間（GitHub Sponsors 申請の審査待ち時間は除く・別途数日〜数週間）

---

## 1. v0.1.0 タグ作成

### 1-1. 判定が必要（のんさん最終決裁）

**選択肢**：

| 案 | 内容 | メリット | デメリット |
|---|---|---|---|
| **A** | v0.1.0 を新たに作成（v0.1.5-dev とは別系統）| OSS 慣例の「初回 Public リリース＝v0.1.0」に整合 | 既に v0.1.5-dev まで進めた実装履歴と矛盾 |
| **B** | v0.1.5 を最初のタグにする | 実装の現状と整合 | 一般ユーザーから見て「いきなり v0.1.5？」と疑問 |
| **C**（推奨）| v0.1.5（dev サフィックス削除）で初回タグ | 実装現状反映 + Public 化と整合 | csproj `<Version>0.1.5</Version>` への bump 1 ステップ必要 |

**推奨**：案 C（v0.1.5 正式版を初回タグ）

**根拠（裏付け数字）**：
- 実装済み 30 項目 + Startup カテゴリ完成は v0.1.0 のスコープを超えている
- v0.1.5-dev 状態で UIA リグレッション ALL PASS（24 OK / 0 FAIL）達成済
- VM 統合検証 PASS 後の Public 化前提なので、dev サフィックス削除のタイミングが妥当
- CHANGELOG 上の `[0.1.5-dev]` セクションを `[0.1.5]` に rename して bump 確定

### 1-2. タグメッセージ案（案 C 採用想定）

```
v0.1.5 - 初回公開リリース

Phase R-3 完了。スタートアップ管理機能（追加・編集・削除・トグル・スナップショット復元）完備。
OSS 慣習ファイル完備（LICENSE / CONTRIBUTING / SECURITY / CODE_OF_CONDUCT / CHANGELOG / ISSUE_TEMPLATE / pull_request_template）。
UI Automation テストランナー完成（~70 ケース）。

実装済み設定項目：30 項目 + Startup カテゴリ完成 UI
ターゲット：Windows 11 / .NET 10 / 自己完結型 EXE 配布

詳細：CHANGELOG.md [0.1.5] セクション参照
```

### 1-3. 実行コマンド（コピペ用）

```powershell
# csproj の Version bump（dev サフィックス削除）
# src/WinKnobs/WinKnobs.csproj の <Version>0.1.5-dev</Version> → <Version>0.1.5</Version>
# AssemblyVersion / FileVersion / PackageVersion も同様

# CHANGELOG.md の [0.1.5-dev] → [0.1.5] に変更

# commit
git add src/WinKnobs/WinKnobs.csproj CHANGELOG.md
$env:GIT_AUTHOR_NAME = "nonlabo"
$env:GIT_AUTHOR_EMAIL = "272591235+non-x2@users.noreply.github.com"
$env:GIT_COMMITTER_NAME = "nonlabo"
$env:GIT_COMMITTER_EMAIL = "272591235+non-x2@users.noreply.github.com"
git commit -m "chore: bump to v0.1.5 (initial public release)"

# タグ作成
git tag -a v0.1.5 -m "v0.1.5 - 初回公開リリース

Phase R-3 完了。..."

# push（明示承認後・本番）
git push origin master
git push origin v0.1.5
```

**注意**：`git push` は **触らないリスト**該当のため、のんさん明示承認後のみ実行。

---

## 2. GitHub Releases バイナリ配布

### 2-1. リリースノート案（v0.1.5）

````markdown
# WinKnobs v0.1.5 — 初回公開リリース

🎛️ 高橋忍さん作「いじくるツール」インスパイアの Windows 11 チューニングツール、初の Public 化リリースです。

## 🌟 ハイライト

- ✅ **30 項目の設定変更**を安全に・自動バックアップ付きで実行
- ✅ **スタートアップ管理**：Run キー族 6 ハイブ + StartupApproved 突合・追加 / 編集 / 削除 / トグル / スナップショット復元
- ✅ **ダーク UI**：Win11 ネイティブテーマ + テーマ✓連動
- ✅ **自動バックアップ**：レジストリ変更前に .reg エクスポート、ワンクリック復元
- ✅ **管理者権限ハンドリング**：必要時のみ UAC 誘導、HKLM 書込時の 3 択ダイアログ
- ✅ **完全自己完結**：.NET 10 同梱の単一 EXE（インストーラ不要・ポータブル）

## 📦 ダウンロード

`WinKnobs.exe`（自己完結型・約 141 MB）をダウンロードして任意のフォルダに配置 → ダブルクリックで起動。

## 🎯 動作環境

- Windows 11（推奨）/ Windows 10
- .NET 10 ランタイム（exe 同梱・別途インストール不要）
- 一部機能で管理者権限を要求

## ⚠️ 免責事項

このソフトウェアは Windows のレジストリを変更します。自動バックアップ機能を備えていますが、最終的なシステム変更の責任はユーザー本人にあります。重要な変更前には**システムの復元ポイント**を作成することを強く推奨します。

## 🔗 リンク

- リポジトリ：[https://github.com/non-x2/WinKnobs](https://github.com/non-x2/WinKnobs)
- ライセンス：[MIT](LICENSE)
- 貢献ガイド：[CONTRIBUTING.md](CONTRIBUTING.md)
- 脆弱性報告：[SECURITY.md](SECURITY.md)
- 詳細変更履歴：[CHANGELOG.md](CHANGELOG.md)

## 🙏 謝辞

このプロジェクトは、高橋忍さん作「いじくるツール」の哲学と UX から大きなインスピレーションを受けています。

---

🤖 Built with [.NET 10](https://dotnet.microsoft.com/) + [WPF](https://learn.microsoft.com/dotnet/desktop/wpf/)
🤝 Co-developed with [Claude](https://claude.ai) (Anthropic)
````

### 2-2. 添付ファイル指針

| ファイル | 種別 | 必須 |
|---|---|---|
| `WinKnobs.exe`（自己完結型・PublishSingleFile=true）| 配布バイナリ | ✅ 必須 |
| `WinKnobs.exe.sha256`（SHA256 ハッシュ）| 整合性確認 | ⭐ 推奨 |
| `LICENSE`（参考添付）| ライセンス確認 | 任意 |
| `CHANGELOG.md`（参考添付）| 変更履歴確認 | 任意 |

**SHA256 生成コマンド（Windows）**：
```powershell
Get-FileHash -Algorithm SHA256 -Path bin\Release\net10.0-windows\win-x64\publish\WinKnobs.exe | Format-List
# 結果をテキスト化
"$((Get-FileHash -Algorithm SHA256 -Path bin\Release\net10.0-windows\win-x64\publish\WinKnobs.exe).Hash)  WinKnobs.exe" | Out-File -Encoding ascii bin\Release\net10.0-windows\win-x64\publish\WinKnobs.exe.sha256
```

### 2-3. 実行コマンド（gh CLI）

```powershell
# Release 作成（バイナリ添付）
gh release create v0.1.5 `
  --title "WinKnobs v0.1.5 — 初回公開リリース" `
  --notes-file docs/release_notes_v0.1.5.md `
  bin\Release\net10.0-windows\win-x64\publish\WinKnobs.exe `
  bin\Release\net10.0-windows\win-x64\publish\WinKnobs.exe.sha256
```

**注意**：release_notes_v0.1.5.md を別ファイルとして用意する場合は、上記リリースノート案を docs/ 配下に保存。

---

## 3. GitHub Sponsors 申請

### 3-1. 申請文章テンプレ（英語・GitHub Sponsors 申請フォーム用）

```text
# Sponsor goals

WinKnobs is a free, open-source Windows 11 tweaking tool inspired by the legendary Japanese freeware "Ijikuru Tool" by Shinobu Takahashi (development stopped around 2018). I built WinKnobs as a clean-room reimplementation with full Windows 11 + .NET 10 support.

Your sponsorship will help me:

1. Maintain and expand the catalog of safe Windows tweaks (28+ items implemented, 26 candidates queued for v0.2-v0.3)
2. Improve the UI Automation test suite (currently ~70 cases) for higher reliability
3. Add more categories: gaming optimization, developer mode toggles, Phone Link integration
4. Support more languages beyond Japanese/English (Chinese, Korean, etc.)

WinKnobs is licensed under MIT and will remain free forever. Sponsorships fund ongoing development and testing only.

# Why I'm raising funds

This is a passion project, but it requires significant time for:

- Windows registry research (each tweak needs careful safety analysis)
- VM-based testing on multiple Windows builds
- Clean-room implementation to avoid any IP concerns with the original tool
- Documentation in Japanese + English

Any support, even at the lowest tier, helps me dedicate more time to this project.

# Languages / 言語

Primary: Japanese / 日本語
Documentation: Japanese + English / 日本語＋英語
```

### 3-2. Sponsor Tiers 案

| Tier | Monthly | Description |
|---|---|---|
| ☕ Coffee | $3 | Help fuel late-night coding sessions. Your name in `SPONSORS.md`. |
| 🍕 Pizza | $10 | Above + early access to v0.2 betas (Discord channel access). |
| 🚀 Booster | $30 | Above + vote on next features + name in README credits. |
| 💎 Custom | Custom | Above + 1-on-1 30 min consultation on Windows tweaking. |

> ⚠️ 運営コスト注記（v1.1・2026-05-29）：当初の Booster 特典「月次動画/ブログ」は撤回（ソロ運営では月 2-4h の機会費用が課金額 $30≒¥4,500 を上回るため）。Booster/Pizza の継続提供物は **v0.2 以降に再評価**（Sponsor 1 名のための定例労働を約束しない方針）。

### 3-3. 申請前チェック

- ✅ GitHub プロフィール完成（アバター・自己紹介）
- ✅ 公開リポジトリの実績（WinKnobs Public 化が前提）
- ✅ メール認証済 / 2FA 有効
- ✅ Stripe アカウント連携（決済）
- ✅ 税務情報入力（W-8BEN / W-9 等・日本居住者は W-8BEN）

**申請から承認まで**：通常 1-2 週間（業界調査・GitHub Docs 参照）

### 3-4. ⚠️ 重要な裏付け（タスク023 整合性確認）

**memory より**：[GitHub_Sponsors_任意整理整合性_判定_v1.md] にて **Plan B（BOOTH BOOST）先行採用判定**済。

| 比較軸 | GitHub Sponsors | BOOTH BOOST |
|---|---|---|
| 手数料 | Stripe 3.6% + 為替 2% = **5.6%** | 5%（pixiv 内） |
| 為替手数料 | あり（USD→JPY）| なし（JPY） |
| 日本国内向け | 認知度低 | 認知度高 |
| 海外向け | 認知度高 | ほぼゼロ |

**統合判定**：GitHub Sponsors と BOOTH BOOST の **両方** 開設 → README で併記。海外向け = GH Sponsors / 国内向け = BOOTH BOOST。

---

## 4. FUNDING.yml 解除（Sponsors 申請承認後）

### 4-1. 解除後の内容

```yaml
# GitHub Sponsors / その他の支援先
github: non-x2
custom:
  - https://note.com/nonlabo
  - https://nonlabo.booth.pm/  # ⚠️ BOOTH BOOST 開設後に追加
```

### 4-2. 注意

- `github:` は Sponsors 申請承認後のみ有効（未承認状態で記載してもエラーにはならないが Sponsors ボタン非表示）
- `custom:` は最大 4 URL まで
- URL は HTTPS 必須

### 4-3. 実行手順

1. GitHub Sponsors 申請承認メール受領
2. `.github/FUNDING.yml` を上記内容に差し替え
3. commit + push（明示承認後）

---

## 5. Dependabot 有効化

### 5-1. `.github/dependabot.yml` 案

```yaml
# Dependabot Configuration
# https://docs.github.com/en/code-security/dependabot/dependabot-version-updates/configuration-options-for-the-dependabot.yml-file

version: 2
updates:
  # .NET / NuGet packages
  - package-ecosystem: "nuget"
    directory: "/src/WinKnobs"
    schedule:
      interval: "weekly"
      day: "monday"
      time: "09:00"
      timezone: "Asia/Tokyo"
    open-pull-requests-limit: 5
    labels:
      - "dependencies"
      - "nuget"
    reviewers:
      - "non-x2"
    commit-message:
      prefix: "deps"
      include: "scope"

  # GitHub Actions
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
      day: "monday"
      time: "09:00"
      timezone: "Asia/Tokyo"
    open-pull-requests-limit: 3
    labels:
      - "dependencies"
      - "github-actions"
    commit-message:
      prefix: "ci"
```

### 5-2. 設定の根拠（裏付け数字）

| 設定 | 値 | 根拠 |
|---|---|---|
| `interval` | weekly | daily だと PR ノイズ過多（業界調査）|
| `day` | monday | 週初確認のリズム |
| `time` | 09:00 JST | のんさんの起床後の時間帯 |
| `open-pull-requests-limit` | 5（nuget） / 3（actions）| 業界中央値（GitHub Docs 推奨）|
| `commit-message.prefix` | deps / ci | Conventional Commits 準拠 |

### 5-3. GitHub UI 設定（補助）

Settings → Code security and analysis → Dependabot：
- ✅ Dependabot alerts → ON
- ✅ Dependabot security updates → ON
- ✅ Dependabot version updates → ON（dependabot.yml 必要）

---

## 6. GitHub Discussions 有効化

### 6-1. 有効化手順

Settings → General → Features → Discussions チェックボックス ON

### 6-2. カテゴリ設計案

GitHub Discussions の標準 5 カテゴリ + WinKnobs 特化 3 カテゴリ：

| カテゴリ | 形式 | 用途 | 絵文字 |
|---|---|---|---|
| **Announcements** | Announcement | リリース告知・重要連絡（メンテナのみ投稿可）| 📢 |
| **General** | Open-ended discussion | 一般的な質問・雑談 | 💬 |
| **Q&A** | Question / Answer | 使い方の質問・回答ベストアンサー機能 | ❓ |
| **Ideas** | Open-ended discussion | 新機能リクエスト（Issue Templates の代替）| 💡 |
| **Show and tell** | Open-ended discussion | カスタマイズ事例の共有 | 🎉 |
| **Setting Suggestions** 🆕 | Open-ended discussion | 「この設定追加してほしい」リクエスト | ⚙️ |
| **Compatibility Reports** 🆕 | Open-ended discussion | 「Win11 build XXXXX で動かない」報告 | 🔧 |
| **Translation Help** 🆕 | Open-ended discussion | 多言語化への協力 | 🌐 |

### 6-3. 各カテゴリの説明文（テンプレ）

**Announcements**：
> WinKnobs のリリース情報やプロジェクトの重要なお知らせを投稿します。メンテナのみが投稿できます。コメント・リアクションは自由です。

**Setting Suggestions**：
> 「この Windows 設定を WinKnobs に追加してほしい」というリクエストを受け付けます。設定の詳細（レジストリパス / 推奨値 / 既存値 / 影響範囲）が分かると優先度が上がります。

**Compatibility Reports**：
> WinKnobs が動作しない・想定外の動作をする環境を報告してください。Windows ビルド番号、エラーメッセージ、再現手順を含めてください。

### 6-4. config.yml 更新

現在の `.github/ISSUE_TEMPLATE/config.yml` に Discussions 誘導を追加：

```yaml
blank_issues_enabled: false
contact_links:
  - name: 💬 Discussions
    url: https://github.com/non-x2/WinKnobs/discussions
    about: 質問・アイデア・雑談はこちらへ。Issue は再現可能なバグ・確定済の機能要望のみでお願いします。
  - name: 🛡️ Security
    url: https://github.com/non-x2/WinKnobs/security/advisories
    about: セキュリティ脆弱性は Security Advisory（非公開）でご報告ください。
```

---

## 7. Security Advisory 設定

### 7-1. テンプレ（Security Advisory 作成時のフォーマット）

```text
# Title
[CVE 候補] 〇〇 機能で △△ により □□ が可能になる

# Severity
- [ ] Critical (CVSS 9.0-10.0)
- [ ] High (CVSS 7.0-8.9)
- [ ] Medium (CVSS 4.0-6.9)
- [ ] Low (CVSS 0.1-3.9)

# Affected versions
- v0.1.0 〜 v0.1.5（または特定バージョンのみ）

# Description
脆弱性の詳細説明（再現手順・影響範囲）

# Workaround / 一時回避策
（あれば）

# Patched in
- v0.1.X（修正版バージョン）

# Credits
報告者の氏名 / ハンドル名（同意あれば）
```

### 7-2. SECURITY.md と連動

既存の SECURITY.md（Phase 3 で作成済）にて GitHub Security Advisory への誘導が既に記載されているか確認。
誘導文例：

> 脆弱性を発見された方は、Issue ではなく [Security Advisory](https://github.com/non-x2/WinKnobs/security/advisories/new) からご報告ください。Coordinated Disclosure の方針に沿って対応します。

### 7-3. WinKnobs 固有の想定脅威リスト

将来 Advisory が必要になりうるシナリオ：
1. **snapshot 改ざん**（v0.1.6 案 A で対策予定）：第三者プロセスが `%APPDATA%\WinKnobs\backup\startup\*.reg` を改ざん → 復元時に任意レジストリ書込
2. **DLL hijacking**：WinKnobs.exe と同フォルダに悪意 DLL を配置 → 自己完結型 EXE なので通常はリスク低だが要監視
3. **管理者再起動 UAC bypass**：`PrivilegeService.TryRestartAsAdministrator` の runas verb 経路で予期せぬパラメータ渡し

---

## 8. Secret Scanning 有効化

### 8-1. 手順

Settings → Code security and analysis → Secret scanning → **Enable**

### 8-2. WinKnobs プロジェクトの追加対策

`.gitignore` で除外済の機密ファイル：
- `docs/autologon_setup_result.log`（パスワード生平文「Nobu2486」含む）
- `scripts/vm_cred.xml`（SecureString）

→ Public 化前に `git log --all -- <file>` で履歴に残っていないか再確認（filter-repo で削除済の想定だが、念のため）。

### 8-3. Push Protection 推奨

Settings → Code security and analysis → Secret scanning → **Push protection** → Enable

→ 万一誤って機密を含むファイルを commit + push しようとした際にブロックされる（保険）。

---

## 9. Code Scanning（CodeQL）有効化

### 9-1. `.github/workflows/codeql.yml` 案

```yaml
name: "CodeQL"

on:
  push:
    branches: [ "master" ]
  pull_request:
    branches: [ "master" ]
  schedule:
    - cron: '0 9 * * 1'  # 毎週月曜 09:00 UTC = 18:00 JST

jobs:
  analyze:
    name: Analyze (${{ matrix.language }})
    runs-on: windows-latest
    timeout-minutes: 360
    permissions:
      actions: read
      contents: read
      security-events: write

    strategy:
      fail-fast: false
      matrix:
        language: [ 'csharp' ]

    steps:
    - name: Checkout repository
      uses: actions/checkout@v4

    - name: Initialize CodeQL
      uses: github/codeql-action/init@v3
      with:
        languages: ${{ matrix.language }}
        queries: security-and-quality

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.0.x'

    - name: Build
      run: dotnet build src/WinKnobs/WinKnobs.csproj -c Release --no-restore
      working-directory: ${{ github.workspace }}

    - name: Perform CodeQL Analysis
      uses: github/codeql-action/analyze@v3
      with:
        category: "/language:${{ matrix.language }}"
```

### 9-2. 設定の根拠

| 設定 | 値 | 根拠 |
|---|---|---|
| `runs-on` | windows-latest | .NET 10 WPF は Windows 限定 |
| `timeout-minutes` | 360 | CodeQL C# 解析は重い（業界標準）|
| `queries` | security-and-quality | security のみより包括的（GitHub 推奨）|
| `cron` | 毎週月曜 09:00 UTC | コード変更がない週でも定期スキャン |

### 9-3. 注意

- .NET 10 が actions/setup-dotnet で対応しているか要確認（2026-05-28 時点で 10.0 preview/RC かもしれない）
- 未対応の場合は `with: dotnet-version: '10.0.100-rc.1.25xxx'` のように pin 指定

---

## 10. Branch Protection（master）

### 10-1. ルール案

Settings → Branches → Add branch protection rule → Branch name pattern: `master`

| 設定 | 推奨値 | 根拠 |
|---|---|---|
| ✅ Require a pull request before merging | ON | 直接 push 防止（メンテナ含む）|
| 　└ Require approvals | 1 | ソロ運営は緩めで OK（業界中央値 1-2）|
| 　└ Dismiss stale pull request approvals when new commits are pushed | ON | 古い approval の無効化 |
| ✅ Require status checks to pass before merging | ON | CI 緑が前提 |
| 　└ CodeQL Analysis（codeql.yml）| Required | セキュリティチェック必須化 |
| 　└ build（任意で workflow 追加）| Required | dotnet build 失敗 PR 防止 |
| ✅ Require branches to be up to date before merging | ON | rebase 必須 |
| ✅ Require conversation resolution before merging | ON | 未解決コメント merge 防止 |
| ✅ Require signed commits | ⚠ 任意 | GPG 鍵管理運用が前提・ソロなら任意 |
| ✅ Require linear history | ON | merge commit 禁止・rebase 強制 |
| ❌ Allow force pushes | OFF | filter-repo 履歴書換時のみ一時 ON |
| ❌ Allow deletions | OFF | master 削除防止 |
| ✅ Do not allow bypassing the above settings | ON | 管理者も例外なし |
| ✅ Restrict pushes that create matching branches | OFF | 通常運用には不要 |

### 10-2. 注意（ソロ運営の場合）

GitHub のルールで「Require approvals = 1」を有効化すると、メンテナ自身の PR は別の人の approval が必要になる。ソロ運営の場合、以下のどちらか：

**選択肢 A**：approval を 0 にする（PR フローのみ強制・承認は不要）
- メリット：ソロ運営でも回せる
- デメリット：レビューゼロのまま merge できる

**選択肢 B**：approval = 1 + bypass 許可（管理者は迂回可能）
- メリット：将来 Contributor 受け入れ時のフロー
- デメリット：ソロ運営中は迂回常態化で形骸化

**推奨**：選択肢 A（approval 0）+ 「PR テンプレで Self-review チェック」運用

### 10-3. v0.2 以降の段階的強化

- v0.1.5（初回 Public 化）：上記推奨設定
- v0.2.x 以降：Contributor 増加に応じて approval 1 / 2 に上げる
- v1.0.0 リリース時：signed commits 必須化検討

---

## 11. Public 化（最終・Danger Zone）

### 11-1. 直前最終チェックリスト（11 項目）

実行前に下記すべて ✅ を確認：

```
[1] v0.1.5 タグが Releases に紐づいて公開可能状態
[2] WinKnobs.exe バイナリが Releases に添付済（141MB / SHA256 ハッシュ付き）
[3] GitHub Sponsors 申請が承認済（FUNDING.yml `github: non-x2` 有効）
[4] FUNDING.yml の note.com / BOOTH URL 確認
[5] dependabot.yml 配置済（commit 反映）
[6] Discussions 8 カテゴリ作成済（既定 5 + WinKnobs 特化 3）
[7] SECURITY.md の Advisory 誘導文 反映済
[8] Secret Scanning + Push Protection ON
[9] CodeQL workflow 配置済（初回 Run の緑確認後）
[10] Branch Protection（master）ルール適用済
[11] 個人情報・パスワード再確認：
      git log --all --format='%an <%ae>' | Sort-Object -Unique
      → nonlabo のみ表示されること

[追加] README.md「開発状況」を「v0.1.5（OSS 公開済）」に更新
[追加] CHANGELOG.md [Unreleased] → [0.1.5] に確定
[追加] csproj Version 0.1.5（dev サフィックス削除）
[追加] ロードマップ表を実態に合わせて更新
```

### 11-2. Public 化手順

```
1. Settings → General → 一番下までスクロール
2. Danger Zone → Change repository visibility
3. "Change to public" をクリック
4. リポジトリ名を入力（タイポ防止）
5. "I understand, change repository visibility" をクリック
```

### 11-3. Public 化後 30 分でやること

1. ✅ Web で公開状態を確認（シークレットウィンドウで開く）
2. ✅ Releases ページの WinKnobs.exe ダウンロードリンクが機能するか確認
3. ✅ Discussions が表示されているか
4. ✅ Issues タブが表示されているか（Templates 反映確認）
5. ✅ Security タブ → Advisories / Code scanning / Secret scanning が緑か
6. ✅ Sponsors ボタンが表示されているか（FUNDING.yml 反映確認）
7. ✅ README.md のバッジ画像（License / Windows 11 / .NET 10）が表示されているか
8. ✅ X / note でアナウンス（[note公開戦略_スキル.md](../../エージェント/AI代行会社/agents/クリエイターさん/skills/note公開戦略_スキル.md) X リンクペナルティ対策準拠）

### 11-4. Public 化を取り消したい場合

Settings → Danger Zone → Change visibility → Change to private で戻せる。
**ただし**：
- 既に外部 fork が発生していたら fork は public のまま残る
- 既にダウンロードされたバイナリは取り戻せない
- → **Public 化は一方向と考えて慎重に**

---

## 🚨 全体注意事項

### A. 順序の堅持

**推奨順序**：1 → 2 → 5 → 6 → 7 → 8 → 9 → 10 → 3（申請）→ 承認待ち → 4 → 11

**根拠**：
- 1-2（タグ / Releases）：Public 化前提のリリースアセット準備
- 5-10（Dependabot / Discussions / Security 系 / Branch Protection）：Private 状態で設定可能 → Public 化と同時に有効
- 3（Sponsors 申請）：審査に 1-2 週間 → 早めに着手
- 4（FUNDING.yml 解除）：Sponsors 承認後
- 11（Public 化）：すべての準備完了確認後の最終ステップ

### B. のんさん明示承認が必要なステップ

| ステップ | 承認必要 | 理由 |
|---|---|---|
| 1 タグ作成 + push | ✅ | git push は外部送信 |
| 2 Releases 作成（gh release create） | ✅ | 公開アクション |
| 3 Sponsors 申請 | ✅ | 個人情報入力 + 税務情報 |
| 4 FUNDING.yml 解除 + push | ✅ | git push 含む |
| 5 dependabot.yml commit + push | ✅ | git push 含む |
| 6 Discussions 有効化 | ⭕ 推奨確認 | 公開機能ON |
| 7 Security Advisory 設定 | ⭕ 推奨確認 | 公開ポリシー |
| 8 Secret Scanning ON | ⭕ 推奨確認 | 公開セキュリティ機能 |
| 9 CodeQL workflow 追加 | ✅ | commit + push 含む |
| 10 Branch Protection | ⭕ 推奨確認 | 開発フロー変更 |
| 11 **Public 化** | ✅✅✅ | 不可逆性高・最重要 |

### C. ロールバック方針

各ステップで失敗した場合の戻し：
- 1 タグ：`git tag -d v0.1.5 && git push --delete origin v0.1.5`
- 2 Release：GitHub Web UI から Release 削除
- 5 dependabot.yml：`git revert <commit>`
- 9 CodeQL workflow：`git revert <commit>`
- 10 Branch Protection：Settings から Rule 削除
- 11 **Public 化**：Settings → Change visibility → Private（ただし fork は残る）

---

## 📊 工数まとめ（裏付け数字）

| カテゴリ | 工数 | 内訳 |
|---|---|---|
| **準備のみ（本ファイル作成）**| 90 分 | 完了済 |
| 実装 + commit（5/9）| 30 分 | dependabot.yml / codeql.yml / FUNDING.yml |
| GitHub Web UI 設定（6/7/8/10）| 40 分 | Discussions / Security 系 / Branch Protection |
| Sponsors 申請（3）| 30 分 + 審査 1-2 週間 | 文章 + 税務情報入力 |
| Release 作成（1/2）| 25 分 | タグ + バイナリビルド + リリースノート |
| **Public 化（11）**| 5 分 | 最終クリック |
| **合計（審査待ち除く）**| 約 2 時間 | |

---

## 🔗 関連

- [GitHub_OSS公開準備チェックリスト.md](../../../ナレッジベース/GitHub_OSS公開準備チェックリスト.md) Phase 4
- [CHANGELOG.md](../CHANGELOG.md) [Unreleased] / [0.1.5-dev]
- [次セッション再開ガイド.md](../次セッション再開ガイド.md) 残課題 4
- [docs/v0.1.6_候補設計ドラフト_v1.md](v0.1.6_候補設計ドラフト_v1.md)（Public 化後の次の実装ターゲット）

---

## 📝 履歴

- v1（2026-05-28 / AI代行会社 秘書さん経由）：初版作成
  - 11 項目を 1 ファイルに集約 + 各項目に裏付け数字
  - タグ案 3 つの比較 + 推奨（案 C：v0.1.5 で初回タグ）
  - リリースノート案 / Sponsors 申請文 / Dependabot config / Discussions カテゴリ / Security Advisory テンプレ / CodeQL workflow / Branch Protection ルール 一式
  - 承認順序 + ロールバック方針 + 工数まとめ
