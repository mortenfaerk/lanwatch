namespace LanWatch.Shared.Parsing;

/// <summary>What a request was downloading: a Steam depot, an Epic app, a Blizzard product, a PlayStation title, ...</summary>
public readonly record struct ContentKey(string Service, string Id);

public static class ContentResolver
{
    public static ContentKey Resolve(string cacheId, string host, string path)
    {
        var service = string.IsNullOrEmpty(cacheId) ? "unknown" : cacheId;
        var id = service switch
        {
            "steam" => Segment(path, "/depot/", 0),
            "epicgames" => Segment(path, "/Builds/", 0) ?? Segment(path, "/ias/", 0),
            "blizzard" => Segment(path, "/tpr/", 0),
            "sony" => SonyTitle(path),
            "riot" => RiotGame(host),
            "wsus" => "windows-update",
            _ => null,
        };
        return new ContentKey(service, id ?? host.ToLowerInvariant());
    }

    /// <summary>Returns the path segment after <paramref name="prefix"/> (skipping <paramref name="skip"/> segments), e.g. the depot id in <c>/depot/123/chunk/x</c>.</summary>
    private static string? Segment(string path, string prefix, int skip)
    {
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var span = path.AsSpan(prefix.Length);
        for (var i = 0; i < skip; i++)
        {
            var s = span.IndexOf('/');
            if (s < 0) return null;
            span = span[(s + 1)..];
        }
        var end = span.IndexOfAny('/', '?');
        var seg = end < 0 ? span : span[..end];
        return seg.IsEmpty ? null : seg.ToString();
    }

    // /gst/prod/00/PPSA01922_00/... -> PPSA01922
    private static string? SonyTitle(string path)
    {
        var seg = Segment(path, "/gst/prod/", 1);
        if (seg is null) return null;
        var underscore = seg.IndexOf('_');
        return underscore > 0 ? seg[..underscore] : seg;
    }

    // lol.dyn.riotcdn.net -> lol, valorant.dyn.riotcdn.net -> valorant
    private static string? RiotGame(string host)
    {
        var dot = host.IndexOf('.');
        return dot > 0 ? host[..dot].ToLowerInvariant() : null;
    }
}
