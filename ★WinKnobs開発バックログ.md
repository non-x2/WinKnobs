---
名前: WinKnobs開発バックログ
最終更新: 2026-07-20
鮮度確認: 2026-07-20（次回目安: 2026-10-20）
正本: この文書（WinKnobs自走開発の「次に何を作るか」の単一情報源）
レーン: WinKnobs
---

# 📋 ★WinKnobs開発バックログ（自走開発の計画正本）

> 🎯 目的：AIセッションが**のんさんの指示なしで自分で次の開発項目を選び、計画を立てる**ための単一情報源。
> 運用：自走セッションは毎サイクル①このファイルを読む→②採点ルールで先頭候補を選ぶ→③計画化（plan-validator）→④実装（worker-exec）→⑤レビュー（cross-reviewer）→⑥本表を更新。
> 起動方法＝`指示書\タスク指示_WinKnobs自走開発_2026-07-20.md` 参照。

## 🧮 採点ルール（自分で計画を立てる基準）
優先度 = 需要（★1-3） + 実装コスト逆点（🟢=3/🟡=2/🔴=1） + 北極星寄与（公開・収益に直結=+2／間接=+1）
- 同点は「ユーザーに見える価値が大きい方」を先。
- 🔴大（新規アーキ・破壊的変更）はバックログに残し、のんさん確認を承認キューへ積む（勝手に着手しない）。
- 新項目の追加自由（競合調査・ユーザー需要・不具合発見時）。ただし出典を1行残す。

## 🔥 アクティブキュー（上から着手）

| # | 項目 | 需要 | コスト | 北極星 | 状態 | 出典 |
|---|---|---|---|---|---|---|
| 8 | DeleteValueOrThrow昇格フロー統一（副次失敗経路が昇格誘導に乗らない→UnauthorizedAccessException化・既存15箇所超の回帰テスト必須） | ★ | 🟡 | +1 | ⬜未着手 | cross-reviewer 2026-07-21 should_fix②（既存ギャップ・v0.4.5〜に波及） |
| 10 | Commandトークンの鮮度表示＋孤児掃除（command_tokens.jsonに保存時刻を持たせ「◯月◯日適用時の状態に戻せます」表示／CanRestore喪失項目の孤児トークン掃除） | ★ | 🟢 | +1 | ⬜未着手 | cross-reviewer 2026-07-21 should_fix2＋nice_to_have4（v0.4.10サイクル・別セッションレビュー分） |
| 5 | 右クリックメニュー項目の追加/削除（クラシック化とは別） | ★★ | 🔴 | +1 | ⬜未着手（実装重め・計画のみ先行可） | 競合差分 #5 |
| 6 | 特殊フォルダ名/アイコン変更（ごみ箱リネーム等） | ★ | 🟢 | +1 | ⬜未着手 | 競合差分 #6 |

