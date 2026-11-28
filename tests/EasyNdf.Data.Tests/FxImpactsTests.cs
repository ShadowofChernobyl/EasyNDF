using EasyNdf.Data.Effects;

namespace EasyNdf.Data.Tests;

/// <summary>The impact names a MimeticImpactMapping.ndf maps, which the value check holds ammunition to.</summary>
public class FxImpactsTests
{
    [Fact]
    public void The_names_mapped_are_read_as_written_and_nothing_else()
    {
        const string mapping = """
            FxImpactShell_ is TRandomHappening
            (
                Alternatives = [ ImpactWrapper( Action = $/GFX/GameFx/Fx_impact_shell ) ]
            )
            MimeticImpactMapping is TMimeticImpactMapping
            (
                Mapping = MAP[
                    ('MissileAASIDEWINDER', TImpactHappening( Happenings = MAP[ (EImpactSurface/Ground, FxImpactShell_) ] )),
                    ( 'Shell' ,TImpactHappening( Happenings = MAP[] )),
                ]
            )
            """;

        Assert.Equal(["MissileAASIDEWINDER", "Shell"], FxImpacts.NamesIn(mapping).Order(StringComparer.Ordinal));
    }
}
