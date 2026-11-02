using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.Divisions;

namespace EasyNdf.App.Divisions;

/// <summary>
/// A division in the Divisions tab's list: its emblem, small, its name, and how many units it has at the right, dim;
/// a pencil when the open mod changes it; a gear before the name when the mod generates it; dim and struck through when
/// the mod takes it out of the game's list. Under
/// the mouse the name turns the accent colour; chosen, the row is on the selection colour.
/// </summary>
public sealed class DivisionListRow : Bevel
{
    /// <summary>How large the emblem before the name is drawn: as high as the line.</summary>
    private const double EmblemSize = 16;

    private readonly TextBlock name;

    /// <summary>The division shown.</summary>
    public Division Division { get; }

    /// <summary>Whether the open mod changes this game division, which the pencil shows.</summary>
    public bool IsChanged { get; }

    /// <summary>Whether the open mod takes this game division out of the game's list, which the row shows dim and struck through.</summary>
    public bool IsRemoved { get; }

    /// <summary>Raised when the row is clicked.</summary>
    public event Action<Division>? Chosen;

    /// <summary>Builds the row for a division.</summary>
    /// <param name="division">The division.</param>
    /// <param name="isChosen">Whether it is the division shown on the right.</param>
    /// <param name="isChanged">Whether the open mod changes this game division.</param>
    /// <param name="isRemoved">Whether the open mod takes this game division out of the game's list.</param>
    /// <param name="isGenerated">Whether it is one of the open mod's generated divisions, made at each export.</param>
    public DivisionListRow(Division division, bool isChosen, bool isChanged, bool isRemoved, bool isGenerated = false)
    {
        Division = division;
        IsChanged = isChanged;
        IsRemoved = isRemoved;
        Padding = new Thickness(10, 1, 6, 1);
        Cursor = Cursors.Hand;
        name = new TextBlock { Text = division.Name, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = division.Name };
        var count = new TextBlock { Text = Texts.Number(division.Units.Count), Margin = new Thickness(8, 0, 0, 0), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        count.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        DockPanel.SetDock(count, Dock.Right);
        var emblem = new DivisionEmblem(division.EmblemSource, EmblemSize, keepsRoom: true, division.EmblemFile) { Margin = new Thickness(0, 0, 5, 0) };
        DockPanel.SetDock(emblem, Dock.Left);
        var panel = new DockPanel { Children = { count } };
        if (isChanged)
        {
            var pencil = new TextBlock { Text = "✎", Margin = new Thickness(6, 0, 0, 0), ToolTip = Texts.Get("Divisions.Changed.Tip") };
            pencil.SetResourceReference(TextBlock.ForegroundProperty, "accent");
            DockPanel.SetDock(pencil, Dock.Right);
            panel.Children.Add(pencil);
        }
        panel.Children.Add(emblem);
        if (isGenerated)
        {
            Viewbox gear = Controls.GeneratorIcon.Make(12, "accent");
            gear.Margin = new Thickness(0, 0, 5, 0);
            gear.ToolTip = Texts.Get("Divisions.Generated.Tip");
            DockPanel.SetDock(gear, Dock.Left);
            panel.Children.Add(gear);
        }
        panel.Children.Add(name);
        Child = panel;
        if (isRemoved)
        {
            name.TextDecorations = TextDecorations.Strikethrough;
            name.ToolTip = Texts.Get("Divisions.Removed.Tip", ("division", division.Name));
            panel.Opacity = 0.6;
        }
        ShowChosen(isChosen);
        MouseEnter += (_, _) => ShowState();
        MouseLeave += (_, _) => ShowState();
        MouseLeftButtonUp += (_, _) => Chosen?.Invoke(division);
        // Tab and the arrows reach the divisions, Enter or Space chooses one, as a click does
        Units.KeyPick.Make(this, () => Chosen?.Invoke(division), "division:" + division.Descriptor);
        System.Windows.Automation.AutomationProperties.SetName(this, division.Name);
    }

    /// <summary>Whether it is the division shown on the right.</summary>
    public bool IsChosen { get; private set; }

    /// <summary>
    /// Shows the row chosen or not, in place: choosing another division changes two rows, so the list is not made
    /// again, which was slow with every division of a game listed.
    /// </summary>
    public void ShowChosen(bool isChosen)
    {
        IsChosen = isChosen;
        if (isChosen) SetResourceReference(BackgroundProperty, "sel");
        else Background = System.Windows.Media.Brushes.Transparent;
        ShowState();
    }

    /// <summary>The name in the accent colour when chosen or under the mouse.</summary>
    private void ShowState() =>
        name.SetResourceReference(TextBlock.ForegroundProperty, IsChosen || IsMouseOver ? "accent" : "ink");
}
