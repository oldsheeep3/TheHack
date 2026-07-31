using Switcher.Contracts;

namespace Switcher.Contracts.Tests;

/// <summary>
/// The Listener/Caller mode only reaches libsrt through the URL query (docs/specs/00-system-overview.md
/// §4.3). Getting this wrong is silent: FFmpeg falls back to caller, the PC dials out instead of binding
/// the port, and the source stays black while the sender reports that it cannot connect.
/// </summary>
public class SrtUrlTests
{
    [Fact]
    public void Listener_BindsWildcardAndDeclaresMode()
    {
        Assert.Equal("srt://0.0.0.0:9000?mode=listener", SrtUrl.ForMode("srt://192.168.1.50:9000", listener: true));
    }

    [Fact]
    public void Listener_EmptyUrl_BindsDefaultPort()
    {
        Assert.Equal($"srt://0.0.0.0:{ProtocolConstants.SrtListenPort}?mode=listener", SrtUrl.ForMode("", listener: true));
        Assert.Equal($"srt://0.0.0.0:{ProtocolConstants.SrtListenPort}?mode=listener", SrtUrl.ForMode(null, listener: true));
    }

    [Fact]
    public void Listener_KeepsTypedPort()
    {
        Assert.Equal("srt://0.0.0.0:9100?mode=listener", SrtUrl.ForMode("srt://192.168.1.50:9100", listener: true));
    }

    [Fact]
    public void Caller_KeepsHostAndDeclaresMode()
    {
        Assert.Equal("srt://192.168.1.100:9000?mode=caller", SrtUrl.ForMode("srt://192.168.1.100:9000", listener: false));
    }

    [Fact]
    public void BareHostPort_GetsScheme()
    {
        Assert.Equal("srt://192.168.1.100:9000?mode=caller", SrtUrl.ForMode("192.168.1.100:9000", listener: false));
    }

    [Fact]
    public void ExplicitMode_IsNeverOverwritten()
    {
        // An operator who typed the mode themselves outranks the dropdown.
        Assert.Equal("srt://0.0.0.0:9000?mode=rendezvous", SrtUrl.ForMode("srt://1.2.3.4:9000?mode=rendezvous", listener: true));
        Assert.Equal("srt://1.2.3.4:9000?mode=listener", SrtUrl.ForMode("srt://1.2.3.4:9000?mode=listener", listener: false));
    }

    [Fact]
    public void OtherQueryParameters_Survive()
    {
        Assert.Equal(
            "srt://0.0.0.0:9000?passphrase=hunter2&streamid=pgm&mode=listener",
            SrtUrl.ForMode("srt://192.168.1.50:9000?passphrase=hunter2&streamid=pgm", listener: true));
    }

    [Fact]
    public void SurroundingWhitespace_IsIgnored()
    {
        Assert.Equal("srt://0.0.0.0:9000?mode=listener", SrtUrl.ForMode("  srt://192.168.1.50:9000  ", listener: true));
    }
}
