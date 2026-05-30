using System.IO;
using System.Text;
using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// StartupSnapshotIntegrity（v0.1.6 機能 A：SHA256 改ざん対策）の単体テスト。
///
/// 機能 B の StartupSnapshotDifferTests に倣うが、こちらはファイル I/O を伴うため、
/// %APPDATA% を汚さないよう Path.GetTempPath() 配下に専用ディレクトリを作り、
/// Dispose で確実に削除する（plan-validator 指摘：テストの環境汚染回避）。
/// </summary>
public class StartupSnapshotIntegrityTests : IDisposable
{
    private readonly string _tempDir;

    public StartupSnapshotIntegrityTests()
    {
        // テスト毎に一意のディレクトリ（並行実行・衝突回避）。
        _tempDir = Path.Combine(Path.GetTempPath(), "WinKnobsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        // WriteSidecar が ACL を掛けるが、現ユーザーは Modify（Delete 含む）なので削除可能。
        // 失敗しても OS の一時ディレクトリ掃除に委ねる（テストは落とさない）。
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* 無視 */ }
    }

    // ─── ヘルパー ───

    /// <summary>テスト用の疑似 snapshot .reg を作る（整合性検証はバイト列のみ見るので中身は任意）。</summary>
    private string CreateRegFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        // reg.exe 同様 UTF-16 LE BOM 付きで書く（BOM 込みでハッシュされることの確認も兼ねる）。
        File.WriteAllText(path, content, new UnicodeEncoding(false, true));
        return path;
    }

    // ─── ComputeSha256（純粋・既知ベクトル）───

    [Fact]
    public void ComputeSha256_Stream_abc_既知ハッシュと一致()
    {
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes("abc"));
        var hash = StartupSnapshotIntegrity.ComputeSha256(ms);

        // SHA256("abc") の既知ベクトル
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    [Fact]
    public void ComputeFileSha256_空ファイル_既知ハッシュと一致()
    {
        var path = Path.Combine(_tempDir, "empty.reg");
        File.WriteAllBytes(path, Array.Empty<byte>());

        var hash = StartupSnapshotIntegrity.ComputeFileSha256(path);

        // SHA256("") の既知ベクトル
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
    }

    [Fact]
    public void ComputeSha256_64桁小文字hexを返す()
    {
        var path = CreateRegFile("a.reg", "Windows Registry Editor Version 5.00\r\n");
        var hash = StartupSnapshotIntegrity.ComputeFileSha256(path);

        Assert.Equal(64, hash.Length);
        Assert.True(hash.All(Uri.IsHexDigit));
        Assert.Equal(hash.ToLowerInvariant(), hash);
    }

    // ─── GetSidecarPath ───

    [Fact]
    public void GetSidecarPath_拡張子をsha256に置換()
    {
        var sidecar = StartupSnapshotIntegrity.GetSidecarPath(@"C:\x\startup_full_20260531_120000_123.reg");
        Assert.Equal(@"C:\x\startup_full_20260531_120000_123.sha256", sidecar);
    }

    // ─── WriteSidecar ───

    [Fact]
    public void WriteSidecar_サイドカーが生成されsha256sum互換形式()
    {
        var reg = CreateRegFile("snap.reg", "dummy content");
        StartupSnapshotIntegrity.WriteSidecar(reg);

        var sidecar = StartupSnapshotIntegrity.GetSidecarPath(reg);
        Assert.True(File.Exists(sidecar));

        var content = File.ReadAllText(sidecar);
        var expectedHash = StartupSnapshotIntegrity.ComputeFileSha256(reg);
        // "<64hex>␣␣<ファイル名>" 形式
        Assert.Equal($"{expectedHash}  snap.reg\r\n", content);
    }

    // ─── Verify：一致 / 改ざん / なし / 破損 ───

    [Fact]
    public void Verify_書込直後は一致してtrue()
    {
        var reg = CreateRegFile("snap.reg", "original content");
        StartupSnapshotIntegrity.WriteSidecar(reg);

        var (isValid, expected, actual) = StartupSnapshotIntegrity.Verify(reg);

        Assert.True(isValid);
        Assert.Equal(expected, actual);
        Assert.NotNull(expected);
    }

    [Fact]
    public void Verify_本体を改ざんするとfalse()
    {
        var reg = CreateRegFile("snap.reg", "original content");
        StartupSnapshotIntegrity.WriteSidecar(reg);

        // サイドカー生成後に本体を 1 バイト書き換え（改ざんシナリオ）
        File.WriteAllText(reg, "original content TAMPERED", new UnicodeEncoding(false, true));

        var (isValid, expected, actual) = StartupSnapshotIntegrity.Verify(reg);

        Assert.False(isValid);
        Assert.NotEqual(expected, actual);
    }

    [Fact]
    public void Verify_サイドカーなしはnull_古いsnapshot扱い()
    {
        var reg = CreateRegFile("old_snap.reg", "no sidecar here");
        // WriteSidecar を呼ばない（= v0.1.5 以前の古い snapshot を模擬）

        var (isValid, expected, actual) = StartupSnapshotIntegrity.Verify(reg);

        Assert.Null(isValid);
        Assert.Null(expected);
        Assert.Null(actual);
    }

    [Fact]
    public void Verify_壊れたサイドカー_64hexでない_failsafeでfalse()
    {
        var reg = CreateRegFile("snap.reg", "content");
        StartupSnapshotIntegrity.WriteSidecar(reg);

        // サイドカーを破損させる（64hex でないゴミ）
        var sidecar = StartupSnapshotIntegrity.GetSidecarPath(reg);
        File.WriteAllText(sidecar, "this_is_not_a_valid_sha256_hash\r\n");

        var (isValid, expected, _) = StartupSnapshotIntegrity.Verify(reg);

        // 破損は「素通り（null）」でなく fail-safe で「怪しい（false）」扱い
        Assert.False(isValid);
        Assert.Null(expected);
    }

    [Fact]
    public void Verify_別の有効hexに手編集_false()
    {
        var reg = CreateRegFile("snap.reg", "content");
        StartupSnapshotIntegrity.WriteSidecar(reg);

        // 形式は正しい（64hex）が中身が違うハッシュに手編集
        var sidecar = StartupSnapshotIntegrity.GetSidecarPath(reg);
        var wrongHash = new string('a', 64);
        File.WriteAllText(sidecar, $"{wrongHash}  snap.reg\r\n");

        var (isValid, expected, actual) = StartupSnapshotIntegrity.Verify(reg);

        Assert.False(isValid);
        Assert.Equal(wrongHash, expected);
        Assert.NotEqual(expected, actual);
    }

    [Fact]
    public void Verify_ファイル名欄が改名後でもハッシュ部分だけ照合してtrue()
    {
        var reg = CreateRegFile("snap.reg", "content");
        var hash = StartupSnapshotIntegrity.ComputeFileSha256(reg);

        // ファイル名欄を別名にしてもハッシュが合っていれば一致する（先頭トークンのみ照合）
        var sidecar = StartupSnapshotIntegrity.GetSidecarPath(reg);
        File.WriteAllText(sidecar, $"{hash}  RENAMED_elsewhere.reg\r\n");

        var (isValid, _, _) = StartupSnapshotIntegrity.Verify(reg);

        Assert.True(isValid);
    }

    // ─── ParseSidecarHash（純粋）───

    [Fact]
    public void ParseSidecarHash_先頭トークンを抽出_ファイル名欄は無視()
    {
        var hash = new string('b', 64);
        var parsed = StartupSnapshotIntegrity.ParseSidecarHash($"{hash}  some_file.reg\r\n");
        Assert.Equal(hash, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n")]
    [InlineData("tooshort")]
    [InlineData("zzzz816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")] // 非 hex 文字混入
    public void ParseSidecarHash_不正な入力はnull(string content)
    {
        Assert.Null(StartupSnapshotIntegrity.ParseSidecarHash(content));
    }

    [Fact]
    public void ParseSidecarHash_大文字hexも小文字化して受理()
    {
        var upper = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
        var parsed = StartupSnapshotIntegrity.ParseSidecarHash(upper + "  f.reg");
        Assert.Equal(upper.ToLowerInvariant(), parsed);
    }
}
