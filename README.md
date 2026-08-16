# MyGit — 游戏存档式版本管理 | Game-Save Style Version Manager

[中文](#中文) · [English](#english)

把项目目录当成「游戏进度」来管理：**存档 = 完整快照，读档 = 整体还原**。
Manage a project directory like game progress: **Save = full snapshot, Load = full restore**.

> ⚠️ **只适用于 Windows / Windows only**（WinForms · `net9.0-windows`）：开发与运行都需要 Windows；
> 发布的自包含 exe 适用于 Windows x64（可用其他 RID 发布 arm64/x86 版本）。Linux / macOS 不支持。

---

# 中文

> ⚠️ **只适用于 Windows**（WinForms · `net9.0-windows`），Linux / macOS 不可用。

快照引擎完全自研（纯文件复制，不依赖 git），UI 基于 [DarkTerminalUI](DarkTerminalUI/README.md)
（深色终端风格 + 右侧日志控制台），左侧为游戏风自绘画布。

## 核心规则（游戏存档直觉）

- **必须先新建或导入项目才能存档**：一个项目 = 一个游戏存档位所在的世界。
- **存档默认名 = 项目名 + 时间**（如 `DemoGame示例 2026-01-01 12:30:00`），可自定义。
- **读档是硬重置**：工作区整体还原为该存档的内容。
- **读档前自动恢复点**：若当前有未保存改动，读档前自动创建「自动恢复存档」，防止误操作丢进度，随时可读回来。
- 顶部状态栏实时显示「未保存进度」（文件监控自动刷新，内容以 SHA256 为准）。

## 快速开始

双击 `run.bat`（自动编译并启动；仅编译用 `build.bat`）。
需要 .NET 9 SDK；没有 SDK 的电脑请用「免环境运行」一节发布的 `MyGit.exe`。

**A. 管理一个全新项目**

1. 点 **[新建项目]** 或控制台输入 `new MyProject`；
2. 在项目目录里干活（项目库默认在 `MyGit\Projects\<项目名>\`，`where` 查看路径）；
3. 点 **[存档]** 或控制台输入 `save` 保存进度；
4. 需要回退时选中存档点 **[读档]**（或控制台 `load 1`），自动保留恢复点。

**B. 管理别处已有的项目（导入，原地管理，不移动你的文件）**

1. 点 **[导入项目]** 选文件夹；或直接把文件夹**拖进窗口**；或控制台输入 `import <路径>`；
2. 目录里会原地生成一个 `.mygit` 存档库（现有文件一个都不动），并自动创建第一个存档（导入快照）；
   如果导入的是 Unity 的 `Assets/`（或其子文件夹），存档库会自动放在 Unity 项目根目录（`.mygit-<目录名>`），
   Unity 看不见它；
3. 之后随时点 **[存档]** 存进度、**[读档]** 回退，与新建项目完全一样。

> 想体验完整流程：`dotnet run --project MyGit.csproj -- --demo` 会创建演示项目
> （2 个存档 + 1 处未保存改动），试一次读档即可看到自动恢复存档。

## Unity 项目注意（导入时自动保护）

1. **导入根目录或只导入 Assets 都支持**：
   - 导入**项目根目录**（含 `Assets/` 与 `ProjectSettings/` 的那一层）→ 自动排除
     `Library / Temp / Logs / obj / Build / Builds`（可重建生成物），快照含 Assets + Packages + ProjectSettings；
   - **只导入 `Assets/`（或其子文件夹）** → 外部存档库模式：存档库放在 Unity 项目根目录的
     `.mygit-<目录名>`（如 `.mygit-Assets`），**Unity 完全看不见**，快照只含 Assets 内容；
   - 也可以在根目录导入后用 `exclude add Packages` 等方式只快照 Assets —— 排除目录读档时原样保留；
2. **排除目录读档语义**：排除目录不参与存档、也不受读档影响（删除/还原都跳过），所以
   Library 读档后原样保留、用户设置不会被误删；
3. **存档/读档前关闭 Unity 编辑器**：读档会整体重写被管理目录，Unity 占用中的文件会导致读档失败
   （读档确认框里有提示）。源文件与 `.meta`（GUID 引用）都完整保存在快照里，不会破坏资源引用。

## 界面

- **左侧画布**：项目信息 + 存档卡片列表（序号 / 名称 / 时间 / 文件数 / 大小 / 当前存档标记）。
  - 单击选中，双击读档，滚轮滚动，悬停高亮；
  - **底部两行命令按钮面板**：16 条控制台命令每条都有对应按钮 ——
    上排项目命令（新建项目 / 打开项目 / 导入项目 / 项目列表 / 项目库 / 删除项目 / 项目位置 / 排除目录 / 帮助），
    下排存档命令（存档 / 读档 / 存档列表 / 状态 / 删除存档 / 重命名存档 / 清屏）；
    需要选中存档的按钮（读档 / 删除存档 / 重命名存档）未选中时置灰；
  - **导入已有项目**：点 [导入项目] 弹 Windows 文件夹选择框，或直接把文件夹拖进窗口；
  - **按钮信息一律弹窗显示**：按钮触发的列表 / 状态 / 帮助 / 操作结果 / 提示都以画布弹窗呈现
    （长文本自动换行 + 滚轮滚动），不刷控制台；控制台输入的命令仍输出到控制台日志；
  - **项目选择弹窗**：打开 / 删除项目弹出项目列表，点击选择（键盘 ↑↓ + 回车也可），无需手输名字；
  - 弹窗四种：输入框（回车确定 / Esc 取消）、确认框（防误删）、信息框、列表选择框。
- **右侧控制台**：日志 + 命令（键盘优先），输入 `help` 查看每条命令的中文说明。

## 控制台命令

输入 `help` 可查看**每条命令的中文说明**（`help <命令名>` 查看单条）。命令清单：

| 命令 | 说明 |
| --- | --- |
| `new <项目名>` | 在项目库目录新建项目并打开 |
| `open [名称\|路径]` | 打开项目：不带参数时弹窗选择；带参数按名字模糊匹配或直接给路径 |
| `import <路径>` | 导入已有目录：原地建立存档库 + 自动创建第一个存档；Assets 内目录自动用外部存档库 |
| `exclude [add\|remove <目录名>]` | 列出 / 增删存档与读档都跳过的目录（读档时原样保留；Unity 导入自动设置） |
| `projects` | 列出项目库与外部存档库的全部项目 |
| `base [目录]` | 查看 / 修改项目库目录 |
| `delproject [项目名]` | 永久删除项目（不带参数时弹窗选择；删除前需确认） |
| `save [名称]` | 存档；名称缺省时用「项目名 + 时间」 |
| `load [序号\|名称片段]` | 读档（自动保留恢复点）；缺省用鼠标选中项 |
| `list` | 列出全部存档 |
| `status` | 显示相对当前存档的改动（+ 新增 / ~ 修改 / - 删除） |
| `delete <序号\|名称片段>` | 删除存档（弹窗确认） |
| `rename <序号> [新名称]` | 重命名存档；不带新名称时弹窗改名 |
| `where` | 显示当前项目目录 |
| `help [命令名]` | 显示命令帮助（每条命令的中文说明） |
| `clear` | 清空控制台日志 |

## 磁盘布局（自研快照格式）

```
<项目目录>\.mygit\
  project.json            项目信息（名称 / id / 当前存档点）
  saves\<存档id>\
    meta.json             存档元数据（名称 / 时间 / 文件数 / 大小 / 是否自动恢复）
    manifest.json         文件清单（相对路径 / 大小 / 修改时间 / SHA256 / 目录树）
    files\...             存档文件内容（目录镜像，与快照时完全一致）
```

- 每个存档都是**独立完整快照**，删除任意存档不影响其他存档；
  代价是磁盘占用 ≈ 目录大小 × 存档数（有意为之：简单、可靠、可整目录搬走）。
- 快照**不包含** `.mygit` / `.mygit-*`（存档库自身）与符号链接/联接点（跳过，防止循环）；
  备份内容与修改时间，不备份只读等属性。
- 导入 Unity `Assets/` 时存档库在外部（Unity 项目根目录），布局为 `<Unity项目>\.mygit-<目录名>\...`。

## 启动参数

| 参数 | 说明 |
| --- | --- |
| （无） | 正常打开主界面 |
| `--demo` | 创建并打开演示项目 |
| `--command <命令>` | 启动后自动执行一条控制台命令（如 `--command help`，测试/脚本用） |
| `--shot <路径>` | 启动后自动截图保存并退出（生成界面预览） |
| `--selftest` | 快照引擎自检（结果写 stdout 与 `bin\...\selftest.log`） |

## 免环境运行（自包含发布）

源码形态**编译**需要 .NET 9 SDK，但**运行**可以完全不需要任何 C# 环境：

1. 在有 SDK 的电脑上双击 `publish.bat`（或执行
   `dotnet publish MyGit.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`）；
2. 产出 `publish\win-x64\MyGit.exe`（约 48 MB，运行时已打包进 exe）；
3. 把 `publish\win-x64` 文件夹拷到**任何 Windows x64 电脑**，双击 `MyGit.exe` 即可运行 ——
   无需安装 .NET / SDK / C# 环境；配置与项目库生成在 exe 所在目录，整个文件夹可随意搬走。

> 需要其他平台（win-arm64 / win-x86）时，把 `-r win-x64` 换成对应 RID 重新发布即可。
> WinForms 仅支持 Windows；Linux/macOS 不可用。

## 项目结构

| 文件 | 作用 |
| --- | --- |
| `MyGitForm.cs` | 主窗体：游戏风画布 + 弹窗 + 控制台命令（UI 层） |
| `ProjectStore.cs` | 快照引擎：项目生命周期、存档 / 读档 / 删除 / 重命名 / 状态（无 UI 依赖） |
| `Manifest.cs` | 文件清单：扫描、SHA256、差异对比、路径越界校验 |
| `SavePoint.cs` | 存档元数据读写 |
| `Config.cs` | 应用配置（项目库目录 / 上次打开的项目 / 外部项目登记） |
| `SelfTest.cs` | 引擎自检（`--selftest`） |
| `run.bat` / `build.bat` / `publish.bat` | 双击运行 / 仅编译 / 发布自包含单文件 exe |
| `DarkTerminalUI\` | 嵌入的 UI 引擎库（深色终端风格窗体 + 控制台） |

## 与 git 的区别

MyGit 不是 git：无分支合并、无 diff 工具、按完整快照存档。它面向「我要给整个目录存档 / 回档」的场景，
用游戏存档的心智模型替代 git 的命令行心智模型；数据是普通文件目录，随时可用任何工具查看或搬走。

---

# English

> ⚠️ **Windows only** (WinForms · `net9.0-windows`); Linux / macOS are not supported.

The snapshot engine is fully self-written (pure file copy, no git dependency). The UI is built on
[DarkTerminalUI](DarkTerminalUI/README.md) (dark-terminal style + docked log console), with a game-style
self-drawn canvas on the left.

## Core Rules (game-save intuition)

- **A project must be created or imported before saving**: one project = the "world" of one save slot.
- **Default save name = project name + time** (e.g. `DemoGame示例 2026-01-01 12:30:00`), customizable.
- **Load is a hard reset**: the working directory is fully restored to that snapshot.
- **Auto recovery point before load**: unsaved changes are automatically saved as a recovery snapshot
  before loading, so a mis-click never loses progress.
- The header status bar shows unsaved changes in real time (auto-refreshed by a file watcher,
  content verified by SHA256).

## Quick Start

Double-click `run.bat` (builds and runs; `build.bat` builds only).
Requires the .NET 9 SDK; for machines without it, use the `MyGit.exe` from "Run Without .NET".

**A. Manage a brand-new project**

1. Click **[新建项目]** (New Project) or type `new MyProject`;
2. Work inside the project directory (library defaults to `MyGit\Projects\<name>\`, type `where` to see the path);
3. Click **[存档]** (Save) or type `save`;
4. To roll back, select a save and click **[读档]** (Load) — or type `load 1`. A recovery point is kept automatically.

**B. Manage an existing project elsewhere (import in place, your files are not moved)**

1. Click **[导入项目]** (Import), drag a folder into the window, or type `import <path>`;
2. A `.mygit` store is created inside the directory (existing files untouched) plus a first save
   (import snapshot). If you import a Unity `Assets/` folder (or a subfolder), the store is placed at
   the Unity project root (`.mygit-<name>`) where Unity never sees it;
3. Save / load exactly like a brand-new project.

> Try the full flow: `dotnet run --project MyGit.csproj -- --demo` creates a demo project
> (2 saves + 1 unsaved change); loading once shows the auto recovery snapshot.

## Unity Projects (automatic protection on import)

1. **Import the root or only Assets — both supported**:
   - Import the **project root** (the folder containing `Assets/` and `ProjectSettings/`) →
     `Library / Temp / Logs / obj / Build / Builds` are excluded automatically (regenerable outputs);
     snapshots contain Assets + Packages + ProjectSettings;
   - **Import only `Assets/` (or a subfolder)** → external-store mode: the store lives at the Unity
     project root as `.mygit-<name>` (e.g. `.mygit-Assets`) — **invisible to Unity**, snapshots contain
     only Assets content;
   - Or import the root, then snapshot only Assets with `exclude add Packages` etc. —
     excluded directories are left untouched by loads;
2. **Excluded-directory load semantics**: excluded directories are neither saved nor affected by loads
   (never deleted, never restored) — so `Library` survives loads and user settings are never wiped;
3. **Close the Unity editor before saving / loading**: a load rewrites the whole managed directory, and
   files locked by Unity would make it fail (the load confirm dialog reminds you). Source files and
   `.meta` (GUID references) are fully preserved in snapshots.

## UI

- **Left canvas**: project info + save cards (index / name / time / file count / size / current marker).
  - Click to select, double-click to load, mouse wheel to scroll, hover to highlight;
  - **Two-row command button panel**: all 16 console commands have a button —
    top row (project): New Project / Open Project / Import / Project List / Library / Delete Project /
    Project Location / Exclude Dirs / Help; bottom row (save): Save / Load / Save List / Status /
    Delete Save / Rename Save / Clear Console; buttons that need a selected save are dimmed otherwise;
  - **Import**: click [导入项目] for a Windows folder picker, or drag a folder into the window;
  - **Button results are dialogs, not console spam**: lists / status / help / results / hints triggered
    by buttons appear in canvas dialogs (long text wraps and scrolls); console-typed commands still
    log to the console;
  - **Project picker dialog**: opening / deleting a project lists projects to click (↑↓ + Enter works too);
  - Four dialog kinds: input (Enter OK / Esc cancel), confirm (guards destructive actions),
    info (scrollable), and list picker.
- **Right console**: log + commands (keyboard-first); type `help` for a Chinese description of every command.

## Console Commands

Type `help` for a Chinese description of every command (`help <name>` for a single one):

| Command | Description |
| --- | --- |
| `new <name>` | Create a project in the library and open it |
| `open [name\|path]` | Open a project; without arguments shows the picker dialog |
| `import <path>` | Import an existing directory in place + auto-create the first save; Assets folders use an external store |
| `exclude [add\|remove <dir>]` | List / edit directories skipped by save and load (loads leave them untouched) |
| `projects` | List all projects (library + external stores) |
| `base [dir]` | Show / change the project library directory |
| `delproject [name]` | Permanently delete a project (picker without arguments; confirmation required) |
| `save [name]` | Save a snapshot; default name = project name + time |
| `load [index\|name fragment]` | Load a snapshot (auto recovery point kept); defaults to the selected card |
| `list` | List all saves |
| `status` | Show changes vs. the current save (+ added / ~ modified / - deleted) |
| `delete <index\|name fragment>` | Delete a save (with confirmation) |
| `rename <index> [new name]` | Rename a save (dialog when no new name given) |
| `where` | Show the current project directory |
| `help [name]` | Show command help with Chinese descriptions |
| `clear` | Clear the console log |

## On-disk Layout (self-written snapshot format)

```
<project dir>\.mygit\
  project.json            project info (name / id / current save)
  saves\<save id>\
    meta.json             save metadata (name / time / file count / size / auto-recovery flag)
    manifest.json         file list (relative paths / size / mtime / SHA256 / directory tree)
    files\...             snapshot contents (directory mirror, identical to save time)
```

- Every save is an **independent full snapshot** — deleting one never affects others;
  the trade-off is disk usage ≈ directory size × save count (intentional: simple, reliable, portable).
- Snapshots **exclude** `.mygit` / `.mygit-*` (the stores themselves) and symlinks/junctions
  (skipped to avoid cycles); content and modification times are preserved, read-only attributes are not.
- Unity `Assets/` imports keep the store outside, at `<Unity project>\.mygit-<name>\...`.

## Launch Arguments

| Argument | Description |
| --- | --- |
| (none) | Open the main window |
| `--demo` | Create and open a demo project |
| `--command <cmd>` | Run one console command at startup (e.g. `--command help`, for tests/scripts) |
| `--shot <path>` | Save a screenshot at startup and exit (UI preview) |
| `--selftest` | Run the snapshot-engine self-test (results to stdout and `bin\...\selftest.log`) |

## Run Without .NET (Self-contained Publish)

Building from source requires the .NET 9 SDK, but **running needs no C# environment at all**:

1. On a machine with the SDK, double-click `publish.bat` (or run
   `dotnet publish MyGit.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`);
2. It produces `publish\win-x64\MyGit.exe` (~48 MB, the runtime is bundled inside the exe);
3. Copy the `publish\win-x64` folder to **any Windows x64 machine** and double-click `MyGit.exe` —
   no .NET / SDK / C# install needed. Config and the project library are created next to the exe,
   so the whole folder is portable.

> For other platforms (win-arm64 / win-x86), republish with the matching RID instead of `win-x64`.
> WinForms is Windows-only; Linux/macOS are not supported.

## Project Layout

| File | Role |
| --- | --- |
| `MyGitForm.cs` | Main window: game-style canvas + dialogs + console commands (UI layer) |
| `ProjectStore.cs` | Snapshot engine: project lifecycle, save / load / delete / rename / status (no UI dependency) |
| `Manifest.cs` | File manifest: scanning, SHA256, diffing, path-escape validation |
| `SavePoint.cs` | Save metadata I/O |
| `Config.cs` | App config (project library dir / last opened project / external project registry) |
| `SelfTest.cs` | Engine self-test (`--selftest`) |
| `run.bat` / `build.bat` / `publish.bat` | Run / build-only / publish a self-contained single-file exe |
| `DarkTerminalUI\` | Embedded UI engine library (dark-terminal window + console) |

## Difference from git

MyGit is not git: no branching/merging, no diff tools — it stores complete snapshots. It targets the
"save / roll back my whole directory" scenario, replacing git's CLI mental model with a game-save mental
model. The data is plain file directories that can be inspected or moved with any tool.
