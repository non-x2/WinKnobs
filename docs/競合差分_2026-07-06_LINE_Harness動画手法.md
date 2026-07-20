# WinKnobs 競合差分リスト（LINE Harness動画手法の適用）

> 生成：2026-07-06 ／ 手法の出典：動画「LINE Harness」野田さん[06:41-07:32]
> 「公式ドキュメント＋競合の解説を全部AIに読ませ、差分を取って"競合にできて自分にできていない機能"を洗い出す」を WinKnobs に適用。
> 照合済み：現状＝CHANGELOG.md（v0.4.6まで実装）／候補＝v0.2_候補項目集_v2.md（32項目）。下記は**両方に無い＝真の差分**を優先掲載。

## 調査した競合（3種）

| ツール | 立ち位置 | 特徴 |
|---|---|---|
| いじくるつくーる（INASOFT） | WinKnobsの直接の代替元 | デスクトップ/エクスプローラ/右クリック/特殊フォルダ等を網羅。2016年開発休止＝Win11対応が弱い（WinKnobsの勝機） |
| Winaero Tweaker | 英語圏の定番・約20カテゴリ | 外観/コンテキストメニュー/挙動/プライバシー/保守。Copilot無効・Win10復元系が強い |
| Ultimate Windows Tweaker 5 | 219KB・200超tweak | システム情報＋DISM/SFC/復元点、プライバシー包括（テレメトリ/広告ID/Bing/Cortana） |

## 真の差分（WinKnobs未実装・候補集にも無し）

| # | 機能 | 競合 | 危険度 | 差別化価値 | 推奨 |
|---|---|---|---|---|---|
| 1 | **クラシックコンテキストメニュー**（Win11右クリック→Win10フル表示） | 全競合 | 🟢 | ★★★ 超定番・検索需要大・HKCU CLSIDで可逆 | ✅実装済みと2026-07-20判明（v0.1.0から`shell.classicContextMenu`が存在・本リスト作成時の「未実装」は調査漏れ） |
| 2 | **Copilot無効化**（タスクバー/AI右クリック除去） | Winaero | 🟢〜🟡 | ★★★ タイムリー・Win11 24H2で需要増 | ✅実装済み（2026-07-15・`privacy.disableCopilot`+`desktop.hideCopilotButton`・v0.4.7） |
| 3 | **プライバシー一括**（テレメトリ/広告ID/Bing検索/Cortana を束ねる） | UWT/Winaero | 🟡 | ★★ WinKnobsは散発のみ→"1画面で束ねる"導線が刺さる | ○ |
| 4 | **バックグラウンドアプリ一括無効化** | Winaero | 🟢 | ★★ 省電力・8700K熱対策と親和 | ○ |
| 5 | **右クリックメニュー項目の追加/削除**（クラシック化とは別） | いじくる/UWT | 🟡 | ★★ いじくるの看板機能・移行組が探す | △（実装重め） |
| 6 | **特殊フォルダ名/アイコン変更**（ごみ箱リネーム等） | いじくる | 🟢 | ★ ニッチだが"いじくるらしさ" | △ |

## 除外（競合にはあるが WinKnobs 思想的に載せない/要注意）

- ❌ **Windows Defender永久無効化／Windows Update永久無効化**（Winaero）：不可逆・セキュリティ低下。WinKnobsの「安全・可逆」思想に反するため非推奨。
- ⚠️ **エクスプローラのリボンUI復活（Win11）**（Winaero）：メモリ `reference_winaero_tweaker_explorer_tabs` の通り、Enable RibbonでExplorerのタブ機能が消える副作用あり。載せるなら強警告必須。
- ⚠️ **Windows10タスクバー/スタート復元**（Winaero）：内部でExplorerPatcher相当の重い介入。破壊リスク中〜大・保守負担大。

## 最優先の3手（この差分から）

1. ~~クラシックコンテキストメニュー（#1）~~＝**✅v0.1.0で実装済みと2026-07-20判明**（`shell.classicContextMenu`・UIAテストT-REG-4済み。本リストの「未実装」判定が誤り＝以後、着手前に実コードgrep必須）。
2. **Copilot無効化**（#2）＝タイムリー・note/Shortsのネタにもなる（柱C/D連動）。**✅2026-07-15実装済み**（`privacy.disableCopilot` HKCU Policies\WindowsCopilot・`desktop.hideCopilotButton` HKCU Explorer\Advanced・v0.4.7）。
3. **プライバシー"束ね"導線**（#3）＝既存の散発項目（Start_TrackProgs等）＋新規を1グループ化＝UX差別化。

→ いずれも「競合にあってWinKnobsに無い」を動画手法で機械的に抽出した結果。実装可否・優先度の最終判断は のんさん（着手はL1調査、実装はL2設計→承認）。

## 出典

- いじくるつくーる：[gigafree](https://www.gigafree.net/system/registry/izikuru.html) / [Wikipedia](https://ja.wikipedia.org/wiki/%E3%81%84%E3%81%98%E3%81%8F%E3%82%8B%E3%81%A4%E3%81%8F%E3%83%BC%E3%82%8B)
- Winaero Tweaker：[公式機能一覧](https://winaero.com/the-list-of-winaero-tweaker-features/) / [Tom's Hardware](https://www.tomshardware.com/software/windows/how-to-tweak-your-windows-11-and-windows-10-setup-with-winaero-tweaker-easily-remove-ads-tracking-ai-copilot-and-more)
- Ultimate Windows Tweaker 5：[The Windows Club](https://www.thewindowsclub.com/ultimate-windows-tweaker-5-for-windows-11) / [Digital Citizen](https://www.digitalcitizen.life/ultimate-windows-tweaker-5-for-windows-11-features-and-how-to-use-it/)
