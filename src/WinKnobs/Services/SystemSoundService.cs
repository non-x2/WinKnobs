using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace WinKnobs.Services;

/// <summary>
/// システム効果音 ON/OFF（HKCU\AppEvents\Schemes・管理者不要）の読み書きサービス（v0.3.3）。
/// 全ロジックの純粋部分は <see cref="SystemSoundHelper"/> に集約し、ここは I/O と退避だけを担う。
///
/// 「実際に消音される」正しい実装（Scope B）：トップの (Default) を .None にするだけでは鳴り続けるため、
///   各イベントの .Current\(Default) を直接書き換える（OFF=空文字／ON=兄弟 .Default からコピー）。
///
/// 退避方針（GraphicsService と同型・致命1 反映）：BackupService の maintenance ストア
///   （FeatureKey="systemsounds"）に「WinKnobs で初めて触る前」の全 .Current 値（値+型+有無）＋
///   トップスキーム名を first-touch-wins で1度だけ JSON 退避。Apply を繰り返しても原状は上書きしない。
///   RestoreAll 成功後に退避を消して次の first-touch を許す。
///
/// REG_EXPAND_SZ（%SystemRoot% 入り）は型を保存して復元（型を落とすと展開されず無音バグ）。
/// DeleteValue/WriteValue のサイレント失敗は集計して最後に throw（見落とし事例 2026-06-10）。
///
/// DI：必ず <b>Singleton</b>（Transient だと BackupService の first-touch-wins 判定が毎回リセットされ二重退避を招く）。
/// </summary>
public class SystemSoundService
{
    /// <summary>maintenance 退避ストアの機能キー。</summary>
    public const string FeatureKey = "systemsounds";

    /// <summary>
    /// cross-reviewer should_fix①（2026-07-21）：委譲型 Command（CommandAction.Backup がマーカーのみを返し、
    /// 実データは Maintenance ストア側にある設計）向け。RestoreAll が実際には何も復元できなかった場合に
    /// この文言で throw する。SettingDialogViewModel 側でこの文言を判定し、幽霊トークン（command_tokens.json）
    /// を掃除する（マーカーと実データがズレた状態を持ち越さない）。
    /// </summary>
    public const string NoBackupDataMessage = "復元用の退避データが見つかりません。既に復元済みか、データが失われています。";

    private readonly RegistryService _registry;
    private readonly BackupService _backup;

    public SystemSoundService(RegistryService registry, BackupService backup)
    {
        _registry = registry;
        _backup = backup;
    }

    // =================== 読取 ===================

    /// <summary>
    /// 現在「消音（OFF）」か。トップスキームポインタ基準の速読（plan-validator 指摘1：外部ツールで
    /// .Current だけ半端に変えた環境では実状態とズレ得る＝UI 文言で注記済み）。
    /// </summary>
    public bool IsMuted()
        => SystemSoundHelper.IsMuteScheme(ReadScheme());

    /// <summary>表示ラベル（Choices の同一参照）。</summary>
    public string CurrentLabel()
        => SystemSoundHelper.LabelForScheme(ReadScheme());

    /// <summary>退避（WinKnobs で初めて触る前の状態）があるか＝「戻す」活性判定。</summary>
    public bool HasBackup() => _backup.HasMaintenanceOriginal(FeatureKey);

    private string? ReadScheme() => _registry.ReadValue<string>(SystemSoundHelper.SchemesPath, "");

    // =================== 退避（first-touch-wins） ===================

    /// <summary>
    /// 全 .Current\(Default) の原状（値+型+有無）＋トップスキームを first-touch-wins で1度だけ退避。
    /// 既に退避済みなら何もしない。
    /// </summary>
    public void BackupFirstTouch()
    {
        if (_backup.HasMaintenanceOriginal(FeatureKey)) return;   // first-touch-wins

        var backup = new SoundBackup { Scheme = ReadScheme() ?? SystemSoundHelper.SchemeDefault };

        foreach (var currentPath in EnumerateEventCurrentPaths())
        {
            var (existed, value, kind) = ReadDefaultValue(currentPath);
            backup.Events[currentPath] = new SoundEventEntry
            {
                Existed = existed,
                Value = value ?? "",
                Kind = SystemSoundHelper.KindToInt(kind)
            };
        }

        _backup.SaveMaintenanceOriginalIfAbsent(FeatureKey, SystemSoundHelper.Serialize(backup));
    }

    // =================== 書込（Apply） ===================

    /// <summary>
    /// 効果音を消音(true)/標準(false)に切替。書込前に first-touch 退避。
    /// OFF＝全 .Current\(Default) を空文字(REG_SZ)に。ON＝各 .Current を兄弟 .Default からコピー（.Default 無しは触らない）。
    /// 最後にトップスキームを .None/.Default に更新。書込失敗は集計して throw（先頭3件をメッセージに含める）。
    /// </summary>
    public void SetMuted(bool mute)
    {
        BackupFirstTouch();

        var failures = new List<string>();

        foreach (var currentPath in EnumerateEventCurrentPaths())
        {
            try
            {
                if (mute)
                {
                    _registry.WriteValue(currentPath, "", SystemSoundHelper.MutedValue, RegistryValueKind.String);
                }
                else
                {
                    // ON＝標準音復元：兄弟 .Default\(Default) を値+型ごとコピー。.Default 無し/値なしは触らない。
                    var defaultPath = SystemSoundHelper.ToSiblingDefaultPath(currentPath);
                    var (existed, value, kind) = ReadDefaultValue(defaultPath);
                    if (existed)
                        _registry.WriteValue(currentPath, "", value ?? "", kind);
                }
            }
            catch
            {
                failures.Add(currentPath);
            }
        }

        // トップスキームポインタを更新（UI 表示・サウンド設定 CPL 反映用）。
        try
        {
            _registry.WriteValue(SystemSoundHelper.SchemesPath, "",
                mute ? SystemSoundHelper.SchemeNone : SystemSoundHelper.SchemeDefault,
                RegistryValueKind.String);
        }
        catch
        {
            failures.Add(SystemSoundHelper.SchemesPath);
        }

        ThrowIfFailed(failures, "効果音の設定の一部を書き込めませんでした");
    }

