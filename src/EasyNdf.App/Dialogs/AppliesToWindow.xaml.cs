using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Text;
using EasyNdf.Data;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// The Applies To window, opened from the line under a rule set's rules ("Change…") or its right-click menu: whether the
/// rule set applies to the files its name holds, or to the files ticked. Done hands back the choice; Cancel, ×, Escape
/// or a click outside change nothing.
/// </summary>
public partial class AppliesToWindow : UserControl
{
    /// <summary>What Done does with the choice: the files ticked, or null for the files the name holds.</summary>
    private Action<List<string>?>? done;

    /// <summary>Every file the list can show, in name order, each with whether it is ticked; the search shows some of them.</summary>
    private List<FileChoice> choices = [];

    /// <summary>The window's widest, as it was made; less in a narrow app window.</summary>
    private const double Widest = 560;

    /// <summary>Builds the window, closed.</summary>
    public AppliesToWindow()
    {
        InitializeComponent();
        // the window stays inside a small app window, 8 pixels in from each edge: the list gives up its height first
        Backdrop.SizeChanged += (_, _) => DialogFit.Keep(Box, Backdrop, Widest);
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>A file in the list: its name as listed, and whether it is ticked.</summary>
    public sealed class FileChoice(string name, bool isTicked) : INotifyPropertyChanged
    {
        /// <summary>The file's name, with its nearest folder when two listed files share the name.</summary>
        public string Name { get; } = name;

        /// <summary>Whether the file is ticked: the rule set applies to it when "From its name" is off.</summary>
        public bool IsTicked
        {
            get => isTicked;
            set
            {
                if (value == isTicked) return;
                isTicked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTicked)));
            }
        }
        private bool isTicked = isTicked;

        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// Opens the window for a rule set. The list holds the files on the Files tab and those the rule set lists, each
    /// ticked when the rule set applies to it now.
    /// </summary>
    /// <param name="forRuleSet">The rule set.</param>
    /// <param name="files">The files on the Files tab.</param>
    /// <param name="whenDone">What Done does with the choice: the files ticked, or null for the files the name holds.</param>
    public void Open(ModPreset forRuleSet, IReadOnlyList<FileEntry> files, Action<List<string>?> whenDone)
    {
        done = whenDone;
        Title.Text = Texts.Get("Rules.AppliesTo.Title", ("ruleSet", forRuleSet.Name));
        ByNameSwitch.IsChecked = forRuleSet.AppliesTo is null;
        IReadOnlyList<string> named = RuleSetFiles.Named(new ModPreset { Name = forRuleSet.Name });
        ByNameNote.Text = named.Count == 0
            ? Texts.Get("Rules.AppliesTo.NameHoldsNone")
            : Texts.Get("Rules.AppliesTo.NameHolds", ("files", string.Join(", ", named)));
        choices = Choices(forRuleSet, files);
        TypedFile.Text = "";
        Search.Text = "";
        ShowChoices();
        ShowWhatIsUsed();
        Visibility = Visibility.Visible;
        DialogFit.Keep(Box, Backdrop, Widest);
        Dispatcher.BeginInvoke(() => ByNameSwitch.Focus());
    }

    /// <summary>
    /// The files to choose from, in name order, each ticked when the rule set applies to it now: those on the Files tab,
    /// each by its name (with its nearest folder when another listed file has the same name, so the two can be told
    /// apart), and those the rule set lists already. Each file is looked at once: comparing every file with every other
    /// one took seconds with thousands of files.
    /// </summary>
    private static List<FileChoice> Choices(ModPreset ruleSet, IReadOnlyList<FileEntry> files)
    {
        // the folders each file name is found in: a name in more than one is shared
        var foldersOfName = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (FileEntry file in files)
        {
            if (!foldersOfName.TryGetValue(file.Name, out HashSet<string>? folders)) foldersOfName[file.Name] = folders = new(StringComparer.Ordinal);
            folders.Add(file.Folder);
        }
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var ticked = new HashSet<string>(StringComparer.Ordinal);
        var byName = new ModPreset { Name = ruleSet.Name };
        foreach (FileEntry file in files)
        {
            string choice = ChoiceFor(file, foldersOfName[file.Name].Count > 1);
            names.Add(choice);
            if (ruleSet.AppliesTo is null && RuleSetFiles.For(file.Name, file.Folder, [byName], byName: true).Count > 0) ticked.Add(choice);
        }
        foreach (string listed in ruleSet.AppliesTo ?? [])
        {
            names.Add(listed);
            ticked.Add(listed);
        }
        return [.. names.Select(name => new FileChoice(name, ticked.Contains(name)))];
    }

    /// <summary>How a file on the Files tab is listed: "Ammunition.ndf", or "Strategic/GDConstants.ndf" when two listed files share the name.</summary>
    private static string ChoiceFor(FileEntry file, bool nameIsShared)
    {
        string nearestFolder = file.Folder[(file.Folder.LastIndexOf('/') + 1)..];
        return nameIsShared && nearestFolder != "" ? nearestFolder + "/" + file.Name : file.Name;
    }

    /// <summary>The files the search finds (all of them with no search), or what the list says when it has none.</summary>
    private void ShowChoices()
    {
        string search = Search.Text.Trim();
        List<FileChoice> shown = search == "" ? choices : [.. choices.Where(choice => choice.Name.Contains(search, StringComparison.OrdinalIgnoreCase))];
        FileTicks.ItemsSource = shown;
        NoFiles.Text = choices.Count == 0 ? Texts.Get("Rules.AppliesTo.NoFiles") : Texts.Get("Rules.AppliesTo.NoMatch");
        NoFiles.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) => ShowChoices();

    /// <summary>With "From its name" on, the list is only a reminder of what the name gives: it can't be changed.</summary>
    private void ShowWhatIsUsed()
    {
        bool byName = ByNameSwitch.IsChecked == true;
        FileTicks.IsEnabled = !byName;
        FileTicks.Opacity = byName ? 0.5 : 1;
        TypedFile.IsEnabled = !byName;
        AddButton.IsEnabled = !byName;
    }

    private void ByNameClicked(object sender, RoutedEventArgs e) => ShowWhatIsUsed();

    /// <summary>"Add": a file typed in, ticked, such as one not on the Files tab yet; the search goes, so it shows.</summary>
    private void AddClicked(object sender, RoutedEventArgs e)
    {
        string file = TypedFile.Text.Trim().Replace('\\', '/').Trim('/');
        if (file == "") return;
        if (choices.FirstOrDefault(choice => choice.Name == file) is FileChoice listed) listed.IsTicked = true;
        else
        {
            choices.Add(new FileChoice(file, isTicked: true));
            choices.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        }
        TypedFile.Text = "";
        Search.Text = "";
        ShowChoices();
    }

    private void TypedFileKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddClicked(sender, e);
    }

    private void DoneClicked(object sender, RoutedEventArgs e)
    {
        List<string>? files = ByNameSwitch.IsChecked == true ? null : [.. choices.Where(choice => choice.IsTicked).Select(choice => choice.Name)];
        Action<List<string>?>? whenDone = done;
        Close();
        whenDone?.Invoke(files);
    }

    private void Close()
    {
        Visibility = Visibility.Collapsed;
        done = null;
        // the list's lines go with the window: thousands of them need not be kept while it is closed
        FileTicks.ItemsSource = null;
        choices = [];
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    /// <summary>Escape empties the search first, as the Settings and Help searches do; then it closes the window.</summary>
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (Search.IsKeyboardFocused && Search.Text != "") Search.Text = "";
        else Close();
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop)) Close();
    }

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
