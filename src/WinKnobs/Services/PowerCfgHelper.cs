using System;
using System.Text.RegularExpressions;

namespace WinKnobs.Services;

/// <summary>
/// powercfg 出力のロケール非依存パース（v0.2.1 Step2・純粋関数）。
///
/// ⚠️ plan-validator 致命1：書込先は `scheme_current` 文字列を使わず、
///   /getactivescheme で解決した実 GUID に固定する（カスタム/OEM プランや
///   Read と Write の間でプランが切り替わる TOCTOU を防ぐ）。GUID 解決失敗時は Apply 中止。
///
/// パース方針（日本語/英語どちらの Windows でも動く）：
///   - GUID は `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` 形式の正規表現で拾う（ラベル文言に依存しない）。
///   - /query の現在値は `0x` 付き 16 進だけが「現在の AC/DC 設定インデックス」の2行に現れる
///     （PERFBOOSTMODE は列挙型のため Min/Max 行が無い）。AC 行が DC 行より先に出る並びは
///     powercfg の固定仕様。よって 0x マッチの [^2]=AC / [^1]=DC とみなす。
/// </summary>
public static class PowerCfgHelper
{
    /// <summary>SUB_PROCESSOR（プロセッサ電源管理）のサブグループ GUID。</summary>
    public const string SubProcessorGuid = "54533251-82be-4824-96c1-47b60b740d00";

    /// <summary>PERFBOOSTMODE（プロセッサ パフォーマンス向上モード）の設定 GUID。</summary>
    public const string PerfBoostModeGuid = "be337238-0d82-4146-a960-4f3749d470c7";

    private static readonly Regex GuidRegex = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    private static readonly Regex HexIndexRegex = new(
        @"0x[0-9a-fA-F]{1,8}",
        RegexOptions.Compiled);

    /// <summary>
    /// `powercfg /getactivescheme` の出力からアクティブな電源プランの GUID を取り出す。
    /// 出力例（日本語）：「電源設定の GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (バランス)」
    /// </summary>
    public static bool TryParseActiveSchemeGuid(string? output, out string schemeGuid)
    {
        schemeGuid = "";
        if (string.IsNullOrWhiteSpace(output)) return false;
        var m = GuidRegex.Match(output);
        if (!m.Success) return false;
        schemeGuid = m.Value.ToLowerInvariant();
        return true;
    }

    /// <summary>
    /// `powercfg /qh <scheme> SUB_PROCESSOR PERFBOOSTMODE` の出力から現在の AC/DC インデックスを取り出す。
    /// ⚠️ /query でなく **/qh（隠し設定込み）**：PERFBOOSTMODE は既定で hidden 属性のため /query では
    /// 設定ブロック自体が出力されない（2026-06-11 VM/ホスト実機で確認）。
    /// 0x 付き 16 進は「現在の AC」「現在の DC」の2行のみ（「利用可能な設定のインデックス: 000」の
    /// 列挙行は 0x 無し＝実出力で確認済）。2件未満なら失敗。
    /// </summary>
    public static bool TryParseAcDcIndexes(string? output, out int acIndex, out int dcIndex)
    {
        acIndex = -1;
        dcIndex = -1;
        if (string.IsNullOrWhiteSpace(output)) return false;
        var matches = HexIndexRegex.Matches(output);
        if (matches.Count < 2) return false;
        try
        {
            acIndex = Convert.ToInt32(matches[matches.Count - 2].Value, 16);
            dcIndex = Convert.ToInt32(matches[matches.Count - 1].Value, 16);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
