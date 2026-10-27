using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Mods;
using EasyNdf.App.Rules;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.FileList;
using EasyNdf.Engine.Mods;
using Microsoft.Win32;
using EasyNdf.App.Text;

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
        // the rule lines' type names and summaries are written in the language in use
        Texts.LanguageChanged += Model.Refresh;
        // a Preview or export found which values two mods both change
        ModConflictList.Changed += ShowMods;
        SetUpColumnGrips();
        Show();
    }

    /// <summary>The message window of the main view this page is in.</summary>
    private MessageOverlay Messages => MainView.Of(this).Messages;

    // ---- showing it ----

    /// <summary>Fills in what the model does not bind: the mod list, the other lists, and which buttons can be used.</summary>
    private void Show()
    {
        // the rule window's changes show once Done saves them: drawing the lists again on every key made typing slow
        if (Model.IsEditingRule) return;
        ShowMods();
        ShowAppliesTo();

        IReadOnlyList<ModPreset> presets = Model.Presets;
        bool several = Model.SelectedPresets.Count > 1;
        PresetLines.ItemsSource = presets.Select(p => new PresetLine(p, Model.IsSelected(p), several && ReferenceEquals(p, Model.OpenPreset), FilesUsing(p))).ToList();
        NoPresets.Visibility = presets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ArmoryLines.ItemsSource = ArmoryPresets.Parts.Select(ArmoryLineOf).ToList();

        ShowRuleLines();
        NoRules.Visibility = Model.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        bool hasPreset = Model.OpenPreset is not null;
        // an Armory preset is named after its part of the Armory
        RenamePresetButton.IsEnabled = hasPreset && !several && !Model.OpenPreset!.IsArmoryPreset;
        DuplicatePresetButton.IsEnabled = DeletePresetButton.IsEnabled = hasPreset;
        ClipNote.Visibility = Model.Note == "" ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// The rule lines, drawn again from the model's rows; the list stays where it was scrolled to, as it did while every
    /// line was made: a new list of lines can send a list that makes only those in view back to its top.
    /// </summary>
    private void ShowRuleLines()
    {
        if (ReferenceEquals(RuleLines.ItemsSource, Model.RuleRows)) return;
        var scroller = RuleLines.Template?.FindName("RuleScroller", RuleLines) as ScrollViewer;
        double offset = scroller?.VerticalOffset ?? 0;
        RuleLines.ItemsSource = Model.RuleRows;
        if (scroller is null || offset <= 0) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (Math.Abs(scroller.VerticalOffset - offset) > 0.5) scroller.ScrollToVerticalOffset(offset);
        });
    }

    /// <summary>A line of the preset list: its name, whether it is selected, and how many files use it.</summary>
    public sealed record PresetLine(ModPreset Preset, bool IsSelected, bool IsOpenAmongSeveral, int FilesUsing)
    {
        /// <summary>The preset's name.</summary>
        public string Name => Preset.Name;

        /// <summary>
        /// Whether the rule set applies to no file at all: it has no files of its own and its name holds no file name,
        /// as a "New rule set" just made. It never runs until its Applies To is set.
        /// </summary>
        public bool AppliesToNothing => RuleSetFiles.Named(Preset).Count == 0;

        /// <summary>"3 files" on the Files tab it applies to; "⚠ no file" when it applies to none at all; nothing otherwise.</summary>
        public string UseText => AppliesToNothing ? Texts.Get("Rules.Preset.NoFile") : FilesUsing == 0 ? "" : Texts.Count("Rules.Preset.FilesUsing", FilesUsing);

        /// <summary>The tooltip of "⚠ no file".</summary>
        public string? UseTip => AppliesToNothing ? Texts.Get("Rules.AppliesTo.None.Tip") : null;
    }

    /// <summary>
    /// A line of the Armory Changes list: a part of the Armory, and its preset of the changes made there; null when no
    /// change was made there.
    /// </summary>
    public sealed record ArmoryLine(ArmoryPart Part, ModPreset? Preset, bool IsSelected)
    {
        /// <summary>The part's name, as its sub-tab in the Armory has it.</summary>
        public string Name => ArmoryPartTexts.Name(Part);

        /// <summary>How many rules its preset has, group headings left out; "None" when it has none.</summary>
        public string CountText => Preset is null ? Texts.Get("Rules.Armory.None")
            : RulesModel.CountRules(Preset.Rules.Count(rule => (string?)rule?["Kind"] != "Group"));

        /// <summary>Whether no change was made in the part: its name is dim, and it opens with no rules.</summary>
        public bool IsEmpty => Preset is null;

        /// <summary>What the part's preset holds, and when it runs.</summary>
        public string ToolTip => ArmoryPartTexts.PresetTip(Part);
    }

    /// <summary>The Armory Changes line of a part of the Armory, for the open mod.</summary>
    private ArmoryLine ArmoryLineOf(ArmoryPart part)
    {
        ModPreset? preset = ArmoryPresets.Of(Model.OpenMod, part);
        bool isOpen = preset is not null ? Model.IsSelected(preset) : Model.OpenEmptyArmoryPart == part;
        return new ArmoryLine(part, preset, isOpen);
    }

    // ---- the mods ----

    /// <summary>
    /// A line of the mod list: the mod, whether it is the open one, whether it is on, its Armory parts as letters, and
    /// the values another mod that is on also changes.
    /// </summary>
    public sealed record ModLine(Mod Mod, bool IsSelected, bool IsOn, IReadOnlyList<ModLetter> Letters, IReadOnlyList<ModConflict> Conflicts)
    {
        /// <summary>The mod's name.</summary>
        public string Name => Mod.Name;

        /// <summary>Whether another mod that is on changes a value this one does: it is marked ⚠.</summary>
        public bool HasConflicts => Conflicts.Count > 0;

        /// <summary>The most values the ⚠'s tooltip lists; the rest are counted.</summary>
        private const int TipLines = 12;

        /// <summary>
        /// The ⚠'s tooltip: how many values, then each with the file and the mod that wins, as "Ammunition.ndf ›
        /// Ammo_X › MaximumRangeGRU: WTO wins over My mod". The mod lower on the list runs later and wins.
        /// </summary>
        public string ConflictsTip
        {
            get
            {
                var lines = new List<string> { Texts.Count("Rules.ModConflicts.Tip", Conflicts.Count) };
                foreach (ModConflict conflict in Conflicts.Take(TipLines))
                    lines.Add(Texts.Get("Rules.ModConflict.Line", ("file", conflict.File), ("descriptor", conflict.Descriptor), ("property", conflict.Property),
                        ("winner", conflict.Winner), ("other", conflict.Overridden)));
                if (Conflicts.Count > TipLines) lines.Add(Texts.Count("Rules.ModConflicts.More", Conflicts.Count - TipLines));
                return string.Join("\n", lines);
            }
        }
    }

    /// <summary>
    /// A letter of a mod's line, as the Files tab's Armory column has them: D, B, U or W, lit when the mod has changes
    /// in that part of the Armory.
    /// </summary>
    public sealed record ModLetter(Mod Mod, ArmoryPart Part, bool IsLit, int RuleCount)
    {
        /// <summary>The part's letter.</summary>
        public string Text => ArmoryLetterTexts.Letter(Part);

        /// <summary>What it stands for, and how many rules the mod's changes there make.</summary>
        public string ToolTip => IsLit
            ? Texts.Count("Rules.ModLetter.Has.Tip", RuleCount, ("part", ArmoryPartTexts.Name(Part)))
            : Texts.Get("Rules.ModLetter.None.Tip", ("part", ArmoryPartTexts.Name(Part)));
    }

    /// <summary>
    /// The mod list, in the order the mods run, the open mod's line marked; and, folded, the strip naming the open mod.
    /// </summary>
    private void ShowMods()
    {
        IReadOnlyList<Mod> mods = ModStack.Ordered;
        ModLines.ItemsSource = mods
            .Select(mod => new ModLine(mod, ReferenceEquals(mod, Model.OpenMod), ModStack.IsOn(mod), LettersOf(mod), ModConflictList.Of(mod))).ToList();
        ModsLine.Text = Texts.Get("Rules.ModsOn", ("on", Texts.Number(mods.Count(ModStack.IsOn))), ("all", Texts.Number(mods.Count)));
        RuleSetsModName.Text = Model.OpenMod.Name;
        ShowModOffLine();
    }

    /// <summary>The mod whose "is off" line was hidden with OK: it stays hidden until another mod is opened or the mod is switched on.</summary>
    private Mod? modOffLineHiddenFor;

    /// <summary>
    /// The line under the Mods list while the open mod is off: "⚠ My mod is off and won't run." with Switch On, as its
    /// rules can be edited but Preview and Run export skip it, which is easy to miss.
    /// </summary>
    private void ShowModOffLine()
    {
        Mod open = Model.OpenMod;
        bool isOff = !ModStack.IsOn(open);
        // OK hides the line for this mod only, while it stays off
        if (!isOff || !ReferenceEquals(modOffLineHiddenFor, open)) modOffLineHiddenFor = null;
        ModOffText.Text = Texts.Get("Rules.ModOff", ("mod", open.Name));
        ModOffLine.Visibility = isOff && modOffLineHiddenFor is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Switch On: the open mod is switched on, as its switch in the list does; Undo takes it back.</summary>
    private void ModOffSwitchOnClicked(object sender, RoutedEventArgs e)
    {
        ModStack.SetOn(Model.OpenMod, true);
        ShowMods();
    }

    /// <summary>OK: the line goes until another mod is opened, or this one is switched on and off again.</summary>
    private void ModOffOkClicked(object sender, RoutedEventArgs e)
    {
        modOffLineHiddenFor = Model.OpenMod;
        ShowModOffLine();
    }

    // ---- which files the open rule set applies to ----

    /// <summary>
    /// The line under the rules' heading: "Applies to Ammunition.ndf", "Applies to Ammunition.ndf and 2 other files"
    /// (the tooltip lists them all), or "Applies to no file" in the caution colour, with Change... An Armory preset's
    /// files are those its changes are written into; they follow the changes, so it has no Change....
    /// </summary>
    private void ShowAppliesTo()
    {
        AppliesText.Inlines.Clear();
        AppliesText.ToolTip = null;
        IReadOnlyList<string>? files = Model.OpenPreset is ModPreset ruleSet
            ? ruleSet.IsArmoryPreset ? [.. DivisionChangeRules.FilesOf(ruleSet.Rules).Order(StringComparer.OrdinalIgnoreCase)] : RuleSetFiles.Named(ruleSet)
            : null;
        ChangeAppliesButton.Visibility = Model.OpenPreset is { IsArmoryPreset: false } ? Visibility.Visible : Visibility.Collapsed;
        if (files is null) return;
        if (files.Count == 0)
        {
            AppliesText.Inlines.Add(Coloured(Texts.Get("Rules.AppliesTo.None"), "caution"));
            AppliesText.ToolTip = Texts.Get("Rules.AppliesTo.None.Tip");
            return;
        }
        AppliesText.Inlines.Add(Coloured(Texts.Get("Rules.AppliesTo.Lead") + " ", "muted"));
        var first = new Run(files[0]);
        first.SetResourceReference(TextElement.FontFamilyProperty, "mono");
        AppliesText.Inlines.Add(first);
        if (files.Count > 1)
        {
            AppliesText.Inlines.Add(Coloured(" " + Texts.Count("Rules.AppliesTo.Others", files.Count - 1, ("count", Texts.Number(files.Count - 1))), "muted"));
            AppliesText.ToolTip = string.Join("\n", files);
        }
    }

    /// <summary>A piece of text in one of the theme's colours.</summary>
    private static Run Coloured(string words, string colour)
    {
        var run = new Run(words);
        run.SetResourceReference(TextElement.ForegroundProperty, colour);
        return run;
    }

    /// <summary>"Change...": the Applies To window for the open rule set.</summary>
    private void ChangeAppliesClicked(object sender, RoutedEventArgs e)
    {
        if (Model.OpenPreset is ModPreset ruleSet) OpenAppliesTo(ruleSet);
    }

    /// <summary>The Applies To window for a rule set; Done saves the choice in the mod.</summary>
    private void OpenAppliesTo(ModPreset ruleSet)
    {
        if (ruleSet.IsArmoryPreset) return;
        MainView view = MainView.Of(this);
        view.AppliesTo.Open(ruleSet, view.FilesTab.Model.Entries, files => Model.SetAppliesTo(ruleSet, files));
    }

    /// <summary>A mod's letters: each part of the Armory, lit when the mod's preset for it has rules.</summary>
    private static List<ModLetter> LettersOf(Mod mod) =>
    [
        .. ArmoryPresets.Parts.Select(part =>
        {
            int rules = ArmoryPresets.Of(mod, part)?.Rules.Count(rule => (string?)rule?["Kind"] != "Group") ?? 0;
            return new ModLetter(mod, part, rules > 0, rules);
        }),
    ];

    /// <summary>A click on a mod's line opens it.</summary>
    private void ModClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModLine line }) Model.OpenModNamed(line.Name);
    }

    /// <summary>A mod's switch: on, it runs with the other mods that are on; off, it does not run. Undo takes it back.</summary>
    private void ModSwitchClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: ModLine line } box) return;
        e.Handled = true;
        ModStack.SetOn(line.Mod, box.IsChecked == true);
        ShowMods();
    }

    /// <summary>
    /// Moves the open mod up or down the list: it then runs earlier or later than the others, and a mod lower on the
    /// list wins where two change the same value. Undo takes it back.
    /// </summary>
    private void MoveOpenMod(int by)
    {
        if (ModStack.Move(Model.OpenMod, by)) ShowMods();
    }

    /// <summary>A click on a mod's letter opens the mod, and that part of the Armory's preset.</summary>
    private void ModLetterClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModLetter letter }) return;
        e.Handled = true;
        Model.OpenModNamed(letter.Mod.Name);
        Model.OpenArmoryPart(letter.Part);
    }

    /// <summary>A mod file dragged over the list can be dropped there.</summary>
    private void ModFilesDraggedOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedModFiles(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Mod files (and backups) dropped on the list are imported, as with "Import mod...".</summary>
    private async void ModFilesDropped(object sender, DragEventArgs e)
    {
        List<string> files = DroppedModFiles(e);
        e.Handled = true;
        if (files.Count > 0) await ImportModFiles(files);
    }

    /// <summary>The .json files being dragged or dropped.</summary>
    private static List<string> DroppedModFiles(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] paths
            ? [.. paths.Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))]
            : [];

    private async void NewModClicked(object sender, RoutedEventArgs e)
    {
        string name = await Messages.AskName(Texts.Get("Rules.Mod.NewName"));
        if (name == "") return;
        if (RulesModel.ModExists(name)) { await Messages.Tell(Texts.Get("Rules.Mod.Exists")); return; }
        Model.NewMod(name);
    }

    private async void RenameModClicked(object sender, RoutedEventArgs e)
    {
        string old = Model.OpenMod.Name, name = await Messages.AskName(Texts.Get("Rules.Mod.Rename"), old);
        if (name == "" || name == old) return;
        if (RulesModel.ModExists(name)) { await Messages.Tell(Texts.Get("Rules.Mod.Exists")); return; }
        Model.RenameMod(name);
    }

    private void CopyModClicked(object sender, RoutedEventArgs e) => Model.CopyMod();

    /// <summary>"Watermark...": the open mod's watermark comment.</summary>
    private void WatermarkClicked(object sender, RoutedEventArgs e) => _ = MainView.Of(this).Watermark.Edit(Model.OpenMod);

    private async void DeleteModClicked(object sender, RoutedEventArgs e)
    {
        int presets = Model.OpenMod.Presets.Count;
        string question = presets > 0
            ? Texts.Get("Rules.Mod.DeleteAskWithPresets", ("mod", Model.OpenMod.Name), ("presets", RulesModel.Count(presets)))
            : Texts.Get("Rules.Mod.DeleteAsk", ("mod", Model.OpenMod.Name));
        if (await Messages.Confirm(question, Texts.Get("Common.Ok"))) Model.DeleteMod();
    }

    /// <summary>"Import mod...": adds the mods in mod files or backups; a mod of a name already here asks what to do.</summary>
    private async void ImportModClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Texts.Get("Rules.Mod.ImportTitle"), Filter = Texts.Get("Settings.ModFiles"), Multiselect = true };
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
                    Texts.Get("Rules.Mod.ImportExists", ("mod", mod.Name), ("existing", RulesModel.Count(existing.Presets.Count)), ("count", mod.Presets.Count)),
                    new DialogOptions { Ok = Texts.Get("Files.Duplicate.Replace"), Alternative = Texts.Get("Files.Duplicate.KeepBoth"), Cancel = Texts.Get("Files.Duplicate.Skip") });
                choices[mod] = answer.Choice switch { DialogChoice.Ok => ImportChoice.Replace, DialogChoice.Alternative => ImportChoice.KeepBoth, _ => ImportChoice.Skip };
            }
            ImportResult result = AppData.Current.ModStore.Import(incoming, (_, mod) => choices[mod]);
            // the files the mods' File rules carry go into the app's store, where their rules find them
            foreach (Mod mod in incoming.Where(AppData.Current.Mods.Contains))
            {
                try { AppData.Current.ModFileStore.KeepCarried(mod.CarriedFiles); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { problems.Add(Path.GetFileName(path) + " (" + error.Message + ")"); }
            }
            // each mod comes in switched on or off as it was when exported; on when its file doesn't say
            foreach (Mod mod in incoming.Where(AppData.Current.Mods.Contains)) ModStack.SetOn(mod, mod.WasOn ?? true);
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
        if (added > 0) parts.Add(Texts.Get("Rules.Mod.ImportAdded", ("count", added)));
        if (skipped > 0) parts.Add(Texts.Get("Rules.Mod.ImportSkipped", ("count", skipped)));
        if (parts.Count > 0) Model.Say(Texts.Get("Rules.Mod.Imported", ("parts", string.Join(", ", parts))));
        if (problems.Count > 0) await Messages.Tell(Texts.Get("Rules.Mod.ImportFailed", ("problems", string.Join(", ", problems))));
    }

    /// <summary>"Export mod": saves the open mod as a mod file, to share or keep.</summary>
    private void ExportModClicked(object sender, RoutedEventArgs e)
    {
        Mod mod = Model.OpenMod;
        var dialog = new SaveFileDialog { Title = Texts.Get("Rules.ExportMod"), FileName = FileNames.ForName(mod.Name) + ".mod.json", Filter = Texts.Get("Rules.Mod.ExportFilter") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, ModFiles.ExportText(mod, ModStack.IsOn(mod), AppData.Current.ModFileStore));
            Model.Say(Texts.Get("Rules.Mod.Exported", ("mod", mod.Name), ("presets", RulesModel.Count(mod.Presets.Count))));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _ = Messages.Tell(Texts.Get("Rules.Mod.NotSaved", ("reason", error.Message)));
        }
    }

    // ---- presets ----

    private void PresetClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PresetLine line }) return;
        ModifierKeys keys = Keyboard.Modifiers;
        Model.ClickPreset(line.Preset, keys.HasFlag(ModifierKeys.Control), keys.HasFlag(ModifierKeys.Shift));
    }

    /// <summary>A click on a part of the Armory: its preset opens, alone, or the part with no rules when it has no changes.</summary>
    private void ArmoryPartClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ArmoryLine line }) Model.OpenArmoryPart(line.Part);
    }

    private async void NewPresetClicked(object sender, RoutedEventArgs e)
    {
        string name = await Messages.AskName(Texts.Get("Rules.Preset.NewName"));
        if (name == "") return;
        if (Model.PresetExists(name)) { await Messages.Tell(Texts.Get("Rules.Preset.Exists")); return; }
        Model.NewPreset(name);
    }

    private async void RenamePresetClicked(object sender, RoutedEventArgs e)
    {
        // an Armory preset is named after its part of the Armory
        if (Model.OpenPreset is not ModPreset preset || preset.IsArmoryPreset) return;
        string name = await Messages.AskName(Texts.Get("Rules.Preset.Rename"), preset.Name);
        if (name == "" || name == preset.Name) return;
        if (Model.PresetExists(name)) { await Messages.Tell(Texts.Get("Rules.Preset.Exists")); return; }
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
        string question = picked.Count == 1
            ? Texts.Get("Rules.Preset.DeleteAsk", ("preset", picked[0].Name))
            : Texts.Get("Rules.Preset.DeleteAskSeveral", ("count", picked.Count));
        if (used > 0) question += " " + Texts.Count("Rules.Preset.FilesUseIt", used);
        // an Armory preset holds the Armory's changes: deleting it takes them all back
        List<ModPreset> armory = [.. picked.Where(preset => preset.IsArmoryPreset && preset.Rules.Any(ArmoryPresets.IsLocked))];
        if (armory.Count > 0)
        {
            if (!MainView.Of(this).UnitsTab.CanMakeEditable)
            {
                await Messages.Tell(MainView.Of(this).UnitsTab.ArmoryRulesBlockedReason ?? Texts.Get("Rules.Locked.ArmoryNotRead"));
                return false;
            }
            // deleting only the Battlegroups preset empties battlegroups; the divisions stay
            bool onlyBattlegroups = armory.All(preset => preset.ArmoryPart == ArmoryPart.Battlegroups);
            question += " " + Texts.Get(onlyBattlegroups ? "Rules.Locked.BattlegroupsDeleteAsk" : "Rules.Locked.PresetDeleteAsk");
        }
        if (!await Messages.Confirm(question, Texts.Get("Common.Ok"))) return false;
        if (armory.Count > 0) MainView.Of(this).UnitsTab.TakeBack([.. armory.SelectMany(preset => preset.Rules)]);
        Model.DeletePresets(picked);
        return true;
    }

    /// <summary>
    /// "Copy" and Ctrl+C: the selected presets go to the clipboard, as the HTML app copies them, so they can be pasted
    /// there too. Returns whether they were copied.
    /// </summary>
    private bool CopyPresets()
    {
        IReadOnlyList<ModPreset> picked = Model.SelectedPresets;
        if (picked.Count == 0) { Model.Say(Texts.Get("Rules.Preset.SelectToCopy")); return false; }
        // the presets as a mod file writes them, with every condition and action named, each marked as a preset
        var asMod = new Mod { Name = Model.OpenMod.Name, Presets = [.. picked] };
        var presets = (JsonArray)JsonNode.Parse(ModFiles.ExportText(asMod))!["Presets"]!;
        foreach (JsonNode? preset in presets) preset!["DataTypeToString"] = "Preset";
        if (!CopyToClipboard(presets.ToJsonString())) return false;
        Model.Say(Texts.Get("Rules.Copied", ("what", RulesModel.Count(picked.Count))));
        return true;
    }

    /// <summary>"Paste": presets on the clipboard become presets of the open mod; rules go in after the selected rule.</summary>
    private void PasteClicked(object sender, RoutedEventArgs e) => PasteFromClipboard();

    /// <summary>Pastes what is on the clipboard.</summary>
    public void PasteFromClipboard() => Paste(ClipboardText());

    /// <summary>
    /// Puts text on the clipboard; false, and the status says so, when another program has the clipboard open, which
    /// Windows reports as an error.
    /// </summary>
    private bool CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            Model.Say(Texts.Get("Rules.ClipboardBusy"));
            return false;
        }
    }

    /// <summary>The text on the clipboard; empty when it holds none, or another program has it open (the status says so).</summary>
    private string ClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : "";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            Model.Say(Texts.Get("Rules.ClipboardBusy"));
            return "";
        }
    }

    /// <summary>Pastes text from the clipboard; presets it adds may be given to files without one. Returns whether presets were added.</summary>
    public bool Paste(string text)
    {
        bool presetsAdded = Model.PasteText(text);
        if (presetsAdded) AppData.Current.TellModsChanged(new ModsChange(PresetsAdded: true));
        return presetsAdded;
    }

    // ---- rules ----

    /// <summary>The rule clicked last, so a second click on it, as part of a double-click, opens it.</summary>
    private int? lastClickedRule;

    /// <summary>
    /// The line the left button went down on. A click counts only when it is let go on the same line: a press that
    /// closed a window or a menu by landing outside it never reaches the list, so letting go over a line does nothing.
    /// </summary>
    private int? pressedRuleIndex;

    /// <summary>
    /// Whether that press was the second of a double-click, as Windows counts it (by its own double-click time, from
    /// one press to the next): timing the clicks here instead, as they were handled, missed double-clicks while the
    /// app was busy.
    /// </summary>
    private bool pressedTwice;

    private void RulePressed(object sender, MouseButtonEventArgs e)
    {
        pressedRuleIndex = sender is FrameworkElement { DataContext: RuleRow row } ? row.Index : null;
        pressedTwice = e.ClickCount == 2;
    }

    private void RuleClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RuleRow row }) return;
        bool pressedHere = pressedRuleIndex == row.Index;
        pressedRuleIndex = null;
        if (!pressedHere) return;
        ModifierKeys keys = Keyboard.Modifiers;
        bool control = keys.HasFlag(ModifierKeys.Control), shift = keys.HasFlag(ModifierKeys.Shift);
        bool twice = !control && !shift && pressedTwice && lastClickedRule == row.Index;
        lastClickedRule = twice || control || shift ? null : row.Index;
        Model.ClickRule(row.Index, control, shift);
        // the click selects the line, which draws the list's lines anew, the one under the mouse too: WPF is told to
        // look again at what the mouse is over, or the second click of a double-click goes to the line that is gone
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, Mouse.Synchronize);
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
                // the whole group moves, with its rules
                Model.SelectOnly(row.Index);
                Model.MoveSelectedGroups(action == "up" ? -1 : +1);
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
            yield return new MenuChoice(RuleKindNames.Of(kind), () => AddRule(kind), Key: kind == "Edit" ? "Ctrl+N" : "",
                Tip: RuleRow.SummaryOf(kind), Symbol: symbol, SymbolColour: colour);
        }
    }

    /// <summary>
    /// The Units tab's "New Rule for This Unit" and the like: an Edit rule for some descriptors, one condition each
    /// ("Descriptor Title", "Is Equal To"), any of which it matches. It is added below the selected rule, this tab
    /// comes to the front, and the rule opens, to choose what it changes.
    /// </summary>
    /// <param name="name">The rule's name, such as the unit's.</param>
    /// <param name="descriptors">The descriptors' names, such as "Descriptor_Unit_M1A1_Abrams_US".</param>
    public void AddRuleFor(string name, IReadOnlyList<string> descriptors)
    {
        JsonObject rule = RuleLoading.NewRule("Edit");
        rule["Name"] = name;
        var conditions = new JsonArray();
        foreach (string descriptor in descriptors)
        {
            JsonObject condition = RuleLoading.NewCondition();
            condition["Operator"] = "Is Equal To";
            condition["Value"] = descriptor;
            conditions.Add(condition);
        }
        rule["Conditions"] = conditions;
        Model.AddRule(rule);
        MainView view = MainView.Of(this);
        view.Tabs.SelectedItem = view.RulesTabItem;
        OpenRuleWindow();
    }

    /// <summary>Adds a rule of a type below the selected one, and opens it.</summary>
    private void AddRule(string kind)
    {
        Model.NewRule(kind);
        OpenRuleWindow();
    }


    /// <summary>"Copy" and Ctrl+C: the selected rules go to the clipboard, each marked as a rule. Returns whether they were copied.</summary>
    private bool CopyRules()
    {
        IReadOnlyList<int> picked = Model.SelectedRules;
        if (picked.Count == 0) { Model.Say(Texts.Get("Rules.Rule.SelectToCopy")); return false; }
        var rules = new JsonArray();
        foreach (int place in picked)
        {
            var rule = (JsonObject)Model.Rules[place]!.DeepClone();
            rule["DataTypeToString"] = "Rule";
            rules.Add(rule);
        }
        if (!CopyToClipboard(rules.ToJsonString())) return false;
        Model.Say(Texts.Get("Rules.Copied", ("what", RulesModel.CountRules(picked.Count))));
        return true;
    }
}
