namespace LanWatch.Server.Content;

/// <summary>A launcher product code we can name. <see cref="SteamAppId"/> is set when the same game is sold on Steam, so its Steam art can be reused.</summary>
public sealed record KnownProduct(string Name, long? SteamAppId = null, bool Infrastructure = false, string? StoreUrl = null);

/// <summary>
/// Battle.net, Epic and Riot have no public metadata API. Their CDN paths only carry internal product codes:
/// Blizzard's <c>/tpr/{code}/</c>, Epic's <c>/Builds/{App}/</c> and Riot's hostnames. This table names the ones
/// seen at LAN parties; anything else falls back to the raw code.
/// </summary>
public static class KnownProducts
{
    private static readonly Dictionary<string, KnownProduct> Blizzard = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ovw"] = new("Overwatch 2", 2357570, StoreUrl: "https://overwatch.blizzard.com"),
        ["pro"] = new("Overwatch 2", 2357570, StoreUrl: "https://overwatch.blizzard.com"),
        ["fenris"] = new("Diablo IV", 2344520, StoreUrl: "https://diablo4.blizzard.com"),
        ["auks"] = new("Call of Duty", 1938090, StoreUrl: "https://www.callofduty.com"),
        ["sc2"] = new("StarCraft II", StoreUrl: "https://starcraft2.blizzard.com"),
        ["s2"] = new("StarCraft II", StoreUrl: "https://starcraft2.blizzard.com"),
        ["s1"] = new("StarCraft: Remastered", StoreUrl: "https://starcraft.blizzard.com"),
        ["Hero-Live-a"] = new("Heroes of the Storm", StoreUrl: "https://heroesofthestorm.blizzard.com"),
        ["hero"] = new("Heroes of the Storm", StoreUrl: "https://heroesofthestorm.blizzard.com"),
        ["d3"] = new("Diablo III", StoreUrl: "https://diablo3.blizzard.com"),
        ["wow"] = new("World of Warcraft", StoreUrl: "https://worldofwarcraft.blizzard.com"),
        ["wow_classic"] = new("World of Warcraft Classic", StoreUrl: "https://worldofwarcraft.blizzard.com"),
        ["hs"] = new("Hearthstone", StoreUrl: "https://hearthstone.blizzard.com"),
        ["hsb"] = new("Hearthstone", StoreUrl: "https://hearthstone.blizzard.com"),
        ["w3"] = new("Warcraft III: Reforged", StoreUrl: "https://warcraft3.blizzard.com"),
        ["anbs"] = new("Diablo Immortal"),
        ["gryphon"] = new("Warcraft Rumble"),
        ["wlby"] = new("Crash Bandicoot 4: It's About Time"),
        ["rtro"] = new("Blizzard Arcade Collection"),
        // Launcher plumbing rather than games.
        ["agent"] = new("Battle.net Agent", Infrastructure: true),
        ["bna"] = new("Battle.net app", Infrastructure: true),
        ["bnt001"] = new("Battle.net app", Infrastructure: true),
        ["bnt002"] = new("Battle.net app", Infrastructure: true),
        ["bnt003"] = new("Battle.net app", Infrastructure: true),
        ["bnt004"] = new("Battle.net app", Infrastructure: true),
        ["catalogs"] = new("Battle.net catalog data", Infrastructure: true),
        ["configs"] = new("Battle.net configuration", Infrastructure: true),
    };

    private static readonly Dictionary<string, KnownProduct> Epic = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Fortnite"] = new("Fortnite", StoreUrl: "https://www.fortnite.com"),
        ["UnrealEngineLauncher"] = new("Epic Games Launcher", Infrastructure: true),
    };

    private static readonly Dictionary<string, KnownProduct> Riot = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lol"] = new("League of Legends", StoreUrl: "https://www.leagueoflegends.com"),
        ["valorant"] = new("VALORANT", StoreUrl: "https://playvalorant.com"),
        ["bacon"] = new("Legends of Runeterra"),
        ["riot-client"] = new("Riot Client", Infrastructure: true),
    };

    public static KnownProduct? Find(string service, string id) => service switch
    {
        "blizzard" => Blizzard.GetValueOrDefault(id),
        "epicgames" => Epic.GetValueOrDefault(id),
        "riot" => Riot.GetValueOrDefault(id),
        _ => null,
    };
}
