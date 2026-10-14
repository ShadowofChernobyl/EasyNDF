using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EasyNdf.App.Dialogs;

/// <summary>
/// The message window: asks a question and gives back the answer (see <see cref="Ask"/>), or tells something (see
/// <see cref="Tell"/>). One question is open at a time.
/// </summary>
public partial class MessageOverlay : UserControl
{
    private TaskCompletionSource<DialogAnswer>? answer;

    /// <summary>Makes the window, hidden until it is asked something.</summary>
    public MessageOverlay()
    {
        InitializeComponent();
        // 36rem wide, or 92% of the app's width when the app is narrower than that.
        SizeChanged += (_, _) => Box.Width = Math.Min(576, ActualWidth * 0.92);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(DialogChoice.Cancel); }
            // Enter in the text box answers OK
            else if (e.Key == Key.Enter && Input.IsKeyboardFocused) { e.Handled = true; Close(DialogChoice.Ok); }
        };
    }

    /// <summary>Whether a question is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>
    /// Shows a question, and gives back which button answered it. The first button has the keyboard focus, so Enter
    /// presses it and Escape cancels.
    /// </summary>
    public Task<DialogAnswer> Ask(string message, DialogOptions options)
    {
        Message.Text = message;
        Extra.Content = options.Extra;
        Extra.Visibility = options.Extra is null ? Visibility.Collapsed : Visibility.Visible;
        Input.Visibility = options.Input is null ? Visibility.Collapsed : Visibility.Visible;
        Input.Text = options.Input ?? "";
        Check.Content = options.CheckText;
        Check.IsChecked = false;
        Check.Visibility = options.CheckText is null ? Visibility.Collapsed : Visibility.Visible;
        OkButton.Content = options.Ok;
        AlternativeButton.Content = options.Alternative;
        AlternativeButton.Visibility = options.Alternative is null ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Content = options.Cancel;
        CancelButton.Visibility = options.InfoOnly ? Visibility.Collapsed : Visibility.Visible;

        answer = new TaskCompletionSource<DialogAnswer>();
        Visibility = Visibility.Visible;
        // the text box has the focus, its text selected, when there is one; else the first button
        Dispatcher.BeginInvoke(() =>
        {
            if (options.Input is null) { OkButton.Focus(); return; }
            Input.Focus();
            Input.SelectAll();
        });
        return answer.Task;
    }

    /// <summary>Asks a yes-or-no question: whether the first button was pressed.</summary>
    public async Task<bool> Confirm(string message, string okText)
    {
        DialogAnswer reply = await Ask(message, new DialogOptions { Ok = okText });
        return reply.Choice == DialogChoice.Ok;
    }

    /// <summary>
    /// Asks for a name (the HTML app's <c>askName</c>): the text typed, without spaces around it or dots and spaces at its
    /// end; "" when cancelled.
    /// </summary>
    public async Task<string> AskName(string message, string start = "")
    {
        DialogAnswer reply = await Ask(message, new DialogOptions { Input = start });
        return reply.Choice == DialogChoice.Ok ? reply.Text.Trim().TrimEnd(' ', '.') : "";
    }

    /// <summary>Tells something, with an OK button alone, and waits until it is closed.</summary>
    public Task Tell(string message) => Ask(message, new DialogOptions { InfoOnly = true });

    /// <summary>Closes the window with the given answer.</summary>
    public void Close(DialogChoice choice)
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        Extra.Content = null;
        TaskCompletionSource<DialogAnswer>? waiting = answer;
        answer = null;
        waiting?.SetResult(new DialogAnswer(choice, Check.IsChecked == true, Input.Text));
    }

    private void OkClicked(object sender, RoutedEventArgs e) => Close(DialogChoice.Ok);

    private void AlternativeClicked(object sender, RoutedEventArgs e) => Close(DialogChoice.Alternative);

    private void CancelClicked(object sender, RoutedEventArgs e) => Close(DialogChoice.Cancel);
}
