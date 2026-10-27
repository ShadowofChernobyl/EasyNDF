using EasyNdf.App.Text;
using EasyNdf.Data;

namespace EasyNdf.App.Rules;

/// <summary>The letters the Files tab's Armory column shows for the Armory's parts: D, B, U and W.</summary>
public static class ArmoryLetterTexts
{
    /// <summary>A part's letter: "D" for Divisions.</summary>
    public static string Letter(ArmoryPart part) => part switch
    {
        ArmoryPart.Divisions => Texts.Get("Files.Armory.Divisions.Letter"),
        ArmoryPart.Battlegroups => Texts.Get("Files.Armory.Battlegroups.Letter"),
        ArmoryPart.Weapons => Texts.Get("Files.Armory.Weapons.Letter"),
        _ => Texts.Get("Files.Armory.Units.Letter"),
    };
}
