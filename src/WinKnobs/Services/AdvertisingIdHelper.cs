using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// v0.4.9b プライバシー「広告 ID を無効化」用の純粋関数群（privacy.advertisingIdDisabled）。
/// I/O を持たず xUnit 対象。Command 型2択（無効化する / 既定に戻す）。DisableCopilotHelper と同型。
///
/// レジストリ：HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo\Enabled（REG_DWORD）。
///   値が厳密に 0 の時のみ「無効化済み」。未設定・1・その他の値はすべて「既定に戻す（未適用）」として
///   扱う（境界値の誤 truthy 判定を防ぐ＝plan-validator 指摘反映）。管理者不要（HKCU）。
///   ※ plan-validator 指摘により Boolean 型は禁止：Boolean の復元は「キー不在」マーカー経由の
///     キーごと削除になり、同居する広告 ID 実体 `Id` 値を巻き添えにする（2026-06-10 事例の再発防止）。
///     Command 2択＋値単位（Enabled 単体）のトークン復元とする（MainViewModel 側の Restore で
///     DeleteValueOrThrow を使用＝Id 値は保持される）。
///
/// plan-validator 反映（DisableCopilotHelper 踏襲）：
///   - LabelForValue は必ず Choices[n] の同一参照を返す（リテラル直書き禁止＝黙って既定化を防ぐ）。
///   - IsDisabled は「== 0」の厳密一致のみ true（1 以外の将来値を誤って truthy 判定しない）。
///   - CoerceInt は共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲。
/// </summary>
public static class AdvertisingIdHelper
{
    public const string RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo";
    public const string ValueName = "Enabled";

    /// <summary>既定に戻す＝Windows 既定（レジストリ 1・未設定も同挙動＝広告 ID を許可）。</summary>
    public const int ValueEnabled = 1;
    /// <summary>無効化する（レジストリ 0）。</summary>
    public const int ValueDisabled = 0;

    /// <summary>ComboBox 表示ラベル。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "無効化する", "既定に戻す"
    };

    /// <summary>無効化するラベル＝Choices[0] の同一参照。</summary>
    public static string DisabledLabel => Choices[0];
    /// <summary>既定に戻すラベル＝Choices[1] の同一参照。</summary>
    public static string EnabledLabel => Choices[1];

    /// <summary>レジストリ値→「無効化されているか」。厳密に 0 の時のみ true（未設定・1・その他は false）。</summary>
    public static bool IsDisabled(int? value) => value == ValueDisabled;

    /// <summary>ラベル→書き込むレジストリ値。「無効化する」→0／それ以外→1。</summary>
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
