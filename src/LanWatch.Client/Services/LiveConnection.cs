using LanWatch.Shared.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace LanWatch.Client.Services;

public enum LiveStatus { Offline, Connecting, Connected, Reconnecting }

/// <summary>The SignalR feed: a tick every ~2 s and a "data changed" nudge when new log lines land.</summary>
public sealed class LiveConnection(NavigationManager nav) : IAsyncDisposable
{
    private HubConnection? _hub;

    public LiveStatus Status { get; private set; } = LiveStatus.Offline;
    public LiveTick? Last { get; private set; }

    public event Action<LiveTick>? Tick;
    public event Action? DataChanged;
    public event Action? StatusChanged;

    public async Task StartAsync()
    {
        if (_hub is not null) return;
        _hub = new HubConnectionBuilder()
            .WithUrl(nav.ToAbsoluteUri(LiveHubContract.Path))
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)])
            .Build();

        _hub.On<LiveTick>(LiveHubContract.Tick, t =>
        {
            Last = t;
            Tick?.Invoke(t);
        });
        _hub.On(LiveHubContract.DataChanged, () => DataChanged?.Invoke());
        _hub.Reconnecting += _ => { SetStatus(LiveStatus.Reconnecting); return Task.CompletedTask; };
        _hub.Reconnected += _ => { SetStatus(LiveStatus.Connected); return Task.CompletedTask; };
        _hub.Closed += async _ =>
        {
            SetStatus(LiveStatus.Offline);
            // Automatic reconnect gave up (e.g. the VM rebooted); keep trying slowly.
            await Task.Delay(TimeSpan.FromSeconds(15));
            await TryStartAsync();
        };

        await TryStartAsync();
    }

    private async Task TryStartAsync()
    {
        if (_hub is null) return;
        SetStatus(LiveStatus.Connecting);
        try
        {
            await _hub.StartAsync();
            SetStatus(LiveStatus.Connected);
        }
        catch (Exception)
        {
            SetStatus(LiveStatus.Offline);
        }
    }

    public async Task StopAsync()
    {
        if (_hub is null) return;
        var hub = _hub;
        _hub = null;
        await hub.DisposeAsync();
        Last = null;
        SetStatus(LiveStatus.Offline);
    }

    private void SetStatus(LiveStatus status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }

    public ValueTask DisposeAsync() => _hub?.DisposeAsync() ?? ValueTask.CompletedTask;
}
