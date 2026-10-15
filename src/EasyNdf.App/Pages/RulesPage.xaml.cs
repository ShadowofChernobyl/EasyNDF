using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Rules;
using EasyNdf.Data;
using EasyNdf.Engine.Mods;
using Microsoft.Win32;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Rules tab: the mods, their presets and the open preset's rules. The data is a <see cref="RulesModel"/>; this class
/// asks the questions (names, "Delete...?") in the message window and passes the answers on.
/// </summary>
public partial class RulesPage : UserControl
{
    /// <summary>The tab's data.</summary>
    public RulesModel Model { get; } = new();

    /// <summary>How many listed files use a preset, for the preset list (set by the main view, from the Files tab).</summary>
    public Func<ModPreset, int> FilesUsing { get; set; } = _ => 0;

    /// <summary>Undo and Redo, set by the main view; Ctrl+Z, Ctrl+Y and the right-click menus use it.</summary>
    public UndoHistory? History { get; set; }

    /// <summary>Builds the tab.</summary>
    public RulesPage()
    {
        InitializeComponent();
        DataContext = Model;
        Model.PropertyChanged += (_, _) => Show();
        AppData.Current.ModsChanged += _ => Model.Refresh();
        Show();
    }

    /// <summary>The message window of the main view this page is in.</summary>
    private MessageOverlay Messages => MainView.Of(this).Messages;

    // ---- showing it ----

    private bool showing;

    /// <summary>Fills in what the model does not bind: the Mod box, the lists, and which buttons can be used.</summary>
    private void Show()
    {
        showing = true;
        ModChoice.ItemsSource = Model.ModNames;
        ModChoice.SelectedItem = Model.OpenMod.Name;
        showing = false;

        IReadOnlyList<ModPreset> presets = Model.Presets;
        bool several = Model.SelectedPresets.Count > 1;
        PresetLines.ItemsSource = presets.Select(p => new PresetLine(p, Model.IsSelected(p), several && ReferenceEquals(p, Model.OpenPreset), FilesUsing(p))).ToList();
        NoPresets.Visibility = presets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RuleLines.ItemsSource = Model.RuleRows;
        NoRules.Visibility = Model.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        bool hasPreset = Model.OpenPreset is not null;
        RenamePresetButton.IsEnabled = hasPreset && !several;
        DuplicatePresetButton.IsEnabled = DeletePresetButton.IsEnabled = hasPreset;
        ClipNote.Visibility = Model.Note == "" ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A line of the preset list: its name, whether it is selected, and how many files use it.</summary>
    public sealed record PresetLine(ModPreset Preset, bool IsSelected, bool IsOpenAmongSeveral, int FilesUsing)
    {
        /// <summary>The preset's name.</summary>
        public string Name => Preset.Name;

        /// <summary>"3 files", or nothing when no file uses it.</summary>
        public string UseText => FilesUsing == 0 ? "" : FilesUsing + (FilesUsing == 1 ? " file" : " files");
    }

    // ---- the mod ----

    private void ModChosen(object sender, SelectionChangedEventArgs e)
    {
        if (!showing && ModChoice.SelectedItem is string name) Model.OpenModNamed(name);
    }

    private async void NewModClicked(object sender, RoutedEventArgs e)
    {
        string name = await Messages.AskName("Name for the new mod:");
        if (name == "") return;
        if (RulesModel.ModExists(name)) { await Messages.Tell("A mod with that name already exists."); return; }
        Model.NewMod(name);
    }

    private async void RenameModClicked(object sender, RoutedEventArgs e)
    {
        string old = Model.OpenMod.Name, name = await Messages.AskName("New name for the mod:", old);
        if (name == "" || name == old) return;
        if (RulesModel.ModExists(name)) { await Messages.Tell("A mod with that name already exists."); return; }
        Model.RenameMod(name);
    }

    private void CopyModClicked(object sender, RoutedEventArgs e) => Model.CopyMod();

    /// <summary>"Watermark...": the open mod's watermark comment.</summary>
    private void WatermarkClicked(object sender, RoutedEventArgs e) => _ = MainView.Of(this).Watermark.Edit(Model.OpenMod);

    private async void DeleteModClicked(object sender, RoutedEventArgs e)
    {
        int presets = Model.OpenMod.Presets.Count;
        string question = "Delete the mod \"" + Model.OpenMod.Name + "\"" + (presets > 0 ? " and its " + presets + " preset(s)? Export it first if you want to keep a copy." : "?");
        if (await Messages.Confirm(question, "OK")) Model.DeleteMod();
    }

    /// <summary>"Import mod...": adds the mods in mod files or backups; a mod of a name already here asks what to do.</summary>
    private async void ImportModClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Import mod", Filter = "Mod files (*.json)|*.json", Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await ImportModFiles(dialog.FileNames);
    }

