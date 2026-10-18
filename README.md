# DiskMonitor（SSDTool）— 低开销磁盘读写监控器

[![build](https://github.com/Gusion1017/SSDTool/actions/workflows/build.yml/badge.svg)](https://github.com/Gusion1017/SSDTool/actions/workflows/build.yml)
![platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![license](https://img.shields.io/badge/license-MIT-green)

基于 **WPF + ETW（Windows 内核事件追踪）** 的磁盘读写监控器：按**进程**持续统计磁盘**读取 / 写入字节数**，提供开始 / 停止记录、实时速率波形图、累计折线图、饼图 + 进程列表、记录时长、历史记录、中英文切换与白天 / 夜间主题；并可对 **System（PID 4）** 的内核 I/O 进一步**按文件**和**按物理磁盘**细分。

A disk read/write monitor built on **WPF + ETW (Windows kernel event tracing)**. It tracks **read/write bytes per process**, with start/stop recording, a live throughput waveform, a cumulative line chart, a pie chart + process list, recording duration, history, Chinese/English UI, and light/dark themes. **System (PID 4)** kernel I/O can be further broken down **by file** and **by physical disk**.

> 设计目标：**对系统干扰最小**。采集完全在内核侧完成，不注入进程、不挂 Hook、不轮询 WMI / 性能计数器。
>
> Design goal: **minimal system interference.** Collection happens entirely in the kernel — no process injection, no hooks, no WMI/performance-counter polling.

> 本文件同时包含**简体中文**与 **English** 两个版本，向下滚动即可查看对应内容。
> This README contains **both Simplified Chinese and English**; scroll down for your language of choice.

---

## 简体中文

### 功能特性

- **按进程统计**磁盘读取 / 写入字节数（含“开始记录前就已运行”的进程）。
- **实时可视化**：读写速率波形图、累计折线图、饼图 + 进程列表（饼图扇区与列表色块同色）。
- **System 细分**：工具栏「System 细分」或双击列表中的 System 行，在右侧栏查看内核 I/O
  按**文件**聚合（路径、字节、占比）与按**物理磁盘**聚合（盘号 + 盘符）。
- **历史记录**：点「停止记录」自动写入一条历史，点击可回看该次会话的进程明细，可删除单条 / 清空全部。
- **工具栏**：记录时长、字节单位切换（自动 / B / KB / MB / GB）、中英文切换、白天 / 夜间主题。
- **低残留**：正常停止即真正关闭内核会话；异常退出残留会在下次「开始记录」时自动清理。

### 环境要求

| 用途 | 要求 |
|---|---|
| 运行 | Windows 10 / 11 x64，**管理员权限**（首次运行弹 UAC） |
| 下载版运行 | 无需安装 .NET（自包含发布已内置运行时） |
| 从源码构建 | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |

### 快速开始

#### 方式一：从 GitHub Actions 下载构建产物（推荐，免装 SDK）

1. 打开 [Actions · build](https://github.com/Gusion1017/SSDTool/actions/workflows/build.yml)；
2. 选择最新一次成功运行，在页面底部 **Artifacts** 下载 `DiskMonitor-win-x64`；
3. 解压后双击 `DiskMonitor.exe`，UAC 弹窗点「是」。

#### 方式二：本地从源码构建并运行

```bat
:: 需要已安装 .NET 8 SDK
build-and-run.cmd
```

或手动执行：

```bat
dotnet build src\DiskMonitor.csproj -c Release
dotnet run --project src\DiskMonitor.csproj -c Release --no-build
```

#### 方式三：本地发布自包含版本（免装 .NET 分发）

```bat
dotnet publish src\DiskMonitor.csproj -c Release -r win-x64 --self-contained true -o publish
```

发布完成后 `publish\DiskMonitor.exe` 即可脱离 .NET 运行时独立运行；
之后双击项目根目录的 `启动磁盘监控.bat` 直接启动（脚本会检查 `publish\DiskMonitor.exe` 是否存在）。

> 本仓库**只提交源码**，不包含 `publish/` 自包含产物（约 166 MB，474 个文件），请按上面任一方式获取可执行文件。

### 使用说明

1. 启动程序，UAC 弹窗点「是」；
2. 点击 **▶ 开始记录**；
3. 观察界面：

   | 区域 | 内容 |
   |---|---|
   | 波形图（左） | 实时读写速率（B/s）随时间波动 |
   | 累计折线图（右） | 累计读取 / 写入字节数 |
   | 饼图 + 右侧列表 | 读取 / 写入全部进程分布；饼图内扇区显示进程名（过小扇区不显示，悬停查看详情），列表每行带同色色块 |
   | 工具栏 | 记录时长、字节单位、语言（中文 / English）、主题（白天 / 夜间） |

4. **System 细分**：点工具栏「System 细分」按钮（更稳），或双击读取 / 写入列表中的 **System** 行；
   右侧栏「按文件」「按磁盘」两个页签每秒随记录刷新，不遮挡工具栏、不改变左侧图表尺寸。
5. 点击 **■ 停止记录** 冻结统计并写入一条历史；**↺ 重置统计** 清空当前累计（不影响历史）；
   再次点 **▶ 开始记录** 会自动清零并开始新统计。
6. **点击历史记录行**在右侧栏查看该次会话明细（饼图 + 同色列表），可删除单条；列表上方「清空全部」一键清空。
   点右上角 **✕** 或按 `Esc` 收起侧栏。
7. 历史记录保存在 `%LocalAppData%\DiskMonitor\history.json`，跨次运行保留。

### 工作原理（为什么“对系统干扰小”）

- 使用 Windows **ETW 内核事件追踪**（`Microsoft-Windows-Kernel-Disk`），与 Process Monitor、资源监视器同源；
- 采集完全在**内核侧**完成：不注入进程、不挂 Hook、不轮询 WMI / 性能计数器，无轮询风暴；
- 事件由微软官方库 `Microsoft.Diagnostics.Tracing.TraceEvent` 实时消费；UI 每秒仅刷新一次，且只在“记录中”刷新；
- 「停止记录」会真正停止内核会话并释放全部追踪资源。

### 关键实现

- 内核磁盘事件（`DiskIo`）只携带线程 ID 与传输字节数，不含进程 ID；程序通过
  `Process/Thread` 关键字事件 + Toolhelp32 快照维护「线程 → 进程 → 进程名」映射，把每次 I/O 归属到具体进程。
- **System 细分**：System（PID 4）是内核线程宿主，没有“发起它的应用进程”，因此换维度拆分：
  额外开启 `DiskFileIO` 关键字获取 `FileKey → 文件名` 映射，按文件聚合；同时用磁盘事件自带的
  `DiskNumber` 按物理磁盘聚合。文件名会尽量把 `\Device\HarddiskVolumeN`、`\SystemRoot\` 转成盘符路径。
- 启动时清理上次异常退出遗留的内核会话（自愈，避免“会话已被占用”）。

### 对系统的影响与残留

- **运行期间**：仅内核采集 + 一个后台线程，不改文件、不写注册表、不注入进程、不创建服务、不设自启；
- **正常停止**（点「停止记录」或关闭窗口）：内核会话被真正停止，无残留；实时统计只存内存，退出即清空
  （仅历史记录写入本地 JSON）；
- **异常退出**（被强杀 / 崩溃 / 断电）：会话可能残留，但下次「开始记录」会自动清理；
  也可管理员命令行执行 `logman stop "NT Kernel Logger" -ets` 手动清理。

### 已知限制

- 实时内核会话 `NT Kernel Logger` 是系统级单例；「开始记录」会独占接管它（会自动停掉 PerfView / WPR 等正在进行的采集）；
- 极端 I/O 压力下内核可能丢弃少量事件（ETW 自身保护机制，属正常现象）；
- 统计的是**磁盘 I/O 请求字节数**（含页文件、缓存回写等内核层面落到磁盘的 I/O），与文件级“应用层读写”口径略有差异；
- System 细分里，页文件、NTFS 元数据、驱动内部 I/O 以及“开始记录前就已打开”的文件可能拿不到文件名，
  会统一归入「未解析文件名」；按磁盘维度仍能完整归因；
- System 细分只对**当前实时统计**有效，历史记录仍只保存进程级数据。

### 项目结构

```text
SSDTool/
├── src/                          # .NET 8 WPF 工程（全部源码，约 108 KB）
│   ├── DiskMonitor.csproj
│   ├── DiskIOMonitor.cs          # ETW 采集核心（含 System 文件 / 磁盘细分）
│   ├── NativeMethods.cs          # Toolhelp32 快照 + ETW 会话清理 + 卷 / 磁盘映射
│   ├── TooltipPieSeries.cs       # 饼图悬停详情
│   ├── HistoryStore.cs           # 历史记录持久化
│   ├── Settings.cs               # 语言 / 主题设置持久化 + 多语言辅助
│   ├── PieChart.cs               # 饼图构建复用 + 柔和配色
│   ├── FormatUtil.cs
│   ├── MainWindow.xaml(.cs)      # 主窗口（含右侧栏容器）
│   ├── HistoryDetailView.xaml(.cs)  # 右侧栏：历史记录详情
│   ├── SystemDetailView.xaml(.cs)   # 右侧栏：System 细分（按文件 / 按磁盘）
│   └── app.manifest / App.xaml(.cs)
├── .github/workflows/build.yml   # CI：构建 + 自包含发布 + 上传 Artifact
├── build-and-run.cmd             # 从源码构建并运行（开发者）
├── 启动磁盘监控.bat               # 一键启动本地 publish\DiskMonitor.exe
├── .gitignore
├── .gitattributes
├── LICENSE
└── README.md                     # 中英双语 / bilingual
```

> **右侧栏设计**：历史记录详情与 System 细分都显示在主窗口右侧的**覆盖式侧栏**里，不弹新窗口；
> 侧栏只覆盖右侧内容，左侧图表 / 列表的尺寸与布局不会因展开 / 收起而变化。

### 依赖

| 包 | 版本 | 用途 |
|---|---|---|
| `Microsoft.Diagnostics.Tracing.TraceEvent` | 3.1.9 | 消费 ETW 事件 |
| `OxyPlot.Wpf` | 2.1.2 | 图表（波形图 / 折线图 / 饼图） |
| .NET | 8.0（`net8.0-windows`，WPF） | 运行框架 |

### 构建与测试

```bat
dotnet build src\DiskMonitor.csproj -c Release          :: 编译
dotnet publish src\DiskMonitor.csproj -c Release -r win-x64 --self-contained true -o publish   :: 自包含发布
```

CI（GitHub Actions）会在每次 push / PR 时执行同样的构建与发布，并上传 `DiskMonitor-win-x64` 产物。

### 许可证

[MIT](./LICENSE) © 2026 Gusion1017

### 免责声明

本工具需要管理员权限并使用内核 ETW 会话，请在理解其行为的前提下使用；因使用本工具造成的任何后果由使用者自行承担。

---

## English

### Features

- **Per-process accounting** of disk read/write bytes (including processes that started before recording began).
- **Live visualization**: throughput waveform, cumulative line chart, pie chart + process list (slices and list swatches share colors).
- **System breakdown**: use the **System breakdown** toolbar button (or double-click the System row) to open the right
  sidebar with kernel I/O aggregated **by file** (path, bytes, share) and **by physical disk** (disk number + drive letters).
- **History**: clicking **Stop** stores a session; click a history row to review its per-process detail, delete one entry,
  or clear all.
- **Toolbar**: recording duration, byte-unit switch (Auto / B / KB / MB / GB), Chinese/English switch, light/dark theme.
- **Low residue**: a normal stop really stops the kernel session; leftovers from abnormal exits are cleaned up
  automatically on the next **Start**.

### Requirements

| Purpose | Requirement |
|---|---|
| Run | Windows 10 / 11 x64, **administrator** (UAC prompt on first launch) |
| Prebuilt binary | No .NET install needed (self-contained publish bundles the runtime) |
| Build from source | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |

### Quick Start

#### Option 1: Download the GitHub Actions artifact (no SDK needed)

1. Open [Actions · build](https://github.com/Gusion1017/SSDTool/actions/workflows/build.yml);
2. Pick the latest successful run and download `DiskMonitor-win-x64` from **Artifacts**;
3. Unzip and double-click `DiskMonitor.exe`, then accept the UAC prompt.

#### Option 2: Build and run locally from source

```bat
:: requires the .NET 8 SDK
build-and-run.cmd
```

Or manually:

```bat
dotnet build src\DiskMonitor.csproj -c Release
dotnet run --project src\DiskMonitor.csproj -c Release --no-build
```

#### Option 3: Publish a local self-contained build (distribute without .NET)

```bat
dotnet publish src\DiskMonitor.csproj -c Release -r win-x64 --self-contained true -o publish
```

`publish\DiskMonitor.exe` then runs without any .NET runtime; the `启动磁盘监控.bat` helper at the repository root
launches it (the script verifies that `publish\DiskMonitor.exe` exists).

> This repository contains **source code only**. The `publish/` self-contained output (about 166 MB, 474 files) is not
> committed — obtain the executable with one of the options above.

### Usage

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

### How It Works (why it is lightweight)

- Uses Windows **ETW kernel event tracing** (`Microsoft-Windows-Kernel-Disk`) — the same source as Process Monitor and
  Resource Monitor.
- Collection is **kernel-side**: no process injection, no hooks, no WMI/performance-counter polling, no polling storm.
- Events are consumed in real time by the official `Microsoft.Diagnostics.Tracing.TraceEvent` library; the UI refreshes
  once per second and only while recording.
- **Stop** genuinely stops the kernel session and releases every tracing resource.

### Key Implementation Details

- Kernel disk events (`DiskIo`) carry a thread ID and transferred bytes but **no process ID**. The app maintains a
  thread → process → process-name map from `Process/Thread` keyword events plus Toolhelp32 snapshots, so every I/O
  transfer is attributed to a concrete process (including processes that were already running before recording started).
- **System breakdown**: System (PID 4) hosts kernel threads and has no "originating application process", so the app
  switches dimensions instead: it additionally enables the `DiskFileIO` keyword to build a `FileKey → file name` map and
  aggregates by file, while the disk events' own `DiskNumber` gives aggregation by physical disk. Paths are normalized
  from `\Device\HarddiskVolumeN` and `\SystemRoot\` into drive-letter paths where possible.
- On startup the app cleans up a leftover kernel session from a previous abnormal exit, a self-healing step that avoids
  "session already in use" errors.

### System Impact & Cleanup

- **While running**: kernel collection plus one background thread only. No files modified, no registry writes, no
  process injection, no service created, no autostart entry.
- **Normal stop** (Stop button or window close): the kernel session is really stopped — nothing is left behind. Live
  stats live in memory only (just the history list is written to a local JSON file).
- **Abnormal exit** (killed / crash / power loss): the session may be left behind, but the next **Start** cleans it up
  automatically. You can also run `logman stop "NT Kernel Logger" -ets` from an elevated command prompt.

### Known Limitations

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

### Project Structure

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
└── README.md                     # 中英双语 / bilingual
```

> **Right-sidebar design**: both history details and the System breakdown are shown in a **covering sidebar** on the
> right side of the main window instead of a new window. It covers only the right-hand content, so chart/list sizes and
> layout do not change when it opens or closes.

### Dependencies

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Diagnostics.Tracing.TraceEvent` | 3.1.9 | Consume ETW events |
| `OxyPlot.Wpf` | 2.1.2 | Charts (waveform / line / pie) |
| .NET | 8.0 (`net8.0-windows`, WPF) | Runtime framework |

### Build & Test

```bat
dotnet build src\DiskMonitor.csproj -c Release          :: build
dotnet publish src\DiskMonitor.csproj -c Release -r win-x64 --self-contained true -o publish   :: self-contained publish
```

CI (GitHub Actions) runs the same build and publish on every push/PR and uploads the `DiskMonitor-win-x64` artifact.

### License

[MIT](./LICENSE) © 2026 Gusion1017

### Disclaimer

This tool requires administrator privileges and a kernel ETW session. Use it only if you understand its behavior; you
are responsible for any consequences of using it.
