using System;
using System.Text.RegularExpressions;

namespace WinKnobs.Services;

/// <summary>
/// Hyper-V（ハイパーバイザー起動）切り替えの結果（v0.2.1 Step5）。
/// bcdedit 子プロセス（PowerShell 経由）のため Sysmain 同様の3分類。
/// </summary>
public enum HyperVOutcome
{
    /// <summary>成功（再起動で確定）。</summary>
    Success,
    /// <summary>管理者権限不足（昇格が必要）。</summary>
    AccessDenied,
    /// <summary>その他の失敗。</summary>
    Failed
}

/// <summary>hypervisorlaunchtype の現在状態（bcdedit /enum {current} のパース結果）。</summary>
public enum HyperVLaunchType
{
    /// <summary>Auto＝ハイパーバイザーが OS 起動時に立ち上がる（Hyper-V 有効）。</summary>
    Auto,
    /// <summary>Off＝ハイパーバイザーを起動しない（LDPlayer 等のサードパーティ VT が使える）。</summary>
    Off,
    /// <summary>hypervisorlaunchtype 要素が未設定（OS 既定＝Hyper-V 未インストール時の素の状態）。</summary>
    NotConfigured,
    /// <summary>Auto/Off 以外の値（手動設定等・raw 文字列は別途保持）。</summary>
    Other,
    /// <summary>読取失敗（権限/環境）。</summary>
    Unknown
}

/// <summary>
/// v0.2.1「Hyper-V Platform ON/OFF」用の定数＋純粋関数群。I/O を持たず xUnit 対象。
/// 実行は <see cref="HyperVService"/>（PowerShell 経由 bcdedit・要管理者・再起動反映）。
///
/// 設計（v0.2.1 設計ドラフト §2-6＋🔧反映2/10）：
///   - 方式＝bcdedit 一本（`bcdedit /set hypervisorlaunchtype off|auto`）。DISM 機能削除は不採用
///     （VM 構成・vSwitch まで消えるリスク＋戻しが重い。bcdedit は機能を残したまま起動だけ止める＝完全可逆）。
///   - 実行は PowerShell 経由に統一（`&amp; bcdedit ...; exit $LASTEXITCODE`）＝既存 ElevationClassifier 資産を活かす（🔧反映10）。
///   - パースは `hypervisorlaunchtype` 行アンカー＋値トークン（bcdedit の要素名・値は日本語環境でも英語固定＝ロケール非依存。
///     説明文だけが翻訳される）。
///   - 🔴 OFF が「効かないのに成功扱い」になる経路（🔧反映2）：コア分離（VBS）が ON のままだと off でも
///     ハイパーバイザーが起動し続ける → ダイアログ側で依存ガイド＋成功文言は言い切らない（呼び出し側責務）。
/// </summary>
public static class HyperVHelper
{
    /// <summary>bcdedit の要素名（英語固定・ロケール非依存）。</summary>
    public const string LaunchTypeElementName = "hypervisorlaunchtype";

    /// <summary>有効方向の設定値。</summary>
    public const string LaunchTypeAuto = "Auto";

    /// <summary>無効方向の設定値。</summary>
    public const string LaunchTypeOff = "Off";

