using EasyNdf.App.Mods;
using EasyNdf.Data;
using EasyNdf.Data.Game;

namespace EasyNdf.App.Tests;

/// <summary>
/// The Armory's "Mod Being Edited": the mod chosen there is kept per game, counts only while it runs, follows a rename,
/// and is let go of when a mod that runs is opened on the Mods tab. With none chosen there is none, so the Armory picks
/// as it did before there was a choice.
/// </summary>
[Collection(AppModsTests.Name)]
public sealed class ArmoryModChoiceTests : IDisposable
{
    private readonly List<Mod> modsMade = [];
    private readonly Mod openBefore = AppData.Current.OpenMod;

    public void Dispose()
    {
        ModStack.ForgetArmoryChoice(GameTitle.Warno);
        ModStack.ForgetArmoryChoice(GameTitle.SteelDivision2);
        AppData.Current.Open(openBefore);
        foreach (Mod mod in modsMade)
        {
            ModStack.SetOn(mod, false);
            AppData.Current.ModStore.Delete(mod);
        }
    }

    private Mod Made(GameTitle game, bool on)
    {
        var mod = new Mod { Name = "Choice " + Guid.NewGuid().ToString("N"), Game = game };
        AppData.Current.ModStore.Add(mod);
        modsMade.Add(mod);
        ModStack.SetOn(mod, on);
        return mod;
    }

    [Fact]
    public void No_mod_is_chosen_until_one_is()
    {
        ModStack.ForgetArmoryChoice(GameTitle.Warno);
        Assert.Null(ModStack.ArmoryChoice(GameTitle.Warno));
    }

    [Fact]
    public void A_chosen_mod_counts_for_its_game_only_and_only_while_it_is_on()
    {
        Mod mod = Made(GameTitle.Warno, on: true);
        ModStack.ChooseForArmory(mod);
        Assert.Same(mod, ModStack.ArmoryChoice(GameTitle.Warno));
        Assert.Null(ModStack.ArmoryChoice(GameTitle.SteelDivision2));

        ModStack.SetOn(mod, false);
        Assert.Null(ModStack.ArmoryChoice(GameTitle.Warno));
        // switched on again, it is the chosen one again
        ModStack.SetOn(mod, true);
        Assert.Same(mod, ModStack.ArmoryChoice(GameTitle.Warno));
    }

    [Fact]
    public void Opening_a_mod_that_is_on_lets_go_of_the_choice_and_one_that_is_off_does_not()
    {
        Mod chosen = Made(GameTitle.Warno, on: true), off = Made(GameTitle.Warno, on: false), other = Made(GameTitle.Warno, on: true);
        ModStack.ChooseForArmory(chosen);

        AppData.Current.Open(off);
        Assert.Same(chosen, ModStack.ArmoryChoice(GameTitle.Warno));

        AppData.Current.Open(other);
        Assert.Null(ModStack.ArmoryChoice(GameTitle.Warno));
    }

    [Fact]
    public void A_chosen_mod_renamed_stays_chosen()
    {
        Mod mod = Made(GameTitle.Warno, on: true);
        ModStack.ChooseForArmory(mod);
        string old = mod.Name, renamed = old + " 2";
        mod.Name = renamed;
        ModStack.Renamed(old, renamed);
        Assert.Same(mod, ModStack.ArmoryChoice(GameTitle.Warno));
    }
}
