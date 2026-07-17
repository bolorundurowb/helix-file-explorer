# AGENTS.md

Instructions for coding agents working in **Helix Explorer** — a Windows file manager (Avalonia + .NET 10). Prefer this file over inventing project conventions. Humans should still use `README.md` and `CONTRIBUTING.md`.

**Speed and performance are the priority.** Prefer efficient enumeration, allocation-conscious code, and UI that stays responsive under large folders and heavy file ops. Do not trade away responsiveness for convenience abstractions.

## Setup and commands

Requires Windows 10/11 and the .NET 10 SDK.

```powershell
dotnet restore HelixExplorer.sln
dotnet build HelixExplorer.sln -c Release
dotnet test HelixExplorer.sln -c Release
dotnet run --project src/HelixExplorer
dotnet run --project src/HelixExplorer -- --path "C:\Users"
```

Release installer publish (matches CI):

```powershell
dotnet publish src/HelixExplorer/HelixExplorer.csproj -c Release -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o publish/win-x64
```

Bump product version only in `Directory.Build.props` (release workflow reads it for tags/`HELIX_VERSION`).

## Architecture (keep boundaries)

| Project | Role |
|---------|------|
| `src/HelixExplorer.Core` | Domain, models, interfaces, settings/session, archives, git CLI, logging — **no** Avalonia/WinForms/COM |
| `src/HelixExplorer.Windows` | `Win*` implementations of Core contracts; shell/FS/COM |
| `src/HelixExplorer` | Avalonia UI, ViewModels, UI adapters (`Avalonia*`), DI composition root |

- Register Windows services via `AddHelixWindowsServices()`, then app services via `AddHelixApplicationServices()` (`HelixServiceRegistration`).
- Do **not** fork DI registration for tests — exercise the real composition root.
- **Per-window scopes:** `WindowHostService` creates one `IServiceScope` per window. Scoped VMs (`MainWindowViewModel`, `HomePageViewModel`, …) are window-local. On window close, use the **captured** VM instance; never re-resolve `MainWindowViewModel` from the scope.
- Prefer Core `I*` interfaces from UI/ViewModels; put Win32/shell details in `HelixExplorer.Windows`.

## Coding conventions

- **Performance first:** optimize hot paths (directory listing, filtering, sorting, thumbnails, watchers, file ops). Avoid unnecessary allocations, sync-over-async on the UI thread, and work that blocks browsing. Measure before micro-optimizing cold paths; never regress listing or navigation speed without a clear reason.
- **Comments explain why, not what or how.** Comment non-obvious intent, trade-offs, invariants, and constraints (e.g. why WinForms is required for drag-out). Do not narrate the code (`// increment i`, `// call Save`). If the “why” is already clear from naming and structure, skip the comment.
- `TreatWarningsAsErrors` is on — fix warnings; do not disable it.
- Nullable enabled; match nearby style (primary constructors, file-scoped namespaces).
- ViewModels: CommunityToolkit.Mvvm (`ObservableObject`, `[RelayCommand]`, `partial`). Pane behavior belongs in coordinators under `ViewModels/Pane/`, not giant VMs.
- Avalonia: compiled bindings by default (`AvaloniaUseCompiledBindingsByDefault`). Prefer compiled bindings over reflection/dynamic binding.
- Settings/session: `System.Text.Json` with atomic save (temp file then replace) — preserve that pattern.
- Keep fonts embedded (`Assets/Fonts/**`, `Avalonia.Fonts.Inter`) unless explicitly asked to change packaging.
- WinForms exists **only** for native file drag-out (`WinFormsExternalFileDragService` / `CF_HDROP`). Do not expand WinForms UI surface without a strong reason.
- Shell file ops use Vanara (`IFileOperation`); archives use SharpCompress; Git uses the `git` CLI (`CliGitProvider`).

## Runtime paths

| Data | Location |
|------|----------|
| Settings / session | `%AppData%\HelixExplorer\` (`settings.json`, `session.json`) |
| Logs | `%TEMP%\HelixExplorer\logs\{version}\` (rolling, versioned) |

Use `AppPaths` / `AppVersion` instead of hard-coding paths.

## Installer

- Manifest: `installer/helix-explorer.install.yaml` (PolyInstall).
- Payload comes from `publish/win-x64`; do not commit `publish/`, `dist/`, `bin/`, or `obj/`.
- Machine-scope install defaults to Program Files today — change only with an intentional scope/path update in the manifest.

## Testing

- Core logic: `tests/HelixExplorer.Core.Tests`
- DI / ViewModel regressions: `tests/HelixExplorer.ViewModels.Tests` (lifetimes, pane refresh, window close)
- Prefer OmniAssert fluent assertions (`.Must()…`) in Core tests where that style already exists; avoid introducing ambiguous `Assert` usage.
- When changing DI lifetimes, pane refresh, or window close behavior, update or add regression tests and run `dotnet test`.

## Do not

- Commit secrets (`.env`, `secrets.json`, `*.local.json`) or build artifacts.
- Put Avalonia, WinForms, or COM interop into `HelixExplorer.Core`.
- Add dependencies with licenses incompatible with **GPL-3.0-or-later** without explicit discussion.
- Drive-by refactors, unrelated formatting, or docs the user did not ask for.
- Invent new versioning schemes outside `Directory.Build.props`.
- Weaken trimming/AOT experiments into mainline without thorough soak testing (reflection/COM/WinForms make this fragile).

## Definition of done

Before finishing a change:

1. `dotnet build HelixExplorer.sln -c Release` succeeds with zero warnings.
2. `dotnet test HelixExplorer.sln -c Release` passes for touched areas (full suite when DI/architecture changes).
3. Boundaries above still hold (Core stays UI-free; Windows stays behind interfaces).
4. User-facing behavior matches the request; no unrelated files modified.
5. No obvious performance regression on listing, filtering, or UI responsiveness for the touched paths.
6. New comments (if any) document *why*, not *what*/*how*.
