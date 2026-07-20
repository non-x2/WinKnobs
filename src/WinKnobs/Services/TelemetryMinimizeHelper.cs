using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// v0.4.9a プライバシー「診断データ送信を最小化」用の純粋関数群（privacy.telemetryMinimized）。
/// I/O を持たず xUnit 対象。Command 型2択（最小化する / 既定に戻す）。DisableCopilotHelper と同型。
///
/// レジストリ：HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry（REG_DWORD）。
///   値が厳密に 1 の時のみ「最小化済み」。0（Security・Enterprise/Education限定）・2（Enhanced）・3（Full）・
///   未設定はすべて「既定に戻す」として扱う（境界値の誤 truthy 判定を防ぐ＝plan-validator 指摘反映）。
///   管理者必須（HKLM）。
///   ※「最小化する」＝1 は Win11 Home/Pro で選べる実質下限（0 は Enterprise/Education 限定のため使わない）。
///   ※「既定に戻す」はポリシー未構成（値削除）に戻す。ポリシー不在が Windows 既定のため、
///     DisableCopilotHelper/DisableBackgroundAppsHelper の「既定=0書込」とは異なり削除が正しい
///     （MainViewModel 側の Apply で DeleteValueOrThrow を使用）。
///   ※ 設定アプリ「プライバシーとセキュリティ > 診断とフィードバック」との表示鮮度レースは
///     既存 Command 型ノブ共通の割り切りとして踏襲する。
///   ※ 企業のグループポリシー管理下では、上位のポリシー設定が優先される場合がある。
///
/// plan-validator 反映（DisableCopilotHelper 踏襲）：
///   - LabelForValue は必ず Choices[n] の同一参照を返す（リテラル直書き禁止＝黙って既定化を防ぐ）。
///   - IsMinimized は「== 1」の厳密一致のみ true（0/2/3 以外の将来値を誤って truthy 判定しない）。
///   - CoerceInt は共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲。
/// </summary>
public static class TelemetryMinimizeHelper
{
    public const string RegistryPath = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection";
    public const string ValueName = "AllowTelemetry";

    /// <summary>最小化する（レジストリ 1・Win11 Home/Pro の実質下限）。</summary>
    public const int ValueMinimized = 1;

    /// <summary>ComboBox 表示ラベル。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "最小化する", "既定に戻す"
    };

    /// <summary>最小化するラベル＝Choices[0] の同一参照。</summary>
    public static string MinimizedLabel => Choices[0];
    /// <summary>既定に戻すラベル＝Choices[1] の同一参照。</summary>
    public static string DefaultLabel => Choices[1];

    /// <summary>レジストリ値→「最小化されているか」。厳密に 1 の時のみ true（0・2・3・未設定は false）。</summary>
    public static bool IsMinimized(int? value) => value == ValueMinimized;

    /// <summary>レジストリ値→表示ラベル（必ず Choices 内の同一参照を返す）。</summary>
    public static string LabelForValue(int? value)
        => IsMinimized(value) ? MinimizedLabel : DefaultLabel;

    /// <summary>
    /// レジストリ生値→int（共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲）。
    /// REG_SZ "0"/"1" 混在環境でも数値化。existed 判定（v != null）は呼び出し側の責務。
    /// </summary>
    public static int CoerceInt(object? v, int fallback) => RegistryValueCoercion.CoerceInt(v, fallback);
}
