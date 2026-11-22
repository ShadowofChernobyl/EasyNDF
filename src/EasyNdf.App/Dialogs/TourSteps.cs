using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using EasyNdf.App.Files;
using EasyNdf.App.Rules;
using EasyNdf.App.Text;
using EasyNdf.Data;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// The tutorial's steps, chapter by chapter: Files, Mods and Rules, Preview and Export, the Editor and the Armory. Each
/// step says one short line (its text is a "Tour.*" text of welcome.json). It works on the built-in sample
/// Ammunition.ndf (<see cref="TutorialSample"/>), added when no Ammunition.ndf is listed, so it needs no files of the
/// user's; only the Armory chapter needs the game.
/// </summary>
public sealed partial class TourOverlay
{
    private static string T(string key, params (string Name, object Value)[] values) => Texts.Get(key, values);

    /// <summary>The part shown if it is, else the other: a pane that may be folded, a view that may be another.</summary>
    private static FrameworkElement Either(FrameworkElement first, FrameworkElement other) => first.IsVisible ? first : other;

    /// <summary>Every step, each marked with its chapter.</summary>
    private static List<Step> AllSteps(MainView v)
    {
        var all = new List<Step>();
        void Add(Chapter chapter, List<Step> steps)
        {
            foreach (Step step in steps) step.Chapter = chapter;
            all.AddRange(steps);
        }
        Add(Chapter.Files, FilesSteps(v));
        List<Step> mods = ModsSteps(v);
        // the rule's steps are their own chapter, from its first card on
        int rules = mods.FindIndex(step => step.Text == T("Tour.Rules.Start"));
        Add(Chapter.ModsAndRules, mods[..rules]);
        Add(Chapter.Rules, mods[rules..]);
        Add(Chapter.PreviewAndExport, PreviewSteps(v));
        Add(Chapter.Editor, EditorSteps(v));
        Add(Chapter.Armory, ArmorySteps(v));
        return all;
    }

