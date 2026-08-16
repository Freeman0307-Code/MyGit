using DarkTerminalUI;

namespace MyGit;

/// <summary>
/// MyGit 主窗体：左侧游戏风自绘画布（项目信息 / 存档卡片列表 / 命令按钮面板 / 弹窗），
/// 右侧 DarkTerminalUI 终端控制台（日志 + 命令）。
///
/// 交互约定：
/// - 单击选中存档、双击读档、滚轮滚动；底部两行按钮与 16 条控制台命令一一对应；
/// - 按钮触发的信息（列表 / 状态 / 帮助 / 操作结果 / 提示）以画布弹窗显示，不刷控制台；
///   控制台输入的命令仍输出到控制台日志；
/// - 弹窗四种：输入框（回车确定 / Esc 取消）、确认框、信息框（长文本可滚动）、
///   列表选择框（打开/删除项目时点击选择，键盘 ↑↓ + 回车也可）；
/// - 所有磁盘操作在后台线程执行，UI 不卡顿。
/// </summary>
public sealed class MyGitForm : TerminalForm
{
    private const int ConsoleWidth = 380;
    private const float MarginX = 20f;
    private const int CardH = 62, CardGap = 8;
    private const float ListTop = 116f;
    private const int BtnW = 78, BtnH = 34, BtnGap = 7;
    private const float InfoLineH = 19f;
    private const float PickerRowH = 40f;

    /// <summary>命令按钮面板（两行，与全部 16 条控制台命令一一对应）：行 0 = 项目命令（9 个），行 1 = 存档命令（7 个）。</summary>
    private static readonly string[] CmdRow0 = { "新建项目", "打开项目", "导入项目", "项目列表", "项目库", "删除项目", "项目位置", "排除目录", "帮助" };
    private static readonly string[] CmdRow1 = { "存档", "读档", "存档列表", "状态", "删除存档", "重命名存档", "清屏" };

    /// <summary>全部命令的中文说明（help 显示，顺序即显示顺序）。</summary>
    private static readonly (string Cmd, string Desc)[] CommandHelp =
    {
        ("new",        "新建项目：new <项目名>，在项目库目录创建新项目并打开"),
        ("open",       "打开项目：open [名称|路径]，不带参数时弹窗选择；带参数按名字匹配或直接给路径"),
        ("import",     "导入项目：import <路径>，把别处的已有目录原地纳入管理并自动创建第一个存档"),
        ("exclude",    "排除目录：exclude [add|remove <目录名>]，列出或增删存档与读档都跳过的目录（Unity 项目导入时自动设置）"),
        ("projects",   "项目列表：列出项目库中的全部项目"),
        ("base",       "项目库目录：base [目录]，查看或修改项目库存放位置"),
        ("delproject", "删除项目：delproject [项目名]，不带参数时弹窗选择；永久删除项目及其全部存档（需确认）"),
        ("save",       "存档：save [名称]，完整快照当前项目目录；名称缺省为「项目名 + 时间」"),
        ("load",       "读档：load [序号|名称片段]，整体还原到该存档；读档前自动保留恢复存档"),
        ("list",       "存档列表：列出全部存档（最新在前，▶ 为当前存档）"),
        ("status",     "状态：显示相对当前存档的改动（+ 新增 / ~ 修改 / - 删除）"),
        ("delete",     "删除存档：delete <序号|名称片段>，删除该存档的快照（需确认）"),
        ("rename",     "重命名存档：rename <序号> [新名称]，不带新名称时弹窗改名"),
        ("where",      "项目位置：显示当前项目的磁盘目录"),
        ("help",       "帮助：显示全部命令说明；help <命令名> 查看单条说明"),
        ("clear",      "清屏：清空控制台日志"),
    };

    // ---- 界面状态 ----
    private ProjectStore? _store;
    private List<SavePoint> _saves = new();
    private int _selected = -1;      // 选中存档（_saves 下标，最新在前）
    private int _hoverRow = -1;      // 悬停的存档行
    private int _hoverBtn = -1;      // 悬停的命令按钮
    private int _scroll;             // 列表滚动像素
    private bool _busy;              // 存档/读档等后台操作进行中
    private bool _statusRefreshing;  // 状态后台检查中
    private DiffResult? _status;     // 未保存改动（相对当前存档）
    private string? _currentId;      // 当前存档 id（缓存）
    private string? _currentName;    // 当前存档名（缓存）

    private UiDialog? _dialog;       // 当前弹窗
    private bool _cursorOn;          // 输入框光标闪烁

    private readonly System.Windows.Forms.Timer _cursorTimer;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private FileSystemWatcher? _watcher;

    private int ViewW => Math.Max(1, ClientSize.Width - ConsoleWidth);
    private int ViewH => ClientSize.Height;
    private float Row0Y => ViewH - 120f;
    private float Row1Y => ViewH - 80f;
    private float ListBottom => Row0Y - 12f;
    private float ListH => ListBottom - ListTop;
    private float CardW => ViewW - MarginX - 46f; // 右侧留滚动条位置

    /// <summary>列表选择弹窗的条目。</summary>
    private sealed record PickerItem(string Title, string Sub);

    /// <summary>
    /// 画布弹窗：Input=文本输入框；Confirm=确认框；Info=信息框（长文本可滚动）；
    /// Picker=列表选择框（点击/键盘选择一项）。
    /// </summary>
    private sealed class UiDialog
    {
        public enum DialogKind
        {
            Input,
            Confirm,
            Info,
            Picker,
        }

        public required DialogKind Kind;
        public required string Title;
        public string Text = "";                 // Input: 输入缓冲；Info: 原始文本
        public List<string> Lines = new();       // Info: 换行后的显示行
        public int Caret;                        // Input: 光标位置
        public string Message = "";              // Confirm: 提示文字
        public Action<string>? OnOk;             // Input / Confirm: 确定回调
        public List<PickerItem> Items = new();   // Picker: 选项
        public Action<PickerItem>? OnPick;       // Picker: 选中回调
        public int Scroll;                       // Info / Picker: 内容滚动像素
        public int SelRow = -1;                  // Picker: 键盘选中行
        public int HoverRow = -1;                // Picker: 鼠标悬停行
        public bool HoverOk;                     // 确定按钮悬停
        public bool HoverCancel;                 // 取消按钮悬停

        public bool IsInput => Kind == DialogKind.Input;
        public bool HasOk => Kind is DialogKind.Input or DialogKind.Confirm;
    }

