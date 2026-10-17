# AGENTS.md

Instructions for coding agents working in **Helix Explorer** — a Windows file manager (Avalonia + .NET 10). Prefer this file over inventing project conventions. Humans should still use `README.md` and `CONTRIBUTING.md`.

**Speed and performance are the priority.** Prefer efficient enumeration, allocation-conscious code, and UI that stays responsive under large folders and heavy file ops. Do not trade away responsiveness for convenience abstractions.

## Setup and commands

Requires Windows 10/11 and the .NET 10 SDK. The solution is `HelixExplorer.slnx` (the XML solution format — there is no `.sln`).

```powershell
dotnet restore HelixExplorer.slnx
dotnet build HelixExplorer.slnx -c Release
dotnet test HelixExplorer.slnx -c Release
dotnet run --project src/HelixExplorer
dotnet run --project src/HelixExplorer -- --path "C:\Users"
```

Release installer publish (matches CI):

```powershell
dotnet publish src/HelixExplorer/HelixExplorer.csproj -c Release -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o publish/win-x64
```

Bump the product version only in `Directory.Build.props` — the release decision reads it via `scripts/Resolve-ReleaseVersion.ps1`. A deliberate bump to a stable version publishes that exact version; a new `master` commit with no bump mints and publishes the next `preview.N`; re-pushing a commit already covered by a non-draft release is a no-op (ancestor check). See "Releases".

## Architecture (keep boundaries)

| Project | Role |
|---------|------|
| `src/HelixExplorer.Core` | Domain, models, interfaces, and **cross-platform** concrete implementations (settings/session JSON, SQLite app DB, SharpCompress archives, `git` CLI, rolling file logging, undo history, formatting/sorting/filtering/search). **No** Avalonia/WinForms/COM. |
| `src/HelixExplorer.Windows` | `Win*` implementations of Core contracts; shell, filesystem, COM, P/Invoke, STA threading. `net10.0-windows`, `UseWindowsForms=true`. |
| `src/HelixExplorer` | Avalonia UI, ViewModels, UI adapters (`Avalonia*`), DI composition root. |
| `tests/HelixExplorer.Core.Tests` | Core logic tests (`net10.0`). |
| `tests/HelixExplorer.ViewModels.Tests` | DI/ViewModel/UI regression tests (`net10.0-windows`, Avalonia.Headless); also exercises Windows internals via `InternalsVisibleTo`. |

- **Composition root:** `AddHelixApplicationServices()` (`Services/HelixServiceRegistration.cs`) wires *everything*, including a call to `AddHelixWindowsServices()` at the top. Call only `AddHelixApplicationServices()`; do not register Windows services separately. Tests share this exact method.
- **Lifetimes matter.** Singletons for process-wide services (`ISettingsStore`, `IAppDatabase`, `CliGitProvider`, `IArchiveProvider`, `FileVisualService`, `IFileOperationHistory`, `IWindowHostService`). Scoped per window for the window graph (`MainWindowViewModel`, `HomePageViewModel`, `IMessenger`, `IWindowOwnerContext`, `IUiHost`, `IUserDialogService`, `FileOperationReporter`, window/app coordinators). Transient for per-pane coordinators. Pass a scoped dependency into a factory record rather than resolving it from the root.
- **Per-window scopes:** `WindowHostService` creates one `IServiceScope` per window. On window close, use the **captured** VM instance; never re-resolve `MainWindowViewModel` from the scope. Only the last closing window saves the session.
- **Pane VMs are not DI-registered.** Build them with `IPaneViewModelFactory`; build their coordinators with `IPaneCoordinatorFactory`. Each pane gets its own watcher via the scoped `Func<IFileChangeWatcher>` factory — do not inject `IFileChangeWatcher` directly into a pane.
- Prefer Core `I*` interfaces from UI/ViewModels. The only platform type allowed to surface in a Core contract is an opaque `nint` window handle (`IShellContextMenuService`, `IUiHost`); never reference Avalonia, WinForms, or COM types from Core.
- **Platform services should implement a Core `I*` contract.** `WinThemeWatcher` is a known exception — registered as a concrete singleton in `HelixServiceRegistration` because it needs a callback closure; give it a Core interface if another consumer needs to abstract it.

## Threading and platform boundaries

