namespace LanWatch.Shared.Parsing;

/// <summary>
/// Resolves the real client of an access-log line. When traffic reaches monolithic through Docker's userland proxy,
/// or loops back through the cache itself, <c>$remote_addr</c> is the Docker bridge gateway. The real client is then
/// in X-Forwarded-For, or lost entirely.
/// </summary>
public sealed class ClientResolver(IEnumerable<string> gatewayIps)
{
    private readonly HashSet<string> _gateways = new(gatewayIps, StringComparer.Ordinal);

    public bool IsGateway(string ip) => _gateways.Contains(ip);

    public string Resolve(string remoteAddr, string? forwardedFor)
    {
        if (forwardedFor is not null && (_gateways.Contains(remoteAddr) || remoteAddr == "127.0.0.1"))
        {
            var comma = forwardedFor.IndexOf(',');
            return (comma < 0 ? forwardedFor : forwardedFor[..comma]).Trim();
        }
        return remoteAddr;
    }
}
