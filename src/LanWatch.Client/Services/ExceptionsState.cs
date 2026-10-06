using LanWatch.Shared.Contracts;

namespace LanWatch.Client.Services;

/// <summary>
/// The open exceptions for the current scope. Shared by the rail badge, the overview band and the errors page.
/// Refreshes when the scope changes, when new errors arrive on the live feed, and when new log data lands.
/// </summary>
public sealed class ExceptionsState : IDisposable
{
    private readonly Api _api;
    private readonly ScopeState _scope;
    private readonly LiveConnection _live;
    private HashSet<string> _openBefore = [];
    private bool _loadedOnce;
    private bool _loading;

    public ExceptionsDto? Current { get; private set; }
    public bool Failed { get; private set; }

    /// <summary>Keys that appeared since the last refresh; the band stamps them in.</summary>
    public IReadOnlySet<string> Fresh { get; private set; } = new HashSet<string>();

    public int OpenCount => Current?.Items.Count(i => i.Open) ?? 0;

    public event Action? Changed;

    public ExceptionsState(Api api, ScopeState scope, LiveConnection live)
    {
        _api = api;
        _scope = scope;
        _live = live;
        _scope.Changed += OnScopeChanged;
        _live.Tick += OnTick;
        _live.DataChanged += OnDataChanged;
    }

    public static string Key(ExceptionDto e) => $"{e.Kind}|{e.Domain}";

    public async Task RefreshAsync(bool resetSeen = false)
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var result = await _api.Exceptions();
            if (result is null) return;
            if (resetSeen) _loadedOnce = false;
            // Fresh = open now but not open last time: a new exception, or one that re-opened after sign-off.
            var openNow = result.Items.Where(i => i.Open).Select(Key).ToHashSet();
            Fresh = _loadedOnce ? openNow.Except(_openBefore).ToHashSet() : [];
            _openBefore = openNow;
            _loadedOnce = true;
            Current = result;
            Failed = false;
        }
        catch (HttpRequestException)
        {
            Failed = true;
        }
        finally
        {
            _loading = false;
        }
        Changed?.Invoke();
    }

    public async Task SignOffAsync(ExceptionDto item)
    {
        await _api.SignOff(item.Kind, item.Domain);
        await RefreshAsync();
    }

    private async void OnScopeChanged()
    {
        Current = null;
        await RefreshAsync(resetSeen: true);
    }

    private async void OnTick(LiveTick tick)
    {
        if (tick.NewErrors > 0 && _scope.IsLive) await RefreshAsync();
    }

    private async void OnDataChanged() => await RefreshAsync();

    public void Dispose()
    {
        _scope.Changed -= OnScopeChanged;
        _live.Tick -= OnTick;
        _live.DataChanged -= OnDataChanged;
    }
}
