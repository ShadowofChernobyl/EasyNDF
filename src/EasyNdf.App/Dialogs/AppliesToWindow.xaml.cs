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

    /// <summary>Builds the window, closed.</summary>
    public AppliesToWindow()
    {
        InitializeComponent();
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

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
        FileTicks.Children.Clear();
        foreach (string file in Choices(forRuleSet, files)) AddTick(file, IsTicked(forRuleSet, file, files));
        TypedFile.Text = "";
        ShowWhatIsUsed();
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => ByNameSwitch.Focus());
    }

    /// <summary>
    /// The files to choose from, in name order: those on the Files tab, each by its name (with its nearest folder when
    /// another listed file has the same name, so the two can be told apart), and those the rule set lists already.
    /// </summary>
    private static List<string> Choices(ModPreset ruleSet, IReadOnlyList<FileEntry> files)
    {
        var choices = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FileEntry file in files) choices.Add(ChoiceFor(file, files));
        foreach (string listed in ruleSet.AppliesTo ?? []) choices.Add(listed);
        return [.. choices];
    }

    /// <summary>How a file on the Files tab is listed: "Ammunition.ndf", or "Strategic/GDConstants.ndf" when two listed files share the name.</summary>
    private static string ChoiceFor(FileEntry file, IReadOnlyList<FileEntry> files)
    {
        bool nameIsShared = files.Any(other => other.Name == file.Name && other.Folder != file.Folder);
        string nearestFolder = file.Folder[(file.Folder.LastIndexOf('/') + 1)..];
        return nameIsShared && nearestFolder != "" ? nearestFolder + "/" + file.Name : file.Name;
    }

    /// <summary>Whether a choice starts ticked: the rule set lists it, or applies to it now by its name.</summary>
    private static bool IsTicked(ModPreset ruleSet, string choice, IReadOnlyList<FileEntry> files)
    {
        if (ruleSet.AppliesTo is List<string> listed) return listed.Contains(choice, StringComparer.Ordinal);
        var byName = new ModPreset { Name = ruleSet.Name };
        return files.Any(file => ChoiceFor(file, files) == choice
            && RuleSetFiles.For(file.Name, file.Folder, [byName], byName: true).Count > 0);
    }

    private void AddTick(string file, bool ticked)
    {
        var tick = new CheckBox { Style = (Style)FindResource("Tick"), Content = file, IsChecked = ticked, Margin = new Thickness(0, 2, 0, 2) };
        tick.SetResourceReference(FontFamilyProperty, "mono");
        FileTicks.Children.Add(tick);
    }

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

    /// <summary>"Add": a file typed in, ticked, such as one not on the Files tab yet.</summary>
    private void AddClicked(object sender, RoutedEventArgs e)
    {
        string file = TypedFile.Text.Trim().Replace('\\', '/').Trim('/');
        if (file == "") return;
        CheckBox? listed = FileTicks.Children.OfType<CheckBox>().FirstOrDefault(tick => (string)tick.Content == file);
        if (listed is not null) listed.IsChecked = true;
        else AddTick(file, ticked: true);
        TypedFile.Text = "";
    }

    private void TypedFileKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddClicked(sender, e);
    }

    private void DoneClicked(object sender, RoutedEventArgs e)
    {
        List<string>? files = ByNameSwitch.IsChecked == true
            ? null
            : [.. FileTicks.Children.OfType<CheckBox>().Where(tick => tick.IsChecked == true).Select(tick => (string)tick.Content)];
        Action<List<string>?>? whenDone = done;
        Close();
        whenDone?.Invoke(files);
    }

    private void Close()
    {
        Visibility = Visibility.Collapsed;
        done = null;
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    private void BackdropPressed(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Backdrop)) Close();
    }

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
