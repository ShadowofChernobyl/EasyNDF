using System.Numerics;
using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Languages;
using EasyNdf.Data.Models;
using EasyNdf.Data.Models.Fbx;
using EasyNdf.Data.Units;
using EasyNdf.Engine;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Tests;

/// <summary>
/// A unit's own 3D model: the .fbx read and written back, its parts matched to the copied unit's, the copy the mod
/// carries (renamed, given a firing point, scaled, split into triangles), and the rules that give the unit its own look.
/// </summary>
public class OwnModelTests
{
    // ---- a small .fbx, as Blender writes one: a hull with a turret, a gun and a quad face ----

    private static FbxNode P(string name, params object[] values) => new("P", [name, "", "", "", .. values], []);

    private static FbxNode Model(long id, string name, string kind, Vector3 at) => new("Model", [id, name + "\0\u0001Model", kind], [
        new FbxNode("Version", 232),
        new FbxNode("Properties70", [], [P("Lcl Translation", (double)at.X, (double)at.Y, (double)at.Z)]),
    ]);

    /// <summary>A box of quads (six faces of four corners) along x from <paramref name="low"/> to <paramref name="high"/>.</summary>
    private static FbxNode Box(long id, string name, Vector3 low, Vector3 high)
    {
        double[] points = [low.X, low.Y, low.Z, high.X, low.Y, low.Z, high.X, high.Y, low.Z, low.X, high.Y, low.Z,
            low.X, low.Y, high.Z, high.X, low.Y, high.Z, high.X, high.Y, high.Z, low.X, high.Y, high.Z];
        int[] faces = [0, 3, 2, ~1, 4, 5, 6, ~7, 0, 1, 5, ~4, 1, 2, 6, ~5, 2, 3, 7, ~6, 3, 0, 4, ~7];
        return new FbxNode("Geometry", [id, name + "\0\u0001Geometry", "Mesh"], [
            new FbxNode("Vertices", points),
            new FbxNode("PolygonVertexIndex", faces),
            new FbxNode("LayerElementUV", [0], [new FbxNode("MappingInformationType", "ByPolygonVertex"), new FbxNode("ReferenceInformationType", "IndexToDirect"),
                new FbxNode("UV", new double[] { 0, 0, 1, 0, 1, 1, 0, 1 }), new FbxNode("UVIndex", Enumerable.Range(0, 24).Select(i => i % 4).ToArray())]),
        ]);
    }

    /// <summary>A tank in metres (Blender's 100 cm units): hull "Hull", turret "Turret" at 1.5 m up, gun "Gun_Barrel" 3 m long, no firing point.</summary>
    private static byte[] TankFbx(double unitCm = 100)
    {
        var file = new FbxFile { Version = 7400 };
        file.Nodes.Add(new FbxNode("GlobalSettings", [], [new FbxNode("Properties70", [], [P("UpAxis", 2), P("UnitScaleFactor", unitCm)])]));
        file.Nodes.Add(new FbxNode("Definitions", [], [new FbxNode("Count", 7), new FbxNode("ObjectType", ["Model"], [new FbxNode("Count", 3)])]));
        file.Nodes.Add(new FbxNode("Objects", [], [
            Model(10, "Hull", "Mesh", Vector3.Zero), Box(11, "Hull", new Vector3(-3, -1.5f, 0), new Vector3(3, 1.5f, 1.5f)),
            Model(20, "Turret", "Mesh", new Vector3(0, 0, 1.5f)), Box(21, "Turret", new Vector3(-1, -1, 0), new Vector3(1, 1, 0.8f)),
            Model(30, "Gun_Barrel", "Mesh", new Vector3(1, 0, 0.4f)), Box(31, "Gun_Barrel", new Vector3(0, -0.1f, -0.1f), new Vector3(3, 0.1f, 0.1f)),
            new FbxNode("Material", [40L, "Paint\0\u0001Material", ""], []),
        ]));
        file.Nodes.Add(new FbxNode("Connections", [], [
            new FbxNode("C", "OO", 10L, 0L), new FbxNode("C", "OO", 11L, 10L), new FbxNode("C", "OO", 40L, 10L),
            new FbxNode("C", "OO", 20L, 10L), new FbxNode("C", "OO", 21L, 20L), new FbxNode("C", "OO", 40L, 20L),
            new FbxNode("C", "OO", 30L, 20L), new FbxNode("C", "OO", 31L, 30L), new FbxNode("C", "OO", 40L, 30L),
        ]));
        return file.Write();
    }

