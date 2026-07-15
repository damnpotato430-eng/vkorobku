using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class EpicGamesScannerTests
{
    [Fact]
    public void ParseManifest_RealGame_IsParsed()
    {
        const string json = """
        {
            "bIsIncompleteInstall": false,
            "AppCategories": ["public", "games", "applications"],
            "DisplayName": "Cyberpunk 2077",
            "InstallLocation": "F:\\Games\\Cyberpunk2077",
            "InstallSize": 68719476736,
            "AppVersionString": "Build_5044915"
        }
        """;

        var manifest = EpicGamesScanner.ParseManifest(json);

        Assert.NotNull(manifest);
        Assert.Equal("Cyberpunk 2077", manifest.Name);
        Assert.Equal(@"F:\Games\Cyberpunk2077", manifest.InstallPath);
        Assert.Equal(68719476736, manifest.SizeBytes);
        Assert.Equal("Build_5044915", manifest.Version);
    }

    [Fact]
    public void ParseManifest_DigitalExtrasAddon_IsExcluded()
    {
        const string json = """
        {
            "AppCategories": ["digitalextras", "applications"],
            "DisplayName": "Cyberpunk 2077 - REDmod",
            "InstallLocation": "F:\\Games\\Cyberpunk2077",
            "InstallSize": 95616643,
            "MainGameAppName": "Ginger"
        }
        """;

        Assert.Null(EpicGamesScanner.ParseManifest(json));
    }

    [Fact]
    public void ParseManifest_IncompleteInstall_IsExcluded()
    {
        const string json = """
        {
            "bIsIncompleteInstall": true,
            "AppCategories": ["games"],
            "DisplayName": "Still Downloading",
            "InstallLocation": "F:\\Games\\Pending"
        }
        """;

        Assert.Null(EpicGamesScanner.ParseManifest(json));
    }

    [Fact]
    public void ParseManifest_MissingInstallLocation_IsExcluded()
    {
        const string json = """
        {
            "AppCategories": ["games"],
            "DisplayName": "No Path Game"
        }
        """;

        Assert.Null(EpicGamesScanner.ParseManifest(json));
    }

    [Fact]
    public void ParseManifest_MissingSize_DefaultsToZero()
    {
        const string json = """
        {
            "AppCategories": ["games"],
            "DisplayName": "Sizeless Game",
            "InstallLocation": "C:\\Games\\Sizeless"
        }
        """;

        var manifest = EpicGamesScanner.ParseManifest(json);

        Assert.NotNull(manifest);
        Assert.Equal(0, manifest.SizeBytes);
        Assert.Null(manifest.Version);
    }
}
