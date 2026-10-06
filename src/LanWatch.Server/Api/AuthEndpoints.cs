using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LanWatch.Shared.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace LanWatch.Server.Api;

/// <summary>A single shared crew password (<c>LANWATCH_PASSWORD</c>) traded for a 7-day sliding cookie.</summary>
public static class AuthEndpoints
{
    public const string LoginRateLimit = "login";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapGet("/me", (ClaimsPrincipal user) => new SessionInfo(user.Identity?.IsAuthenticated == true))
            .AllowAnonymous();

        auth.MapPost("/login", async (LoginRequest request, HttpContext http, IOptions<LanWatchOptions> options) =>
        {
            if (!PasswordMatches(request.Password, options.Value.Password))
            {
                await Task.Delay(Random.Shared.Next(200, 600)); // blunt timing and brute force
                return Results.Unauthorized();
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "crew")], CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true });
            return Results.Ok(new SessionInfo(true));
        }).AllowAnonymous().RequireRateLimiting(LoginRateLimit);

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new SessionInfo(false));
        }).AllowAnonymous();
    }

    private static bool PasswordMatches(string? given, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(given ?? "")),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
