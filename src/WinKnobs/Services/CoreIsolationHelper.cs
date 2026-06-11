namespace WinKnobs.Services;

/// <summary>
/// コア分離（メモリ整合性・HVCI）切り替えの結果（v0.2.1 Step4）。
/// レジストリ直接書込のため子プロセス分類は不要＝3分類。
/// </summary>
public enum CoreIsolationOutcome
{
    /// <summary>成功（再起動で確定）。</summary>
    Success,
    /// <summary>管理者権限不足（昇格が必要）。</summary>
    AccessDenied,
    /// <summary>その他の失敗。</summary>
    Failed
}

/// <summary>
/// v0.2.1「コア分離（メモリ整合性・HVCI）ON/OFF」用の定数＋純粋関数（I/O なし・xUnit 対象）。
/// 実行は <see cref="CoreIsolationService"/>（レジストリ DWORD 直接書込・要管理者・再起動反映）。
///
/// 設計（v0.2.1 設計ドラフト §2-5）：
///   - `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity` の
///     `Enabled`（DWORD 0/1）。同居値（WasEnabledBy 等）は触らない＝値単位。
///   - OFF＝セキュリティ後退方向（ドライバ悪用攻撃への防壁を外す）→ ConfirmDestructive 強確認。
///   - GPO/MDM 管理下（Policies\DeviceGuard 配下に HVCI ポリシー値あり）はグレーアウト（RDP IsPolicyManaged 同型）。
///   - UEFI ロック（Secured-core）環境では書いても再起動で戻る場合あり → 事後検証はせず
///     「再起動後に Windows セキュリティで確認」を案内（成功と言い切らない）。
/// </summary>
public static class CoreIsolationHelper
{
    /// <summary>HVCI 状態キー。</summary>
    public const string HvciKeyPath = @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";

    /// <summary>有効/無効を持つ値名（DWORD 0/1）。</summary>
    public const string EnabledValueName = "Enabled";

    /// <summary>GPO/MDM ポリシーキー（ここに HVCI ポリシー値があれば本ツールでは変更不可）。</summary>
    public const string PolicyKeyPath = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeviceGuard";

    /// <summary>ポリシー側の HVCI 値名（EnableVirtualizationBasedSecurity と並ぶ代表値）。</summary>
    public const string PolicyHvciValueName = "HypervisorEnforcedCodeIntegrity";

    /// <summary>
    /// 現在値（null=値なし・0=無効・1=有効）から表示用の状態文字列を作る（純粋関数）。
    /// 値なしは「無効（未設定）」扱い＝Windows セキュリティ UI の表示と整合。
    /// </summary>
    public static string DescribeState(int? enabledValue) => enabledValue switch
    {
        1 => "有効",
        0 => "無効",
        null => "無効（未設定）",
        _ => $"不明（{enabledValue}）"
    };

    /// <summary>有効=1 / 無効=0 の書込値（純粋関数・タイポ防止の一元化）。</summary>
    public static int ToRegistryValue(bool enable) => enable ? 1 : 0;
}
