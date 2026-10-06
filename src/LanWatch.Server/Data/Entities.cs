using LanWatch.Shared.Parsing;

namespace LanWatch.Server.Data;

// All timestamps are unix seconds (UTC). SQLite has no native DateTimeOffset ordering, and integers keep the
// aggregate tables small and range queries cheap.

/// <summary>Cache traffic per minute, client and service. Every chart and total is derived from this table.</summary>
public class TrafficMinute
{
    public long Minute { get; set; }
    public string ClientIp { get; set; } = "";
    public string Service { get; set; } = "";
    public long HitBytes { get; set; }
    public long MissBytes { get; set; }
    public long HitRequests { get; set; }
    public long MissRequests { get; set; }
    /// <summary>Requests answered with status 5xx (incl. 508 loop detection).</summary>
    public long ErrorRequests { get; set; }
}

/// <summary>HTTP status code counts per minute and service.</summary>
public class StatusMinute
{
    public long Minute { get; set; }
    public string Service { get; set; } = "";
    public int Status { get; set; }
    public long Count { get; set; }
}

/// <summary>
/// A download session: one client pulling one piece of content (depot, app, product). A gap of more than five minutes
/// without requests starts a new session.
/// </summary>
public class DownloadSession
{
    public long Id { get; set; }
    public string ClientIp { get; set; } = "";
    public string Service { get; set; } = "";
    public string ContentId { get; set; } = "";
    public long StartUnix { get; set; }
    public long LastUnix { get; set; }
    public long HitBytes { get; set; }
    public long MissBytes { get; set; }
    public long Requests { get; set; }
}

public class Client
{
    public string Ip { get; set; } = "";
    public string? Name { get; set; }
    public long FirstSeenUnix { get; set; }
    public long LastSeenUnix { get; set; }
}

public class ErrorEvent
{
    public long Id { get; set; }
    public long Unix { get; set; }
    public ErrorSource Source { get; set; }
    public ErrorKind Kind { get; set; }
    public string? ClientIp { get; set; }
    public string? Host { get; set; }
    public string? Upstream { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>HTTPS passthrough (stream module) connections per minute, client, SNI host and status.</summary>
public class StreamMinute
{
    public long Minute { get; set; }
    public string ClientIp { get; set; } = "";
    /// <summary>Empty when the client sent no SNI.</summary>
    public string SniHost { get; set; } = "";
    public int Status { get; set; }
    public long Connections { get; set; }
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
}

/// <summary>A LAN party. Auto-detected from activity gaps, then renamed or adjusted by the crew.</summary>
public class LanEvent
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public long StartUnix { get; set; }
    public long EndUnix { get; set; }
    /// <summary>True while the detector owns the bounds; editing an event by hand clears it.</summary>
    public bool IsAuto { get; set; } = true;
}

/// <summary>When the crew last signed off an exception group (error kind + domain).</summary>
public class ExceptionSignOff
{
    public ErrorKind Kind { get; set; }
    public string Domain { get; set; } = "";
    public long SignedOffUnix { get; set; }
}

/// <summary>Small key/value store for settings the crew edits in the UI.</summary>
public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Steam depot to app mapping, imported from the SteamDepotFinder dataset.</summary>
public class SteamDepot
{
    public long DepotId { get; set; }
    public long AppId { get; set; }
    public string AppName { get; set; } = "";
}

/// <summary>How far each log file has been ingested.</summary>
public class IngestCursor
{
    public string File { get; set; } = "";
    public long Offset { get; set; }
    /// <summary>Hash of the file's first bytes, used to notice that logrotate replaced the file.</summary>
    public string Fingerprint { get; set; } = "";
    public long UpdatedUnix { get; set; }
}
