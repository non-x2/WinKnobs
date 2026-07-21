<#
.SYNOPSIS
    WinKnobs のバージョンタグ（v0.1.0 〜 v0.4.9）を一括作成する冪等スクリプト。

.DESCRIPTION
    バージョン→コミットハッシュの対応表（下記 $VersionCommitMap）をもとに
    `git tag -a vX.Y.Z <hash> -m "WinKnobs vX.Y.Z"` を一括実行する。
    既存タグの扱い：
      - 同一コミットを指す既存タグ → skip（再作成しない）
      - 別コミットを指す既存タグ → 警告表示のみ（削除・上書きはしない）
      - タグが無い → 新規作成

    対応表の根拠：docs\release_tags_commit_map_2026-07-21.md（バージョン→コミット突合表）。

.PARAMETER DryRun
    実際にはタグを作成せず、作成予定/skip/警告の一覧のみ表示する。

.NOTES
    ⚠️ 重要：将来 `git filter-repo`（履歴匿名化）を実行するとコミットハッシュが
    全て変わる。filter-repo 実行後は本スクリプト冒頭の $VersionCommitMap を
    新しいハッシュに更新してから再実行すること（filter-repo 前のこのハッシュ表は無効になる）。

.EXAMPLE
    pwsh scripts\create_release_tags.ps1 -DryRun
    pwsh scripts\create_release_tags.ps1
#>

param(
    [switch]$DryRun
)

# リポジトリ直下以外での誤実行防止（55件の警告羅列で気づきにくい事故を防ぐ）
$repoRoot = git rev-parse --show-toplevel 2>$null
if (-not $repoRoot -or -not (Test-Path (Join-Path $repoRoot 'WinKnobs.slnx'))) {
    Write-Error "WinKnobs リポジトリ直下で実行してください（git rev-parse 失敗 or WinKnobs.slnx が見つかりません）。"
    exit 1
}

$ErrorActionPreference = "Stop"

