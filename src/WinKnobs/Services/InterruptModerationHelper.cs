using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinKnobs.Services;

/// <summary>
/// v0.4.6「割り込み加減（Interrupt Moderation）」用の純粋関数群（I/O なし・xUnit 対象）。
/// *InterruptModeration は NDIS 標準化キーワードで ValidRegistryValues={0,1} が実機確認済み
/// （0=無効／1=有効）＝NicPowerHelper の複数キーワード・ValidValues 動的ガードと異なり
/// 単一キーワード・固定値域のため一般化して安全に扱える。実行は <see cref="InterruptModerationService"/>。
/// JSON パースは NicPowerHelper の ExtractJsonObject／FlexibleListConverter（PS 5.1 単一要素展開吸収）を再利用。
/// </summary>
public static class InterruptModerationHelper
{
    public const string Keyword = "*InterruptModeration";
    public const string ResultMarker = "WINKNOBS_IM_RESULT";
    public const int ExitPartialOrFail = 4;

    public record AdapterValue(string Guid, string Value);

    /// <summary>全物理アダプタの現在値を1回で読むコマンド（本キーワード非対応アダプタは結果から除外）。</summary>
    public static string BuildReadCommand() =>
        "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; " +
        "$list = New-Object System.Collections.ArrayList; " +
        "Get-NetAdapter -Physical | ForEach-Object { " +
        $"  $p = Get-NetAdapterAdvancedProperty -Name $_.Name -RegistryKeyword '{Keyword}' -ErrorAction SilentlyContinue; " +
        "  if ($p) { [void]$list.Add([pscustomobject]@{ Guid=[string]$_.InterfaceGuid; Value=[string]@($p.RegistryValue)[0] }) } " +
        "}; [pscustomobject]@{ Adapters = $list } | ConvertTo-Json -Compress -Depth 4; exit 0";

    /// <summary>全物理アダプタへ一括適用するコマンド（value: 0=無効化／1=既定＝有効）。個別集計で一部失敗を握りつぶさない。</summary>
    public static string BuildApplyCommand(int value) =>
        "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; $ok=0; $fail=0; " +
        "Get-NetAdapter -Physical | ForEach-Object { " +
        $"  $p = Get-NetAdapterAdvancedProperty -Name $_.Name -RegistryKeyword '{Keyword}' -ErrorAction SilentlyContinue; " +
        "  if ($p) { " +
        $"    try {{ Set-NetAdapterAdvancedProperty -Name $_.Name -RegistryKeyword '{Keyword}' -RegistryValue '{value}' -NoRestart -ErrorAction Stop; $ok++ }} " +
        "    catch { $fail++ } " +
        "  } " +
        $"}}; Write-Output ('{ResultMarker} ok=' + $ok + ' fail=' + $fail); " +
        $"if ($fail -gt 0) {{ exit {ExitPartialOrFail} }} else {{ exit 0 }}";

    /// <summary>退避済みの原状（アダプタ GUID 単位）へ書き戻すコマンド（GUID から現在の Name を再解決）。</summary>
    public static string BuildRestoreCommand(IReadOnlyList<AdapterValue> original)
    {
        var sb = new StringBuilder();
        sb.Append("$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; $ok=0; $fail=0; ");
        foreach (var a in original)
        {
            var g = NicPowerHelper.EscapePsSingleQuote(a.Guid);
            var v = NicPowerHelper.EscapePsSingleQuote(a.Value);
            sb.Append(
                $"$n=(Get-NetAdapter -Physical | Where-Object {{ $_.InterfaceGuid -eq '{g}' }} | Select-Object -First 1).Name; " +
                $"if ($n) {{ try {{ Set-NetAdapterAdvancedProperty -Name $n -RegistryKeyword '{Keyword}' -RegistryValue '{v}' -NoRestart -ErrorAction Stop; $ok++ }} catch {{ $fail++ }} }}; ");
        }
        sb.Append($"Write-Output ('{ResultMarker} ok=' + $ok + ' fail=' + $fail); ");
        sb.Append($"if ($fail -gt 0) {{ exit {ExitPartialOrFail} }} else {{ exit 0 }}");
        return sb.ToString();
    }

    /// <summary>BuildReadCommand の JSON 出力をパース（想定外・破損は null）。対象アダプタ0件は空リスト。</summary>
    public static List<AdapterValue>? TryParseRead(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var payload = NicPowerHelper.ExtractJsonObject(output);
        if (payload == null) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<ReadDto>(payload);
            if (dto?.Adapters == null) return new List<AdapterValue>();
            return dto.Adapters
                .Where(a => !string.IsNullOrWhiteSpace(a.Guid) && a.Value != null)
                .Select(a => new AdapterValue(a.Guid!, a.Value!))
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>全アダプタが無効化(0)されているか（対象0件＝「既定」扱いで false）。</summary>
    public static bool AllDisabled(IReadOnlyList<AdapterValue> adapters)
        => adapters.Count > 0 && adapters.All(a => NicPowerHelper.NormalizeNumeric(a.Value) == "0");

    /// <summary>「WINKNOBS_IM_RESULT ok=N fail=M」行から件数を読む（純粋関数・無ければ null）。</summary>
    public static (int Ok, int Fail)? ParseCounts(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(output, ResultMarker + @"\s+ok=(\d+)\s+fail=(\d+)");
        if (!m.Success) return null;
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
    }

    private sealed class ReadDto
    {
        [JsonConverter(typeof(NicPowerHelper.FlexibleListConverter<AdapterDto>))]
        public List<AdapterDto>? Adapters { get; set; }
    }

    private sealed class AdapterDto
    {
        public string? Guid { get; set; }
        public string? Value { get; set; }
    }
}
