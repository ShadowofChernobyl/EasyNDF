using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Files;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// Before an export into a game mod folder: when a game update left the folder behind (<see cref="GameUpdateCheck"/>),
/// the export says so and offers to run the folder's UpdateMod.bat first, to export anyway, or to stop.
/// </summary>
public partial class FilesPage
{
    /// <summary>
    /// Checks the folders an export writes into. True to go on with the export: none is out of date, the user chose
    /// Export Anyway, or Update First brought it up to date.
    /// </summary>
    /// <param name="chosen">The folder chosen for this export; null when each file goes to the folder kept for it.</param>
    private async Task<bool> ExportFoldersAreUpToDate(string? chosen)
    {
        List<string> folders = chosen is not null ? [chosen]
            : [.. FilesToExport().Select(entry => entry.ExportFolder).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];
        var asked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders)
        {
            GameUpdateCheck.OutOfDate? found = await Task.Run(() => GameUpdateCheck.Check(folder));
            if (found is null || !asked.Add(found.ModFolder)) continue;
            DialogAnswer answer = await Messages.Ask(Texts.Get("Files.UpdateCheck.Ask"), new DialogOptions
            {
                Icon = DialogIcon.Warning,
                Extra = Facts(found),
                Ok = Texts.Get("Files.UpdateCheck.UpdateFirst"),
                Alternative = Texts.Get("Files.UpdateCheck.ExportAnyway"),
            });
            if (answer.Choice == DialogChoice.Alternative) continue;
            if (answer.Choice != DialogChoice.Ok || !await UpdateModFolder(found)) return false;
        }
        return true;
    }

    /// <summary>Folder, Made for and Latest, one line each under the question.</summary>
    private static Grid Facts(GameUpdateCheck.OutOfDate found)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        (string Label, string Value)[] lines =
        [
            (Texts.Get("Files.UpdateCheck.Folder"), found.ModFolder),
            (Texts.Get("Files.UpdateCheck.MadeFor"), found.MadeFor),
            (Texts.Get("Files.UpdateCheck.Latest"), found.Latest),
        ];
        for (int row = 0; row < lines.Length; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = lines[row].Label, Margin = new Thickness(0, 0, 12, 2) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            var value = new TextBlock { Text = lines[row].Value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) };
            Grid.SetRow(label, row);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }
        return grid;
    }

    /// <summary>
    /// "Update First": the folder's UpdateMod.bat runs in a console window of its own, as it may ask about files it
    /// cannot merge and ends on "Press any key"; once that window closes, the folder is checked again. True when it is
    /// up to date now.
    /// </summary>
    private async Task<bool> UpdateModFolder(GameUpdateCheck.OutOfDate found)
    {
        if (found.UpdateScript is null)
        {
            await Messages.Tell(Texts.Get("Files.UpdateCheck.NoScript", ("folder", found.ModFolder)), DialogIcon.Error);
            return false;
        }
        Model.Say(Texts.Get("Files.UpdateCheck.Running"));
        try
        {
            // by its whole path: a command prompt may be set not to look in the folder it is in
            using Process? run = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + found.UpdateScript + "\"\"")
            {
                WorkingDirectory = found.ModFolder,
                UseShellExecute = true,
            });
            if (run is not null) await run.WaitForExitAsync();
        }
        catch (Exception problem) when (problem is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            await Messages.Tell(Texts.Get("Files.UpdateCheck.NotRun", ("reason", problem.Message)), DialogIcon.Error);
            return false;
        }
        if (await Task.Run(() => GameUpdateCheck.Check(found.ModFolder)) is null)
        {
            Model.Say(Texts.Get("Files.UpdateCheck.Updated"));
            return true;
        }
        await Messages.Tell(Texts.Get("Files.UpdateCheck.StillOld"), DialogIcon.Error);
        return false;
    }
}
