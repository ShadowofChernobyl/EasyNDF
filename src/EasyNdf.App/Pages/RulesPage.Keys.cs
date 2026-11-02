using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Mods;
using EasyNdf.App.Rules;
using EasyNdf.Data;
using EasyNdf.Data.Game;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Rules tab's keys and right-click menus. The keys act on the list clicked last (the HTML app's <c>active</c>): the
/// preset list, the Armory Changes list or the rule list, whose edge then shows in the accent colour.
/// </summary>
public partial class RulesPage
{
    /// <summary>The lists the keys can act on. Armory is the Armory Changes list: its keys act on presets too.</summary>
    private enum KeyList { None, Mods, Presets, Armory, Rules }

    private KeyList activeList;

    /// <summary>"Do not ask again" for deleting group headings, until the app is closed.</summary>
    private static bool groupHeadingsNeedNoQuestion;

    /// <summary>
    /// A click anywhere on the tab says which list the keys act on: the rule list or its Copy and Paste buttons, the
    /// preset list or its buttons, or neither.
    /// </summary>
    private void PageMouseDown(object sender, MouseButtonEventArgs e)
    {
        // a right-click picks the list without lighting its edge, as in the HTML app, where only a click does that
        if (e.ChangedButton != MouseButton.Left) return;
        var clicked = e.OriginalSource as DependencyObject;
        KeyList list = IsInside(clicked, RuleBox) ? KeyList.Rules
            : IsInside(clicked, PresetBox, NewPresetButton, RenamePresetButton, DuplicatePresetButton, DeletePresetButton) ? KeyList.Presets
            : IsInside(clicked, ArmoryBox) ? KeyList.Armory
            : IsInside(clicked, ModBox, RenameModButton, ExportModButton, WatermarkButton, DeleteModButton) ? KeyList.Mods
            : KeyList.None;
        SetActiveList(list);
        // a click on a line gives its list the keyboard, so the arrow keys go on from there; a switch or button keeps it
        if (BoxOf(list) is Bevel box && IsInside(clicked, box) && !IsInControl(clicked)) box.Focus();
    }

    /// <summary>The sunken box of a list that takes the keyboard; null for the Armory Changes list and for none.</summary>
    private Bevel? BoxOf(KeyList list) => list switch
    {
        KeyList.Mods => ModBox,
        KeyList.Presets => PresetBox,
        KeyList.Rules => RuleBox,
        _ => null,
    };

