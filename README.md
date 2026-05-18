# SpaceSweeper

SpaceSweeper is a native Windows storage scanner and safe cleanup app inspired by SpaceSniffer and WizTree.

## Project Direction

- Native UI only: WPF first, no WebView, no CEF, no Chromium.
- Core logic is isolated into reusable libraries.
- `SpaceSweeper.Core` is UI-neutral and keeps the scan/cleanup contracts.
- `SpaceSweeper.Windows` contains Windows-specific filesystem, recycle-bin, elevation, and future NTFS provider work.
- `SpaceSweeper.App.Wpf` is the first desktop shell.
- WinUI 3 should be explored later on a separate branch after the WPF MVP is stable.

## Branch Workflow

- `main`: stable release branch.
- `dev`: default development branch.
- Feature work starts from `dev`.
- Do not merge feature branches into `dev` locally by default. Open a GitHub PR and let the maintainer approve/merge.
- A future WinUI 3 rewrite should use a separate branch, for example `feature/winui3-ui-shell`.

## Build

This repository is pinned to .NET SDK `10.0.300` through `global.json`.

```powershell
dotnet restore
dotnet build SpaceSweeper.sln
dotnet test SpaceSweeper.sln
```

## Self-contained Publish

The app is designed so target machines do not need to install .NET separately.

```powershell
dotnet publish src/SpaceSweeper.App.Wpf/SpaceSweeper.App.Wpf.csproj `
  /p:PublishProfile=win-x64-self-contained
```

The publish output contains the runtime and app binaries for `win-x64`.

## Current MVP Status

- Managed filesystem scanning is functional.
- NTFS fast scanning has a provider boundary in `SpaceSweeper.Windows`, but the low-level MFT/USN reader is intentionally not enabled yet.
- Cleanup defaults to moving selected items to the Recycle Bin and blocks protected paths.
- The WPF app includes Chinese/English runtime switching.
