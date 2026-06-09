using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace WinKnobs.Services;

/// <summary>
/// 現在の RDP 状態スナップショット（v0.1.19・ReadStatus が返す）。
/// ON 確認ダイアログ（§4）に「今 ON/OFF・NLA・ポート・自分がリモートか・LAN IP・ユーザー名」を提示するための材料。
///
/// plan-validator #4（2026-06-06）：各項目は <see cref="RemoteDesktopService.ReadStatus"/> で**独立 try** で読み、
/// 読めない項目は安全側の既定（fDeny は 1=無効／NLA は null=不明／FW は false）で返す。一部が読めなくても
/// ReadStatus 全体は落とさない（ダイアログのコンテキスト提示を全滅させない）。
/// </summary>
/// <param name="FDeny">fDenyTSConnections（0=許可/ON・1=拒否/OFF）。読めない時は安全側 1。</param>
/// <param name="FDenyExisted">fDeny 値が実在したか（退避時の「元々キーが無かった」判定用）。</param>
/// <param name="FwTcpEnabled">FW 規則 RemoteDesktop-UserMode-In-TCP が有効か。読めない→false。</param>
/// <param name="FwUdpEnabled">FW 規則 RemoteDesktop-UserMode-In-UDP が有効か。読めない→false。</param>
/// <param name="Nla">UserAuthentication（1=必須/0=不要）。ACL で読めない環境は null=不明。</param>
/// <param name="Port">RDP 待ち受けポート（既定 3389）。</param>
/// <param name="Edition">EditionID（軽量取得）。</param>
/// <param name="IsHostCapable">RDP ホスト可エディションか。</param>
/// <param name="IsPolicyManaged">GPO/MDM で fDeny が管理されているか。</param>
/// <param name="IsCurrentSessionRemote">この操作セッションがリモート（RDP 経由）か。</param>
/// <param name="LanIPv4">このPCの LAN IPv4 一覧（169.254/ループバック除外）。</param>
/// <param name="UserName">現在のユーザー名。</param>
public record RdpStatus(
    int FDeny,
    bool FDenyExisted,
    bool FwTcpEnabled,
    bool FwUdpEnabled,
    int? Nla,
    int Port,
    string Edition,
    bool IsHostCapable,
    bool IsPolicyManaged,
    bool IsCurrentSessionRemote,
    IReadOnlyList<string> LanIPv4,
    string UserName)
{
    /// <summary>到達可能（有効）か＝fDeny=0 かつ FW(TCP) 有効。NLA は到達性条件でない。</summary>
    public bool IsEnabled => RemoteDesktopHelper.IsRdpEnabled(FDeny, FwTcpEnabled);

    /// <summary>状態ラベル（「有効/無効/一部だけ有効」＋NLA）。to-caveat 込み。</summary>
    public string StateLabel => RemoteDesktopHelper.DescribeRdpState(FDeny, FwTcpEnabled, Nla);
}

/// <summary>
/// first-touch（WinKnobs が初めて触る前）の RDP 原状退避（§3）。ユーザーの元の姿勢を壊さず戻すための record。
/// JSON で <see cref="BackupService.SaveMaintenanceOriginalIfAbsent"/>（backup\maintenance\originals.json）に保存。
///
/// *Existed=false（元々キーが無かった）なら復元時は**削除して既定へ**戻す（無かったものを 0 で作らない）。
/// </summary>
/// <param name="FDenyExisted">退避時に fDeny 値が実在したか。</param>
/// <param name="FDenyValue">退避時の fDeny 値（Existed=false 時は無意味）。</param>
/// <param name="FwTcpWasEnabled">退避時に TCP 規則が有効だったか（ロールバック対象外判定）。</param>
/// <param name="FwUdpWasEnabled">退避時に UDP 規則が有効だったか。</param>
/// <param name="NlaExisted">退避時に NLA 値が実在したか（不明=未取得は false 扱い）。</param>
/// <param name="NlaValue">退避時の NLA 値（Existed=false 時は無意味）。</param>
public record RemoteDesktopBackup(
    bool FDenyExisted,
    int FDenyValue,
    bool FwTcpWasEnabled,
    bool FwUdpWasEnabled,
    bool NlaExisted,
    int NlaValue);

