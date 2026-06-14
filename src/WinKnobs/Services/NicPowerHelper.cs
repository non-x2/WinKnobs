using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinKnobs.Services;

/// <summary>
/// NIC省電力プリセット切り替えの結果（v0.2.2 Step1）。
/// PowerShell（Set-NetAdapterAdvancedProperty / Set-NetAdapterPowerManagement）子プロセスの分類。
/// </summary>
public enum NicPowerOutcome
{
    /// <summary>全項目成功（即時反映・適用時に数秒の通信断あり）。</summary>
    Success,
    /// <summary>一部だけ成功（@plan-validator 致命1：途中失敗の握りつぶし防止）。</summary>
    PartiallyApplied,
    /// <summary>対象アダプタが見つからない（@plan-validator エッジ1：適用直前の差替 TOCTOU）。</summary>
    AdapterNotFound,
    /// <summary>管理者権限不足（昇格が必要）。</summary>
    AccessDenied,
    /// <summary>その他の失敗（全項目失敗）。</summary>
    Failed
}

/// <summary>
/// v0.2.2「NIC省電力プリセット」用の定数＋純粋関数群。I/O を持たず xUnit 対象。
/// 実行は <see cref="NicPowerService"/>（PowerShell 経由・要管理者・即時反映＝適用時に数秒通信断）。
///
/// 設計（v0.2.2 設計ドラフト §2-2＋🔧 plan-validator 反映）：
///   - 対象は省電力 RegistryKeyword の固定リスト。**実在し、かつ 0 が ValidRegistryValues に含まれる
///     項目だけ** 0 を書く（🔧反映2：機種により enum 値域・意味反転があり 0 が無効値のことがある）。
///   - 値は **文字列のまま厳密保持**（REG_SZ 格納・🔧反映3）。比較は数値正規化で `"00000000"`/`"0"` を吸収。
///   - アダプタ識別は Name でなく **InterfaceGuid**（🔧反映4：日本語アダプタ名の CP932 化け回避）。
///     適用直前に GUID で再解決＝差替 TOCTOU を検知（🔧反映エッジ1）。
///   - 各 Set- の失敗は **個別に捕捉**し「N項目中M成功」を構造化出力（🔧反映1：最後の成功が先行失敗を
///     0 で上書きするのを防ぐ）。
///   - 電源管理ロックは広範な Disable-NetAdapterPowerManagement でなく
///     **Set-NetAdapterPowerManagement -AllowComputerToTurnOffDevice Disabled**（チェックボックスのみ・
///     原状値へクリーン復元可。Wake-on-LAN 等の巻き添えを避ける）。
/// </summary>
public static class NicPowerHelper
{
    /// <summary>
    /// 無効化対象の省電力 RegistryKeyword（固定リスト・ドライバ差は「実在するものだけ」で吸収）。
    /// 先頭 `*` は NDIS 標準化キーワード。意味反転・enum 値域差があるため一律 0 でなく ValidValues ガード必須。
    /// </summary>
    public static readonly IReadOnlyList<string> PowerSavingKeywords = new[]
    {
        "*EEE",                      // 標準化：省電力イーサネット（Energy-Efficient Ethernet）
        "EeePhyEnable",              // ベンダ：EEE PHY
        "EEELinkAdvertisement",      // ベンダ：EEE リンクアドバタイズ
        "ULPMode",                   // 超低消費電力モード
        "AutoPowerSaveModeEnabled",  // 無動作時節電
        "GreenEthernet",             // グリーンイーサネット
        "EnableGreenEthernet",       // グリーンイーサネット（別名）
        "PowerSavingMode",           // 汎用省電力モード
    };

    /// <summary>省電力 OFF として書き込む値（文字列・REG_SZ）。</summary>
    public const string OffValue = "0";

    /// <summary>「対象アダプタが見つからない」マーカー（PowerShell 出力に埋める）。</summary>
    public const string AdapterNotFoundMarker = "WINKNOBS_ADAPTER_NOT_FOUND";

    /// <summary>構造化結果行のプレフィックス（ok/fail 件数）。</summary>
    public const string ResultMarker = "WINKNOBS_RESULT";

    // 終了コード（PowerShell 側 exit と一致させる）。
    public const int ExitAdapterNotFound = 3;
    public const int ExitPartialOrFail = 4;

    // ============ スナップショット（退避・復元の素材） ============

