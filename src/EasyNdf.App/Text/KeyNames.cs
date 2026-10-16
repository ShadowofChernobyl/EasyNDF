namespace EasyNdf.App.Text;

/// <summary>
/// Keys as the language in use names them, for the shortcuts menus show beside their items: "Ctrl+Up" is
/// "Ctrl+Haut" in French. Keys whose names are the same everywhere (Ctrl, letters, signs) are left as they are.
/// </summary>
public static class KeyNames
{
    /// <summary>A shortcut such as "Ctrl+Shift+Up", each key in the language in use.</summary>
    public static string Of(string? shortcut) =>
        string.IsNullOrEmpty(shortcut) ? "" : string.Join("+", shortcut.Split('+').Select(Key));

    /// <summary>One key's name; a key with no name of its own in the language is given back as it is.</summary>
    private static string Key(string key) => key switch
    {
        "Shift" => Texts.Get("Common.Key.Shift"),
        "Enter" => Texts.Get("Common.Key.Enter"),
        "Delete" => Texts.Get("Common.Key.Delete"),
        "Escape" => Texts.Get("Common.Key.Escape"),
        "Up" => Texts.Get("Common.Key.Up"),
        "Down" => Texts.Get("Common.Key.Down"),
        _ => key,
    };
}
