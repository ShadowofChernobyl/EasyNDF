using System.Text.Json.Nodes;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data;
using EasyNdf.Data.FileList;

namespace EasyNdf.App;

/// <summary>What the usage readout (Settings, Debug) asks of the tabs: where the user is, and what holds the memory.</summary>
public partial class MainView
{
    /// <summary>
    /// About how much the Armory's game data takes for each unit: its values, weapons and loadouts, and its share of
    /// the divisions and weapons read with it. Measured on WARNO with Paul's mods, 2,929
    /// units, November 2026 (about 35 MB).
    /// </summary>
    private const long ArmoryBytesPerUnit = 12 * 1024;

    /// <summary>
    /// Each rule set's worked-out size while in use, with how many rules it had then: worked out again only when that
    /// changes, as going through a mod's thousands of rules every second would itself slow the app.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ModPreset, Tuple<int, long>> ruleSetSizes = [];

    /// <summary>
    /// About how much a rule held as JSON objects takes per character of its text in the rule window: measured from 1.3
    /// (long texts, as Add rules have) to 1.9 (short Edit rules), November 2026.
    /// </summary>
    private const double RuleBytesPerCharacter = 1.6;

    /// <summary>
    /// The same for rules read from a mod file, which are kept as the file's own values (beside the file itself, see
    /// <see cref="FileKeptBytes"/>): 3.7, measured on WARNO ACTUAL and Yokaiste's Sandbox Mod, November 2026.
    /// </summary>
    private const double ReadRuleBytesPerCharacter = 3.7;

    /// <summary>The tab the user is on, and the Armory's sub-tab, such as "Armory › Divisions".</summary>
    private string WhereTheUserIs()
    {
        string tab = (Tabs.SelectedItem as TabItem)?.Header as string ?? "";
        return Tabs.SelectedItem == UnitsTabItem ? tab + " › " + UnitsTab.ShownPartName : tab;
    }

    /// <summary>
    /// The parts of the app's memory: those it can count (the files on the Files tab, packed as they are kept; the rule
    /// sets, packed or about what their rules take while in use; the Armory's game data, and its units as the mods make
    /// them, about, from their units; the unit
    /// pictures kept; the File Editor's text; the Undo history and the mods' Armory changes, about), then the rest split
    /// by what .NET says: its other data, what is no longer in use but not yet freed, what it holds ready unused, and what is outside it (drawing, the pictures as
    /// drawn, Windows' share). The counted parts other than the pictures are .NET's data, so they are taken from it.
    /// </summary>
    private IReadOnlyList<MemoryPart> MemoryParts(UsageReading reading)
    {
        long files = FilesTab.Model.Entries.Sum(entry =>
            (entry.ArchivedContent?.PackedLength ?? 0) + (entry.FirstContent?.PackedLength ?? 0) + 2L * (entry.HeldOutput?.Length ?? 0));
        long ruleSets = AppData.Current.Mods.Sum(mod => mod.Presets.Sum(preset => RuleSetBytes(preset, mod.ReadFromCharacters > 0)) + FileKeptBytes(mod));
        long armory = UnitsTab.AllUnits.Count * ArmoryBytesPerUnit;
        long armoryModded = UnitsTab.ModdedUnitCount * ArmoryBytesPerUnit;
        long pictures = Units.UnitPictureLibrary.Current?.BytesInUse ?? 0;
        long editor = 2L * (EditorTab.Text.Document?.TextLength ?? 0);
        long undo = History.AboutBytes();
        long armoryChanges = AppData.Current.Mods.Sum(ArmoryChangeBytes);
        long counted = files + ruleSets + armory + armoryModded + editor + undo + armoryChanges;
        long waiting = Math.Min(reading.DotNetWaiting, Math.Max(0, reading.DotNet - counted));
        long otherDotNet = Math.Max(0, reading.DotNet - waiting - counted);
        long unused = Math.Max(0, reading.DotNetHeld - reading.DotNet);
        long outside = Math.Max(0, reading.Memory - Math.Max(reading.DotNetHeld, counted) - pictures);
        return
        [
            new(Texts.Get("Main.Usage.Part.Files"), files),
            new(Texts.Get("Main.Usage.Part.RuleSets"), ruleSets, IsAbout: true),
            new(Texts.Get("Main.Usage.Part.Armory"), armory, IsAbout: true),
            new(Texts.Get("Main.Usage.Part.ArmoryModded"), armoryModded, IsAbout: true),
            new(Texts.Get("Main.Usage.Part.Pictures"), pictures),
            new(Texts.Get("Main.Usage.Part.Editor"), editor),
            new(Texts.Get("Main.Usage.Part.Undo"), undo, IsAbout: true),
            new(Texts.Get("Main.Usage.Part.ArmoryChanges"), armoryChanges, IsAbout: true),
            new(Texts.Get("Main.Usage.Part.OtherDotNet"), otherDotNet),
            new(Texts.Get("Main.Usage.Part.Waiting"), waiting, IsAbout: true),
            new(Texts.Get("Main.Usage.Part.DotNetUnused"), unused),
            new(Texts.Get("Main.Usage.Part.OutsideDotNet"), outside),
        ];
    }

