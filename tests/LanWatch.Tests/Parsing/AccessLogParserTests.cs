using LanWatch.Shared.Parsing;

namespace LanWatch.Tests.Parsing;

public class AccessLogParserTests
{
    [Fact]
    public void Parses_steam_miss()
    {
        const string line = """[steam] 10.100.32.13 / - - - [24/Aug/2025:14:52:07 +0100] "GET /depot/2347770/chunk/d41f1ff8fc5e9912896a0c545beb91120ef99f3b HTTP/1.1" 200 581488 "-" "Valve/Steam HTTP Client 1.0" "MISS" "cache14-fra2.steamcontent.com" "-" """;

        Assert.True(AccessLogParser.TryParse(line.TrimEnd(), out var e));
        Assert.Equal("steam", e.CacheId);
        Assert.Equal("10.100.32.13", e.RemoteAddr);
        Assert.Null(e.ForwardedFor);
        Assert.Equal(new DateTimeOffset(2025, 8, 24, 14, 52, 7, TimeSpan.FromHours(1)), e.Timestamp);
        Assert.Equal("GET", e.Method);
        Assert.Equal("/depot/2347770/chunk/d41f1ff8fc5e9912896a0c545beb91120ef99f3b", e.Path);
        Assert.Equal(200, e.Status);
        Assert.Equal(581488, e.Bytes);
        Assert.Equal("Valve/Steam HTTP Client 1.0", e.UserAgent);
        Assert.Equal(CacheStatus.Miss, e.CacheStatus);
        Assert.Equal("cache14-fra2.steamcontent.com", e.Host);
        Assert.Null(e.Range);
    }

    [Fact]
    public void Parses_blizzard_partial_with_range()
    {
        const string line = """[blizzard] 192.168.1.111 / - - - [16/Sep/2026:22:24:59 +0100] "GET /tpr/ovw/data/00/f6/00f6bd732ee529c7692abece37d38e6b HTTP/1.1" 206 43986892 "-" "-" "MISS" "eu.cdn.blizzard.com" "bytes=122781043-229780984" """;

        Assert.True(AccessLogParser.TryParse(line.TrimEnd(), out var e));
        Assert.Equal(206, e.Status);
        Assert.Equal(43986892, e.Bytes);
        Assert.Equal("bytes=122781043-229780984", e.Range);
        Assert.Equal(new ContentKey("blizzard", "ovw"), ContentResolver.Resolve(e.CacheId, e.Host, e.Path));
    }

    [Fact]
    public void Parses_loop_detected_with_forwarded_for()
    {
        const string line = """[10.100.32.12] 172.18.0.1 / 10.100.32.11 - - [24/Aug/2025:14:38:24 +0100] "GET / HTTP/1.0" 508 0 "-" "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:142.0) Gecko/20100101 Firefox/142.0" "-" "10.100.32.12" "bytes=0-1048575" """;

        Assert.True(AccessLogParser.TryParse(line.TrimEnd(), out var e));
        Assert.Equal("10.100.32.12", e.CacheId);
        Assert.Equal(508, e.Status);
        Assert.Equal(CacheStatus.None, e.CacheStatus);
        Assert.Equal("10.100.32.11", e.ForwardedFor);
        Assert.Equal("10.100.32.11", new ClientResolver(["172.18.0.1"]).Resolve(e.RemoteAddr, e.ForwardedFor));
    }

    [Fact]
    public void Parses_heartbeat()
    {
        const string line = """[127.0.0.1] 172.18.0.1 / - - - [24/Aug/2025:15:45:54 +0100] "GET /lancache-heartbeat HTTP/1.1" 204 0 "-" "-" "-" "127.0.0.1" "-" """;

        Assert.True(AccessLogParser.TryParse(line.TrimEnd(), out var e));
        Assert.True(e.IsHeartbeat);
        Assert.Equal(204, e.Status);
    }

    [Fact]
    public void Gateway_without_forwarded_for_stays_gateway()
    {
        const string line = """[epicgames] 172.18.0.1 / - - - [24/Aug/2025:15:45:55 +0100] "GET /Builds/Fortnite/CloudDir/ChunksV4/29/1C0C4994FC529A95_A06A6D3B482CC75546F327A175A40C10.chunk HTTP/1.1" 200 978399 "-" "EpicGamesLauncher/14.6.2-24350103+++Portal+Release-Live Windows/10.0.19044.1.256.64bit" "MISS" "fastly-download.epicgames.com" "-" """;

        Assert.True(AccessLogParser.TryParse(line.TrimEnd(), out var e));
        var resolver = new ClientResolver(["172.18.0.1"]);
        Assert.Equal("172.18.0.1", resolver.Resolve(e.RemoteAddr, e.ForwardedFor));
        Assert.True(resolver.IsGateway("172.18.0.1"));
        Assert.Equal(new ContentKey("epicgames", "Fortnite"), ContentResolver.Resolve(e.CacheId, e.Host, e.Path));
    }

    [Fact]
    public void Parses_empty_cache_id_and_hit()
    {
        const string line = """[] 10.0.0.5 / - - - [11/Oct/2025:10:00:00 +0100] "GET /x HTTP/1.1" 200 10 "-" "-" "HIT" "example.com" "-" """;

        Assert.True(AccessLogParser.TryParse(line.TrimEnd(), out var e));
        Assert.Equal("", e.CacheId);
        Assert.Equal(CacheStatus.Hit, e.CacheStatus);
        Assert.Equal(new ContentKey("unknown", "example.com"), ContentResolver.Resolve(e.CacheId, e.Host, e.Path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("[steam] 1.2.3.4 / - - - [bad time] \"GET / HTTP/1.1\" 200 0")]
    public void Rejects_malformed(string line) => Assert.False(AccessLogParser.TryParse(line, out _));

    [Theory]
    [InlineData("steam", "cache1.steamcontent.com", "/depot/1149461/chunk/abc", "1149461")]
    [InlineData("epicgames", "download.epicgames.com", "/ias/fortnite/chunks/f6/x", "fortnite")]
    [InlineData("sony", "gst.prod.dl.playstation.net", "/gst/prod/00/PPSA01922_00/app/pkg/x.pkg", "PPSA01922")]
    [InlineData("riot", "lol.dyn.riotcdn.net", "/channels/public/bundles/9D60F490AC33E318.bundle", "lol")]
    [InlineData("wsus", "download.windowsupdate.com", "/d/msdownload/update/others/x.cab", "windows-update")]
    public void Resolves_content(string cacheId, string host, string path, string expected)
        => Assert.Equal(expected, ContentResolver.Resolve(cacheId, host, path).Id);
}
