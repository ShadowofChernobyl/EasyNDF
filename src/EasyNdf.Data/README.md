# EasyNdf.Data

Where the app keeps its data, the file formats it shares with the HTML app, and the app's logic that needs no screen (the file list's, the themes'). It has no user interface.

## The folders

The data is in `%LOCALAPPDATA%\EasyNDF\`, or, in portable mode, in a `Data` folder beside EasyNDF.exe. Portable mode is on while the file `EasyNDF portable mode.txt` sits beside the exe (`AppFolders`). The WinForms app's `RulePresets` folder is never read or changed.

| Folder | What | Class |
|---|---|---|
| `Mods` | One `.mod.json` per mod, in the "Export mod" format. A mod file can be copied in or out by hand. | `ModStore` |
| `Themes` | One `.theme.json` per custom theme, in the "Export theme" format. | `ThemeStore` |
| `Filters` | One `.json` per saved descriptor filter, in the filter window's "Export" format. | `FilterStore` |
| `Languages` | One `.json` per imported language pack, named after its code. | `LanguageStore` |
| `Settings` | `settings.json` (the Settings tab, with the HTML app's keys) and `state.json` (what the app remembers between sessions). | `SettingsStore` |

Every file is written to a temporary file first, which then replaces the old one, so a crash never leaves half a file.

## Formats shared with the HTML app

`ModFiles` writes and reads mod files and backups exactly as the HTML app does, including its messages for files that are not mods. The JSON is written like JavaScript's `JSON.stringify(value, null, 2)`.

One known difference: a number written by hand in a different form, such as `1.0` or `1e2`, keeps that form here, while the HTML app would write `1` or `100`. Neither app writes numbers that way itself.

## The file list (`FileList/`)

The Files tab's logic, ported from the HTML app: a row (`FileEntry`), its Status text (`FileStatus`), which preset a file gets when it is added (`PresetMatching`), the search box, "Show only" and sorting (`FileListView`), and the automatic column widths (`ColumnFit`).

