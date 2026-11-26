using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Text;
using EasyNdf.Data.Divisions;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// The Armory's tab window. <see cref="Edit"/> changes a tab's label and tooltip; <see cref="New"/> brings in one of
/// the spare tabs left. Each answers the values to keep, or null when closed without keeping them: the Armory saves
/// them in the open mod. A tab's cost rows are set per division in the Divisions view, which the window links to
/// (<see cref="WentToCosts"/>).
/// </summary>
public partial class TabWindow : UserControl
{
    private TaskCompletionSource<ModTab?>? answer;
    private string factory = "";

    /// <summary>Whether the window was left by its link to the Divisions view, its values kept: the caller shows the tab's costs there.</summary>
    public bool WentToCosts { get; private set; }

    /// <summary>Builds the window, closed.</summary>
    public TabWindow()
    {
        InitializeComponent();
        PreviewKeyDown += KeyPressed;
        Backdrop.SizeChanged += (_, _) => DialogFit.Keep(Box, Backdrop, 520);
        foreach (TextBox box in new[] { LabelBox, TipTitleBox, TipTextBox, TipMoreBox }) box.TextChanged += (_, _) => ShowResets();
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Edit Tab: each box shows the mod's text or, where it gives none, the game's own; a text left as the game's is not kept.</summary>
    /// <param name="tab">The tab, with what the mod gives it so far.</param>
    /// <param name="game">The tab's label and tooltip texts without the mod.</param>
    public Task<ModTab?> Edit(ModTab tab, ModTab game)
    {
        Title.Text = Texts.Get("Units.Tabs.Edit.Title");
        DoneButton.Content = Texts.Get("Units.Tabs.Done");
        TabName.Text = "Factory/" + tab.Factory;
        TabName.Visibility = Visibility.Visible;
        TabChoice.Visibility = SpareNote.Visibility = Visibility.Collapsed;
        return Open(tab, game);
    }

    /// <summary>New Tab: one of the spare tabs left is chosen, and given its label and tooltip.</summary>
    /// <param name="spare">The spare tabs left, with the label each starts with.</param>
    /// <param name="gameTextsOf">A spare tab's texts as the game shows them, which its boxes start with.</param>
    public Task<ModTab?> New(IReadOnlyList<(string Factory, string Label)> spare, Func<string, ModTab>? gameTextsOf = null)
    {
        textsOf = gameTextsOf ?? (tab => new ModTab { Factory = tab });
        Title.Text = Texts.Get("Units.Tabs.New.Title");
        DoneButton.Content = Texts.Get("Units.Tabs.Create");
        TabName.Visibility = Visibility.Collapsed;
        TabChoice.Visibility = SpareNote.Visibility = Visibility.Visible;
        choosing = true;
        choices = [.. spare.Select(each => each.Factory)];
        TabChoice.ItemsSource = spare.Select(each => each.Factory + "  (" + each.Label + ")").ToList();
        TabChoice.SelectedIndex = 0;
        choosing = false;
        labels = spare.ToDictionary(each => each.Factory, each => each.Label);
        return Open(new ModTab { Factory = spare[0].Factory, Label = spare[0].Label }, textsOf(spare[0].Factory));
    }

    /// <summary>New Tab: a spare tab's texts as the game shows them.</summary>
    private Func<string, ModTab> textsOf = tab => new ModTab { Factory = tab };

    private bool choosing;
    private List<string> choices = [];
    private Dictionary<string, string> labels = [];

    private ModTab gameTexts = new() { Factory = "" };

    private Task<ModTab?> Open(ModTab tab, ModTab game)
    {
        answer?.TrySetResult(null);
        answer = new TaskCompletionSource<ModTab?>();
        factory = tab.Factory;
        WentToCosts = false;
        TabLabel.Text = Texts.WithColon(Texts.Get("Units.Tabs.Field.Tab"));
        LabelLabel.Text = Texts.WithColon(Texts.Get("Units.Tabs.Field.Label"));
        TitleLabel.Text = Texts.WithColon(Texts.Get("Units.Tabs.Field.TipTitle"));
        TextLabel.Text = Texts.WithColon(Texts.Get("Units.Tabs.Field.TipText"));
        MoreLabel.Text = Texts.WithColon(Texts.Get("Units.Tabs.Field.TipMore"));
        CostsNoteText.Text = Texts.Get("Units.Tabs.Costs.Note") + " ";
        CostsLinkText.Text = Texts.Get("Units.Tabs.Costs.Link");
        gameTexts = game;
        ShowGameTexts();
        LabelBox.Text = tab.Label ?? game.Label ?? "";
        TipTitleBox.Text = tab.TipTitle ?? game.TipTitle ?? "";
        TipTextBox.Text = tab.TipText ?? game.TipText ?? "";
        TipMoreBox.Text = tab.TipMore ?? game.TipMore ?? "";
        // each opening starts with boxes that fit their text, up to a few lines
        foreach (TextBox box in new[] { TipTextBox, TipMoreBox }) { box.Height = double.NaN; box.MaxHeight = 160; }
        ShowResets();
        Visibility = Visibility.Visible;
        // once the window is laid out: a box not yet shown cannot take the keyboard, which would stay with the page behind
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => { Keyboard.Focus(LabelBox); LabelBox.SelectAll(); });
        return answer.Task;
    }

