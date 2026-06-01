using System.Text.Json;
using Microsoft.Win32;

namespace WinKnobs.Services;

/// <summary>
/// ネットワーク機能のサービス層（v0.1.7・設計ドラフト 4-2）。
/// レジストリ／netsh 呼び出しを集約する。コマンド生成・値判定など純粋部分は static にして xUnit 可能に保つ。
///
/// 実装フェーズ：
///   Phase 3（実装済）：配信最適化（DODownloadMode）の取得・適用・復元。
///   Phase 4：DNS プロファイル切替（netsh・物理アダプタ判定）。
///   Phase 6：軽いリセット（flushdns）。
/// </summary>
public class NetworkService
{
    private readonly RegistryService _registry;
    private readonly BackupService _backup;

    // 配信最適化（Delivery Optimization）：設定アプリが使う Config 側。
    private const string DoConfigPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config";
    // グループポリシー側（企業 PC で上書きされる）。存在検出に使う。
    private const string DoPolicyPath = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
    private const string DoValueName = "DODownloadMode";

    /// <summary>退避ストアの機能キー（配信最適化）。</summary>
    public const string FeatureDeliveryOptimization = "deliveryOptimization";

    public const int DoModeOff = 0;      // HTTP のみ（P2P 完全オフ・軽い）
    public const int DoModeDefault = 3;  // インターネット含む（Windows 既定）

    public NetworkService(RegistryService registry, BackupService backup)
    {
        _registry = registry;
        _backup = backup;
    }

    /// <summary>現在の DODownloadMode。未設定は null（＝Windows 既定 3 とみなす）。</summary>
    public int? GetDeliveryOptimizationMode()
    {
        var v = _registry.ReadValue<object>(DoConfigPath, DoValueName);
        if (v is int i) return i;
        if (v != null && int.TryParse(v.ToString(), out var p)) return p;
        return null;
    }

    /// <summary>配信最適化がグループポリシーで管理されているか（Policies 側に値があるか）。</summary>
    public bool IsDeliveryOptimizationPolicyManaged()
        => _registry.ReadValue<object>(DoPolicyPath, DoValueName) != null;

    /// <summary>DODownloadMode 値を初心者向けの日本語ラベルに（純粋関数・xUnit 対象）。</summary>
    public static string DescribeDeliveryOptimizationMode(int mode) => mode switch
    {
        0 => "オフ（P2P なし・軽い）",
        1 => "LAN 内のみ",
        2 => "LAN 内のみ（同じグループ）",
        3 => "オン（インターネット含む・既定）",
        99 => "ダウンロードを簡素化",
        _ => $"モード {mode}",
    };

    /// <summary>「今の状態」表示用サマリ（ポリシー管理なら注記付き）。</summary>
    public string GetDeliveryOptimizationSummary()
    {
        var mode = GetDeliveryOptimizationMode() ?? DoModeDefault;
        var label = DescribeDeliveryOptimizationMode(mode);
        return IsDeliveryOptimizationPolicyManaged() ? label + "・ポリシー管理" : label;
    }

    /// <summary>
    /// 配信最適化を適用（light=true で P2P オフ）。適用前に原状を退避（first-touch-wins）。
    /// HKLM 書込のため非管理者では UnauthorizedAccessException 等（呼び出し側で昇格誘導）。
    /// </summary>
    public NetworkApplyResult ApplyDeliveryOptimization(bool light)
    {
        // 退避（first-touch-wins）：WinKnobs が触る前の原状を JSON で保存。
        var before = GetDeliveryOptimizationMode();
        var payload = JsonSerializer.Serialize(new DeliveryOptimizationBackup(before, before.HasValue));
        _backup.SaveNetworkOriginalIfAbsent(FeatureDeliveryOptimization, payload);

        var target = light ? DoModeOff : DoModeDefault;
        _registry.WriteValue(DoConfigPath, DoValueName, target, RegistryValueKind.DWord);

        // read-back：書込が反映されたか（グループポリシーで弾かれると一致しない・エッジ4）。
        var after = GetDeliveryOptimizationMode();
        return new NetworkApplyResult(after == target, after ?? DoModeDefault, IsDeliveryOptimizationPolicyManaged());
    }

    /// <summary>配信最適化を退避ペイロードから復元（Phase 5「ぜんぶ戻す」が利用）。</summary>
    public void RestoreDeliveryOptimization(string jsonPayload)
    {
        var b = JsonSerializer.Deserialize<DeliveryOptimizationBackup>(jsonPayload);
        if (b == null) return;
        if (b.Existed && b.Mode.HasValue)
            _registry.WriteValue(DoConfigPath, DoValueName, b.Mode.Value, RegistryValueKind.DWord);
        else
            _registry.DeleteValue(DoConfigPath, DoValueName);  // 元々無かった → 削除して Windows 既定へ
    }
}

/// <summary>ネットワーク設定適用の結果。</summary>
public record NetworkApplyResult(bool Applied, int EffectiveValue, bool PolicyManaged);

/// <summary>配信最適化の原状退避ペイロード（JSON 直列化用）。</summary>
public record DeliveryOptimizationBackup(int? Mode, bool Existed);