/// <summary>
/// v0.1.18 で**無効化（OFF）**、v0.1.19 で**有効化（ON）＋状態読み取り＋退避/復元**を担うサービス
/// （システム・メンテナンス Phase 4・🔴Red）。
///
/// 文字列の組み立て・分類・エディション判定は <see cref="RemoteDesktopHelper"/>（純粋関数・xUnit 済）に委譲。
/// 本サービスは「実行」と、純粋関数で表現できない判定（GPO 管理／自セッションがリモートか／ホスト対応エディションか）を担う。
///
/// ■ ON の原子性は **C# 側 done-stack** で担う（plan-validator #1・2026-06-06）：
///   単発 powershell の try/catch ロールバックは「catch 自身の失敗を握れない／-Command 内 throw の ExitCode が 0
///   素通りし得る／FW 開放後の巻き戻し失敗で攻撃口が開いたまま」になり原子性を担保できない。よって
///   <see cref="Enable"/> は NLA=1 / fDeny=0 / FW Enable を**1ステップずつ**実行し、成功分を done に積んで失敗時に
///   逆順ロールバックする（<see cref="NetworkService.ApplyDoh"/> と同型）。結果は4分岐に拡張：
///   Success / AccessDenied / Failed / **LeftExposed**（巻き戻し後も到達可能＝攻撃口が開いたまま）。
///
/// ■ 退避は network ストアと**別系統**の backup\maintenance\originals.json（plan-validator #2）。
///   NetworkService.RevertAll の対象外であることを構造で明示し、孤児 UX 事故・道連れ破損を断つ。
/// </summary>
public class RemoteDesktopService
{
    private readonly RegistryService _registry;
    private readonly EnvironmentInfoService _env;
    private readonly BackupService _backup;

    /// <summary>退避ストア（backup\maintenance\originals.json）内の機能キー。</summary>
    public const string FeatureKey = "remoteDesktop";

    /// <summary>FW 規則の安定 Name（言語非依存・個別操作用）。</summary>
    private const string FwTcpRule = "RemoteDesktop-UserMode-In-TCP";
    private const string FwUdpRule = "RemoteDesktop-UserMode-In-UDP";

