using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class UpdateCheckServiceTests
{
    [Theory]
    [InlineData("v0.1.5", "0.1.5")]
    [InlineData("V1.2.3", "1.2.3")]
    [InlineData(" v0.2.0 ", "0.2.0")]
    [InlineData("0.1.5", "0.1.5")]
    public void TryParseTag_ValidTags_Parse(string tag, string expected)
    {
        Assert.True(UpdateCheckService.TryParseTag(tag, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    public void TryParseTag_InvalidTags_AreRejected(string? tag)
    {
        Assert.False(UpdateCheckService.TryParseTag(tag, out _));
    }

    [Fact]
    public void ReleaseComparison_TreatsAssemblyRevisionCorrectly()
    {
        var current = new Version(0, 1, 4, 0);

        Assert.False(new Version(0, 1, 4) > current);
        Assert.True(new Version(0, 1, 5) > current);
    }
}