    /// <summary>Whether the sample's row (or the user's own Ammunition.ndf) is listed and read.</summary>
    private static bool AmmunitionListed(MainView v) =>
        v.FilesTab.Model.IsIdle && v.FilesTab.Model.Entries.Any(entry => string.Equals(entry.Name, TutorialSample.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gives a box the keys once the window it is in has taken them (it gives its first box the keys as it opens).</summary>
    private static void FocusSoon(Func<FrameworkElement?> box) =>
        Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () =>
        {
            if (box() is TextBox text) { text.Focus(); text.SelectAll(); }
            else box()?.Focus();
        });

    /// <summary>Lists the sample Ammunition.ndf when none is listed.</summary>
    private static Action AddSample(MainView v) => () => _ = v.FilesTab.EnsureTutorialFile();

    // ---- 1. Files ----

    private static List<Step> FilesSteps(MainView v)
    {
        Pages.FilesPage files = v.FilesTab;
        TabItem tab = v.FilesTabItem;
        return
        [
            new(T("Tour.Files.Start"), () => null),
            new(T("Tour.Files.Tab"), () => tab, () => v.Tabs.SelectedItem == tab),
            new(T("Tour.Files.Source"), () => files.SourceBar, null, tab),
            new(T("Tour.Files.Add"), () => files.AddFilesButton, null, tab, Also: () => files.AddFolderButton),
            new(T("Tour.Files.Sample"), () => AmmunitionListed(v) ? files.ListArea : null, null, tab, OnEnter: AddSample(v)),
        ];
    }

    // ---- 2. Mods and Rules ----

    private static List<Step> ModsSteps(MainView v)
    {
        Pages.RulesPage mods = v.RulesTab;
        TabItem tab = v.RulesTabItem;
        MessageOverlay ask = v.Messages;
        RuleWindow rule = v.RuleWindow;
        RowWindow row = v.RowWindow;
        int modsBefore = AppData.Current.Mods.Count, setsBefore = 0;
        string ruleSet = Texts.Get("Rules.NewMenu.RuleSet"), editKind = RuleKindNames.Of("Edit");
        // the open rule's first condition and first action, as the windows change them
        string? Condition(string field) => mods.Model.OpenRule?["Conditions"]?[0]?[field]?.ToString();
        string? Action(string field) => mods.Model.OpenRule?["Actions"]?[0]?[field]?.ToString();
        bool CommentHas(string field) => JsonValues.IsTruthy(mods.Model.OpenRule?["Actions"]?[0]?["Comment"]?[field]);
        FrameworkElement? Line(int index) => rule.Parts is { } parts && parts.Lists.Count > index ? parts.Lists[index].FirstLine : null;
        // a second run reuses what the first made: the mod, its rule set and its rule, each opened and its steps passed over
        const string modName = "Tutorial Mod", ruleName = "Longer autocannon range";
        ModPreset? AmmunitionSet() => AppData.Current.OpenMod.Presets.FirstOrDefault(set => string.Equals(set.Name, TutorialSample.Name, StringComparison.OrdinalIgnoreCase));
        // what was there as the chapter began: what the user makes during it must not have its own steps passed over
        // (the rule has its name as soon as it is typed, while its window is still open)
        bool reuseMod = false, reuseSet = false, reuseRule = false;
        // what the user made in this run: its steps are passed over on the way back, so nothing is made twice
        bool madeMod = false, madeSet = false;
        System.Text.Json.Nodes.JsonObject? madeRule = null;
        bool HasMod() => reuseMod || madeMod;
        bool InSetSteps() => reuseSet || madeSet;
        bool HasRule() => reuseRule || madeRule is not null;
        bool InRuleSteps() => reuseRule;
        // where each step is: on the Mods tab with nothing open over it, in the rule's window, or in its condition or action window
        void Outside()
        {
            if (row.IsOpen) row.Close();
            if (rule.IsOpen) rule.Close();
            Menus.CloseOpen();
        }
        void OpenRule()
        {
            if (rule.IsOpen) return;
            Menus.CloseOpen();
            if (madeRule is not null && mods.Model.Rules.ToList().IndexOf(madeRule) is int index and >= 0) mods.Model.SelectOnly(index);
            if (mods.Model.OpenRule is not null) mods.OpenRuleWindow();
        }
        void InRule()
        {
            OpenRule();
            if (row.IsOpen) row.Close();
        }
        Action InRow(string list) => () =>
        {
            OpenRule();
            if (rule.IsOpen && (!row.IsOpen || row.List != list)) rule.OpenRow(list, 0);
        };
        void Reuse()
        {
            AddSample(v)();
            reuseMod = AppData.Current.Mods.Any(mod => mod.Name == modName);
            if (reuseMod) mods.Model.OpenModNamed(modName);
            reuseSet = reuseMod && AmmunitionSet() is not null;
            if (AmmunitionSet() is ModPreset set && mods.Model.OpenPreset != set) mods.Model.ClickPreset(set, control: false, shift: false);
            reuseRule = reuseSet && AmmunitionSet()!.Rules.Any(each => (string?)each?["Name"] == ruleName);
        }
        return
        [
            new(T("Tour.Mods.Start"), () => null, OnEnter: Reuse, Show: Outside),
            new(T("Tour.Mods.Tab"), () => tab, () => v.Tabs.SelectedItem == tab, Show: Outside),
            new(T("Tour.Mods.List"), () => mods.ModBox, null, tab, Show: Outside),
            new(T("Tour.Mods.Letters"), () => mods.ModBox, null, tab, Show: Outside),
            new(T("Tour.Mods.Plus"), () => mods.NewModButton, () =>
            {
                modsBefore = AppData.Current.Mods.Count;
                return ask.IsOpen;
            }, tab, SkipIf: HasMod, Show: Outside, Needed: true),
            new(T("Tour.Mods.Name"), () => ask.Box, () =>
            {
                bool made = !ask.IsOpen && AppData.Current.Mods.Count > modsBefore;
                madeMod |= made;
                return made;
            }, tab, Undone: () => !ask.IsOpen && AppData.Current.Mods.Count == modsBefore, SkipIf: HasMod, Needed: true),
            new(T("Tour.Mods.RuleSets"), () => mods.PresetBox, null, tab, Show: Outside),
            new(T("Tour.Mods.NewSet"), () => mods.NewPresetButton, () =>
            {
                setsBefore = AppData.Current.OpenMod.Presets.Count;
                return Menus.IsOpen || ask.IsOpen;
            }, tab, SkipIf: InSetSteps, Show: Outside, Needed: true),
            new(T("Tour.Mods.RuleSetItem"), () => Menus.OpenItem(ruleSet), () => ask.IsOpen, tab,
                Undone: () => !Menus.IsOpen && !ask.IsOpen, SkipIf: InSetSteps, Needed: true),
            new(T("Tour.Mods.SetName"), () => ask.Box, () =>
            {
                bool made = !ask.IsOpen && AppData.Current.OpenMod.Presets.Count > setsBefore;
                madeSet |= made;
                return made;
            }, tab, Undone: () => !ask.IsOpen && AppData.Current.OpenMod.Presets.Count == setsBefore, SkipIf: InSetSteps, Needed: true),
            new(T("Tour.Mods.Applies"), () => mods.AppliesText, null, tab, Show: Outside),
            new(T("Tour.Rules.Start"), () => null, Show: Outside),
            new(T("Tour.Mods.NewRule"), () => mods.NewRuleButton, () => Menus.IsOpen || rule.IsOpen, tab, SkipIf: HasRule, Show: Outside, Needed: true),
            new(T("Tour.Mods.EditItem", ("item", editKind)), () => Menus.OpenItem(editKind), () =>
            {
                if (rule.IsOpen) madeRule ??= mods.Model.OpenRule;
                return rule.IsOpen;
            }, tab, Undone: () => !Menus.IsOpen && !rule.IsOpen, SkipIf: HasRule, Needed: true),
            // the name box comes with "New rule" in it: selected, so typing replaces it
            new(T("Tour.Mods.RuleName"), () => rule.Parts?.NameBox, null, tab, LookOnly: false, SkipIf: InRuleSteps, OnEnter: () =>
            {
                if (rule.Parts?.NameBox is TextBox name) { name.Focus(); name.SelectAll(); }
            }, Show: InRule, Ready: () => rule.Parts?.NameBox?.Text.Trim() is { Length: > 0 } typed && typed != "New rule"),
            new(T("Tour.Mods.Scope"), () => rule.Parts?.ScopeRow, null, tab, SkipIf: InRuleSteps, Show: InRule),
            new(T("Tour.Mods.Gate"), () => rule.Parts?.GateBox, null, tab, SkipIf: InRuleSteps, Show: InRule),
            new(T("Tour.Mods.Scan"), () => rule.ScanChoice, null, tab, SkipIf: InRuleSteps, Show: InRule),
            // the row's own line is outlined: a row without a name is in dim text, which looked switched off
            new(T("Tour.Mods.OpenCondition"), () => Line(0), () => row.IsOpen, tab, SkipIf: InRuleSteps, Show: InRule, Needed: true),
            // goes on once the word is typed and the list of names it suggests is put away
            new(T("Tour.Mods.ConditionValue"), () => row.Field("Value"),
                () => Condition("Value")?.Equals("AutoCanon", StringComparison.OrdinalIgnoreCase) == true
                    && row.Field("Value") is TextBox box && !Suggestions.IsOpenFor(box), tab, Undone: () => !row.IsOpen, SkipIf: InRuleSteps,
                OnEnter: () => FocusSoon(() => row.Field("Value")), Show: InRow(RulesModel.Conditions), Needed: true),
            new(T("Tour.Mods.ConditionDone"), () => row.DoneButton, () => !row.IsOpen, tab, SkipIf: InRuleSteps, Show: InRow(RulesModel.Conditions), Needed: true),
            new(T("Tour.Mods.OpenAction"), () => Line(1), () => row.IsOpen, tab, SkipIf: InRuleSteps, Show: InRule, Needed: true),
            new(T("Tour.Mods.ActionTarget"), () => row.Field("Target"), () => Action("Target") == "MaximumRangeGRU", tab, Undone: () => !row.IsOpen,
                SkipIf: InRuleSteps, Show: InRow(RulesModel.Actions), Needed: true),
            new(T("Tour.Mods.ActionValue"), () => row.Field("Value"), () => Action("Value") == "1.5", tab, Undone: () => !row.IsOpen, SkipIf: InRuleSteps,
                OnEnter: () => FocusSoon(() => row.Field("Value")), Show: InRow(RulesModel.Actions), Needed: true),
            // a comment beside each changed value: the Editor chapter's export shows them
            new(T("Tour.Mods.Comment"), () => row.Field("CommentEnabled"), () => CommentHas("Enabled"), tab, Undone: () => !row.IsOpen,
                SkipIf: InRuleSteps, Show: InRow(RulesModel.Actions), Needed: true),
            new(T("Tour.Mods.CommentOriginal"), () => row.Field("CommentIncludeOriginalValue"), () => CommentHas("IncludeOriginalValue"), tab,
                Undone: () => !row.IsOpen, SkipIf: InRuleSteps, Show: InRow(RulesModel.Actions), Needed: true),
            new(T("Tour.Mods.ActionNote"), () => row.Field("Note"), null, tab, Undone: () => !row.IsOpen, SkipIf: InRuleSteps, Show: InRow(RulesModel.Actions)),
            new(T("Tour.Mods.ActionDone"), () => row.DoneButton, () => !row.IsOpen, tab, SkipIf: InRuleSteps, Show: InRow(RulesModel.Actions), Needed: true),
            new(T("Tour.Mods.Save"), () => rule.DoneButton, () => !rule.IsOpen, tab, SkipIf: InRuleSteps, Show: InRule, Needed: true),
            new(T("Tour.Mods.Saved"), () => mods.RuleBox, null, tab, Show: Outside),
        ];
    }

    // ---- 3. Preview and Export ----

    private static List<Step> PreviewSteps(MainView v)
    {
        Pages.FilesPage files = v.FilesTab;
        TabItem tab = v.FilesTabItem;
        ExportWindow export = v.ExportWindow;
        // Preview goes on once Ammunition.ndf has a run newer than the one it had as the step began (a one-file run can
        // start and end between two looks, so watching for "busy" missed it)
        object? runBefore = null;
        Data.FileList.FileEntry? Ammunition() => files.Model.Entries.FirstOrDefault(entry => string.Equals(entry.Name, TutorialSample.Name, StringComparison.OrdinalIgnoreCase));
        bool Previewed() => files.Model.IsIdle && Ammunition()?.LastRun is { } run && !ReferenceEquals(run, runBefore);
        void InExport()
        {
            if (!export.IsOpen && files.Model.IsIdle) files.OpenExportNow();
        }
        return
        [
            new(T("Tour.Preview.Start"), () => null, OnEnter: AddSample(v)),
            new(T("Tour.Preview.Tab"), () => tab, () => v.Tabs.SelectedItem == tab),
            new(T("Tour.Preview.Run"), () => files.PreviewButton, Previewed, tab, OnEnter: () => runBefore = Ammunition()?.LastRun, Needed: true),
            new(T("Tour.Preview.Status"), () => files.ListArea, null, tab, Show: export.CancelNow),
            new(T("Tour.Preview.PickFile"), () => files.ListArea, () => files.Model.SelectedEntries.Count > 0, tab, SkipIf: () => files.Model.SelectedEntries.Count > 0, Show: export.CancelNow),
            // Details may have been closed before the tutorial: opened first
            // (the button is in the Files heading with the panes beside, and along the bottom with them below)
            new(T("Tour.Preview.OpenDetails"), () => files.Panes.OpenPart, () => !files.Panes.IsFolded, tab,
                SkipIf: () => !files.Panes.IsFolded, Show: export.CancelNow),
            // come to by Skip Step, or with Details closed again: the step opens it, so it always has its part to show
            new(T("Tour.Preview.Details"), () => Either(files.DetailsArea, files.Panes), null, tab, Show: () =>
            {
                if (files.Model.SelectedEntries.Count == 0 && Ammunition() is { } sample) files.Model.SelectEntry(sample);
                if (files.Panes.IsFolded) files.Panes.Fold(false);
            }),
            new(T("Tour.Preview.Export"), () => files.RunButton, () => export.IsOpen, tab, Show: export.CancelNow),
            new(T("Tour.Preview.Where"), () => export.WherePart, null, tab, Undone: () => !export.IsOpen, Show: InExport),
            new(T("Tour.Preview.Which"), () => export.FilesPart, null, tab, Undone: () => !export.IsOpen, Show: InExport),
            new(T("Tour.Preview.Options"), () => export.OptionsPart, null, tab, Undone: () => !export.IsOpen, Show: InExport),
            new(T("Tour.Preview.WillWrite"), () => export.ListPart, null, tab, Undone: () => !export.IsOpen, Show: InExport),
            new(T("Tour.Preview.Cancel"), () => export.CancelButton, () => !export.IsOpen, tab, Show: InExport),
            new(T("Tour.Preview.RealExport"), () => files.RunButton, null, tab, Show: export.CancelNow),
        ];
    }

    // ---- 4. The Editor ----

    private static List<Step> EditorSteps(MainView v)
    {
        Pages.EditorPage editor = v.EditorTab;
        TabItem tab = v.EditorTabItem;
        bool AmmunitionShown() => string.Equals(editor.ShownName, TutorialSample.Name, StringComparison.OrdinalIgnoreCase);
        // the file, its export or its changed lines, as a step begins
        Action View(string kind) => () => { if (AmmunitionShown()) _ = editor.ShowForTour(kind); };
        return
        [
            new(T("Tour.Editor.Start"), () => null, OnEnter: AddSample(v)),
            new(T("Tour.Editor.Tab"), () => tab, () => v.Tabs.SelectedItem == tab),
            new(T("Tour.Editor.Pick"), () => editor.FilePick, AmmunitionShown, tab, Needed: true),
            // the words' tips show under the mouse: the text stays usable in this step
            new(T("Tour.Editor.Text"), () => editor.EditorArea, null, tab, LookOnly: false, Show: View("file")),
            new(T("Tour.Editor.ShowExport"), () => editor.ExportButton, () => editor.ShowsExport, tab, Show: View("file")),
            new(T("Tour.Editor.Changes"), () => editor.ChangesButton, () => editor.ChangesButton.IsChecked == true, tab, Show: View("export")),
            new(T("Tour.Editor.Changed"), () => editor.EditorArea, null, tab, LookOnly: false, Show: View("changes")),
            // the Descriptor Tree is folded at first: its button opens it, then the tree and the values are shown apart
            // (its button is in the Editor's heading with the panes beside, and along the bottom with them below)
            new(T("Tour.Editor.OpenTree"), () => Either(editor.Halves.LeftButton, editor.Panes.OpenPart), () => !editor.Panes.IsFolded, tab, SkipIf: () => !editor.Panes.IsFolded),
            // either list may have been folded away by its own button before the tutorial
            new(T("Tour.Editor.OpenList", ("list", Texts.Get("Editor.Descriptors"))), () => editor.Halves.LeftButton, () => editor.Halves.LeftShows, tab,
                SkipIf: () => editor.Halves.LeftShows),
            // come to by Skip Step: the step opens the tree itself, so it always has its part to show
            new(T("Tour.Editor.Tree"), () => editor.TreeBox, null, tab, LookOnly: false, Show: () =>
            {
                if (editor.Panes.IsFolded || !editor.Halves.LeftShows) editor.Halves.Clicked(isLeft: true);
            }),
            new(T("Tour.Editor.OpenList", ("list", Texts.Get("Editor.Properties"))), () => editor.Halves.RightButton, () => editor.Halves.RightShows, tab,
                SkipIf: () => editor.Halves.RightShows),
            new(T("Tour.Editor.Properties"), () => editor.PropertiesBox, null, tab, Show: () =>
            {
                if (editor.Panes.IsFolded || !editor.Halves.RightShows) editor.Halves.Clicked(isLeft: false);
            }),
        ];
    }

    // ---- 5. The Armory ----

    private static List<Step> ArmorySteps(MainView v)
    {
        Pages.UnitsPage armory = v.UnitsTab;
        Models.ModelViewer viewer = v.ModelViewer;
        TabItem tab = v.UnitsTabItem;
        string showModel = Texts.Get("Units.Model.Menu.Show");
        // the Armory part a step is on, once its units are read
        Action Part(ArmoryPart part) => () =>
        {
            if (viewer.IsOpen) viewer.Close();
            if (armory.AllUnits.Count > 0 && armory.Part != part) armory.ShowPartNow(part.ToString());
        };
        // the picked unit's model, open
        void InViewer()
        {
            Menus.CloseOpen();
            if (!viewer.IsOpen && armory.SelectedNow is { } unit) armory.ShowModel(unit);
        }
        return
        [
            new(T("Tour.Armory.Start"), () => null),
            new(T("Tour.Armory.Tab"), () => tab, () => v.Tabs.SelectedItem == tab),
            new(T("Tour.Armory.Units"), () => armory.UnitsPartButton, () => armory.Part == ArmoryPart.Units, tab),
            new(T("Tour.Armory.Source"), () => armory.SourceChoice, () => armory.AllUnits.Count > 0, tab),
            new(T("Tour.Armory.Cards"), () => Either(armory.CardsList, armory.TableList), null, tab, Show: Part(ArmoryPart.Units)),
            new(T("Tour.Armory.Views"), () => armory.CardsButton, null, tab, Also: () => armory.TableButton, Show: Part(ArmoryPart.Units)),
            new(T("Tour.Armory.Find"), () => armory.SearchBox, null, tab, LookOnly: false, OnEnter: () => FocusSoon(() => armory.SearchBox), Show: Part(ArmoryPart.Units)),
            new(T("Tour.Armory.PickUnit"), () => Either(armory.CardsList, armory.TableList), () => armory.SelectedNow is not null, tab, Show: Part(ArmoryPart.Units)),
            new(T("Tour.Armory.Values"), () => armory.UnitPaneBox, null, tab, Show: Part(ArmoryPart.Units)),
            new(T("Tour.Armory.RightClick"), () => Either(armory.CardsList, armory.TableList), () => Menus.IsOpen || viewer.IsOpen, tab, Show: () =>
            {
                if (viewer.IsOpen) viewer.Close();
                Part(ArmoryPart.Units)();
            }),
            new(T("Tour.Armory.ShowModel"), () => Menus.OpenItem(showModel), () => viewer.IsOpen, tab, Undone: () => !Menus.IsOpen),
            new(T("Tour.Armory.Model"), () => viewer.Picture, null, tab, LookOnly: false, Undone: () => !viewer.IsOpen, Show: InViewer),
            new(T("Tour.Armory.ModelSide"), () => viewer.SideScroller, null, tab, Undone: () => !viewer.IsOpen, Show: InViewer),
            new(T("Tour.Armory.CloseModel"), () => viewer.CloseButton, () => !viewer.IsOpen, tab, Show: InViewer),
            // the name typed in Find would leave a division's list empty
            new(T("Tour.Armory.Divisions"), () => armory.DivisionsPartButton, () => armory.Part == ArmoryPart.Divisions, tab, Show: Part(ArmoryPart.Units)),
            // the name typed in Find would leave a division's list empty: emptied once Divisions shows
            new(T("Tour.Armory.DivisionList"), () => armory.DivisionsBox, null, tab, LookOnly: false, OnEnter: () => armory.SearchBox.Text = "", Show: Part(ArmoryPart.Divisions)),
            new(T("Tour.Armory.Battlegroups"), () => armory.BattlegroupsPartButton, () => armory.Part == ArmoryPart.Battlegroups, tab, Show: Part(ArmoryPart.Divisions)),
            new(T("Tour.Armory.Battlegroup"), () => armory.Panes, null, tab, Show: Part(ArmoryPart.Battlegroups)),
            new(T("Tour.Armory.Weapons"), () => armory.WeaponsPartButton, () => armory.Part == ArmoryPart.Weapons, tab, Show: Part(ArmoryPart.Battlegroups)),
            new(T("Tour.Armory.WeaponList"), () => armory.WeaponsView, null, tab, Show: Part(ArmoryPart.Weapons)),
            new(T("Tour.Armory.Done"), () => null),
        ];
    }
}