    public RemoteDesktopService(RegistryService registry, EnvironmentInfoService env, BackupService backup)
    {
        _registry = registry;
        _env = env;
        _backup = backup;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    /// <summary>このセッションがリモート（RDP 経由）か（MS 公式の判定法）。</summary>
    private const int SM_REMOTESESSION = 0x1000;

    /// <summary>
    /// 現在の WinKnobs 操作セッションが**リモートデスクトップ経由**か。
    /// GetSystemMetrics(SM_REMOTESESSION)（MS 公式：リモートセッション判定）。0以外＝リモート。
    /// 判定できない例外時は安全側で true（＝強警告を出す方向）に倒す。
    /// </summary>
    public bool IsCurrentSessionRemote()
    {
        try { return GetSystemMetrics(SM_REMOTESESSION) != 0; }
        catch { return true; } // 取れない＝危険側に倒す（締め出しガードを必ず出す）
    }

    /// <summary>
    /// RDP が GPO/MDM ポリシーで管理（強制）されているか。
    /// ポリシーキーに fDenyTSConnections 値が存在すれば、`Control\Terminal Server` を書いても効かない／戻されるため
    /// 本ツールでは変更不可。存在すれば true。
    ///
    /// ■ 既知の限界（cross-reviewer 重要①・2026-06-06）：本判定は fDenyTSConnections ポリシーのみを見る。
    ///   GPO/MDM が**ファイアウォールを別経路で管理**している環境では、本ツールが Defender FW の該当規則を
    ///   無効化／有効化しても GPO が規則を別管理しており、攻撃面が想定どおりに開閉しない場合がある。
    ///   （ON では「ポリシー非管理と誤判定→有効化→GPO に即戻される」が起こりうる＝ダイアログに注記。）
    /// </summary>
    public bool IsPolicyManaged()
    {
        // ReadValue<object> は値が無ければ null。DWORD があれば boxed int が返る。
        var v = _registry.ReadValue<object>(RemoteDesktopHelper.PolicyKeyPath, RemoteDesktopHelper.FDenyValueName);
        return v != null;
    }

    /// <summary>
    /// この PC が RDP ホスト（接続を受ける側）になれるエディションか。
    /// 既存 <see cref="EnvironmentInfoService"/> の EditionID を非昇格で読み、ホワイトリスト判定（Home は不可）。
    /// </summary>
    public bool IsHostCapableEdition()
        => RemoteDesktopHelper.IsRdpHostCapableEdition(_env.Collect().Edition);

    // ============================================================
    // ReadStatus（状態読み取り・非昇格可・plan-validator #4：各項目独立 try）
    // ============================================================

    /// <summary>
    /// 現在の RDP 状態スナップショットを読む（非昇格可）。
    /// plan-validator #4：各項目を独立 try で囲み、読めない項目は安全側既定（fDeny=1／NLA=null/不明／FW=false）で返す。
    /// 特に NLA(UserAuthentication) は ACL が厳しく**非昇格で読めない環境がある**ため、読取失敗＝「不明（null）」で返す。
    /// </summary>
    public RdpStatus ReadStatus()
    {
        // fDeny（読めない/不在は安全側 1=無効）
        int fDeny = 1;
        bool fDenyExisted = false;
        try
        {
            var v = _registry.ReadValue<object>(
                RemoteDesktopHelper.TerminalServerKeyPathNoColon, RemoteDesktopHelper.FDenyValueName);
            if (v != null)
            {
                fDenyExisted = true;
                if (int.TryParse(v.ToString(), out var n)) fDeny = n;
            }
            else
            {
                fDeny = 1; // キー無し＝RDP 無効（既定）が安全側で正しい
            }
        }
        catch { fDeny = 1; fDenyExisted = false; }

        // NLA（ACL で読めない環境あり＝不明 null）
        int? nla = null;
        try
        {
            var v = _registry.ReadValue<object>(
                RemoteDesktopHelper.RdpTcpKeyPathNoColon, RemoteDesktopHelper.NlaValueName);
            if (v != null && int.TryParse(v.ToString(), out var n)) nla = n;
        }
        catch { nla = null; } // ACL 拒否（TrustedInstaller/SYSTEM 寄り ACL）→不明で返す

        // Port（読めない/不在は既定 3389）
        int port = 3389;
        try
        {
            var v = _registry.ReadValue<object>(
                RemoteDesktopHelper.RdpTcpKeyPathNoColon, RemoteDesktopHelper.PortValueName);
            if (v != null && int.TryParse(v.ToString(), out var p) && p > 0) port = p;
        }
        catch { /* 既定 3389 のまま */ }

        // FW（TCP/UDP・独立読み取り・読めない→false）
        bool fwTcp = false, fwUdp = false;
        try { (fwTcp, fwUdp) = ReadFirewallEnabled(); }
        catch { fwTcp = false; fwUdp = false; }

        // Edition（軽量取得＝full Collect() を避ける・plan-validator #6）
        var edition = ReadEditionIdLight();
        bool hostCapable;
        try { hostCapable = RemoteDesktopHelper.IsRdpHostCapableEdition(edition); }
        catch { hostCapable = false; }

        bool policy;
        try { policy = IsPolicyManaged(); }
        catch { policy = false; }

        bool remote;
        try { remote = IsCurrentSessionRemote(); }
        catch { remote = true; } // 不明は安全側（締め出しガードを出す方向）

        var lan = GetLanIPv4();

        string user;
        try { user = Environment.UserName; }
        catch { user = ""; }

        return new RdpStatus(
            fDeny, fDenyExisted, fwTcp, fwUdp, nla, port,
            edition, hostCapable, policy, remote, lan, user);
    }

    /// <summary>
    /// FW 規則 TCP/UDP の Enabled 状態を 1 回の PowerShell で読む（非昇格可）。
    /// 規則が存在しない環境でも落ちないよう -ErrorAction SilentlyContinue。各行を "TCP=" / "UDP=" 接頭辞で
    /// 明示出力し、$null（規則不在）でも行が消えず TCP/UDP の対応がズレない。解析は <see cref="RemoteDesktopHelper.ParseFirewallEnabled"/>。
    /// </summary>
    private (bool tcp, bool udp) ReadFirewallEnabled()
    {
        var cmd =
            $"$t=(Get-NetFirewallRule -Name '{FwTcpRule}' -ErrorAction SilentlyContinue).Enabled; 'TCP=' + $t; " +
            $"$u=(Get-NetFirewallRule -Name '{FwUdpRule}' -ErrorAction SilentlyContinue).Enabled; 'UDP=' + $u";

        int ec = RunPowerShell(cmd, out var output);
        if (ec != 0 && string.IsNullOrWhiteSpace(output)) return (false, false);

        bool tcp = false, udp = false;
        foreach (var raw in (output ?? string.Empty).Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("TCP=", StringComparison.OrdinalIgnoreCase))
                tcp = RemoteDesktopHelper.ParseFirewallEnabled(line.Substring(4));
            else if (line.StartsWith("UDP=", StringComparison.OrdinalIgnoreCase))
                udp = RemoteDesktopHelper.ParseFirewallEnabled(line.Substring(4));
        }
        return (tcp, udp);
    }

