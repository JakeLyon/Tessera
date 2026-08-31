<p align="center">
  <img src="docs/tessera.ico" width="96" alt="Tessera">
</p>

<h1 align="center">Tessera</h1>

<p align="center"><strong>See what's eating your disk. Reclaim it in minutes.</strong></p>

Tessera is a fast disk space analyzer for Windows 10 and 11. It scans a drive or folder and turns it into a picture you can actually read: every file becomes a rectangle sized by the space it takes, so the things worth deleting are the things you notice first. A full 1.1-million-file `C:` scan finishes in around 20 seconds.

No installer, no account, no telemetry. One file, and it runs.

![Tessera showing a full 569 GB C:\ drive — 1.17 million files in one view](docs/screenshot.png)

**[Download the latest release →](https://github.com/JakeLyon/Tessera/releases/latest)**

## Features

- **Fast parallel scanning** — one worker per core over a shared directory queue; a full 1.1M-file `C:\` scan takes ~20 s cold / ~4 s warm. Live file/byte counters while scanning, cancellable at any time.
- **Squarified treemap** (Bruls et al.) — rectangle area ∝ file size, coloured by nesting depth or by file extension, click to select, double-click to drill in, Up button and breadcrumb to navigate back. Rendering is cached to a bitmap, so hover/selection is instant even on dense views.
- **Size-sorted tree** with size and %-of-parent columns (virtualized `TreeDataGrid`, handles 10k-child folders smoothly).
- **Context menu** — open in Explorer, copy path, delete to Recycle Bin, rescan a single subtree (sizes re-propagate to the root without a full rescan), top-100 files under a folder.
- **No detail settings** — everything large enough to see is drawn, always. There is no cap and nothing is grouped away, so the picture is never quietly incomplete; the only cutoff is a rectangle too small to put a pixel in. Detail past that is reached by drilling into a folder, which lays it out again in the whole pane.
- **Colour by depth or by file type** — **View ▸ Colour**. Depth is the default: the hue tells you how deeply nested a box is, so the folder structure reads at a glance. Switch to file type to colour by extension instead, when what you want is to spot one kind of file across the drive.
- **Free space** — **View ▸ Show free space** gives unused space its own block, so the picture is the whole drive rather than only the full part of it.
- **Top 100 largest files** — flat list for quick wins, double-click to reveal in Explorer.
- **Safe by construction** — junctions/symlinks are shown but never followed (no cycles, no double-counting); access-denied folders are flagged and counted, never fatal.
- **Headless CLI mode** — `Tessera.exe --scan <path>` prints totals without opening a window.
- **Help ▸ About** — version (with the build's git revision), the licence, and the full third-party notices, all embedded in the exe so they travel with it.

## Requirements

- Windows 10/11 (the scanner and shell integration are Windows-first; the UI stack is cross-platform Avalonia)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build; .NET 10 Desktop Runtime to run the framework-dependent build

## Build & run

```powershell
dotnet build -c Release
dotnet run --project src/Tessera/Tessera.csproj                      # open the UI
dotnet run --project src/Tessera/Tessera.csproj -- "D:\some\folder"  # open and scan immediately
dotnet run --project src/Tessera/Tessera.csproj -- --scan "C:\"      # headless: print totals and exit
```

`--scan` exits `0` on success, `2` on a usage error (missing or blank path) and `3` when the scan itself fails. The UI exits `1` if it cannot start.

### Scripting `--scan` — read this before automating it

Tessera is a `WinExe` (GUI subsystem), so it has no console of its own. It borrows the parent's console via `AttachConsole`, which is enough to read output interactively — but **shells do not wait for a GUI-subsystem process**. Calling it directly returns immediately, before it has written anything:

```powershell
$out = & .\Tessera.exe --scan "C:\"    # ✗ returns instantly; $out empty, $LASTEXITCODE unset
```

Two invocations are verified to work from PowerShell. Piping makes PowerShell read to EOF, which means it waits:

```powershell
.\Tessera.exe --scan "C:\" | Out-Host   # ✓ prints totals, sets $LASTEXITCODE
```

And to capture output and the exit code, wait explicitly:

```powershell
$out = New-TemporaryFile
$p = Start-Process .\Tessera.exe -ArgumentList '--scan','C:\' `
        -NoNewWindow -Wait -PassThru -RedirectStandardOutput $out
"exit=$($p.ExitCode)"; Get-Content $out
```

**Known limitation:** plain file redirection (`> totals.txt`) captures nothing, in either PowerShell or `cmd.exe`, and `cmd.exe` has no working equivalent of the two patterns above — neither `start /wait` with redirection nor piping captures the output. The proper fix is to ship a small console-subsystem companion executable for CLI use, which is why tools in this position usually ship two binaries. Until then, treat `--scan` as PowerShell-only for automation.

## Publish

Framework-dependent single file (~27 MB, needs the .NET 10 runtime on the target machine):

```powershell
# Name the project explicitly — publishing the solution also picks up Tessera.Tests,
# which cannot be single-file published (NETSDK1098).
dotnet publish src/Tessera/Tessera.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:DebugType=none
```

Add `--self-contained true` instead for machines without the runtime (~90 MB). `PublishTrimmed` is not supported by Avalonia — don't enable it.

## Tests

Three layers, all runnable without elevation:

```powershell
dotnet test
```

| Layer | What it covers |
|---|---|
| Unit (`tests/Tessera.Tests/Unit`) | Treemap layout invariants (area conservation, no overlap, proportionality — including seeded property tests over random trees), tree mutations, top-K selection vs a LINQ oracle, formatting, color hashing, shell-argument safety and the context-menu state matrix. Nothing here touches the disk or needs an application. |
| Integration (`tests/Tessera.Tests/Integration`) | The scanner against real temp directories: exact counts, hidden/system files, unicode names, junctions (including a deliberate cycle), deny-ACL folders, cancellation, injected worker failures (the scan must always terminate), the `--scan` CLI in-process and as a real child process (totals and exit codes), free-space lookup, and the ShellOps paths that start a real process |
| Headless UI (`tests/Tessera.Tests/Headless`) | Avalonia.Headless: treemap hit-testing and mouse events, tree↔treemap selection sync, drill/up navigation, Top-100 window, colour modes and the free-space block, the delete and rescan flows across their async boundaries, that a big layout renders repeatedly without killing the render pass, that the About window's embedded licence and notices are present and attribute every bundled component, and that a failing handler reports instead of terminating the process. Only tests that genuinely need a window live here. |

The suite never deletes anything it didn't create; fixtures build under `%TEMP%\TesseraTests_*` and clean up after themselves through one shared `TempDir`, which lifts deny-ACEs, removes junctions before any recursive walk, clears attributes and retries once. Recycle-bin deletion is intentionally left to manual testing.

## Architecture

```
Tessera.slnx                       solution
src/Tessera/                       the application
  Program.cs                       entry point; --scan CLI mode
  Models/FsNode.cs                 lean scan-tree node (~70 B + name per entry; millions of files fit in RAM)
  Models/FsTreeOps.cs              UI-free tree mutations: delete splice, rescan splice, top-K
  Scanning/Scanner.cs              parallel enumerator-based scanner + aggregate/sort post-pass
  Scanning/ScanProgress.cs         lock-free counters, polled by the UI on a timer
  Treemap/Squarify.cs              pure squarified-treemap layout algorithm + its geometric cutoffs
  Treemap/TmRect.cs                one laid-out rectangle
  Treemap/TreemapColorMode.cs      colour by nesting depth or by file extension
  UI/TreemapControl.cs             custom control: cached layout + cached scene bitmap, hit-testing
  UI/MainWindow.cs                 fields, seams, the error funnel, and the window's construction
  UI/MainWindow.Scanning.cs        drive list, folder picking, the scan lifecycle
  UI/MainWindow.Selection.cs       tree source, two-way selection sync, drill/up navigation
  UI/MainWindow.Menus.cs           menu bar and context menu
  UI/MainWindow.Operations.cs      delete, rescan, and the refresh after a mutation
  UI/TopFilesWindow.cs             top-100 largest files list
  UI/ConfirmDialog.cs              hand-rolled modal confirm/message window
  UI/DeleteRequest.cs              what a delete confirmation is being asked about
  UI/AboutWindow.cs                version, licence and third-party notices, from embedded resources
  UI/CrashHandler.cs               turns an unexpected exception into something readable
  Util/Format.cs                   byte/percent formatting
  Util/ShellOps.cs                 SHFileOperationW recycle-bin delete, Explorer reveal
  Util/ShellResult.cs              the outcome of one shell operation
  Util/DiskSpace.cs                free bytes on a drive, for the free-space block
tests/Tessera.Tests/               unit, integration and headless-UI layers
docs/                              icon, screenshot, marketing copy
LICENSE, THIRD-PARTY-NOTICES.txt   shipped beside the exe and embedded in it
```

Design notes:

- **Sizes are apparent sizes** (`FileSystemEntry.Length`), matching Explorer's "Size", not "Size on disk".
- Full paths are never stored — they're rebuilt on demand by walking parent links; the root node holds the absolute path.
- Every `Children` array is kept sorted by size descending; the treemap layout and the tree view both rely on this invariant, and `FsTreeOps` preserves it through every mutation.
- `Avalonia.Controls.TreeDataGrid` is pinned to **11.1.1** simply because that is the version this was built and tested against. It is **MIT at every version** — the licence text lives in `licence.md` in the upstream repo; the NuGet packages just carry no licence metadata, which is why automated scanners report it as "unknown". The upstream repo is **archived**, so treat it as a frozen dependency.
- **Nothing fails silently.** The UI runs on `async void` event handlers, where a throw is unhandled by definition, so every menu and toolbar action goes through `MainWindow.Guarded` and lands in the status bar. `Dispatcher.UIThread.UnhandledException` catches whatever slips past and keeps the window alive. Nothing is written to disk — the app reports and carries on.
- The scanner's workers share a pending-directory counter, and a worker that fails without decrementing it strands the rest in a spin loop. The decrement lives in a `finally` for that reason; `Scanner.OnDirectoryEnter` exists so the case stays regression-tested.
- The layout has no rectangle budget, only geometry. `Squarify.MinSide` is 1 and is gated as `MinSide + 2` to offset the per-level `Deflate(1)`, so the descent stops at 3px — the narrowest rectangle that still leaves an inner pixel to draw. Below that there is nothing to show, which is why no cap is needed: the screen is the limit. `MaxDepth` is a sanity bound against pathological nesting, not a detail setting.
- Colour mode and the free-space toggle are **session-only** by design — the app writes nothing outside its own folder.
- Because a drive lays out several hundred thousand rectangles, hover cost must not scale with their number. `TreemapControl` indexes each layout twice: a `Dictionary<FsNode, int>` for the two per-frame outline lookups, and a 32px bucket grid (flat CSR arrays, not a list per cell) for hit-testing. `HitTestLinear` is kept as the definition of correct, and a test asserts the grid agrees with it point for point.

## Licence

Tessera is proprietary; see [LICENSE](LICENSE) for terms.

Every third-party dependency is MIT-licensed bar two — the ANGLE natives are BSD-3-Clause and the embedded Inter typeface is under the SIL Open Font Licence — and all three licences require their notices to travel with any distributed copy. [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) carries them.

A single self-contained exe gets moved on its own, away from whatever was sitting beside it, so the notices are **embedded in the binary** as well as copied into the output folder. **Help ▸ About** reads them straight out of the assembly and never touches the filesystem, which is what makes a bare `Tessera.exe` a compliant distribution in its own right. Headless tests assert both resources are present and name every bundled component, so dropping them breaks the build rather than a shipped download.

One case is worth knowing about beyond that: the TreeDataGrid packages declare no licence metadata at all, so automated scanners report them as unknown — the grant is MIT, from `licence.md` in the upstream repo.
