# DarkTerminalUI — 深色终端风格 WinForms UI 库 | Dark-Terminal WinForms UI Library

[中文](#中文) · [English](#english)

---

# 中文

把多个项目公共的 UI 风格提取成独立类库，通过接口对外提供服务，新项目一行引用即可获得同样的外观与交互：

- 深色近黑窗体（`#121218`）+ 防闪烁 + 键盘优先
- 右侧停靠的彩色日志控制台：`❯` 终端输入条、打字机效果、拖选复制、右键菜单
- 与 UI 框架无关的颜色结构 `GameColor` 与 2D 绘制抽象 `ICanvas2D`（GDI+ 实现 `GdiCanvas`）
- 可定制的主题调色板（`IThemePalette` / `DarkPalette`）
- 控制台命令注册表（`ICommandRegistry`）：输入命令自动分发，自带 help / clear
- 现成窗体基类 `TerminalForm`：以上全部能力开箱即用

## 快速上手

```csharp
using DarkTerminalUI;

public sealed class MyForm : TerminalForm
{
    public MyForm()
    {
        Text = "我的程序";
        ClientSize = new Size(1100, 720);
        DockConsole(380);                      // 右侧停靠日志控制台

        Commands.Register("hello", args =>     // 控制台输入 hello 张三 回车即触发
            Console.Log($"你好，{(args.Length == 0 ? "世界" : args)}！", Palette.Good));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = CreateCanvas(e.Graphics);      // ICanvas2D：与 UI 框架无关的绘制
        g.FillRectangle(Palette.WindowBack, 0, 0, ClientSize.Width, ClientSize.Height);
        g.DrawString("示例", 12, 12, 11f, true, Palette.Info);
        g.FillEllipse(Palette.Warn, 100, 100, 40, 40);
    }
}
```

```csharp
// Program.cs
[STAThread]
static void Main() {
    ApplicationConfiguration.Initialize();
    Application.Run(new MyForm());
}
```

## 接口清单（别人如何调用）

| 接口 / 类型 | 作用 |
| --- | --- |
| `TerminalForm` | 窗体基类：主题 + 控制台 + 命令分发开箱即用。`Console` / `Commands` / `Palette` / `CreateCanvas` / `SafeInvoke` / `DockConsole` |
| `IConsolePanel` | 控制台抽象：`Log` / `Log(text,color)` / `TypeLog` / `ClearLog` / `FocusInput` / 事件 `CommandEntered` / `Logged` |
| `GameConsole` | `IConsolePanel` 的默认实现（UserControl），也可单独放到任意窗体 |
| `ICommandRegistry` | 命令注册表：`Register(name, handler)` / `Execute(line)` / `Names`，内置 help / clear |
| `IThemePalette` / `DarkPalette` | 主题调色板：语义色（Info/Good/Warn/Bad/Title/Cyan/DimText…）+ 表面色（窗体/控制台底色）+ 字体名。实现接口即可自定义配色 |
| `GameColor` | 与框架无关的 RGBA 颜色结构（`FromArgb` / `White` / `LightGray`…） |
| `GameColorExtensions` | `ToColor()`（→ `System.Drawing.Color`）/ `ToGameColor()` |
| `ICanvas2D` | 2D 绘制抽象：椭圆/矩形/直线/文字 + 虚线 + 文字测量，坐标 float 像素 |
| `GdiCanvas` | `ICanvas2D` 的 GDI+ 实现（`new GdiCanvas(graphics, palette?)`） |

自定义主题示例：

```csharp
sealed class MyTheme : IThemePalette   // 只改几个颜色，其余继承默认
{
    public GameColor Info => GameColor.FromArgb(120, 220, 120);
    // ... 其余属性直接委托给 DarkPalette.Instance
}
var form = new MyForm(new MyTheme());  // TerminalForm(palette) / GameConsole(palette) 均可传入

// 运行时换肤：窗体背景 + 右侧控制台立即同步切换
form.SetTheme(new MyTheme());          // 或控制台单独换肤：console.ApplyTheme(theme)
```

## 在项目里引用

方式一（推荐，同仓库内）：csproj 加项目引用

```xml
<ItemGroup>
  <ProjectReference Include="DarkTerminalUI\DarkTerminalUI.csproj" />
</ItemGroup>
```

并在任意 cs 文件（或新建 `GlobalUsings.cs`）加：

```csharp
global using DarkTerminalUI;
```

方式二（NuGet）：`dotnet pack -c Release` 产出 nupkg 后发布到自己的源，
再 `dotnet add package DarkTerminalUI`。

## 目标框架

`net9.0-windows` + WinForms。

---

# English

A standalone class library extracted from the common UI style of several WinForms projects.
Every capability is exposed through interfaces, so a new project gets the same look and interaction
with a single reference:

- Dark near-black window (`#121218`) + flicker-free rendering + keyboard-first interaction
- Docked color log console: `❯` terminal input bar, typewriter effect, drag-select copy, context menu
- Framework-agnostic color struct `GameColor` and 2D drawing abstraction `ICanvas2D` (GDI+ implementation `GdiCanvas`)
- Customizable theme palette (`IThemePalette` / `DarkPalette`)
- Console command registry (`ICommandRegistry`): typed commands are dispatched automatically, with built-in help / clear
- Ready-made form base class `TerminalForm`: everything above out of the box

## Quick Start

```csharp
using DarkTerminalUI;

public sealed class MyForm : TerminalForm
{
    public MyForm()
    {
        Text = "My App";
        ClientSize = new Size(1100, 720);
        DockConsole(380);                      // dock the log console on the right

        Commands.Register("hello", args =>     // typing "hello John" in the console triggers this
            Console.Log($"Hello, {(args.Length == 0 ? "world" : args)}!", Palette.Good));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = CreateCanvas(e.Graphics);      // ICanvas2D: framework-agnostic drawing
        g.FillRectangle(Palette.WindowBack, 0, 0, ClientSize.Width, ClientSize.Height);
        g.DrawString("Demo", 12, 12, 11f, true, Palette.Info);
        g.FillEllipse(Palette.Warn, 100, 100, 40, 40);
    }
}
```

```csharp
// Program.cs
[STAThread]
static void Main() {
    ApplicationConfiguration.Initialize();
    Application.Run(new MyForm());
}
```

## Public API

| Interface / Type | Role |
| --- | --- |
| `TerminalForm` | Form base class: theme + console + command dispatch out of the box. `Console` / `Commands` / `Palette` / `CreateCanvas` / `SafeInvoke` / `DockConsole` |
| `IConsolePanel` | Console abstraction: `Log` / `Log(text,color)` / `TypeLog` / `ClearLog` / `FocusInput` / events `CommandEntered` / `Logged` |
| `GameConsole` | Default `IConsolePanel` implementation (UserControl); can also be dropped into any form |
| `ICommandRegistry` | Command registry: `Register(name, handler)` / `Execute(line)` / `Names`, with built-in help / clear |
| `IThemePalette` / `DarkPalette` | Theme palette: semantic colors (Info/Good/Warn/Bad/Title/Cyan/DimText…) + surface colors (window/console backgrounds) + font name. Implement the interface to customize |
| `GameColor` | Framework-agnostic RGBA color struct (`FromArgb` / `White` / `LightGray`…) |
| `GameColorExtensions` | `ToColor()` (→ `System.Drawing.Color`) / `ToGameColor()` |
| `ICanvas2D` | 2D drawing abstraction: ellipses/rects/lines/text + dashed lines + text measurement, float pixel coordinates |
| `GdiCanvas` | GDI+ implementation of `ICanvas2D` (`new GdiCanvas(graphics, palette?)`) |

Custom theme example:

```csharp
sealed class MyTheme : IThemePalette   // change a few colors, delegate the rest
{
    public GameColor Info => GameColor.FromArgb(120, 220, 120);
    // ... other properties delegate to DarkPalette.Instance
}
var form = new MyForm(new MyTheme());  // TerminalForm(palette) / GameConsole(palette) both accept one

// Switch theme at runtime: window background + console update immediately
form.SetTheme(new MyTheme());          // or per-console: console.ApplyTheme(theme)
```

## Referencing from a Project

Option 1 (recommended, same repository): add a project reference to your csproj

```xml
<ItemGroup>
  <ProjectReference Include="DarkTerminalUI\DarkTerminalUI.csproj" />
</ItemGroup>
```

and add the following to any cs file (or a new `GlobalUsings.cs`):

```csharp
global using DarkTerminalUI;
```

Option 2 (NuGet): `dotnet pack -c Release` produces a nupkg; publish it to your own feed,
then `dotnet add package DarkTerminalUI`.

## Target Framework

`net9.0-windows` + WinForms.
