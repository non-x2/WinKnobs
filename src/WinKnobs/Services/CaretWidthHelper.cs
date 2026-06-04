using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// v0.1.12 アクセシビリティ第4項目「テキストカーソルの太さ」(CaretWidth) 用の純粋関数群。
/// I/O を持たず xUnit 対象。プリセット選択式（Command 型）のためのラベル⇔値変換・範囲丸め・最近接プリセット解決。
///
/// レジストリ：HKCU\Control Panel\Desktop\CaretWidth（REG_DWORD・1〜20px）。
/// 設定 &gt; アクセシビリティ &gt; テキスト カーソル &gt; テキスト カーソルの太さ と同じ値。
/// テキストカーソル（文字入力時に点滅する縦棒｜）の「太さ」であり、点滅速度（CaretBlinkRate）とは別。
/// Windows 既定はキー未設定（＝1px 相当）のことが多い。
///
/// plan-validator 反映（TextScaleHelper を踏襲）：
///   - NearestPresetLabel は Choices 配列から直接引く（戻り値は必ず Choices 内＝ComboBox 整合・黙って既定化を防ぐ）。
///   - Choices と PresetValues は同順・同数（インデックス対応）。先頭＝既定。
///   - 距離計算は long（int.MinValue 等の極端値オーバーフロー回避）。
///   - CoerceInt は共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲（REG_SZ/REG_DWORD 混在対応・v0.1.10/v0.1.11 の知見）。
///     異常型（REG_BINARY 等）や未設定は fallback=MinWidth に正規化される（実害小・既定1px へ寄せる）。
/// </summary>
public static class CaretWidthHelper
{
    /// <summary>レジストリキー（HKCU・管理者不要）。</summary>
    public const string RegistryPath = @"HKCU\Control Panel\Desktop";

    /// <summary>レジストリ値名（REG_DWORD）。</summary>
    public const string ValueName = "CaretWidth";

    /// <summary>有効範囲の下限（Windows 既定＝最も細い・未設定時もこの扱い）。</summary>
    public const int MinWidth = 1;

    /// <summary>有効範囲の上限。</summary>
    public const int MaxWidth = 20;

    /// <summary>px 実値のプリセット（Choices と同順・同数）。低い側を細かく＝アクセシビリティ重視。</summary>
    public static IReadOnlyList<int> PresetValues { get; } = new[] { 1, 2, 3, 4, 5, 10, 20 };

    /// <summary>ComboBox 表示ラベル（PresetValues と同順・同数）。先頭＝既定。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "1px（既定）", "2px", "3px", "4px", "5px", "10px", "20px"
    };

    /// <summary>値を有効範囲 [MinWidth, MaxWidth] に丸める。</summary>
    public static int ClampToRange(int value)
        => value < MinWidth ? MinWidth : (value > MaxWidth ? MaxWidth : value);

    /// <summary>
    /// ラベル先頭の整数（px 値）を取り出す。
    /// "3px"→3 / "1px（既定）"→1 / "10"(px なし)→10 / "  20px "→20。
    /// 先頭に数字が無い・空なら false。
    /// </summary>
    public static bool TryParseLabel(string? label, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(label)) return false;

        int i = 0;
        while (i < label.Length && char.IsWhiteSpace(label[i])) i++;   // 先頭空白スキップ
        int start = i;
        while (i < label.Length && char.IsDigit(label[i])) i++;        // 先頭の連続数字
        if (i == start) return false;                                  // 先頭に数字なし

        return int.TryParse(label.Substring(start, i - start), out value);
    }

    /// <summary>
    /// 任意の px 値に最も近いプリセットのラベルを返す（必ず Choices 内の文字列）。
    /// Read 専用：ComboBox の SelectedIndex 整合のため、Choices に無い文字列は返さない。
    /// 同距離なら小さい方（先に出現したプリセット）を採用。
    /// </summary>
    public static string NearestPresetLabel(int value)
    {
        int bestIdx = 0;
        long bestDist = long.MaxValue;
        for (int idx = 0; idx < PresetValues.Count; idx++)
        {
            // long 化で (1 - int.MinValue) 等のオーバーフローを回避
            long dist = System.Math.Abs((long)PresetValues[idx] - value);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = idx;
            }
        }
        return Choices[bestIdx];
    }

    /// <summary>
    /// レジストリ生値→int（共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲）。
    /// REG_SZ "3" 等の型混在環境でも数値化。existed 判定（v != null）は呼び出し側の責務。
    /// </summary>
    public static int CoerceInt(object? v, int fallback) => RegistryValueCoercion.CoerceInt(v, fallback);
}
