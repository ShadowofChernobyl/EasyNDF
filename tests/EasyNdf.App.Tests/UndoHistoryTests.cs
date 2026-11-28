using System.Text.Json.Nodes;
using System.Windows.Threading;
using EasyNdf.App.Files;
using EasyNdf.Data;

namespace EasyNdf.App.Tests;

/// <summary>
/// Undo files a step for each change to the mods, and none for opening another mod; a mod added and then opened, as
/// New and Make Copy do, is a change even though only the opening is told.
/// </summary>
[Collection(AppModsTests.Name)]
public sealed class UndoHistoryTests : IDisposable
{
    private readonly List<Mod> modsMade = [];

    public void Dispose()
    {
        // by name: Undo and Redo put copies in place of the mods made
        foreach (Mod mod in modsMade)
            if (AppData.Current.ModStore.Find(mod.Name) is Mod now) AppData.Current.ModStore.Delete(now);
    }

    private Mod NewMod(string name)
    {
        var mod = new Mod { Name = name, Presets = [new ModPreset { Name = name + " (Ammunition.ndf)", Rules = [new JsonObject { ["Name"] = "A rule", ["Kind"] = "Edit" }] }] };
        AppData.Current.ModStore.Add(mod);
        modsMade.Add(mod);
        return mod;
    }

    private static UndoHistory NewHistory()
    {
        var history = new UndoHistory(AppData.Current, new FileListModel());
        AppData.Current.ModsChanged += history.ModsChanged;
        return history;
    }

    [Fact]
    public void Opening_another_mod_is_not_a_step()
    {
        Mod first = NewMod("Undo open test A");
        Mod second = NewMod("Undo open test B");
        UndoHistory history = NewHistory();
        try
        {
            AppData.Current.Open(second);
            AppData.Current.Open(first);
            Assert.False(history.CanUndo);
        }
        finally
        {
            AppData.Current.ModsChanged -= history.ModsChanged;
        }
    }

    [Fact]
    public void A_mod_added_then_opened_is_a_step_and_undo_takes_it_away()
    {
        NewMod("Undo add test A");
        UndoHistory history = NewHistory();
        try
        {
            AppData.Current.Open(AppData.Current.Mods[0]);
            Mod added = NewMod("Undo add test B");
            AppData.Current.Open(added);
            Assert.True(history.CanUndo);
            history.Undo();
            Assert.Null(AppData.Current.ModStore.Find("Undo add test B"));
        }
        finally
        {
            AppData.Current.ModsChanged -= history.ModsChanged;
        }
    }

    [Fact]
    public void A_rule_changed_and_saved_after_opening_is_a_step()
    {
        Mod first = NewMod("Undo edit test A");
        Mod second = NewMod("Undo edit test B");
        UndoHistory history = NewHistory();
        try
        {
            AppData.Current.Open(second);
            Assert.False(history.CanUndo);
            second.Presets[0].Rules[0]!["Name"] = "Changed";
            AppData.Current.Save(second);
            Assert.True(history.CanUndo);
            AppData.Current.Open(first);
            history.Undo();
            Assert.Equal("A rule", (string?)AppData.Current.ModStore.Find("Undo edit test B")!.Presets[0].Rules[0]!["Name"]);
        }
        finally
        {
            AppData.Current.ModsChanged -= history.ModsChanged;
        }
    }

    [Fact]
    public void Steps_kept_packed_come_back_as_they_were_and_leave_the_history_as_it_was()
    {
        NewMod("Undo pack test A");
        Mod second = NewMod("Undo pack test B");
        UndoHistory history = NewHistory();
        try
        {
            foreach (string name in new[] { "First", "Second" })
            {
                Mod mod = AppData.Current.ModStore.Find("Undo pack test B")!;
                mod.Presets[0].Rules[0]!["Name"] = name;
                AppData.Current.Save(mod);
                // each change its own step: the moment of the one before is over
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            }
            string NameNow() => (string?)AppData.Current.ModStore.Find("Undo pack test B")!.Presets[0].Rules[0]!["Name"] ?? "";
            history.Undo();
            Assert.Equal("First", NameNow());
            history.Undo();
            Assert.Equal("A rule", NameNow());
            Assert.False(history.CanUndo);
            history.Redo();
            history.Redo();
            Assert.Equal("Second", NameNow());
            history.Undo();
            Assert.Equal("First", NameNow());
            Assert.Equal("A rule", (string?)AppData.Current.ModStore.Find("Undo pack test A")!.Presets[0].Rules[0]!["Name"]);
        }
        finally
        {
            AppData.Current.ModsChanged -= history.ModsChanged;
        }
    }

    /// <summary>Lets the window's thread rest, as it does between two things the user does: the next change is a step of its own.</summary>
    private static void Rest() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    [Fact]
    public void Changes_made_while_the_history_is_paused_are_one_step_as_an_export_is()
    {
        WindowThread.Run(() =>
        {
            Mod mod = NewMod("Undo pause test");
            UndoHistory history = NewHistory();
            try
            {
                // not paused: two changes, with a rest between them, are two steps
                mod.Presets[0].Rules[0]!["Name"] = "First";
                AppData.Current.Save(mod);
                Rest();
                mod.Presets[0].Rules[0]!["Name"] = "Second";
                AppData.Current.Save(mod);
                Rest();
                history.Undo();
                Assert.Equal("First", (string?)AppData.Current.ModStore.Find("Undo pause test")!.Presets[0].Rules[0]!["Name"]);
                history.Undo();
                Assert.False(history.CanUndo);

                // paused, as while an export runs: the same two changes are one step, filed when the pause ends
                mod = AppData.Current.ModStore.Find("Undo pause test")!;
                history.IsPaused = true;
                mod.Presets[0].Rules[0]!["Name"] = "First";
                AppData.Current.Save(mod);
                Rest();
                mod.Presets[0].Rules[0]!["Name"] = "Second";
                AppData.Current.Save(mod);
                Rest();
                Assert.False(history.CanUndo);
                history.IsPaused = false;
                history.Mark();
                Assert.True(history.CanUndo);
                history.Undo();
                Assert.Equal("A rule", (string?)AppData.Current.ModStore.Find("Undo pause test")!.Presets[0].Rules[0]!["Name"]);
                Assert.False(history.CanUndo);
            }
            finally
            {
                AppData.Current.ModsChanged -= history.ModsChanged;
            }
        });
    }
}
