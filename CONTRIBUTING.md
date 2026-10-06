# Contributing

Issues and pull requests are welcome. Please keep changes focused, and describe the behavior you are fixing or adding.

## Setup

Install the .NET 8 SDK or newer. Then:

```powershell
dotnet test tests/SimplyDraw.Core.Tests/SimplyDraw.Core.Tests.csproj
dotnet run --project src/SimplyDraw.App
```

## What to put where

- Drawing rules, file format, and brush data go in `src/SimplyDraw.Core`, with a test when the behavior is easy to state.
- Window layout, input, and painting go in `src/SimplyDraw.App`.
- User-facing behavior belongs in `docs/` when it changes how someone uses the app.

`dotnet test` should pass before you open a pull request. If you change the canvas or the tools, also try a pen if you have one, plus the mouse: draw, erase, undo, select, save, and reopen.

## Style

Match the file you are editing. Nullable reference types are on. Prefer a small type with a clear job over a new framework. Do not add a dependency unless the app cannot do the job with the .NET libraries it already uses.
