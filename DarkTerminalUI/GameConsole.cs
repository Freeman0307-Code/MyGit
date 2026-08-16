namespace DarkTerminalUI;

/// <summary>
/// 文字控制台控件：右侧面板，彩色记录日志，
/// 底部是内嵌的终端式输入条（❯ 提示符），回车提交命令
/// （通过 <see cref="CommandEntered"/> 事件供外部接入）。
/// 支持拖选复制、右键菜单（复制/全选/清空）、打字机效果输出。
/// </summary>
public sealed class GameConsole : UserControl, IConsolePanel
{
    private const int MaxChars = 60000;

    private IThemePalette _palette;
    private readonly RichTextBox _output;
    private readonly Panel _inputBar;
    private readonly Label _prompt;
    private readonly TextBox _input;

    /// <summary>写入队列：所有日志串行输出，打字机效果的行逐字追加。</summary>
    private readonly Queue<(string Text, GameColor Color, bool Type)> _writeQueue = new();
    private bool _writing;
    private bool _typingNow;
    private bool _dragSelect; // 日志区鼠标拖选进行中

    /// <summary>用户按回车提交命令时触发。</summary>
    public event EventHandler<string>? CommandEntered;

    /// <summary>整行日志输出后触发（打字机逐字过程不触发；联机镜像用）。</summary>
    public event Action<string, GameColor>? Logged;

