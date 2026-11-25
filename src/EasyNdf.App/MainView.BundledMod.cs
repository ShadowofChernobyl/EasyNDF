using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Mods;
using EasyNdf.App.Text;
using EasyNdf.Data;

namespace EasyNdf.App;

/// <summary>
/// The mod the app comes with (WARNO Tactical Overhaul) may be newer in a new version of the app than the copy in the
/// user's Mods. After an update that brings a different one, the user is asked whether to update theirs: replace its
/// rule sets, keep both, or leave it. A tick stops the question for good (Settings > Data turns it back on).
/// </summary>
public partial class MainView
{
    /// <summary>Where the saved state keeps which bundled mod the user was last offered (a short hash of its text).</summary>
    public const string BundledModKey = "bundledMod";

    /// <summary>The setting that lets the question be asked.</summary>
    public const string AskBundledModSetting = "askBundledModUpdate";

    /// <summary>Where the saved state keeps that the question was answered Not Now, so the next start asks again.</summary>
    public const string BundledModLaterKey = "bundledModLater";

    /// <summary>Whether the question was answered Not Now at an earlier start.</summary>
    private static bool BundledModAskedLater() => AppData.Current.State[BundledModLaterKey] is JsonValue later && later.TryGetValue(out bool yes) && yes;

    /// <summary>A short name for the bundled mod's text: another text, another name.</summary>
    public static string BundledModMark(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    /// <summary>
    /// Whether to ask: the bundled mod is not the one last offered, the setting allows it, and the user still has a mod
    /// of its name. The state remembers the mod offered either way, so each new one is offered once.
    /// </summary>
    public static bool BundledModUpdateIsDue(JsonObject state, JsonObject settings, string bundledText, IEnumerable<string> modNames, string bundledName)
    {
        string mark = BundledModMark(bundledText);
        string? offered = state[BundledModKey] is JsonValue value && value.TryGetValue(out string? kept) ? kept : null;
        state[BundledModKey] = mark;
        if (offered == mark) return false;
        if (settings[AskBundledModSetting]?.GetValue<bool>() == false) return false;
        return modNames.Contains(bundledName);
    }

    /// <summary>Remembers the bundled mod as offered without asking: a first start, which has just been given it.</summary>
    private static void RememberBundledMod()
    {
        if (AppData.Current.State[BundledModKey] is not null) return;
        AppData.Current.State[BundledModKey] = BundledModMark(DefaultMods.Text);
        AppData.Current.SaveState();
    }

    /// <summary>After an update: asks whether to update the user's copy of the bundled mod, when it is due.</summary>
    private async Task OfferBundledModUpdate()
    {
        Mod bundled = DefaultMods.TacticalOverhaul;
        JsonNode? offeredBefore = AppData.Current.State[BundledModKey]?.DeepClone();
        AppData.Current.State.Remove(BundledModLaterKey);
        bool due = BundledModUpdateIsDue(AppData.Current.State, AppData.Current.Settings, DefaultMods.Text, AppData.Current.Mods.Select(mod => mod.Name), bundled.Name);
        AppData.Current.SaveState();
        if (!due || AppData.Current.Mods.FirstOrDefault(mod => mod.Name == bundled.Name) is not Mod existing) return;

        var never = new CheckBox { Content = Texts.Get("Main.BundledMod.DontShow"), Margin = new System.Windows.Thickness(0, 4, 0, 0) };
        never.SetResourceReference(StyleProperty, "Tick");
        DialogAnswer answer = await Messages.Ask(Texts.Get("Main.BundledMod.Ask", ("mod", bundled.Name)), new DialogOptions
        {
            Icon = DialogIcon.Question,
            Extra = never,
            Ok = Texts.Get("Main.BundledMod.Update"),
            Alternative = Texts.Get("Files.Duplicate.KeepBoth"),
            Cancel = Texts.Get("Main.BundledMod.NotNow"),
        });
        if (never.IsChecked == true)
        {
            AppData.Current.Settings[AskBundledModSetting] = false;
            AppData.Current.SaveSettings();
        }
        if (answer.Choice == DialogChoice.Ok)
        {
            // in place: the mod keeps its place in the list, its switch and its group
            existing.Presets = bundled.Presets;
            existing.Divisions = bundled.Divisions;
            existing.Tabs = bundled.Tabs;
            AppData.Current.ModStore.Save(existing);
        }
        else if (answer.Choice == DialogChoice.Alternative)
        {
            AppData.Current.ModStore.Import([bundled], (_, _) => ImportChoice.KeepBoth);
            // beside the user's own, switched off: both on would change every value twice
            ModStack.SetOn(bundled, false);
        }
        else
        {
            // Not Now: asked again at the next start, unless the question was turned off
            if (never.IsChecked != true)
            {
                AppData.Current.State[BundledModKey] = offeredBefore;
                AppData.Current.State[BundledModLaterKey] = true;
                AppData.Current.SaveState();
            }
            return;
        }
        // steps to undo taken before this would bring back rule sets that are gone
        History.Forget();
        AppData.Current.TellModsChanged(new ModsChange(PresetsAdded: true));
        RulesTab.Model.Refresh();
    }
}