    /// <summary>Imports mod files or backups (also the Settings tab's "Restore backup..."), asking about each mod of a name already here.</summary>
    public async Task ImportModFiles(IEnumerable<string> paths)
    {
        var problems = new List<string>();
        int added = 0, skipped = 0;
        Mod? last = null;
        foreach (string path in paths)
        {
            List<Mod> incoming;
            try { incoming = ModFiles.Read(File.ReadAllText(path), Path.GetFileName(path)); }
            catch (Exception error) when (error is ModFileException or IOException or UnauthorizedAccessException)
            {
                problems.Add(Path.GetFileName(path) + " (" + error.Message + ")");
                continue;
            }
            // what to do with each mod of a name already here, asked before anything changes
            var choices = new Dictionary<Mod, ImportChoice>();
            foreach (Mod mod in incoming)
            {
                if (AppData.Current.Mods.FirstOrDefault(m => m.Name == mod.Name) is not Mod existing) continue;
                DialogAnswer answer = await Messages.Ask(
                    "A mod named \"" + mod.Name + "\" already exists, with " + RulesModel.Count(existing.Presets.Count) + ". The file holds " + mod.Presets.Count + ". Replace it, keep both, or skip?",
                    new DialogOptions { Ok = "Replace", Alternative = "Keep both", Cancel = "Skip" });
                choices[mod] = answer.Choice switch { DialogChoice.Ok => ImportChoice.Replace, DialogChoice.Alternative => ImportChoice.KeepBoth, _ => ImportChoice.Skip };
            }
            ImportResult result = AppData.Current.ModStore.Import(incoming, (_, mod) => choices[mod]);
            added += result.Added;
            skipped += result.Skipped;
            last = result.LastMod ?? last;
        }
        if (last is not null)
        {
            AppData.Current.Open(last);
            AppData.Current.TellModsChanged(new ModsChange(PresetsAdded: true));
        }
        Model.Refresh();
        var parts = new List<string>();
        if (added > 0) parts.Add(added + " added");
        if (skipped > 0) parts.Add(skipped + " skipped");
        if (parts.Count > 0) Model.Say("Imported presets: " + string.Join(", ", parts) + ".");
        if (problems.Count > 0) await Messages.Tell("Could not import: " + string.Join(", ", problems));
    }

    /// <summary>"Export mod": saves the open mod as a mod file, to share or keep.</summary>
    private void ExportModClicked(object sender, RoutedEventArgs e)
    {
        Mod mod = Model.OpenMod;
        var dialog = new SaveFileDialog { Title = "Export mod", FileName = FileNames.ForName(mod.Name) + ".mod.json", Filter = "Mod files (*.mod.json)|*.mod.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, ModFiles.ExportText(mod));
            Model.Say("Exported \"" + mod.Name + "\": " + RulesModel.Count(mod.Presets.Count) + ".");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _ = Messages.Tell("The mod could not be saved. " + error.Message);
        }
    }

    // ---- presets ----

