namespace DarkTerminalUI;

/// <summary>
/// 文字控制台面板的抽象接口：日志输出 + 终端式命令输入。
/// 窗体只依赖本接口（通过 <see cref="TerminalForm.Console"/> 暴露），
/// 便于替换实现（如联机镜像控制台）或单元测试。
/// </summary>
public interface IConsolePanel
{
    /// <summary>底层控件（用于停靠到窗体、调整尺寸）。</summary>
    Control Control { get; }

    /// <summary>用户在输入条按回车提交命令时触发（参数为去掉首尾空格的命令文本）。</summary>
    event EventHandler<string>? CommandEntered;

    /// <summary>整行日志输出后触发（打字机逐字过程不触发；联机镜像用）。</summary>
    event Action<string, GameColor>? Logged;

    /// <summary>命令输入框是否拥有焦点（有焦点时窗体不应响应快捷键，避免打字误触发）。</summary>
    bool InputHasFocus { get; }

    /// <summary>日志区是否选中了文字。</summary>
    bool HasSelection { get; }

    /// <summary>让命令输入框获得焦点。</summary>
    void FocusInput();

    /// <summary>清空日志区。</summary>
    void ClearLog();

    /// <summary>复制选中的日志到剪贴板（不依赖焦点）。</summary>
    void CopySelection();

    /// <summary>全选日志区。</summary>
    void SelectAllLog();

    /// <summary>追加一行普通灰色日志。</summary>
    void Log(string text);

    /// <summary>追加一行带颜色的日志。</summary>
    void Log(string text, GameColor color);

    /// <summary>逐字弹出（打字机效果）输出一行文字，约 40ms/字。</summary>
    void TypeLog(string text, GameColor color);
}
