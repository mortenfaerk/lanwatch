namespace LanWatch.Shared.Contracts;

public enum UpdateState { Unknown, UpToDate, UpdateAvailable, Ahead, Error }

public sealed record CommitDto(string Sha, string Message, long Unix, string Url);

/// <summary>The running build and how it compares to the newest commit on GitHub.</summary>
public sealed record VersionDto(
    string? Commit,
    long? BuiltUnix,
    string RepositoryUrl,
    string Branch,
    UpdateState State,
    int CommitsBehind,
    IReadOnlyList<CommitDto> NewCommits,
    string? Error,
    long? CheckedUnix)
{
    public string? ShortCommit => Commit is { Length: >= 7 } c ? c[..7] : Commit;
}
