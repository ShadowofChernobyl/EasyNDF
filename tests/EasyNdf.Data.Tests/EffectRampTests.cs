using EasyNdf.Data.Effects;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Tests;

/// <summary>
/// The ramps effect scripts fade with: Attack, Decay and AttackDecay, worked out over each whole unit of their first
/// number.
/// </summary>
public class EffectRampTests
{
    private static float Ramp(string function, float at, float point)
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, new Dictionary<string, string> { ["Fx/Bank/Empty.ndf"] = "" });
        using var files = new GameDataFiles(zip, null);
        var evaluator = new FxEvaluator(new FxLibrary(files));
        var call = new FxCall(new FxName(function), [new FxNumber(at), new FxNumber(point)]);
        return evaluator.Evaluate(call, evaluator.Outermost, new FxMoment()).Number;
    }

    [Theory]
    [InlineData(0.1f, 0.2f, 0.5f)]
    [InlineData(0.5f, 0.2f, 1f)]
    [InlineData(1.1f, 0.2f, 0.5f)]
    public void Attack_rises_to_one_over_its_share_then_holds_and_starts_again_each_unit(float at, float share, float expected) =>
        Assert.Equal(expected, Ramp("Attack", at, share), 3);

    [Theory]
    [InlineData(0.3f, 0.5f, 1f)]
    [InlineData(0.75f, 0.5f, 0.5f)]
    [InlineData(1.5f, 0.5f, 1f)]
    public void Decay_holds_at_one_until_its_point_then_falls_to_zero(float at, float point, float expected) =>
        Assert.Equal(expected, Ramp("Decay", at, point), 3);

    [Theory]
    [InlineData(0.25f, 0.5f, 0.5f)]
    [InlineData(0.5f, 0.5f, 1f)]
    [InlineData(0.75f, 0.5f, 0.5f)]
    [InlineData(1.25f, 0.5f, 0.5f)]
    [InlineData(0.25f, 0f, 0.75f)]
    [InlineData(0.25f, 1f, 0.25f)]
    public void AttackDecay_rises_to_one_at_its_point_and_falls_back(float at, float point, float expected) =>
        Assert.Equal(expected, Ramp("AttackDecay", at, point), 3);

    [Fact]
    public void A_function_an_action_declares_among_its_values_is_called_where_it_is_declared()
    {
        using var temp = new TemporaryFolder();
        string zip = Path.Combine(temp.Path, "base.zip");
        GameFileWriters.BaseZip(zip, new Dictionary<string, string> { ["Fx/Bank/Empty.ndf"] = "" });
        using var files = new GameDataFiles(zip, null);
        var evaluator = new FxEvaluator(new FxLibrary(files));
        var action = new FxScope(evaluator.Outermost);
        action.Bind("Period", new FxNumber(0.5f), action);
        action.Bind("currentRepeat", FxSyntax.ReadValue("Template_FunctionDeclaration( Params = [currentTime is Template_Alias()] Code = floor[currentTime div Period] )"), action);

        FxValue repeat = evaluator.Evaluate(FxSyntax.ReadValue("currentRepeat[1.2]"), action, new FxMoment());

        Assert.Equal(2, repeat.Number);
    }
}
