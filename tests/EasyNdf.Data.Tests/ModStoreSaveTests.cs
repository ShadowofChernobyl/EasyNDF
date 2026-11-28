using System.Text;
using System.Text.Json.Nodes;

namespace EasyNdf.Data.Tests;

/// <summary>
/// A mod is saved from the text it is made into, no longer read back into JSON and made again first: the file must
/// hold exactly the bytes it held before, for rules with every kind of value and every character, and a mod too deep
/// to be saved must still fail to save.
/// </summary>
public class ModStoreSaveTests
{
    /// <summary>The text a mod was saved as before: its text read back into JSON and made again.</summary>
    private static string SavedBefore(Mod mod) => JsonFiles.ToText(JsonFiles.Parse(ModFiles.StoredText(mod)));

    /// <summary>Text with letters, quotes, a backslash, a line separator, an emoji, a tab and half of a pair.</summary>
    private static string AwkwardText(int seed) =>
        "Rule " + new string([(char)0xE9, (char)0x4E2D, (char)0xA0, (char)0x2028, (char)0xD83D, (char)0xDE00, (char)0x9]) + " \"quoted\" "
        + (char)0x5C + " back <tag> & 'single' a" + (char)0xD800 + "b " + seed;

    /// <summary>A rule with numbers, texts and lists of the kinds rules hold, and some read from a file as it was written.</summary>
    private static JsonNode RuleWithEveryKindOfValue(int seed)
    {
        var random = new Random(seed);
        var rule = new JsonObject
        {
            ["Name"] = AwkwardText(seed),
            ["Kind"] = "Edit",
            ["Enabled"] = random.Next(2) == 0,
            ["Numbers"] = new JsonArray(0.1, 1e-7, 1e21, 123456789012345, -0.0, 3.0, double.Epsilon, 1.0 / 3, random.NextDouble() * 1000, int.MaxValue, long.MinValue, 2.5m),
            ["Nothing"] = null,
            ["Control"] = new string([(char)0x1, (char)0x1F, (char)0x7F]),
        };
        // values parsed from a file, kept as the file wrote them
        rule["FromFile"] = JsonNode.Parse("{\"Value\": 1.50, \"Big\": 1E+3, \"Text\": \"" + (char)0x5C + "u00e9" + (char)0x5C + "n\", \"List\": [1, 2.0, -3e-2]}");
        return rule;
    }

    private static Mod MadeUpMod(int seed) => new()
    {
        Name = "Saved mod " + seed + " " + (char)0xE4,
        Presets =
        [
            new ModPreset { Name = "First", Rules = [RuleWithEveryKindOfValue(seed), RuleWithEveryKindOfValue(seed + 1)] },
            new ModPreset { Name = "Second (Ammunition.ndf)", Rules = [RuleWithEveryKindOfValue(seed + 2)], AppliesTo = ["Ammunition.ndf", "Units.ndf"] },
        ],
    };

    [Fact]
    public void A_saved_mod_file_holds_the_same_bytes_as_before()
    {
        using var folder = new TemporaryFolder();
        var store = new ModStore(folder.Path);
        for (int seed = 0; seed < 20; seed++)
        {
            Mod mod = MadeUpMod(seed);
            store.Add(mod);
            store.Save(mod);
            string file = Directory.GetFiles(folder.Path, "*.mod.json", SearchOption.AllDirectories).Single(path => Path.GetFileName(path).StartsWith("Saved mod " + seed + " ", StringComparison.Ordinal));
            Assert.Equal(Encoding.UTF8.GetBytes(SavedBefore(mod)), File.ReadAllBytes(file));
        }
    }

    [Fact]
    public void Every_character_is_saved_as_before()
    {
        // each UTF-16 character on its own (halves of pairs among them), and with a whole pair after it
        string pair = new([(char)0xD83D, (char)0xDE00]);
        var texts = new JsonArray();
        for (int code = 0; code <= char.MaxValue; code++)
        {
            texts.Add(((char)code).ToString());
            texts.Add("a" + (char)code + pair + (char)code);
        }
        var mod = new Mod { Name = "Characters", Presets = [new ModPreset { Name = "All", Rules = [new JsonObject { ["Name"] = "Texts", ["Kind"] = "Edit", ["Params"] = texts }] }] };

        Assert.Equal(SavedBefore(mod), JsonFiles.AsMadeAgain(ModFiles.StoredText(mod)));
        // without halves of pairs, the text is written just as it is made
        var whole = new Mod { Name = "Whole", Presets = [new ModPreset { Name = "All", Rules = [RuleWithoutHalves()] }] };
        Assert.Equal(SavedBefore(whole), ModFiles.StoredText(whole));
    }

    private static JsonNode RuleWithoutHalves()
    {
        var texts = new JsonArray();
        for (int code = 0; code <= char.MaxValue; code++)
            if (!char.IsSurrogate((char)code)) texts.Add("x" + (char)code);
        return new JsonObject { ["Name"] = "Texts", ["Kind"] = "Edit", ["Params"] = texts };
    }

    /// <summary>Rules nested this deep: lists in lists.</summary>
    private static JsonNode Nested(int depth)
    {
        JsonNode inner = new JsonArray(1);
        for (int level = 1; level < depth; level++) inner = new JsonArray(inner);
        return new JsonObject { ["Name"] = "Deep", ["Kind"] = "Edit", ["Params"] = inner };
    }

    private static string Outcome(Func<string> save)
    {
        try { return save(); }
        catch (Exception problem) when (problem is InvalidOperationException or System.Text.Json.JsonException) { return "cannot be saved"; }
    }

    [Fact]
    public void A_mod_too_deep_to_save_still_cannot_be_saved()
    {
        int saved = 0, refused = 0;
        for (int depth = 50; depth <= 80; depth++)
        {
            var mod = new Mod { Name = "Deep " + depth, Presets = [new ModPreset { Name = "Deep", Rules = [Nested(depth)] }] };
            string before = Outcome(() => SavedBefore(mod));
            Assert.Equal(before, Outcome(() => JsonFiles.AsMadeAgain(ModFiles.StoredText(mod))));
            if (before == "cannot be saved") refused++;
            else saved++;
        }
        // both sides of the limit were tried
        Assert.True(saved > 0 && refused > 0, $"{saved} saved, {refused} refused");
    }
}
