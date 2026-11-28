using EasyNdf.Data.Models;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Tests;

public class ModelLinksTests
{
    [Fact]
    public void A_mods_new_unit_finds_its_model_in_a_file_of_the_mods_own_by_any_name()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, new Dictionary<string, string>
        {
            ["Gameplay/Gfx/DepictionResources/Char/M1A1.ndf"] = "export Modele_M1A1 is TResourceMesh( Mesh = 'GameData:/Assets/3D/Units/US/Char/M1A1/M1A1.fbx' )",
            ["Generated/Gameplay/Gfx/Depictions/DepictionAlternatives.ndf"] = """
                Alternatives_M1A1_US is
                [
                    DepictionVisual_LOD_High( MeshDescriptor=$/GFX/DepictionResources/Modele_M1A1 ),
                ]
                """,
        });
        string mod = Path.Combine(temp.Path, "Mod");
        string resources = Path.Combine(mod, "GameData", "Gameplay", "Gfx", "DepictionResources", "WiF");
        Directory.CreateDirectory(resources);
        File.WriteAllText(Path.Combine(resources, "Planes.ndf"), """
            export WF_Rafale_Mesh is TResourceMesh
            (
                Name = 'Rafale'
                Mesh = 'GameData:/Assets/3D/Units/WiF/Rafale/Rafale_C.fbx'
            )
            """);
        using var files = new GameDataFiles(zip, mod);
        var links = new ModelLinks(files);

        Assert.Equal([new ModelFile("Assets/3D/Units/WiF/Rafale/Rafale_C.fbx")], links.ModelsOf(new UnitLook("WF_Rafale_Mesh", null, null)));
        // a mesh the files do not declare falls back to the model its depiction draws up close
        Assert.Equal([new ModelFile("Assets/3D/Units/US/Char/M1A1/M1A1.fbx")], links.ModelsOf(new UnitLook("Missing", null, "M1A1_US")));
    }

    [Fact]
    public void A_mesh_named_through_a_constant_written_any_way_the_game_writes_it_is_found()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, new Dictionary<string, string>
        {
            // a folder's constant with the file added to it
            ["Gameplay/Gfx/DepictionResources/Vehicule/AEC_Militant_UK.ndf"] = """
                Modele_AEC_Militant_UK_Path is "GameData:/Assets/3D/Units/UK/Vehicule/AEC_Militant"

                export Modele_AEC_Militant_UK     is TResourceMesh ( Mesh=Modele_AEC_Militant_UK_Path+"/AEC_Militant.fbx" )
                """,
            // a constant of another file, named with ~/
            ["Gameplay/Gfx/DepictionResources/Vehicule/BTR_70_MP_SOV.ndf"] = """
                export Modele_BTR_70_MP_SOV is TResourceMesh
                (
                    Mesh=~/ModelFile_BTR_70_SOV
                    Textures="GameData:/Assets/3D/Units/URSS/APC/BTR70_MP_Komendatura"
                )
                """,
            ["Gameplay/Gfx/DepictionResources/Vehicule/BTR_70_SOV.ndf"] = """
                export ModelFile_BTR_70_SOV is 'GameData:/Assets/3D/Units/URSS/APC/BTR70/BTR70.fbx'
                """,
            // constants straight after the declaration before them, on its line
            ["Gameplay/Gfx/DepictionResources/Towed/DCA_ZUR_23_2S_JOD_POL.ndf"] =
                "export Modele_DCA_ZUR_23_2S_JOD_POL is TResourceMesh( Mesh=DCA_ZUR_23_2S_JOD_POL )\n"
                + "export Modele_DCA_ZUR_23_2S_JOD_POL_LOW is TResourceMesh( Mesh=DCA_ZUR_23_2S_JOD_POL_LOW )"
                + "DCA_ZUR_23_2S_JOD_POL is 'GameData:/Assets/3D/Units/POL/Canon/DCA_ZUR_23_2S_JOD/DCA_ZUR_23_2S_JOD.fbx'"
                + "DCA_ZUR_23_2S_JOD_POL_LOW is 'GameData:/Assets/3D/Units/POL/Canon/DCA_ZUR_23_2S_JOD_LOW/DCA_ZUR_23_2S_JOD_LOW.fbx'",
        });
        using var files = new GameDataFiles(zip, null);
        var links = new ModelLinks(files);

        Assert.Equal(new ModelFile("Assets/3D/Units/UK/Vehicule/AEC_Militant/AEC_Militant.fbx"), links.MeshPath("Modele_AEC_Militant_UK"));
        Assert.Equal(new ModelFile("Assets/3D/Units/URSS/APC/BTR70/BTR70.fbx", "Assets/3D/Units/URSS/APC/BTR70_MP_Komendatura"), links.MeshPath("Modele_BTR_70_MP_SOV"));
        Assert.Equal(new ModelFile("Assets/3D/Units/POL/Canon/DCA_ZUR_23_2S_JOD/DCA_ZUR_23_2S_JOD.fbx"), links.MeshPath("Modele_DCA_ZUR_23_2S_JOD_POL"));
        Assert.Equal(new ModelFile("Assets/3D/Units/POL/Canon/DCA_ZUR_23_2S_JOD_LOW/DCA_ZUR_23_2S_JOD_LOW.fbx"), links.MeshPath("Modele_DCA_ZUR_23_2S_JOD_POL_LOW"));
    }

    [Fact]
    public void A_model_painted_for_another_army_takes_its_pictures_from_the_folder_its_descriptor_names()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, new Dictionary<string, string>
        {
            ["Gameplay/Gfx/DepictionResources/Avion/AT33_ShootingStar_HE_COL.ndf"] = """
                export Modele_AT33_ShootingStar_HE_COL is TResourceMesh
                (
                    Mesh="GameData:/Assets/3D/Units/CAN/Avion/SilverStar_Mk3AT/SilverStar_Mk3AT.fbx"
                    Textures="GameData:/Assets/3D/Units/COL/Avion/SilverStar_Mk3AT"
                )

                export Modele_AT33_ShootingStar_HE_COL_train is TResourceMesh
                (
                    Mesh="GameData:/Assets/3D/Units/CAN/Avion/SilverStar_Mk3AT/SilverStar_Mk3AT_train.fbx"
                )
                """,
            ["Gameplay/Gfx/DepictionResources/Avion/SilverStar_Mk3AT_RKT_CAN.ndf"] = """
                export Modele_SilverStar_Mk3AT_RKT_CAN is TResourceMesh
                (
                    Textures = 'GameData:/Assets/3D/Units/CAN/Avion/SilverStar_Mk3AT/'
                    Mesh = 'GameData:/Assets/3D/Units/CAN/Avion/SilverStar_Mk3AT/SilverStar_Mk3AT.fbx'
                )
                """,
        });
        using var files = new GameDataFiles(zip, null);
        var links = new ModelLinks(files);

        Assert.Equal([new ModelFile("Assets/3D/Units/CAN/Avion/SilverStar_Mk3AT/SilverStar_Mk3AT.fbx", "Assets/3D/Units/COL/Avion/SilverStar_Mk3AT")],
            links.ModelsOf(new UnitLook("Modele_AT33_ShootingStar_HE_COL", null, null)));
        // the next declaration's model keeps its own folder
        Assert.Equal(new ModelFile("Assets/3D/Units/CAN/Avion/SilverStar_Mk3AT/SilverStar_Mk3AT_train.fbx"), links.MeshPath("Modele_AT33_ShootingStar_HE_COL_train"));
        // Textures written before Mesh, in single quotes
        Assert.Equal(new ModelFile("Assets/3D/Units/CAN/Avion/SilverStar_Mk3AT/SilverStar_Mk3AT.fbx", "Assets/3D/Units/CAN/Avion/SilverStar_Mk3AT"),
            links.MeshPath("Modele_SilverStar_Mk3AT_RKT_CAN"));
    }
}