- **Shell/COM work runs on one process-wide STA thread** via `IStaTaskScheduler` (`Win*` implementations call `Shell/StaTaskScheduler.cs` / `STATask.cs`). Shell COM objects are apartment-bound; hop onto the STA scheduler instead of spawning threads.
- **UI work goes through `IUiThreadDispatcher`** (Core abstraction). In the UI project, inject `IUiThreadDispatcher` rather than touching `Dispatcher.UIThread` directly — `NetworkLocationCoordinator` is the reference pattern. Direct `Dispatcher.UIThread` use in `PaneRefreshCoordinator`, `PaneViewModel` (archive-overlay ordering), and `FileVisualService` is a legacy deviation: do not add new call sites, and migrate to the abstraction when you touch those files. Never block the UI thread on I/O or shell work; offload long walks (`Task.Run`) and marshal results back.
- **Never block on async** (`.Result` / `.GetAwaiter().GetResult()`) in UI code or anything that captured a `SynchronizationContext`. The legacy exceptions — `IFileConflictResolver.ResolveSync`, `App.axaml.cs` startup window open, and `WinFileVisualProvider`'s icon fallback — are only defensible because they run off the UI thread; do not add new ones, and prefer a genuinely synchronous path or an await.
- **No `async void` except required Avalonia event-handler signatures.** Any `async void` handler must catch and report through an injected `ILogger` — never `Debug.WriteLine`. The legacy `Debug.WriteLine` handlers in `TabViewModel`, `PaneViewModel`, and the `PaneView` / `MainWindow` / `Omnibar` code-behind are what not to copy.
- **ViewModels must not spawn processes directly.** Launch terminals/editors through Core abstractions (`ITerminalLauncher`, shell services); do not `Process.Start` from a VM (`TabViewModel` does today — route it through a launcher instead).
- **Watchers** raise events on background threads — subscribers must marshal to the UI thread. Debounce with a gate + `CancellationTokenSource` swap, and re-check a disposed flag after awaiting (see `WinVolumeChangeWatcher`, `FileChangeWatcherService`).
- **`CancellationTokenScope`** (Core) is the pattern for debounced/superseding work (browse, filter, search, settings persist). Use it rather than ad-hoc CTS churn.
- Fire-and-forget must go through `FireAndForgetSafe.Run` (logs and swallows cancellation) — not `async void`.

## Coding conventions

