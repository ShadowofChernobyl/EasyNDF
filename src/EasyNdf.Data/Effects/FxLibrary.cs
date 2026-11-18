using EasyNdf.Data.Units;

namespace EasyNdf.Data.Effects;

/// <summary>
/// The game's effect scripts, by name: the building blocks of GameData/Fx/Bank and Fx/Set and of CommonData/Fx/Bank,
/// and the ways of drawing particles the scripts name from CommonData (RotorParticleRenderInfo), read together the first
/// time an effect is asked for, and each effect of Fx/Generated (fx_tir_..., fx_traine_...) read
/// from its own file when first asked for. Files that cannot be read are listed in <see cref="Problems"/>.
/// </summary>
/// <param name="files">The game's files, with a game mod's own copies first.</param>
public sealed class FxLibrary(GameDataFiles files)
{
    private readonly Dictionary<string, FxDeclaration> declarations = new(StringComparer.Ordinal);
    private readonly HashSet<string> generatedRead = new(StringComparer.OrdinalIgnoreCase);
    private bool bankRead;

    /// <summary>The effect files that could not be read, each with what went wrong.</summary>
    public List<string> Problems { get; } = [];

    /// <summary>How many characters of effect script are held, for the memory list.</summary>
    public long CharactersRead { get; private set; }

    /// <summary>
    /// A declaration by name or path ("$/VFX_Bank/BANK_FireBalls", "$/GFX/GameFx/fx_tir_...", "~/Name"); null when no
    /// effect file has it.
    /// </summary>
    public FxDeclaration? Find(string nameOrPath)
    {
        // particles are moved on several threads at once (FxPlayer.Step), and a file may be read on the way
        lock (declarations)
        {
            ReadBank();
            string name = nameOrPath[(nameOrPath.LastIndexOf('/') + 1)..];
            if (declarations.TryGetValue(name, out FxDeclaration? found)) return found;
            if (generatedRead.Add(name)) ReadFile("GameData/Fx/Generated/" + name + ".ndf");
            return declarations.GetValueOrDefault(name);
        }
    }

    /// <summary>The declaration a name stands for, kept on the name once found.</summary>
    public FxDeclaration? Find(FxName name)
    {
        if (!Volatile.Read(ref name.LookedUp))
        {
            name.Declared = Find(name.Value);
            Volatile.Write(ref name.LookedUp, true);
        }
        return name.Declared;
    }

    /// <summary>Reads every file of the building blocks, once.</summary>
    private void ReadBank()
    {
        if (bankRead) return;
        bankRead = true;
        // CommonData's first, so the game's own GameData blocks of the same name win; of CommonData's templates only the
        // ways of drawing that are not templates themselves (RotorParticleRenderInfo): its templates, such as
        // ParticleRenderInfo, are the scripts' building blocks this viewer reads by their values
        foreach (string folder in new[] { "CommonData/Fx/Templates/Particles_Misc_RenderInfo", "CommonData/Fx/Bank/", "GameData/Fx/Bank/", "GameData/Fx/Set/" })
            foreach (string path in files.PathsWithModWhere(path => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
                ReadFile(path, withTemplates: !folder.StartsWith("CommonData/Fx/Templates/", StringComparison.Ordinal));
    }

    private void ReadFile(string path, bool withTemplates = true)
    {
        string? text = files.ReadPath(path) ?? files.Read(path);
        if (text is null) return;
        CharactersRead += text.Length;
        try
        {
            foreach (FxDeclaration declaration in FxSyntax.ReadFile(text))
                if (declaration.Name.Length > 0 && (withTemplates || declaration.TemplateParameters is null)) declarations[declaration.Name] = declaration;
        }
        catch (FormatException problem)
        {
            Problems.Add(path + ": " + problem.Message);
        }
    }
}
