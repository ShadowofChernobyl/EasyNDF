using System.Numerics;
using EasyNdf.Data.Effects;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Tests;

public class EffectScriptTests
{
    [Fact]
    public void Effect_files_are_read_with_templates_maps_negative_lists_and_names_given_in_place()
    {
        List<FxDeclaration> read = FxSyntax.ReadFile("""
            // a comment
            private template Spawner_Of_Ten [ Count = 10 ] is ParticleBombSpawner ( Count = <Count> )
            export fx_test is SimultaneousActionDeclaration
            (
                Params = [ private parPosition is TActionAlias( ExpectedType = 'float3' DefaultValue = float3[-29,11,5] ) ]
                Actions = ActionList is
                [
                    TActionCall( Action = $/VFX_Bank/Burst NamedParams = MAP[ ('parSize', 4 * 2), ] ),
                ]
            )
            """);

        Assert.Equal(["Spawner_Of_Ten", "fx_test"], read.Select(declaration => declaration.Name));
        Assert.Equal(new FxNumber(10), read[0].TemplateParameters!["Count"]);
        var effect = (FxObject)read[1].Value;
        var call = (FxObject)((FxList)effect.Get("Actions")!).Items[0];
        Assert.Equal("Burst", ((FxName)call.Get("Action")!).Last);
        var map = (FxCall)call.Get("NamedParams")!;
        Assert.Equal(new FxText("parSize"), ((FxPair)map.Arguments[0]).Key);
        var defaultPlace = (FxCall)((FxObject)((FxPair)((FxList)effect.Get("Params")!).Items[0]).Value).Get("DefaultValue")!;
        Assert.Equal(new FxUnary("-", new FxNumber(29)), defaultPlace.Arguments[0]);
    }