    /// <summary>1 つの省電力プロパティの原状（値は文字列のまま厳密保持・🔧反映3）。</summary>
    public record NicPowerProperty(string Keyword, string CurrentValue, IReadOnlyList<string> ValidValues);

    /// <summary>
    /// アダプタ 1 枚分の NIC省電力 原状退避。
    /// AllowComputerToTurnOffDevice は "Enabled"/"Disabled"／電源管理非対応アダプタは null（PM 手順スキップ）。
    /// </summary>
    public record NicPowerSnapshot(
        string AdapterName,
        string Guid,
        IReadOnlyList<NicPowerProperty> PowerSavingProps,
        string? AllowComputerToTurnOffDevice);

    // ============ コマンド構築 ============

    /// <summary>
    /// 現在の省電力プロパティ＋電源管理状態を JSON で 1 回取得するコマンド（読取・GUID 識別）。
    /// ConvertTo-Json で受ける＝表形式パース禁止（🔧反映5・ロケール非依存）。
    /// </summary>
    public static string BuildReadCommand(string interfaceGuid)
    {
        var g = EscapePsSingleQuote(interfaceGuid);
        return
            "$ErrorActionPreference='Stop'; " +
            $"$a = Get-NetAdapter -Physical | Where-Object {{ $_.InterfaceGuid -eq '{g}' }}; " +
            $"if (-not $a) {{ Write-Output '{AdapterNotFoundMarker}'; exit {ExitAdapterNotFound} }}; " +
            // 複数 NIC が同 GUID を返すことは無いが、念のため先頭を採る。
            "$a = @($a)[0]; " +
            "$adv = Get-NetAdapterAdvancedProperty -Name $a.Name -AllProperties -ErrorAction SilentlyContinue | " +
            "Select-Object RegistryKeyword, @{n='RegistryValue';e={[string[]]$_.RegistryValue}}, " +
            "@{n='ValidRegistryValues';e={[string[]]$_.ValidRegistryValues}}, DisplayName; " +
            "$pm = Get-NetAdapterPowerManagement -Name $a.Name -ErrorAction SilentlyContinue; " +
            "$allow = if ($pm) { [string]$pm.AllowComputerToTurnOffDevice } else { $null }; " +
            "[pscustomobject]@{ Name=$a.Name; Guid=[string]$a.InterfaceGuid; " +
            "Advanced=@($adv); AllowComputerToTurnOffDevice=$allow } | ConvertTo-Json -Compress -Depth 6; " +
            "exit 0";
    }

    /// <summary>
    /// スナップショットから「実際に 0 を書く省電力キーワード」を選ぶ（純粋関数・🔧反映2）。
    /// 条件：①0 が ValidValues に含まれる ②現在値が 0 でない（数値正規化で `"00000000"` も 0 扱い）。
    /// ValidValues が空（取得できなかった）の項目は安全側でスキップする（一律 0 書込を避ける）。
    /// </summary>
    public static IReadOnlyList<string> SelectKeywordsToApply(NicPowerSnapshot snapshot)
    {
        var result = new List<string>();
        foreach (var p in snapshot.PowerSavingProps)
        {
            if (p.ValidValues == null || p.ValidValues.Count == 0) continue;       // 値域不明＝触らない
            if (!p.ValidValues.Any(v => NormalizeNumeric(v) == "0")) continue;     // 0 が有効値でない
            if (NormalizeNumeric(p.CurrentValue) == "0") continue;                 // 既に 0（no-op）
            result.Add(p.Keyword);
        }
        return result;
    }

    /// <summary>
    /// 省電力 OFF を適用するコマンド。適用直前に GUID で再解決（🔧反映エッジ1・TOCTOU）。
    /// 各 Set- を個別 try/catch で集計し「ok=N fail=M」を構造化出力（🔧反映1）。
    /// </summary>
    /// <param name="interfaceGuid">対象アダプタの InterfaceGuid。</param>
    /// <param name="keywords">0 を書く省電力キーワード（<see cref="SelectKeywordsToApply"/> の結果）。</param>
    /// <param name="lockPowerManagement">電源オフ許可を Disabled にするか（現状 Enabled の時だけ true）。</param>
    public static string BuildApplyCommand(
        string interfaceGuid, IReadOnlyList<string> keywords, bool lockPowerManagement)
    {
        var sb = new StringBuilder();
        sb.Append("$ErrorActionPreference='Stop'; ");
        AppendResolveAdapter(sb, interfaceGuid);
        sb.Append("$ok=0; $fail=0; $errs=@(); ");

        foreach (var kw in keywords)
        {
            var k = EscapePsSingleQuote(kw);
            sb.Append(
                $"try {{ Set-NetAdapterAdvancedProperty -Name $a.Name -RegistryKeyword '{k}' " +
                $"-RegistryValue '{OffValue}' -NoRestart -ErrorAction Stop; $ok++ }} " +
                "catch { $fail++; $errs += $_.Exception.Message }; ");
        }

        if (lockPowerManagement)
        {
            sb.Append(
                "try { Set-NetAdapterPowerManagement -Name $a.Name -AllowComputerToTurnOffDevice Disabled " +
                "-ErrorAction Stop; $ok++ } catch { $fail++; $errs += $_.Exception.Message }; ");
        }

        AppendEmitResult(sb);
        return sb.ToString();
    }

