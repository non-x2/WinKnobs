using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// v0.3.11「最大のプロセッサの状態（PROCTHROTTLEMAX）」用の純粋関数群（system.processorPowerState）。
/// I/O を持たず xUnit 対象。powercfg 実行は MainViewModel 側（processorBoost と同型）。
///
/// PROCTHROTTLEMAX は「列挙インデックス」ではなく **パーセント値（0..100・単位%）**。
/// プリセット（100/90/80/70/60/50%）と percent↔ラベルを index 整合で対応させる。
/// 範囲外・プリセット外は呼び出し側でセンチネル表示にする（誤った既定表示を防ぐ）。
/// </summary>
public static class ProcMaxStateHelper
{
    /// <summary>プリセットのパーセント値（Choices と index 一致）。</summary>
    public static IReadOnlyList<int> Percents { get; } = new[] { 100, 90, 80, 70, 60, 50 };

    /// <summary>ComboBox 表示ラベル。index = Percents の index。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "100%（制限なし・最大）",                 // 0
        "90%",                                    // 1
        "80%",                                    // 2
        "70%（発熱/静音 やや優先）",              // 3
        "60%",                                    // 4
        "50%（強め制限・最も静かで低発熱）"        // 5
    };

    /// <summary>パーセントが有効範囲（1..100）か。</summary>
    public static bool IsValidPercent(int percent) => percent >= 1 && percent <= 100;

    /// <summary>
    /// パーセント→表示ラベル（必ず Choices 内の同一参照）。プリセットに無いパーセント・範囲外は null
    /// （呼び出し側が「変更しない」センチネル表示にする＝中途半端な値を既定プリセットに誤丸めしない）。
    /// </summary>
    public static string? LabelForPercent(int percent)
    {
        for (int i = 0; i < Percents.Count; i++)
            if (Percents[i] == percent) return Choices[i];
        return null;
    }

    /// <summary>
    /// ラベル→書き込むパーセント。Choices に無いラベル（センチネル/不正）は -1（＝書き込まない目印）。
    /// </summary>
    public static int PercentForLabel(string? label)
    {
        if (label == null) return -1;
        for (int i = 0; i < Choices.Count; i++)
            if (ReferenceEquals(Choices[i], label) || Choices[i] == label) return Percents[i];
        return -1;
    }
}
