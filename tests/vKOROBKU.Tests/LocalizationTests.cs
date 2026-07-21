using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using vKOROBKU.App.Resources;

namespace vKOROBKU.Tests;

/// <summary>Guards the localization invariants: every key exists in the English base
/// and the Russian satellite, and the hand-written accessor stays in sync with the
/// resx files — adding a language or a string cannot silently drift.</summary>
public sealed class LocalizationTests
{
    private static readonly string[] TranslatedCultures = ["ru"];

    [Fact]
    public void EverySatellite_CoversExactlyTheBaseKeys()
    {
        var baseKeys = ReadKeys(CultureInfo.InvariantCulture);
        Assert.NotEmpty(baseKeys);
        foreach (var culture in TranslatedCultures)
        {
            var satelliteKeys = ReadKeys(CultureInfo.GetCultureInfo(culture));
            Assert.Equal(
                baseKeys.OrderBy(key => key, StringComparer.Ordinal),
                satelliteKeys.OrderBy(key => key, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void AccessorProperties_MatchBaseKeys()
    {
        var properties = typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name)
            .ToList();
        var baseKeys = ReadKeys(CultureInfo.InvariantCulture);

        Assert.Equal(
            baseKeys.OrderBy(key => key, StringComparer.Ordinal),
            properties.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryValue_IsNonEmpty_InEveryCulture()
    {
        var cultures = new[] { CultureInfo.InvariantCulture }
            .Concat(TranslatedCultures.Select(CultureInfo.GetCultureInfo));
        foreach (var culture in cultures)
        {
            foreach (var key in ReadKeys(CultureInfo.InvariantCulture))
            {
                var value = Strings.ResourceManager.GetString(key, culture);
                Assert.False(string.IsNullOrWhiteSpace(value), $"Пустое значение {key} для культуры «{culture.Name}»");
            }
        }
    }

    // A translation that loses or invents a {N} placeholder either silently drops part
    // of the message or throws FormatException at runtime — on the hot path of
    // operation summaries. The placeholder sets must match the English base exactly.
    [Fact]
    public void Placeholders_MatchTheBase_InEveryCulture()
    {
        foreach (var key in ReadKeys(CultureInfo.InvariantCulture))
        {
            var basePlaceholders = ExtractPlaceholders(
                Strings.ResourceManager.GetString(key, CultureInfo.InvariantCulture)!);
            foreach (var culture in TranslatedCultures)
            {
                var translated = ExtractPlaceholders(
                    Strings.ResourceManager.GetString(key, CultureInfo.GetCultureInfo(culture))!);
                Assert.True(
                    basePlaceholders.SetEquals(translated),
                    $"Плейсхолдеры ключа {key} расходятся для культуры «{culture}»: " +
                    $"база [{string.Join(" ", basePlaceholders)}], перевод [{string.Join(" ", translated)}]");
            }
        }
    }

    private static HashSet<string> ExtractPlaceholders(string value)
    {
        var placeholders = new HashSet<string>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(value, @"\{\d+\}"))
            placeholders.Add(match.Value);
        return placeholders;
    }

    private static HashSet<string> ReadKeys(CultureInfo culture)
    {
        var resourceSet = Strings.ResourceManager.GetResourceSet(culture, true, tryParents: false);
        Assert.NotNull(resourceSet);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in resourceSet)
            keys.Add((string)entry.Key);
        return keys;
    }
}
