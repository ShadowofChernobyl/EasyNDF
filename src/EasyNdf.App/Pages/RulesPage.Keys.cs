using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Rules;
using EasyNdf.Data;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Rules tab's keys and right-click menus. The keys act on the list clicked last (the HTML app's <c>active</c>): the
/// preset list or the rule list, whose edge then shows in the accent colour.
/// </summary>
public partial class RulesPage
{
    /// <summary>The lists the keys can act on.</summary>
    private enum KeyList { None, Presets, Rules }

    private KeyList activeList;

    /// <summary>"Do not ask again" for moving and deleting group headings, until the app is closed.</summary>
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
            : KeyList.None;
        SetActiveList(list);
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
        ShowActive(PresetBox, list == KeyList.Presets);
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
            if (Clipboard.ContainsText() && Clipboard.GetText() is { Length: > 0 } text) Paste(text);
            return;
        }
        // as do Expand all and Collapse all: a preset clicked last still has its rule list's groups shown beside it
        if (IsCollapseAllKey(e) || IsExpandAllKey(e))
        {
            e.Handled = true;
            Model.FoldAllGroups(collapsed: IsCollapseAllKey(e));
            return;
        }
        if (activeList == KeyList.Presets)
        {
            // the preset list is kept in name order, so nothing moves up or down in it
            if (control && e.Key == Key.A) { e.Handled = true; Model.SelectAllPresets(); }
            else if (control && e.Key == Key.C) { e.Handled = true; CopyPresets(); }
            else if (control && e.Key == Key.X) { e.Handled = true; _ = CutPresets(); }
            else if (plain && e.Key == Key.Delete) { e.Handled = true; DeletePresetClicked(this, e); }
        }
        else if (activeList == KeyList.Rules)
        {
            if (control && e.Key == Key.Up) { e.Handled = true; _ = MoveSelectedRules(-1); }
            else if (control && e.Key == Key.Down) { e.Handled = true; _ = MoveSelectedRules(+1); }
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

    /// <summary>Moves the selected rules up or down; a group heading among them asks first.</summary>
    private async Task MoveSelectedRules(int direction)
    {
        if (Model.SelectedRules.Count == 0) return;
        if (Model.SelectionHasGroupHeading && !await MayChangeGroupHeading(isDelete: false)) return;
        Model.MoveSelectedRules(direction);
    }

    /// <summary>
    /// Deletes the selected rules (the HTML app's <c>opDelete</c>). Deleting a group heading without its rules asks
    /// about that; otherwise the "Ask before deleting" setting asks. Returns whether they were deleted.
    /// </summary>
    private async Task<bool> DeleteSelectedRules()
    {
        IReadOnlyList<int> picked = Model.SelectedRules;
        if (picked.Count == 0) return false;
        // the rules themselves, to find them again after the question
        List<JsonNode?> rules = [.. picked.Select(place => Model.Rules[place])];
        if (Model.DeletingLeavesGroupRulesBehind)
        {
            if (!await MayChangeGroupHeading(isDelete: true)) return false;
        }
        else if (AppData.Current.Settings["confirmDelete"]?.GetValue<bool>() != false)
        {
            string question = picked.Count == 1 ? Texts.Get("Rules.Rule.DeleteAsk", ("rule", Model.RuleName(picked[0]))) : Texts.Get("Rules.Rule.DeleteAskSeveral", ("count", picked.Count));
            if (!await Messages.Confirm(question, "OK")) return false;
        }
        List<int> places = [.. rules.Select(rule => Model.Rules.IndexOf(rule)).Where(place => place >= 0).Order()];
        if (places.Count == 0) return false;
        Model.DeleteRules(places);
        return true;
    }

    /// <summary>
    /// Asks before a group heading is moved or deleted, since that changes which rules belong to the group (the HTML
    /// app's <c>groupAsk</c>).
    /// </summary>
    private async Task<bool> MayChangeGroupHeading(bool isDelete)
    {
        if (groupHeadingsNeedNoQuestion) return true;
        string question = isDelete
            ? Texts.Get("Rules.Group.DeleteAsk")
            : Texts.Get("Rules.Group.MoveAsk");
        DialogAnswer answer = await Messages.Ask(question, new DialogOptions { Ok = isDelete ? Texts.Get("Common.Menu.Delete") : Texts.Get("Rules.Group.Move"), CheckText = Texts.Get("Rules.Group.DoNotAskAgain") });
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
        CopyRules();
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
        CopyPresets();
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
            new MenuChoice("Create New Rule", () => Menus.Show(RuleBox, NewRuleChoices())),
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
            new MenuChoice("Move Up", () => _ = MoveSelectedRules(-1), Key: "Ctrl+Up"),
            new MenuChoice("Move Down", () => _ = MoveSelectedRules(+1), Key: "Ctrl+Down"),
            new MenuChoice(count > 1 ? Texts.Get("Common.Menu.CutSeveral", ("what", RulesModel.CountRules(count))) : Texts.Get("Common.Menu.Cut"), () => _ = CutRules(), Key: "Ctrl+X"),
            new MenuChoice(count > 1 ? Texts.Get("Common.Menu.CopySeveral", ("what", RulesModel.CountRules(count))) : Texts.Get("Common.Menu.Copy"), CopyRules, Key: "Ctrl+C"),
            new MenuChoice(isOn ? Texts.Get("Common.Menu.Disable") : Texts.Get("Common.Menu.Enable"), () => Model.SetSelectedRulesOn(!isOn)),
            new MenuChoice(count > 1 ? Texts.Get("Common.Menu.DeleteSeveral", ("what", RulesModel.CountRules(count))) : Texts.Get("Common.Menu.Delete"), () => _ = DeleteSelectedRules(), Key: "Delete"),
            Menus.Line,
            .. tail,
        ];
    }

    /// <summary>A right-click in the preset list: the menu for the preset under the mouse, or for the list.</summary>
    private void PresetListRightClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var owner = (FrameworkElement)sender;
        activeList = KeyList.Presets;
        IEnumerable<MenuChoice?> tail =
        [
            new MenuChoice("Create New Preset", () => NewPresetClicked(this, e)),
            new MenuChoice(Texts.Get("Common.Menu.Paste"), () => PasteClicked(this, e), Key: "Ctrl+V"),
            .. UndoChoices(),
        ];
        if (LineUnder(e.OriginalSource) is not PresetLine line) { Menus.Show(owner, tail); return; }

        if (!Model.IsSelected(line.Preset)) Model.ClickPreset(line.Preset, control: false, shift: false);
        int count = Model.SelectedPresets.Count;
        string several = RulesModel.Count(count);
        Menus.Show(owner,
        [
            new MenuChoice(Texts.Get("Common.Menu.Rename"), () => RenamePresetClicked(this, e), IsEnabled: count == 1),
            new MenuChoice(count > 1 ? Texts.Get("Common.Menu.DuplicateSeveral", ("what", several)) : Texts.Get("Common.Menu.Duplicate"), Model.DuplicatePresets),
            new MenuChoice(count > 1 ? Texts.Get("Common.Menu.CopySeveral", ("what", several)) : Texts.Get("Common.Menu.Copy"), CopyPresets, Key: "Ctrl+C"),
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

    /// <summary>A right-click on the Mod box.</summary>
    private void ModBoxRightClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Menus.Show((FrameworkElement)sender,
        [
            new MenuChoice(Texts.Get("Common.Menu.Rename"), () => RenameModClicked(this, e)),
            new MenuChoice("Make Copy", () => CopyModClicked(this, e)),
            new MenuChoice(Texts.Get("Rules.ExportMod"), () => ExportModClicked(this, e)),
            new MenuChoice(Texts.Get("Rules.Watermark"), () => WatermarkClicked(this, e)),
            new MenuChoice("Delete Mod", () => DeleteModClicked(this, e)),
            Menus.Line,
            new MenuChoice("Create New Mod", () => NewModClicked(this, e)),
            new MenuChoice(Texts.Get("Rules.ImportMod"), () => ImportModClicked(this, e)),
        ]);
    }

    /// <summary>The rule line an element is on, if any.</summary>
    private static RuleRow? RowUnder(object source)
    {
        for (DependencyObject? at = source as DependencyObject; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is FrameworkElement { DataContext: RuleRow row }) return row;
        return null;
    }

    /// <summary>The preset line an element is on, if any.</summary>
    private static PresetLine? LineUnder(object source)
    {
        for (DependencyObject? at = source as DependencyObject; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is FrameworkElement { DataContext: PresetLine line }) return line;
        return null;
    }
}
