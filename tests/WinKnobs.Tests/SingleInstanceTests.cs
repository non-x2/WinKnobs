using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// 単一インスタンス名の決定的生成（純粋関数・v0.3.1）。
/// 実 Mutex/Event の往復は <see cref="SingleInstanceManagerTests"/>（プロセス内・headless）で検証。
/// </summary>
public class SingleInstanceNamesTests
{
    [Fact]
    public void Mutex_and_activate_names_differ()
        => Assert.NotEqual(SingleInstanceNames.Mutex(), SingleInstanceNames.Activate());

    [Fact]
    public void Names_start_with_local_namespace()
    {
        Assert.StartsWith(@"Local\", SingleInstanceNames.Mutex());
        Assert.StartsWith(@"Local\", SingleInstanceNames.Activate());
    }

    [Fact]
    public void Names_contain_app_guid()
    {
        Assert.Contains(SingleInstanceNames.AppGuid, SingleInstanceNames.Mutex());
        Assert.Contains(SingleInstanceNames.AppGuid, SingleInstanceNames.Activate());
    }

    [Fact]
    public void Names_within_260_chars()
    {
        Assert.True(SingleInstanceNames.Mutex().Length <= 260);
        Assert.True(SingleInstanceNames.Activate().Length <= 260);
    }

    [Fact]
    public void Names_have_no_whitespace()
    {
        Assert.DoesNotContain(" ", SingleInstanceNames.Mutex());
        Assert.DoesNotContain(" ", SingleInstanceNames.Activate());
    }

    [Fact]
    public void Names_are_deterministic()
    {
        Assert.Equal(SingleInstanceNames.Mutex(), SingleInstanceNames.Mutex());
        Assert.Equal(SingleInstanceNames.Activate(), SingleInstanceNames.Activate());
    }

    [Fact]
    public void Sanctioned_restart_arg_is_a_flag()
        => Assert.StartsWith("--", SingleInstanceNames.SanctionedRestartArg);
}

/// <summary>
/// xUnit コレクション定義：OS 往復テストを直列実行（並列無効化）。
/// ⚠️ <c>[Collection("名前")]</c> だけでは直列化されず、この <c>[CollectionDefinition(DisableParallelization=true)]</c>
/// マーカーが必須（cross-reviewer 重要1）。一意名で衝突は防げるが、タイミング依存テストの flaky を負荷集中から守る。
/// </summary>
[CollectionDefinition("SingleInstanceSerial", DisableParallelization = true)]
public class SingleInstanceSerialCollection { }

/// <summary>
/// 実 OS プリミティブ（Mutex/EventWaitHandle）のプロセス内往復（v0.3.1）。
/// UI を出さず・フォーカスを奪わず・レジストリにも触れない＝headless で安全。
/// 各テストは <c>Guid.NewGuid()</c> 由来の一意名を使い、ホスト上で実 WinKnobs 稼働中でも衝突しない（重要2/3 反映）。
/// </summary>
[Collection("SingleInstanceSerial")]
public class SingleInstanceManagerTests
{
    private static int CallbackTimeoutMs => Debugger.IsAttached ? 10000 : 2000;

    private static (string mutex, string ev) UniqueNames()
    {
        string id = Guid.NewGuid().ToString("N");
        return ($@"Local\WinKnobsTest_{id}_Mutex", $@"Local\WinKnobsTest_{id}_Activate");
    }

    [Fact]
    public void First_manager_is_primary_second_is_secondary()
    {
        var (m, ev) = UniqueNames();
        using var first = new SingleInstanceManager(m, ev);
        Assert.True(first.TryAcquireOwnership());

        using var second = new SingleInstanceManager(m, ev);
        Assert.False(second.TryAcquireOwnership());
    }

    [Fact]
    public void Name_is_released_after_dispose()
    {
        var (m, ev) = UniqueNames();
        using (var first = new SingleInstanceManager(m, ev))
        {
            Assert.True(first.TryAcquireOwnership());
        }

        // Dispose 済 → 同名で再びプライマリになれる（名前が解放されている）
        using var again = new SingleInstanceManager(m, ev);
        Assert.True(again.TryAcquireOwnership());
    }

    [Fact]
    public void Signal_invokes_primary_callback()
    {
        var (m, ev) = UniqueNames();
        using var primary = new SingleInstanceManager(m, ev);
        Assert.True(primary.TryAcquireOwnership());

        using var fired = new ManualResetEventSlim(false);
        primary.StartListening(() => fired.Set());

        using var secondary = new SingleInstanceManager(m, ev);
        Assert.False(secondary.TryAcquireOwnership());
        Assert.True(secondary.SignalPrimaryInstance());

        Assert.True(fired.Wait(CallbackTimeoutMs), "プライマリのコールバックが時間内に発火しなかった");
    }

    [Fact]
    public void Signal_without_listener_returns_false_and_does_not_throw()
    {
        var (m, ev) = UniqueNames();
        using var lonely = new SingleInstanceManager(m, ev);
        // リスナ未起動＝イベント未生成 → false（例外なし）
        Assert.False(lonely.SignalPrimaryInstance());
    }

    [Fact]
    public async Task WaitForRelease_acquires_after_old_instance_releases()
    {
        var (m, ev) = UniqueNames();
        var old = new SingleInstanceManager(m, ev);
        Assert.True(old.TryAcquireOwnership());

        // 200ms 後に旧インスタンスを解放（昇格再起動で旧プロセスが終了するのを模す）
        var releaser = Task.Run(async () =>
        {
            await Task.Delay(200);
            old.Dispose();
        });

        using var restarted = new SingleInstanceManager(m, ev);
        bool acquired = restarted.TryAcquireOwnership(waitForRelease: true, waitMs: 5000);
        await releaser;   // .Wait() はブロッキング警告 xUnit1031 を出すため await

        Assert.True(acquired, "waitForRelease は旧解放後にプライマリを取得できるべき");
    }

    [Fact]
    public void WaitForRelease_times_out_when_not_released()
    {
        var (m, ev) = UniqueNames();
        using var held = new SingleInstanceManager(m, ev);
        Assert.True(held.TryAcquireOwnership());   // 解放しないまま保持

        using var restarted = new SingleInstanceManager(m, ev);
        // 旧が解放しない → 短いタイムアウトで false（自滅せず呼び出し側が窓を出す判断材料）
        bool acquired = restarted.TryAcquireOwnership(waitForRelease: true, waitMs: 300);
        Assert.False(acquired);
    }
}
