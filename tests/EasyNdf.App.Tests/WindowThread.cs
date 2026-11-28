using System.Windows;
using System.Windows.Threading;

namespace EasyNdf.App.Tests;

/// <summary>
/// The tests' window thread, with the app's styles, for checks that make the app's controls. One thread for all the
/// checks, as the app's styles belong to the thread that read them.
/// </summary>
internal static class WindowThread
{
    /// <summary>Runs a check on the window thread; a failure there fails the test.</summary>
    public static void Run(Action check)
    {
        Exception? failure = null;
        Thread.Value.Invoke(() =>
        {
            try { check(); }
            catch (Exception problem) { failure = problem; }
        });
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static readonly Lazy<Dispatcher> Thread = new(() =>
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new System.Threading.Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            if (Application.Current is null)
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (string dictionary in new[] { "Styles/DefaultTheme.xaml", "Styles/Controls.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/EasyNDF;component/" + dictionary) });
            }
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    });
}