    public MyGitForm(string[] args)
    {
        Text = "MyGit — 游戏存档式版本管理";
        ClientSize = new Size(1180, 760);
        DockConsole(ConsoleWidth);

        _cursorTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _cursorTimer.Tick += (_, _) =>
        {
            if (_dialog is { IsInput: true }) { _cursorOn = !_cursorOn; Invalidate(); }
        };
        _cursorTimer.Start();

        // 状态自动刷新：由文件监控事件重启，1.5 秒无变化后检查一次
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); RefreshStatusAsync(); };

        RegisterCommands();
        WireMouse();
        WireFolderDrop();

        Console.Log("═══ MyGit · 游戏存档式版本管理 ═══", Palette.Title);
        Console.Log("像玩游戏一样管理项目：new 新建项目 → save 存档 → load 读档（读档前自动保留恢复点）。", Palette.Info);
        Console.Log("别处的项目：点 [导入项目] 选文件夹、或直接把文件夹拖进窗口，原地纳入管理。", Palette.Info);
        Console.Log("控制台输入 help 查看全部命令；左侧画布可用鼠标操作，按钮信息以弹窗显示。", Palette.DimText);

        if (args.Contains("--demo", StringComparer.OrdinalIgnoreCase)) DemoSetup();
        else OpenLastProject();

        // 命令行自动化：--command <命令> 启动后自动执行一条命令（命令以空格分隔的多个词组成，遇到下一个 -- 参数截止）
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--command", StringComparison.OrdinalIgnoreCase))
            {
                var cmdWords = args.Skip(i + 1).TakeWhile(a => !a.StartsWith("--")).ToList();
                string startupCmd = string.Join(" ", cmdWords).Trim();
                if (startupCmd.Length > 0)
                {
                    Console.Log($"> {startupCmd}", Palette.Echo);
                    Commands.Execute(startupCmd);
                }
                break;
            }
        }

        FormClosed += (_, _) => DisposeWatcher();
        Console.FocusInput();
    }

    // ======================== 鼠标 / 键盘 ========================

    private void WireMouse()
    {
        MouseMove += (_, e) =>
        {
            var (row, btn) = HitTest(e.Location);
            bool dialogHoverChanged = UpdateDialogHover(e.Location);
            if (row != _hoverRow || btn != _hoverBtn || dialogHoverChanged)
            {
                _hoverRow = row;
                _hoverBtn = btn;
                Invalidate();
            }
        };
        MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) HandleClick(e.Location); };
        MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) HandleDoubleClick(e.Location); };
        MouseWheel += (_, e) =>
        {
            if (_dialog is { Kind: UiDialog.DialogKind.Info or UiDialog.DialogKind.Picker } d
                && DialogBoxRect().Contains(PointToClient(Cursor.Position)))
            {
                var content = DialogContentRect();
                int maxScroll = d.Kind == UiDialog.DialogKind.Picker
                    ? Math.Max(0, (int)(d.Items.Count * PickerRowH - content.Height))
                    : Math.Max(0, (int)(d.Lines.Count * InfoLineH - content.Height));
                d.Scroll = Math.Clamp(d.Scroll - Math.Sign(e.Delta) * 40, 0, maxScroll);
                Invalidate();
                return;
            }
            if (_dialog != null || _store == null) return;
            int listMax = Math.Max(0, _saves.Count * (CardH + CardGap) + CardGap - (int)ListH);
            _scroll = Math.Clamp(_scroll - Math.Sign(e.Delta) * 56, 0, listMax);
            Invalidate();
        };
    }

    /// <summary>把文件夹拖进窗口（画布或控制台区域均可）= 导入该项目。</summary>
    private void WireFolderDrop()
    {
        DragEventHandler onEnter = (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true
                && ((string[])e.Data.GetData(DataFormats.FileDrop)!).Any(Directory.Exists))
                e.Effect = DragDropEffects.Copy;
        };
        DragEventHandler onDrop = (_, e) =>
        {
            var paths = e.Data?.GetData(DataFormats.FileDrop) as string[];
            string? dir = paths?.FirstOrDefault(Directory.Exists);
            if (dir != null) RunImport(dir, viaButton: true);
        };
        AllowDrop = true;
        DragEnter += onEnter;
        DragDrop += onDrop;
        Console.Control.AllowDrop = true;
        Console.Control.DragEnter += onEnter;
        Console.Control.DragDrop += onDrop;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var d = _dialog;
        if (d == null) return;
        switch (e.KeyCode)
        {
            case Keys.Enter:
                e.Handled = e.SuppressKeyPress = true;
                ConfirmDialog();
                return;
            case Keys.Escape:
                e.Handled = e.SuppressKeyPress = true;
                CloseDialog();
                return;
            case Keys.Up when d.Kind == UiDialog.DialogKind.Picker && d.Items.Count > 0:
                d.SelRow = Math.Max(0, d.SelRow - 1);
                AdjustPickerScroll(d);
                e.Handled = e.SuppressKeyPress = true;
                Invalidate();
                return;
            case Keys.Down when d.Kind == UiDialog.DialogKind.Picker && d.Items.Count > 0:
                d.SelRow = Math.Min(d.Items.Count - 1, d.SelRow + 1);
                AdjustPickerScroll(d);
                e.Handled = e.SuppressKeyPress = true;
                Invalidate();
                return;
            case Keys.Back when d.IsInput && d.Caret > 0:
                d.Text = d.Text.Remove(d.Caret - 1, 1);
                d.Caret--;
                e.Handled = e.SuppressKeyPress = true;
                Invalidate();
                return;
            case Keys.Delete when d.IsInput && d.Caret < d.Text.Length:
                d.Text = d.Text.Remove(d.Caret, 1);
                e.Handled = e.SuppressKeyPress = true;
                Invalidate();
                return;
            case Keys.Left when d.IsInput && d.Caret > 0:
                d.Caret--;
                e.Handled = e.SuppressKeyPress = true;
                Invalidate();
                return;
            case Keys.Right when d.IsInput && d.Caret < d.Text.Length:
                d.Caret++;
                e.Handled = e.SuppressKeyPress = true;
                Invalidate();
                return;
            case Keys.Home when d.IsInput:
                d.Caret = 0;
                e.Handled = e.SuppressKeyPress = true;
                Invalidate();
                return;
            case Keys.End when d.IsInput:
                d.Caret = d.Text.Length;
                e.Handled = e.SuppressKeyPress = true;
                Invalidate();
                return;
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (_dialog is { IsInput: true } d)
        {
            if (!char.IsControl(e.KeyChar) && d.Text.Length < 200)
            {
                d.Text = d.Text.Insert(d.Caret, e.KeyChar.ToString());
                d.Caret++;
                Invalidate();
            }
            e.Handled = true;
        }
    }

    // ---- 命中测试 ----

    private RectangleF CardRect(int row) =>
        new(MarginX, ListTop - _scroll + row * (CardH + CardGap), CardW, CardH);

    /// <summary>命令按钮矩形（idx: 0-7 上排项目命令，8-14 下排存档命令）。</summary>
    private RectangleF CmdButtonRect(int idx)
    {
        int row = idx < CmdRow0.Length ? 0 : 1;
        int col = row == 0 ? idx : idx - CmdRow0.Length;
        return new RectangleF(MarginX + col * (BtnW + BtnGap), row == 0 ? Row0Y : Row1Y, BtnW, BtnH);
    }

    private string CmdButtonLabel(int idx) =>
        idx < CmdRow0.Length ? CmdRow0[idx] : CmdRow1[idx - CmdRow0.Length];

    /// <summary>按钮是否可用：处理中全禁用；存档类命令需要项目；读档/删除存档/重命名还要选中存档。</summary>
    private bool CmdButtonEnabled(int idx)
    {
        if (_busy) return false;
        bool needsProject = idx is 9 or 10 or 11 or 12 or 13 or 14;
        if (needsProject && _store == null) return false;
        bool needsSel = idx is 10 or 13 or 14;
        if (needsSel && _selected < 0) return false;
        return true;
    }

    /// <summary>命中测试：返回 (悬停存档行, 悬停命令按钮)，互斥。弹窗打开时都返回无。</summary>
    private (int Row, int Btn) HitTest(Point p)
    {
        if (_dialog != null) return (-1, -1);
        for (int i = 0; i < CmdRow0.Length + CmdRow1.Length; i++)
            if (CmdButtonRect(i).Contains(p)) return (-1, i);
        for (int r = 0; r < _saves.Count; r++)
        {
            var rc = CardRect(r);
            if (rc.Contains(p) && rc.Top >= ListTop && rc.Bottom <= ListBottom) return (r, -1);
        }
        return (-1, -1);
    }

    private void HandleClick(Point p)
    {
        var d = _dialog;
        if (d != null)
        {
            var (ok, cancel) = DialogButtonRects();
            if (d.HasOk)
            {
                if (ok.Contains(p)) { ConfirmDialog(); return; }
                if (cancel.Contains(p)) { CloseDialog(); return; }
            }
            else if (cancel.Contains(p)) { CloseDialog(); return; }

            if (d.Kind == UiDialog.DialogKind.Picker)
            {
                var content = DialogContentRect();
                for (int i = 0; i < d.Items.Count; i++)
                {
                    var rr = new RectangleF(content.X, content.Y - d.Scroll + i * PickerRowH, content.Width, PickerRowH);
                    if (rr.Contains(p) && rr.Top >= content.Y && rr.Bottom <= content.Bottom)
                    {
                        _dialog = null;
                        Invalidate();
                        d.OnPick?.Invoke(d.Items[i]);
                        return;
                    }
                }
            }
            if (!DialogBoxRect().Contains(p)) CloseDialog();
            return;
        }

        var (row, btn) = HitTest(p);
        if (row >= 0) { _selected = row; Invalidate(); return; }
        if (btn >= 0) HandleButton(btn);
    }

    private void HandleDoubleClick(Point p)
    {
        if (_dialog != null) return;
        var (row, _) = HitTest(p);
        if (_store != null && row >= 0)
        {
            _selected = row;
            Invalidate();
            BeginLoadConfirm(_saves[row], viaButton: true);
        }
    }

    /// <summary>命令按钮统一入口：每个按钮对应一条控制台命令（0-8 项目类，9-15 存档类）。按钮信息一律弹窗显示。</summary>
    private void HandleButton(int idx)
    {
        switch (idx)
        {
            case 0: BeginInput("新建项目", "MyProject", text => RunNewProject(text, viaButton: true)); break;   // new
            case 1: BeginOpenProjectPicker(); break;                                                              // open
            case 2: BeginImportProject(); break;                                                                  // import
            case 3: BeginInfo("项目列表", Join(BuildProjectLines())); break;                                      // projects
            case 4: BeginInput("项目库目录", AppConfig.ProjectsBaseDir, text => ApplyBase(text, viaButton: true)); break; // base
            case 5: BeginDeleteProjectPicker(); break;                                                            // delproject
            case 6:                                                                                               // where
                BeginInfo("项目位置",
                    _store == null
                        ? "未打开项目。\n\n点 [新建项目] 创建，或 [打开项目] 打开一个已有项目。"
                        : $"当前项目: {_store.ProjectName}{( _store.IsExternal ? "（外部存档库）" : "")}\n"
                          + $"{( _store.IsExternal ? "管理的目录" : "项目目录")}: {_store.ProjectDir}\n"
                          + (_store.IsExternal ? $"存档库位置: {_store.StoreDir}" : ""));
                break;
            case 7:                                                                                               // exclude
                if (_store == null) { BeginInfo("排除目录", "还没有打开项目：先点 [新建项目] 或 [导入项目]。"); break; }
                BeginInput("排除目录（逗号分隔，留空=全部清除）",
                    string.Join(", ", _store.ExcludeDirs),
                    text => RunSetExcludes(text, viaButton: true));
                break;
            case 8: BeginInfo("命令帮助", Join(BuildHelpLines(""))); break;                                       // help
            case 9:                                                                                               // save
                if (_store == null) { BeginInfo("存档", "还没有打开项目：先点 [新建项目]。"); break; }
                BeginSavePrompt();
                break;
            case 10:                                                                                              // load
                if (_store == null) { BeginInfo("读档", "还没有打开项目：先点 [新建项目]。"); break; }
                if (_selected < 0) { BeginInfo("读档", "请先点击选中一个存档。"); break; }
                BeginLoadConfirm(_saves[_selected], viaButton: true);
                break;
            case 11:                                                                                              // list
                if (_store == null) { BeginInfo("存档列表", "还没有打开项目：先点 [新建项目]。"); break; }
                BeginInfo("存档列表", Join(BuildSaveLines()));
                break;
            case 12:                                                                                              // status
                if (_store == null) { BeginInfo("状态", "还没有打开项目：先点 [新建项目]。"); break; }
                if (_status == null)
                {
                    BeginInfo("状态", "状态检查中…\n\n后台正在对比当前目录与当前存档，稍后再点一次查看。");
                    RefreshStatusAsync();
                    break;
                }
                BeginInfo("状态", Join(BuildStatusLines()));
                break;
            case 13:                                                                                              // delete
                if (_store == null) { BeginInfo("删除存档", "还没有打开项目：先点 [新建项目]。"); break; }
                if (_selected < 0) { BeginInfo("删除存档", "请先点击选中一个存档。"); break; }
                BeginDeleteConfirm(_saves[_selected], viaButton: true);
                break;
            case 14:                                                                                              // rename
                if (_store == null) { BeginInfo("重命名存档", "还没有打开项目：先点 [新建项目]。"); break; }
                if (_selected < 0) { BeginInfo("重命名存档", "请先点击选中一个存档。"); break; }
                BeginRenamePrompt(_saves[_selected], viaButton: true);
                break;
            case 15: Console.ClearLog(); break;                                                                   // clear
        }
    }

    // ======================== 弹窗 ========================

    private void BeginDialog(UiDialog d)
    {
        _dialog = d;
        _cursorOn = true;
        Invalidate();
    }

    private void BeginInput(string title, string initial, Action<string> onOk) =>
        BeginDialog(new UiDialog { Kind = UiDialog.DialogKind.Input, Title = title, Text = initial, Caret = initial.Length, OnOk = onOk });

    private void BeginConfirm(string title, string message, Action<string> onOk) =>
        BeginDialog(new UiDialog { Kind = UiDialog.DialogKind.Confirm, Title = title, Message = message, OnOk = onOk });

    /// <summary>信息弹窗：长文本自动换行、可滚动（鼠标滚轮）。</summary>
    private void BeginInfo(string title, string text)
    {
        List<string> lines;
        using (var gfx = CreateGraphics())
        {
            lines = WrapLines(CreateCanvas(gfx), text, 10f, false, 536f);
        }
        BeginDialog(new UiDialog { Kind = UiDialog.DialogKind.Info, Title = title, Text = text, Lines = lines });
    }

    /// <summary>列表选择弹窗：点击条目选择（键盘 ↑↓ + 回车也可）。</summary>
    private void BeginPicker(string title, IReadOnlyList<PickerItem> items, Action<PickerItem> onPick) =>
        BeginDialog(new UiDialog
        {
            Kind = UiDialog.DialogKind.Picker,
            Title = title,
            Items = items.ToList(),
            OnPick = onPick,
            SelRow = items.Count > 0 ? 0 : -1,
        });

    private void BeginNewProjectPrompt() => BeginInput("新建项目", "MyProject", text => RunNewProject(text, viaButton: true));

    /// <summary>合并列出全部项目：项目库内部项目 + 已登记的外部存档库项目。</summary>
    private List<(string Name, string Dir, bool External)> ListAllProjects()
    {
        var list = new List<(string Name, string Dir, bool External)>();
        foreach (var p in ProjectStore.ListProjects(AppConfig.ProjectsBaseDir))
            list.Add((p.Name, p.Dir, false));
        foreach (var e in AppConfig.ExternalProjects)
        {
            if (list.Any(x => Path.GetFullPath(x.Dir).Equals(Path.GetFullPath(e.Dir), StringComparison.OrdinalIgnoreCase)))
                continue;
            string name;
            try { name = ProjectStore.Open(e.Dir).ProjectName; }
            catch
            {
                name = Path.GetFileName(e.Dir.TrimEnd(Path.DirectorySeparatorChar));
                if (name.Length == 0) name = e.Dir;
            }
            list.Add((name, e.Dir, true));
        }
        list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return list;
    }

    /// <summary>打开项目弹窗：合并列出项目库与外部存档库项目，点击选择。</summary>
    private void BeginOpenProjectPicker()
    {
        var projects = ListAllProjects();
        if (projects.Count == 0)
        {
            BeginInfo("打开项目",
                "还没有任何项目。\n\n点 [新建项目] 创建，或 [导入项目] 把一个已有目录纳入管理。");
            return;
        }
        BeginPicker("打开项目（点击选择）",
            projects.Select(p => new PickerItem(p.Name + (p.External ? "（外部存档库）" : ""), p.Dir)).ToList(),
            item =>
            {
                try { OpenProjectByStore(ProjectStore.Open(item.Sub), viaButton: true); }
                catch (Exception ex) { BeginInfo("打开项目", "打开失败: " + ex.Message); }
            });
    }

    /// <summary>删除项目弹窗：合并列出项目库与外部存档库项目，点击选择（选定后还需确认）。</summary>
    private void BeginDeleteProjectPicker()
    {
        var projects = ListAllProjects();
        if (projects.Count == 0)
        {
            BeginInfo("删除项目", "还没有任何项目。");
            return;
        }
        BeginPicker("删除项目（点击选择，选定后还需确认）",
            projects.Select(p => new PickerItem(p.Name + (p.External ? "（外部存档库）" : ""), p.Dir)).ToList(),
            item => BeginDeleteProjectConfirm(item.Title, item.Sub, external: item.Title.EndsWith("（外部存档库）", StringComparison.Ordinal), viaButton: true));
    }

    private void BeginDeleteProjectConfirm(string name, string dir, bool external, bool viaButton)
    {
        string what = external
            ? "该项目的外部存档库（.mygit-…，位于 Unity 项目根目录）。\n注意：只删除存档库，被管理的目录（如 Assets）原样保留。"
            : "整个项目目录（含全部存档）将被永久删除。";
        BeginConfirm("确认删除项目", $"删除项目「{name}」？\n{what}\n{dir}",
            _ => RunDeleteProject(dir, viaButton));
    }

    private void BeginSavePrompt()
    {
        string def = DefaultSaveName();
        BeginInput("新建存档", def, text =>
        {
            string n = text.Trim();
            RunSave(n.Length == 0 ? def : n, viaButton: true);
        });
    }

    private void BeginLoadConfirm(SavePoint sp, bool viaButton) =>
        BeginConfirm("确认读档",
            $"将用「{sp.Name}」整体覆盖当前工作区。\n当前未保存的改动会先自动创建「自动恢复存档」，可随时找回。\n\n提示：读档前请先关闭正在使用该项目目录的程序（如 Unity 编辑器），避免文件被占用。",
            _ => RunLoad(sp, viaButton));

    private void BeginDeleteConfirm(SavePoint sp, bool viaButton) =>
        BeginConfirm("确认删除存档",
            $"删除存档「{sp.Name}」？\n该存档的快照数据将被永久删除，无法恢复。",
            _ => RunDelete(sp, viaButton));

    private void BeginRenamePrompt(SavePoint sp, bool viaButton) =>
        BeginInput("重命名存档", sp.Name, text =>
        {
            string n = text.Trim();
            if (n.Length == 0) { BeginInfo("重命名存档", "名称不能为空，已取消。"); return; }
            RunRename(sp, n, viaButton);
        });

    private void ConfirmDialog()
    {
        var d = _dialog;
        _dialog = null;
        Invalidate();
        if (d == null) return;
        if (d.Kind == UiDialog.DialogKind.Picker)
        {
            if (d.SelRow >= 0 && d.SelRow < d.Items.Count)
                d.OnPick?.Invoke(d.Items[d.SelRow]);
            return;
        }
        d.OnOk?.Invoke(d.Kind == UiDialog.DialogKind.Confirm ? "" : d.Text);
    }

    private void CloseDialog()
    {
        _dialog = null;
        Invalidate();
    }

    private RectangleF DialogBoxRect()
    {
        float bw = 580f, bh;
        switch (_dialog!.Kind)
        {
            case UiDialog.DialogKind.Input: bh = 210f; break;
            case UiDialog.DialogKind.Confirm: bh = 190f; break;
            default: bh = 440f; break; // Info / Picker
        }
        return new RectangleF((ViewW - bw) / 2f, (ViewH - bh) / 2f - 30f, bw, bh);
    }

    private RectangleF DialogContentRect()
    {
        var box = DialogBoxRect();
        return new RectangleF(box.X + 16f, box.Y + 46f, box.Width - 32f, box.Height - 46f - 52f);
    }

    private (RectangleF Ok, RectangleF Cancel) DialogButtonRects()
    {
        var box = DialogBoxRect();
        var cancel = new RectangleF(box.Right - 100f, box.Bottom - 42f, 88f, 28f);
        var ok = new RectangleF(cancel.X - 96f, cancel.Y, 88f, 28f);
        return (ok, cancel);
    }

    /// <summary>更新弹窗内部悬停状态，返回是否有变化。</summary>
    private bool UpdateDialogHover(Point p)
    {
        var d = _dialog;
        if (d == null) return false;
        bool oldOk = d.HoverOk, oldCancel = d.HoverCancel;
        int oldRow = d.HoverRow;
        d.HoverOk = d.HoverCancel = false;
        d.HoverRow = -1;

        var (ok, cancel) = DialogButtonRects();
        if (d.HasOk)
        {
            if (ok.Contains(p)) d.HoverOk = true;
            if (cancel.Contains(p)) d.HoverCancel = true;
        }
        else if (cancel.Contains(p))
        {
            d.HoverCancel = true;
        }

        if (d.Kind == UiDialog.DialogKind.Picker)
        {
            var content = DialogContentRect();
            for (int i = 0; i < d.Items.Count; i++)
            {
                var rr = new RectangleF(content.X, content.Y - d.Scroll + i * PickerRowH, content.Width, PickerRowH);
                if (rr.Contains(p) && rr.Top >= content.Y && rr.Bottom <= content.Bottom)
                {
                    d.HoverRow = i;
                    break;
                }
            }
        }
        return oldOk != d.HoverOk || oldCancel != d.HoverCancel || oldRow != d.HoverRow;
    }

    private void AdjustPickerScroll(UiDialog d)
    {
        var content = DialogContentRect();
        float top = d.SelRow * PickerRowH, bottom = top + PickerRowH;
        if (top < d.Scroll) d.Scroll = (int)top;
        else if (bottom - d.Scroll > content.Height) d.Scroll = (int)(bottom - content.Height);
        d.Scroll = Math.Max(0, d.Scroll);
    }

    // ======================== 项目打开 / 刷新 ========================

    /// <summary>打开项目：viaButton=true（按钮/选择弹窗流程）时结果弹窗显示，否则输出控制台。</summary>
    private void OpenProjectByStore(ProjectStore store, bool viaButton)
    {
        string? prev = _store?.ProjectName;
        _store = store;
        SetupWatcher(store.ProjectDir);
        AppConfig.SetLastProject(store.ProjectDir);
        if (store.IsExternal)
            AppConfig.RegisterExternal(store.ProjectDir, store.StoreDir);
        var lines = new List<(string, GameColor)>
        {
            ($"✔ 已打开项目: {store.ProjectName}{(store.IsExternal ? "（外部存档库）" : "")}", Palette.Good),
            ($"{(store.IsExternal ? "管理的目录" : "项目目录")}: {store.ProjectDir}", Palette.DimText),
        };
        if (store.IsExternal)
            lines.Add(($"存档库位置: {store.StoreDir}", Palette.DimText));
        if (prev != null && !prev.Equals(store.ProjectName, StringComparison.Ordinal))
            lines.Add(($"已从「{prev}」切换到「{store.ProjectName}」（原项目数据仍在磁盘上）", Palette.DimText));
        RefreshAll();
        Emit(viaButton, "打开项目", lines);
    }

    private void OpenLastProject()
    {
        string? last = AppConfig.LastProject;
        if (last != null && ProjectStore.IsProjectDir(last))
        {
            try
            {
                OpenProjectByStore(ProjectStore.Open(last), viaButton: false);
                return;
            }
            catch (Exception ex)
            {
                Console.Log("上次项目打开失败: " + ex.Message, Palette.Warn);
            }
        }
        Console.Log("没有已打开的项目：控制台输入 new <项目名> 新建项目，或 open <名称|路径> 打开项目。", Palette.Info);
    }

    /// <summary>重载存档列表 + 选中当前存档 + 触发后台状态检查。</summary>
    private void RefreshAll()
    {
        if (_store == null)
        {
            _saves.Clear();
            _selected = -1;
            _status = null;
            _currentId = null;
            _currentName = null;
            Invalidate();
            return;
        }
        _saves = _store.ListSaves().ToList();
        var cur = _store.CurrentSave;
        _currentId = cur?.Id;
        _currentName = cur?.Name;
        _selected = cur == null ? (_saves.Count > 0 ? 0 : -1) : _saves.FindIndex(s => s.Id == cur.Id);
        _scroll = 0;
        _status = null;
        RefreshStatusAsync();
        Invalidate();
    }

    private void RefreshStatusAsync()
    {
        var store = _store;
        if (store == null || _busy || _statusRefreshing) return;
        _statusRefreshing = true;
        Task.Run(() =>
        {
            DiffResult st;
            try { st = store.GetStatus(); }
            catch (Exception ex)
            {
                SafeInvoke(() =>
                {
                    _statusRefreshing = false;
                    _status = null;
                    Console.Log("状态检查失败: " + ex.Message, Palette.Warn);
                    Invalidate();
                });
                return;
            }
            SafeInvoke(() => { _statusRefreshing = false; _status = st; Invalidate(); });
        });
    }

    private void SetupWatcher(string dir)
    {
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(dir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                           | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            InternalBufferSize = 64 * 1024,
        };
        void Restart() => SafeInvoke(() => { _refreshTimer.Stop(); _refreshTimer.Start(); });
        FileSystemEventHandler onFs = (_, _) => Restart();
        RenamedEventHandler onRen = (_, _) => Restart();
        _watcher.Changed += onFs;
        _watcher.Created += onFs;
        _watcher.Deleted += onFs;
        _watcher.Renamed += onRen;
        _watcher.EnableRaisingEvents = true;
    }

    private void DisposeWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    // ======================== 后台操作（viaButton=true 时结果弹窗，否则控制台） ========================

    private string DefaultSaveName() =>
        _store == null ? "存档" : $"{_store.ProjectName} {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

    /// <summary>按来源输出提示：按钮 → 信息弹窗；控制台 → 日志。</summary>
    private void Notice(bool viaButton, string text, GameColor color)
    {
        if (viaButton) BeginInfo("提示", text);
        else Console.Log(text, color);
    }

    /// <summary>按来源输出多行结果：按钮 → 信息弹窗；控制台 → 逐行日志。</summary>
    private void Emit(bool viaButton, string title, IEnumerable<(string Text, GameColor Color)> lines)
    {
        if (viaButton) BeginInfo(title, Join(lines));
        else foreach (var (t, c) in lines) Console.Log(t, c);
    }

    private static string Join(IEnumerable<(string Text, GameColor Color)> lines) =>
        string.Join("\n", lines.Select(l => l.Text));

    private void RunNewProject(string text, bool viaButton = false)
    {
        string name = text.Trim();
        if (name.Length == 0) { Notice(viaButton, "项目名不能为空。", Palette.Warn); return; }
        try
        {
            var store = ProjectStore.Create(AppConfig.ProjectsBaseDir, name);
            string? prev = _store?.ProjectName;
            _store = store;
            SetupWatcher(store.ProjectDir);
            AppConfig.SetLastProject(store.ProjectDir);
            var lines = new List<(string, GameColor)>
            {
                ($"✔ 已创建项目: {name}", Palette.Good),
                ($"项目目录: {store.ProjectDir}", Palette.DimText),
            };
            if (prev != null)
                lines.Add(($"已从「{prev}」切换（原项目数据仍在磁盘上，可随时打开回来）", Palette.DimText));
            RefreshAll();
            Emit(viaButton, "新建项目", lines);
        }
        catch (Exception ex)
        {
            Notice(viaButton, "创建项目失败: " + ex.Message, Palette.Bad);
        }
    }

    /// <summary>open <名称|路径>：控制台版本；不带参数时改为弹窗选择。</summary>
    private void RunOpenProject(string text)
    {
        string arg = text.Trim();
        if (arg.Length == 0) { BeginOpenProjectPicker(); return; }
        try
        {
            string full = Path.GetFullPath(arg);
            if (Directory.Exists(full) && ProjectStore.IsProjectDir(full))
            {
                OpenProjectByStore(ProjectStore.Open(full), viaButton: false);
                return;
            }
            var projects = ProjectStore.ListProjects(AppConfig.ProjectsBaseDir);
            var exact = projects.Where(p => p.Name.Equals(arg, StringComparison.OrdinalIgnoreCase)).ToList();
            var fuzzy = exact.Count > 0
                ? exact
                : projects.Where(p => p.Name.Contains(arg, StringComparison.OrdinalIgnoreCase)).ToList();
            if (fuzzy.Count == 1) { OpenProjectByStore(ProjectStore.Open(fuzzy[0].Dir), viaButton: false); return; }
            if (fuzzy.Count == 0) Console.Log($"未找到项目「{arg}」：输入 projects 查看项目库。", Palette.Warn);
            else Console.Log("找到多个匹配项目: " + string.Join("、", fuzzy.Select(p => p.Name)) + " —— 请输入更完整的名字。", Palette.Warn);
        }
        catch (Exception ex)
        {
            Console.Log("打开项目失败: " + ex.Message, Palette.Bad);
        }
    }

    /// <summary>导入项目按钮：Windows 文件夹选择框选一个已有目录，原地纳入管理。</summary>
    private void BeginImportProject()
    {
        using var fbd = new FolderBrowserDialog
        {
            Description = "选择要纳入 MyGit 管理的项目文件夹（原地建立存档库，不会移动或复制你的文件）",
            UseDescriptionForTitle = true,
        };
        if (Directory.Exists(AppConfig.ProjectsBaseDir))
            fbd.InitialDirectory = AppConfig.ProjectsBaseDir;
        if (fbd.ShowDialog(this) != DialogResult.OK) return;
        RunImport(fbd.SelectedPath, viaButton: true);
    }

    /// <summary>
    /// 导入已有目录。普通目录 → 内部存档库（目录内 .mygit）；
    /// Unity Assets 内（含子目录）→ 外部存档库（放在 Unity 项目根目录，Unity 不会扫描）。
    /// 已导入过的目录直接打开。目标目录中的现有文件完全不动。
    /// </summary>
    private void RunImport(string dir, bool viaButton = false)
    {
        string full = Path.GetFullPath(dir);
        if (!Directory.Exists(full)) { Notice(viaButton, "目录不存在: " + full, Palette.Bad); return; }
        if (_busy) { Notice(viaButton, "正在处理其他操作，请稍候…", Palette.Warn); return; }
        if (_store != null && Path.GetFullPath(_store.ProjectDir).Equals(full, StringComparison.OrdinalIgnoreCase))
        {
            Notice(viaButton, "该目录就是当前打开的项目。", Palette.Info);
            return;
        }
        if (ProjectStore.IsManagedDir(full))
        {
            try { OpenProjectByStore(ProjectStore.Open(full), viaButton); }
            catch (Exception ex) { Notice(viaButton, "打开已导入目录失败: " + ex.Message, Palette.Bad); }
            return;
        }
        RunImportCore(full, viaButton);
    }

    /// <summary>导入执行体（RunImport 通过各项检查后调用）。</summary>
    private void RunImportCore(string full, bool viaButton)
    {
        bool external = ProjectStore.FindUnityRootOf(full) != null;
        _busy = true;
        Invalidate();
        if (!viaButton) Console.Log($"开始导入: {full}{(external ? "（外部存档库）" : "")}", Palette.Info);
        Action<string>? progress = viaButton ? null : msg => SafeInvoke(() => Console.Log("  " + msg, Palette.DimText));
        Task.Run(() =>
        {
            var lines = new List<(string, GameColor)>();
            ProjectStore? store = null;
            SavePoint? sp = null;
            try
            {
                store = external ? ProjectStore.AdoptExternal(full) : ProjectStore.Adopt(full);
                sp = store.Save($"{store.ProjectName} 导入存档 {DateTime.Now:yyyy-MM-dd HH:mm:ss}", isAuto: false, progress);
            }
            catch (Exception ex)
            {
                lines.Add(($"导入失败: {ex.Message}", Palette.Bad));
            }
            SafeInvoke(() =>
            {
                _busy = false;
                if (store != null && sp != null)
                {
                    string? prev = _store?.ProjectName;
                    _store = store;
                    SetupWatcher(store.ProjectDir);
                    AppConfig.SetLastProject(store.ProjectDir);
                    if (store.IsExternal)
                        AppConfig.RegisterExternal(store.ProjectDir, store.StoreDir);
                    lines.Insert(0, ($"✔ 已导入并打开项目: {store.ProjectName}", Palette.Good));
                    lines.Add(($"{(store.IsExternal ? "管理的目录" : "项目目录")}: {store.ProjectDir}", Palette.DimText));
                    if (store.IsExternal)
                        lines.Add(($"存档库位置: {store.StoreDir}（放在 Assets 外面，Unity 不会扫描）", Palette.Cyan));
                    else if (ProjectStore.IsUnityStyleProject(store.ProjectDir))
                        lines.Add(("检测到 Unity 项目：已自动排除 Library / Temp / Logs / obj / Build / Builds（均可由 Unity 重建）。\n提示：存档 / 读档前请先关闭 Unity 编辑器。", Palette.Cyan));
                    lines.Add(($"已自动创建第一个存档（导入快照）: {sp.Name}", Palette.Good));
                    lines.Add(($"{sp.FileCount} 个文件 · {FormatBytes(sp.TotalBytes)} · {sp.CreatedLocal:yyyy-MM-dd HH:mm:ss}", Palette.DimText));
                    if (prev != null && !prev.Equals(store.ProjectName, StringComparison.Ordinal))
                        lines.Add(($"已从「{prev}」切换（原项目数据仍在磁盘上）", Palette.DimText));
                    RefreshAll();
                }
                Emit(viaButton, "导入项目", lines);
                Invalidate();
            });
        });
    }

    private void RunDeleteProject(string dir, bool viaButton = false)
    {
        if (_busy) { Notice(viaButton, "正在处理其他操作，请稍候…", Palette.Warn); return; }
        _busy = true;
        Invalidate();
        bool wasOpen = _store != null
            && string.Equals(Path.GetFullPath(_store.ProjectDir), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase);
        Task.Run(() =>
        {
            var lines = new List<(string, GameColor)>();
            try
            {
                ProjectStore.DeleteProjectAt(dir);
                AppConfig.UnregisterExternal(dir);
                lines.Add(($"✔ 已删除项目: {dir}", Palette.Good));
            }
            catch (Exception ex)
            {
                lines.Add(($"删除项目失败: {ex.Message}", Palette.Bad));
            }
            SafeInvoke(() =>
            {
                _busy = false;
                if (wasOpen)
                {
                    _store = null;
                    DisposeWatcher();
                }
                RefreshAll();
                Emit(viaButton, "删除项目", lines);
                Invalidate();
            });
        });
    }

    private void RunSave(string name, bool viaButton = false)
    {
        if (_store == null) { Notice(viaButton, "还没有打开项目：先点 [新建项目] 或控制台输入 new <项目名>。", Palette.Warn); return; }
        if (_busy) { Notice(viaButton, "正在处理其他操作，请稍候…", Palette.Warn); return; }
        _busy = true;
        Invalidate();
        var store = _store;
        if (!viaButton) Console.Log($"开始存档: {name}", Palette.Info);
        Action<string>? progress = viaButton ? null : msg => SafeInvoke(() => Console.Log("  " + msg, Palette.DimText));
        Task.Run(() =>
        {
            var lines = new List<(string, GameColor)>();
            try
            {
                var sp = store.Save(name, isAuto: false, progress);
                lines.Add(($"✔ 存档完成: {sp.Name}", Palette.Good));
                lines.Add(($"{sp.FileCount} 个文件 · {FormatBytes(sp.TotalBytes)} · {sp.CreatedLocal:yyyy-MM-dd HH:mm:ss}", Palette.DimText));
            }
            catch (Exception ex)
            {
                lines.Add(($"存档失败: {ex.Message}", Palette.Bad));
            }
            SafeInvoke(() => { _busy = false; RefreshAll(); Emit(viaButton, "存档结果", lines); Invalidate(); });
        });
    }

    private void RunLoad(SavePoint target, bool viaButton = false)
    {
        if (_store == null) { Notice(viaButton, "还没有打开项目。", Palette.Warn); return; }
        if (_busy) { Notice(viaButton, "正在处理其他操作，请稍候…", Palette.Warn); return; }
        _busy = true;
        Invalidate();
        var store = _store;
        if (!viaButton) Console.Log($"开始读档: {target.Name}", Palette.Info);
        Action<string>? progress = viaButton ? null : msg => SafeInvoke(() => Console.Log("  " + msg, Palette.DimText));
        Task.Run(() =>
        {
            var lines = new List<(string, GameColor)>();
            try
            {
                var result = store.Load(target, autoRecover: true, progress);
                if (result.Recovered != null)
                    lines.Add(($"↩ 读档前已自动创建恢复存档: {result.Recovered.Name}", Palette.Warn));
                lines.Add(($"✔ 已读档回到: {target.Name}", Palette.Good));
            }
            catch (Exception ex)
            {
                lines.Add(($"读档失败: {ex.Message}", Palette.Bad));
            }
            SafeInvoke(() => { _busy = false; RefreshAll(); Emit(viaButton, "读档结果", lines); Invalidate(); });
        });
    }

    private void RunDelete(SavePoint sp, bool viaButton = false)
    {
        if (_store == null) { Notice(viaButton, "还没有打开项目。", Palette.Warn); return; }
        if (_busy) { Notice(viaButton, "正在处理其他操作，请稍候…", Palette.Warn); return; }
        _busy = true;
        Invalidate();
        var store = _store;
        Task.Run(() =>
        {
            var lines = new List<(string, GameColor)>();
            try
            {
                store.DeleteSave(sp, viaButton ? null : msg => SafeInvoke(() => Console.Log("  " + msg, Palette.DimText)));
                lines.Add(($"✔ 已删除存档: {sp.Name}", Palette.Good));
            }
            catch (Exception ex)
            {
                lines.Add(($"删除失败: {ex.Message}", Palette.Bad));
            }
            SafeInvoke(() => { _busy = false; RefreshAll(); Emit(viaButton, "删除存档", lines); Invalidate(); });
        });
    }

    private void RunRename(SavePoint sp, string newName, bool viaButton = false)
    {
        if (_store == null) { Notice(viaButton, "还没有打开项目。", Palette.Warn); return; }
        if (_busy) { Notice(viaButton, "正在处理其他操作，请稍候…", Palette.Warn); return; }
        _busy = true;
        Invalidate();
        var store = _store;
        Task.Run(() =>
        {
            var lines = new List<(string, GameColor)>();
            try
            {
                store.RenameSave(sp, newName);
                lines.Add(($"✔ 已重命名: {sp.Name} → {newName}", Palette.Good));
            }
            catch (Exception ex)
            {
                lines.Add(($"重命名失败: {ex.Message}", Palette.Bad));
            }
            SafeInvoke(() => { _busy = false; RefreshAll(); Emit(viaButton, "重命名存档", lines); Invalidate(); });
        });
    }

    // ======================== 信息构建（控制台输出 / 弹窗显示共用） ========================

    private List<(string Text, GameColor Color)> BuildProjectLines()
    {
        var lines = new List<(string, GameColor)>
        {
            ($"项目库目录: {AppConfig.ProjectsBaseDir}", Palette.Info),
            ("已登记的外部存档库项目（管理 Unity Assets 等外部目录）:", Palette.Info),
        };
        var projects = ListAllProjects();
        if (projects.Count == 0)
        {
            lines.Add(("（暂无项目：点 [新建项目] 创建，或 [导入项目] 纳入已有目录）", Palette.DimText));
            return lines;
        }
        foreach (var p in projects)
        {
            bool cur = _store != null
                && string.Equals(Path.GetFullPath(_store.ProjectDir), Path.GetFullPath(p.Dir), StringComparison.OrdinalIgnoreCase);
            lines.Add(($"{(cur ? "▶" : " ")} {p.Name}{(p.External ? "  [外部存档库]" : "")}  —  {p.Dir}", cur ? Palette.Good : Palette.Text));
        }
        return lines;
    }

    private List<(string Text, GameColor Color)> BuildSaveLines()
    {
        var lines = new List<(string, GameColor)> { ($"存档列表（{_saves.Count} 个，最新在前）:", Palette.Info) };
        if (_saves.Count == 0)
        {
            lines.Add(("（还没有存档，点 [存档] 或输入 save 创建第一个存档）", Palette.DimText));
            return lines;
        }
        for (int i = 0; i < _saves.Count; i++)
        {
            var s = _saves[i];
            bool cur = s.Id == _currentId;
            var color = cur ? Palette.Good : s.IsAuto ? Palette.Warn : Palette.Text;
            lines.Add(($"{i + 1}. {s.Name}  ·  {s.CreatedLocal:yyyy-MM-dd HH:mm:ss}  ·  {s.FileCount} 个文件 · {FormatBytes(s.TotalBytes)}"
                + (s.IsAuto ? " · 自动恢复" : "") + (cur ? " · ◀ 当前" : ""), color));
        }
        return lines;
    }

    private List<(string Text, GameColor Color)> BuildStatusLines()
    {
        var lines = new List<(string, GameColor)> { ($"当前存档: {_currentName ?? "（无）"}", Palette.Info) };
        var st = _status;
        if (st == null)
        {
            lines.Add(("状态检查中…（后台自动刷新，稍后再看）", Palette.DimText));
            return lines;
        }
        if (!st.Dirty)
        {
            lines.Add(("工作区与当前存档一致，没有未保存的改动。", Palette.Good));
            return lines;
        }
        lines.Add(($"未保存改动: 新增 {st.Added.Count} · 修改 {st.Modified.Count} · 删除 {st.Deleted.Count}", Palette.Warn));
        foreach (var f in st.Added.Take(60)) lines.Add(("+ " + f, Palette.Good));
        foreach (var f in st.Modified.Take(60)) lines.Add(("~ " + f, Palette.Warn));
        foreach (var f in st.Deleted.Take(60)) lines.Add(("- " + f, Palette.Bad));
        int shown = Math.Min(60, st.Added.Count) + Math.Min(60, st.Modified.Count) + Math.Min(60, st.Deleted.Count);
        int total = st.Added.Count + st.Modified.Count + st.Deleted.Count;
        if (shown < total) lines.Add(($"…（其余 {total - shown} 项省略）", Palette.DimText));
        return lines;
    }

    private List<(string Text, GameColor Color)> BuildHelpLines(string arg)
    {
        var lines = new List<(string, GameColor)>();
        if (arg.Length > 0)
        {
            var hit = CommandHelp.FirstOrDefault(c => c.Cmd.Equals(arg, StringComparison.OrdinalIgnoreCase));
            if (hit != default)
            {
                lines.Add(($"命令说明 — {hit.Cmd}:", Palette.Info));
                lines.Add((hit.Desc, Palette.Text));
            }
            else
            {
                lines.Add(($"未知命令名「{arg}」。直接输入 help 查看全部命令。", Palette.Warn));
            }
            return lines;
        }
        lines.Add(("═══ MyGit 命令帮助（每条命令的中文说明） ═══", Palette.Title));
        foreach (var (cmd, desc) in CommandHelp)
            lines.Add(($"{cmd.PadRight(12)}{desc}", Palette.Text));
        lines.Add(("也可直接用鼠标：单击选中存档 · 双击读档 · 底部按钮操作 · 弹窗内回车确定。", Palette.DimText));
        return lines;
    }

    // ======================== 控制台命令 ========================

    private void RegisterCommands()
    {
        Commands.Register("save", args =>
        {
            if (!EnsureProject()) return;
            string n = args.Trim();
            RunSave(n.Length == 0 ? DefaultSaveName() : n);
        });
        Commands.Register("load", args =>
        {
            if (!EnsureProject()) return;
            var sp = ResolveSave(args.Trim(), allowEmpty: true);
            if (sp != null) BeginLoadConfirm(sp, viaButton: false);
        });
        Commands.Register("delete", args =>
        {
            if (!EnsureProject()) return;
            var sp = ResolveSave(args.Trim(), allowEmpty: false);
            if (sp != null) BeginDeleteConfirm(sp, viaButton: false);
        });
        Commands.Register("rename", RenameCommand);
        Commands.Register("list", _ => CmdList());
        Commands.Register("status", _ => CmdStatus());
        Commands.Register("new", args => RunNewProject(args));
        Commands.Register("open", RunOpenProject);
        Commands.Register("import", args =>
        {
            string path = args.Trim();
            if (path.Length == 0) { Console.Log("用法: import <目录路径> —— 把已有目录原地纳入管理并自动存第一个存档。", Palette.Warn); return; }
            RunImport(path);
        });
        Commands.Register("exclude", ExcludeCommand);
        Commands.Register("projects", _ => CmdProjects());
        Commands.Register("base", args => ApplyBase(args, viaButton: false));
        Commands.Register("delproject", DelProjectCommand);
        Commands.Register("where", _ => CmdWhere());
        // 覆盖库内置 help：输出全部命令的中文说明
        Commands.Register("help", args =>
        {
            foreach (var (t, c) in BuildHelpLines(args.Trim()))
                Console.Log(t, c);
        });
    }

    private bool EnsureProject()
    {
        if (_store != null) return true;
        Console.Log("还没有打开项目：先 new <项目名> 新建项目，或 open <名称|路径> 打开。", Palette.Warn);
        return false;
    }

    private void CmdWhere()
    {
        if (_store == null)
        {
            Console.Log("未打开项目。", Palette.Warn);
            return;
        }
        Console.Log($"项目: {_store.ProjectName}{(_store.IsExternal ? "（外部存档库）" : "")}", Palette.Info);
        Console.Log($"{( _store.IsExternal ? "管理的目录" : "项目目录")}: {_store.ProjectDir}", Palette.Info);
        if (_store.IsExternal)
            Console.Log($"存档库位置: {_store.StoreDir}", Palette.Info);
    }

    private void CmdProjects()
    {
        foreach (var (t, c) in BuildProjectLines())
            Console.Log(t, c);
    }

    private void CmdList()
    {
        if (!EnsureProject()) return;
        foreach (var (t, c) in BuildSaveLines())
            Console.Log(t, c);
    }

    private void CmdStatus()
    {
        if (!EnsureProject()) return;
        foreach (var (t, c) in BuildStatusLines())
            Console.Log(t, c);
        if (_status == null) RefreshStatusAsync();
    }

    private void ApplyBase(string args, bool viaButton)
    {
        string a = args.Trim();
        if (a.Length == 0)
        {
            Notice(viaButton, $"项目库目录: {AppConfig.ProjectsBaseDir}\n用法: base <目录> —— 修改项目库存放位置。", Palette.Info);
            return;
        }
        try
        {
            string full = Path.GetFullPath(a);
            Directory.CreateDirectory(full);
            AppConfig.SetProjectsBase(full);
            Notice(viaButton, $"✔ 项目库目录已改为: {full}", Palette.Good);
        }
        catch (Exception ex)
        {
            Notice(viaButton, "修改项目库失败: " + ex.Message, Palette.Bad);
        }
    }

    /// <summary>按逗号（中英文均可）解析并整体替换排除目录列表（按钮流程）。</summary>
    private void RunSetExcludes(string text, bool viaButton = false)
    {
        if (_store == null) { Notice(viaButton, "还没有打开项目。", Palette.Warn); return; }
        try
        {
            var names = text.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _store.SetExcludeDirs(names);
            string summary = names.Length == 0 ? "（无）" : string.Join("、", names);
            Notice(viaButton, $"✔ 排除目录已更新: {summary}\n（存档与读档都会跳过这些目录，读档时原样保留；.mygit 始终排除）", Palette.Good);
            RefreshAll();
        }
        catch (Exception ex)
        {
            Notice(viaButton, "设置排除目录失败: " + ex.Message, Palette.Bad);
        }
    }

    /// <summary>exclude [list] / exclude add <目录名> / exclude remove <目录名>（控制台版本）。</summary>
    private void ExcludeCommand(string args)
    {
        if (!EnsureProject()) return;
        var store = _store!;
        string a = args.Trim();
        try
        {
            if (a.Length == 0 || a.Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                var list = store.ExcludeDirs;
                Console.Log(list.Count == 0
                    ? "排除目录: （无 —— .mygit 始终排除）"
                    : "排除目录（存档与读档都跳过，读档时原样保留）: " + string.Join("、", list), Palette.Info);
                return;
            }
            int space = a.IndexOf(' ');
            string op = space < 0 ? a : a[..space];
            string arg = space < 0 ? "" : a[(space + 1)..].Trim();
            if (op.Equals("add", StringComparison.OrdinalIgnoreCase))
            {
                if (arg.Length == 0) { Console.Log("用法: exclude add <目录名>", Palette.Warn); return; }
                store.SetExcludeDirs(store.ExcludeDirs.Append(arg));
                Console.Log($"✔ 已加入排除目录: {arg}", Palette.Good);
                RefreshAll();
                return;
            }
            if (op.Equals("remove", StringComparison.OrdinalIgnoreCase))
            {
                if (arg.Length == 0) { Console.Log("用法: exclude remove <目录名>", Palette.Warn); return; }
                store.SetExcludeDirs(store.ExcludeDirs.Where(n => !n.Equals(arg, StringComparison.OrdinalIgnoreCase)));
                Console.Log($"✔ 已移出排除目录: {arg}", Palette.Good);
                RefreshAll();
                return;
            }
            Console.Log("用法: exclude [list] / exclude add <目录名> / exclude remove <目录名>", Palette.Warn);
        }
        catch (Exception ex)
        {
            Console.Log("设置排除目录失败: " + ex.Message, Palette.Bad);
        }
    }

    private void DelProjectCommand(string args)
    {
        string name = args.Trim();
        if (name.Length == 0) { BeginDeleteProjectPicker(); return; }
        var match = ListAllProjects().FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            || p.Name.Replace("（外部存档库）", "").Equals(name, StringComparison.OrdinalIgnoreCase));
        if (match == default)
        {
            Console.Log($"没有名为「{name}」的项目（projects 查看）。", Palette.Warn);
            return;
        }
        BeginDeleteProjectConfirm(match.Name, match.Dir, match.External, viaButton: false);
    }

    /// <summary>按「序号(1 起) | 名称片段」解析存档；arg 为空且 allowEmpty 时用鼠标选中项。找不到时打日志并返回 null。</summary>
    private SavePoint? ResolveSave(string arg, bool allowEmpty)
    {
        if (arg.Length == 0)
        {
            if (!allowEmpty || _selected < 0 || _selected >= _saves.Count)
            {
                Console.Log("请指定存档序号或名称片段（list 查看），或先用鼠标选中一个存档。", Palette.Warn);
                return null;
            }
            return _saves[_selected];
        }
        if (int.TryParse(arg, out int idx))
        {
            if (idx < 1 || idx > _saves.Count)
            {
                Console.Log($"序号越界：共 {_saves.Count} 个存档。", Palette.Warn);
                return null;
            }
            return _saves[idx - 1];
        }
        var matches = _saves.Where(s => s.Name.Contains(arg, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1) return matches[0];
        if (matches.Count == 0) Console.Log($"没有名称包含「{arg}」的存档。", Palette.Warn);
        else Console.Log($"多个存档匹配「{arg}」: {string.Join("、", matches.Select(s => s.Name))}", Palette.Warn);
        return null;
    }

    private void RenameCommand(string args)
    {
        if (!EnsureProject()) return;
        string trimmed = args.Trim();
        if (trimmed.Length == 0)
        {
            if (_selected >= 0) BeginRenamePrompt(_saves[_selected], viaButton: false);
            else Console.Log("用法: rename <序号> <新名称>（不带新名称时弹窗改名）", Palette.Warn);
            return;
        }
        int space = trimmed.IndexOf(' ');
        if (space > 0 && int.TryParse(trimmed[..space], out _))
        {
            var target = ResolveSave(trimmed[..space], allowEmpty: false);
            if (target == null) return;
            string newName = trimmed[(space + 1)..].Trim();
            if (newName.Length == 0) { Console.Log("新名称不能为空。", Palette.Warn); return; }
            RunRename(target, newName);
        }
        else
        {
            var target = ResolveSave(trimmed, allowEmpty: false);
            if (target != null) BeginRenamePrompt(target, viaButton: false);
        }
    }

    // ======================== 演示数据 ========================

    private void DemoSetup()
    {
        try
        {
            string baseDir = AppConfig.ProjectsBaseDir;
            string demoDir = Path.Combine(baseDir, "DemoGame示例");
            if (ProjectStore.IsProjectDir(demoDir))
                ProjectStore.DeleteProjectAt(demoDir);

            var store = ProjectStore.Create(baseDir, "DemoGame示例");
            File.WriteAllText(Path.Combine(store.ProjectDir, "readme.txt"), "DemoGame 示例项目 v1\n这是「游戏进度」：目录里的文件就是你的存档数据。\n");
            Directory.CreateDirectory(Path.Combine(store.ProjectDir, "data"));
            File.WriteAllText(Path.Combine(store.ProjectDir, "data", "角色.txt"), "生命值: 100\n等级: 1\n");
            File.WriteAllText(Path.Combine(store.ProjectDir, "data", "背包.txt"), "金币: 50\n");
            store.Save("DemoGame示例 初始存档", isAuto: false, progress: null);

            File.WriteAllText(Path.Combine(store.ProjectDir, "readme.txt"), "DemoGame 示例项目 v2\n");
            File.WriteAllText(Path.Combine(store.ProjectDir, "data", "背包.txt"), "金币: 80\n");
            store.Save("DemoGame示例 第一章完成", isAuto: false, progress: null);

            // 留一处未存档改动，演示「未保存进度」提示
            File.WriteAllText(Path.Combine(store.ProjectDir, "data", "背包.txt"), "金币: 95\n");

            OpenProjectByStore(store, viaButton: false);
            Console.Log("已创建演示项目 DemoGame示例（2 个存档 + 1 处未保存改动，可试 load 读档体验自动恢复）。", Palette.Info);
        }
        catch (Exception ex)
        {
            Console.Log("演示数据初始化失败: " + ex.Message, Palette.Bad);
        }
    }

    // ======================== 绘制 ========================

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = CreateCanvas(e.Graphics);
        int vw = ViewW, vh = ViewH;

        g.FillRectangle(Palette.WindowBack, 0, 0, vw, vh);
        for (int gx = 0; gx <= vw; gx += 40) g.DrawLine(Palette.DimText, 1f, gx, 0, gx, vh);
        for (int gy = 0; gy <= vh; gy += 40) g.DrawLine(Palette.DimText, 1f, 0, gy, vw, gy);

        DrawHeader(g, vw);
        if (_store == null)
        {
            DrawEmptyState(g, vw);
        }
        else
        {
            g.DrawString($"存档列表 · {_saves.Count} 个（最新在前）", MarginX, ListTop - 22f, 11.5f, true, Palette.Info);
            if (_saves.Count == 0) DrawNoSaves(g, vw);
            else
            {
                DrawCards(g, vw);
                DrawScrollbar(g, vw);
            }
        }
        DrawButtons(g, vw);
        g.DrawString("按钮与命令一一对应（信息弹窗显示） · 单击选中 · 双击读档 · 拖入文件夹即导入", MarginX, vh - 24f, 9.5f, false, Palette.DimText);
        DrawDialog(g, vw, vh);
    }

    private void DrawHeader(ICanvas2D g, int vw)
    {
        g.DrawString("MyGit — 游戏存档式版本管理", MarginX, 14f, 16f, true, Palette.Title);
        g.DrawString("像存档游戏一样管理项目文件：每次「存档」都是完整快照，「读档」整体还原。", MarginX, 38f, 10f, false, Palette.DimText);

        if (_store == null)
        {
            g.DrawString("未打开项目 —— 用下方按钮或控制台命令（new / open）开始", MarginX, 62f, 10.5f, false, Palette.DimText);
        }
        else
        {
            string dirty = _status == null ? "检查中…" : _status.Dirty ? $"{_status.ChangeCount} 项改动" : "无改动";
            string line = $"项目: {_store.ProjectName}   ·   当前存档: {_currentName ?? "（无）"}   ·   未保存进度: {dirty}"
                + (_busy ? "   ·   ⏳ 处理中…" : "");
            GameColor color = _busy ? Palette.Cyan : _status != null && _status.Dirty ? Palette.Warn : Palette.Text;
            g.DrawString(FitText(g, line, 10.5f, false, vw - MarginX * 2), MarginX, 62f, 10.5f, false, color);
        }
        g.DrawLine(GameColor.FromArgb(90, Palette.DimText), 1f, MarginX, 86f, vw - MarginX, 86f);
    }

    private void DrawEmptyState(ICanvas2D g, int vw)
    {
        float cy = ListTop + 100f;
        CenterText(g, "未打开项目", cy, 18f, true, Palette.Title);
        CenterText(g, "MyGit 把项目目录当成「游戏进度」：存档 = 完整快照，读档 = 整体还原，读档前自动保留恢复点。", cy + 36f, 11f, false, Palette.Text);
        CenterText(g, "第一步：点下方 [新建项目] 创建新项目，或 [导入项目] 把别处的目录原地纳入管理（也可直接拖文件夹进窗口）。", cy + 60f, 10.5f, false, Palette.DimText);
        CenterText(g, $"项目库目录: {AppConfig.ProjectsBaseDir}", cy + 86f, 10.5f, false, Palette.DimText);
    }

    private void DrawNoSaves(ICanvas2D g, int vw)
    {
        float cy = ListTop + 90f;
        CenterText(g, "还没有存档", cy, 18f, true, Palette.Title);
        CenterText(g, "点击下方 [存档] 按钮，或在控制台输入 save", cy + 36f, 11f, false, Palette.Text);
        CenterText(g, "存档名默认为「项目名 + 时间」；存档会完整复制当前项目目录（.mygit 除外）。", cy + 60f, 10.5f, false, Palette.DimText);
    }

    private void DrawCards(ICanvas2D g, int vw)
    {
        for (int r = 0; r < _saves.Count; r++)
        {
            var rc = CardRect(r);
            if (rc.Bottom < ListTop || rc.Top > ListBottom) continue;
            var sp = _saves[r];
            bool sel = r == _selected;
            bool hover = r == _hoverRow;
            bool cur = sp.Id == _currentId;

            GameColor fill = sel ? GameColor.FromArgb(40, 120, 200, 255)
                            : hover ? GameColor.FromArgb(28, 90, 140, 190)
                            : GameColor.FromArgb(30, 42, 42, 56);
            g.FillRectangle(fill, rc.X, rc.Y, rc.Width, rc.Height);
            GameColor border = sel ? Palette.Cyan : cur ? Palette.Good : GameColor.FromArgb(60, 180, 180, 180);
            DrawRectBorder(g, border, sel ? 1.8f : 1f, rc);

            GameColor badge = cur ? Palette.Good : sel ? Palette.Cyan : GameColor.FromArgb(90, 150, 160, 170);
            g.FillEllipse(badge, rc.X + 10f, rc.Y + 15f, 32f, 32f);
            string idx = (r + 1).ToString();
            float iw = g.MeasureStringWidth(idx, 12f, true);
            g.DrawString(idx, rc.X + 10f + (32f - iw) / 2f, rc.Y + 22f, 12f, true, Palette.WindowBack);

            string name = sp.IsAuto ? sp.Name + "  [自动恢复]" : sp.Name;
            g.DrawString(FitText(g, name, 11.5f, true, rc.Width - 180f), rc.X + 54f, rc.Y + 10f, 11.5f, true,
                sp.IsAuto ? Palette.Warn : Palette.Text);
            string sub = $"{sp.CreatedLocal:yyyy-MM-dd HH:mm:ss} · {sp.FileCount} 个文件 · {FormatBytes(sp.TotalBytes)}";
            g.DrawString(sub, rc.X + 54f, rc.Y + 37f, 9.5f, false, Palette.DimText);

            if (cur)
            {
                const string tag = "▶ 当前存档";
                float tw = g.MeasureStringWidth(tag, 10f, true);
                g.DrawString(tag, rc.Right - tw - 14f, rc.Y + 12f, 10f, true, Palette.Good);
            }
        }
    }

    private void DrawScrollbar(ICanvas2D g, int vw)
    {
        int maxScroll = Math.Max(0, _saves.Count * (CardH + CardGap) + CardGap - (int)ListH);
        DrawVScrollbar(g, new RectangleF(vw - 30f, ListTop, 10f, ListH), _scroll, maxScroll);
    }

    /// <summary>底部两行命令按钮面板：上排项目命令（0-6），下排存档命令（7-13）。</summary>
    private void DrawButtons(ICanvas2D g, int vw)
    {
        for (int i = 0; i < CmdRow0.Length + CmdRow1.Length; i++)
        {
            var rc = CmdButtonRect(i);
            bool enabled = CmdButtonEnabled(i);
            bool hover = _hoverBtn == i && enabled;
            g.FillRectangle(hover ? GameColor.FromArgb(46, Palette.Info) : GameColor.FromArgb(26, 60, 60, 76), rc.X, rc.Y, rc.Width, rc.Height);
            DrawRectBorder(g, hover ? Palette.Info : GameColor.FromArgb(90, Palette.DimText), 1.2f, rc);
            string label = CmdButtonLabel(i);
            float tw = g.MeasureStringWidth(label, 11f, true);
            GameColor color = !enabled ? GameColor.FromArgb(90, Palette.DimText) : hover ? Palette.Info : Palette.Text;
            g.DrawString(label, rc.X + (rc.Width - tw) / 2f, rc.Y + 9f, 11f, true, color);
        }
    }

    private void DrawDialog(ICanvas2D g, int vw, int vh)
    {
        var d = _dialog;
        if (d == null) return;
        g.FillRectangle(GameColor.FromArgb(150, Palette.WindowBack), 0, 0, vw, vh);

        var box = DialogBoxRect();
        g.FillRectangle(Palette.ConsoleBack, box.X, box.Y, box.Width, box.Height);
        DrawRectBorder(g, Palette.Cyan, 1.8f, box);
        g.DrawString(d.Title, box.X + 22f, box.Y + 16f, 12.5f, true, Palette.Title);

        switch (d.Kind)
        {
            case UiDialog.DialogKind.Input:
                DrawDialogInput(g, box);
                break;
            case UiDialog.DialogKind.Confirm:
            {
                float my = box.Y + 52f;
                foreach (var line in d.Message.Split('\n'))
                {
                    g.DrawString(line, box.X + 22f, my, 10.5f, false, Palette.Text);
                    my += 21f;
                }
                break;
            }
            case UiDialog.DialogKind.Info:
                DrawDialogInfo(g);
                break;
            case UiDialog.DialogKind.Picker:
                DrawDialogPicker(g);
                break;
        }

        var (ok, cancel) = DialogButtonRects();
        if (d.HasOk)
        {
            DrawDialogButton(g, ok, "确定", d.HoverOk);
            DrawDialogButton(g, cancel, "取消", d.HoverCancel);
        }
        else
        {
            DrawDialogButton(g, cancel, d.Kind == UiDialog.DialogKind.Info ? "关闭" : "取消", d.HoverCancel);
        }
    }

    private void DrawDialogInput(ICanvas2D g, RectangleF box)
    {
        var d = _dialog!;
        float ix = box.X + 22f, iy = box.Y + 52f, iw = box.Width - 44f, ih = 34f;
        g.FillRectangle(Palette.InputBarBack, ix, iy, iw, ih);
        DrawRectBorder(g, Palette.Prompt, 1.5f, new RectangleF(ix, iy, iw, ih));

        float startX = ix + 10f;
        float textW = g.MeasureStringWidth(d.Text, 12f, false);
        if (textW > iw - 20f) startX -= textW - (iw - 20f);
        g.DrawString(d.Text, startX, iy + 7f, 12f, false, Palette.InputText);
        if (_cursorOn)
        {
            float cx = startX + g.MeasureStringWidth(d.Text[..d.Caret], 12f, false);
            g.DrawLine(Palette.Prompt, 1.5f, cx, iy + 6f, cx, iy + ih - 6f);
        }
        g.DrawString("回车 = 确定 · Esc = 取消", box.X + 22f, box.Y + box.Height - 30f, 9.5f, false, Palette.DimText);
    }

    private void DrawDialogInfo(ICanvas2D g)
    {
        var d = _dialog!;
        var content = DialogContentRect();
        int maxScroll = Math.Max(0, (int)(d.Lines.Count * InfoLineH - content.Height));
        d.Scroll = Math.Clamp(d.Scroll, 0, maxScroll);
        for (int i = 0; i < d.Lines.Count; i++)
        {
            float ly = content.Y + i * InfoLineH - d.Scroll;
            if (ly + InfoLineH < content.Y || ly > content.Bottom) continue;
            g.DrawString(d.Lines[i], content.X + 6f, ly + 3f, 10f, false, Palette.Text);
        }
        DrawVScrollbar(g, content, d.Scroll, maxScroll);
    }

    private void DrawDialogPicker(ICanvas2D g)
    {
        var d = _dialog!;
        var content = DialogContentRect();
        int maxScroll = Math.Max(0, (int)(d.Items.Count * PickerRowH - content.Height));
        d.Scroll = Math.Clamp(d.Scroll, 0, maxScroll);
        for (int i = 0; i < d.Items.Count; i++)
        {
            var rr = new RectangleF(content.X, content.Y - d.Scroll + i * PickerRowH, content.Width, PickerRowH);
            if (rr.Bottom < content.Y || rr.Top > content.Bottom) continue;
            bool hover = d.HoverRow == i, sel = d.SelRow == i;
            g.FillRectangle(hover || sel ? GameColor.FromArgb(46, Palette.Info) : GameColor.FromArgb(26, 60, 60, 76),
                rr.X, rr.Y, rr.Width, rr.Height - 2f);
            DrawRectBorder(g, hover || sel ? Palette.Info : GameColor.FromArgb(90, Palette.DimText), 1f, rr);
            g.DrawString(FitText(g, d.Items[i].Title, 11f, true, rr.Width - 16f), rr.X + 10f, rr.Y + 5f, 11f, true,
                hover || sel ? Palette.Info : Palette.Text);
            if (d.Items[i].Sub.Length > 0)
                g.DrawString(FitText(g, d.Items[i].Sub, 8.5f, false, rr.Width - 16f), rr.X + 10f, rr.Y + 24f, 8.5f, false, Palette.DimText);
        }
        DrawVScrollbar(g, content, d.Scroll, maxScroll);
    }

    private void DrawDialogButton(ICanvas2D g, RectangleF rc, string label, bool hover)
    {
        g.FillRectangle(hover ? GameColor.FromArgb(46, Palette.Good) : GameColor.FromArgb(26, 60, 60, 76), rc.X, rc.Y, rc.Width, rc.Height);
        DrawRectBorder(g, hover ? Palette.Good : GameColor.FromArgb(90, Palette.DimText), 1.2f, rc);
        float tw = g.MeasureStringWidth(label, 10.5f, true);
        g.DrawString(label, rc.X + (rc.Width - tw) / 2f, rc.Y + 6f, 10.5f, true, hover ? Palette.Good : Palette.Text);
    }

    // ---- 绘制小工具 ----

    private void CenterText(ICanvas2D g, string text, float y, float size, bool bold, GameColor color)
    {
        float w = g.MeasureStringWidth(text, size, bold);
        g.DrawString(text, (ViewW - w) / 2f, y, size, bold, color);
    }

    private static void DrawRectBorder(ICanvas2D g, GameColor c, float t, RectangleF r)
    {
        g.DrawLine(c, t, r.X, r.Y, r.Right, r.Y);
        g.DrawLine(c, t, r.Right, r.Y, r.Right, r.Bottom);
        g.DrawLine(c, t, r.Right, r.Bottom, r.X, r.Bottom);
        g.DrawLine(c, t, r.X, r.Bottom, r.X, r.Y);
    }

    /// <summary>内容区右侧竖向滚动条（无内容溢出时不画）。</summary>
    private void DrawVScrollbar(ICanvas2D g, RectangleF area, int scroll, int maxScroll)
    {
        if (maxScroll <= 0) return;
        float trackX = area.Right - 12f;
        float thumbH = Math.Max(24f, area.Height * area.Height / (area.Height + maxScroll));
        g.FillRectangle(GameColor.FromArgb(60, 120, 120, 132), trackX, area.Y, 8f, area.Height);
        float thumbY = area.Y + (area.Height - thumbH) * (scroll / (float)maxScroll);
        g.FillRectangle(Palette.Cyan, trackX, thumbY, 8f, thumbH);
    }

    /// <summary>超宽文字截断加省略号（GdiCanvas 不自动裁剪）。</summary>
    private static string FitText(ICanvas2D g, string text, float size, bool bold, float maxW)
    {
        if (g.MeasureStringWidth(text, size, bold) <= maxW) return text;
        while (text.Length > 1 && g.MeasureStringWidth(text + "…", size, bold) > maxW)
            text = text[..^1];
        return text + "…";
    }

    /// <summary>按最大宽度贪婪换行（信息弹窗显示用）。</summary>
    private static List<string> WrapLines(ICanvas2D g, string text, float size, bool bold, float maxW)
    {
        var result = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            string line = raw;
            while (g.MeasureStringWidth(line, size, bold) > maxW && line.Length > 1)
            {
                int cut = line.Length;
                while (cut > 1 && g.MeasureStringWidth(line[..cut], size, bold) > maxW) cut--;
                result.Add(line[..cut]);
                line = line[cut..];
            }
            result.Add(line);
        }
        return result;
    }

    private static string FormatBytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.00} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.00} MB",
        >= 1L << 10 => $"{b / 1024.0:0.0} KB",
        _ => $"{b} B",
    };
}
