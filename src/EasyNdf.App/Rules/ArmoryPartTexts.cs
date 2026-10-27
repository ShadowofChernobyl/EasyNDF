using EasyNdf.App.Text;
using EasyNdf.Data;

namespace EasyNdf.App.Rules;

/// <summary>The texts of the Armory's parts: their names, as their sub-tabs have them, and what their presets hold.</summary>
public static class ArmoryPartTexts
{
    /// <summary>The part's name: "Divisions".</summary>
    public static string Name(ArmoryPart part) => part switch
    {
        ArmoryPart.Divisions => Texts.Get("Armory.Divisions"),
        ArmoryPart.Battlegroups => Texts.Get("Armory.Battlegroups"),
        ArmoryPart.Weapons => Texts.Get("Armory.Weapons"),
        _ => Texts.Get("Armory.Units"),
    };

    /// <summary>What the part's Armory preset holds, and when it runs, for its line's tooltip on the Rules tab.</summary>
    public static string PresetTip(ArmoryPart part) => part switch
    {
        ArmoryPart.Divisions => Texts.Get("Rules.Armory.Divisions.Tip"),
        ArmoryPart.Battlegroups => Texts.Get("Rules.Armory.Battlegroups.Tip"),
        ArmoryPart.Weapons => Texts.Get("Rules.Armory.Weapons.Tip"),
        _ => Texts.Get("Rules.Armory.Units.Tip"),
    };
}
