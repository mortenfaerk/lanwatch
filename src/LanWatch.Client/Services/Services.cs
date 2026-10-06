namespace LanWatch.Client.Services;

/// <summary>Lancache service identifiers, in the fixed colour order the charts use.</summary>
public static class Svc
{
    /// <summary>All known services, in display order.</summary>
    public static readonly string[] Order = ["steam", "epicgames", "blizzard", "wsus", "sony", "riot"];

    /// <summary>Services with their own hue; the rest share the neutral "other" swatch and rely on their label.</summary>
    private static readonly string[] Coloured = ["steam", "epicgames", "blizzard", "wsus"];

    public static string Name(string id) => id switch
    {
        "steam" => "Steam",
        "epicgames" => "Epic Games",
        "blizzard" => "Battle.net",
        "wsus" => "Windows Update",
        "sony" => "PlayStation",
        "riot" => "Riot",
        "origin" => "EA",
        "uplay" => "Ubisoft",
        "" or "unknown" => "Unknown",
        _ => id,
    };

    /// <summary>A CSS custom property assignment, e.g. <c>--svc: var(--svc-steam)</c>.</summary>
    public static string Var(string id) => Coloured.Contains(id) ? $"--svc: var(--svc-{id})" : "--svc: var(--svc-other)";
}