    [Fact]
    public void A_burst_is_spawned_in_its_frame_turned_by_its_euler_angles_and_moved_on_by_its_evolvers()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, new Dictionary<string, string>
        {
            ["Fx/Bank/Test.ndf"] = """
                export RandomUnit is Template_AparametricFunction_ForBank (Function = 'UnitRand')
                export RandomRange is Template_FunctionDeclaration ( Params = [parMin is Template_Alias(), parMax is Template_Alias()] Code = parMin + (RandomUnit*(parMax - parMin)))
                export ParticleSize is Template_ParticleAccessor_ForBank (Field = $/Particle/Size)
                export Burst is SimultaneousActionDeclaration
                (
                    Params = [ private parSize is Template_Param_Float(DefaultValue = 10) ]
                    Actions =
                    [
                        ParticleBombSpawner
                        (
                            Count = 3
                            Spawner = ParticleSpawner
                            (
                                ParticleLifetime = 1
                                Initialisers = [ ParticleInitialiserList( Size = parSize * 2  Speed = GenFloat3[100, 0, 0]  CustomFloat_1 = RandomRange[5, 6] ) ]
                                ParticleDescriptor = ParticleDescriptor
                                (
                                    RenderInfo = ParticleRenderInfo( TextureName = 'GameData:/Assets/2D/Fx/spot.png' )
                                    Evolvers = ParticleStandardEvolverListWithPhysic( Size = ParticleSize + 1 )
                                )
                            )
                        ),
                    ]
                )
                """,
            ["Fx/Generated/fx_test.ndf"] = """
                export fx_test is SimultaneousActionDeclaration
                (
                    Actions = ActionList is
                    [
                        TSimultaneousAction
                        (
                            Mobile = TMobileWithLocalRepereMatrixFactory( Position = float3[0, 0, 50] EulerAngles = float3[3.14159265, 0, 0] )
                            Actions = [ TActionCall( Action = $/VFX_Bank/Burst NamedParams = MAP[ ('parSize', 4) ] ) ]
                        ),
                    ]
                )
                """,
        });
        using var files = new GameDataFiles(zip, null);
        var player = new FxPlayer(new FxLibrary(files));

        player.Start("$/GFX/GameFx/fx_test", FxFrame.Fixed(Matrix4x4.Identity));
        player.Step(0.1);

        Assert.Empty(player.Problems);
        Assert.Equal(3, player.Particles.Count);
        foreach (FxParticle particle in player.Particles)
        {
            // Size 4 * 2 from the call, then one step of its evolver; the speed along x turned half round about z
            Assert.Equal(9, particle.Fields[FxFields.Size].Number, 3);
            Assert.Equal(-100, particle.Fields[FxFields.Speed].Vector.X, 2);
            Assert.Equal(new Vector3(-10, 0, 50), particle.Position, new ApproximateVectors(0.01f));
            Assert.InRange(particle.Fields[FxFields.Of("CustomFloat_1")].Number, 5, 6);
        }
        player.Step(1);
        Assert.False(player.IsBusy);
    }

    [Fact]
    public void A_particle_placed_along_its_direction_has_that_direction_from_its_first_step()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, new Dictionary<string, string>
        {
            ["Fx/Bank/Test.ndf"] = """
                export ParticleDirection is Template_ParticleAccessor_ForBank (Field = $/Particle/Direction)
                export Flash is SimultaneousActionDeclaration
                (
                    Actions =
                    [
                        ParticleBombSpawner
                        (
                            Count = 1
                            Spawner = ParticleSpawner
                            (
                                ParticleLifetime = 1
                                ParticleDescriptor = ParticleDescriptor
                                (
                                    RenderInfo = ParticleRenderInfo( TextureName = 'GameData:/Assets/2D/Fx/spot.png' )
                                    Evolvers = ParticleStandardEvolverList( Position = ParticleDirection * 10  Direction = GenFloat3[1, 0, 0] )
                                )
                            )
                        ),
                    ]
                )
                """,
            ["Fx/Generated/fx_test.ndf"] = """
                export fx_test is SimultaneousActionDeclaration
                (
                    Actions = ActionList is [ TActionCall( Action = $/VFX_Bank/Flash ) ]
                )
                """,
        });
        using var files = new GameDataFiles(zip, null);
        var player = new FxPlayer(new FxLibrary(files));

        player.Start("$/GFX/GameFx/fx_test", FxFrame.Fixed(Matrix4x4.Identity));
        player.Step(0.01);

        Assert.Empty(player.Problems);
        Assert.Equal(new Vector3(10, 0, 0), Assert.Single(player.Particles).Position, new ApproximateVectors(0.01f));
    }

    [Fact]
    public void A_templates_parameters_choose_which_of_its_actions_run()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        // as a jet engine's FX_Binder_Avion_Reacteur: flames only when their colour is above 0, a trail only when its alpha is
        GameFileWriters.BaseZip(zip, new Dictionary<string, string>
        {
            ["Fx/Set/FX_Test.ndf"] = """
                export FX_Reactor is template_Reactor ( Colour = 0.01 )

                template template_Fire [ Colour, ] is ParticleBombSpawner
                (
                    Count = 1
                    Spawner = ParticleSpawner
                    (
                        ParticleLifetime = 1
                        ParticleDescriptor = ParticleDescriptor
                        (
                            InfiniteParticles = true
                            RenderInfo = ParticleRenderInfo( TextureName = 'GameData:/Assets/2D/Fx/spot.png' )
                            Evolvers = ParticleStandardEvolverList( Size = 10 * <Colour> )
                        )
                    )
                )

                template template_Reactor
                [
                    Colour = 0.2,
                    TrailAlpha = 0
                ] is SimultaneousActionDeclaration
                (
                    Actions =
                    [
                    ] +
                    ( <Colour> > 0 ? [ template_Fire ( Colour = <Colour> ), template_Fire ( Colour = <Colour> ), ] : [] ) +
                    ( <TrailAlpha> > 0 ? [ template_Fire ( Colour = 1 ) ] : [] )
                )
                """,
        });
        using var files = new GameDataFiles(zip, null);
        var player = new FxPlayer(new FxLibrary(files));

        player.Start("FX_Reactor", FxFrame.Fixed(Matrix4x4.Identity));
        player.Step(0.1);

        Assert.Empty(player.Problems);
        Assert.Equal(2, player.Particles.Count);
        Assert.All(player.Particles, particle => Assert.Equal(0.1, particle.Fields[FxFields.Size].Number, 3));
    }

    private sealed class ApproximateVectors(float within) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= within;

        public int GetHashCode(Vector3 vector) => 0;
    }
}