    /// <param name="palette">主题（决定配色与字体）；为 null 时使用默认深色主题。</param>
    public GameConsole(IThemePalette? palette = null)
    {
        _palette = palette ?? DarkPalette.Instance;

        BackColor = _palette.ConsoleBack.ToColor();

        _output = new RichTextBox
        {
            ReadOnly = true,
            Dock = DockStyle.Fill,
            BackColor = _palette.LogBack.ToColor(),
            ForeColor = _palette.Text.ToColor(),
            BorderStyle = BorderStyle.None,
            Font = new Font(_palette.FontName, 9f),
            WordWrap = true, // 自动换行，避免长日志超出面板右边界看不见
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };

        // 终端式输入条：❯ 提示符 + 大号输入框，直接在控制台里打字
        _inputBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            BackColor = _palette.InputBarBack.ToColor(),
            Padding = new Padding(10, 8, 10, 8),
        };
        _prompt = new Label
        {
            Text = "❯",
            Dock = DockStyle.Left,
            Width = 26,
            ForeColor = _palette.Prompt.ToColor(),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(_palette.FontName, 16f, FontStyle.Bold),
        };
        _input = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = _palette.InputBarBack.ToColor(),
            ForeColor = _palette.InputText.ToColor(),
            Font = new Font(_palette.FontName, 12f),
        };
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            string cmd = _input.Text.Trim();
            if (cmd.Length == 0) return;
            CommandEntered?.Invoke(this, cmd);
            _input.Clear();
        };

        _inputBar.Controls.Add(_input);   // Fill 最后停靠：占满剩余空间
        _inputBar.Controls.Add(_prompt);  // 先停靠：占据左侧提示符位置

        // 点击控制台任意位置时聚焦输入框，方便直接打字
        MouseDown += (_, _) => _input.Focus();

        // ---- 日志区支持复制：只读 RichTextBox 默认不能用鼠标选择，这里手动实现拖选 ----
        _output.HideSelection = false;
        _output.MouseDown += (_, e) =>
        {
            _input.Focus(); // 点击日志区仍聚焦输入框
            if (e.Button != MouseButtons.Left) return;
            _dragSelect = true;
            _output.SelectionStart = _output.GetCharIndexFromPosition(e.Location);
            _output.SelectionLength = 0;
        };
        _output.MouseMove += (_, e) =>
        {
            if (!_dragSelect) return;
            int cur = _output.GetCharIndexFromPosition(e.Location);
            int start = _output.SelectionStart;
            _output.SelectionStart = Math.Min(start, cur);
            _output.SelectionLength = Math.Abs(cur - start);
        };
        _output.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _dragSelect = false;
        };
        _output.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C && _output.SelectionLength > 0)
                _output.Copy(); // Ctrl+C 复制选中内容
        };

        // 右键菜单：复制 / 全选 / 清空日志
        var menu = new ContextMenuStrip();
        menu.Items.Add("复制", null, (_, _) => { if (_output.SelectionLength > 0) _output.Copy(); });
        menu.Items.Add("全选", null, (_, _) => { _output.Focus(); _output.SelectAll(); });
        menu.Items.Add("清空日志", null, (_, _) => _output.Clear());
        _output.ContextMenuStrip = menu;

        Controls.Add(_output);
        Controls.Add(_inputBar);
    }

    /// <summary>
    /// 运行时更换主题：立即刷新面板、日志区、输入条的全部配色与字体。
    /// 已输出的历史日志颜色不变（RichTextBox 按段着色）。
    /// </summary>
    public void ApplyTheme(IThemePalette palette)
    {
        _palette = palette ?? DarkPalette.Instance;
        BackColor = _palette.ConsoleBack.ToColor();
        _output.BackColor = _palette.LogBack.ToColor();
        _output.ForeColor = _palette.Text.ToColor();
        _output.Font = new Font(_palette.FontName, 9f);
        _inputBar.BackColor = _palette.InputBarBack.ToColor();
        _prompt.ForeColor = _palette.Prompt.ToColor();
        _prompt.Font = new Font(_palette.FontName, 16f, FontStyle.Bold);
        _input.BackColor = _palette.InputBarBack.ToColor();
        _input.ForeColor = _palette.InputText.ToColor();
        _input.Font = new Font(_palette.FontName, 12f);
    }

    /// <summary>底层控件（本控件自身）。</summary>
    public Control Control => this;

    /// <summary>命令输入框是否拥有焦点（有焦点时窗体不应响应快捷键，避免打字误触发）。</summary>
    public bool InputHasFocus => _input.Focused;

    /// <summary>让命令输入框获得焦点。</summary>
    public void FocusInput() => _input.Focus();

    /// <summary>日志区是否选中了文字。</summary>
    public bool HasSelection => _output.SelectionLength > 0;

    /// <summary>清空日志区。</summary>
    public void ClearLog() => _output.Clear();

    /// <summary>复制选中的日志到剪贴板（不依赖焦点）。</summary>
    public void CopySelection()
    {
        if (_output.SelectionLength > 0) _output.Copy();
    }

    /// <summary>全选日志区。</summary>
    public void SelectAllLog()
    {
        _output.Focus();
        _output.SelectAll();
    }

    /// <summary>追加一行灰色日志。</summary>
    public void Log(string text) => Enqueue(text, _palette.Text, type: false);

    /// <summary>追加一行带颜色的日志。</summary>
    public void Log(string text, GameColor color) => Enqueue(text, color, type: false);

    /// <summary>逐字弹出（打字机效果）输出一行文字，约 40ms/字。</summary>
    public void TypeLog(string text, GameColor color) => Enqueue(text, color, type: true);

    private void Enqueue(string text, GameColor color, bool type)
    {
        lock (_writeQueue)
            _writeQueue.Enqueue((text, color, type));
        _ = PumpAsync(); // 已在泵时直接返回，由泵继续处理
    }

    /// <summary>串行泵：按队列顺序输出；打字机行逐字追加并延时。</summary>
    private async Task PumpAsync()
    {
        if (_writing) return;
        _writing = true;
        try
        {
            while (true)
            {
                (string Text, GameColor Color, bool Type) item;
                lock (_writeQueue)
                {
                    if (_writeQueue.Count == 0) return;
                    item = _writeQueue.Dequeue();
                }

                if (item.Type)
                {
                    _typingNow = true;
                    foreach (char c in item.Text)
                    {
                        Append(c.ToString(), item.Color);
                        await Task.Delay(40); // 打字机节奏
                    }
                    _typingNow = false;
                }
                else
                {
                    Append(item.Text, item.Color);
                }
            }
        }
        finally
        {
            _writing = false;
        }
    }

    private void Append(string text, GameColor color)
    {
        if (_output.TextLength > MaxChars) _output.Clear(); // 防无限增长
        _output.SelectionStart = _output.TextLength;
        _output.SelectionColor = color.ToColor();
        _output.AppendText(text + Environment.NewLine);
        _output.SelectionStart = _output.TextLength;
        _output.ScrollToCaret();
        if (!_typingNow) Logged?.Invoke(text, color); // 整行输出才镜像（打字机逐字不触发）
    }
}