    /// <summary>The copied unit's model as the game has it: a hull, turret 1, its gun mount, gun and firing point, and two wheels.</summary>
    private static ModelMesh CopiedTank()
    {
        static ModelNode Node(string name, int parent, float x, float z) => new(name, parent, Matrix4x4.CreateTranslation(x * 215, 0, z * 215));
        return new ModelMesh("Assets/3D/Units/US/Char/Tank/Tank.fbx", [], [
            Node("chassis", -1, 0, 0), Node("tourelle_01", 0, 0, 1.5f), Node("axe_canon_01", 1, 1, 1.9f), Node("canon_01", 2, 1, 1.9f),
            Node("fx_tourelle1_tir_01", 3, 4, 1.9f), Node("roue_d1", 0, 2, 0.4f), Node("roue_g1", 0, 2, 0.4f),
        ], new Vector3(-3, -1.5f, 0) * 215, new Vector3(3, 1.5f, 2) * 215);
    }

    [Fact]
    public void An_fbx_file_is_read_and_written_back_with_its_parts_and_meshes()
    {
        byte[] bytes = TankFbx();
        FbxScene scene = FbxScene.Read(bytes);
        FbxScene again = FbxScene.Read(FbxFile.Read(bytes).Write());

        Assert.Equal(["Hull", "Turret", "Gun_Barrel"], scene.Parts.Select(part => part.Name));
        Assert.Equal(scene.Parts.Select(part => part.World.Translation), again.Parts.Select(part => part.World.Translation));
        // the gun sits in the turret, so its place in the model is the turret's and its own added
        Assert.Equal(new Vector3(1, 0, 1.9f), scene.Parts[2].World.Translation, new Vector3Comparer());
        ModelMesh mesh = scene.ToMesh("x", material => new ModelMaterial(material.Name, null));
        // 7 metres long (the hull from -3 to 3, the gun reaching 4), in the game's units
        Assert.Equal(7 * 215, mesh.High.X - mesh.Low.X, 1);
        Assert.Equal(1.5f * 215, mesh.Nodes[mesh.NodeNamed("turret")].Place.Z, 1);
        // three boxes of six quads: 36 triangles
        Assert.Equal(36, mesh.TriangleCount);
    }

    [Fact]
    public void The_parts_are_matched_by_a_similar_name_and_the_checks_say_what_is_missing()
    {
        FbxScene scene = FbxScene.Read(TankFbx());
        var fit = new ModelFit(scene, CopiedTank());
        ModelMesh shown = scene.ToMesh("x", material => new ModelMaterial(material.Name, null));

        string Chosen(string name) => scene.Parts.Single(part => part.Id == fit.Chosen[fit.Expected.FindIndex(each => each.Names.Contains(name))]).Name;
        Assert.Equal("Hull", Chosen("chassis"));
        Assert.Equal("Turret", Chosen("tourelle_01"));
        Assert.Equal("Gun_Barrel", Chosen("canon_01"));
        Assert.Equal(3, fit.BySimilarName.Count);
        List<FitCheck> checks = fit.Checks(shown);
        // no firing point, with the gun's end offered; no gun mount; no wheels; faces split into triangles; the size is right
        Assert.Contains(checks, check => check.Text == "Units.OwnModel.Check.NoFiringPoint" && check.FixKind!.StartsWith("GunsEnd:"));
        Assert.Contains(checks, check => check.Text == "Units.OwnModel.Check.NoMount");
        Assert.Contains(checks, check => check.Text == "Units.OwnModel.Check.Wheels");
        Assert.Contains(checks, check => check.Text == "Units.OwnModel.Check.Triangulated");
        Assert.Contains(checks, check => check.Text == "Units.OwnModel.Check.SizeOk");
        Assert.False(fit.Blocks);
    }

    [Fact]
    public void A_model_made_in_centimetres_is_offered_a_scale_and_the_copy_is_that_size()
    {
        FbxScene scene = FbxScene.Read(TankFbx(unitCm: 1));
        var fit = new ModelFit(scene, CopiedTank());
        FitCheck tooSmall = Assert.Single(fit.Checks(scene.ToMesh("x", material => new ModelMaterial(material.Name, null))),
            check => check.Text == "Units.OwnModel.Check.TooSmall");
        Assert.Equal("Scale:100", tooSmall.FixKind);

        fit.Scale = 100;
        ModelMesh copy = FbxScene.Read(fit.MakeCopy(TankFbx(unitCm: 1))).ToMesh("x", material => new ModelMaterial(material.Name, null));
        Assert.Equal(7 * 215, copy.High.X - copy.Low.X, 1);
    }

