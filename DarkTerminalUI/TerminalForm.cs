namespace DarkTerminalUI;

/// <summary>
/// 深色终端风格窗体基类：自动应用主题配色（深色背景 + 防闪烁 + 键盘优先），
/// 自带右侧日志控制台（<see cref="Console"/>）与命令注册表（<see cref="Commands"/>），
/// 以及 <see cref="CreateCanvas"/> / <see cref="SafeInvoke"/> 等常用能力。
///
/// 用法：继承本类，在构造函数里 DockConsole() 停靠控制台、Commands.Register 注册命令，
/// 在 OnPaint 里用 CreateCanvas 绘制。
/// </summary>
public abstract class TerminalForm : Form
{
    private IThemePalette _palette;
    private readonly IConsolePanel _console;
    private readonly ICommandRegistry _commands;

    /// <param name="palette">主题；为 null 时使用默认深色主题（<see cref="DarkPalette"/>）。</param>
    protected TerminalForm(IThemePalette? palette = null)
    {
        _palette = palette ?? DarkPalette.Instance;
        DoubleBuffered = true;                 // 防闪烁
        KeyPreview = true;                     // 键盘事件先到窗体
        BackColor = _palette.WindowBack.ToColor();

        _console = new GameConsole(_palette);
        _commands = new CommandRegistry();

        // 命令入口：回显 → 分发 → 未知命令提示
        _console.CommandEntered += (_, cmd) =>
        {
            _console.Log($"> {cmd}", _palette.Echo);
            if (!_commands.Execute(cmd))
                _console.Log($"未知命令: {cmd}（输入 help 查看命令）", _palette.Warn);
        };

        // 内置命令：help / clear（可用 Commands.Register 覆盖）
        _commands.Register("help", _ => LogHelp());
        _commands.Register("clear", _ => _console.ClearLog());
    }

    /// <summary>当前主题调色板（自定义配色/绘制用）。</summary>
    public IThemePalette Palette => _palette;

    /// <summary>
    /// 运行时更换主题：窗体背景、右侧控制台（<see cref="Console"/>）立即换肤，
    /// <see cref="Palette"/> / <see cref="CreateCanvas"/> 同步生效。
    /// </summary>
    public void SetTheme(IThemePalette theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _palette = theme;
        BackColor = theme.WindowBack.ToColor();
        if (_console is GameConsole gameConsole)
            gameConsole.ApplyTheme(theme);
    }

    /// <summary>右侧日志控制台（日志输出 + 命令输入）。</summary>
    public IConsolePanel Console => _console;

    /// <summary>控制台命令注册表：Register 自己的命令即可被控制台调用。</summary>
    public ICommandRegistry Commands => _commands;

    /// <summary>创建 2D 绘制画布（GDI+ 实现），OnPaint 里用它绘制。</summary>
    public ICanvas2D CreateCanvas(Graphics g) => new GdiCanvas(g, _palette);

    /// <summary>后台线程回调统一切回 UI 线程（控件已销毁时安全忽略）。</summary>
    public void SafeInvoke(Action action)
    {
        if (IsDisposed) return;
        try { BeginInvoke(action); } catch { }
    }

    /// <summary>把控制台停靠到窗体右侧（调用一次即可）。</summary>
    /// <param name="width">控制台宽度（像素），如 380。</param>
    protected void DockConsole(int width = 380)
    {
        var control = _console.Control;
        control.Width = width;
        control.Dock = DockStyle.Right;
        Controls.Add(control);
    }

    /// <summary>日志区有选中时 Ctrl+C 优先复制日志（窗体级快捷键，不依赖输入框焦点）。</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Control && e.KeyCode == Keys.C && _console.HasSelection)
        {
            _console.CopySelection();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void LogHelp()
    {
        _console.Log("可用命令（输入后回车执行）：", _palette.Info);
        foreach (var name in _commands.Names)
            _console.Log($"  {name}", _palette.Text);
        _console.Log("help — 显示本帮助；clear — 清空日志", _palette.Text);
    }
}
