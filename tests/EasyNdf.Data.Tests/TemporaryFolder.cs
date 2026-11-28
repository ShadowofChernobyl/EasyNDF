namespace EasyNdf.Data.Tests;

/// <summary>A new empty folder for one test, deleted afterwards.</summary>
public sealed class TemporaryFolder : IDisposable
{
    /// <summary>The folder's path.</summary>
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "easyndf-test-" + Guid.NewGuid().ToString("N"));

    public TemporaryFolder() => Directory.CreateDirectory(Path);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