    /// <summary>Another spare tab chosen in New Tab: the label follows it, unless one was typed.</summary>
    private void TabChosen(object sender, SelectionChangedEventArgs e)
    {
        if (choosing || TabChoice.SelectedIndex < 0 || TabChoice.SelectedIndex >= choices.Count) return;
        string chosen = choices[TabChoice.SelectedIndex];
        bool labelWasItsOwn = LabelBox.Text.Trim() == "" || LabelBox.Text == labels.GetValueOrDefault(factory);
        factory = chosen;
        if (labelWasItsOwn) LabelBox.Text = labels.GetValueOrDefault(factory) ?? "";
        // the tooltip boxes follow the tab chosen, unless something else was typed in them
        ModTab next = textsOf(chosen);
        if (TipTitleBox.Text == (gameTexts.TipTitle ?? "")) TipTitleBox.Text = next.TipTitle ?? "";
        if (TipTextBox.Text == (gameTexts.TipText ?? "")) TipTextBox.Text = next.TipText ?? "";
        if (TipMoreBox.Text == (gameTexts.TipMore ?? "")) TipMoreBox.Text = next.TipMore ?? "";
        gameTexts = next;
        ShowGameTexts();
        ShowResets();
    }

    /// <summary>Each box's game text, shown greyed while the box is empty (a text box's hint is its Tag).</summary>
    private void ShowGameTexts()
    {
        LabelBox.Tag = gameTexts.Label ?? "";
        TipTitleBox.Tag = gameTexts.TipTitle ?? "";
        TipTextBox.Tag = gameTexts.TipText ?? "";
        TipMoreBox.Tag = gameTexts.TipMore ?? "";
    }

    /// <summary>The game's text of a box.</summary>
    private string GameTextOf(TextBox box) =>
        (box == LabelBox ? gameTexts.Label : box == TipTitleBox ? gameTexts.TipTitle : box == TipTextBox ? gameTexts.TipText : gameTexts.TipMore) ?? "";

    /// <summary>A box's ↺ shows while its text is not the game's.</summary>
    private void ShowResets()
    {
        foreach ((TextBox box, Button reset) in new[] { (LabelBox, LabelReset), (TipTitleBox, TitleReset), (TipTextBox, TextReset), (TipMoreBox, MoreReset) })
            reset.Visibility = box.Text.Trim() != GameTextOf(box).Trim() ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>↺: the box gets the game's text back.</summary>
    private void ResetClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TextBox box) return;
        box.Text = GameTextOf(box);
        box.Focus();
        box.CaretIndex = box.Text.Length;
    }

    /// <summary>The link to the Divisions view: the values are kept, as with Done, and the caller shows the tab's costs.</summary>
    private void CostsLinkClicked(object sender, RoutedEventArgs e)
    {
        WentToCosts = true;
        Close(Typed());
    }

    /// <summary>The values typed, an empty box keeping the game's.</summary>
    private ModTab Typed()
    {
        // a box left empty, or left as the game's own text, keeps the game's
        static string? Given(TextBox box, string? game) => box.Text.Trim() is { Length: > 0 } text && text != game?.Trim() ? text : null;
        return new ModTab { Factory = factory, Label = Given(LabelBox, gameTexts.Label), TipTitle = Given(TipTitleBox, gameTexts.TipTitle), TipText = Given(TipTextBox, gameTexts.TipText), TipMore = Given(TipMoreBox, gameTexts.TipMore) };
    }

    private void DoneClicked(object sender, RoutedEventArgs e) => Close(Typed());

    /// <summary>The grip under a tooltip box: dragging it sets the box's height, from two lines up to most of the window.</summary>
    private void GripDragged(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TextBox box) return;
        box.MaxHeight = double.PositiveInfinity;
        // by how far the mouse went over the app, which stays put: the window is centred, so the grip itself moves as
        // the box grows, and its own count of the drag made the box grow faster than the mouse
        box.Height = Math.Clamp(gripStartHeight + Mouse.GetPosition(Backdrop).Y - gripStartY, 44, Math.Max(44, Backdrop.ActualHeight * 0.6));
    }

    /// <summary>Where the mouse was, and how tall the box, when a grip was taken.</summary>
    private double gripStartY, gripStartHeight;

    private void GripGrabbed(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TextBox box) return;
        gripStartY = Mouse.GetPosition(Backdrop).Y;
        gripStartHeight = box.ActualHeight;
    }

    private void CancelClicked(object sender, RoutedEventArgs e) => Close(null);

    private void BackdropPressed(object sender, MouseButtonEventArgs e) => Close(null);

    private void BoxPressed(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (!IsOpen) return;
        if (e.Key == Key.Escape) { e.Handled = true; Close(null); }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; DoneClicked(this, e); }
    }

    private void Close(ModTab? kept)
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        answer?.TrySetResult(kept);
    }
}
