using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// v0.4.8 バックグラウンドアプリ「一括無効化」用の純粋関数群（system.disableBackgroundApps）。
/// I/O を持たず xUnit 対象。Command 型2択（無効にする / 有効にする）。DisableCopilotHelper と同型。
///
/// レジストリ：HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications\GlobalUserDisabled
///   （REG_DWORD）。値が厳密に 1 の時のみ無効化。0・未設定・1以外の将来値はすべて「有効にする（既定）」として
///   扱う（境界値の誤 truthy 判定を防ぐ＝plan-validator 指摘反映）。管理者不要（HKCU）。
///   ※ 対象は UWP/Store アプリのバックグラウンド実行のみ（このキーは UWP バックグラウンドアクセス管理の
///     機構であるため）。Win32 アプリは対象外。
///   ※ 出典：GlobalUserDisabled は 2026-07 時点の Windows 11 で有効なコミュニティ文書由来のレジストリ仕様
///     （https://www.elevenforum.com/t/enable-or-disable-background-apps-in-windows-11.923/ ／
///     https://winaero.com/windows-11-disable-background-apps/ ）。learn.microsoft.com の公式文書は
///     全ユーザー向けポリシー LetAppsRunInBackground 側のみで、GlobalUserDisabled 自体はフォーラム実証ベース。
///   ※ 企業のグループポリシー管理下では、上位のポリシー設定（LetAppsRunInBackground）が優先され
///     本設定が効かない場合がある。
///
/// plan-validator 反映（DisableCopilotHelper 踏襲）：
///   - LabelForValue は必ず Choices[n] の同一参照を返す（リテラル直書き禁止＝黙って既定化を防ぐ）。
///   - IsDisabled は「== 1」の厳密一致のみ true（0/1 以外の将来値を誤って truthy 判定しない）。
///   - CoerceInt は共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲。
/// </summary>
public static class DisableBackgroundAppsHelper
{
    public const string RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
    public const string ValueName = "GlobalUserDisabled";

    /// <summary>有効にする＝Windows 既定（レジストリ 0・未設定も同挙動＝バックグラウンド実行を許可）。</summary>
    public const int ValueEnabled = 0;
    /// <summary>無効にする（レジストリ 1・全 UWP/Store アプリのバックグラウンド実行を停止）。</summary>
    public const int ValueDisabled = 1;

    /// <summary>ComboBox 表示ラベル。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "無効にする", "有効にする（既定）"
    };

    /// <summary>無効にするラベル＝Choices[0] の同一参照。</summary>
    public static string DisabledLabel => Choices[0];
    /// <summary>有効にするラベル＝Choices[1] の同一参照。</summary>
    public static string EnabledLabel => Choices[1];

    /// <summary>レジストリ値→「無効化されているか」。厳密に 1 の時のみ true（0・未設定・1以外は false）。</summary>
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
