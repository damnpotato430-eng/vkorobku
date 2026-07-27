namespace vKOROBKU.App.Services;

/// <summary>The one list of shipped interface languages. The startup culture, the
/// settings combo and the resource parity tests all read it from here, so adding a
/// language cannot half-land — previously the codes were spelled out in three places
/// and a forgotten one would fail silently at runtime.</summary>
public static class AppLanguages
{
    public const string Auto = "auto";

    /// <param name="IsBase">English lives in the neutral Strings.resx rather than a
    /// satellite, so it is offered to the user but has no separate resource set.</param>
    public sealed record Language(string Code, string DisplayName, bool IsBase = false);

    /// <summary>Display names are written in their own language on purpose and are
    /// never translated: someone hunting for their language recognises it by sight,
    /// especially when the current interface is in a language they cannot read.</summary>
    public static IReadOnlyList<Language> All { get; } =
    [
        new("en", "English", IsBase: true),
        new("ru", "Русский"),
        new("de", "Deutsch"),
        new("es", "Español"),
        new("fr", "Français"),
        new("pt-BR", "Português (Brasil)"),
        new("pl", "Polski"),
        new("tr", "Türkçe"),
        new("ja", "日本語"),
        new("ko", "한국어"),
        new("zh-Hans", "简体中文")
    ];

    /// <summary>Languages shipped as satellite resources — everything except the base.</summary>
    public static IReadOnlyList<Language> Satellites { get; } =
        All.Where(language => !language.IsBase).ToArray();

    /// <summary>True for a code the app can actually switch to. "auto" is not one of
    /// them: it means "leave whatever Windows chose" rather than a culture to apply.</summary>
    public static bool IsSelectable(string? code) =>
        code is not null &&
        All.Any(language => string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase));
}
