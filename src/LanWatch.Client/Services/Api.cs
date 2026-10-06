using System.Net;
using System.Net.Http.Json;
using LanWatch.Shared.Contracts;

namespace LanWatch.Client.Services;

/// <summary>Typed access to the LanWatch API. Every query carries the selected event scope.</summary>
public sealed class Api(HttpClient http, Session session, ScopeState scope)
{
    public Task<OverviewDto?> Overview() => Get<OverviewDto>("api/overview");
    public Task<TimeSeriesDto?> TimeSeries(int points = 240, string? client = null) =>
        Get<TimeSeriesDto>($"api/timeseries?points={points}{Opt("client", client)}");
    public Task<PagedResult<DownloadDto>?> Downloads(string? client, string? service, string? q, bool active, int skip, int take) =>
        Get<PagedResult<DownloadDto>>($"api/downloads?skip={skip}&take={take}{Opt("client", client)}{Opt("service", service)}{Opt("q", q)}{(active ? "&active=true" : "")}");
    public Task<List<ClientDto>?> Clients() => Get<List<ClientDto>>("api/clients");
    public Task<List<ContentTotalDto>?> Content(string? service = null, int take = 100) =>
        Get<List<ContentTotalDto>>($"api/content?take={take}{Opt("service", service)}");
    public Task<ErrorsDto?> Errors() => Get<ErrorsDto>("api/errors");
    public Task<List<StreamHostDto>?> Stream() => Get<List<StreamHostDto>>("api/stream");
    public Task<ExceptionsDto?> Exceptions() => Get<ExceptionsDto>("api/exceptions");
    public Task<SystemDto?> System() => Get<SystemDto>("api/system", scoped: false);
    public Task<HealthDto?> Health() => Get<HealthDto>("api/health", scoped: false);
    public Task<AdGuardDto?> AdGuard() => Get<AdGuardDto>("api/adguard", scoped: false);
    public Task<SettingsDto?> Settings() => Get<SettingsDto>("api/settings", scoped: false);
    public Task<List<LanEventDto>?> Events() => Get<List<LanEventDto>>("api/events", scoped: false);

    public Task SignOff(string kind, string domain) => Send(HttpMethod.Post, "api/exceptions/signoff", new SignOffRequest(kind, domain));
    public Task RenameClient(string ip, string? name) => Send(HttpMethod.Put, $"api/clients/{Uri.EscapeDataString(ip)}/name", new ClientRename(name));
    public Task SaveSettings(SettingsDto dto) => Send(HttpMethod.Put, "api/settings", dto);
    public Task CreateEvent(LanEventUpsert e) => Send(HttpMethod.Post, "api/events", e);
    public Task UpdateEvent(int id, LanEventUpsert e) => Send(HttpMethod.Put, $"api/events/{id}", e);
    public Task DeleteEvent(int id) => Send(HttpMethod.Delete, $"api/events/{id}", null);

    private async Task<T?> Get<T>(string path, bool scoped = true)
    {
        var url = scoped && scope.Query is { Length: > 0 } q ? $"{path}{(path.Contains('?') ? '&' : '?')}{q}" : path;
        using var response = await http.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            session.Expired();
            return default;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }

    private async Task Send(HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
        using var response = await http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            session.Expired();
            throw new ApiException("Your session expired. Sign in again.");
        }
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync();
            throw new ApiException(string.IsNullOrWhiteSpace(text) ? $"The server answered {(int)response.StatusCode}." : text.Trim('"'));
        }
    }

    private static string Opt(string key, string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : $"&{key}={Uri.EscapeDataString(value)}";
}

public sealed class ApiException(string message) : Exception(message);
