using System.IO;
using System.Windows;
using System.Windows.Media;

namespace EasyNdf.App.Units;

/// <summary>
/// The font a card's number of units is drawn in, as two app-wide resources the tiles follow: the game's own
/// (Liberator, read from its archives by <see cref="Data.Game.GameFonts"/>) once a source is read, else the app's
/// font in bold. A source read later puts its game's font in place on every tile shown.
/// </summary>
public static class UnitCountFont
{
    /// <summary>The resource keys: the font family, and its weight.</summary>
    public const string FamilyKey = "unitCountFont", WeightKey = "unitCountWeight";

    /// <summary>Uses the game's font file, or the app's font in bold when there is none or it cannot be read.</summary>
    public static void Use(string? fontFile)
    {
        ResourceDictionary resources = Application.Current.Resources;
        if (FamilyIn(fontFile) is FontFamily game)
        {
            resources[FamilyKey] = game;
            // the game's font is a medium weight of its own: made bold again, it would be smudged
            resources[WeightKey] = FontWeights.Normal;
            return;
        }
        resources[FamilyKey] = resources["ui-font"];
        resources[WeightKey] = FontWeights.Bold;
    }

    /// <summary>The font family in a font file; null when there is no file or it holds no font.</summary>
    private static FontFamily? FamilyIn(string? fontFile)
    {
        if (fontFile is null || !File.Exists(fontFile)) return null;
        try
        {
            return Fonts.GetFontFamilies(new Uri(Path.GetFullPath(fontFile))).FirstOrDefault();
        }
        catch (Exception problem) when (problem is IOException or UriFormatException or ArgumentException)
        {
            return null;
        }
    }
}
