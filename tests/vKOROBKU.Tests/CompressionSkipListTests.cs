using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class CompressionSkipListTests
{
    [Fact]
    public void DefaultList_ContainsKnownMediaAndArchives()
    {
        Assert.Contains(".bik", CompressionSkipList.DefaultExtensions);
        Assert.Contains(".bk2", CompressionSkipList.DefaultExtensions);
        Assert.Contains(".mp4", CompressionSkipList.DefaultExtensions);
        Assert.Contains(".zip", CompressionSkipList.DefaultExtensions);
    }

    [Fact]
    public void DefaultList_DoesNotContainPak()
    {
        Assert.DoesNotContain(".pak", CompressionSkipList.DefaultExtensions);
    }

    [Fact]
    public void BuildEffectiveExtensions_MergesUserAdditionsWithoutDuplicates()
    {
        var preferences = new UserPreferences(UserSkipExtensions: [".pak", ".MP4"]);

        var effective = CompressionSkipList.BuildEffectiveExtensions(preferences);

        Assert.NotNull(effective);
        Assert.Contains(".pak", effective);
        Assert.Single(effective, extension => string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildEffectiveExtensions_Disabled_ReturnsNull()
    {
        var preferences = new UserPreferences(SkipNonCompressable: false, UserSkipExtensions: [".pak"]);

        Assert.Null(CompressionSkipList.BuildEffectiveExtensions(preferences));
    }

    [Theory]
    [InlineData("pak", ".pak")]
    [InlineData(".PAK", ".pak")]
    [InlineData(" .bk2 ", ".bk2")]
    public void TryNormalizeExtension_ValidInput_Normalizes(string input, string expected)
    {
        Assert.True(CompressionSkipList.TryNormalizeExtension(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData(".a.b")]
    [InlineData(".with space")]
    [InlineData(".verylongextension1")]
    public void TryNormalizeExtension_InvalidInput_IsRejected(string input)
    {
        Assert.False(CompressionSkipList.TryNormalizeExtension(input, out _));
    }
}
