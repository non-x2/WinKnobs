using System;

namespace WinKnobs.Services;

/// <summary>
/// v0.2.1 Step4：コア分離（メモリ整合性・HVCI）の有効/無効。
/// レジストリ DWORD 直接書込（**要管理者権限・再起動で確定**）。子プロセスを使わないため
/// Sysmain/WSL より単純（UnauthorizedAccessException → AccessDenied 分類のみ）。
///
/// ⚠️ UEFI ロック（Secured-core）環境では書込が成功しても再起動で元に戻る場合がある。
///   事後検証は行わず、呼び出し側が「再起動後に Windows セキュリティで確認」を案内する
///   （成功と言い切らない・設計ドラフト §2-5）。
/// 同居値（WasEnabledBy 等）は触らない＝Enabled の値単位操作。
/// </summary>
public class CoreIsolationService
{
    private readonly RegistryService _registry;

    public CoreIsolationService(RegistryService registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// 現在の Enabled 値（null=値なし／0=無効／1=有効）。非昇格でも HKLM 読取は可能。
    /// 読取例外は null（不明）に倒す。
    /// </summary>
    public int? ReadEnabled()
    {
        try
        {
            var v = _registry.ReadValue<object>(CoreIsolationHelper.HvciKeyPath, CoreIsolationHelper.EnabledValueName);
            return v is int i ? i : (v is null ? null : (int?)Convert.ToInt32(v));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// GPO/MDM ポリシーで HVCI が管理されているか（RemoteDesktopService.IsPolicyManaged 同型）。
    /// ポリシー値が存在すれば、Scenarios 側を書いても GPO に戻されるため本ツールでは変更不可。
    /// </summary>
    public bool IsPolicyManaged()
    {
        try
        {
            var v = _registry.ReadValue<object>(CoreIsolationHelper.PolicyKeyPath, CoreIsolationHelper.PolicyHvciValueName);
            if (v != null) return true;
            // 代表値が無くても EnableVirtualizationBasedSecurity ポリシーがあれば VBS ごと管理されている
            var vbs = _registry.ReadValue<object>(CoreIsolationHelper.PolicyKeyPath, "EnableVirtualizationBasedSecurity");
            return vbs != null;
        }
        catch
        {
            return false;   // 読めない＝非管理扱い（書込時の失敗は別途 AccessDenied で拾う）
        }
    }

    /// <summary>
    /// コア分離（HVCI）を有効/無効にする（要管理者・再起動で確定）。
    /// </summary>
    public CoreIsolationOutcome SetEnabled(bool enable, out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            _registry.WriteValue(
                CoreIsolationHelper.HvciKeyPath,
                CoreIsolationHelper.EnabledValueName,
                CoreIsolationHelper.ToRegistryValue(enable),
                Microsoft.Win32.RegistryValueKind.DWord);
            return CoreIsolationOutcome.Success;
        }
        catch (UnauthorizedAccessException ex)
        {
            errorMessage = ex.Message;
            return CoreIsolationOutcome.AccessDenied;
        }
        catch (System.Security.SecurityException ex)
        {
            errorMessage = ex.Message;
            return CoreIsolationOutcome.AccessDenied;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return CoreIsolationOutcome.Failed;
        }
    }
}