- **Performance first:** optimize hot paths (directory listing, filtering, sorting, thumbnails, watchers, file ops). Reuse the existing allocation-conscious primitives — `ArrayPoolList<T>` for pooled enumeration, `SearchValues<char>` for first-char probes, precomputed comparer permutations in `FileSystemEntryComparer`, cached grouping buckets in `FileGrouping`. Avoid unnecessary allocations, sync-over-async on the UI thread, and work that blocks browsing. Measure before micro-optimizing cold paths.
- **Comments explain why, not what or how.** Comment non-obvious intent, trade-offs, invariants, and constraints (e.g. why WinForms is required for drag-out). Do not narrate the code (`// increment i`, `// call Save`). If the “why” is already clear from naming and structure, skip the comment.
- `TreatWarningsAsErrors` is on in `Directory.Build.props` — fix warnings; do not disable it.
- Nullable + `ImplicitUsings` enabled; `LangVersion latest`. Match nearby style: file-scoped namespaces, primary constructors for services/stores, collection expressions `[]`, immutable `readonly record struct`/`sealed` models.
- **ViewModels:** CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`, `partial`). Split cross-cutting pane behavior into coordinators under `ViewModels/Pane/` (`.cs`), not into the large VMs. Keep new pane behavior out of `PaneViewModel`/`MainWindowViewModel` where a coordinator fits; coordinators read/mutate VM state through a host interface (see `IPaneRefreshHost`). Extract pure decision logic into testable helper classes (`PaneNavigationController`, `PaneSelectionModel`, `ListingPipeline`, `Input/RenameKeyGesture`).
- **Messaging:** `IMessenger` is scoped per window and is a `WeakReferenceMessenger`. Define messages as `record` types in `ViewModels/Messages.cs`.
- **Settings persistence:** `AppSettingsCoordinator.ScheduleSave` is debounced and applies only mutated fields through `ISettingsStore.Update` (field-level last-write-wins across windows). Do not overwrite the whole settings document from a window.
- **Avalonia:** compiled bindings by default (`AvaloniaUseCompiledBindingsByDefault`). Every view/control root declares `x:DataType`. `x:CompileBindings="False"` is only for `ComboBox` `SelectedValueBinding` over untyped `Tag`; reflection `Binding` is reserved for programmatically built Miller-column visuals.
- **Converters** live in `Converters/`, are `sealed`, expose a static `Instance` when used from code-behind, and throw `NotSupportedException` from `ConvertBack`. Theme-sensitive converters bind the `HelixThemeRevision` resource so they re-run on System theme flips.
- **Custom controls** are `sealed`, register styled properties with `AvaloniaProperty.Register`, set `StyleKeyOverride` when deriving, and unsubscribe in `OnDetachedFromVisualTree`. Styles live in `Styles/FilesTheme.axaml`, icon geometries in `Styles/IconResources.axaml`, theme brushes/layout constants in `App.axaml` (`Helix*` resource keys).
- **User-facing strings** shared across the UI go in `Localization/UiStrings.cs`.
- **Settings/session files:** `System.Text.Json` with atomic save — write a unique `*.tmp` then `File.Move(tmp, path, overwrite: true)` — under an in-process lock plus a cross-process named mutex, and expose read-modify-write `Update` rather than blind overwrite. Preserve that pattern.
- **SQLite (`IAppDatabase`):** one shared connection, a single `ConnectionGate` lock for all store commands, WAL mode, and schema versioning via `PRAGMA user_version` (no silent schema drift). Stores own their SQL under that gate.
- Keep fonts embedded (`Assets/Fonts/**`, `Avalonia.Fonts.Inter`, `DMSans`) unless explicitly asked to change packaging.
- **WinForms exists only for native file drag-out** (`WinFormsExternalFileDragService` / `CF_HDROP`). Because the Windows project sets `UseWindowsForms=true`, `System.Windows.Forms` is a project-wide implicit using — do not let WinForms types leak anywhere else.
- **Shell file ops:** delete-to-recycle, elevated permanent delete, and Recycle Bin restore go through Vanara `IFileOperation` (`ShellFileOperationsHelper`); copy, move, rename, and create-folder use `System.IO`. Surface failures as HRESULTs (`FileSystemErrorMessages`), not raw exception text. Archives use SharpCompress; Git uses the `git` CLI (`CliGitProvider`).
- Keep custom-control/view code-behind to interaction only; put behavior in ViewModels/coordinators.

## Runtime paths

| Data | Location |
|------|----------|
| Settings / session | `%AppData%\HelixExplorer\` (`settings.json`, `session.json`) |
| App database | `%AppData%\HelixExplorer\` (`helix.db` — per-folder view preferences and colors via `IAppDatabase`) |
| Logs | `%TEMP%\HelixExplorer\logs\{version}\` (rolling, versioned) |
| Archive/scratch temp | `%TEMP%\HelixExplorer\` (`AppPaths.TempRoot`; cleaned on shutdown) |

Debug builds (F5 / `dotnet run`) and any process with `HELIX_DEV_PROFILE=1` use the sibling `HelixExplorer.Dev` folder for AppData, logs, and archive temp so they cannot overwrite an installed Release profile. Installed Release builds keep `%AppData%\HelixExplorer\`.

Use `AppPaths` / `AppVersion` instead of hard-coding paths. Do not write anything durable under `AppPaths.TempRoot` — it is wiped on shutdown.

## Releases

- `.github/workflows/release-on-master.yml` and `release-on-tag.yml` decide *what* to publish (stable vs. the next `preview.N`, `make_latest`); `release-installers.yml` is a `workflow_call`-only publisher that *builds and ships* a version it is told to. Both callers share `scripts/Resolve-ReleaseVersion.ps1` for parsing, preview-increment, and `make_latest` logic — do not duplicate that logic inline in a workflow. (The props bump/commit/push/retry loop is still duplicated inline in both callers — keep it that way if you touch it, or extract it deliberately.)
- The decision is derived from already-published GitHub releases (ancestor-of-a-non-draft-release check), never from a commit diff, so a re-run of an already-released commit is a safe no-op. Note that `release-on-master.yml` hard-resets to `origin/master` before deciding, so a `workflow_dispatch` from a branch evaluates current `master`, not that branch's commit or `Directory.Build.props`.
- `build-and-test.yml` runs on **every** push/PR to `master`; it is not path-filtered. Only the release workflows ignore `**/*.md`, `.github/ISSUE_TEMPLATE/**`, `.github/workflows/**`, and `LICENSE`. **Workflow-only changes are never exercised by pushing to `master` in this repo** — test them on a scratch fork.
- To force a republish: delete the GitHub release for that version (the tag can stay) then re-run/re-dispatch. This only reliably works for the **latest** release — if any later non-draft release also contains the commit, the ancestor check still no-ops.
- Releases are **not** gated on `build-and-test.yml`; a published release does not imply tests passed. Preview tags never sync `Directory.Build.props` back to `master`; stable tags do.

## Installer

- Manifest: `installer/helix-explorer.install.yaml` (PolyInstall). Validate changes with `polyinstall validate installer/helix-explorer.install.yaml --base .` before release (CI only validates at publish time).
- Install scope is **user** (`install_scope: user`) under `{LocalAppData}/Helix Explorer`. Changing scope/path is an intentional manifest update.
- Payload comes from `publish/win-x64`; `version` defaults to `0.1.0` unless `HELIX_VERSION` is set (CI always sets it). Do not commit `publish/`, `dist/`, `bin/`, or `obj/`.

## Testing

- Core logic: `tests/HelixExplorer.Core.Tests` (`net10.0`). DI/ViewModel/UI regressions: `tests/HelixExplorer.ViewModels.Tests` (`net10.0-windows`, Avalonia.Headless). CI is Windows-only.
- Stack is xUnit + **OmniAssert** (both projects have `global using OmniAssert;`). Prefer `.Must()…` assertions; use `Ensure.ThrowsAsync<T>` for async throws. Avoid introducing other assertion libraries.
- Name tests `Subject_Condition_Expected`/`Behavior_Context_Outcome`; use `[Theory]`/`[InlineData]` for tables. Model shared provider behavior as an abstract contract class (`IFileSystemProviderContractTests`).
- **Exercise the real composition root** via `AddHelixApplicationServices()`; build providers with `validateScopes: true`. Do not fork registration. For graph tests, override *boundary* services with last-registration-wins — and always override **both** `IAppDatabase` **and** `ISettingsStore` (plus network discovery) so tests never read/write the developer's real `%AppData%\HelixExplorer`. Unit tests may construct VMs/coordinators directly.
- Anything touching Avalonia objects or `Dispatcher.UIThread` must build/show/measure/arrange/dispose on the headless UI thread (`HeadlessSession.RunOnUiThread`). The real `HelixExplorer.App` is loaded via `HeadlessAppBuilder`.
- Prefer injected clocks (`Func<DateTime>`) and schedulers (`ManualStatusCentreScheduler`) over wall-clock `Task.Delay`; do not add timing-based performance assertions to the normal suite.
- When changing DI lifetimes, pane refresh, or window close behavior, update or add regression tests (`DiAndRefreshRegressionTests.cs`) and run `dotnet test`.

## Do not

- Commit secrets (`.env`, `secrets.json`, `*.local.json`) or build artifacts.
- Put Avalonia, WinForms, or COM interop into `HelixExplorer.Core`.
- Add projects outside `HelixExplorer.slnx` or invent alternate build/solution files.
- Add dependencies with licenses incompatible with **GPL-3.0-or-later** without explicit discussion.
- Drive-by refactors, unrelated formatting, or docs the user did not ask for.
- Invent new versioning schemes outside `Directory.Build.props`.
- Weaken trimming/AOT experiments into mainline without thorough soak testing (reflection/COM/WinForms make this fragile).

## Definition of done

Before finishing a change:

1. `dotnet build HelixExplorer.slnx -c Release` succeeds with zero warnings.
2. `dotnet test HelixExplorer.slnx -c Release` passes for touched areas (full suite when DI/architecture changes).
3. Boundaries above still hold (Core stays UI-free; Windows stays behind interfaces; no new WinForms beyond drag-out).
4. User-facing behavior matches the request; no unrelated files modified.
5. No obvious performance regression on listing, filtering, or UI responsiveness for the touched paths.
6. New comments (if any) document *why*, not *what*/*how*.