    /// <summary>Whether an element is a button, switch, text box or scroll bar, or inside one, which takes the keyboard itself.</summary>
    private static bool IsInControl(DependencyObject? element)
    {
        for (DependencyObject? at = element; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.Primitives.ScrollBar) return true;
            if (at is Bevel) return false;
        }
        return false;
    }

    /// <summary>A list got the keyboard, by a click or by Tab: the keys act on it, and its edge shows in the accent colour.</summary>
    private void ListGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        KeyList list = sender == ModBox ? KeyList.Mods : sender == PresetBox ? KeyList.Presets : KeyList.Rules;
        if (activeList != list) SetActiveList(list);
    }

    /// <summary>
    /// The keys of the list that has the keyboard: Up and Down move to the line above or below (Shift with them selects
    /// on, in the rule set and rule lists), Home and End go to the first or last line, Enter opens the line (a rule opens
    /// its window; a rule set hands the keyboard to its rules) and Space switches a mod or the selected rules on or off.
    /// The other keys go on to the tab's shortcuts.
    /// </summary>
    private void ListKeyDown(object sender, KeyEventArgs e)
    {
        // a switch or button in a line takes Space and Enter itself (Space presses it); the arrows still move the list
        bool fromLine = !ReferenceEquals(e.OriginalSource, sender);
        if (fromLine && e.Key is Key.Space or Key.Enter) return;
        ModifierKeys keys = Keyboard.Modifiers;
        if (keys.HasFlag(ModifierKeys.Control) || keys.HasFlag(ModifierKeys.Alt)) return;
        bool shift = keys.HasFlag(ModifierKeys.Shift);
        bool handled = sender == ModBox ? ModListKey(e.Key)
            : sender == PresetBox ? PresetListKey(e.Key, shift)
            : RuleListKey(e.Key, shift);
        if (!handled) return;
        e.Handled = true;
        // the lines are made again as the selection moves, the switch that had the keyboard with them: the list takes it
        if (fromLine) ((UIElement)sender).Focus();
    }

    /// <summary>Where a key moves to in a list of a number of lines from a line (-1 for none): the line above, below, first or last; null for any other key.</summary>
    private static int? StepTo(Key key, int at, int count) => key switch
    {
        Key.Up => Math.Max(0, at - 1),
        Key.Down => Math.Min(count - 1, at + 1),
        Key.Home => 0,
        Key.End => count - 1,
        _ => null,
    };

    /// <summary>A key in the mod list: the arrows open the mod above or below; Space switches the open mod on or off.</summary>
    private bool ModListKey(Key key)
    {
        IReadOnlyList<Mod> mods = ModStack.Ordered;
        if (mods.Count == 0) return false;
        int at = mods.ToList().IndexOf(Model.OpenMod);
        if (key == Key.Space)
        {
            ModStack.SetOn(Model.OpenMod, !ModStack.IsOn(Model.OpenMod));
            ShowMods();
            return true;
        }
        // a mod opens as soon as it is chosen: Enter hands the keyboard on to its rule sets
        if (key == Key.Enter)
        {
            PresetBox.Focus();
            return true;
        }
        if (StepTo(key, at, mods.Count) is not int to) return false;
        if (to != at) Model.OpenModNamed(mods[to].Name);
        BringLineIntoView(ModLines, to);
        return true;
    }

    /// <summary>A key in the rule set list: the arrows open the rule set above or below; Enter hands the keyboard to its rules.</summary>
    private bool PresetListKey(Key key, bool shift)
    {
        IReadOnlyList<ModPreset> presets = Model.Presets;
        if (presets.Count == 0) return false;
        int at = Model.OpenPreset is ModPreset open ? presets.ToList().IndexOf(open) : -1;
        if (key == Key.Enter)
        {
            RuleBox.Focus();
            return true;
        }
        if (StepTo(key, at, presets.Count) is not int to) return false;
        Model.ClickPreset(presets[to], control: false, shift: shift);
        BringLineIntoView(PresetLines, to);
        return true;
    }

    /// <summary>A key in the rule list: the arrows select the rule above or below; Enter opens it; Space switches the selected rules on or off.</summary>
    private bool RuleListKey(Key key, bool shift)
    {
        IReadOnlyList<RuleRow> rows = Model.RuleRows;
        if (rows.Count == 0) return false;
        int at = rows.ToList().FindIndex(row => row.Index == Model.CurrentRule);
        switch (key)
        {
            case Key.Enter:
                if (Model.SelectedRules.Count == 1) { Model.SelectOnly(Model.SelectedRules[0]); OpenRuleWindow(); }
                return true;
            case Key.Space:
                if (at >= 0) Model.SetSelectedRulesOn(!rows[at].IsOn);
                return true;
        }
        if (StepTo(key, at, rows.Count) is not int to) return false;
        Model.ClickRule(rows[to].Index, control: false, shift: shift);
        BringLineIntoView(RuleLines, to);
        return true;
    }

    /// <summary>
    /// Scrolls a list so a line shows. The rule list makes only the lines in view, so a line not made yet is brought in
    /// by its panel, which makes it.
    /// </summary>
    private static void BringLineIntoView(System.Windows.Controls.ItemsControl list, int index)
    {
        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement line) { line.BringIntoView(); return; }
        if (PanelOf(list) is System.Windows.Controls.VirtualizingStackPanel panel) panel.BringIndexIntoViewPublic(index);
    }

    /// <summary>The panel that holds a list's lines.</summary>
    private static System.Windows.Controls.VirtualizingStackPanel? PanelOf(DependencyObject element)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(element, i);
            if (child is System.Windows.Controls.VirtualizingStackPanel panel) return panel;
            if (PanelOf(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>Whether an element is one of the areas given, or inside one.</summary>
    private static bool IsInside(DependencyObject? element, params FrameworkElement[] areas)
    {
        for (DependencyObject? at = element; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (areas.Contains(at)) return true;
        return false;
    }

    /// <summary>Makes a list the one the keys act on; its edge shows in the accent colour (#ruleList.active).</summary>
    private void SetActiveList(KeyList list)
    {
        activeList = list;
        ShowActive(ModBox, list == KeyList.Mods);
        ShowActive(PresetBox, list == KeyList.Presets);
        ShowActive(ArmoryBox, list == KeyList.Armory);
        ShowActive(RuleBox, list == KeyList.Rules);
    }

    private static void ShowActive(Bevel box, bool isActive)
    {
        if (isActive)
        {
            box.SetResourceReference(Bevel.TopLeftProperty, "accent");
            box.SetResourceReference(Bevel.BottomRightProperty, "accent");
        }
        else
        {
            // back to the sunken edges the list's style gives
            box.ClearValue(Bevel.TopLeftProperty);
            box.ClearValue(Bevel.BottomRightProperty);
        }
    }

    /// <summary>A key the focused control did not take (see <see cref="MainView.HandleShortcut"/>).</summary>
    public void HandleShortcut(KeyEventArgs e)
    {
        // Keys typed in a text box or a dropdown are theirs.
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox or System.Windows.Controls.ComboBox or System.Windows.Controls.ComboBoxItem) return;
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool plain = Keyboard.Modifiers == ModifierKeys.None;

        // Paste works anywhere on the tab; the rest acts on the list clicked last, except the fold keys below.
        if (control && e.Key == Key.V)
        {
            e.Handled = true;
            if (ClipboardText() is { Length: > 0 } text) Paste(text);
            return;
        }
        // as do Expand all and Collapse all: a preset clicked last still has its rule list's groups shown beside it
        if (IsCollapseAllKey(e) || IsExpandAllKey(e))
        {
            e.Handled = true;
            Model.FoldAllGroups(collapsed: IsCollapseAllKey(e));
            return;
        }
        if (activeList == KeyList.Mods)
        {
            // the open mod moves up or down: the mods run in the list's order
            if (control && e.Key == Key.Up) { e.Handled = true; MoveOpenMod(-1); }
            else if (control && e.Key == Key.Down) { e.Handled = true; MoveOpenMod(+1); }
            else if (plain && e.Key == Key.F2) { e.Handled = true; RenameModClicked(this, e); }
            else if (plain && e.Key == Key.Delete) { e.Handled = true; DeleteModClicked(this, e); }
        }
        else if (activeList is KeyList.Presets or KeyList.Armory)
        {
            // the preset list is kept in name order, so nothing moves up or down in it; the Armory's parts are one at a time
            if (control && e.Key == Key.A && activeList == KeyList.Presets) { e.Handled = true; Model.SelectAllPresets(); }
            else if (control && e.Key == Key.C) { e.Handled = true; CopyPresets(); }
            else if (control && e.Key == Key.X) { e.Handled = true; _ = CutPresets(); }
            else if (plain && e.Key == Key.Delete) { e.Handled = true; DeletePresetClicked(this, e); }
        }
        else if (activeList == KeyList.Rules)
        {
            if (control && e.Key == Key.Up) { e.Handled = true; MoveSelectedRules(-1); }
            else if (control && e.Key == Key.Down) { e.Handled = true; MoveSelectedRules(+1); }
            else if (control && e.Key == Key.A) { e.Handled = true; Model.SelectAllRules(); }
            else if (control && e.Key == Key.C) { e.Handled = true; CopyRules(); }
            else if (control && e.Key == Key.X) { e.Handled = true; _ = CutRules(); }
            else if (control && e.Key == Key.N) { e.Handled = true; AddRule("Edit"); }
            else if (plain && e.Key == Key.Delete) { e.Handled = true; _ = DeleteSelectedRules(); }
            else if (plain && e.Key == Key.Enter && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.ButtonBase)
            {
                e.Handled = true;
                if (Model.SelectedRules.Count == 1) { Model.SelectOnly(Model.SelectedRules[0]); OpenRuleWindow(); }
            }
        }
    }

    // ---- acting on the selected rules ----

    /// <summary>Moves the selected rules up or down; with a group heading among them, whole groups move.</summary>
    private void MoveSelectedRules(int direction)
    {
        if (Model.SelectedRules.Count == 0) return;
        if (Model.SelectionHasGroupHeading) Model.MoveSelectedGroups(direction);
        else Model.MoveSelectedRules(direction);
    }

    /// <summary>
    /// Deletes the selected rules (the HTML app's <c>opDelete</c>). Deleting a group heading without its rules asks
    /// about that; otherwise the "Ask before deleting" setting asks. Returns whether they were deleted.
    /// </summary>
    private async Task<bool> DeleteSelectedRules()
    {
        IReadOnlyList<int> picked = Model.SelectedRules;
        if (picked.Count == 0) return false;
        if (picked.Any(Model.IsLocked)) return await TakeBackArmoryRules(picked);
        // the rules themselves, to find them again after the question
        List<JsonNode?> rules = [.. picked.Select(place => Model.Rules[place])];
        if (Model.DeletingLeavesGroupRulesBehind)
        {
            if (!await MayDeleteGroupHeading()) return false;
        }
        else if (AppData.Current.Settings["confirmDelete"]?.GetValue<bool>() != false)
        {
            string question = picked.Count == 1 ? Texts.Get("Rules.Rule.DeleteAsk", ("rule", Model.RuleName(picked[0]))) : Texts.Get("Rules.Rule.DeleteAskSeveral", ("count", picked.Count));
            if (!await Messages.Confirm(question, Texts.Get("Rules.Delete"))) return false;
        }
        List<int> places = [.. rules.Select(rule => Model.Rules.IndexOf(rule)).Where(place => place >= 0).Order()];
        if (places.Count == 0) return false;
        Model.DeleteRules(places);
        return true;
    }

    /// <summary>
    /// Deleting rules the Armory made takes its changes back: it asks first, saying so, then the Armory gives those
    /// values back as the game has them (a new division is deleted whole), and its rules go with them. A group heading
    /// of an Armory preset stands for every rule under it. Other rules selected with them are deleted as usual.
    /// </summary>
    private async Task<bool> TakeBackArmoryRules(IReadOnlyList<int> picked)
    {
        List<JsonNode?> armoryRules = [.. Model.WithRulesUnderGroups(picked).Where(Model.IsLocked).Select(place => Model.Rules[place])];
        List<JsonNode?> otherRules = [.. picked.Where(place => !Model.IsLocked(place)).Select(place => Model.Rules[place])];
        MainView view = MainView.Of(this);
        if (!view.UnitsTab.CanMakeEditable)
        {
            await Messages.Tell(view.UnitsTab.ArmoryRulesBlockedReason ?? Texts.Get("Rules.Locked.ArmoryNotRead"));
            return false;
        }
        if (!await Messages.Confirm(Texts.Get("Rules.Locked.DeleteAsk"), Texts.Get("Common.Menu.Delete"))) return false;
        List<int> places = [.. otherRules.Select(rule => Model.Rules.IndexOf(rule)).Where(place => place >= 0).Order()];
        if (places.Count > 0) Model.DeleteRules(places);
        view.UnitsTab.TakeBack(armoryRules);
        return true;
    }

    /// <summary>
    /// Asks before a group heading is deleted without its rules, since they then belong to the group above, or to none
    /// (the HTML app's <c>groupAsk</c>).
    /// </summary>
    private async Task<bool> MayDeleteGroupHeading()
    {
        if (groupHeadingsNeedNoQuestion) return true;
        DialogAnswer answer = await Messages.Ask(Texts.Get("Rules.Group.DeleteAsk"), new DialogOptions { Ok = Texts.Get("Common.Menu.Delete"), CheckText = Texts.Get("Rules.Group.DoNotAskAgain") });
        if (answer.Choice != DialogChoice.Ok) return false;
        if (answer.IsChecked) groupHeadingsNeedNoQuestion = true;
        return true;
    }

    /// <summary>Ctrl+X: the selected rules go to the clipboard, then are deleted. A deletion cancelled leaves them copied.</summary>
    private async Task CutRules()
    {
        IReadOnlyList<int> picked = Model.SelectedRules;
        if (picked.Count == 0) { Model.Say(Texts.Get("Rules.Rule.SelectToCut")); return; }
        int count = picked.Count;
        // nothing is deleted when the copy failed, as when another program has the clipboard open
        if (!CopyRules()) return;
        if (await DeleteSelectedRules()) Model.Say(Texts.Get("Rules.Cut", ("what", RulesModel.CountRules(count))));
    }

    /// <summary>
    /// Ctrl+X: copies the selected presets, then deletes them. Pasting them into another mod is how presets move between
    /// mods.
    /// </summary>
    private async Task CutPresets()
    {
        int count = Model.SelectedPresets.Count;
        if (count == 0) return;
        if (!CopyPresets()) return;
        if (await DeleteSelectedPresets()) Model.Say(Texts.Get("Rules.Cut", ("what", RulesModel.Count(count))));
    }

    // ---- right-click menus ----

    /// <summary>A right-click in the rule list: the menu for the rule under the mouse, or for the list.</summary>
    private void RuleListRightClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Menus.Show((FrameworkElement)sender, RuleMenu(RowUnder(e.OriginalSource)));
    }

    /// <summary>
    /// The rule list's right-click menu: for a rule, which is selected first unless it is already; or, with no rule under
    /// the mouse, for the list.
    /// </summary>
    internal IEnumerable<MenuChoice?> RuleMenu(RuleRow? row)
    {
        IEnumerable<MenuChoice?> tail =
        [
            new MenuChoice(Texts.Get("Rules.Menu.CreateNewRule"), () => Menus.Show(RuleBox, NewRuleChoices())),
            new MenuChoice(Texts.Get("Common.Menu.Paste"), () => PasteClicked(this, new RoutedEventArgs()), Key: "Ctrl+V"),
            .. FoldChoices(),
            .. UndoChoices(),
        ];
        if (Model.OpenPreset is null) return tail;
        activeList = KeyList.Rules;
        if (row is null) return tail;

        if (!Model.SelectedRules.Contains(row.Index))
        {
            Model.SelectOnly(row.Index);
            Model.Refresh();
        }
        int count = Model.SelectedRules.Count;
        bool isOn = row.IsOn;
        return
        [
            new MenuChoice(Texts.Get("Common.Menu.Configure"), () => { Model.SelectOnly(row.Index); OpenRuleWindow(); }, IsEnabled: count == 1, Key: "Enter"),
            new MenuChoice(Texts.Get("Common.Menu.Duplicate"), Model.DuplicateSelectedRules),
            new MenuChoice(Texts.Get("Common.Menu.MoveUp"), () => MoveSelectedRules(-1), Key: "Ctrl+Up"),
            new MenuChoice(Texts.Get("Common.Menu.MoveDown"), () => MoveSelectedRules(+1), Key: "Ctrl+Down"),
            new MenuChoice(count > 1 ? Texts.Get("Common.Menu.CutSeveral", ("what", RulesModel.CountRules(count))) : Texts.Get("Common.Menu.Cut"), () => _ = CutRules(), Key: "Ctrl+X"),
            new MenuChoice(count > 1 ? Texts.Get("Common.Menu.CopySeveral", ("what", RulesModel.CountRules(count))) : Texts.Get("Common.Menu.Copy"), () => CopyRules(), Key: "Ctrl+C"),
            new MenuChoice(isOn ? Texts.Get("Common.Menu.Disable") : Texts.Get("Common.Menu.Enable"), () => Model.SetSelectedRulesOn(!isOn)),
            new MenuChoice(count > 1 ? Texts.Get("Common.Menu.DeleteSeveral", ("what", RulesModel.CountRules(count))) : Texts.Get("Common.Menu.Delete"), () => _ = DeleteSelectedRules(), Key: "Delete"),
            Menus.Line,
            .. tail,
        ];
    }

    /// <summary>
    /// A right-click in the preset list or the Armory Changes list: the menu for the preset under the mouse, or for the
    /// list. An Armory preset is named after its part, so it cannot be renamed.
    /// </summary>
    private void PresetListRightClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var owner = (FrameworkElement)sender;
        activeList = sender == ArmoryBox ? KeyList.Armory : KeyList.Presets;
        IEnumerable<MenuChoice?> tail =
        [
            new MenuChoice(Texts.Get("Rules.Menu.CreateNewPreset"), () => NewPresetClicked(this, e)),
            new MenuChoice(Texts.Get("Common.Menu.Paste"), () => PasteClicked(this, e), Key: "Ctrl+V"),
            .. UndoChoices(),
        ];
        if (PresetUnder(e.OriginalSource) is not ModPreset preset) { Menus.Show(owner, tail); return; }

        if (!Model.IsSelected(preset)) Model.ClickPreset(preset, control: false, shift: false);
        int count = Model.SelectedPresets.Count;
        string several = RulesModel.Count(count);
        Menus.Show(owner,
        [
            new MenuChoice(Texts.Get("Common.Menu.Rename"), () => RenamePresetClicked(this, e), IsEnabled: count == 1 && !preset.IsArmoryPreset),
            new MenuChoice(Texts.Get("Rules.Menu.AppliesTo"), () => OpenAppliesTo(preset), IsEnabled: count == 1 && !preset.IsArmoryPreset),
            new MenuChoice(count > 1 ? Texts.Get("Common.Menu.DuplicateSeveral", ("what", several)) : Texts.Get("Common.Menu.Duplicate"), Model.DuplicatePresets),
            new MenuChoice(count > 1 ? Texts.Get("Common.Menu.CopySeveral", ("what", several)) : Texts.Get("Common.Menu.Copy"), () => CopyPresets(), Key: "Ctrl+C"),
            new MenuChoice(count > 1 ? Texts.Get("Common.Menu.CutSeveral", ("what", several)) : Texts.Get("Common.Menu.Cut"), () => _ = CutPresets(), Key: "Ctrl+X"),
            new MenuChoice(count > 1 ? Texts.Get("Common.Menu.DeleteSeveral", ("what", several)) : Texts.Get("Common.Menu.Delete"), () => DeletePresetClicked(this, e), Key: "Delete"),
            Menus.Line,
            .. tail,
        ]);
    }

    /// <summary>Ctrl+Shift+[ collapses every group of the rule list, as VS Code folds with it; the file list's folders use it too.</summary>
    internal static bool IsCollapseAllKey(KeyEventArgs e) =>
        e.Key == Key.OemOpenBrackets && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);

    /// <summary>Ctrl+Shift+] expands every group again.</summary>
    internal static bool IsExpandAllKey(KeyEventArgs e) =>
        e.Key == Key.OemCloseBrackets && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);

    /// <summary>"Expand all" and "Collapse all" in the rule list's menu, when the preset has groups.</summary>
    private IEnumerable<MenuChoice?> FoldChoices()
    {
        if (Model.OpenPreset is null || !Model.HasGroups) return [];
        return
        [
            new MenuChoice(Texts.Get("Files.ExpandAll"), () => Model.FoldAllGroups(collapsed: false), Key: "Ctrl+Shift+]"),
            new MenuChoice(Texts.Get("Files.CollapseAll"), () => Model.FoldAllGroups(collapsed: true), Key: "Ctrl+Shift+["),
        ];
    }

    /// <summary>Undo and Redo at the end of the lists' menus, as there are no buttons for them.</summary>
    private IEnumerable<MenuChoice?> UndoChoices() =>
    [
        Menus.Line,
        new MenuChoice(Texts.Get("Common.Menu.Undo"), () => MainView.Of(this).Undo(), History?.CanUndo == true, "Ctrl+Z"),
        new MenuChoice(Texts.Get("Common.Menu.Redo"), () => MainView.Of(this).Redo(), History?.CanRedo == true, "Ctrl+Y"),
    ];

    /// <summary>
    /// A right-click on the mod list: on a mod, it opens, and the menu acts on it; on the list's empty part, the menu
    /// has only New and Import.
    /// </summary>
    private void ModListRightClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var onMod = new List<MenuChoice?>();
        if (ModUnder(e.OriginalSource) is Mod mod)
        {
            Model.OpenModNamed(mod.Name);
            IReadOnlyList<Mod> ordered = ModStack.Ordered;
            bool isOn = ModStack.IsOn(mod);
            onMod =
            [
                new MenuChoice(Texts.Get(isOn ? "Rules.Menu.ModOff" : "Rules.Menu.ModOn"), () => { ModStack.SetOn(mod, !isOn); ShowMods(); }),
                new MenuChoice(Texts.Get("Rules.Menu.MoveModUp"), () => MoveOpenMod(-1), IsEnabled: !ReferenceEquals(ordered[0], mod), Key: "Ctrl+↑"),
                new MenuChoice(Texts.Get("Rules.Menu.MoveModDown"), () => MoveOpenMod(+1), IsEnabled: !ReferenceEquals(ordered[^1], mod), Key: "Ctrl+↓"),
                // the games listed under it, the mod's own ticked: a list, not a switch, as more games may come
                MenuChoice.Submenu(Texts.Get("Rules.Menu.Game"), [.. GameTitle.All.Select(game => (MenuChoice?)new MenuChoice(game.Name,
                    () => { Model.SetModGame(mod, game); ShowMods(); }, Tip: Texts.Get("Rules.Menu.Game.Tip", ("game", game.Name)), Symbol: mod.Game == game ? "✓" : ""))]),
                Menus.Line,
                new MenuChoice(Texts.Get("Common.Menu.Rename"), () => RenameModClicked(this, e), Key: "F2"),
                new MenuChoice(Texts.Get("Rules.Menu.MakeCopy"), () => CopyModClicked(this, e)),
                new MenuChoice(Texts.Get("Rules.ExportMod"), () => ExportModClicked(this, e)),
                new MenuChoice(Texts.Get("Rules.Watermark"), () => WatermarkClicked(this, e)),
                new MenuChoice(Texts.Get("Rules.Menu.DeleteMod"), () => DeleteModClicked(this, e), Key: "Del"),
                Menus.Line,
            ];
        }
        Menus.Show((FrameworkElement)sender,
        [
            .. onMod,
            new MenuChoice(Texts.Get("Rules.Menu.CreateNewMod"), () => NewModClicked(this, e)),
            new MenuChoice(Texts.Get("Rules.ImportMod"), () => ImportModClicked(this, e)),
        ]);
    }

    /// <summary>The mod of the line an element is on, in the mod list; null when none is.</summary>
    private static Mod? ModUnder(object source)
    {
        for (DependencyObject? at = source as DependencyObject; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is FrameworkElement { DataContext: ModLine line }) return line.Mod;
            if (at is FrameworkElement { DataContext: ModLetter letter }) return letter.Mod;
        }
        return null;
    }

    /// <summary>The rule line an element is on, if any.</summary>
    private static RuleRow? RowUnder(object source)
    {
        for (DependencyObject? at = source as DependencyObject; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is FrameworkElement { DataContext: RuleRow row }) return row;
        return null;
    }

    /// <summary>The preset of the line an element is on, in the preset list or the Armory Changes list; null when none is.</summary>
    private static ModPreset? PresetUnder(object source)
    {
        for (DependencyObject? at = source as DependencyObject; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is FrameworkElement { DataContext: PresetLine line }) return line.Preset;
            if (at is FrameworkElement { DataContext: ArmoryLine armoryLine }) return armoryLine.Preset;
        }
        return null;
    }
}
