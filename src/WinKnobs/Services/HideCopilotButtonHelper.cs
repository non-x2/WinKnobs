using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// v0.4.7b デスクトップ「タスクバーの Copilot ボタンを非表示」用の純粋関数群（ShowCopilotButton・desktop.hideCopilotButton）。
/// I/O を持たず xUnit 対象。Command 型2択（表示する / 表示しない）。TaskManagerHelper と同型（判定方向が逆＝0 が「非表示」側）。
///
/// レジストリ：HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\ShowCopilotButton（REG_DWORD）。
///   未設定または 1 = 表示する（Windows 既定）／ 0 = 表示しない。管理者不要（HKCU）。
///   反映はエクスプローラ再起動（WinKnobs は自動再起動しない＝情報提示のみ・RestartScope.Explorer）。
///
/// plan-validator 反映（TaskManagerHelper 踏襲）：
///   - LabelForValue は必ず Choices[n] の同一参照を返す（リテラル直書き禁止＝黙って既定化を防ぐ）。
///   - IsHidden は「== 0」の厳密一致のみ true（0 以外はすべて「表示する」扱い＝安全側デフォルト）。
///   - CoerceInt は共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲。
/// </summary>
public static class HideCopilotButtonHelper
{
    public const string RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    public const string ValueName = "ShowCopilotButton";

    /// <summary>表示する＝Windows 既定（レジストリ 1・未設定も同挙動）。</summary>
    public const int ValueShown = 1;
    /// <summary>表示しない（レジストリ 0）。</summary>
    public const int ValueHidden = 0;

    /// <summary>ComboBox 表示ラベル。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "表示する（既定）", "表示しない"
    };

    /// <summary>表示するラベル＝Choices[0] の同一参照。</summary>
    public static string ShownLabel => Choices[0];
    /// <summary>表示しないラベル＝Choices[1] の同一参照。</summary>
    public static string HiddenLabel => Choices[1];

    /// <summary>レジストリ値→「非表示になっているか」。厳密に 0 の時のみ true（未設定・1・それ以外は false＝表示する）。</summary>
    public static bool IsHidden(int? value) => value == ValueHidden;

    /// <summary>ラベル→書き込むレジストリ値。「表示しない」→0／それ以外→1。</summary>
    public static int ValueForLabel(string? label)
        => label == HiddenLabel ? ValueHidden : ValueShown;

    /// <summary>レジストリ値→表示ラベル（必ず Choices 内の同一参照を返す）。</summary>
    public static string LabelForValue(int? value)
        => IsHidden(value) ? HiddenLabel : ShownLabel;

    /// <summary>
    /// レジストリ生値→int（共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲）。
    /// REG_SZ "0"/"1" 混在環境でも数値化。existed 判定（v != null）は呼び出し側の責務。
    /// </summary>
    public static int CoerceInt(object? v, int fallback) => RegistryValueCoercion.CoerceInt(v, fallback);
}
