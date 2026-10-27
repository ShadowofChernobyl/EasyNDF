# EasyNDF

EasyNDF is a rule-based editor for WARNO's `.ndf` game files. You describe a change once, such as "double the range of every autocannon", and EasyNDF applies it to every matching entry in the game's files and exports edited copies into your mod.

> **This branch is the new Windows app, in progress.** It is being rebuilt in C# and WPF to look, feel and act like the HTML version of EasyNDF, which is the reference.
> - The earlier WinForms app is kept on the [`archive/winforms`](https://github.com/ShadowofChernobyl/EasyNDF/tree/archive/winforms) branch.
> - The HTML version runs in the browser: <https://shadowofchernobyl.github.io/EasyNDF-HTML-Demo/>

## Building

You need **Visual Studio 2026** (Community is free) with the **.NET desktop development** workload, or the **.NET 10 SDK** on its own.

- In Visual Studio: open `EasyNDF.slnx`, then **Build > Build Solution**.
- From a command line: `dotnet build EasyNDF.slnx`

## What is where

| Folder | What |
|---|---|
| `src/EasyNdf.Engine` | The rule engine: reads NDF, runs rules, writes the exported file. It has no user interface. See its [README](src/EasyNdf.Engine/README.md). |
| `src/EasyNdf.Data` | Where the app keeps its data (Local AppData or portable), and the mod, theme, filter and language pack files. See its [README](src/EasyNdf.Data/README.md). |
| `src/EasyNdf.App` | The WPF app itself. |

## The app's texts and languages

Every text the app shows has a fixed name, such as `Files.AddFolder`, and its English is in `src/EasyNdf.App/Languages/en`, one file per part of the app (`files.json`, `rules.json`, `settings.json`...).

- **In a screen (XAML):** `Content="{t:Text Files.AddFolder}"`, with `xmlns:t="clr-namespace:EasyNdf.App.Text"` at the top of the file.
- **In code:** `Texts.Get("Files.AddFolder")`. A text with blanks: `"Delete \"{name}\"?"` and `Texts.Get("Rules.DeleteAsk", ("name", rule.Name))`. A text that depends on a number lists its forms with `|`: `"{count} edit|{count} edits"` and `Texts.Count("Files.Edits", 5)`.
- **Bold or code inside a sentence:** `**bold**` and `` `code` ``, shown with `t:RichText.Markup="{t:Text Welcome.Intro}"` on a TextBlock.
- **To add a text:** add its name and English to the right file, then use the name. Rewording the English later never loses a translation: packs remember which English they were made from, and count the text as "to check".
- **The Help wiki and the About tab** stay as HTML (`src/EasyNdf.App/Help/wiki.json` and `about.html`); each page's title and HTML, and each group, are texts too (`Help.Page.start.Title`, `Help.Page.start.Html`, `Help.Group.GettingStarted`, `About.Page.Html`), so packs translate them like the rest. The change log is one text per version (`Help.ChangeLog.1.0.0.Html`), so a new version is the only part to translate.
- **Numbers:** `Texts.Number(1234)` writes "1,234" with the language's separators; `Texts.CountWide` is `Texts.Count` with the number written that way.
- Other languages are packs imported on the Settings tab (Language card). **Export** gives a file with every text to translate; whatever a pack lacks shows in English.

## Mods made in other versions

Mod files keep the HTML app's format (`FormatVersion: 1`), so mods made in the HTML app open here unchanged. Plain Edit rules are also the format of the WinForms app.

## Test switches

For a PC that draws the app oddly: `--render software` draws without the graphics card, and `--frame native` uses Windows' own title bar and frame instead of the app's. In Visual Studio, set them under **Debug > EasyNdf.App Debug Properties > Command line arguments**.

## License

EasyNDF is free software under the [GNU Affero General Public License, version 3](LICENSE.txt), with two additional terms (section 7): forks may not call themselves EasyNDF or suggest that they are, and modified versions must be marked as different from the original. The code it includes from others is under the MIT License (see THIRD-PARTY-NOTICES.txt).

"ShadowofChernobyl" is the author's user name. Despite it, the author is not involved with GSC Game World or any of its products.
