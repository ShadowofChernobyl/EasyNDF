using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Divisions;

/// <summary>
/// A section of the Division pane, or the Unit pane, that folds: a header the whole width of the pane (▾ or ▸, its name in the accent
/// colour, and, folded, a line saying what is inside), then its contents. A click anywhere on the header folds or
/// opens it, and the choice is kept for next time, for every division (or unit).
/// </summary>
public sealed class FoldSection : StackPanel
{
    private readonly string key;
    private readonly string summary;
    private readonly bool foldedAtFirst;
    private readonly TextBlock arrow = new() { Width = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock summaryText = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border body;

    /// <summary>Builds a section.</summary>
    /// <param name="key">The name its folding is kept under, such as "standout".</param>
    /// <param name="title">The header's name.</param>
    /// <param name="summary">What the header says while folded, such as "3 units"; empty for nothing.</param>
    /// <param name="contents">What the section holds.</param>
    /// <param name="pane">The pane it is in, which its folding is kept under: "divisionPane" or "unitPane".</param>
    /// <param name="foldedAtFirst">Whether it is folded until the user opens it.</param>
    public FoldSection(string key, string title, string summary, UIElement contents, string pane = "divisionPane", bool foldedAtFirst = false)
    {
        this.key = pane + "." + key;
        this.foldedAtFirst = foldedAtFirst;
        this.summary = summary;
        arrow.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        summaryText.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var name = new TextBlock { Text = title, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        DockPanel.SetDock(arrow, Dock.Left);
        DockPanel.SetDock(name, Dock.Left);
        summaryText.Margin = new Thickness(10, 0, 0, 0);
        summaryText.HorizontalAlignment = HorizontalAlignment.Right;
        var header = new Button
        {
            Style = (Style)Application.Current.FindResource("SectionHeader"),
            Content = new DockPanel { Children = { arrow, name, summaryText } },
            ToolTip = Text.Texts.Get("Divisions.Pane.Fold.Tip"),
        };
        header.Click += (_, _) => Fold(!IsFolded);
        // 10 pixels in from the pane's sides, as the header's own text
        body = new Border { Child = contents, Padding = new Thickness(10, 8, 10, 10) };
        Children.Add(header);
        Children.Add(body);
        Show();
    }

    /// <summary>Whether the section is folded, as last chosen.</summary>
    public bool IsFolded => AppData.Current.State["folds"]?[key]?.GetValue<bool>() ?? foldedAtFirst;

    /// <summary>Opens the section when it is folded, as a click on its header does, and shows it in its pane.</summary>
    public void Open()
    {
        if (IsFolded) Fold(false);
        BringIntoView();
    }

    private void Fold(bool folded)
    {
        if (AppData.Current.State["folds"] is not JsonObject folds) AppData.Current.State["folds"] = folds = [];
        folds[key] = folded;
        AppData.Current.SaveState();
        Show();
    }

    /// <summary>The arrow, the summary and the contents as the section is now.</summary>
    private void Show()
    {
        bool folded = IsFolded;
        arrow.Text = folded ? "▸" : "▾";
        summaryText.Text = folded ? summary : "";
        body.Visibility = folded ? Visibility.Collapsed : Visibility.Visible;
    }
}
