using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Styling;

namespace MacroPad.App;

/// <summary>A theme the window can use: a name for the settings and the variant its tokens are keyed by.</summary>
public sealed record ThemeChoice(string Name, ThemeVariant Variant)
{
    public override string ToString() => Name;
}

/// <summary>
/// The themes in Themes/Tokens.axaml. A new theme is a <see cref="ThemeVariant"/> here that inherits Dark or Light,
/// plus a dictionary keyed by it in Tokens.axaml with the tokens it changes.
/// </summary>
public static class Themes
{
    public static IReadOnlyList<ThemeChoice> All { get; } =
    [
        new("System", ThemeVariant.Default),
        new("Dark", ThemeVariant.Dark),
        new("Light", ThemeVariant.Light),
    ];

    public static ThemeChoice Find(string? name) => All.FirstOrDefault(t => t.Name == name) ?? All[1];

    public static void Apply(ThemeChoice theme)
    {
        if (Application.Current is { } app) app.RequestedThemeVariant = theme.Variant;
    }
}
