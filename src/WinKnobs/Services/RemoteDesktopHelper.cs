namespace WinKnobs.Services;

/// <summary>
/// リモートデスクトップ（RDP）ON/OFF 切り替えの結果。SystemProtectionOutcome / MemoryCompressionOutcome と同流儀。
/// </summary>
public enum RemoteDesktopOutcome
{
    /// <summary>成功。</summary>
    Success,
    /// <summary>管理者権限不足（昇格が必要）。</summary>
    AccessDenied,
    /// <summary>その他の失敗。</summary>
    Failed
}

/// <summary>
/// v0.1.18 システム・メンテナンス「リモートデスクトップ ON/OFF」（Phase 4・🔴Red）用の純粋関数群。
/// I/O を持たず xUnit 対象。PowerShell コマンド組み立て／結果分類／エディション判定。
///
/// 実行は <see cref="RemoteDesktopService"/>。本クラスは「文字列を作る／文字列を分類する」だけ＝完全テスト可能。
///
/// ■ v0.1.18 のスコープは「無効化（OFF）」のみ（有効化 ON は v0.1.19）。OFF は攻撃面を閉じるだけ＝安全側。
///
/// ■ 実機照合で判明した重要事実（2026-06-05）：
///   netsh の `name=` / `group=` は安定IDでなく**ローカライズされた表示名**を取る（日本語環境では
///   「リモート デスクトップ - ユーザー モード (TCP 受信)」等）。よって安定ID指定の言語非依存な操作は netsh では不可。
///   → FW は PowerShell `Disable-NetFirewallRule -Name &lt;安定ID&gt;`（言語非依存）で叩く。fDeny もまとめて1つの
///   `powershell.exe -Command` で実行する（Phase 2/3 と同型）。結果、powershell.exe の ExitCode で分類でき、
///   <see cref="SystemProtectionHelper"/>/<see cref="MemoryCompressionHelper"/> と同じ Classify(exitCode, output) に統一できる。
/// </summary>
public static class RemoteDesktopHelper
{
    /// <summary>RDP の許可/拒否を決めるレジストリキー（fDenyTSConnections の親）。HKLM。</summary>
    public const string TerminalServerKeyPath = @"HKLM:\System\CurrentControlSet\Control\Terminal Server";

    /// <summary>RDP 許可フラグ。0=許可(ON) / 1=拒否(OFF・既定)。REG_DWORD。</summary>
    public const string FDenyValueName = "fDenyTSConnections";

    /// <summary>
    /// GPO/MDM がポリシーで RDP を強制している場合のキー（存在し値が設定されていれば本ツールでは変更不可）。
    /// ※こちらは <see cref="RegistryService"/>.ReadValue で読むため**コロン無し**形式（HKLM\...）。
    /// （PowerShell の -LiteralPath はコロン有り HKLM:\... なので <see cref="TerminalServerKeyPath"/> と形式が異なる点に注意）
    /// </summary>
    public const string PolicyKeyPath = @"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";

    /// <summary>
    /// 無効化対象の FW 規則の**安定 Name**（言語非依存・PowerShell -Name 指定で使う）。
    /// 標準「リモート デスクトップ」グループの UserMode TCP/UDP 受信規則。
    /// </summary>
    public static readonly string[] FirewallRuleNames =
    {
        "RemoteDesktop-UserMode-In-TCP",
        "RemoteDesktop-UserMode-In-UDP",
    };

    /// <summary>
    /// 無効化（OFF）の PowerShell -Command 文字列を組み立てる。
    /// 順序＝**外→内**（FW を先に閉じる → OS の fDeny を拒否に）。
    /// -ErrorAction Stop で非終了エラーも terminating 化し powershell.exe の ExitCode!=0 を保証。
    ///
    /// fail-closed：FW 無効化が成功した後に fDeny 書込が権限不足で止まっても、`;` 連結＋ErrorAction Stop により
    /// **スクリプトはそこで停止し、FW を開け直すロールバックは行わない**（防御方向に倒す）。ExitCode!=0 が呼び出し側に
    /// 「途中まで進んだ」を伝える。Disable-NetFirewallRule / Set-ItemProperty とも冪等（既に無効/1 でも成功）。
    /// </summary>
    public static string BuildDisableCommand()
    {
        var names = string.Join(",", System.Array.ConvertAll(FirewallRuleNames, n => $"'{n}'"));
        return
            $"Disable-NetFirewallRule -Name {names} -ErrorAction Stop; " +
            $"Set-ItemProperty -LiteralPath '{TerminalServerKeyPath}' -Name {FDenyValueName} -Value 1 -Type DWord -ErrorAction Stop";
    }

    /// <summary>
    /// 実行結果を分類。ExitCode 0=Success。出力に権限不足キーワードがあれば AccessDenied。その他は Failed。
    /// 権限不足キーワード集は <see cref="ElevationClassifier"/> に一元化（v0.1.17〜・3クラス共有）。
    /// </summary>
    public static RemoteDesktopOutcome Classify(int exitCode, string? output)
    {
        if (exitCode == 0) return RemoteDesktopOutcome.Success;

        if (ElevationClassifier.LooksLikeAccessDenied(output))
            return RemoteDesktopOutcome.AccessDenied;

        return RemoteDesktopOutcome.Failed;
    }

    /// <summary>
    /// このエディションが RDP ホスト（接続を受ける側）になれるか。ホワイトリスト方式。
    /// Pro / Enterprise / Education 系は可。Home（EditionID="Core" 系）は不可。
    /// 読取失敗・未知・空文字は **false（安全側＝機能を見せない/無効化）**。
    /// 入力は <see cref="EnvironmentInfoService"/> が非昇格で読む `EditionID`。
    /// </summary>
    public static bool IsRdpHostCapableEdition(string? editionId)
    {
        if (string.IsNullOrWhiteSpace(editionId)) return false;
        var e = editionId.Trim();

        // ホワイトリスト：既知のホスト可能エディションの接頭辞。
        // Professional* / Enterprise* / Education*（N・S・Workstation・Education 派生を包含）。
        string[] capablePrefixes = { "Professional", "Enterprise", "Education" };
        foreach (var p in capablePrefixes)
            if (e.StartsWith(p, System.StringComparison.OrdinalIgnoreCase))
                return true;

        // 念のため Workstation を明示（理論上 Professional 接頭辞で拾えるが安全網）。
        if (e.IndexOf("Workstation", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false; // Core(Home) / CoreSingleLanguage / Starter / 未知 / 空 → 非対応扱い
    }
}
