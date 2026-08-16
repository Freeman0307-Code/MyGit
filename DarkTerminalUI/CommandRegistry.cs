namespace DarkTerminalUI;

/// <summary>控制台命令处理器：args 为命令名之后的部分（去掉首尾空格，可为空串）。</summary>
public delegate void ConsoleCommandHandler(string args);

/// <summary>
/// 控制台命令注册表：把「输入命令 → 分发到处理器」做成可注册的接口，
/// 窗体通过 <see cref="TerminalForm.Commands"/> 暴露给外部注册自己的命令。
/// 命令名不区分大小写；后注册的同名命令覆盖先注册的。
/// </summary>
public interface ICommandRegistry
{
    /// <summary>注册命令。name 不区分大小写；同名覆盖。</summary>
    void Register(string name, ConsoleCommandHandler handler);

    /// <summary>注销命令。</summary>
    void Unregister(string name);

    /// <summary>是否已注册该命令名（不区分大小写）。</summary>
    bool IsRegistered(string name);

    /// <summary>已注册的命令名（按字母序）。</summary>
    IEnumerable<string> Names { get; }

    /// <summary>
    /// 执行一行命令文本（"名字 参数…"）。第一个词为命令名，其余为参数。
    /// 返回 true 表示命中已注册命令；false 表示未知命令（由调用方决定如何提示）。
    /// </summary>
    bool Execute(string line);
}

/// <summary><see cref="ICommandRegistry"/> 的默认实现。</summary>
public sealed class CommandRegistry : ICommandRegistry
{
    private readonly Dictionary<string, ConsoleCommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string name, ConsoleCommandHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[name.Trim()] = handler;
    }

    public void Unregister(string name) => _handlers.Remove(name);

    public bool IsRegistered(string name) => _handlers.ContainsKey(name);

    public IEnumerable<string> Names => _handlers.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

    public bool Execute(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return true;
        string trimmed = line.Trim();
        int space = trimmed.IndexOf(' ');
        string name = space < 0 ? trimmed : trimmed[..space];
        string args = space < 0 ? "" : trimmed[(space + 1)..].Trim();
        if (_handlers.TryGetValue(name, out var handler))
        {
            handler(args);
            return true;
        }
        return false;
    }
}
