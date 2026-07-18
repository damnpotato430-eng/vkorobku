using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class LauncherScannerTests
{
    [Fact]
    public void Ubisoft_CreateEntry_NormalizesForwardSlashesAndUsesUninstallName()
    {
        var entry = UbisoftScanner.CreateEntry("5595", "D:/Games/Trackmania/", "Trackmania");

        Assert.NotNull(entry);
        Assert.Equal("5595", entry.GameId);
        Assert.Equal("Trackmania", entry.Name);
        Assert.Equal(@"D:\Games\Trackmania", entry.InstallPath);
    }

    [Fact]
    public void Ubisoft_CreateEntry_WithoutUninstallRecord_FallsBackToFolderName()
    {
        var entry = UbisoftScanner.CreateEntry("101", @"G:\Games\Anno 1800\", null);

        Assert.NotNull(entry);
        Assert.Equal("Anno 1800", entry.Name);
        Assert.Equal(@"G:\Games\Anno 1800", entry.InstallPath);
    }

    [Fact]
    public void Ubisoft_CreateEntry_WithoutInstallDir_ReturnsNull()
    {
        Assert.Null(UbisoftScanner.CreateEntry("101", null, "Game"));
        Assert.Null(UbisoftScanner.CreateEntry("101", " ", "Game"));
        Assert.Null(UbisoftScanner.CreateEntry("101", "/", "Game"));
    }

    [Fact]
    public void Ea_CreateEntry_StripsTrademarkGlyphs()
    {
        var entry = EaScanner.CreateEntry("198402", @"F:\Games\Dead Space\", "Dead Space™");

        Assert.NotNull(entry);
        Assert.Equal("Dead Space", entry.Name);
        Assert.Equal(@"F:\Games\Dead Space", entry.InstallPath);
    }

    [Fact]
    public void Ea_CreateEntry_WithoutDisplayName_UsesKeyName()
    {
        var entry = EaScanner.CreateEntry("Titanfall 2", @"E:\Games\Titanfall2", null);

        Assert.NotNull(entry);
        Assert.Equal("Titanfall 2", entry.Name);
    }

    [Fact]
    public void Ea_CreateEntry_LeftoverWithoutPath_ReturnsNull()
    {
        // Uninstalled games keep a DisplayName-only registry key behind.
        Assert.Null(EaScanner.CreateEntry("198402", null, "Dead Space™"));
    }
}
