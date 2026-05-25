namespace WinKnobs.Models;

/// <summary>
/// スタートアップ系レジストリのハイブ（場所）。
/// Run キー族のみ対象（タスクスケジューラ・スタートアップフォルダは対象外）。
/// 設計書 3-1 参照。
/// </summary>
public enum StartupHive
{
    /// <summary>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run</summary>
    HKLM_Run,
    /// <summary>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce</summary>
    HKLM_RunOnce,
    /// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</summary>
    HKCU_Run,
    /// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce</summary>
    HKCU_RunOnce,
    /// <summary>HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run（64bit OS のみ）</summary>
    HKLM_Wow64_Run,
    /// <summary>HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce（64bit OS のみ）</summary>
    HKLM_Wow64_RunOnce
}
