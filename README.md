

# EasyNDF (In Alpha)

EasyNDF is a Windows desktop tool and .NET codebase for parsing and modifying `.ndf` files through reusable rules.

It consists of three main parts:

* **EasyNDF**: a WinForms GUI for importing an NDF file, building rules, saving rule presets, and exporting a modified file.
* **NDFParser**: a standalone parser/writer library built on ANTLR4 that turns NDF text into a typed AST and writes that AST back to text.
* **NDFCLI**: a minimal command-line utility that round-trips an input file through the parser and writer.

## Purpose

EasyNDF is meant to make repetitive NDF edits easier.

Instead of hand-editing the same fields over and over, the GUI lets you:

* import an `.ndf` file
* define one or more **rules**
* give each rule one or more **conditions**
* apply one or more **actions** when those conditions match
* export a rewritten file
* save and reuse rule presets for later use via JSON files

At the code level, the repository is also a general NDF parsing project. The `NDFParser` library can be used independently from the GUI if you want to build your own tools on top of the AST.

## What this project is

* A **rule-based NDF modding tool**
* A **parser/writer library** for NDF syntax
* A **WinForms desktop application** targeting **.NET 8 / Windows**
* A **developer-oriented codebase** with parser tests and property-based round-trip tests

## What this project is not

* Not a general-purpose text editor
* Not a full semantic validator for every possible NDF construct
* Not a mod manager
* Not a command-line-heavy automation suite **_yet_**; the CLI is currently minimal


## Key Features

### Rule-based editing

