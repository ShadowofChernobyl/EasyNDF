namespace EasyNdf.Data.Tests;

public class AppFoldersTests
{
    [Fact]
    public void Without_the_marker_the_data_is_in_local_app_data()
    {
        using var app = new TemporaryFolder();
        using var local = new TemporaryFolder();

        AppFolders folders = AppFolders.Find(app.Path, local.Path);

        Assert.False(folders.IsPortable);
        Assert.Equal(Path.Combine(local.Path, "EasyNDF"), folders.Root);
        Assert.Equal(Path.Combine(local.Path, "EasyNDF", "Mods"), folders.Mods);
    }

    [Fact]
    public void Turning_portable_mode_on_copies_the_data_and_leaves_the_old_data()
    {
        using var app = new TemporaryFolder();
        using var local = new TemporaryFolder();
        AppFolders installed = AppFolders.Find(app.Path, local.Path);
        installed.CreateAll();
        File.WriteAllText(Path.Combine(installed.Mods, "WTO.mod.json"), "{}");

        AppFolders portable = AppFolders.SetPortable(true, app.Path, local.Path, copyData: true);

        Assert.True(portable.IsPortable);
        Assert.Equal(Path.Combine(app.Path, "Data"), portable.Root);
        Assert.True(File.Exists(Path.Combine(portable.Mods, "WTO.mod.json")));
        Assert.True(File.Exists(Path.Combine(installed.Mods, "WTO.mod.json")));
        Assert.True(AppFolders.Find(app.Path, local.Path).IsPortable);

        AppFolders back = AppFolders.SetPortable(false, app.Path, local.Path, copyData: false);
        Assert.False(back.IsPortable);
        Assert.False(File.Exists(Path.Combine(app.Path, AppFolders.PortableMarkerName)));
    }

    [Fact]
    public void A_writable_folder_can_be_portable()
    {
        using var app = new TemporaryFolder();
        Assert.True(AppFolders.CanBePortable(app.Path));
        Assert.False(AppFolders.CanBePortable(Path.Combine(app.Path, "missing")));
    }
}
