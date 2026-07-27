using System.Globalization;
using vKOROBKU.App.Models;

namespace vKOROBKU.Tests;

/// <summary>The analysis is cached on disk, so an estimate must render its texts
/// from codes at display time. Storing the translated string would freeze the
/// language of the moment it was calculated and leak it into the other UI language.</summary>
public sealed class EstimateLocalizationTests
{
    [Fact]
    public void ConfidenceAndPerformance_FollowTheCurrentCulture()
    {
        var estimate = new CompressionEstimate(
            CompressionAlgorithm.Xpress16K, 100, 10, 20, 0.5,
            AnalysisConfidence.High, 500, PerformanceImpact.PossiblySlower, 400);

        var (russianConfidence, russianPerformance) = Render(estimate, "ru");
        var (englishConfidence, englishPerformance) = Render(estimate, "en");

        Assert.Equal("Высокая", russianConfidence);
        Assert.Equal("High", englishConfidence);
        Assert.NotEqual(russianPerformance, englishPerformance);
    }

    // A cached estimate is just a record: rehydrating it in another language must
    // produce that language, which is only true while the fields stay codes.
    [Fact]
    public void EveryConfidenceAndImpact_HasATranslationInBothCultures()
    {
        foreach (var confidence in Enum.GetValues<AnalysisConfidence>())
        {
            foreach (var impact in Enum.GetValues<PerformanceImpact>())
            {
                var estimate = new CompressionEstimate(
                    CompressionAlgorithm.Lzx, 1, 1, 1, 0.5, confidence, 100, impact, 100);
                foreach (var culture in new[] { "ru", "en" })
                {
                    var (confidenceText, impactText) = Render(estimate, culture);
                    Assert.False(string.IsNullOrWhiteSpace(confidenceText));
                    Assert.False(string.IsNullOrWhiteSpace(impactText));
                    // A missing resource falls back to the key name itself.
                    Assert.NotEqual(nameof(confidence), confidenceText);
                    Assert.DoesNotContain("Confidence_", confidenceText);
                    Assert.DoesNotContain("Perf_", impactText);
                }
            }
        }
    }

    private static (string Confidence, string Performance) Render(CompressionEstimate estimate, string culture)
    {
        var previous = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            return (estimate.ConfidenceText, estimate.PerformanceImpactText);
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = previous;
        }
    }
}
