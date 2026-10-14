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

## Mods made in other versions

Mod files keep the HTML app's format (`FormatVersion: 1`), so mods made in the HTML app open here unchanged. Plain Edit rules are also the format of the WinForms app.

## Test switches

For a PC that draws the app oddly: `--render software` draws without the graphics card, and `--frame native` uses Windows' own title bar and frame instead of the app's. In Visual Studio, set them under **Debug > EasyNdf.App Debug Properties > Command line arguments**.
