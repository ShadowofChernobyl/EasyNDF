using System.Numerics;
using System.Text.RegularExpressions;

namespace EasyNdf.Data.Models.Fbx;

/// <summary>What a part of a copied unit's model does, for its row in the Parts list.</summary>
public enum PartRole { Hull, Turret, GunMount, Gun, FiringPoint, Wheels, Tracks, Skirts, Effects }

/// <summary>
/// A part the copied unit's depiction moves or fires from, as its model names it ("tourelle_01"), and the file's part
/// chosen for it (its id), if any. A group stands for every part of a kind (the wheels, the tracks, the effects'
/// points), matched by name only.
/// </summary>
/// <param name="Role">What the part does.</param>
/// <param name="Number">Its number among its kind (turret 1, gun 2); 0 for the hull and the groups.</param>
/// <param name="Names">The names the game looks for: one for a part, all of a group's.</param>
public sealed record ExpectedPart(PartRole Role, int Number, IReadOnlyList<string> Names)
{
    /// <summary>Whether it stands for a group of parts.</summary>
    public bool IsGroup => Role is PartRole.Wheels or PartRole.Tracks or PartRole.Skirts or PartRole.Effects;
}

/// <summary>How a check came out: good, worth knowing, or a problem.</summary>
public enum CheckLevel { Good, Caution, Problem }

/// <summary>What a check found: its level, a text key with its blanks, and a fix to offer (a text key), if any.</summary>
public sealed record FitCheck(CheckLevel Level, string Text, IReadOnlyList<(string Name, object? Value)> Blanks, string? Fix = null, string? FixKind = null);

/// <summary>
/// How a model file fits a copied unit: which of the copied unit's parts the file has (by name; a few common names
/// are matched to the game's: "Turret" to tourelle_01, "Hull" to chassis), and what is wrong. The parts the game moves
/// and fires from are named in the copied unit's model: the file must have parts of the same names (in any case)
/// for its turrets to turn and its guns to fire from the right place. The chosen names are given to the file's parts
/// in the copy the export carries.
/// </summary>
public sealed partial class ModelFit
{
    /// <summary>The copied unit's parts, in the order the Parts list shows them.</summary>
    public List<ExpectedPart> Expected { get; } = [];

    /// <summary>The file's part chosen for each single part (by its place in <see cref="Expected"/>); none for those not chosen.</summary>
    public Dictionary<int, long> Chosen { get; } = [];

    /// <summary>The single parts matched by a similar name rather than their own, which the list marks.</summary>
    public HashSet<int> BySimilarName { get; } = [];

    /// <summary>The firing points to put at their gun's end (by their place in <see cref="Expected"/>).</summary>
    public HashSet<int> AtGunsEnd { get; } = [];

    /// <summary>The scale the model is given (1 for as it is), when a size check's fix was taken.</summary>
    public double Scale { get; set; } = 1;

    private readonly FbxScene scene;
    private readonly ModelMesh copied;

    /// <param name="scene">The file's model.</param>
    /// <param name="copied">The copied unit's model, as the game has it.</param>
    public ModelFit(FbxScene scene, ModelMesh copied)
    {
        this.scene = scene;
        this.copied = copied;
        Expected.AddRange(ExpectedOf(copied));
        MatchByName();
    }

    /// <summary>The copied unit's parts that matter, from its model's nodes' names.</summary>
    public static List<ExpectedPart> ExpectedOf(ModelMesh copied)
    {
        var Expected = new List<ExpectedPart>();
        var names = copied.Nodes.Select(node => node.Name.ToLowerInvariant()).Distinct().ToList();
        if (names.Contains("chassis")) Expected.Add(new ExpectedPart(PartRole.Hull, 0, ["chassis"]));
        foreach (string name in names.Where(name => Turret().IsMatch(name)).OrderBy(Number))
        {
            int number = Number(name);
            Expected.Add(new ExpectedPart(PartRole.Turret, number, [name]));
            foreach (string mount in names.Where(each => GunMount().Match(each) is { Success: true } m && int.Parse(m.Groups[1].Value) == number))
                Expected.Add(new ExpectedPart(PartRole.GunMount, number, [mount]));
            foreach (string gun in names.Where(each => Gun().Match(each) is { Success: true } m && int.Parse(m.Groups[1].Value) == number))
                Expected.Add(new ExpectedPart(PartRole.Gun, number, [gun]));
            foreach (string point in names.Where(each => FiringPoint().Match(each) is { Success: true } m && int.Parse(m.Groups[1].Value) == number))
                Expected.Add(new ExpectedPart(PartRole.FiringPoint, number, [point]));
        }
        void Group(PartRole role, Func<string, bool> picked)
        {
            List<string> group = [.. names.Where(picked)];
            if (group.Count > 0) Expected.Add(new ExpectedPart(role, 0, group));
        }
        Group(PartRole.Wheels, name => name.StartsWith("roue", StringComparison.Ordinal));
        Group(PartRole.Tracks, name => name.StartsWith("chenille", StringComparison.Ordinal));
        Group(PartRole.Skirts, name => name.StartsWith("blindage", StringComparison.Ordinal));
        Group(PartRole.Effects, name => name.StartsWith("fx_", StringComparison.Ordinal) && !FiringPoint().IsMatch(name));
        return Expected;
    }