    // 復元で書き戻してよいトークンのホワイトリスト（cross-reviewer C-1・2026-06-11）。
    // hypervisorlaunchtype が受理するのは Auto/Off のみ。英字パターン（^[A-Za-z]+$）だと
    // bcdedit が受理しない値（手動設定の Other 値）も退避→「元に戻す」が永久失敗する
    // ＝退避の原子性「保存したら必ず戻せる」が崩れるため、書ける値だけを退避・復元対象にする。
    // （Other 検出時は Service 側が変更自体を中止する＝退避にも乗らない）
    private static readonly Regex RestorableTokenPattern = new(
        @"^(Auto|Off)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 行アンカー：行頭（空白許容）に要素名＋空白＋値トークン。説明文等の誤ヒットを防ぐ。
    private static readonly Regex LaunchTypeLinePattern = new(
        @"^\s*hypervisorlaunchtype\s+(\S+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    /// <summary>
    /// 現在のブートエントリ {current} を列挙するコマンド（読取・hypervisorlaunchtype 行のパース用）。
    /// PowerShell では {current} がスクリプトブロックに解釈されるためシングルクォートで文字列化する。
    /// </summary>
    public static string BuildReadCommand()
        => "& bcdedit /enum '{current}'; exit $LASTEXITCODE";

    /// <summary>
    /// hypervisorlaunchtype を auto/off に設定するコマンド（識別子省略＝{current} 対象）。
    /// </summary>
    public static string BuildSetLaunchTypeCommand(bool autoOn)
        => BuildSetLaunchTypeRawCommand(autoOn ? LaunchTypeAuto : LaunchTypeOff);

    /// <summary>
    /// hypervisorlaunchtype を退避トークンに設定するコマンド（退避の raw 値復元用）。
    /// Auto/Off 以外は例外（C-1＝bcdedit が受理しない値で復元が永久失敗するのを防ぐ・
    /// 退避 JSON 改ざん・破損によるコマンド注入への防衛線も兼ねる）。
    /// </summary>
    public static string BuildSetLaunchTypeRawCommand(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !RestorableTokenPattern.IsMatch(token))
            throw new ArgumentException($"hypervisorlaunchtype の値が不正です：{token}", nameof(token));
        return $"& bcdedit /set {LaunchTypeElementName} {token}; exit $LASTEXITCODE";
    }

    /// <summary>
    /// hypervisorlaunchtype 要素そのものを削除するコマンド（退避が「未設定」だった場合の復元用）。
    /// </summary>
    public static string BuildDeleteLaunchTypeCommand()
        => $"& bcdedit /deletevalue {LaunchTypeElementName}; exit $LASTEXITCODE";

    /// <summary>
    /// bcdedit /enum '{current}' の出力から hypervisorlaunchtype を読む（純粋関数・ロケール非依存）。
    /// 行が無い＝要素未設定（NotConfigured）。値は raw も返す（Other の表示・退避用）。
    /// </summary>
    public static (HyperVLaunchType Type, string? Raw) TryParseLaunchType(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return (HyperVLaunchType.Unknown, null);

        var m = LaunchTypeLinePattern.Match(output);
        if (!m.Success) return (HyperVLaunchType.NotConfigured, null);

        var raw = m.Groups[1].Value;
        if (string.Equals(raw, LaunchTypeAuto, StringComparison.OrdinalIgnoreCase))
            return (HyperVLaunchType.Auto, raw);
        if (string.Equals(raw, LaunchTypeOff, StringComparison.OrdinalIgnoreCase))
            return (HyperVLaunchType.Off, raw);
        return (HyperVLaunchType.Other, raw);
    }

    /// <summary>
    /// 実行結果を分類。ExitCode 0=Success／権限不足キーワード=AccessDenied／その他=Failed
    /// （Sysmain/WSL と同流儀・ElevationClassifier 一元化）。
    /// </summary>
    public static HyperVOutcome Classify(int exitCode, string? output)
    {
        if (exitCode == 0) return HyperVOutcome.Success;
        if (ElevationClassifier.LooksLikeAccessDenied(output))
            return HyperVOutcome.AccessDenied;
        return HyperVOutcome.Failed;
    }

    /// <summary>
    /// メニュー常設ラベル（v0.1.20 RDP 状態ラベルと同 UX・純粋関数）。
    /// 「再起動で反映」のため、ここで見えるのは bcdedit の設定値＝次回起動時の動き。
    /// </summary>
    public static string DescribeMenuStatusLabel(HyperVLaunchType type, string? raw)
    {
        const string prefix = "現在の状態：ハイパーバイザー起動＝";
        return type switch
        {
            HyperVLaunchType.Auto => prefix + "Auto（Hyper-V 有効）",
            HyperVLaunchType.Off => prefix + "Off（無効・LDPlayer 等が使用可）",
            HyperVLaunchType.NotConfigured => prefix + "未設定（OS 既定）",
            HyperVLaunchType.Other => prefix + $"{raw}（手動設定値）",
            _ => "現在の状態：取得できませんでした（管理者で起動すると表示されます）"
        };
    }

    /// <summary>
    /// 適用前の原状退避（first-touch・backup\maintenance\originals.json）。
    /// LaunchTypeExisted=false は「要素未設定」＝復元は /deletevalue で行う。
    /// </summary>
    public record HyperVBackup(bool LaunchTypeExisted, string LaunchTypeValue);

    /// <summary>
    /// 退避 JSON の厳密パース（RemoteDesktopHelper.TryParseBackup と同流儀・E-1 対策）。
    /// `{}`・フィールド欠落・破損は null（破損扱い）。JsonSerializer 直 deserialize は
    /// 破損を全 default の record にして「正常」と誤認するため使わない。
    /// </summary>
    public static HyperVBackup? TryParseBackup(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<BackupDto>(json);
            if (dto == null) return null;
            if (!dto.LaunchTypeExisted.HasValue || dto.LaunchTypeValue == null)
                return null;
            // 存在したのに復元可能トークン（Auto/Off）でない＝破損（C-1・復元時のコマンド注入も防ぐ）
            if (dto.LaunchTypeExisted.Value && !RestorableTokenPattern.IsMatch(dto.LaunchTypeValue))
                return null;
            return new HyperVBackup(dto.LaunchTypeExisted.Value, dto.LaunchTypeValue);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// <see cref="TryParseBackup"/> 専用の中間 DTO（全フィールド nullable で「JSON に存在したか」を検査）。
    /// プロパティ名は <see cref="HyperVBackup"/> のシリアライズ名（PascalCase）と一致させる。
    /// </summary>
    private sealed class BackupDto
    {
        public bool? LaunchTypeExisted { get; set; }
        public string? LaunchTypeValue { get; set; }
    }
}