Rules are applied in the order shown in the main list (top-to-bottom)
![Insert Rule List Description.](https://github.com/ShadowofChernobyl/EasyNDF/blob/master/images/RuleListPreview.png?raw=true)

Each rule contains:

* a **name**
* an **enabled/disabled** state
* a condition logic mode: **Any** or **All**
* a list of **conditions**
* a list of **actions**

![Insert Rule Description](https://github.com/ShadowofChernobyl/EasyNDF/blob/master/images/RulePreview.png?raw=true)

Each condition contains

* **Operands** - Descriptor Title, Descriptor Type, Descriptor Properties, etc.
* **Operators**  - Starts With, Contains, Is Less Than, Is Greater Than, etc.
*  **Values** - Ammo_AutoCanon, Ammo_RocketArt, TEntityDescriptor, etc,

![Insert Condition Description](https://github.com/ShadowofChernobyl/EasyNDF/blob/master/images/ConditionPreview.png?raw=true)


Each action contains:

* **Target** - MaximumRangeGRU, FuelMoveDuration, PhysicalDamages, etc.
* **Operators**  - Add, Subtract, Multiply, Divide, Set To, etc.
*  **Values** - 1.5, 2, 10, etc.

![Insert Action Description](https://github.com/ShadowofChernobyl/EasyNDF/blob/master/images/ActionPreview.png?raw=true)

Actions can also append an optional generated comment, with optional timestamp and original-value text.

![Insert Comment Description](https://github.com/ShadowofChernobyl/EasyNDF/blob/master/images/CommentPreview.png?raw=true)

### Rule Presets

A list of rules can be saved as JSON presets and reloaded later.

![Insert Preset Description](https://github.com/ShadowofChernobyl/EasyNDF/blob/master/images/RulePresetPreview.png?raw=true)

Saved presets can be found here:
`%USERPROFILE%\AppData\Local\EasyNDF\RulePresets\`


## How to Use

### GUI workflow

1. Launch **EasyNDF**
2. Click **Import** and select an `.ndf` file
3. Click **New Rule**
4. Add one or more conditions
5. Add one or more actions
6. Save rules as a preset if desired
7. Click **Export** to write the transformed file

### Rule workflow

A rule is evaluated against each parsed top-level assignment whose value is an object (also known as a descriptor). 

Example from UniteDescriptor.ndf:
```
export Descriptor_Unit_M1A1HA_Abrams_CMD_US is TEntityDescriptor
```

If the conditions pass, the configured actions are applied and the descriptor is rewritten before export.

### Preset workflow

* Use **Save Preset** to store the current rule list
* Use the preset combo box to reload a saved preset
* Right-click the deleted preset to access options such as deleting the preset

### Keyboard / UI shortcuts

In the rule list:

* **Ctrl+C** copies the selected rule as JSON
* **Ctrl+V** pastes a previously copied rule
* **Ctrl+Up / Ctrl+Down** moves a rule
* **Delete** removes a rule
* **Double-click** opens rule configuration
* Right-click menus are available for rules, conditions, actions, and presets

## Known Issues

These are the main issues visible from the current source code:

* `MainForm` still lists several TODO items, including a more complete settings window, tooltips/help, better file path display, and broader multi-item copy/paste support.
* `Remove [array]` is marked in code as not fully implemented.
* The settings form is currently just a placeholder shell.

There are also a few code-level implementation details worth being aware of:

* The GUI applies rules only to **top-level assignment declarations whose values are object values** before recursing into nested members.
* Numeric action paths currently construct `StringLiteral` nodes for rewritten numeric values, which may not be the intended long-term type behavior.
* The parser grammar explicitly handles `//` line comments, but not general block-comment syntax.

## Useful Links

### Discord Servers
* [Eugen Systems Official](https://discord.com/invite/pv9VAcN)
* [YSM Community](https://discord.gg/XmbhaSRqfZ)
* [A World In Flames Community](https://discord.gg/3FsfYNKpZe)

### Steam Workshop
* [WARNO Tactical Overhaul](https://steamcommunity.com/sharedfiles/filedetails/?id=3387658237)
* [YSM x WiF x WTO](https://steamcommunity.com/sharedfiles/filedetails/?id=3554281691)
* [Yokaiste's Sandbox Mod](https://steamcommunity.com/sharedfiles/filedetails/?id=3296415395)
* [A World in Flames](https://steamcommunity.com/sharedfiles/filedetails/?id=3388575848)

### Additional Links
* [WARNO Wiki](https://waryes.com/)
* [Tank Encyclopedia](https://tanks-encyclopedia.com/)

## Special Thanks

* **mattysmith22** - Created NDFParser, NDFCLI and mentored me throughout the development of this project. Without his help, this project would not be possible.
* **Silver (AKA Playmobill)** - Provided stylistic overlays for buttons and other UI elements as well and guided me through many design decisions pertaining to the UI.
* **Yokaiste** - Creator of Yokaiste's Sandbox Mod, a Steam Workshop mod for WARNO which has driven my desire to continue my work with modding WARNO and the eventual development of this modding tool.
* **Chinofchrist** - Creator of Chinofchrist's Realism Mod, a Steam Workshop mod for WARNO which initially piqued my interest in the WARNO modding scene years ago.
* **CHO KING (AKA étouf fement)** - Creator of A World in Flames, a Steam Workshop mod for WARNO which has provided myself, my friends and the greater WARNO community with great enjoyment.
* **Eugen Systems** - Development team behind great games like Steel Division 2, Wargame and WARNO
* **The Entire WARNO Modding Community** - Many thanks for saving me countless hours of troubleshooting time by having stepped on many landmines (encountered bugs) before me and posting their findings in the Official Eugen Discord Server

---

## Development

### Design Notes

A few implementation choices are especially important for understanding the project:

* The parser is intentionally separated from the GUI so the AST and writer can be reused elsewhere.
* The writer produces normalized formatting rather than preserving original whitespace.
* The GUI repopulates target/operand lists by scanning the imported file rather than relying on a fixed schema.
* Presets are stored separately from the imported/exported NDF files so they can be reused across sessions.

### Requirements

* **.NET 8 SDK**
* **Windows** for the WinForms GUI
* Visual Studio 2022 or the .NET CLI for building the non-GUI projects

### Build

```bash
dotnet restore
dotnet build
```

### Test

```bash
dotnet test
```

### GitHub Actions

The repository includes a CI workflow that restores, builds, and tests the solution on pushes and pull requests to `master`.

### CLI Usage

The CLI is intentionally simple.

```bash
dotnet run --project NDFCLI -- <input.ndf> <output.ndf>
```

Behavior:

* parses the input file
* writes the parsed AST back to the output file
* prints a success message and elapsed time

This is mainly useful as a parser/writer smoke test or a base for future automation.


## Project Structure

```text
EasyNDF.sln
├── EasyNDF/           # WinForms GUI application
├── NDFParser/         # Parser, grammar, AST, writer
├── NDFCLI/            # Minimal console entry point
└── NDFParserTests/    # xUnit + FsCheck tests
```

### EasyNDF

The GUI project is centered around:

* `MainForm` for file import/export, preset management, and rule ordering
* `RuleForm` for editing a single rule
* `ConditionForm` for editing one condition
* `ActionForm` for editing one action
* `FileManager` for import/export and preset persistence
* `RuleEngine` for condition evaluation and action execution
* `PropertyFinder` for discovering candidate property names from the imported file
* `OpenForms` for moving data into and out of the modal forms

### NDFParser

The parser project contains:

* `ndf.g4` ANTLR grammar
* `Parser.cs` for parse entry points and AST construction
* `Writer.cs` for serializing the AST back to text
* `AST/` nodes and visitor interfaces

### NDFCLI

The CLI currently:

* accepts an input file path and output file path
* parses the input file
* writes it back out
* prints elapsed time

### NDFParserTests

Tests cover:

* parser correctness on representative examples
* writer output formatting
* parser/writer round-trip compatibility across generated ASTs
