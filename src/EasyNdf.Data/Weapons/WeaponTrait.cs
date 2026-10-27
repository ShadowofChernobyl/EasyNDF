using System.Text.RegularExpressions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Weapons;

/// <summary>
/// A weapon trait as the game shows it beside a weapon (its ammunition's TraitsToken): "KE" is Kinetic, with the
/// game's small picture of it.
/// </summary>
/// <param name="Token">The trait as the ammunition names it: "KE", "HEAT", "F&amp;F".</param>
/// <param name="Name">Its name in the game's texts: "Kinetic"; the token when the texts have none.</param>
/// <param name="PictureSource">Its picture's path in the game; null when it is not known.</param>
public sealed record WeaponTrait(string Token, string Name, string? PictureSource);

/// <summary>
/// Reads the game's weapon traits: WeaponTraits.ndf gives each trait its texture's name and its name's text token;
/// BUCKTextureBank.ndf gives each texture its file. Steel Division 2 has neither, and its weapons show their tokens.
/// </summary>
public static class WeaponTraitReader
{
    /// <summary>Every trait the game describes, by token; empty when it describes none.</summary>
    public static Dictionary<string, WeaponTrait> Read(GameDataFiles files, GameTexts texts)
    {
        var traits = new Dictionary<string, WeaponTrait>(StringComparer.Ordinal);
        string described = (files.Read("WeaponTraits.ndf") ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);
        if (described == "") return traits;
        Dictionary<string, string> textures = TextureFiles(files.Read("BUCKTextureBank.ndf") ?? "");
        foreach (Match trait in Regex.Matches(described, @"\(\s*""([^""]+)""\s*,\s*TWeaponTraitDescriptor\s*\((.*?)\)\s*\)", RegexOptions.Singleline))
        {
            string token = trait.Groups[1].Value;
            string body = trait.Groups[2].Value;
            string? texture = Value(body, "TraitTextureName");
            string? title = Value(body, "TraitHintTitleToken");
            string name = texts.Get(title) ?? Readable(token);
            traits[token] = new WeaponTrait(token, name, texture is null ? null : textures.GetValueOrDefault(texture));
        }
        return traits;
    }

    /// <summary>A trait's token as a name, for one the game's texts do not name: "MOTION" is "Motion", "F&amp;F" stays.</summary>
    private static string Readable(string token)
    {
        bool allCapitals = token.Length > 3 && token.All(letter => !char.IsLetter(letter) || char.IsUpper(letter));
        return allCapitals ? token[0] + token[1..].ToLowerInvariant() : token;
    }

    /// <summary>A quoted value of a descriptor's body: TraitTextureName = "Texture_Trait_Icon_ke".</summary>
    private static string? Value(string body, string name)
    {
        Match value = Regex.Match(body, name + @"\s*=\s*[""']([^""']*)[""']");
        return value.Success ? value.Groups[1].Value : null;
    }

    /// <summary>
    /// The interface's textures and their files, from the texture bank's lines:
    /// ("Texture_Trait_Icon_ke", MAP [(~/ComponentState/Normal, TUIResourceTexture_Common( FileName='GameData:/…/KE.png' )), ] ).
    /// The trait files name their textures in another case than the bank ("Texture_Trait_Icon_KE"), so any case is taken.
    /// </summary>
    private static Dictionary<string, string> TextureFiles(string bank)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match texture in Regex.Matches(bank, @"\(\s*""(Texture_Trait_[A-Za-z0-9_]+)""\s*,\s*MAP\s*\[.*?FileName\s*=\s*['""]([^'""]+)['""]"))
            files.TryAdd(texture.Groups[1].Value, GamePictures.Normalised(texture.Groups[2].Value));
        return files;
    }
}
