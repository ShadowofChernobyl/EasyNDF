using System.Windows;
using EasyNdf.App.Windowing;

namespace EasyNdf.App;

/// <summary>The main window, holding the <see cref="MainView"/>.</summary>
public partial class MainWindow : Window
{
    /// <summary>Opens the window.</summary>
    /// <param name="frame">
    /// For testing on a PC that draws the window oddly: "native" gives the window Windows' own title bar instead of the
    /// app's.
    /// </param>
    public MainWindow(string frame = "")
    {
        InitializeComponent();
        StateChanged += (_, _) => View.ShowWindowState(WindowState);
        // Every key not taken by the control that has the focus ends up here, even when nothing has the focus.
        KeyDown += (_, e) => View.HandleShortcut(e);
        // the file list is kept for next time, and comes back as the window opens ("Reopen the last files")
        Closing += (_, e) =>
        {
            if (!closeConfirmed && View.WhatClosingLoses().Count > 0)
            {
                // the window stays open while the user is asked; it closes again once they say so
                e.Cancel = true;
                _ = AskBeforeClosing();
                return;
            }
            // the last two are kept in the state the first one saves
            View.EditorTab.KeepOpenFile();
            WindowPlace.Keep(this);
            View.FilesTab.KeepSession();
        };
        WindowPlace.Restore(this);
        Loaded += async (_, _) =>
        {
            await View.FilesTab.ReopenSession();
            await View.EditorTab.ReopenLastFile();
            // opening the app is not a step Undo takes back
            View.History.Forget();
            if (!Dialogs.WelcomeWindow.WasSeen) View.Welcome.Open();
        };
        if (frame == "native") View.CaptionButtons.Visibility = Visibility.Collapsed;
        else _ = new CustomTitleBar(this, View.IsTitleBarAt);
    }

    /// <summary>Whether the user said to close although something would be lost: the window then closes without asking again.</summary>
    private bool closeConfirmed;

    /// <summary>Closes the window at once, without asking about anything not yet saved or finished.</summary>
    internal void CloseWithoutAsking()
    {
        closeConfirmed = true;
        Close();
    }

    /// <summary>
    /// Asks before the app closes on something not yet saved or finished (<see cref="MainView.WhatClosingLoses"/>), and
    /// closes it when the user says Close Anyway.
    /// </summary>
    private async Task AskBeforeClosing()
    {
        if (View.Messages.IsOpen) return;
        if (!await View.AskBeforeClosing()) return;
        closeConfirmed = true;
        Close();
    }
}
