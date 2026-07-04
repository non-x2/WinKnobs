namespace WinKnobs.Services;

/// <summary>
/// TCP 即時 ACK（TcpAckFrequency）関連の純粋関数（v0.3.21）。
/// この値はグローバルでなく物理アダプタ個別（Tcpip\Parameters\Interfaces\{GUID}）に書く点が
/// Network Throttling Index（グローバル単一パス）と異なるため、パス生成だけ切り出す。
/// </summary>
public static class TcpAckFrequencyHelper
{
    public const string ValueName = "TcpAckFrequency";
    public const int ImmediateAckValue = 1;   // 遅延 ACK を無効化（即時 ACK）

    /// <summary>アダプタ GUID からインターフェース別レジストリパスを生成（純粋関数・xUnit 対象）。</summary>
    public static string BuildInterfacePath(string guid)
        => $@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{guid}";
}
