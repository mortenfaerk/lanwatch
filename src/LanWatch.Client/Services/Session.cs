using System.Net;
using System.Net.Http.Json;
using LanWatch.Shared.Contracts;

namespace LanWatch.Client.Services;

/// <summary>The crew login. The server holds the cookie; the client only tracks whether it is valid.</summary>
public sealed class Session(HttpClient http)
{
    public bool Checked { get; private set; }
    public bool Authenticated { get; private set; }

    public event Action? Changed;

    public async Task CheckAsync()
    {
        try
        {
            var me = await http.GetFromJsonAsync<SessionInfo>("api/auth/me");
            Set(me?.Authenticated == true);
        }
        catch (HttpRequestException)
        {
            Set(false);
        }
    }

    /// <summary>Returns null on success, or a message the login form shows.</summary>
    public async Task<string?> LoginAsync(string password)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/auth/login", new LoginRequest(password));
            if (response.IsSuccessStatusCode)
            {
                Set(true);
                return null;
            }
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "That password is not right. Ask the crew lead for the current one.",
                HttpStatusCode.TooManyRequests => "Too many attempts. Wait a minute and try again.",
                _ => $"The server answered {(int)response.StatusCode}. Check that LanWatch is running.",
            };
        }
        catch (HttpRequestException)
        {
            return "Cannot reach the LanWatch server. Check the container is up.";
        }
    }

    public async Task LogoutAsync()
    {
        try { await http.PostAsync("api/auth/logout", null); }
        catch (HttpRequestException) { /* signing out locally is enough */ }
        Set(false);
    }

    /// <summary>Called by <see cref="Api"/> when the server rejects the cookie (expired or server restarted with a new password).</summary>
    public void Expired() => Set(false);

    private void Set(bool authenticated)
    {
        var changed = !Checked || Authenticated != authenticated;
        Checked = true;
        Authenticated = authenticated;
        if (changed) Changed?.Invoke();
    }
}