    // =================== 復元（個別「戻す」） ===================

    /// <summary>
    /// 効果音設定を「WinKnobs で初めて触る前」へ厳密復元。退避の各 .Current を値+型で書戻し
    /// （existed=false は削除）、トップスキームも復元。退避に在るが現存しないキー（WU 等で消失）はスキップ。
    /// GraphicsService と同ポリシー：書戻し後に退避を削除（部分失敗でも退避は消す＝壊し過ぎない優先）→最後に集計 throw。
    /// </summary>
    public void RestoreAll()
    {
        // cross-reviewer should_fix①（2026-07-21）：退避なし時の無音 no-op をやめ throw する。
        // 委譲型 Command は CommandAction.Backup がマーカー文字列しか返さないため、SettingDialogViewModel は
        // 「Restore が例外を投げなかった＝成功」としか判定できない。無音 return のままだと、マーカー
        // （command_tokens.json）だけが残って実データ（Maintenance ストア）が既に無い状態でも
        // 「✅ Command を適用前の状態に戻しました。」という偽成功が表示されてしまう。
        if (!_backup.HasMaintenanceOriginal(FeatureKey))
            throw new InvalidOperationException(NoBackupDataMessage);

        var dict = _backup.LoadMaintenanceOriginals();
        if (!dict.TryGetValue(FeatureKey, out var json))
        {
            _backup.RemoveMaintenanceOriginal(FeatureKey);
            throw new InvalidOperationException(NoBackupDataMessage);
        }

        var backup = SystemSoundHelper.TryDeserialize(json);
        if (backup == null)
        {
            // 退避破損：これ以上壊さない。後始末のみ。破損も「実質的に復元用データを失った」ため同じ扱いで throw。
            _backup.RemoveMaintenanceOriginal(FeatureKey);
            throw new InvalidOperationException(NoBackupDataMessage);
        }

        var failures = new List<string>();

        foreach (var kv in backup.Events)
        {
            var currentPath = kv.Key;
            var entry = kv.Value;

            // ゴースト鍵スキップ（plan-validator エッジ3）：退避後に消えた .Current は復活させない。
            if (!_registry.KeyExists(currentPath)) continue;

            try
            {
                if (entry.Existed)
                    _registry.WriteValue(currentPath, "", entry.Value, SystemSoundHelper.IntToKind(entry.Kind));
                else if (!_registry.DeleteValue(currentPath, ""))
                    failures.Add(currentPath);   // 退避時に値なし→WinKnobs が書いた "" を削除して原状（未設定）へ
            }
            catch
            {
                failures.Add(currentPath);
            }
        }

        // トップスキーム復元。
        try
        {
            _registry.WriteValue(SystemSoundHelper.SchemesPath, "", backup.Scheme, RegistryValueKind.String);
        }
        catch
        {
            failures.Add(SystemSoundHelper.SchemesPath);
        }

        // GraphicsService 同型：復元後は退避を消す（部分失敗でも消す＝次操作で新規 first-touch を取れる状態）。
        _backup.RemoveMaintenanceOriginal(FeatureKey);

        ThrowIfFailed(failures, "効果音の設定の一部を元に戻せませんでした");
    }

    // =================== 内部ヘルパー（I/O） ===================

    /// <summary>Apps\&lt;App&gt;\&lt;Event&gt;\.Current が存在する全イベントのフルパスを列挙。</summary>
    private IEnumerable<string> EnumerateEventCurrentPaths()
    {
        foreach (var app in _registry.GetSubKeyNames(SystemSoundHelper.AppsPath))
        {
            if (string.IsNullOrEmpty(app)) continue;
            var appPath = $@"{SystemSoundHelper.AppsPath}\{app}";
            foreach (var ev in _registry.GetSubKeyNames(appPath))
            {
                if (string.IsNullOrEmpty(ev)) continue;
                var currentPath = SystemSoundHelper.BuildEventCurrentPath(app, ev);
                if (_registry.KeyExists(currentPath))
                    yield return currentPath;
            }
        }
    }

    /// <summary>キーの (Default) 値を 値の有無・値・型 で読む。</summary>
    private (bool existed, string? value, RegistryValueKind kind) ReadDefaultValue(string keyPath)
    {
        var value = _registry.ReadValue<string>(keyPath, "");
        if (value == null) return (false, null, RegistryValueKind.String);
        var kind = _registry.GetValueKind(keyPath, "");
        if (kind == RegistryValueKind.Unknown) kind = RegistryValueKind.String;
        return (true, value, kind);
    }

    /// <summary>失敗があれば先頭3件を添えて throw（サイレント失敗禁止）。</summary>
    private static void ThrowIfFailed(List<string> failures, string headline)
    {
        if (failures.Count == 0) return;
        var sample = string.Join("、", failures.Take(3));
        throw new InvalidOperationException(
            $"{headline}（{failures.Count} 件失敗）。例：{sample}。" +
            "Windows の「サウンド」設定を開いている場合は閉じてからやり直してください。");
    }
}
