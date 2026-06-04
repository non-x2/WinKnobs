using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// v0.1.14 システム・メンテナンス「タスクマネージャを使えるようにする」用の純粋関数群（DisableTaskMgr）。
/// I/O を持たず xUnit 対象。Command 型2択（使える / 無効にする）。ScrollbarHelper と同型。
///
/// レジストリ：HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System\DisableTaskMgr（REG_DWORD）。
///   0（または未設定）= 使える（Windows 既定）／ 1 = タスクマネージャを無効化（グループポリシー相当）。
///   ※ Policies\System キーは既定で存在しないことが多いが RegistryService.WriteValue が CreateSubKey で作る。
///   ※ 管理者不要（HKCU・ユーザー単位のポリシー）。
///
/// plan-validator 反映（ScrollbarHelper 踏襲）：
///   - LabelForValue は必ず Choices[n] の同一参照を返す（リテラル直書き禁止＝黙って既定化を防ぐ）。
///   - CoerceInt は共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲（REG_SZ "0"/"1" 混在対応）。
/// </summary>
public static class TaskManagerHelper
{
    public const string RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System";
    public const string ValueName = "DisableTaskMgr";

    /// <summary>使える＝Windows 既定（レジストリ 0・未設定も同挙動）。</summary>
    public const int ValueEnabled = 0;
    /// <summary>無効にする（レジストリ 1）。</summary>
    public const int ValueDisabled = 1;

    /// <summary>ComboBox 表示ラベル（先頭＝既定）。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "使える（既定）", "無効にする"
    };

    /// <summary>使えるラベル＝Choices[0] の同一参照。</summary>
    public static string EnabledLabel => Choices[0];
    /// <summary>無効にするラベル＝Choices[1] の同一参照。</summary>
    public static string DisabledLabel => Choices[1];

    /// <summary>レジストリ値→「無効化されているか」。1=true／0・未設定(null)=false。</summary>
    public static bool IsDisabled(int? value) => value == ValueDisabled;

    /// <summary>ラベル→書き込むレジストリ値。「無効にする」→1／それ以外→0。</summary>
    public static int ValueForLabel(string? label)
        => label == DisabledLabel ? ValueDisabled : ValueEnabled;

    /// <summary>レジストリ値→表示ラベル（必ず Choices 内の同一参照を返す）。</summary>
    public static string LabelForValue(int? value)
        => IsDisabled(value) ? DisabledLabel : EnabledLabel;

    /// <summary>
    /// レジストリ生値→int（共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲）。
    /// REG_SZ "0"/"1" 混在環境でも数値化。existed 判定（v != null）は呼び出し側の責務。
    /// </summary>
    public static int CoerceInt(object? v, int fallback) => RegistryValueCoercion.CoerceInt(v, fallback);
}
