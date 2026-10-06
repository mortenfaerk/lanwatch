using System.Net;
using System.Text.Json;
using LanWatch.Server.Api;
using LanWatch.Server.Health;

namespace LanWatch.Tests.Health;

public class DnsQueryTests
{
    [Fact]
    public void Builds_a_standard_A_query()
    {
        var q = DnsQuery.BuildQuery(0x1234, "lancache.steamcontent.com");
        Assert.Equal([0x12, 0x34, 0x01, 0x00, 0x00, 0x01], q[..6]);
        Assert.Equal(8, q[12]); // "lancache"
        Assert.Equal([0, 0, 1, 0, 1], q[^5..]);
    }

    [Fact]
    public void Parses_answers_with_name_compression()
    {
        var query = DnsQuery.BuildQuery(7, "lancache.steamcontent.com");
        var response = query.ToArray();
        response[2] = 0x81; response[3] = 0x80; // response, no error
        response[7] = 2;                         // two answers
        byte[] answer(byte a, byte b, byte c, byte d) => [0xC0, 0x0C, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4, a, b, c, d];
        var msg = response.Concat(answer(10, 100, 32, 12)).Concat(answer(10, 100, 32, 13)).ToArray();

        var ips = DnsQuery.ParseAnswers(msg, 7);
        Assert.Equal([IPAddress.Parse("10.100.32.12"), IPAddress.Parse("10.100.32.13")], ips);
        Assert.Empty(DnsQuery.ParseAnswers(msg, 8)); // id mismatch
    }
}

public class AdGuardBypassTests
{
    [Theory]
    [InlineData("cache3-fra1.steamcontent.com", true)]
    [InlineData("steamcontent.com", true)]
    [InlineData("download.epicgames.com", true)]
    [InlineData("notsteamcontent.com", false)]
    [InlineData("google.com", false)]
    public void Recognises_cache_domains(string name, bool expected) =>
        Assert.Equal(expected, AdGuardPoller.IsCacheDomain(name));

    [Fact]
    public void Flags_clients_that_bypass_lancache_dns()
    {
        var now = DateTimeOffset.UtcNow;
        var json = $$$"""
        {"data":[
          {"client":"10.100.32.11","time":"{{{now:O}}}","question":{"name":"cache3-fra1.steamcontent.com","type":"A"}},
          {"client":"10.100.32.11","time":"{{{now:O}}}","question":{"name":"level3.blizzard.com","type":"A"}},
          {"client":"10.100.32.12","time":"{{{now:O}}}","question":{"name":"google.com","type":"A"}},
          {"client":"10.100.32.2","time":"{{{now:O}}}","question":{"name":"cache3-fra1.steamcontent.com","type":"A"}},
          {"client":"10.100.32.13","time":"{{{now.AddHours(-2):O}}}","question":{"name":"steamcontent.com","type":"A"}}
        ]}
        """;
        using var doc = JsonDocument.Parse(json);

        // 10.100.32.2 is lancache-dns itself (forwarding upstream); 10.100.32.13 is outside the window.
        var bypass = AdGuardPoller.FindBypass(doc.RootElement, ["10.100.32.2"], now.AddMinutes(-15)).ToList();

        var b = Assert.Single(bypass);
        Assert.Equal("10.100.32.11", b.ClientIp);
        Assert.Equal(2, b.Queries);
    }
}

public class ExceptionDomainTests
{
    [Theory]
    [InlineData("cache3-fra1.steamcontent.com", "steamcontent.com")]
    [InlineData("download.epicgames.com", "epicgames.com")]
    [InlineData("10.0.0.1", "10.0.0.1")]
    [InlineData(null, "")]
    public void Groups_hosts_by_domain(string? host, string expected) =>
        Assert.Equal(expected, ExceptionEndpoints.DomainOf(host));
}
