using LanWatch.Shared.Parsing;

namespace LanWatch.Tests.Parsing;

public class StreamLogParserTests
{
    [Fact]
    public void Parses_passthrough_with_sni()
    {
        Assert.True(StreamLogParser.TryParse(
            "192.168.1.111 [16/Sep/2026:21:57:58 +0100] TCP 200 array516.prod.do.dsp.mp.microsoft.com 3030 1053 125.526", out var e));
        Assert.Equal("192.168.1.111", e.ClientIp);
        Assert.Equal(200, e.Status);
        Assert.Equal("array516.prod.do.dsp.mp.microsoft.com", e.SniHost);
        Assert.Equal(3030, e.BytesSent);
        Assert.Equal(1053, e.BytesReceived);
        Assert.Equal(125.526, e.SessionSeconds, 3);
    }

    [Fact]
    public void Parses_missing_sni()
    {
        Assert.True(StreamLogParser.TryParse("10.100.32.11 [24/Aug/2025:14:38:28 +0100] TCP 500  0 0 0.000", out var e));
        Assert.Equal(500, e.Status);
        Assert.Null(e.SniHost);
        Assert.Equal(0, e.BytesSent);
    }
}

public class ErrorLogParserTests
{
    [Fact]
    public void Parses_upstream_timeout()
    {
        const string line = """2025/10/10 18:14:14 [error] 1886#1886: *51593 upstream timed out (110: Connection timed out) while reading response header from upstream, client: 10.100.32.26, server: , request: "GET /depot/1149461/chunk/c1634581d0ff48d4b1d0ddd8cf8637d07f496f94 HTTP/1.1", upstream: "http://127.0.0.1:3128/depot/1149461/chunk/c1634581d0ff48d4b1d0ddd8cf8637d07f496f94", host: "cache6-ams1.steamcontent.com" """;

        Assert.True(ErrorLogParser.TryParse(line.TrimEnd(), out var e));
        Assert.Equal(new DateTime(2025, 10, 10, 18, 14, 14), e.LocalTime);
        Assert.Equal("error", e.Level);
        Assert.Equal(ErrorKind.UpstreamTimeout, e.Kind);
        Assert.Equal("upstream timed out (110: Connection timed out) while reading response header from upstream", e.Message);
        Assert.Equal("10.100.32.26", e.Client);
        Assert.Equal("cache6-ams1.steamcontent.com", e.Host);
        Assert.Equal("http://127.0.0.1:3128/depot/1149461/chunk/c1634581d0ff48d4b1d0ddd8cf8637d07f496f94", e.Upstream);
        Assert.StartsWith("GET /depot/1149461", e.Request);
    }

    [Fact]
    public void Parses_dns_failure_and_extracts_host()
    {
        const string line = "2026/09/16 21:00:00 [error] 1888#1888: *1 cache3-fra1.steamcontent.com could not be resolved (110: Operation timed out), client: 127.0.0.1, server: , request: \"GET /depot/1/chunk/a HTTP/1.0\"";

        Assert.True(ErrorLogParser.TryParse(line, out var e));
        Assert.Equal(ErrorKind.DnsResolveFailed, e.Kind);
        Assert.Equal("cache3-fra1.steamcontent.com", e.Host);
    }

    [Fact]
    public void Parses_stream_reset_with_byte_counters()
    {
        const string line = """2026/09/16 22:20:19 [error] 1888#1888: *798782 recv() failed (104: Connection reset by peer) while proxying and reading from upstream, client: 192.168.1.111, server: 0.0.0.0:443, upstream: "72.153.5.132:443", bytes from/to client:1054/3030, bytes from/to upstream:3030/1274""";

        Assert.True(ErrorLogParser.TryParse(line, out var e));
        Assert.Equal(ErrorKind.ConnectionReset, e.Kind);
        Assert.Equal("192.168.1.111", e.Client);
        Assert.Equal("72.153.5.132:443", e.Upstream);
    }

    [Theory]
    [InlineData("no host in upstream \":443\"", ErrorKind.NoHostInUpstream)]
    [InlineData("upstream prematurely closed connection while reading upstream", ErrorKind.UpstreamClosed)]
    [InlineData("unexpected status code 504 in slice response while sending to client", ErrorKind.SliceStatus)]
    [InlineData("connect() failed (111: Connection refused) while connecting to upstream", ErrorKind.ConnectionRefused)]
    [InlineData("something else", ErrorKind.Other)]
    public void Classifies(string message, ErrorKind expected) => Assert.Equal(expected, ErrorLogParser.Classify(message));

    [Fact]
    public void Parses_no_host_line_without_dropping_message()
    {
        const string line = "2025/08/24 14:38:28 [error] 1883#1883: *6 no host in upstream \":443\", client: 10.100.32.11, server: 0.0.0.0:443, bytes from/to client:0/0, bytes from/to upstream:0/0";

        Assert.True(ErrorLogParser.TryParse(line, out var e));
        Assert.Equal(ErrorKind.NoHostInUpstream, e.Kind);
        Assert.Equal("10.100.32.11", e.Client);
    }
}