    [Fact]
    public void The_copy_has_the_games_part_names_a_firing_point_at_the_guns_end_and_triangles_only()
    {
        byte[] original = TankFbx();
        FbxScene scene = FbxScene.Read(original);
        var fit = new ModelFit(scene, CopiedTank());
        fit.AtGunsEnd.Add(fit.Expected.FindIndex(each => each.Role == PartRole.FiringPoint));

        FbxScene copy = FbxScene.Read(fit.MakeCopy(original));

        Assert.Equal(["Chassis", "Tourelle_01", "Canon_01", "Fx_Tourelle1_Tir_01"], copy.Parts.Select(part => part.Name));
        Assert.False(copy.HasLargerFaces);
        // the point is at the end of the 3 m gun, which starts 1 m forward
        Assert.Equal(new Vector3(4, 0, 1.9f), copy.Parts.Single(part => part.Name == "Fx_Tourelle1_Tir_01").World.Translation, new Vector3Comparer());
        ModelMesh mesh = copy.ToMesh("x", material => new ModelMaterial(material.Name, null));
        Assert.Equal(36, mesh.TriangleCount);
    }

    [Fact]
    public void A_soldiers_model_cannot_be_given_to_a_vehicle()
    {
        var file = FbxFile.Read(TankFbx());
        file.Node("Objects")!.Children.Add(new FbxNode("Deformer", [50L, "Skin\0\u0001Deformer", "Skin"], []));
        foreach (FbxNode model in file.Node("Objects")!.All("Model")) model.Values[1] = "Spine" + model.Long(0) + "\0\u0001Model";
        var fit = new ModelFit(FbxScene.Read(file.Write()), CopiedTank());

        Assert.True(fit.Blocks);
        Assert.Equal("Units.OwnModel.Check.Soldier", Assert.Single(fit.Checks(FbxScene.Read(file.Write()).ToMesh("x", material => new ModelMaterial(material.Name, null)))).Text);
    }

    // ---- the rules ----

    private const string Vehicles = """
        DepictionOperator_Tank_US_Weapon1 is DepictionOperator_WeaponInstantFire
        (
            FireEffectTag = "weapon_effet_tag1"
        )
        unnamed TacticVehicleDepictionRegistration
        (
            BlackHoleKey = 'Tank_US'
            Selector = SpecificMechanicalDepictionSelector
            Alternatives = Alternatives_Tank_US
            Operators = [ DepictionOperator_Tank_US_Weapon1 ]
            SubDepictionGenerators = [ TransportedInfantrySubGenerator(Mesh = $/GFX/DepictionResources/Modele_Tank_US) ]
        )
        """;

    private const string Alternatives = """
        Alternatives_Tank_US is
        [
            DepictionVisual_LOD_High( MeshDescriptor=$/GFX/DepictionResources/Modele_Tank_US ),
            DepictionVisual_LOD_Mid( MeshDescriptor=$/GFX/DepictionResources/Modele_Tank_US_MID ),
            DepictionVisual_LOD_Low( DisabledOperators=[ "tourelle2" ] MeshDescriptor=$/GFX/DepictionResources/Modele_Tank_US_LOW ),
        ]
        """;

    [Fact]
    public void The_copied_units_look_is_read_and_given_the_units_own_key_and_models()
    {
        (string depiction, string alternatives) = OwnModel.ReadLook(Vehicles, Alternatives, "Tank_US")!.Value;
        var own = new OwnModel
        {
            Key = "EZ_Hunter_US", Folder = "Assets/3D/Units/US/Char/EZ_Hunter_US", CopiedKey = "Tank_US", Depiction = depiction, Alternatives = alternatives,
            Files = [new OwnModelFile("C:/Kept/Hunter.fbx", "Hunter.fbx", "High"), new OwnModelFile("C:/Kept/Hunter_LOW.fbx", "Hunter_LOW.fbx", "Low")],
        };

        Assert.Contains("BlackHoleKey = 'EZ_Hunter_US'", own.DepictionText());
        Assert.Contains("Alternatives = Alternatives_EZ_Hunter_US", own.DepictionText());
        Assert.Contains("Mesh = $/GFX/DepictionResources/Modele_EZ_Hunter_US)", own.DepictionText());
        string levels = own.AlternativesText();
        Assert.StartsWith("Alternatives_EZ_Hunter_US is", levels);
        // the model up close where no Mid is given; the Low given; what the level hides kept
        Assert.Contains("DepictionVisual_LOD_Mid( MeshDescriptor=$/GFX/DepictionResources/Modele_EZ_Hunter_US )", levels);
        Assert.Contains("DisabledOperators=[ \"tourelle2\" ] MeshDescriptor=$/GFX/DepictionResources/Modele_EZ_Hunter_US_LOW", levels);
        Assert.Contains("export Modele_EZ_Hunter_US_LOW is TResourceMesh", own.ResourcesText());
        Assert.Contains("'GameData:/Assets/3D/Units/US/Char/EZ_Hunter_US/Hunter.fbx'", own.ResourcesText());
        Assert.Null(OwnModel.ReadLook(Vehicles, Alternatives, "Plane_US"));
        Assert.Equal(own.ToJson().ToJsonString(), OwnModel.FromJson(own.ToJson())!.ToJson().ToJsonString());
    }

