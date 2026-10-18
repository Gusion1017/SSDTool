# DiskMonitor (SSDTool) — Low-Overhead Disk I/O Monitor

[![build](https://github.com/Gusion1017/SSDTool/actions/workflows/build.yml/badge.svg)](https://github.com/Gusion1017/SSDTool/actions/workflows/build.yml)
![platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![license](https://img.shields.io/badge/license-MIT-green)

A disk read/write monitor built on **WPF + ETW (Windows kernel event tracing)**. It tracks **read/write bytes per
process**, with start/stop recording, a live throughput waveform, a cumulative line chart, a pie chart + process list,
recording duration, history, Chinese/English UI, and light/dark themes. **System (PID 4)** kernel I/O can be further
broken down **by file** and **by physical disk**.

> Design goal: **minimal system interference.** Collection happens entirely in the kernel — no process injection,
> no hooks, no WMI/performance-counter polling.

[简体中文](./README.md) | **English**

---

## Features

- **Per-process accounting** of disk read/write bytes (including processes that started before recording began).
- **Live visualization**: throughput waveform, cumulative line chart, pie chart + process list (slices and list swatches share colors).
- **System breakdown**: use the **System breakdown** toolbar button (or double-click the System row) to open the right
  sidebar with kernel I/O aggregated **by file** (path, bytes, share) and **by physical disk** (disk number + drive letters).
- **History**: clicking **Stop** stores a session; click a history row to review its per-process detail, delete one entry,
  or clear all.
- **Toolbar**: recording duration, byte-unit switch (Auto / B / KB / MB / GB), Chinese/English switch, light/dark theme.
- **Low residue**: a normal stop really stops the kernel session; leftovers from abnormal exits are cleaned up
  automatically on the next **Start**.

## Requirements

| Purpose | Requirement |
|---|---|
| Run | Windows 10 / 11 x64, **administrator** (UAC prompt on first launch) |
| Prebuilt binary | No .NET install needed (self-contained publish bundles the runtime) |
| Build from source | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |

## Quick Start

### Option 1: Download the GitHub Actions artifact (no SDK needed)

1. Open [Actions · build](https://github.com/Gusion1017/SSDTool/actions/workflows/build.yml);
2. Pick the latest successful run and download `DiskMonitor-win-x64` from **Artifacts**;
3. Unzip and double-click `DiskMonitor.exe`, then accept the UAC prompt.

### Option 2: Build and run locally from source

```bat
:: requires the .NET 8 SDK
build-and-run.cmd
```

Or manually:

```bat
dotnet build src\DiskMonitor.csproj -c Release
dotnet run --project src\DiskMonitor.csproj -c Release --no-build
```

### Option 3: Publish a local self-contained build (distribute without .NET)

```bat
dotnet publish src\DiskMonitor.csproj -c Release -r win-x64 --self-contained true -o publish
```

`publish\DiskMonitor.exe` then runs without any .NET runtime; the `启动磁盘监控.bat` helper at the repository root
launches it (the script verifies that `publish\DiskMonitor.exe` exists).

> This repository contains **source code only**. The `publish/` self-contained output (about 166 MB, 474 files) is not
> committed — obtain the executable with one of the options above.

## Usage

1. Launch the app and accept the UAC prompt.
2. Click **▶ Start recording**.
3. Watch the UI:

   | Area | Content |
   |---|---|
   | Waveform (left) | Live read/write throughput (B/s) over time |
   | Cumulative chart (right) | Cumulative read/write bytes |
   | Pie chart + list | Distribution across all processes; slices show process names (tiny slices are hidden — hover for details), and each list row carries the matching color swatch |
   | Toolbar | Recording duration, byte unit, language (中文 / English), theme (light / dark) |

4. **System breakdown**: click the toolbar button (most reliable), or double-click the **System** row in the read or
   write list. The right sidebar refreshes every second without covering the toolbar or resizing the charts.
5. Click **■ Stop** to freeze the stats and store a history entry; **↺ Reset** clears the current totals (history is
   kept); clicking **▶ Start** again clears the totals and starts a new session.
6. **Click a history row** to open that session in the right sidebar (pie chart + matched list). You can delete a single
   entry, or clear all from the list header. Click **✕** or press `Esc` to close the sidebar.
7. History is stored in `%LocalAppData%\DiskMonitor\history.json` and kept across runs.

## How It Works (why it is lightweight)

- Uses Windows **ETW kernel event tracing** (`Microsoft-Windows-Kernel-Disk`) — the same source as Process Monitor and
  Resource Monitor.
- Collection is **kernel-side**: no process injection, no hooks, no WMI/performance-counter polling, no polling storm.
- Events are consumed in real time by the official `Microsoft.Diagnostics.Tracing.TraceEvent` library; the UI refreshes
  once per second and only while recording.
- **Stop** genuinely stops the kernel session and releases every tracing resource.

## Key Implementation Details

- Kernel disk events (`DiskIo`) carry a thread ID and transferred bytes but **no process ID**. The app maintains a
  thread → process → process-name map from `Process/Thread` keyword events plus Toolhelp32 snapshots, so every I/O
  transfer is attributed to a concrete process (including processes that were already running before recording started).
- **System breakdown**: System (PID 4) hosts kernel threads and has no "originating application process", so the app
  switches dimensions instead: it additionally enables the `DiskFileIO` keyword to build a `FileKey → file name` map and
  aggregates by file, while the disk events' own `DiskNumber` gives aggregation by physical disk. Paths are normalized
  from `\Device\HarddiskVolumeN` and `\SystemRoot\` into drive-letter paths where possible.
- On startup the app cleans up a leftover kernel session from a previous abnormal exit, a self-healing step that avoids
  "session already in use" errors.

## System Impact & Cleanup

- **While running**: kernel collection plus one background thread only. No files modified, no registry writes, no
  process injection, no service created, no autostart entry.
- **Normal stop** (Stop button or window close): the kernel session is really stopped — nothing is left behind. Live
  stats live in memory only (just the history list is written to a local JSON file).
- **Abnormal exit** (killed / crash / power loss): the session may be left behind, but the next **Start** cleans it up
  automatically. You can also run `logman stop "NT Kernel Logger" -ets` from an elevated command prompt.

## Known Limitations

- The live kernel session `NT Kernel Logger` is a system-wide singleton; **Start** takes it over exclusively and will
  stop any in-progress PerfView / WPR capture.
- Under extreme I/O pressure the kernel may drop a few events (a built-in ETW protection; this is normal).
- The counters measure **disk I/O request bytes** (including pagefile and cache write-back I/O that reaches the disk at
  the kernel level), which differs slightly from the file-level "application read/write" view.
- In the System breakdown, the pagefile, NTFS metadata, driver-internal I/O and files already open before recording may
  have no resolved file name and are grouped under "unresolved file name". This is a limitation of ETW disk events
  (they carry a `FileKey` but not the name-mapping event), not a statistics error; the per-disk dimension still
  attributes everything.
- The System breakdown only applies to **live** statistics; history entries keep process-level data only.

## Project Structure

```text
SSDTool/
├── src/                          # .NET 8 WPF project (all source, ~108 KB)
│   ├── DiskMonitor.csproj
│   ├── DiskIOMonitor.cs          # ETW collection core (System file/disk breakdown)
│   ├── NativeMethods.cs          # Toolhelp32 snapshots + ETW session cleanup + volume/disk mapping
│   ├── TooltipPieSeries.cs       # Pie hover details
│   ├── HistoryStore.cs           # History persistence
│   ├── Settings.cs               # Language/theme persistence + localization helpers
│   ├── PieChart.cs               # Pie construction helpers + soft palette
│   ├── FormatUtil.cs
│   ├── MainWindow.xaml(.cs)      # Main window (with right-sidebar host)
│   ├── HistoryDetailView.xaml(.cs)  # Right sidebar: history detail
│   ├── SystemDetailView.xaml(.cs)   # Right sidebar: System breakdown (by file / by disk)
│   └── app.manifest / App.xaml(.cs)
├── .github/workflows/build.yml   # CI: build + self-contained publish + artifact upload
├── build-and-run.cmd             # Build and run from source (developers)
├── 启动磁盘监控.bat               # One-click launcher for a local publish\DiskMonitor.exe
├── .gitignore
├── .gitattributes
├── LICENSE
└── README.md / README.en.md
```

> **Right-sidebar design**: both history details and the System breakdown are shown in a **covering sidebar** on the
> right side of the main window instead of a new window. It covers only the right-hand content, so chart/list sizes and
> layout do not change when it opens or closes.

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Diagnostics.Tracing.TraceEvent` | 3.1.9 | Consume ETW events |
| `OxyPlot.Wpf` | 2.1.2 | Charts (waveform / line / pie) |
| .NET | 8.0 (`net8.0-windows`, WPF) | Runtime framework |

## Build & Test

```bat
dotnet build src\DiskMonitor.csproj -c Release          :: build
dotnet publish src\DiskMonitor.csproj -c Release -r win-x64 --self-contained true -o publish   :: self-contained publish
```

CI (GitHub Actions) runs the same build and publish on every push/PR and uploads the `DiskMonitor-win-x64` artifact.

## License

[MIT](./LICENSE) © 2026 Gusion1017

## Disclaimer

This tool requires administrator privileges and a kernel ETW session. Use it only if you understand its behavior; you
are responsible for any consequences of using it.