    /// <summary>EditionID を軽量取得（full Collect() を避ける・plan-validator #6）。読めなければ空文字。</summary>
    private string ReadEditionIdLight()
    {
        try
        {
            return _registry.ReadValue<string>(
                @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID") ?? "";
        }
        catch { return ""; }
    }

    /// <summary>LAN IPv4 一覧（Up の物理/仮想 IF・ループバック/トンネル/APIPA 169.254 を除外）。読めなければ空。</summary>
    private static IReadOnlyList<string> GetLanIPv4()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback
                    || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = ua.Address.ToString();
                    if (ip.StartsWith("169.254", StringComparison.Ordinal)) continue; // APIPA link-local
                    if (!list.Contains(ip)) list.Add(ip);
                }
            }
        }
        catch { /* 読めない時は空一覧 */ }
        return list;
    }

    // ============================================================
    // Enable（有効化・done-stack 原子化・plan-validator #1）
    // ============================================================

    /// <summary>
    /// リモートデスクトップを**有効化**する（要管理者権限）。
    /// 適用順＝内→外（NLA=1 → fDeny=0 → FW Enable）。途中失敗は done-stack を逆順ロールバック。
    /// 巻き戻し後も到達可能なら <see cref="RemoteDesktopOutcome.LeftExposed"/>（🔴強警告）。
    ///
    /// FW は TCP 必須・UDP は best-effort（欠如/失敗は警告に留め ON 成立／UDP は体感品質用）。
    /// 適用前に現状3値を first-touch 退避（backup\maintenance\originals.json）。退避保存に失敗したら**有効化を中止**
    /// （退避無しの ON は事故＝戻せなくなる・§0-3）。
    /// </summary>
    /// <param name="errorMessage">失敗/警告メッセージ（完全成功時は null）</param>
    /// <returns>Success / AccessDenied / Failed / LeftExposed</returns>
    public RemoteDesktopOutcome Enable(out string? errorMessage)
    {
        errorMessage = null;

        // ① 現状読み取り（退避とロールバックの基準）
        RdpStatus status;
        try { status = ReadStatus(); }
        catch (Exception ex)
        {
            errorMessage = $"現在の状態の読み取りに失敗したため、有効化を中止しました：{ex.Message}";
            return RemoteDesktopOutcome.Failed;
        }

        // ② first-touch 退避（保存失敗は ON 中止＝戻せない ON を作らない）
        var backup = new RemoteDesktopBackup(
            FDenyExisted: status.FDenyExisted,
            FDenyValue: status.FDeny,
            FwTcpWasEnabled: status.FwTcpEnabled,
            FwUdpWasEnabled: status.FwUdpEnabled,
            NlaExisted: status.Nla.HasValue,
            NlaValue: status.Nla ?? 0);
        try
        {
            _backup.SaveMaintenanceOriginalIfAbsent(FeatureKey, JsonSerializer.Serialize(backup));
        }
        catch (Exception ex)
        {
            errorMessage = $"退避の保存に失敗したため、安全のため有効化を中止しました：{ex.Message}";
            return RemoteDesktopOutcome.Failed;
        }

        // ③ done-stack（成功分の巻き戻しアクションを逆順実行）
        var rollback = new List<Action>();

        // Step1（内）：NLA=1（安全側を先に固める＝認証必須にしてから開ける）
        var r1 = RunStep(
            RemoteDesktopHelper.BuildSetDwordCommand(
                RemoteDesktopHelper.RdpTcpKeyPath, RemoteDesktopHelper.NlaValueName, 1),
            out var e1);
        if (r1 != RemoteDesktopOutcome.Success)
            return RollbackAndClassify(rollback, r1, e1, fDenyMayBeZeroed: false, out errorMessage);
        rollback.Add(() => RestoreNlaReg(backup));

        // Step2：fDeny=0（OS 上で接続許可）
        var r2 = RunStep(
            RemoteDesktopHelper.BuildSetDwordCommand(
                RemoteDesktopHelper.TerminalServerKeyPath, RemoteDesktopHelper.FDenyValueName, 0),
            out var e2);
        // Step2 が失敗＝fDeny=0 は書かれていない（Set-ItemProperty -ErrorAction Stop は原子的）→ fDenyMayBeZeroed:false
        if (r2 != RemoteDesktopOutcome.Success)
            return RollbackAndClassify(rollback, r2, e2, fDenyMayBeZeroed: false, out errorMessage);
        rollback.Add(() => RestoreFDenyReg(backup));

        // Step3（外）：FW-TCP を Enable（必須・最後に外部口を開ける）
        var r3 = RunStep(
            RemoteDesktopHelper.BuildFirewallCommand(true, new[] { FwTcpRule }),
            out var e3);
        // Step3 失敗時は Step2 成功済＝fDeny=0 が書かれている → 事後検証不能なら安全側 LeftExposed（fDenyMayBeZeroed:true）
        if (r3 != RemoteDesktopOutcome.Success)
            return RollbackAndClassify(rollback, r3, e3, fDenyMayBeZeroed: true, out errorMessage);
        // 退避時に元々開いていた規則はロールバック対象外（元ON分を誤って閉じない・plan-validator #1）
        if (!backup.FwTcpWasEnabled)
            rollback.Add(() => RunPowerShell(
                RemoteDesktopHelper.BuildFirewallCommand(false, new[] { FwTcpRule }), out _));

        // Step3b：FW-UDP を Enable（best-effort・体感品質用。欠如/失敗は ON 成立のまま警告に留める）
        // ※ S-2（2026-06-09 cross-reviewer）：UDP 成功分は rollback に積まない＝**これが最終ステップで、以降に
        //   失敗して巻き戻す経路が無い**ため。将来この後ろにステップを足す時は、!backup.FwUdpWasEnabled の閉じ直しを
        //   rollback に push すること（UDP 開けっ放しの設計負債を防ぐ）。
        var r4 = RunStep(
            RemoteDesktopHelper.BuildFirewallCommand(true, new[] { FwUdpRule }),
            out var e4);
        if (r4 != RemoteDesktopOutcome.Success)
            errorMessage = $"有効化は成功しましたが、UDP 規則の有効化に失敗しました" +
                           $"（体感品質用・接続自体は可能）：{e4}";

        return RemoteDesktopOutcome.Success;
    }

    /// <summary>
    /// done-stack を逆順ロールバックし、結果を分類する。
    /// 巻き戻し自体の失敗は内側 try/catch で握る（plan-validator #1）。巻き戻し後に**到達可能**（fDeny=0 かつ FW-TCP 開）が
    /// 残れば <see cref="RemoteDesktopOutcome.LeftExposed"/>（🔴攻撃口が開いたまま）。それ以外は元の失敗分類を返す。
    /// </summary>
    /// <param name="fDenyMayBeZeroed">
    /// この巻き戻し時点で fDeny=0（OS 側 接続許可）が**既に書かれている可能性**があるか
    /// （＝Step2 成功後の失敗）。事後検証ができない時の安全側判定（E-2）に使う。
    /// </param>
    private RemoteDesktopOutcome RollbackAndClassify(
        List<Action> rollback, RemoteDesktopOutcome failOutcome, string? failMsg,
        bool fDenyMayBeZeroed, out string? errorMessage)
    {
        bool rollbackHadFailure = false;
        for (int i = rollback.Count - 1; i >= 0; i--)
        {
            try { rollback[i](); }
            catch { rollbackHadFailure = true; } // 巻き戻し失敗は事後検証/保守的判定（下）へ委ねる
        }

        // 事後検証：巻き戻し後も「到達可能」または「OS 側許可(fDeny=0)が残存」なら攻撃口が開いたまま＝LeftExposed。
        // S-1（2026-06-09 cross-reviewer）：到達性(fDeny=0 かつ FW)だけでなく fDeny=0 単独残存も警告する
        // （FW を GPO 等が別管理する環境では fDeny=0 残存が露出に化けうる＝fail-safe）。
        try
        {
            var post = ReadStatus();
            bool reachable = RemoteDesktopHelper.IsRdpEnabled(post.FDeny, post.FwTcpEnabled);
            bool osPermitResidue = post.FDeny == 0; // 巻き戻し失敗で OS 側許可が残った
            if (reachable || osPermitResidue)
            {
                errorMessage = reachable
                    ? "外部接続を受け付ける状態のまま失敗しました。" +
                      "今すぐ『無効にする』を管理者で実行するか、Windows設定で確認してください。"
                    : "巻き戻しが完全に終わらず、OS 側の接続許可（fDeny=0）が残った可能性があります。" +
                      "今すぐ『無効にする』を管理者で実行してください。";
                return RemoteDesktopOutcome.LeftExposed;
            }
        }
        catch
        {
            // E-2（2026-06-09 cross-reviewer）：事後検証ができない。fDeny=0 を書いた後の失敗で巻き戻しも失敗した
            // 可能性があるなら、露出見逃しより過警告を選び安全側で LeftExposed を返す。
            if (fDenyMayBeZeroed && rollbackHadFailure)
            {
                errorMessage = "巻き戻し後の状態を確認できませんでした。OS 側の接続許可が残っている可能性があるため、" +
                               "今すぐ『無効にする』を管理者で実行してください。";
                return RemoteDesktopOutcome.LeftExposed;
            }
        }

        errorMessage = failMsg;
        return failOutcome;
    }

    // ============================================================
    // Disable（無効化・v0.1.18・OFF・fail-closed）
    // ============================================================

    /// <summary>
    /// リモートデスクトップを**無効化**する（要管理者権限）。
    /// FW 規則 Disable → fDeny=1 の順（外→内・fail-closed）。冪等（既に無効でも成功）。
    /// </summary>
    /// <param name="errorMessage">失敗時のエラーメッセージ（成功時は null）</param>
    /// <returns>結果（Success / AccessDenied / Failed）</returns>
    public RemoteDesktopOutcome Disable(out string? errorMessage)
    {
        int ec = RunPowerShell(RemoteDesktopHelper.BuildDisableCommand(), out var combined);
        errorMessage = ec == 0 ? null : combined;
        return RemoteDesktopHelper.Classify(ec, combined);
    }

    // ============================================================
    // RestoreRemoteDesktop（first-touch 復元・§3）
    // ============================================================

    /// <summary>
    /// 退避（first-touch）から RDP を**元の姿勢へ復元**する（要管理者権限）。
    /// 安全順 FW → NLA → fDeny（§3）。途中失敗は §3.1：安全側（fDeny=1・FW 閉）へ寄せたうえで例外送出
    /// （退避は呼び出し側が残し、管理者で再試行可能にする）。退避破損（§3.2）も同様に安全側へ倒して例外送出。
    ///
    /// *Existed=false（元々キーが無かった）なら**削除して既定へ**戻す（無かったものを作らない）。
    /// </summary>
    /// <param name="json">退避ストアの remoteDesktop ペイロード（<see cref="RemoteDesktopBackup"/> の JSON）</param>
    public void RestoreRemoteDesktop(string json)
    {
        // E-1（2026-06-09 cross-reviewer）：厳密パース。`{}` や欠落フィールドは null（破損扱い）で返る。
        // JsonSerializer 直 deserialize は破損を全 default の record にして「正常」と誤認するため使わない。
        var b = RemoteDesktopHelper.TryParseBackup(json);

        if (b == null)
        {
            // §3.2 退避破損：ともかく安全側（fDeny=1・FW 閉）へ
            try { SafeFallbackToDisabled(); } catch { /* best-effort */ }
            throw new InvalidOperationException("退避情報が読めず、安全のため『無効』にしました。");
        }

        Exception? firstError = null;

        // 安全順 FW → NLA → fDeny。各ステップ独立に試し、最初のエラーを保持。
        try { RestoreFirewallTo(b); } catch (Exception ex) { firstError ??= ex; }
        try { RestoreNlaReg(b); } catch (Exception ex) { firstError ??= ex; }
        try { RestoreFDenyReg(b); } catch (Exception ex) { firstError ??= ex; }

        if (firstError != null)
        {
            // §3.1 部分失敗：安全側へ寄せられる範囲で寄せる（露出側中間状態を放置しない）
            try { SafeFallbackToDisabled(); } catch { /* best-effort */ }
            throw new InvalidOperationException(
                "完全復元に失敗しました。安全のため『無効』寄りに留めました。管理者で再実行してください。", firstError);
        }
    }

    /// <summary>FW を退避時の姿勢へ戻す（TCP は必須＝失敗で throw・UDP は best-effort）。</summary>
    private void RestoreFirewallTo(RemoteDesktopBackup b)
    {
        int tcpEc = RunPowerShell(
            RemoteDesktopHelper.BuildFirewallCommand(b.FwTcpWasEnabled, new[] { FwTcpRule }), out var tcpOut);
        if (tcpEc != 0)
            throw new InvalidOperationException($"FW(TCP) の復元に失敗しました：{tcpOut}");

        // UDP は best-effort（体感品質用・失敗しても致命でない）
        RunPowerShell(
            RemoteDesktopHelper.BuildFirewallCommand(b.FwUdpWasEnabled, new[] { FwUdpRule }), out _);
    }

    /// <summary>
    /// NLA を退避時の値へ戻す（Existed=false なら削除＝既定へ）。
    /// S-1（2026-06-09 cross-reviewer）：forward と同じ **PowerShell 経路**で実行し、失敗は ExitCode で検知して throw。
    /// C# Registry API（DeleteValue が無言 false）の握り潰しで巻き戻し失敗を見逃す穴を塞ぐ。
    /// </summary>
    private void RestoreNlaReg(RemoteDesktopBackup b)
    {
        string cmd = b.NlaExisted
            ? RemoteDesktopHelper.BuildSetDwordCommand(
                RemoteDesktopHelper.RdpTcpKeyPath, RemoteDesktopHelper.NlaValueName, b.NlaValue)
            : RemoteDesktopHelper.BuildRemoveValueCommand(
                RemoteDesktopHelper.RdpTcpKeyPath, RemoteDesktopHelper.NlaValueName);
        int ec = RunPowerShell(cmd, out var outp);
        if (ec != 0)
            throw new InvalidOperationException($"NLA の復元に失敗しました：{outp}");
    }

    /// <summary>
    /// fDeny を退避時の値へ戻す（Existed=false なら削除＝「キー無し＝RDP 無効」既定へ＝安全側）。
    /// S-1（2026-06-09 cross-reviewer）：forward と同じ **PowerShell 経路**で実行し、失敗は ExitCode で検知して throw。
    /// </summary>
    private void RestoreFDenyReg(RemoteDesktopBackup b)
    {
        string cmd = b.FDenyExisted
            ? RemoteDesktopHelper.BuildSetDwordCommand(
                RemoteDesktopHelper.TerminalServerKeyPath, RemoteDesktopHelper.FDenyValueName, b.FDenyValue)
            : RemoteDesktopHelper.BuildRemoveValueCommand(
                RemoteDesktopHelper.TerminalServerKeyPath, RemoteDesktopHelper.FDenyValueName);
        int ec = RunPowerShell(cmd, out var outp);
        if (ec != 0)
            throw new InvalidOperationException($"fDeny の復元に失敗しました：{outp}");
    }

    /// <summary>安全側フォールバック＝FW 両規則 Disable ＋ fDeny=1（OFF）。best-effort。</summary>
    private void SafeFallbackToDisabled()
    {
        RunPowerShell(RemoteDesktopHelper.BuildDisableCommand(), out _);
    }

    // ============================================================
    // PowerShell 実行（1プロセス・60秒タイムアウト・小出力前提）
    // ============================================================

    /// <summary>
    /// 1 ステップを実行し、ExitCode + 出力から <see cref="RemoteDesktopOutcome"/> を返す（done-stack の forward 用）。
    /// </summary>
    private RemoteDesktopOutcome RunStep(string command, out string? err)
    {
        int ec = RunPowerShell(command, out var combined);
        err = ec == 0 ? null : combined;
        return RemoteDesktopHelper.Classify(ec, combined);
    }

    /// <summary>
    /// powershell.exe を 1 プロセス起動して -Command を実行。ExitCode を返し、stderr 優先（無ければ stdout）を combined に。
    /// 60秒タイムアウトで Kill。起動失敗/タイムアウト/例外は ExitCode=-1（Classify で Failed か AccessDenied）。
    /// 小出力前提（Set-ItemProperty / *-NetFirewallRule / Get-NetFirewallRule はメッセージ少）。
    /// </summary>
    private int RunPowerShell(string command, out string combined)
    {
        combined = string.Empty;
        var psi = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList =
            {
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy", "Bypass",
                "-Command",
                command
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var p = Process.Start(psi);
            if (p == null)
            {
                combined = "powershell.exe を起動できませんでした。";
                return -1;
            }

            if (!p.WaitForExit(60_000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                combined = "処理がタイムアウトしました（60秒）。";
                return -1;
            }

            var stderr = p.StandardError.ReadToEnd();
            var stdout = p.StandardOutput.ReadToEnd();
            combined = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            combined = ex.Message;
            return -1;
        }
    }
}