    private static int Number(string name) => int.TryParse(Digits().Match(name).Value, out int n) ? n : 0;

    /// <summary>Each single part gets the file's part of its own name; failing that, one of a common name for it, when only one has one.</summary>
    private void MatchByName()
    {
        var used = new HashSet<long>();
        for (int i = 0; i < Expected.Count; i++)
        {
            ExpectedPart part = Expected[i];
            if (part.IsGroup) continue;
            if (FilePart(part.Names[0]) is FbxPart own && used.Add(own.Id)) Chosen[i] = own.Id;
        }
        for (int i = 0; i < Expected.Count; i++)
        {
            ExpectedPart part = Expected[i];
            if (part.IsGroup || Chosen.ContainsKey(i) || part.Number > 1) continue;
            Regex? similar = part.Role switch
            {
                PartRole.Hull => SimilarHull(),
                PartRole.Turret => SimilarTurret(),
                PartRole.GunMount => SimilarMount(),
                PartRole.Gun => SimilarGun(),
                PartRole.FiringPoint => SimilarMuzzle(),
                _ => null,
            };
            if (similar is null) continue;
            List<FbxPart> candidates = [.. scene.Parts.Where(each => !used.Contains(each.Id) && similar.IsMatch(each.Name))];
            if (candidates.Count != 1) continue;
            Chosen[i] = candidates[0].Id;
            used.Add(candidates[0].Id);
            BySimilarName.Add(i);
        }
    }

