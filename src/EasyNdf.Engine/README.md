# EasyNdf.Engine

The rule engine: it reads an NDF file, runs a mod's rules over it, and writes the edited file. It has no user interface.

It is a port of the HTML app's engine (the `<script id="engine-src">` part of the page), and must give **byte-for-byte the same output**. 

## Using it

```csharp
var engine = new NdfEngine();
engine.Load(File.ReadAllText("Ammunition.ndf"));          // throws NdfParseException for text that is not NDF

ModFile mod = ModFile.Read(File.ReadAllText("My mod.json"));
RunResult result = engine.Run(mod.Presets[0].Rules, new RunOptions { ModName = mod.ModName ?? "" });

File.WriteAllText(@"MyMod\Ammunition.ndf", result.Text);  // check result.ParseError first
```

`RunResult` also has, per rule, what it matched and changed (`Stats`), the before-and-after list the Details panel shows (`Logs`), and the warnings for the Files tab (`Warnings`).

## How it works

1. **Reading** (`Ndf/`). `Tokenizer` splits the text into tokens and keeps the comments between them. `Parser` builds `Declaration`s whose values are `NdfValue`s: objects, lists, pairs, numbers, texts and so on. Every value remembers where it sits in the text.
2. **Running rules** (`Rules/`). `RuleRunner` goes through the rules in order. Each rule type has its own method there (`RunEdit`, `RunClone`, `RunTable`...). `Conditions` decides which descriptors a rule applies to; `ActionApplier` changes the values.
   - A rule never changes the file's own declarations. The first time a rule is about to change one, it gets a copy (`WorkItem.Own()`), and the copy remembers which text it replaces.
3. **Writing** (`Writing/`). `ExportWriter` copies the source text and replaces only what rules changed. That is how exports keep their comments, spacing, line endings and the mod builder's marker comments. `CanonicalWriter` writes values in one fixed layout, without comments; that is the text conditions read.

## Beyond the rules

These are the HTML app's logic outside its engine, ported so the app's screens only show results:

| Folder | What |
|---|---|
| `Viewer/` | The Editor tab's descriptor list, property rows and filter index (`DescriptorViewer`), and Filter Descriptors' matching (`DescriptorFilter`). |
| `Writing/LineDiff.cs` | The changed lines for "Changes only". |
| `Mods/RuleLoading.cs` | Tidying rules as they load, including turning the old "Add descriptor" action into Add rules. |
| `Export/` | The "Edited with EasyNDF" and watermark lines, the "Line endings" setting, and how rules name loaded files. |

## Matching JavaScript

The HTML app's engine is JavaScript, and a few things work differently in C#. They are handled in `Text/`:

- **Numbers** (`JsNumber`): JavaScript writes `1e+21` where C# writes `1E+21`, writes `-0` as `0`, and rounds halves up where .NET rounds them to even.
- **Text** (`JsText`): JavaScript and C# disagree on what counts as white space, and C#'s `IndexOf`, `StartsWith` and `EndsWith` compare by culture unless told otherwise.
- **Patterns**: `\w` and `\d` in .NET also match non-English letters and digits, and `$` also matches before a final line break. The patterns here spell out `[A-Za-z0-9_]` and use `\z`.

When you change the engine, keep to the same rules: compare strings with `StringComparison.Ordinal`, and use the `JsText` and `JsNumber` helpers.
