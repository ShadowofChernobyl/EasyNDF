using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// The welcome window (the HTML app's <c>showWelcome</c> and <c>closeWelcome</c>). Once it is closed, the app
/// remembers that it was seen (state "welcomed"), so it is not shown again at the next start.
/// </summary>
public partial class WelcomeWindow : UserControl
{
    /// <summary>Builds the window, closed.</summary>
    public WelcomeWindow()
    {
        InitializeComponent();
        // narrower and no taller than a small app window, its buttons always showing
        Backdrop.SizeChanged += (_, _) => DialogFit.Keep(Box, Backdrop, 640);
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Whether the window has been closed before, in this or an earlier run.</summary>
    public static bool WasSeen => AppData.Current.State["welcomed"] is not null;

    /// <summary>Opens the window, with the keyboard on Get started.</summary>
    public void Open()
    {
        FillChoices();
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => OkButton.Focus());
        // once laid out, where the notes and tabs fall is known
        Dispatcher.BeginInvoke(DrawWires, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Closes the window, and remembers which version showed it (as the HTML app keeps its VERSION).</summary>
    private void Close()
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        AppData.Current.State["welcomed"] = MainView.AppVersion;
        AppData.Current.SaveState();
    }

    private void OkClicked(object sender, RoutedEventArgs e) => Close();

    // ---- the language and the theme ----

    /// <summary>Set while the dropdowns are filled, so filling them changes nothing.</summary>
    private bool filling;

    /// <summary>The dropdowns as the Settings tab has them: English and every language the app has, and the themes.</summary>
    private void FillChoices()
    {
        filling = true;
        var languages = new List<Rules.ChoiceOption> { new(Data.Languages.LanguagePack.EnglishCode, Text.Texts.Get("Settings.LanguageChoice.English")) };
        foreach (Data.Languages.LanguagePack pack in Text.Texts.Languages().OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            languages.Add(new Rules.ChoiceOption(pack.Code, pack.Name,
                Text.Texts.Get("Settings.LanguageChoice.Progress", ("percent", pack.ProgressAgainst(Text.Texts.Catalog.English).Percent))));
        LanguageChoice.SetResourceReference(ItemsControl.ItemTemplateProperty, "DimmedChoice");
        LanguageChoice.ItemsSource = languages;
        LanguageChoice.SelectedItem = languages.FirstOrDefault(o => o.Value == Text.Texts.Catalog.LanguageCode) ?? languages[0];
        Pages.ThemeDesigner.FillThemeChoice(ThemeChoice);
        filling = false;
    }

    /// <summary>A language picked: the app uses it at once, this window too, and keeps it.</summary>
    private void LanguageChosen(object sender, SelectionChangedEventArgs e)
    {
        if (filling || LanguageChoice.SelectedItem is not Rules.ChoiceOption { Value: string code }) return;
        AppData.Current.Settings["lang"] = code;
        AppData.Current.SaveSettings();
        Text.Texts.Use(code);
        MainView.Find(this)?.SettingsChanged("lang");
        // the names in the dropdowns, and where the notes' lines run, change with the words
        Dispatcher.BeginInvoke(FillChoices, System.Windows.Threading.DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(DrawWires, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>A theme picked: the app wears it at once, this window too, and keeps it.</summary>
    private void ThemeChosen(object sender, SelectionChangedEventArgs e)
    {
        if (filling || ThemeChoice.SelectedItem is not Rules.ChoiceOption { Value: string id }) return;
        AppData.Current.Settings["theme"] = id;
        AppData.Current.SaveSettings();
        Theming.ThemeManager.ApplySaved();
        MainView.Find(this)?.SettingsChanged("theme");
        Dispatcher.BeginInvoke(DrawWires, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>"Start the Tutorial": the window closes and the tour starts.</summary>
    /// <summary>Tutorials ▾: the tutorials, each with what it covers; one chosen starts as the window closes.</summary>
    /// <summary>
    /// Start the Tutorial: from its first chapter (the Help tab's Tutorials starts any chapter).
    /// </summary>
    private void TourClicked(object sender, RoutedEventArgs e)
    {
        Close();
        MainView.Find(this)?.Tour.Start(Chapter.Files);
    }

    private void MapResized(object sender, SizeChangedEventArgs e) => DrawWires();

    /// <summary>
    /// The lines from each note to what it is about: down from a note above, across at a height of its own, and down to
    /// the middle of its tab. The notes keep the tabs' order, so
    /// lines turning further left run higher and none cross.
    /// </summary>
    private void DrawWires()
    {
        Wires.Children.Clear();
        if (!IsOpen || Map.ActualWidth <= 0) return;
        Brush ink = TryFindResource("muted") as Brush ?? Brushes.Gray;
        Point At(FrameworkElement element, double x, double y) => element.TransformToAncestor(Map).Transform(new Point(x * element.ActualWidth, y * element.ActualHeight));
        void Line(params Point[] points)
        {
            var line = new Polyline { Stroke = ink, StrokeThickness = 1, SnapsToDevicePixels = true, Points = new PointCollection(points) };
            Wires.Children.Add(line);
        }
        (FrameworkElement Note, FrameworkElement Tab)[] above = [(FilesNote, FilesTab), (ModsNote, ModsTab), (EditorNote, EditorTab), (ArmoryNote, ArmoryTab)];
        double notesBottom = above.Max(pair => At(pair.Note, 0.5, 1).Y), tabsTop = At(FilesTab, 0.5, 0).Y;
        for (int index = 0; index < above.Length; index++)
        {
            (FrameworkElement note, FrameworkElement tab) = above[index];
            Point from = At(note, 0.5, 1), to = At(tab, 0.5, 0);
            // left-going lines: the first runs highest; right-going ones the other way round
            int lane = from.X >= to.X ? index : above.Length - 1 - index;
            double y = Math.Round(notesBottom + (tabsTop - notesBottom) * (lane + 1) / (above.Length + 1)) + 0.5;
            Line(new Point(from.X, from.Y), new Point(from.X, y), new Point(to.X, y), new Point(to.X, to.Y));
        }
    }

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
