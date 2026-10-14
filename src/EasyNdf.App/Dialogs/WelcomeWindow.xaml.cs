using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
    }

    /// <summary>Whether the window is open.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Whether the window has been closed before, in this or an earlier run.</summary>
    public static bool WasSeen => AppData.Current.State["welcomed"] is not null;

    /// <summary>Opens the window, with the keyboard on Get started.</summary>
    public void Open()
    {
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => OkButton.Focus());
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
