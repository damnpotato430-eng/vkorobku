using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class UiScaleTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(110)]
    [InlineData(125)]
    [InlineData(150)]
    public void SupportedPercents_SurviveNormalization(int percent) =>
        Assert.Equal(percent, UiScale.Normalize(percent));

    // Anything unexpected — a hand-edited preferences file, a value from a future
    // version — falls back to "whatever Windows says" rather than a broken layout.
    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    [InlineData(75)]
    [InlineData(137)]
    [InlineData(400)]
    public void UnsupportedPercent_FallsBackToWindowsScaling(int percent) =>
        Assert.Equal(100, UiScale.Normalize(percent));

    [Fact]
    public void DefaultPreference_AddsNoScalingOfItsOwn()
    {
        // Users already running Windows at 125% or 150% must not be scaled twice,
        // so an untouched installation applies no extra zoom.
        Assert.Equal(100, new UserPreferences().UiScalePercent);
        Assert.Equal(100, UiScale.Normalize(new UserPreferences().UiScalePercent));
    }

    [Fact]
    public void HundredPercentIsOffered_SoTheScalingCanBeUndone()
    {
        Assert.Contains(100, UiScale.SupportedPercents);
        Assert.Equal(100, UiScale.SupportedPercents[0]);
    }
}
