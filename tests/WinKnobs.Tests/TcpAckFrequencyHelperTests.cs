using WinKnobs.Services;
using Xunit;

namespace WinKnobs.Tests;

public class TcpAckFrequencyHelperTests
{
    [Fact]
    public void BuildInterfacePath_ReturnsExpectedPath()
    {
        var path = TcpAckFrequencyHelper.BuildInterfacePath("{ABC-123}");
        Assert.Equal(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{ABC-123}", path);
    }

    [Fact]
    public void BuildInterfacePath_DifferentGuids_ProduceDifferentPaths()
    {
        var a = TcpAckFrequencyHelper.BuildInterfacePath("{111}");
        var b = TcpAckFrequencyHelper.BuildInterfacePath("{222}");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ValueName_IsTcpAckFrequency()
    {
        Assert.Equal("TcpAckFrequency", TcpAckFrequencyHelper.ValueName);
    }
}