    /// <summary>The file's part of a name, in any case; null when it has none.</summary>
    public FbxPart? FilePart(string name) => scene.Parts.FirstOrDefault(part => string.Equals(part.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>How many of a group's parts the file has, by name.</summary>
    public int FoundOf(ExpectedPart group) => group.Names.Count(name => FilePart(name) is not null);

    /// <summary>Whether a single part has the file's part chosen for it, or is put at its gun's end.</summary>
    public bool IsMet(int index) => Chosen.ContainsKey(index) || AtGunsEnd.Contains(index);

    /// <summary>Whether the file is a soldier's model: bones bending its skin, and nothing like a hull.</summary>
    public bool IsSoldier => scene.HasSkin && !scene.Parts.Any(part => SimilarHull().IsMatch(part.Name) || part.Name.Equals("chassis", StringComparison.OrdinalIgnoreCase))
        && Expected.Any(part => part.Role == PartRole.Hull);

    /// <summary>The model's length (along x), in metres, as read.</summary>
    public double FileLength(ModelMesh shown) => (shown.High.X - shown.Low.X) / FbxScene.GameUnitsAMetre;

    /// <summary>The copied unit's model's length, in metres.</summary>
    public double CopiedLength => (copied.High.X - copied.Low.X) / FbxScene.GameUnitsAMetre;

    /// <summary>
    /// What is wrong, and what is right: a soldier's model for a vehicle; no hull; a gun without its firing point; a
    /// size ten times or more off the copied unit's (a model made in centimetres); turrets and guns not found; groups
    /// partly found.
    /// </summary>
    /// <param name="shown">The model as read, for its size.</param>
    public List<FitCheck> Checks(ModelMesh shown)
    {
        var checks = new List<FitCheck>();
        if (IsSoldier)
        {
            checks.Add(new FitCheck(CheckLevel.Problem, "Units.OwnModel.Check.Soldier", []));
            return checks;
        }
        int hull = Expected.FindIndex(part => part.Role == PartRole.Hull);
        if (hull >= 0 && !IsMet(hull)) checks.Add(new FitCheck(CheckLevel.Problem, "Units.OwnModel.Check.NoHull", []));
        for (int i = 0; i < Expected.Count; i++)
        {
            ExpectedPart part = Expected[i];
            if (part.Role != PartRole.FiringPoint || IsMet(i)) continue;
            bool gunFound = Expected.Select((each, at) => (each, at)).Any(x => x.each.Role == PartRole.Gun && x.each.Number == part.Number && Chosen.ContainsKey(x.at));
            checks.Add(new FitCheck(CheckLevel.Problem, "Units.OwnModel.Check.NoFiringPoint", [("turret", part.Number)],
                gunFound ? "Units.OwnModel.Fix.GunsEnd" : null, gunFound ? "GunsEnd:" + i : null));
        }
        double length = FileLength(shown) * Scale, theirs = CopiedLength;
        if (length > 0 && theirs > 0)
        {
            double ratio = length / theirs;
            if (ratio >= 10 || ratio <= 0.1)
            {
                double by = Math.Pow(10, -Math.Round(Math.Log10(ratio)));
                checks.Add(new FitCheck(CheckLevel.Problem, ratio >= 10 ? "Units.OwnModel.Check.TooBig" : "Units.OwnModel.Check.TooSmall",
                    [("length", Math.Round(length, 1)), ("theirs", Math.Round(theirs, 1))], "Units.OwnModel.Fix.Scale", "Scale:" + by.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            else checks.Add(new FitCheck(CheckLevel.Good, "Units.OwnModel.Check.SizeOk", [("length", Math.Round(length, 1)), ("theirs", Math.Round(theirs, 1))]));
        }
        foreach (var (part, at) in Expected.Select((part, at) => (part, at)))
        {
            if (part.IsGroup || IsMet(at) || part.Role is PartRole.Hull or PartRole.FiringPoint) continue;
            string key = part.Role switch { PartRole.Turret => "Units.OwnModel.Check.NoTurret", PartRole.Gun => "Units.OwnModel.Check.NoGun", _ => "Units.OwnModel.Check.NoMount" };
            checks.Add(new FitCheck(CheckLevel.Caution, key, [("turret", part.Number)]));
        }
        foreach (ExpectedPart group in Expected.Where(part => part.IsGroup))
        {
            int found = FoundOf(group);
            if (found == group.Names.Count) continue;
            string key = group.Role switch
            {
                PartRole.Wheels => "Units.OwnModel.Check.Wheels", PartRole.Tracks => "Units.OwnModel.Check.Tracks",
                PartRole.Skirts => "Units.OwnModel.Check.Skirts", _ => "Units.OwnModel.Check.Effects",
            };
            checks.Add(new FitCheck(CheckLevel.Caution, key, [("found", found), ("count", group.Names.Count)]));
        }
        if (scene.HasLargerFaces) checks.Add(new FitCheck(CheckLevel.Good, "Units.OwnModel.Check.Triangulated", []));
        int singles = Expected.Count(part => !part.IsGroup);
        int met = Enumerable.Range(0, Expected.Count).Count(i => !Expected[i].IsGroup && IsMet(i));
        if (singles > 0 && met == singles) checks.Insert(0, new FitCheck(CheckLevel.Good, "Units.OwnModel.Check.AllParts", [("count", singles)]));
        return checks;
    }

    /// <summary>Whether a problem stops the model being used: a soldier's model for a vehicle. Other problems can be gone on with.</summary>
    public bool Blocks => IsSoldier;

    /// <summary>
    /// The copy the export carries: the chosen parts given the game's names, the firing points put at their guns' ends
    /// (an empty at the far end of the gun's mesh, along its length), and the scale given.
    /// </summary>
    public byte[] MakeCopy(byte[] original)
    {
        FbxScene copy = FbxScene.Read(original);
        foreach ((int index, long id) in Chosen)
        {
            FbxPart? part = copy.Parts.FirstOrDefault(each => each.Id == id);
            string name = Expected[index].Names[0];
            if (part is not null && !string.Equals(part.Name, name, StringComparison.OrdinalIgnoreCase)) copy.Rename(id, GameStyleName(name));
        }
        foreach (int index in AtGunsEnd)
        {
            ExpectedPart point = Expected[index];
            int gunAt = Expected.FindIndex(part => part.Role == PartRole.Gun && part.Number == point.Number);
            if (gunAt < 0 || !Chosen.TryGetValue(gunAt, out long gun)) continue;
            Vector3 end = copy.FarEndOf(gun);
            copy.AddPoint(GameStyleName(point.Names[0]), gun, end);
        }
        if (Scale != 1) copy.Scale(Scale);
        // the game's mod tools take triangles only
        if (copy.HasLargerFaces) copy.Triangulate();
        return copy.Save();
    }

    /// <summary>A game part's name as the game's own files write it: "fx_tourelle1_tir_01" as "Fx_Tourelle1_Tir_01".</summary>
    public static string GameStyleName(string name) =>
        string.Join("_", name.Split('_').Select(word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));

    [GeneratedRegex(@"^tourelle_?0*(\d+)$")]
    private static partial Regex Turret();

    [GeneratedRegex(@"^axe_canon_?0*(\d+)$")]
    private static partial Regex GunMount();

    [GeneratedRegex(@"^canon_?0*(\d+)$")]
    private static partial Regex Gun();

    [GeneratedRegex(@"^fx_tourelle0*(\d+)_tir_\d+$")]
    private static partial Regex FiringPoint();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    [GeneratedRegex(@"^(hull|body|base|chassis)([_.\- ]?0*1)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SimilarHull();

    [GeneratedRegex(@"^(turret|tourelle)([_.\- ]?0*1)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SimilarTurret();

    [GeneratedRegex(@"^(gun[_.\- ]?mount|mantlet|axe[_.\- ]?canon)([_.\- ]?0*1)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SimilarMount();

    [GeneratedRegex(@"^(gun|gun[_.\- ]?barrel|barrel|cannon|canon)([_.\- ]?0*1)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SimilarGun();

    [GeneratedRegex(@"^(muzzle|fx[_.\- ]?muzzle|firing[_.\- ]?point|fire[_.\- ]?point)([_.\- ]?0*1)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SimilarMuzzle();
}
