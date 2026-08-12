# Tessera — marketing copy

Reusable copy for a website, store listing, or release announcement. Every factual
claim here is grounded in what the app actually does; the performance figure comes
from a real 1.1M-file `C:` scan. **Re-measure before quoting it on a paid landing
page** — it will differ by machine, and an inflated benchmark is the fastest way to
lose trust with this audience.

---

## Tagline options

1. **See what's eating your disk. Reclaim it in minutes.** *(primary)*
2. Your hard drive, finally legible.
3. Find the big files. Delete them safely. Move on.
4. Disk space, at a glance.

## One-liner (160 chars, meta description)

> Tessera is a fast disk space analyzer for Windows. Scan any drive in seconds, see exactly what's using your space, and delete it safely from one view.

## Short description (app store / directory listing)

Tessera shows you where your disk space went.

Point it at a drive or folder and it maps every file to a rectangle sized by the
space it occupies. The 40 GB thing you forgot about stops being buried twelve
folders deep and becomes the biggest block on screen. Select it, check the path
and size, and send it to the Recycle Bin without leaving the app.

Built for speed: a parallel scanner uses every core on your machine and reads a
1.1-million-file drive in about 20 seconds.

## Long description (landing page)

### Your C: drive is full and Explorer won't tell you why

Windows tells you how much space is left. It won't tell you what took it. Finding
the culprit by hand means opening folder after folder, checking properties,
guessing. Tessera does it in one pass.

### A map, not a spreadsheet

Tessera draws your disk as a **treemap**: every file is a rectangle, and the
rectangle's area is proportional to its size. Big files are big shapes. You don't
have to read anything to find them — they're simply the largest things on screen,
colour-coded by file type.

A conventional size-sorted folder tree sits alongside it, and the two stay in sync
both ways. Click a block in the map, the tree jumps to it. Select a folder in the
tree, the map highlights it. Double-click to drill in; one button to come back out.

### Fast enough to actually use

Tessera scans with one worker per CPU core over a shared queue, pulling sizes
straight from the OS directory entries rather than opening every file. A full
1.1-million-file `C:` drive takes around **20 seconds cold and 4 seconds warm**.
Counters tick up live while it works, and you can cancel at any point and keep
the partial result.

### Delete with confidence, not hope

Deleting from a disk cleaner should never be the scary part. Tessera confirms
every deletion with the **full path and size** in front of you, and sends files to
the **Recycle Bin**, not oblivion. Rescan a single folder after a clear-out and the
totals re-propagate to the root without re-reading the whole drive.

### Honest about what it shows you

- Junctions and symlinks are displayed but never followed, so nothing is counted
  twice and no loop can hang a scan.
- Folders Windows won't let it read are flagged and counted rather than quietly
  appearing empty.
- When a display limit hides part of the view, Tessera says so on screen rather
  than silently under-reporting your disk.

### No install, no account, no telemetry

One self-contained executable. Nothing is installed, no runtime is required, no
data leaves your machine, and nothing is written outside the app's own folder.
Copy it to a USB stick and run it on any Windows 10 or 11 PC.

### For scripts and servers

`Tessera.exe --scan "C:\"` prints totals and exits without opening a window, with
proper exit codes for automation.

---

## Feature bullets (for a features grid)

- **Squarified treemap** — file size becomes rectangle area, coloured by type
- **Two-way sync** — treemap and folder tree always agree
- **Parallel scanner** — one worker per core; ~20 s for 1.1M files
- **Recycle Bin delete** — confirmed with full path and size, always recoverable
- **Single-folder rescan** — no full re-scan after a clear-out
- **Top 100 largest files** — the quick wins, in one list
- **Adjustable detail** — trade rectangle count for responsiveness on huge drives
- **Headless CLI** — `--scan` for scripting, with real exit codes
- **Portable** — one file, no installer, no runtime, no telemetry

---

## SEO notes

**Primary keyword:** disk space analyzer
**Secondary:** disk usage analyzer Windows, what is taking up space on my hard drive,
find large files Windows, free up disk space, visualize disk usage, treemap disk,
C drive full, disk cleanup tool, portable disk space analyzer

**Comparison terms** people actually search — worth a comparison page each:
WinDirStat alternative, TreeSize alternative, SpaceSniffer alternative,
SpaceMonger alternative, WizTree alternative, DaisyDisk for Windows.

**Title tag (≤60 chars):**
> Tessera — Disk Space Analyzer for Windows

**Meta description (≤160 chars):**
> Find what's using your disk space in seconds. Tessera maps any Windows drive as a
> treemap, then deletes safely to the Recycle Bin. Free, portable, no install.

**Guidance**
- Lead with the problem ("C: drive full", "what's taking up space"), not the
  technique. Almost nobody searches for "treemap"; a great many people search for
  "why is my hard drive full".
- Name the competitors on comparison pages. That is where the search volume is,
  and Tessera's honest differentiators are real: portability, no telemetry, and
  surfacing when the view is incomplete.
- Do not claim "fastest" or benchmark against a named competitor without a
  reproducible measurement to publish. In this category the audience is technical
  and will check.
- The 20 s / 4 s figures need a stated test machine and dataset beside them, or
  drop them. An unqualified benchmark reads as marketing noise.
