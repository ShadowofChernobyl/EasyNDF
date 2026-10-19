using System.IO;
using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Help;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>The About tab: what EasyNDF is, who made it, the credits and some links (Help/about.html).</summary>
public partial class AboutPage : UserControl
{
    /// <summary>Builds the tab with the app's version in its text.</summary>
    public AboutPage()
    {
        InitializeComponent();
        ShowPage();
        Texts.LanguageChanged += ShowPage;
    }

    /// <summary>Draws the page in the language in use, with the app's version in it.</summary>
    private void ShowPage()
    {
        (FrameworkElement body, double top, _) = new WikiRenderer(PageLook.About).Render(WikiPages.AboutHtml.Replace("{version}", MainView.AppVersion));
        // the larger of the buttons' 12 pixels and the first heading's 16
        body.Margin = new Thickness(0, Math.Max(12, top), 0, 0);
        Body.Content = body;
    }

    private MainView View => MainView.Of(this);

    /// <summary>A text the app carries in itself (the csproj names them).</summary>
    private static string ReadText(string name)
    {
        using Stream stream = typeof(AboutPage).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void LicenseClicked(object sender, RoutedEventArgs e) => ShowLicense();

    /// <summary>"License": the license EasyNDF is under (LICENSE.txt).</summary>
    public void ShowLicense() => View.TextWindow.Open(Texts.Get("About.License"), Dialogs.TextWindow.PlainText(ReadText("LICENSE.txt").TrimEnd()));

    private void ChangeLogClicked(object sender, RoutedEventArgs e) => View.ShowChangeLog();

    /// <summary>EasyNDF's Ko-fi page, where people who like the app can support it.</summary>
    public const string SupportPage = "https://ko-fi.com/shadowofchernobyl";

    /// <summary>"Support on Ko-fi": opens the Ko-fi page in the browser.</summary>
    private void SupportClicked(object sender, RoutedEventArgs e)
    {
        // a browser that cannot be started (none set, or blocked) leaves the app as it was
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SupportPage) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
