using System.Reflection;
using System.Text.Json;
using LanWatch.Shared.Contracts;
using Microsoft.Extensions.Options;

namespace LanWatch.Server.Api;

/// <summary>
/// Knows which commit this build came from (the SDK stamps it into the informational version as <c>1.0.0+sha</c>)
/// and asks GitHub's compare API how far the update branch has moved since. Results are cached, because the
/// unauthenticated GitHub API allows 60 requests per hour.
/// </summary>
public sealed class UpdateChecker(IHttpClientFactory httpFactory, IOptions<LanWatchOptions> options, TimeProvider time, ILogger<UpdateChecker> logger)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);
    private static readonly TimeSpan MinManualInterval = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1);
    private VersionDto? _cached;

    public static string? Commit { get; } = ReadCommit();

    public static long? BuiltUnix { get; } = ReadBuildTime();

    public async Task<VersionDto> GetAsync(bool refresh, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (_cached?.CheckedUnix is { } checkedAt)
        {
            var age = now - DateTimeOffset.FromUnixTimeSeconds(checkedAt);
            if (age < (refresh ? MinManualInterval : CacheFor)) return _cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            return _cached = await CheckAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<VersionDto> CheckAsync(CancellationToken ct)
    {
        var o = options.Value;
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        VersionDto Result(UpdateState state, int behind = 0, IReadOnlyList<CommitDto>? commits = null, string? error = null) =>
            new(Commit, BuiltUnix, o.RepositoryUrl, o.UpdateBranch, state, behind, commits ?? [], error, now);

        if (Commit is null)
            return Result(UpdateState.Unknown, error: "This build does not know its commit (built without the .git folder).");
        if (!TryParseRepo(o.RepositoryUrl, out var owner, out var repo))
            return Result(UpdateState.Unknown, error: $"Cannot read a GitHub owner/repo from {o.RepositoryUrl}.");

        try
        {
            var http = httpFactory.CreateClient("github");
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repo}/compare/{Commit}...{o.UpdateBranch}");
            // A private repository is invisible to anonymous API calls; a read-only token makes it visible.
            if (!string.IsNullOrWhiteSpace(o.GitHubToken))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", o.GitHubToken);
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return Result(UpdateState.Unknown, error: string.IsNullOrWhiteSpace(o.GitHubToken)
                    ? "GitHub answered 404: the repository is private (set GITHUB_TOKEN to a read-only token) or this commit was never pushed."
                    : "GitHub answered 404: this commit is not on GitHub, or the token cannot read the repository.");
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden && !string.IsNullOrWhiteSpace(o.GitHubToken))
                return Result(UpdateState.Error, error: "GitHub rejected GITHUB_TOKEN (expired, or missing read access to the repository).");
            if (!response.IsSuccessStatusCode)
                return Result(UpdateState.Error, error: $"GitHub answered {(int)response.StatusCode}.");

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            var status = root.GetProperty("status").GetString();
            var aheadBy = root.GetProperty("ahead_by").GetInt32();
            var commits = root.GetProperty("commits").EnumerateArray()
                .Select(c =>
                {
                    var commit = c.GetProperty("commit");
                    var message = commit.GetProperty("message").GetString() ?? "";
                    var date = commit.GetProperty("committer").GetProperty("date").GetDateTimeOffset();
                    return new CommitDto(c.GetProperty("sha").GetString()!, message.Split('\n')[0], date.ToUnixTimeSeconds(),
                        c.GetProperty("html_url").GetString()!);
                })
                .Reverse() // newest first
                .Take(10)
                .ToList();

            // compare/{base}...{head}: "ahead" means the branch (head) has commits this build (base) lacks.
            return status switch
            {
                "identical" => Result(UpdateState.UpToDate),
                "ahead" => Result(UpdateState.UpdateAvailable, aheadBy, commits),
                "behind" => Result(UpdateState.Ahead),
                "diverged" => Result(UpdateState.UpdateAvailable, aheadBy, commits),
                _ => Result(UpdateState.Unknown, error: $"Unexpected compare status '{status}'."),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            logger.LogDebug("Update check failed: {Message}", ex.Message);
            return Result(UpdateState.Error, error: "Could not reach GitHub. Is the VM online?");
        }
    }

    internal static bool TryParseRepo(string url, out string owner, out string repo)
    {
        owner = repo = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2) return false;
        owner = parts[0];
        repo = parts[1].EndsWith(".git") ? parts[1][..^4] : parts[1];
        return true;
    }

    private static string? ReadCommit()
    {
        var info = typeof(UpdateChecker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = info?.IndexOf('+') ?? -1;
        return plus < 0 ? null : info![(plus + 1)..];
    }

    private static long? ReadBuildTime()
    {
        var path = typeof(UpdateChecker).Assembly.Location;
        return string.IsNullOrEmpty(path) ? null : new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
    }
}
