using System.Windows;
using EasyNdf.App.Text;

namespace EasyNdf.App.Dialogs;

/// <summary>What the message window offers, as the HTML app's <c>dialog(msg, o)</c> takes it.</summary>
public sealed record DialogOptions
{
    /// <summary>The first button, drawn bold: "OK" unless named.</summary>
    public string Ok { get; init; } = "OK";

    /// <summary>A middle button for a second choice, such as "Keep both"; none when null.</summary>
    public string? Alternative { get; init; }

    /// <summary>The last button: "Cancel" unless named.</summary>
    public string Cancel { get; init; } = Texts.Get("Common.Cancel");

    /// <summary>Whether the window only tells something: then it has an OK button alone.</summary>
    public bool InfoOnly { get; init; }

    /// <summary>A checkbox's text, such as "Do the same for any other duplicates in this batch"; no checkbox when null.</summary>
    public string? CheckText { get; init; }

    /// <summary>A text box under the message, with this text in it, selected (the HTML app's <c>dlgIn</c>); none when null.</summary>
    public string? Input { get; init; }

    /// <summary>Something shown under the message, such as a table comparing two files.</summary>
    public UIElement? Extra { get; init; }

    /// <summary>
    /// Dim, smaller words under the text box that explain the question, so the question itself can stay short; none when
    /// null.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>The text box's grey hint while it is empty, such as "Folder name"; none when null.</summary>
    public string? Placeholder { get; init; }

    /// <summary>Whether the first button stays off until something is typed in the text box.</summary>
    public bool NeedsInput { get; init; }
}

/// <summary>Which button closed the message window. Escape counts as <see cref="Cancel"/>.</summary>
public enum DialogChoice
{
    /// <summary>The first button.</summary>
    Ok,
    /// <summary>The middle button.</summary>
    Alternative,
    /// <summary>The last button, or Escape.</summary>
    Cancel,
}

/// <summary>The answer: the button pressed, and whether the checkbox was ticked.</summary>
/// <param name="Choice">The button pressed.</param>
/// <param name="IsChecked">Whether the checkbox was ticked.</param>
/// <param name="Text">What the text box held, for a question with one.</param>
public sealed record DialogAnswer(DialogChoice Choice, bool IsChecked, string Text = "");
