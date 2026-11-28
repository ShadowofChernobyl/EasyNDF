# Tests

Three test projects, one for each part of EasyNDF:

| Project | Covers |
|---|---|
| `EasyNdf.Engine.Tests` | Reading NDF, running rules, writing the edited file, value checks |
| `EasyNdf.Data.Tests` | Mod files, the file list, Armory changes turned into rules, the games' own file formats |
| `EasyNdf.App.Tests` | The app's logic that needs no window: export runs, mod conflicts, settings |

Run them all from the repository folder:

```
dotnet test tests/EasyNdf.Engine.Tests
dotnet test tests/EasyNdf.Data.Tests
dotnet test tests/EasyNdf.App.Tests
```

They need no game files. A few tests also check the real game data when you point them at it (`EASYNDF_GAMEDATA` and `EASYNDF_GUIDE`); without it they pass without checking.

## The HTML app's cases

The `...MatchesHtmlApp` tests compare the engine with what the HTML app gave, from the cases in `Cases/`. Those were recorded before the HTML app was frozen (EasyNDF 1.1.4), with scripts that are not part of this repository (comments name them `tools/engine-check`). When a new feature changes one of these results on purpose, the case is updated by hand.
