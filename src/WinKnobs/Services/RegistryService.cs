using Microsoft.Win32;

namespace WinKnobs.Services;

/// <summary>
/// レジストリ読み書きの安全ラッパー。
/// HKLM\..., HKCU\... 形式のパスを解釈して操作する。
/// A3-2-c で追加：WriteBinary / DeleteKey / KeyExistsFull、ReadValue の byte[] 整形。
/// </summary>
public class RegistryService
{
    /// <summary>
    /// レジストリ値を読む。値が存在しない場合は default(T)。
    /// A3-2-c：T=object で byte[] が返った場合は "00 00 00 00" 形式の16進文字列に整形（plan-validator V-9）。
    /// </summary>
    public T? ReadValue<T>(string keyPath, string valueName)
    {
        var (hive, subKey) = ParsePath(keyPath);
        using var key = hive.OpenSubKey(subKey, false);
        if (key == null) return default;
        var v = key.GetValue(valueName);
        if (v == null) return default;

        try
        {
            if (typeof(T) == typeof(string)) return (T)(object)v.ToString()!;
            // A3-2-c V-9：T=object で byte[] を 16進文字列に整形
            if (typeof(T) == typeof(object) && v is byte[] bytes)
            {
                return (T)(object)FormatBytesToHex(bytes);
            }
            return (T)Convert.ChangeType(v, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// レジストリ値を書く。キーが存在しなければ作成する。
    /// </summary>
    public void WriteValue<T>(string keyPath, string valueName, T value, RegistryValueKind kind = RegistryValueKind.Unknown)
    {
        var (hive, subKey) = ParsePath(keyPath);
        using var key = hive.CreateSubKey(subKey, true)
            ?? throw new InvalidOperationException($"Failed to open or create key: {keyPath}");

        if (kind == RegistryValueKind.Unknown)
        {
            key.SetValue(valueName, value!);
        }
        else
        {
            key.SetValue(valueName, value!, kind);
        }
    }

    /// <summary>
    /// REG_BINARY 値を生バイト列のまま読む（v0.2.2 夜間モード blob 用）。
    /// 値が無い／型が byte[] でない／キー無しは null。ReadValue&lt;object&gt; は16進文字列へ
    /// 整形してしまうため、blob 解析にはこちらを使う。
    /// </summary>
    public byte[]? ReadBinary(string keyPath, string valueName)
    {
        var (hive, subKey) = ParsePath(keyPath);
        using var key = hive.OpenSubKey(subKey, false);
        if (key == null) return null;
        return key.GetValue(valueName) as byte[];
    }

    /// <summary>
    /// A3-2-c：バイト列を REG_BINARY として書込（plan-validator V-9）。
    /// Convert.ChangeType が byte[] に対応しないため WriteValue&lt;T&gt; とは別に専用メソッドを用意。
    /// </summary>
    public void WriteBinary(string keyPath, string valueName, byte[] data)
    {
        var (hive, subKey) = ParsePath(keyPath);
        using var key = hive.CreateSubKey(subKey, true)
            ?? throw new InvalidOperationException($"Failed to open or create key: {keyPath}");
        key.SetValue(valueName, data, RegistryValueKind.Binary);
    }

    /// <summary>
    /// レジストリ値を削除。
    /// </summary>
    public bool DeleteValue(string keyPath, string valueName)
    {
        var (hive, subKey) = ParsePath(keyPath);
        using var key = hive.OpenSubKey(subKey, true);
        if (key == null) return false;
        try
        {
            key.DeleteValue(valueName, false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 指定キーの存在確認。
    /// </summary>
    public bool KeyExists(string keyPath)
    {
        var (hive, subKey) = ParsePath(keyPath);
        using var key = hive.OpenSubKey(subKey, false);
        return key != null;
    }

    /// <summary>
    /// 指定キー直下の値名を列挙する（v0.3.0 グラフィック：アプリ別GPU設定の一覧用）。
    /// キーが存在しない場合は空配列。既定値（名前="")も含まれ得るので呼び出し側で除外すること。
    /// </summary>
    public string[] GetValueNames(string keyPath)
    {
        var (hive, subKey) = ParsePath(keyPath);
        using var key = hive.OpenSubKey(subKey, false);
        return key?.GetValueNames() ?? Array.Empty<string>();
    }

    /// <summary>
    /// 指定キー直下のサブキー名を列挙する（v0.3.3 効果音：Apps\&lt;App&gt;\&lt;Event&gt; の3段列挙用）。
    /// キーが存在しない場合は空配列。GetValueNames（値名列挙）と対称の薄いラッパー。
    /// </summary>
    public string[] GetSubKeyNames(string keyPath)
    {
        var (hive, subKey) = ParsePath(keyPath);
        using var key = hive.OpenSubKey(subKey, false);
        return key?.GetSubKeyNames() ?? Array.Empty<string>();
    }

    /// <summary>
    /// 指定値の型（REG_SZ / REG_DWORD / REG_BINARY 等）を取得する（v0.3.0 グラフィック用）。
    /// キーが存在しない・値が存在しない場合は <see cref="RegistryValueKind.Unknown"/>。
    /// アプリ別GPU値（REG_SZ）と Windows 予約値（REG_DWORD 等）を型で区別し、
    /// 予約値の混入・退避時の型変換（DWORD→SZ）を防ぐために使う。
    /// 注意：値ごとに OpenSubKey する。件数が多い場合はキーを一度開いて
    ///       GetValueNames/GetValueKind をまとめて行うバッチ化が望ましい（現状の利用は数十件で問題なし）。
    /// </summary>
    public RegistryValueKind GetValueKind(string keyPath, string valueName)
    {
        var (hive, subKey) = ParsePath(keyPath);
        using var key = hive.OpenSubKey(subKey, false);
        if (key == null) return RegistryValueKind.Unknown;
        try
        {
            return key.GetValueKind(valueName);   // 値が無いと ArgumentException
        }
        catch
        {
            return RegistryValueKind.Unknown;
        }
    }

    /// <summary>
    /// A3-2-c：キーを作成（W5 クラシック右クリックメニュー Command 用）。
    /// 既に存在しても成功扱い。
    /// </summary>
    public void CreateKey(string keyPath)
    {
        var (hive, subKey) = ParsePath(keyPath);
        using var key = hive.CreateSubKey(subKey, true)
            ?? throw new InvalidOperationException($"Failed to create key: {keyPath}");
    }

    /// <summary>
    /// A3-2-c：キーをツリーごと削除（W5 クラシック右クリックメニュー Command 用）。
    /// 存在しない場合は false を返すだけでエラーにしない。
    /// </summary>
    public bool DeleteKeyTree(string keyPath)
    {
        var (hive, subKey) = ParsePath(keyPath);
        try
        {
            hive.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// "HKLM\Software\..." のような文字列を Hive と SubKey に分解する。
    /// </summary>
    private static (RegistryKey hive, string subKey) ParsePath(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
            throw new ArgumentException("keyPath is empty", nameof(keyPath));

        var parts = keyPath.Split('\\', 2);
        var hiveName = parts[0].ToUpperInvariant();
        var subKey = parts.Length > 1 ? parts[1] : string.Empty;

        var hive = hiveName switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKU" or "HKEY_USERS" => Registry.Users,
            "HKCC" or "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
            _ => throw new ArgumentException($"Unknown hive: {hiveName}")
        };
        return (hive, subKey);
    }

    /// <summary>
    /// byte[] を "00 11 22 33" 形式の16進文字列に整形（A3-2-c V-9）。
    /// </summary>
    private static string FormatBytesToHex(byte[] bytes)
        => BitConverter.ToString(bytes).Replace("-", " ");
}