    [Fact]
    public void A_new_unit_with_its_own_model_gets_rules_that_add_its_look_and_carry_its_files()
    {
        (string depiction, string alternatives) = OwnModel.ReadLook(Vehicles, Alternatives, "Tank_US")!.Value;
        var mod = new Mod { Name = "My mod" };
        mod.Units.Add(new ModUnit
        {
            Descriptor = "Descriptor_Unit_EZ_Hunter_US", MadeFrom = "Descriptor_Unit_Tank_US", Name = "HUNTER", DeckNumber = 5,
            OwnModel = new OwnModel
            {
                Key = "EZ_Hunter_US", Folder = "Assets/3D/Units/US/Char/EZ_Hunter_US", CopiedKey = "Tank_US", Depiction = depiction, Alternatives = alternatives,
                Files = [new OwnModelFile("C:/Kept/Hunter.fbx", "Hunter.fbx", "High"), new OwnModelFile("C:/Kept/Hunter.png", "Hunter.png")],
            },
        });
        var tank = new Unit { Descriptor = "Descriptor_Unit_Tank_US", Name = "TANK", File = UnitChangeRules.UnitsFile, Nation = "US", Side = "NATO" };

        JsonArray rules = UnitChangeRules.For(mod, [tank], new TextCatalog());

        JsonObject clone = rules.OfType<JsonObject>().Single(rule => (string?)rule["Kind"] == "Clone");
        string[] targets = [.. ((JsonArray)clone["Actions"]!).Select(action => (string)action!["Target"]!)];
        Assert.Contains("BlackHoleKey", targets);
        Assert.Contains("ReferenceMesh", targets);
        Assert.Contains("ShowRoomBlackHoleIdentifier", targets);
        string[] files = [.. rules.OfType<JsonObject>().Where(rule => (string?)rule["Kind"] == "Add").Select(rule => (string)rule[DivisionChangeRules.ArmoryKey]![DivisionChangeRules.FileKey]!)];
        Assert.Equal([UnitChangeRules.ModelResourcesFile, UnitChangeRules.AlternativesFile, UnitChangeRules.VehicleDepictionsFile, UnitChangeRules.GhostsFile, UnitChangeRules.ShowroomFile], files);
        JsonObject resources = rules.OfType<JsonObject>().Single(rule => (string?)rule[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.FileKey] == UnitChangeRules.ModelResourcesFile);
        Assert.Equal([("C:/Kept/Hunter.fbx", "Assets/3D/Units/US/Char/EZ_Hunter_US/Hunter.fbx"), ("C:/Kept/Hunter.png", "Assets/3D/Units/US/Char/EZ_Hunter_US/Hunter.png")],
            DivisionExtras.CarriedFiles(resources));

        // each addition is valid NDF that adds to its file
        foreach (JsonObject add in rules.OfType<JsonObject>().Where(rule => (string?)rule["Kind"] == "Add"))
        {
            var engine = new NdfEngine();
            engine.Load((string?)add[DivisionChangeRules.ArmoryKey]![DivisionChangeRules.FileKey] == UnitChangeRules.VehicleDepictionsFile ? Vehicles : "Existing is 1\n");
            RunResult result = engine.Run(RuleLoading.LoadForEngine(new JsonArray(add.DeepClone())), new RunOptions());
            Assert.Equal("", result.ParseError);
            Assert.All(result.Stats, stat => Assert.Equal(0, stat.Skipped));
        }
    }

    [Fact]
    public void Exporting_model_files_declares_the_mods_mesh_pack_once()
    {
        using var temp = new TemporaryFolder();
        string gameData = Path.Combine(temp.Path, "MyMod", "GameData");
        Directory.CreateDirectory(gameData);
        DivisionExtras.DeclareMeshPack(gameData);
        string file = Path.Combine(gameData, "ResourcePacks", "MyMod", "ResourcePacks.ndf");
        Assert.Contains("PackRelativePath = \"MeshPack/MyMod/Mesh_All.spk\"", File.ReadAllText(file));
        File.WriteAllText(file, "kept");
        DivisionExtras.DeclareMeshPack(gameData);
        Assert.Equal("kept", File.ReadAllText(file));
    }

    private sealed class Vector3Comparer : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.001f;
        public int GetHashCode(Vector3 value) => 0;
    }
}
