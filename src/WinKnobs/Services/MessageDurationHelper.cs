using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// v0.1.13 アクセシビリティ第5項目「通知を表示する時間」(MessageDuration) 用の純粋関数群。
/// I/O を持たず xUnit 対象。プリセット選択式（Command 型）。
///
/// レジストリ：HKCU\Control Panel\Accessibility\MessageDuration（REG_DWORD・秒・既定5）。
/// 設定 &gt; アクセシビリティ &gt; 視覚効果 &gt;「通知を表示する時間」と同じ。
///
/// 設計の肝（CaretWidth/TextScale と違う点）：
///   ラベルに「1分」「5分」が混在するため、ラベル先頭の数字を読む方式は使えない（"1分"→1秒 と誤変換する）。
///   ラベル⇔値は PresetSeconds とのインデックス対応で明示マッピングする（Scrollbar の ValueForLabel 流儀）。
///
/// plan-validator 反映：
///   - SecondsForLabel / NearestPresetLabel はともに同一の Choices 配列を走査・参照する（ラベルのリテラル直書き禁止
///     ＝表記ゆれ〔全角/半角〕で IndexOf 不一致→黙って既定化 する事故を構造的に排除）。
///   - PresetSeconds と Choices は同順・同数。先頭＝既定（5秒）。端値一致は PresetSeconds で直接照合（ラベル非依存）。
///   - 距離計算は long（int.MinValue 等の極端値オーバーフロー回避）。同距離は小さい方（先に出現）を採用。
///   - CoerceInt は共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲（REG_SZ 混在対応）。
/// </summary>
public static class MessageDurationHelper
{
    /// <summary>レジストリキー（HKCU・管理者不要）。</summary>
    public const string RegistryPath = @"HKCU\Control Panel\Accessibility";

    /// <summary>レジストリ値名（REG_DWORD・秒）。</summary>
    public const string ValueName = "MessageDuration";

    /// <summary>有効範囲の下限（Windows 既定＝5秒・未設定時もこの扱い）。</summary>
    public const int MinSeconds = 5;

    /// <summary>有効範囲の上限（設定アプリの最大＝5分）。</summary>
    public const int MaxSeconds = 300;

    /// <summary>秒数のプリセット（Choices と同順・同数）。5秒/7/15/30/1分/5分。</summary>
    public static IReadOnlyList<int> PresetSeconds { get; } = new[] { 5, 7, 15, 30, 60, 300 };

    /// <summary>ComboBox 表示ラベル（PresetSeconds と同順・同数）。先頭＝既定。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "5秒（既定）", "7秒", "15秒", "30秒", "1分", "5分"
    };

    /// <summary>値を有効範囲 [MinSeconds, MaxSeconds] に丸める。</summary>
    public static int ClampToRange(int value)
        => value < MinSeconds ? MinSeconds : (value > MaxSeconds ? MaxSeconds : value);

    /// <summary>
    /// ラベル→秒数。Choices を走査し一致したラベルの PresetSeconds[index] を返す。
    /// Choices に無いラベル（想定外）・null は既定 PresetSeconds[0]=5。
    /// ※ラベル先頭の数字を読む方式は「1分」「5分」を誤変換するため採用しない（明示マッピング）。
    /// </summary>
    public static int SecondsForLabel(string? label)
    {
        if (label == null) return PresetSeconds[0];
        for (int i = 0; i < Choices.Count; i++)
            if (Choices[i] == label) return PresetSeconds[i];
        return PresetSeconds[0];
    }

    /// <summary>
    /// 秒数→最も近いプリセットのラベル（必ず Choices 内の同一参照）。
    /// Read 専用：ComboBox の SelectedIndex 整合のため Choices に無い文字列は返さない。
    /// 同距離なら小さい方（先に出現したプリセット）を採用。
    /// </summary>
    public static string NearestPresetLabel(int seconds)
    {
        int bestIdx = 0;
        long bestDist = long.MaxValue;
        for (int idx = 0; idx < PresetSeconds.Count; idx++)
        {
            // long 化で (5 - int.MinValue) 等のオーバーフローを回避
            long dist = System.Math.Abs((long)PresetSeconds[idx] - seconds);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = idx;
            }
        }
        return Choices[bestIdx];
    }

    /// <summary>
    /// レジストリ生値→int（共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲）。REG_SZ "30" 等の混在環境でも数値化。
    /// existed 判定（v != null）は呼び出し側の責務。
    /// </summary>
    public static int CoerceInt(object? v, int fallback) => RegistryValueCoercion.CoerceInt(v, fallback);
}