    /// <summary>
    /// スナップショットから原状を書き戻すコマンド（復元・GUID 再解決）。
    /// 省電力プロパティは保存した文字列値をそのまま、電源管理は保存した Enabled/Disabled へ。
    /// 復元も個別集計（一部失敗を握りつぶさない）。
    /// </summary>
    public static string BuildRestoreCommand(NicPowerSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.Append("$ErrorActionPreference='Stop'; ");
        AppendResolveAdapter(sb, snapshot.Guid);
        sb.Append("$ok=0; $fail=0; $errs=@(); ");

        foreach (var p in snapshot.PowerSavingProps)
        {
            // 値域に無い原状値は書き戻さない（破損・他ツール改変からの防衛）。
            if (p.ValidValues != null && p.ValidValues.Count > 0 &&
                !p.ValidValues.Any(v => NormalizeNumeric(v) == NormalizeNumeric(p.CurrentValue)))
                continue;
            var k = EscapePsSingleQuote(p.Keyword);
            var v = EscapePsSingleQuote(p.CurrentValue);
            sb.Append(
                $"try {{ Set-NetAdapterAdvancedProperty -Name $a.Name -RegistryKeyword '{k}' " +
                $"-RegistryValue '{v}' -NoRestart -ErrorAction Stop; $ok++ }} " +
                "catch { $fail++; $errs += $_.Exception.Message }; ");
        }

        if (snapshot.AllowComputerToTurnOffDevice is "Enabled" or "Disabled")
        {
            var allow = snapshot.AllowComputerToTurnOffDevice;
            sb.Append(
                $"try {{ Set-NetAdapterPowerManagement -Name $a.Name -AllowComputerToTurnOffDevice {allow} " +
                "-ErrorAction Stop; $ok++ } catch { $fail++; $errs += $_.Exception.Message }; ");
        }

        AppendEmitResult(sb);
        return sb.ToString();
    }

    // ============ 結果のパース・分類 ============

    /// <summary>「WINKNOBS_RESULT ok=N fail=M」行から件数を読む（純粋関数・無ければ null）。</summary>
    public static (int Ok, int Fail)? ParseApplyCounts(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(
            output, ResultMarker + @"\s+ok=(\d+)\s+fail=(\d+)");
        if (!m.Success) return null;
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
    }

    /// <summary>
    /// 実行結果を分類。
    /// AdapterNotFound マーカー＝AdapterNotFound／ExitCode 0＝全成功 Success／
    /// ok&gt;0 かつ fail&gt;0＝PartiallyApplied（最優先で確定＝一部成功は昇格済みの証左ゆえ AccessDenied より先）／
    /// 権限不足キーワード＝AccessDenied（ok=0 のとき）／その他＝Failed。
    /// </summary>
    public static NicPowerOutcome Classify(int exitCode, string? output)
    {
        if (output != null && output.Contains(AdapterNotFoundMarker, StringComparison.Ordinal))
            return NicPowerOutcome.AdapterNotFound;

        if (exitCode == 0) return NicPowerOutcome.Success;

        // 一部成功（ok>0 かつ fail>0）は最も具体的＝AccessDenied 判定より先に確定する（重要②・誤分類防止）。
        // 1 つでも成功している以上「全部ダメ・要管理者」ではない（成功は昇格済みの証左）。
        // 失敗項目のエラー文に "管理者"/"administrator" 等が偶発混入しても PartiallyApplied を維持し、
        // 「N 件適用済み・元に戻すで戻せます」と正しく誘導する（AccessDenied は ok=0 のときだけ）。
        var counts = ParseApplyCounts(output);
        if (counts is { Ok: > 0, Fail: > 0 }) return NicPowerOutcome.PartiallyApplied;

        if (ElevationClassifier.LooksLikeAccessDenied(output))
            return NicPowerOutcome.AccessDenied;

        return NicPowerOutcome.Failed;
    }