## ✅ 完了
| 日付 | 項目 | バージョン | 備考 |
|---|---|---|---|
| 2026-07-21 | レジストリ型LastBackupPathの永続化（#9・backup\registry_backup_paths.json新設） | v0.4.11 | 品質ゲート0＝未実装実証（LastBackupPathはVMプロパティのみ・Load時nullリセット）＋command_tokens.json同型/ファイル分離で相乗り。latest-wins（レジストリ型のセッション内挙動の鏡・Command型first-touch-winsとの非対称はダイアログ文言+CHANGELOG明記）。保存はDoApply書込成功後のみ（幽霊エントリ防止）。CleanupOldBackups参照中.reg保護（共有10件枠の押し出し対策）。plan-validator 8指摘反映・cross-reviewer must_fixゼロ+should_fix3件対処（③VM統合テストは#7先例のストア層テストで受容＝机上トレース済み）・テスト1229件green（新規9件）・build 0警告・自走ループ第5サイクル・commit 188ac3c |
| 2026-07-21 | Command型戻しトークンの永続化（#7・backup\command_tokens.json新設） | v0.4.10 | 品質ゲート0＝既存BackupServiceのJSON原状退避パターンに相乗り（二重ストア回避）。first-touch-wins＋原子的置換。Load時トークン復元→閉じても「戻す」可能に。plan-validator 7指摘反映（保存はApply成功後＝幽霊トークン防止・旧仕様文言更新等）・cross-reviewer must_fixゼロ＋should_fix2件反映（SystemSoundService無音no-op→例外化で偽成功排除・横展開は#9起票）・テスト1220件green（新規7件）・自走ループ第4サイクル |
| 2026-07-21 | Public化残項目のローカル完結分（#4） | bumpなし（アプリコード変更なし） | タグ55件ローカル作成（冪等スクリプトscripts\create_release_tags.ps1・filter-repo後も1コマンド再生成可）＋codeql.yml＋リリースノート下書き＋のんさん手順書（指示書\WinKnobs_Public化_のんさん手順書_2026-07-21.md）。commit de7bf6f+954a9a4（ローカルのみ）。秘匿5ファイルの履歴クリーン実測済み。**残＝のんさん側**：push承認（計4commit+55tag）→Settings操作→Public化。plan-validator 4指摘・cross-reviewer must_fix1+should_fix5全て反映・テスト1213件green・自走ループ第3サイクル |
| 2026-07-21 | プライバシー2本＝診断データ最小化＋広告ID無効化（privacy.telemetryMinimized / privacy.advertisingIdDisabled） | v0.4.9 | 競合差分 #3（旧バックログ#2）・品質ゲート0で判明＝Bing/Cortanaは実装済み（スコープ半減）・**束ねUI不採用**（個別ノブと二重管理になるため。Privacyカテゴリ画面で1画面要件充足）・plan-validator 4指摘反映（広告IDはBoolean禁止→Command型で巻き添え削除防止）・cross-reviewer must_fixゼロ・テスト1213件green・自走ループ第2サイクル |
| 2026-07-21 | バックグラウンドアプリ一括無効化（system.disableBackgroundApps） | v0.4.8 | 競合差分 #4・品質ゲート0〜5全通過（plan-validator 6指摘反映・cross-reviewer must_fixゼロ・テスト1163件green）・自走ループ初回サイクル |
| 2026-07-15 | Copilot無効化（privacy.disableCopilot / desktop.hideCopilotButton） | v0.4.7 | 競合差分 #2・cross-reviewer済 |
| 2026-05-15 | クラシックコンテキストメニュー（shell.classicContextMenu） | v0.1.0 | **07-20判明＝競合差分#1の「未実装」は調査漏れ**（plan-validatorが検出・UIAテストT-REG-4済み）。教訓→品質ゲート0番を新設 |

## 🚫 やらない（思想的除外）
- 競合差分リストの「除外」節参照（`docs\競合差分_2026-07-06_LINE_Harness動画手法.md`）

## 📐 品質ゲート（全項目共通・省略禁止）
0. **実装済みチェック**（着手前に実コードをgrep＝ドキュメントの「未実装」記載を鵜呑みにしない。07-20に#1で誤記を実証）
1. plan-validator（実装前・見落とし3つ指摘→潰す）
2. worker-exec実装（既存ノブのパターン踏襲・レジストリ操作は可逆＝戻し実装必須）
3. cross-reviewer（実装後）
4. `dotnet build -c Release` 成功＋既存テスト green（テスト実行方法はリポジトリのscripts参照・VM UIAテストはvm_cred有効時のみ）
5. バージョンbump＋CHANGELOG＋ローカルcommit（**push禁止＝承認キュー**）

## 🔗 関連
- 競合差分の正本: `docs\競合差分_2026-07-06_LINE_Harness動画手法.md`
- 自走の起動: `C:\AI\Claudo\指示書\タスク指示_WinKnobs自走開発_2026-07-20.md`
- 自走の型: `C:\AI\Claudo\指示書\★ノンストップ自走_起動キット.md`

## 📝 更新履歴
- 2026-07-20 新規作成（自走開発システム構築の一環・競合差分リスト+Public化残項目を初期投入）
