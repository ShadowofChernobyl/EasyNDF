using System.IO;
using System.Text.Json.Nodes;
using EasyNdf.App.Files;
using EasyNdf.App.Mods;
using EasyNdf.Data;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Tests;

/// <summary>
/// What a Preview finds of two mods changing the same value (the Mod Conflicts window): each change is put down to the
/// right mod and rule, also when a mod's rules load as more rules than it lists (an "Add descriptor" action becomes an
/// Add rule of its own), which is what splits the run's logs between the mods.
/// </summary>
public sealed class ModConflictsRunTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "EasyNdf.App.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<Mod> modsMade = [];

    public ModConflictsRunTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        foreach (Mod mod in modsMade)
        {
            ModStack.SetOn(mod, false);
            AppData.Current.ModStore.Delete(mod);
        }
        ModConflictList.Forget();
        Directory.Delete(folder, recursive: true);
    }

    /// <summary>A mod, switched on, with a rule set for Ammunition.ndf (by its name) of these rules.</summary>
    private Mod ModWith(string name, params JsonObject[] rules)
    {
        var mod = new Mod { Name = name, Presets = [new ModPreset { Name = name + " (Ammunition.ndf)", Rules = [.. rules] }] };
        AppData.Current.ModStore.Add(mod);
        modsMade.Add(mod);
        ModStack.SetOn(mod, true);
        return mod;
    }

    /// <summary>An Edit rule for the descriptors whose name holds "Ammo", with these actions.</summary>
    private static JsonObject EditRule(string name, params JsonObject[] actions) => new()
    {
        ["Name"] = name,
        ["Kind"] = "Edit",
        ["LogicGate"] = "Any",
        ["Conditions"] = new JsonArray(new JsonObject { ["Operand"] = "Descriptor Title", ["Operator"] = "Contains", ["Value"] = "Ammo" }),
        ["Actions"] = new JsonArray([.. actions]),
    };

    private static JsonObject SetTo(string target, string value) => new() { ["Target"] = target, ["Operator"] = "Set To", ["Value"] = value };

    [Fact]
    public async Task Each_change_is_put_down_to_its_own_mod_and_rule()
    {
        // the first mod's one rule loads as two: the edit, then an Add rule made from its "Add descriptor" action
        Mod first = ModWith("Conflict run test A",
            EditRule("A sets X", SetTo("X", "2"), new JsonObject { ["Target"] = "New_Thing", ["Operator"] = "Add descriptor", ["Value"] = "export New_Thing is TThing ( X = 9 )" }),
            EditRule("A sets Y", SetTo("Y", "5")));
        Mod second = ModWith("Conflict run test B", EditRule("B sets X", SetTo("X", "3")), EditRule("B sets Y", SetTo("Y", "6")));
        string path = Path.Combine(folder, "Ammunition.ndf");
        File.WriteAllText(path, "export Ammo_One is TAmmo\n(\n    X = 1\n    Y = 4\n)\n");
        var model = new FileListModel();
        await model.AddFiles([new FileToAdd("Ammunition.ndf", "GameData/Gameplay", path)], fromFolder: false,
            (_, _) => Task.FromResult(new DuplicateAnswer(DuplicateChoice.KeepBoth, false)));

        await new FileRunner(model, new RunSettings()).Run(write: false, () => Task.FromResult<string?>(null));

        List<string> found = [.. ModConflictList.All.Select(conflict => conflict.Value).OrderBy(value => value.Property, StringComparer.Ordinal)
            .Select(value => value.Descriptor + "." + value.Property + ": " + string.Join(", ", value.Changes.Select(change =>
                change.Mod + " / " + change.Rule + " #" + change.RuleIndex + " " + change.Old + " -> " + change.New)))];
        Assert.Equal(
        [
            "Ammo_One.X: " + first.Name + " / A sets X #0 1 -> 2, " + second.Name + " / B sets X #0 2 -> 3",
            "Ammo_One.Y: " + first.Name + " / A sets Y #2 4 -> 5, " + second.Name + " / B sets Y #1 5 -> 6",
        ], found);
    }
}