    // ============ JSON パース（厳密 DTO・想定外は null） ============

    /// <summary>
    /// BuildReadCommand の JSON 出力からスナップショットを構築（厳密・想定外は null）。
    /// AdapterNotFound マーカーが混じる出力は呼び出し側が先に Classify で弾く前提だが、ここでも null を返す。
    /// 省電力キーワードに該当する Advanced 項目だけを抽出（実在するものだけ＝ドライバ差吸収）。
    /// </summary>
    public static NicPowerSnapshot? TryParseSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        if (json.Contains(AdapterNotFoundMarker, StringComparison.Ordinal)) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<SnapshotDto>(json);
            if (dto == null || string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Guid))
                return null;

            var keywordSet = new HashSet<string>(PowerSavingKeywords, StringComparer.OrdinalIgnoreCase);
            var props = new List<NicPowerProperty>();
            foreach (var adv in dto.Advanced ?? new List<AdvancedDto>())
            {
                if (string.IsNullOrWhiteSpace(adv.RegistryKeyword)) continue;
                if (!keywordSet.Contains(adv.RegistryKeyword)) continue;          // 省電力キーワードのみ
                var current = FirstOrEmpty(adv.RegistryValue);
                var valid = (adv.ValidRegistryValues ?? new List<string>())
                    .Where(v => v != null).Select(v => v!).ToList();
                props.Add(new NicPowerProperty(adv.RegistryKeyword, current, valid));
            }

            var allow = NormalizeAllow(dto.AllowComputerToTurnOffDevice);
            return new NicPowerSnapshot(dto.Name!, dto.Guid!, props, allow);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// スナップショットを退避 JSON にシリアライズ（maintenance ストア格納用・record をそのまま）。
    /// </summary>
    public static string SerializeBackup(NicPowerSnapshot snapshot)
        => JsonSerializer.Serialize(snapshot);

    /// <summary>退避 JSON を厳密にスナップショットへ戻す（破損は null・HyperVHelper.TryParseBackup 同流儀）。</summary>
    public static NicPowerSnapshot? TryParseBackup(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var dto = JsonSerializer.Deserialize<BackupDto>(json);
            if (dto == null || string.IsNullOrWhiteSpace(dto.AdapterName) || string.IsNullOrWhiteSpace(dto.Guid))
                return null;
            if (dto.PowerSavingProps == null) return null;

            var props = new List<NicPowerProperty>();
            foreach (var p in dto.PowerSavingProps)
            {
                if (string.IsNullOrWhiteSpace(p.Keyword) || p.CurrentValue == null) return null;
                props.Add(new NicPowerProperty(
                    p.Keyword, p.CurrentValue,
                    (p.ValidValues ?? new List<string>()).Where(v => v != null).Select(v => v!).ToList()));
            }
            var allow = NormalizeAllow(dto.AllowComputerToTurnOffDevice);
            return new NicPowerSnapshot(dto.AdapterName!, dto.Guid!, props, allow);
        }
        catch
        {
            return null;
        }
    }

    // ============ 表示用ラベル（純粋関数） ============

    /// <summary>「今の状態」表示（省電力項目数＋電源オフ許可）。2 軸を独立に出す（🔧反映エッジ2）。</summary>
    public static string DescribeStatus(NicPowerSnapshot? snapshot)
    {
        if (snapshot == null) return "（取得できませんでした）";
        var savingOn = snapshot.PowerSavingProps.Count(p => NormalizeNumeric(p.CurrentValue) != "0");
        var savingPart = snapshot.PowerSavingProps.Count == 0
            ? "省電力項目：このアダプタには該当設定なし"
            : $"省電力項目：{snapshot.PowerSavingProps.Count} 件中 {savingOn} 件が有効";
        var allowPart = snapshot.AllowComputerToTurnOffDevice switch
        {
            "Enabled" => "電源オフ許可：ON（⚠️ 切断対策には OFF 推奨）",
            "Disabled" => "電源オフ許可：OFF",
            _ => "電源オフ許可：（非対応）"
        };
        return savingPart + " / " + allowPart;
    }

    // ============ 内部ヘルパー ============

    private static void AppendResolveAdapter(StringBuilder sb, string interfaceGuid)
    {
        var g = EscapePsSingleQuote(interfaceGuid);
        sb.Append($"$a = Get-NetAdapter -Physical | Where-Object {{ $_.InterfaceGuid -eq '{g}' }}; ");
        sb.Append($"if (-not $a) {{ Write-Output '{AdapterNotFoundMarker}'; exit {ExitAdapterNotFound} }}; ");
        sb.Append("$a = @($a)[0]; ");
    }

    private static void AppendEmitResult(StringBuilder sb)
    {
        sb.Append($"Write-Output ('{ResultMarker} ok=' + $ok + ' fail=' + $fail); ");
        sb.Append("if ($errs.Count -gt 0) { Write-Output ($errs -join ' | ') }; ");
        sb.Append($"if ($fail -gt 0) {{ exit {ExitPartialOrFail} }} else {{ exit 0 }}");
    }

    /// <summary>PowerShell シングルクォート文字列のエスケープ（' を '' に）。注入防止。</summary>
    public static string EscapePsSingleQuote(string? s)
        => (s ?? string.Empty).Replace("'", "''");

    /// <summary>
    /// 数値文字列を正規化して比較可能にする（`"00000000"`/`" 0 "`/`"0x0"`→"0"・🔧反映3）。
    /// 数値として読めない値は trim のみ（元文字列の同一性で比較）。
    /// </summary>
    public static string NormalizeNumeric(string? s)
    {
        var t = (s ?? string.Empty).Trim();
        if (t.Length == 0) return string.Empty;
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(t.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var hex))
            return hex.ToString();
        if (long.TryParse(t, out var dec)) return dec.ToString();
        return t;
    }

    private static string FirstOrEmpty(List<string>? values)
        => values != null && values.Count > 0 && values[0] != null ? values[0] : string.Empty;

    private static string? NormalizeAllow(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();
        if (t.Equals("Enabled", StringComparison.OrdinalIgnoreCase)) return "Enabled";
        if (t.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) return "Disabled";
        // NetAdapterPowerManagement の enum は数値で来ることもある（1=Enabled/0=Disabled の報告）。
        var n = NormalizeNumeric(t);
        if (n == "1") return "Enabled";
        if (n == "0") return "Disabled";
        return null;   // 想定外は「非対応」扱い（PM 手順スキップ）
    }

    // ---- JSON DTO（読取・退避の厳密パース用） ----

    private sealed class SnapshotDto
    {
        public string? Name { get; set; }
        public string? Guid { get; set; }
        // PS 5.1 の ConvertTo-Json は単一要素配列をスカラーに展開する → 1 項目のみの NIC で
        // List への直接 deserialize が失敗する。Flex コンバータで「オブジェクト単体／配列」両方を吸収。
        [JsonConverter(typeof(FlexibleListConverter<AdvancedDto>))]
        public List<AdvancedDto>? Advanced { get; set; }
        public string? AllowComputerToTurnOffDevice { get; set; }
    }

    private sealed class AdvancedDto
    {
        public string? RegistryKeyword { get; set; }
        [JsonConverter(typeof(FlexibleListConverter<string>))]
        public List<string>? RegistryValue { get; set; }
        [JsonConverter(typeof(FlexibleListConverter<string>))]
        public List<string>? ValidRegistryValues { get; set; }
        public string? DisplayName { get; set; }
    }

    /// <summary>
    /// 「スカラー単体／配列」どちらの JSON でも List&lt;T&gt; に読む（PS 5.1 単一要素展開の吸収）。
    /// </summary>
    private sealed class FlexibleListConverter<T> : JsonConverter<List<T>>
    {
        public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var list = new List<T>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var item = JsonSerializer.Deserialize<T>(ref reader, options);
                    if (item != null) list.Add(item);
                }
                return list;
            }
            var single = JsonSerializer.Deserialize<T>(ref reader, options);
            return single == null ? new List<T>() : new List<T> { single };
        }

        public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value, options);
    }

    private sealed class BackupDto
    {
        public string? AdapterName { get; set; }
        public string? Guid { get; set; }
        public List<PropDto>? PowerSavingProps { get; set; }
        public string? AllowComputerToTurnOffDevice { get; set; }
    }

    private sealed class PropDto
    {
        public string? Keyword { get; set; }
        public string? CurrentValue { get; set; }
        public List<string>? ValidValues { get; set; }
    }
}