# バージョン → コミットハッシュ 対応表（2026-07-21 時点・filter-repo 実行前）
# 複数バージョンをまとめて実装したコミットは同一ハッシュを複数バージョンへ割り当てる。
$VersionCommitMap = [ordered]@{
    "v0.1.0"    = "fc0c75690cfb43e46bf7a443d31927a8a0f628fc"  # 初期コミット（v0.1.0-v0.1.2 baseline squash）
    "v0.1.1"    = "fc0c75690cfb43e46bf7a443d31927a8a0f628fc"
    "v0.1.2"    = "fc0c75690cfb43e46bf7a443d31927a8a0f628fc"
    "v0.1.6"    = "22fceb5c092d8d174f2ea1fd94ea8b1834c0a756"
    "v0.1.7"    = "29b2b9d1549715c60ea575fa5bd4853d3be65b57"
    "v0.1.8"    = "a890ec267e2509304fb2c53062cf826d93a9af4a"
    "v0.1.9"    = "6a5527a5212463787ff47945d85f775f5ad8ae36"
    "v0.1.10"   = "72c344a16685f19c578553dc51ae88e7e797b6ff"
    "v0.1.11"   = "8072b70a31b39a7b43f9857c4561838386f5030c"
    "v0.1.12"   = "16d4eeef43bcb4abc97e657e41093a56949b61aa"
    "v0.1.13"   = "0dd46363de591ce7e45ac9297a4f6ca16641ce1f"
    "v0.1.14"   = "1c523434c9edc57a97be67416da6aa61d374ce63"
    "v0.1.15"   = "1d523f4e5c45863d229e41d2254a8a448bccce88"
    "v0.1.16"   = "3ce0eb03f869d7a80c0df36e7896eaf7716a88e2"
    "v0.1.17"   = "94ac1ce81deed9089a2aad7b421343ac82a48e37"
    "v0.1.18"   = "d5e2131ca73a1390ffb1821ebd560cc7a756ab84"
    "v0.1.19"   = "9835333f806b35c4976485884514bd1a40c26cc8"
    "v0.1.19.1" = "feea5ad4a411f53a533fbe5abf5da0ba8d0d03d5"
    "v0.1.20"   = "c66d6afb2fefb6a8a236aff6edeb4fd8630d997a"
    "v0.2.0"    = "2c8bc9017aff4a95f730ec344d8a5a76cd3ffd89"
    "v0.2.1"    = "359bf0e58253d7ed9fcfbd331df13ac976066b14"
    "v0.2.2"    = "3216e8b8d9104f1883d5abff36c931a1df55a54d"
    "v0.3.0"    = "ddf9f0821c5dd081cb3110db3a5cdcee78fd166a"
    "v0.3.1"    = "3995ba9afb2f10425f750482f2d05dde17d1c259"
    "v0.3.2"    = "d79ccf5bfd3756260747fbc9cecc67c047284ebe"
    "v0.3.3"    = "7b54688df82eca18f8fb8ccd880f406c17eff9bd"
    "v0.3.4"    = "01eed785dbd48431cf74ccc03752e2318d4f4cdb"
    "v0.3.5"    = "6a863f83931390c7c43cd6231ab24d788564043f"
    "v0.3.6"    = "b3c6f88a623cd5b5292fdc07c5581480f5f638d6"
    "v0.3.7"    = "5e72a8782753e65871891d53ac34afb20ac634f1"
    "v0.3.8"    = "1e449036793a6df7ab55e4572f2083a16108f997"
    "v0.3.9"    = "3d265131ee6b3d4a316bee6466d9aa968b902d64"
    "v0.3.10"   = "197065c005382f631814bd8ea532fc9689f91e0c"
    "v0.3.11"   = "f1a04a050799388cb5dfff0a1611a533ff7dadeb"
    "v0.3.12"   = "bf3f92ce2c227967bd0b36a31baaa8c0d01d1d0c"
    "v0.3.13"   = "5c05b6b6e7a058df5214ab5726525233d8eb22cc"
    "v0.3.14"   = "96c63f4c5aeacfff2ea3c4fbcd9efff09b3561ff"
    "v0.3.15"   = "18c30e1a23e0ffb6bab7362e48360b6114784114"  # v0.3.15-17 まとめコミット
    "v0.3.16"   = "18c30e1a23e0ffb6bab7362e48360b6114784114"
    "v0.3.17"   = "18c30e1a23e0ffb6bab7362e48360b6114784114"
    "v0.3.18"   = "eaa4d73d3a1072404906f81e56cff39da7fc4d2a"  # v0.3.18-20 まとめコミット
    "v0.3.19"   = "eaa4d73d3a1072404906f81e56cff39da7fc4d2a"
    "v0.3.20"   = "eaa4d73d3a1072404906f81e56cff39da7fc4d2a"
    "v0.3.21"   = "a461ec4f4794c36c55c2ff5c9a8fd8c062c06b5d"  # v0.3.21-22 まとめコミット
    "v0.3.22"   = "a461ec4f4794c36c55c2ff5c9a8fd8c062c06b5d"
    "v0.4.0"    = "8a3fd2c4aef5e490b5e1e263b89c9831ec4dd707"  # v0.4.0-1 まとめコミット
    "v0.4.1"    = "8a3fd2c4aef5e490b5e1e263b89c9831ec4dd707"
    "v0.4.2"    = "abf84a69a66fe86ebed756b5e67f6b5b5c3b67fb"
    "v0.4.3"    = "61460d820bee0957a11039e49479b4ef701cf6ef"
    "v0.4.4"    = "4f148d8de1fdf0a40f75772977665c7731406461"  # v0.4.4-5 まとめコミット
    "v0.4.5"    = "4f148d8de1fdf0a40f75772977665c7731406461"
    "v0.4.6"    = "fd6383e30e4aeb998e7b1b71eb72f116a4b77f6b"  # 既存タグ v0.4.6 と同一コミット→skip想定
    "v0.4.7"    = "613caccbebbde938612e628abb17996d2d85db3d"
    "v0.4.8"    = "87e36ef2ac15672abd71bf645961c3b8181950c1"
    "v0.4.9"    = "7e4008982f5c288acdd0b59d398bbd78df0c9ec5"
}

$created = 0
$skipped = 0
$warned = 0

foreach ($tagName in $VersionCommitMap.Keys) {
    $targetHash = $VersionCommitMap[$tagName]

    # コミットの実在確認
    git cat-file -e "$targetHash^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "$tagName : コミット $targetHash が見つかりません（対応表を確認）。skip します。"
        $warned++
        continue
    }

    $existingHash = git rev-parse -q --verify "refs/tags/$tagName^{commit}" 2>$null

    if ($existingHash) {
        if ($existingHash -eq $targetHash) {
            Write-Host "[skip] $tagName は既に同一コミット ($($existingHash.Substring(0,7))) を指しています。" -ForegroundColor DarkGray
            $skipped++
        } else {
            Write-Warning "$tagName : 既存タグは $($existingHash.Substring(0,7)) を指していますが、対応表は $($targetHash.Substring(0,7)) です。差異あり・削除はしません。"
            $warned++
        }
        continue
    }

    if ($DryRun) {
        Write-Host "[dry-run] $tagName -> $($targetHash.Substring(0,7)) を作成予定" -ForegroundColor Cyan
        $created++
    } else {
        git tag -a $tagName $targetHash -m "WinKnobs $tagName"
        if ($LASTEXITCODE -eq 0) {
            Write-Host "[create] $tagName -> $($targetHash.Substring(0,7))" -ForegroundColor Green
            $created++
        } else {
            Write-Warning "$tagName : git tag 作成に失敗しました。"
            $warned++
        }
    }
}

Write-Host ""
if ($DryRun) {
    Write-Host "=== DryRun結果: 作成予定 $created 件 / skip $skipped 件 / 警告 $warned 件 ===" -ForegroundColor Yellow
} else {
    Write-Host "=== 実行結果: 作成 $created 件 / skip $skipped 件 / 警告 $warned 件 ===" -ForegroundColor Yellow
}