    private void PresetClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PresetLine line }) return;
        ModifierKeys keys = Keyboard.Modifiers;
        Model.ClickPreset(line.Preset, keys.HasFlag(ModifierKeys.Control), keys.HasFlag(ModifierKeys.Shift));
    }

    private async void NewPresetClicked(object sender, RoutedEventArgs e)
    {
        string name = await Messages.AskName("Name for the new preset:");
        if (name == "") return;
        if (Model.PresetExists(name)) { await Messages.Tell("This mod already has a preset with that name."); return; }
        Model.NewPreset(name);
    }

    private async void RenamePresetClicked(object sender, RoutedEventArgs e)
    {
        if (Model.OpenPreset is not ModPreset preset) return;
        string name = await Messages.AskName("New name:", preset.Name);
        if (name == "" || name == preset.Name) return;
        if (Model.PresetExists(name)) { await Messages.Tell("This mod already has a preset with that name."); return; }
        Model.RenamePreset(name);
    }

    private void DuplicatePresetClicked(object sender, RoutedEventArgs e) => Model.DuplicatePresets();

    private async void DeletePresetClicked(object sender, RoutedEventArgs e) => await DeleteSelectedPresets();

    /// <summary>Deletes the selected presets once the user agrees, saying how many files use them. Returns whether they were deleted.</summary>
    private async Task<bool> DeleteSelectedPresets()
    {
        IReadOnlyList<ModPreset> picked = Model.SelectedPresets;
        if (picked.Count == 0) return false;
        int used = picked.Sum(FilesUsing);
        string question = (picked.Count == 1 ? "Delete the preset \"" + picked[0].Name + "\"?" : "Delete these " + picked.Count + " presets?")
            + (used > 0 ? " " + used + (used == 1 ? " file uses" : " files use") + (picked.Count == 1 ? " it" : " them") + " and will have no preset." : "");
        if (!await Messages.Confirm(question, "OK")) return false;
        Model.DeletePresets();
        return true;
    }

    /// <summary>"Copy" and Ctrl+C: the selected presets go to the clipboard, as the HTML app copies them, so they can be pasted there too.</summary>
    private void CopyPresets()
    {
        IReadOnlyList<ModPreset> picked = Model.SelectedPresets;
        if (picked.Count == 0) { Model.Say("Select a preset to copy."); return; }
        // the presets as a mod file writes them, with every condition and action named, each marked as a preset
        var asMod = new Mod { Name = Model.OpenMod.Name, Presets = [.. picked] };
        var presets = (JsonArray)JsonNode.Parse(ModFiles.ExportText(asMod))!["Presets"]!;
        foreach (JsonNode? preset in presets) preset!["DataTypeToString"] = "Preset";
        Clipboard.SetText(presets.ToJsonString());
        Model.Say("Copied " + RulesModel.Count(picked.Count) + " to the clipboard.");
    }

    /// <summary>"Paste": presets on the clipboard become presets of the open mod; rules go in after the selected rule.</summary>
    private void PasteClicked(object sender, RoutedEventArgs e) => PasteFromClipboard();

    /// <summary>Pastes what is on the clipboard.</summary>
    public void PasteFromClipboard() => Paste(Clipboard.ContainsText() ? Clipboard.GetText() : "");

    /// <summary>Pastes text from the clipboard; presets it adds may be given to files without one. Returns whether presets were added.</summary>
    public bool Paste(string text)
    {
        bool presetsAdded = Model.PasteText(text);
        if (presetsAdded) AppData.Current.TellModsChanged(new ModsChange(PresetsAdded: true));
        return presetsAdded;
    }

    // ---- rules ----

    /// <summary>The rule clicked last and when, so a second click on it soon after opens it, as a double-click.</summary>
    private (int Index, DateTime At)? lastRuleClick;

    /// <summary>How soon a second click on a rule opens it.</summary>
    private static readonly TimeSpan RuleDoubleClickTime = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How soon a second click on a Group heading opens it. Shorter than for other rules: the first click has already
    /// folded or unfolded the group, so a double-click must be quick to be told from two single clicks.
    /// </summary>
    private static readonly TimeSpan GroupDoubleClickTime = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The line the left button went down on. A click counts only when it is let go on the same line: a press that
    /// closed a window or a menu by landing outside it never reaches the list, so letting go over a line does nothing.
    /// </summary>
    private int? pressedRuleIndex;

    private void RulePressed(object sender, MouseButtonEventArgs e) =>
        pressedRuleIndex = sender is FrameworkElement { DataContext: RuleRow row } ? row.Index : null;

    private void RuleClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RuleRow row }) return;
        bool pressedHere = pressedRuleIndex == row.Index;
        pressedRuleIndex = null;
        if (!pressedHere) return;
        ModifierKeys keys = Keyboard.Modifiers;
        bool control = keys.HasFlag(ModifierKeys.Control), shift = keys.HasFlag(ModifierKeys.Shift);
        TimeSpan doubleClickTime = row.IsGroup ? GroupDoubleClickTime : RuleDoubleClickTime;
        bool twice = !control && !shift && lastRuleClick is { } last && last.Index == row.Index && DateTime.UtcNow - last.At < doubleClickTime;
        lastRuleClick = twice || control || shift ? null : (row.Index, DateTime.UtcNow);
        Model.ClickRule(row.Index, control, shift);
        if (row.IsGroup && !control && !shift)
        {
            // a click on a group's heading folds or unfolds it at once, as on a folder in the file list; the second
            // click of a double-click puts it back as it was, and opens the group instead
            Model.ToggleFold(row.Index);
            // the list's lines are drawn anew, the one under the mouse too: WPF is told to look again at what the mouse
            // is over, or the second click of a double-click goes to the line that is gone
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, Mouse.Synchronize);
        }
        if (twice) OpenRuleWindow();
    }

    /// <summary>A click on a rule's type symbol opens it at once, as a click on an icon does.</summary>
    private void SymbolClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RuleRow row }) return;
        e.Handled = true;
        Model.SelectOnly(row.Index);
        Model.Refresh();
        OpenRuleWindow();
    }


    /// <summary>Opens the rule window on the selected rule (the HTML app's <c>ruleOpen</c>).</summary>
    public void OpenRuleWindow()
    {
        if (Model.OpenRule is null || History is null) return;
        activeList = KeyList.None;
        MainView.Of(this).RuleWindow.Open(Model, History);
    }

    /// <summary>The rule window closed: the rule list takes the keys again, its edge in the accent colour.</summary>
    public void RuleWindowClosed() => SetActiveList(KeyList.Rules);

    private void RuleSwitchClicked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: RuleRow row } box) Model.SetRuleOn(row.Index, box.IsChecked == true);
    }

    /// <summary>A rule's buttons, which say what they do in their Tag: fold, up, down, dup, del.</summary>
    private async void RuleButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RuleRow row, Tag: string action }) return;
        switch (action)
        {
            case "fold": Model.ToggleFold(row.Index); break;
            case "up" or "down" when row.IsGroup:
                // a heading asks first
                Model.SelectOnly(row.Index);
                Model.Refresh();
                await MoveSelectedRules(action == "up" ? -1 : +1);
                break;
            case "up": Model.MoveRule(row.Index, -1); break;
            case "down": Model.MoveRule(row.Index, +1); break;
            case "dup": Model.DuplicateRule(row.Index); break;
            case "del":
                Model.SelectOnly(row.Index);
                Model.Refresh();
                await DeleteSelectedRules();
                break;
        }
    }

    /// <summary>"New rule ▾": the menu of rule types opens above the button.</summary>
    private void NewRuleClicked(object sender, RoutedEventArgs e) => Menus.ShowAbove(NewRuleButton, NewRuleChoices());

    /// <summary>
    /// The rule types, in the order of the old dropdown, each with its symbol, and a tooltip on what it does; Group and
    /// Variable, which organise the others, after a line.
    /// </summary>
    internal IEnumerable<MenuChoice?> NewRuleChoices()
    {
        foreach ((string kind, string label) in RuleLoading.Kinds)
        {
            if (kind == "Check") yield return Menus.Line;
            (string symbol, string colour) = RuleRow.SymbolOf(kind);
            yield return new MenuChoice(label, () => AddRule(kind), Key: kind == "Edit" ? "Ctrl+N" : "",
                Tip: RuleRow.Summaries.GetValueOrDefault(kind, ""), Symbol: symbol, SymbolColour: colour);
        }
    }

    /// <summary>Adds a rule of a type below the selected one, and opens it.</summary>
    private void AddRule(string kind)
    {
        Model.NewRule(kind);
        OpenRuleWindow();
    }


    /// <summary>"Copy" and Ctrl+C: the selected rules go to the clipboard, each marked as a rule.</summary>
    private void CopyRules()
    {
        IReadOnlyList<int> picked = Model.SelectedRules;
        if (picked.Count == 0) { Model.Say("Select a rule to copy."); return; }
        var rules = new JsonArray();
        foreach (int place in picked)
        {
            var rule = (JsonObject)Model.Rules[place]!.DeepClone();
            rule["DataTypeToString"] = "Rule";
            rules.Add(rule);
        }
        Clipboard.SetText(rules.ToJsonString());
        Model.Say("Copied " + picked.Count + (picked.Count == 1 ? " rule" : " rules") + " to the clipboard.");
    }
}
