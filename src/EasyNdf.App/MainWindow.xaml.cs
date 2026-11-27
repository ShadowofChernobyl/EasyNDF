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
        // F11: full screen, or back (Windowing/FullScreen.cs)
        FullScreen.Watch(this, View, () => View.ShowWindowState(WindowState));
        // Every key not taken by the control that has the focus ends up here, even when nothing has the focus.
        KeyDown += (_, e) => View.HandleShortcut(e);
        // the app's windows and questions are shown in whichever of its windows is in use (MainView.PopOut.cs)
        Activated += (_, _) => View.MainWindowCameIntoUse();
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
            // the tabs in windows of their own come back first, each window keeping its place
            View.CloseTabWindows();
            // the Editor's open files and the window's place go into the state, saved here; the file list has a file of
            // its own (since the file list moved out of the state, nothing else saved these as the app closed)
            View.EditorTab.KeepOpenFile();
            WindowPlace.Keep(this);
            AppData.Current.SaveState();
            View.FilesTab.KeepSession();
            AppData.Current.SaveWhatWaits();
            // a mod deleted this session can no longer be brought back by Undo: its folder goes, when it holds nothing
            ModFolders.Views?.RemoveEmptyFoldersOfGoneMods(
                AppData.Current.Mods.Select(mod => AppData.Current.ModStore.FileStemOf(mod)).Where(stem => stem is not null).Select(stem => stem!));
        };
        WindowPlace.SizeForFirstTime(this);
        WindowPlace.Restore(this);
        // a window that opens maximized raised no change of state: its maximize button shows Restore from the start
        View.ShowWindowState(WindowState);
        Loaded += async (_, _) =>
        {
            await View.FilesTab.ReopenSession();
            await View.EditorTab.ReopenLastFile();
            // opening the app is not a step Undo takes back
            View.History.Forget();
            if (!Dialogs.WelcomeWindow.WasSeen) View.Welcome.Open();
            else if (!App.RunsTests) View.ShowLatestChangesAfterUpdate();
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
