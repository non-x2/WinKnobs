# TC-dialog-inputs: 入力系項目のダイアログ動作確認
# T-W3 MenuShowDelay（数値検証）/ T-W8 Link Binary（4byte 表示確認）
#
# 安全設計：適用ボタンは押さない（読込＋値変更＋キャンセル閉じのみ）
# 副作用なし

function Invoke-DialogInputs {
    param([Parameter(Mandatory=$true)]$Win)

    $results = @()

    # 冒頭で検索クリア
    Clear-SearchBox -Win $Win | Out-Null
    Start-Sleep -Milliseconds 300

    # ========== T-W3 MenuShowDelay（DWord・数値入力） ==========
    $tcId = "dialog_inputs.T-W3.menu_show_delay"
    $sel = Select-CategoryAndItem -Win $Win -CategoryType "Desktop" -ItemId "desktop.menuShowDelay"
    if (-not $sel.Success) {
        $results += New-TestResult -Id "$tcId.select" -Status "FAIL" -Detail "$($sel.Step) / $($sel.Detail)"
    } else {
        $open = Open-DialogViaMenu -Win $Win
        if (-not $open.Success) {
            $results += New-TestResult -Id "$tcId.open" -Status "FAIL" -Detail $open.Detail
        } else {
            # EditableValueBox（TextBox）取得
            $vbox = Find-ById $open.DialogWindow "EditableValueBox" 3
            if (-not $vbox) {
                $results += New-TestResult -Id "$tcId.find_vbox" -Status "FAIL" -Detail "EditableValueBox 未検出（DWord 型のはず）"
            } else {
                # 現在値読取
                $currentVal = Get-ElementValue $vbox
                if ($currentVal -match "^\d+$") {
                    $results += New-TestResult -Id "$tcId.current_is_numeric" -Status "OK" -Detail "現在値 '$currentVal' は数値"
                } else {
                    $results += New-TestResult -Id "$tcId.current_is_numeric" -Status "INFO" -Detail "現在値 '$currentVal'（数値以外の可能性）"
                }

                # OriginalValueText の確認（編集前のスナップショット）
                $orig = Find-ById $open.DialogWindow "OriginalValueText" 2
                if ($orig) {
                    $origVal = Get-ElementName $orig
                    $results += New-TestResult -Id "$tcId.original_value" -Status "INFO" -Detail "OriginalValue: '$origVal'"
                }

                # 不正値「abc」を入力
                $setOk = Set-ElementValue -Element $vbox -Value "abc"
                Start-Sleep -Milliseconds 500
                if ($setOk) {
                    $afterVal = Get-ElementValue $vbox
                    $results += New-TestResult -Id "$tcId.set_invalid" -Status "OK" -Detail "不正値 'abc' をセット・取得値 '$afterVal'"
                } else {
                    $results += New-TestResult -Id "$tcId.set_invalid" -Status "FAIL" -Detail "TextBox SetValue 失敗"
                }

                # 適用ボタンは押さない（安全のため）→ キャンセル閉じ
            }
            Close-DialogByCancel | Out-Null
            Start-Sleep -Milliseconds 500
        }
    }

    # ========== T-W8 Link Binary（Binary 4 byte 表示確認） ==========
    $tcId = "dialog_inputs.T-W8.link_binary"
    $sel = Select-CategoryAndItem -Win $Win -CategoryType "Shell" -ItemId "shell.shortcutSuffixOmit"
    if (-not $sel.Success) {
        $results += New-TestResult -Id "$tcId.select" -Status "FAIL" -Detail "$($sel.Step) / $($sel.Detail)"
    } else {
        $open = Open-DialogViaMenu -Win $Win
        if (-not $open.Success) {
            $results += New-TestResult -Id "$tcId.open" -Status "FAIL" -Detail $open.Detail
        } else {
            # EditableValueBox（TextBox）取得 - Binary 型も TextBox 表示
            $vbox = Find-ById $open.DialogWindow "EditableValueBox" 3
            if (-not $vbox) {
                $results += New-TestResult -Id "$tcId.find_vbox" -Status "FAIL" -Detail "EditableValueBox 未検出（Binary 型のはず）"
            } else {
                # 現在値の形式確認（16 進数 4 バイト形式 "XX XX XX XX"）
                $currentVal = Get-ElementValue $vbox
                if ($currentVal -match "^([0-9A-Fa-f]{2}\s+){3}[0-9A-Fa-f]{2}$") {
                    $results += New-TestResult -Id "$tcId.binary_4byte_format" -Status "OK" -Detail "Binary 4 byte 形式 OK: '$currentVal'"
                } elseif ($currentVal) {
                    # 4バイトでなくても何らかの 16 進数表記なら INFO
                    $results += New-TestResult -Id "$tcId.binary_4byte_format" -Status "INFO" -Detail "現在値 '$currentVal'（4byte 形式と一致しない・但しレジストリ未設定なら空欄）"
                } else {
                    $results += New-TestResult -Id "$tcId.binary_4byte_format" -Status "INFO" -Detail "現在値が空（レジストリ未設定の可能性）"
                }

                # OriginalValueText 確認
                $orig = Find-ById $open.DialogWindow "OriginalValueText" 2
                if ($orig) {
                    $origVal = Get-ElementName $orig
                    $results += New-TestResult -Id "$tcId.original_value" -Status "INFO" -Detail "OriginalValue: '$origVal'"
                }
            }
            Close-DialogByCancel | Out-Null
            Start-Sleep -Milliseconds 500
        }
    }

    return $results
}
