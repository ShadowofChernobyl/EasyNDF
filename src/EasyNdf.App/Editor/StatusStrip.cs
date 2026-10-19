using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using ICSharpCode.AvalonEdit;
using EasyNdf.App.Text;

namespace EasyNdf.App.Editor;

/// <summary>
/// The thin strip along the bottom of an editor's box, as in Visual Studio: the caret's line and column at the left
/// (after any buttons the editor puts there, <see cref="AddTools"/>), and the text size as a percentage at the right,
/// in a dropdown. The size is the one both editors share
/// (<see cref="TextZoom"/>); the editor calls <see cref="ShowSize"/> whenever its size changes, so the dropdown follows Ctrl and the wheel too.
/// </summary>
public sealed class StatusStrip : Border
{
    private readonly TextEditor editor;
    private readonly DockPanel row = new();
    private readonly TextBlock caretPlace = new() { VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The caret's line and column, and what comes right after them (<see cref="AddAfterCaretPlace"/>), side by side.</summary>
    private readonly StackPanel caretRow = new() { Orientation = Orientation.Horizontal };
    private readonly ComboBox sizeChoice = new() { MinWidth = 72, FontSize = 12, ToolTip = Texts.Get("Editor.TextSize.Tip") };
    private bool showing;

    /// <summary>A size in the dropdown: its pixels, shown as a percentage.</summary>
    private sealed record SizeItem(double Size)
    {
        public override string ToString() => TextZoom.Percent(Size);
    }

    /// <summary>Builds the strip for an editor.</summary>
    public StatusStrip(TextEditor editor)
    {
        this.editor = editor;
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(6, 2, 2, 2);
        SetResourceReference(BorderBrushProperty, "line");
        SetResourceReference(BackgroundProperty, "panel");
        caretPlace.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        caretPlace.FontSize = 12;
        DockPanel.SetDock(sizeChoice, Dock.Right);
        row.Children.Add(sizeChoice);
        caretRow.Children.Add(caretPlace);
        row.Children.Add(caretRow);
        Child = row;

        sizeChoice.SelectionChanged += SizeChosen;
        editor.TextArea.Caret.PositionChanged += (_, _) => ShowCaretPlace();
        ShowSize(editor.FontSize);
        ShowCaretPlace();
    }

    /// <summary>Puts buttons at the strip's left, before the caret's line and column, which move right to make room.</summary>
    public void AddTools(FrameworkElement tools)
    {
        DockPanel.SetDock(tools, Dock.Left);
        tools.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Insert(0, tools);
    }

    /// <summary>Puts something just right of the caret's line and column, such as the File Editor's problems.</summary>
    public void AddAfterCaretPlace(FrameworkElement element)
    {
        element.Margin = new Thickness(12, 0, 0, 0);
        caretRow.Children.Add(element);
    }

    /// <summary>The percentage the dropdown shows.</summary>
    internal string ShownPercent => sizeChoice.SelectedItem?.ToString() ?? "";

    /// <summary>The caret's place as the strip shows it.</summary>
    internal string ShownCaretPlace => caretPlace.Text;

    /// <summary>"Ln 6, Col 14" for where the caret is.</summary>
    private void ShowCaretPlace()
    {
        var caret = editor.TextArea.Caret;
        caretPlace.Text = Texts.Get("Editor.CaretPlace", ("line", caret.Line), ("column", caret.Column));
    }

    /// <summary>Fills the dropdown with the offered sizes, and the size in use among them if it is not one, chosen.</summary>
    public void ShowSize(double size)
    {
        showing = true;
        var sizes = new SortedSet<double>(TextZoom.OfferedSizes.Select(s => (double)s)) { size };
        List<SizeItem> items = [.. sizes.Select(s => new SizeItem(s))];
        sizeChoice.ItemsSource = items;
        sizeChoice.SelectedItem = items.First(i => i.Size == size);
        showing = false;
    }

    private void SizeChosen(object sender, SelectionChangedEventArgs e)
    {
        if (showing || sizeChoice.SelectedItem is not SizeItem chosen) return;
        TextZoom.Set(chosen.Size);
        editor.TextArea.Focus();
    }
}
