# Contributing to Helix Explorer

Thanks for your interest in contributing. This guide covers how to report issues and submit changes.

## Reporting issues

- Search [existing issues](https://github.com/bolorundurowb/helix-file-explorer/issues) before opening a new one.
- Use the bug report or feature request template when it fits.
- For bugs, include steps to reproduce, expected vs actual behavior, Helix Explorer version, and Windows version.

## Development setup

Requirements:

- Windows 10 or 11
- [.NET 10 SDK](https://dot.net)

Clone the repository, then from the repo root:

```powershell
dotnet restore HelixExplorer.sln
dotnet build HelixExplorer.sln
dotnet run --project src/HelixExplorer
```

Open a folder in a new window:

```powershell
dotnet run --project src/HelixExplorer -- --path "C:\Users"
```

Run tests:

```powershell
dotnet test HelixExplorer.sln
```

## Pull requests

1. Open an issue first for larger changes so the approach can be discussed.
2. Keep PRs focused on one change.
3. Match the style of nearby code.
4. Add or update tests when you change behavior.
5. Ensure `dotnet build` and `dotnet test` succeed before you open the PR.

## License

By contributing, you agree that your contributions are licensed under the project's [GPL-3.0](LICENSE) license.
