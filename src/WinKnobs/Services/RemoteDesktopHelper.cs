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
    Failed,
    /// <summary>
    /// plan-validator（2026-06-06・ON 専用）：FW を開けた後に後続処理が失敗し、ロールバックでも
    /// 閉じ直せなかった＝**外部接続を受け付ける攻撃口が開いたまま**の危険状態。
    /// この時だけ 🔴強警告（「今すぐ『無効にする』を管理者で実行してください」）を出す。
    /// Enable() の done-stack 巻き戻し後の事後検証（fDeny=0 かつ FW-TCP 開）で判定する。
    /// </summary>
    LeftExposed
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
    ///
    /// ■ plan-validator（2026-06-06）：ServerStandard / IoTEnterprise / Cloud(Win11 SE) は接頭辞に一致せず false。
    ///   サーバ/IoT は本来 RDP ホスト可だが**過剰グレーアウト（実害小・安全側）**として許容。
    ///   Home 系を誤って通す穴は無い（誤って ON を活性化する危険方向の漏れはゼロ）。
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

    // ============================================================
    // v0.1.19 ON（有効化）用の純粋関数群
    //
    // ■ 原子性は C# 側 done-stack で担う（plan-validator 反映・2026-06-06）：
    //   単発 powershell の try/catch ロールバックは「catch 自身の失敗が握れない／-Command 内 throw の
    //   ExitCode が 0 素通りし得る／FW 開放後の巻き戻し失敗で攻撃口が開いたまま」になり原子性を担保できない。
    //   よって ON は <see cref="RemoteDesktopService"/> が **1ステップずつ powershell を実行し、成功分を
    //   done に積んで失敗時に逆順ロールバック**する（NetworkService.ApplyDoh 同型）。本クラスは各ステップの
    //   コマンド文字列生成と状態解析（I/O 無し）だけを担う＝完全テスト可能。
    // ============================================================

    /// <summary>NLA(ネットワークレベル認証) を持つレジストリキー（RDP-Tcp）。PS -LiteralPath 用（コロン有り）。</summary>
    public const string RdpTcpKeyPath =
        @"HKLM:\System\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp";

    /// <summary><see cref="RegistryService"/>.ReadValue 用（コロン無し）。NLA/PortNumber 読み取り。</summary>
    public const string RdpTcpKeyPathNoColon =
        @"HKLM\System\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp";

    /// <summary>fDeny 親キーの <see cref="RegistryService"/>.ReadValue 用（コロン無し）。</summary>
    public const string TerminalServerKeyPathNoColon =
        @"HKLM\System\CurrentControlSet\Control\Terminal Server";

    /// <summary>NLA 値名。1=必須(推奨・ON 時に強制) / 0=不要。REG_DWORD。</summary>
    public const string NlaValueName = "UserAuthentication";

    /// <summary>RDP 待ち受けポート値名（既定 3389）。REG_DWORD。</summary>
    public const string PortValueName = "PortNumber";

    /// <summary>
    /// HKLM の REG_DWORD を value に設定する単一ステップ PowerShell -Command。
    /// -ErrorAction Stop で非終了エラーも terminating 化し powershell.exe の ExitCode!=0 を保証。
    /// done-stack の1ステップとして C# から個別実行する（fDeny / NLA 共用）。
    /// </summary>
    public static string BuildSetDwordCommand(string psLiteralPath, string valueName, int value)
        => $"Set-ItemProperty -LiteralPath '{psLiteralPath}' -Name {valueName} -Value {value} -Type DWord -ErrorAction Stop";

    /// <summary>
    /// HKLM の REG_DWORD 値を削除する単一ステップ PowerShell -Command（ロールバック/復元の「既定へ戻す」用）。
    /// まず SilentlyContinue で削除 → なお残っていれば <c>throw</c>（powershell.exe の ExitCode!=0）で
    /// **削除失敗を確実に観測**する。値が元々無ければ no-op（冪等）。
    ///
    /// ■ S-1（2026-06-09 cross-reviewer）：done-stack 巻き戻しを forward と同じ PowerShell 経路に統一し、
    ///   C# Registry API（<see cref="RegistryService.DeleteValue"/> が失敗を握って false を返し throw しない）の
    ///   無言失敗で原子性が破れる穴を塞ぐ。これにより「fDeny=0 を消せなかったのに巻き戻し済みと誤報告」を防ぐ。
    /// </summary>
    public static string BuildRemoveValueCommand(string psLiteralPath, string valueName)
        => $"Remove-ItemProperty -LiteralPath '{psLiteralPath}' -Name {valueName} -ErrorAction SilentlyContinue; " +
           $"if (Get-ItemProperty -LiteralPath '{psLiteralPath}' -Name {valueName} -ErrorAction SilentlyContinue) {{ throw 'remove-value-failed' }}";

    /// <summary>
    /// 退避 JSON を <see cref="RemoteDesktopBackup"/> へ**厳密**にパースする（純粋関数・I/O 無し）。
    ///
    /// ■ E-1（2026-06-09 cross-reviewer）：<see cref="System.Text.Json.JsonSerializer"/> は <c>{}</c> やフィールド欠落を
    ///   例外でなく全 default(0/false) の record にしてしまう。それを「正常な退避」と誤認すると、ユーザーの実原状
    ///   （例：元々 ON）を破壊して無言で無効化する事故になる。よって**全フィールドが JSON に存在する場合のみ**
    ///   復元値を返し、破損（malformed / 欠落 / null / 空）は <c>null</c> を返す。呼び出し側は null を安全側
    ///   （無効へフォールバック＋通知）として扱う。
    /// </summary>
    public static RemoteDesktopBackup? TryParseBackup(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<BackupDto>(json);
            if (dto == null) return null;
            if (!dto.FDenyExisted.HasValue || !dto.FDenyValue.HasValue
                || !dto.FwTcpWasEnabled.HasValue || !dto.FwUdpWasEnabled.HasValue
                || !dto.NlaExisted.HasValue || !dto.NlaValue.HasValue)
                return null;
            return new RemoteDesktopBackup(
                dto.FDenyExisted.Value, dto.FDenyValue.Value,
                dto.FwTcpWasEnabled.Value, dto.FwUdpWasEnabled.Value,
                dto.NlaExisted.Value, dto.NlaValue.Value);
        }
        catch { return null; }
    }

    /// <summary>
    /// <see cref="TryParseBackup"/> 専用の中間 DTO。全フィールド nullable にして「JSON に存在したか」を
    /// HasValue で検査できるようにする（record の positional パラメータは欠落時に default が入り検知できないため）。
    /// プロパティ名は <see cref="RemoteDesktopBackup"/> のシリアライズ名（PascalCase）と一致させる。
    /// </summary>
    private sealed class BackupDto
    {
        public bool? FDenyExisted { get; set; }
        public int? FDenyValue { get; set; }
        public bool? FwTcpWasEnabled { get; set; }
        public bool? FwUdpWasEnabled { get; set; }
        public bool? NlaExisted { get; set; }
        public int? NlaValue { get; set; }
    }

    /// <summary>
    /// FW 規則を Enable/Disable する単一ステップ PowerShell -Command（-Name 安定ID・言語非依存）。
    /// names 省略時は TCP/UDP 両方（<see cref="FirewallRuleNames"/>）。ロールバックでは個別の規則名を渡す。
    /// </summary>
    public static string BuildFirewallCommand(bool enable, string[]? ruleNames = null)
    {
        var verb = enable ? "Enable-NetFirewallRule" : "Disable-NetFirewallRule";
        var target = ruleNames ?? FirewallRuleNames;
        var names = string.Join(",", System.Array.ConvertAll(target, n => $"'{n}'"));
        return $"{verb} -Name {names} -ErrorAction Stop";
    }

    /// <summary>
    /// 1つの FW 規則の Enabled 状態を読む PowerShell -Command（出力は <see cref="ParseFirewallEnabled"/> で解析）。
    /// 読み取りは非昇格で可能。
    /// </summary>
    public static string BuildReadFirewallCommand(string ruleName)
        => $"(Get-NetFirewallRule -Name '{ruleName}' -ErrorAction Stop).Enabled";

    /// <summary>
    /// Get-NetFirewallRule の `.Enabled` 出力から「有効か」を判定。
    /// PowerShell の Enabled enum は "True"/"False"。netsh 風 "Yes"/"はい"/"Enabled"/"1" も保険対応。
    /// 読めない/空/未知は **false（不明＝安全側で『有効でない』扱い）**。
    /// </summary>
    public static bool ParseFirewallEnabled(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;
        var s = output.Trim();

        // 明示 false 系を先に判定（過検出防止）。
        if (s.Equals("False", System.StringComparison.OrdinalIgnoreCase)
            || s.Equals("Disabled", System.StringComparison.OrdinalIgnoreCase)
            || s.Equals("No", System.StringComparison.OrdinalIgnoreCase)
            || s == "2"
            || s.Contains("いいえ"))
            return false;

        if (s.Contains("True", System.StringComparison.OrdinalIgnoreCase)
            || s.Contains("Enabled", System.StringComparison.OrdinalIgnoreCase)
            || s.Equals("Yes", System.StringComparison.OrdinalIgnoreCase)
            || s == "1"
            || s.Contains("はい"))
            return true;

        return false; // 未知の出力は安全側
    }

    /// <summary>
    /// RDP が「到達可能（有効）」か。fDeny=0 かつ FW(TCP) 有効。
    /// NLA は到達性の必須条件ではない（ラベルで別途注記）ため判定に含めない。
    /// </summary>
    public static bool IsRdpEnabled(int fDeny, bool fwTcpEnabled) => fDeny == 0 && fwTcpEnabled;

    /// <summary>
    /// 状態ラベル（到達性を保証しない but-caveat 込み）。nla: 1=必須 / 0=不要 / null=不明。
    /// fDeny と FW が食い違う片肺は「一部だけ有効（要注意）」として明示。
    /// </summary>
    public static string DescribeRdpState(int fDeny, bool fwTcpEnabled, int? nla)
    {
        string core;
        if (fDeny == 0 && fwTcpEnabled) core = "有効";
        else if (fDeny != 0 && !fwTcpEnabled) core = "無効";
        else core = "一部だけ有効（要注意）"; // OS 許可だが FW 閉、または その逆

        string nlaLabel = nla switch
        {
            1 => "NLA：必須",
            0 => "NLA：不要",
            _ => "NLA：不明"
        };
        return $"{core} / {nlaLabel}";
    }

    /// <summary>
    /// メニュー常設の「現在の状態」ラベル文言を組み立てる（v0.1.20・状態表示／グレーアウト常設ラベル）。
    /// 表示は読み取り専用 MenuItem（IsEnabled=false）に出す。優先順は **非対応 → ポリシー管理 → 実状態**。
    ///
    /// - 非対応エディション（Home 等）：そもそもホストになれない＝Enable/Disable はグレーアウト。理由を常設表示。
    /// - GPO/MDM 管理中：本ツールで変更不可＝同じくグレーアウト。理由を常設表示。
    /// - それ以外：<see cref="DescribeRdpState"/> の「有効/無効/一部だけ有効」＋NLA に待ち受けポートを添える。
    /// </summary>
    /// <param name="isHostCapable">RDP ホスト可エディションか（<see cref="IsRdpHostCapableEdition"/>）。</param>
    /// <param name="isPolicyManaged">GPO/MDM で fDeny が管理されているか。</param>
    /// <param name="fDeny">fDenyTSConnections（0=許可/ON・1=拒否/OFF）。</param>
    /// <param name="fwTcpEnabled">FW(TCP) 規則が有効か。</param>
    /// <param name="nla">UserAuthentication（1=必須/0=不要/null=不明）。</param>
    /// <param name="port">RDP 待ち受けポート（既定 3389）。</param>
    public static string DescribeMenuStatusLabel(
        bool isHostCapable, bool isPolicyManaged, int fDeny, bool fwTcpEnabled, int? nla, int port)
    {
        const string prefix = "現在の状態：";

        if (!isHostCapable)
            return prefix + "このエディションは非対応（Pro 以上が必要）";

        if (isPolicyManaged)
            return prefix + "ポリシー管理中（GPO/MDM・このツールでは変更不可）";

        var state = DescribeRdpState(fDeny, fwTcpEnabled, nla);
        return $"{prefix}{state}（ポート {port}）";
    }
}
