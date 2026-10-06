using LanWatch.Shared.Contracts;

namespace LanWatch.Client.Services;

/// <summary>
/// Which LAN event every page is looking at. Null means "the latest event", which follows a new event
/// automatically when it starts; picking an event pins it.
/// </summary>
public sealed class ScopeState
{
    private const long LiveGraceSeconds = 6 * 3600;

    public IReadOnlyList<LanEventDto> Events { get; private set; } = [];
    public int? SelectedId { get; private set; }

    public event Action? Changed;

    public string Query => SelectedId is { } id ? $"event={id}" : "";

    public LanEventDto? Current => SelectedId is { } id ? Events.FirstOrDefault(e => e.Id == id) : Events.FirstOrDefault();

    /// <summary>True when the scope includes "now": live data, sign-offs and active downloads make sense.</summary>
    public bool IsLive
    {
        get
        {
            var latest = Events.FirstOrDefault();
            if (latest is null) return true; // no events yet: the default scope is the last 24 h
            if (SelectedId is { } id && id != latest.Id) return false;
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() - latest.EndUnix < LiveGraceSeconds;
        }
    }

    public void SetEvents(IReadOnlyList<LanEventDto> events)
    {
        Events = events.OrderByDescending(e => e.StartUnix).ToList();
        if (SelectedId is { } id && Events.All(e => e.Id != id)) SelectedId = null;
        Changed?.Invoke();
    }

    public void Select(int? id)
    {
        // Selecting the latest event keeps "follow latest" behaviour.
        var normalized = id == Events.FirstOrDefault()?.Id ? null : id;
        if (normalized == SelectedId) return;
        SelectedId = normalized;
        Changed?.Invoke();
    }
}
