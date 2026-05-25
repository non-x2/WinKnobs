using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace WinKnobs.Services;

/// <summary>
/// 環境情報スナップショット。
/// </summary>
public class EnvironmentInfo
{
    public string OsName { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string Edition { get; set; } = "";
    public string CpuName { get; set; } = "";
    public string Architecture { get; set; } = "";
    public long TotalMemoryMB { get; set; }
    public string DisplayResolution { get; set; } = "";
    public string DotNetVersion { get; set; } = "";
}

/// <summary>
/// 現在のPC環境情報を取得するサービス。
/// Issues 報告用にクリップボードコピー可能な文字列も提供する。
/// </summary>
public class EnvironmentInfoService
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    public EnvironmentInfo Collect()
    {
        var info = new EnvironmentInfo();

        try
        {
            using var os = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            info.OsName = os?.GetValue("ProductName")?.ToString() ?? "Windows";
            var displayVer = os?.GetValue("DisplayVersion")?.ToString() ?? "";
            var build = os?.GetValue("CurrentBuild")?.ToString() ?? "";
            var ubr = os?.GetValue("UBR")?.ToString() ?? "0";
            info.OsVersion = $"{displayVer} (Build {build}.{ubr})";
            info.Edition = os?.GetValue("EditionID")?.ToString() ?? "";
        }
        catch
        {
            info.OsName = Environment.OSVersion.VersionString;
        }

        try
        {
            using var cpu = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            info.CpuName = cpu?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "";
        }
        catch
        {
            info.CpuName = "(取得失敗)";
        }

        info.Architecture = RuntimeInformation.OSArchitecture.ToString();

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
        if (GlobalMemoryStatusEx(ref mem))
        {
            info.TotalMemoryMB = (long)(mem.ullTotalPhys / (1024UL * 1024UL));
        }

        try
        {
            var w = SystemParameters.PrimaryScreenWidth;
            var h = SystemParameters.PrimaryScreenHeight;
            info.DisplayResolution = $"{w:F0} x {h:F0}";
        }
        catch
        {
            info.DisplayResolution = "(取得失敗)";
        }

        info.DotNetVersion = RuntimeInformation.FrameworkDescription;

        return info;
    }

    /// <summary>
    /// クリップボード貼付け用のマークダウン整形テキスト（GitHub Issues 報告用）。
    /// 2026-05-20 改良：WinKnobs アセンブリ情報＋実行権限も追加。
    /// </summary>
    public string BuildClipboardText()
    {
        var info = Collect();
        var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName();
        var isAdmin = new System.Security.Principal.WindowsPrincipal(
            System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

        var sb = new StringBuilder();
        sb.AppendLine("# WinKnobs 環境情報");
        sb.AppendLine();
        sb.AppendLine("## アプリ");
        sb.AppendLine($"- WinKnobs: {asm.Name} v{asm.Version}");
        sb.AppendLine($"- 実行権限: {(isAdmin ? "管理者" : "標準ユーザー")}");
        sb.AppendLine();
        sb.AppendLine("## OS");
        sb.AppendLine($"- OS: {info.OsName} ({info.Edition})");
        sb.AppendLine($"- バージョン: {info.OsVersion}");
        sb.AppendLine($"- アーキテクチャ: {info.Architecture}");
        sb.AppendLine();
        sb.AppendLine("## ハードウェア");
        sb.AppendLine($"- CPU: {info.CpuName}");
        sb.AppendLine($"- 物理メモリ: {info.TotalMemoryMB:N0} MB");
        sb.AppendLine($"- 解像度: {info.DisplayResolution}");
        sb.AppendLine();
        sb.AppendLine("## ランタイム");
        sb.AppendLine($"- .NET: {info.DotNetVersion}");
        sb.AppendLine($"- 日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return sb.ToString();
    }
}
