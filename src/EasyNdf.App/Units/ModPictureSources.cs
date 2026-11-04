using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;

namespace EasyNdf.App.Units;

/// <summary>
/// The pictures and flags EasyNDF mods bring, for the Armory: the pictures their File rules carry (a division's
/// emblem, a new unit's card picture, a nation's flag), and the flags of the nations they add or give a new flag. A
/// nation's flag is found as the game finds it: a TUISpecificCountryInfos names the nation's FlagTexture, and that
/// texture names its picture's file.
/// </summary>
public sealed partial class ModPictureSources
{
    /// <summary>No mod's pictures: the game's alone.</summary>
    public static readonly ModPictureSources None = new([], new Dictionary<string, string>());

    /// <summary>Each carried picture's kept file, by its path under GameData in lower case ("assets/2d/.../x.png").</summary>
    private readonly Dictionary<string, string> files;

    /// <summary>The mods' flags, by nation code, as "GameData:/....png" sources.</summary>
    public IReadOnlyDictionary<string, string> Flags { get; }

    private ModPictureSources(Dictionary<string, string> files, IReadOnlyDictionary<string, string> flags)
    {
        this.files = files;
        Flags = flags;
    }

    /// <summary>The kept file of a picture a mod carries, by its path under GameData; null when no mod carries it.</summary>
    public string? FileFor(string relative) => files.GetValueOrDefault(relative.Replace('\\', '/').TrimStart('/').ToLowerInvariant());

    /// <summary>Whether these are the same pictures and flags as another's.</summary>
    public bool SameAs(ModPictureSources other) =>
        files.Count == other.files.Count && files.All(pair => other.files.GetValueOrDefault(pair.Key) == pair.Value)
        && Flags.Count == other.Flags.Count && Flags.All(pair => other.Flags.GetValueOrDefault(pair.Key) == pair.Value);

    /// <summary>The pictures and flags of these mods, in the order they run: a later mod's take the place of an earlier one's.</summary>
    /// <param name="mods">The mods shown.</param>
    /// <param name="storeFolder">Where the app keeps the files mods carry (<see cref="ModFileStore.Folder"/>).</param>
    public static ModPictureSources Of(IEnumerable<Mod> mods, string storeFolder)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var textures = new Dictionary<string, string>(StringComparer.Ordinal);
        var countries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Mod mod in mods)
        {
            Found found = FoundIn(mod, storeFolder);
            foreach ((string place, string file) in found.Files) files[place] = file;
            foreach ((string name, string file) in found.Textures) textures[name] = file;
            foreach ((string code, string texture) in found.Countries) countries[code] = texture;
        }
        var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string code, string texture) in countries)
            if (textures.TryGetValue(texture, out string? file)) flags[code] = GamePictures.Normalised(file);
        return files.Count == 0 && flags.Count == 0 ? None : new ModPictureSources(files, flags);
    }

    /// <summary>What a mod's rules carry and name, in their order: a later one takes the place of an earlier one.</summary>
    private sealed record Found(List<(string, string)> Files, List<(string, string)> Textures, List<(string, string)> Countries);

    /// <summary>Each mod's last <see cref="Found"/>, with the packed rule sets and the folder it was found from.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Mod, Tuple<object?[], Found>> foundByMod = [];

    /// <summary>
    /// Tuned for memory and speed: what a mod's rules carry and name, read without unpacking its rule sets put away
    /// (each Armory visit unpacked every rule set of the mods shown, some 200, only for the idle clean-up to put them
    /// away again), and kept while they all stay put away, as packed rules never change. Keep it that way when tidying.
    /// </summary>
    private static Found FoundIn(Mod mod, string storeFolder)
    {
        object?[] key = [storeFolder, .. mod.Presets.Select(preset => preset.PackedRules)];
        bool allPacked = key.Skip(1).All(packed => packed is not null);
        if (allPacked && foundByMod.TryGetValue(mod, out Tuple<object?[], Found>? known)
            && known.Item1.Length == key.Length && known.Item1.Zip(key).All(pair => Equals(pair.First, pair.Second)))
            return known.Item2;
        var found = new Found([], [], []);
        List<JsonNode?> rules = [.. mod.Presets.SelectMany(preset => preset.RulesToRead())];
        foreach (FileAndTextRules.CarriedFile file in FileAndTextRules.FilesOf(rules))
        {
            if (!ModFileStore.IsStoredName(file.StoredName)) continue;
            string place = file.Destination.Replace('\\', '/').TrimStart('/');
            if (!place.StartsWith("GameData/", StringComparison.OrdinalIgnoreCase)) continue;
            found.Files.Add((place["GameData/".Length..].ToLowerInvariant(), Path.Combine(storeFolder, file.StoredName)));
        }
        foreach (string text in TextsOf(rules))
        {
            foreach (Match texture in TextureFile().Matches(text)) found.Textures.Add((texture.Groups[1].Value, texture.Groups[2].Value));
            foreach (Match country in CountryInfos().Matches(text))
            {
                Match flag = FlagTexture().Match(country.Value), code = CountryCode().Match(country.Value);
                if (flag.Success && code.Success) found.Countries.Add((code.Groups[1].Value, flag.Groups[1].Value));
            }
        }
        if (allPacked) foundByMod.AddOrUpdate(mod, Tuple.Create(key, found));
        return found;
    }

    /// <summary>
    /// The NDF the rules write that can name a texture or a nation: Add rules' text and actions' values, only those that
    /// mention one, so a large mod's thousands of other rules cost a quick look each.
    /// </summary>
    private static IEnumerable<string> TextsOf(IEnumerable<JsonNode?> rules)
    {
        foreach (JsonNode rule in DivisionExtras.RulesThatAreOn(rules))
        {
            if (rule["Params"]?["Text"] is JsonValue text && text.TryGetValue(out string? added) && Names(added)) yield return added;
            if (rule["Actions"] is not JsonArray actions) continue;
            foreach (JsonNode? action in actions)
                if (action?["Value"] is JsonValue value && value.TryGetValue(out string? written) && Names(written)) yield return written;
        }
    }

    private static bool Names(string text) =>
        text.Contains("TUIResourceTexture", StringComparison.Ordinal) || text.Contains("TUISpecificCountryInfos", StringComparison.Ordinal);

    // X is TUIResourceTexture_Common ( FileName = 'GameData:/....png' )
    [GeneratedRegex(@"(\w+)\s+is\s+TUIResourceTexture\w*\s*\(\s*FileName\s*=\s*[""']([^""']+)[""']")]
    private static partial Regex TextureFile();

    // TUISpecificCountryInfos ( FlagTexture = "X" Country = "YSM" ... )
    [GeneratedRegex(@"TUISpecificCountryInfos\s*\([^()]*\)")]
    private static partial Regex CountryInfos();

    [GeneratedRegex(@"FlagTexture\s*=\s*[""']?(\w+)")]
    private static partial Regex FlagTexture();

    [GeneratedRegex(@"\bCountry\s*=\s*[""']?(\w+)")]
    private static partial Regex CountryCode();
}