    /// <summary>
    /// Each mod's worked-out Armory changes, with their counts then: worked out again only when those change, as writing
    /// out a mod's divisions every second would itself slow the app.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Mod, Tuple<string, long>> armoryChangeSizes = [];

    /// <summary>About what a mod's Armory changes take: its divisions, units, unit and weapon changes and weapons.</summary>
    private static long ArmoryChangeBytes(Mod mod)
    {
        string counts = $"{mod.Divisions.Count} {mod.Units.Count} {mod.UnitEdits.Count} {mod.WeaponEdits.Count} {mod.Weapons.Count}";
        if (armoryChangeSizes.TryGetValue(mod, out Tuple<string, long>? known) && known.Item1 == counts) return known.Item2;
        long characters = mod.Divisions.Sum(each => CharactersIn(each.ToJson())) + mod.Units.Sum(each => CharactersIn(each.ToJson()))
            + mod.UnitEdits.Sum(each => CharactersIn(each.ToJson())) + mod.WeaponEdits.Sum(each => CharactersIn(each.ToJson()))
            + mod.Weapons.Sum(each => CharactersIn(each.ToJson()));
        long bytes = (long)(characters * RuleBytesPerCharacter);
        armoryChangeSizes.AddOrUpdate(mod, Tuple.Create(counts, bytes));
        return bytes;
    }

    /// <summary>
    /// A rule set's memory: its packed size while put away; while in use, about what its rules take, worked out from the
    /// length of their texts without writing them out (a mod's thousands of rules would take a moment).
    /// </summary>
    /// <param name="ruleSet">The rule set.</param>
    /// <param name="readFromFile">Whether its mod was read from a mod file (see <see cref="ReadRuleBytesPerCharacter"/>).</param>
    private static long RuleSetBytes(ModPreset ruleSet, bool readFromFile)
    {
        if (ruleSet.PackedLength is long packed) return packed;
        JsonArray rules = ruleSet.Rules;
        if (ruleSetSizes.TryGetValue(ruleSet, out Tuple<int, long>? known) && known.Item1 == rules.Count) return known.Item2;
        long characters = 0;
        foreach (JsonNode? rule in rules) characters += CharactersIn(rule);
        long bytes = (long)(characters * (readFromFile ? ReadRuleBytesPerCharacter : RuleBytesPerCharacter));
        ruleSetSizes.AddOrUpdate(ruleSet, Tuple.Create(rules.Count, bytes));
        return bytes;
    }

    /// <summary>
    /// About what a mod's file takes while kept: rules read from a mod file keep the whole file in memory, with its index,
    /// while any rule set of the mod is in use (not put away), whatever the others. Measured on WARNO ACTUAL (51 MB of
    /// text kept as 67 MB) and Yokaiste's Sandbox Mod (14 MB as 23 MB), November 2026.
    /// </summary>
    private static long FileKeptBytes(Mod mod) =>
        mod.ReadFromCharacters > 0 && mod.Presets.Any(preset => !preset.IsPacked) ? (long)(mod.ReadFromCharacters * FileKeptPerCharacter) : 0;

    /// <summary>What a kept mod file takes per character of its text (see <see cref="FileKeptBytes"/>).</summary>
    private const double FileKeptPerCharacter = 1.4;

    /// <summary>About how many characters a rule's JSON holds: its texts' lengths, and a few for each name and value.</summary>
    private static long CharactersIn(JsonNode? node) => node switch
    {
        JsonObject values => values.Sum(pair => pair.Key.Length + 4 + CharactersIn(pair.Value)),
        JsonArray items => items.Sum(item => 2 + CharactersIn(item)),
        JsonValue value when value.TryGetValue(out string? text) => text.Length + 2,
        JsonValue => 6,
        _ => 4,
    };
}
