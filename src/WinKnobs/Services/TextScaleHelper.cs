using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// v0.1.9 アクセシビリティ「テキストのサイズ」(TextScaleFactor) 用の純粋関数群。
/// I/O を持たず xUnit 対象。プリセット選択式（ComboBox / Command 型）のための
/// ラベル⇔値変換・範囲丸め・最近接プリセット解決を提供する。
///
/// レジストリ：HKCU\Software\Microsoft\Accessibility\TextScaleFactor（REG_DWORD・100〜225）。
/// 設定 &gt; アクセシビリティ &gt; テキストのサイズ と同じ値（文字のみ拡大・画面拡大率/DPI とは別）。
///
/// plan-validator 反映：
///   - NearestPresetLabel は Choices 配列から直接引く（戻り値は必ず Choices に含まれる＝ComboBox 整合）。
///   - Choices と PresetValues は同順・同数（インデックス対応）。
///   - 距離計算は long で行い極端値（int.MinValue 等）のオーバーフローを回避。
/// </summary>
public static class TextScaleHelper
{
    /// <summary>レジストリキー（HKCU・管理者不要）。</summary>
    public const string RegistryPath = @"HKCU\Software\Microsoft\Accessibility";

    /// <summary>レジストリ値名（REG_DWORD）。</summary>
    public const string ValueName = "TextScaleFactor";

    /// <summary>有効範囲の下限（Windows 既定＝未設定時もこの扱い）。</summary>
    public const int MinScale = 100;

    /// <summary>有効範囲の上限。</summary>
    public const int MaxScale = 225;

    /// <summary>パーセント実値のプリセット（Choices と同順・同数）。</summary>
    public static IReadOnlyList<int> PresetValues { get; } = new[] { 100, 125, 135, 150, 175, 200, 225 };

    /// <summary>ComboBox 表示ラベル（PresetValues と同順・同数）。先頭＝既定。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "100%（既定）", "125%", "135%", "150%", "175%", "200%", "225%"
    };

    /// <summary>値を有効範囲 [MinScale, MaxScale] に丸める。</summary>
    public static int ClampToRange(int value)
        => value < MinScale ? MinScale : (value > MaxScale ? MaxScale : value);

    /// <summary>
    /// ラベル先頭の整数（パーセント値）を取り出す。
    /// "135%"→135 / "100%（既定）"→100 / "150"(% なし)→150 / "  175% "→175。
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
    /// 任意のパーセント値に最も近いプリセットのラベルを返す（必ず Choices 内の文字列）。
    /// Read 専用：ComboBox の SelectedIndex 整合のため、Choices に無い文字列は返さない。
    /// 同距離なら小さい方（先に出現したプリセット）を採用。
    /// </summary>
    public static string NearestPresetLabel(int value)
    {
        int bestIdx = 0;
        long bestDist = long.MaxValue;
        for (int idx = 0; idx < PresetValues.Count; idx++)
        {
            // long 化で (100 - int.MinValue) 等のオーバーフローを回避
            long dist = System.Math.Abs((long)PresetValues[idx] - value);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = idx;
            }
        }
        return Choices[bestIdx];
    }
}
