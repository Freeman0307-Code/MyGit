namespace DarkTerminalUI;

/// <summary>
/// 深色终端风格主题调色板：统一提供「语义色」（日志/绘制用）与「表面色」（控件底色）。
/// 所有颜色均为与框架无关的 <see cref="GameColor"/>，需要 WinForms 颜色时调用
/// <see cref="GameColorExtensions.ToColor"/> 转换。
/// 实现 <see cref="IThemePalette"/> 即可定制配色，控件与窗体自动跟随。
/// </summary>
public interface IThemePalette
{
    /// <summary>全局字体名（如 "Microsoft YaHei UI"）。</summary>
    string FontName { get; }

    // ---- 语义色：日志与 HUD 绘制用（与 PositionDemo / TinyAI / XiangqiAI 一致）----

    /// <summary>信息（浅蓝），如 (150,220,255)。</summary>
    GameColor Info { get; }
    /// <summary>成功（浅绿），如 (150,255,150)。</summary>
    GameColor Good { get; }
    /// <summary>警告（橙），如 (255,170,90)。</summary>
    GameColor Warn { get; }
    /// <summary>错误（红），如 (255,120,120)。</summary>
    GameColor Bad { get; }
    /// <summary>标题（金黄），如 (255,215,120)。</summary>
    GameColor Title { get; }
    /// <summary>强调青，如 (120,200,255)。</summary>
    GameColor Cyan { get; }
    /// <summary>弱化文字（半透明白），如 (140,180,180,180)。</summary>
    GameColor DimText { get; }
    /// <summary>普通正文，如 (215,215,215)。</summary>
    GameColor Text { get; }
    /// <summary>命令回显 "&gt; cmd"，如 (180,180,180)。</summary>
    GameColor Echo { get; }
    /// <summary>控制台提示符 ❯，如 (120,200,255)。</summary>
    GameColor Prompt { get; }

    // ---- 表面色：窗体 / 控制台底色 ----

    /// <summary>窗体背景，如 (18,18,24)。</summary>
    GameColor WindowBack { get; }
    /// <summary>控制台面板背景，如 (28,28,36)。</summary>
    GameColor ConsoleBack { get; }
    /// <summary>日志区背景，如 (20,20,28)。</summary>
    GameColor LogBack { get; }
    /// <summary>命令输入条背景，如 (40,40,52)。</summary>
    GameColor InputBarBack { get; }
    /// <summary>输入框文字颜色（白色）。</summary>
    GameColor InputText { get; }
}

/// <summary>
/// 默认深色终端主题（终端风格：近黑底色 + 高饱和语义色）。
/// 数值与 PositionDemo / TinyAI / XiangqiAI 三项目完全一致。
/// </summary>
public sealed class DarkPalette : IThemePalette
{
    /// <summary>全局单例。</summary>
    public static readonly DarkPalette Instance = new();

    private DarkPalette() { }

    public string FontName => "Microsoft YaHei UI";

    public GameColor Info    => GameColor.FromArgb(150, 220, 255);
    public GameColor Good    => GameColor.FromArgb(150, 255, 150);
    public GameColor Warn    => GameColor.FromArgb(255, 170, 90);
    public GameColor Bad     => GameColor.FromArgb(255, 120, 120);
    public GameColor Title   => GameColor.FromArgb(255, 215, 120);
    public GameColor Cyan    => GameColor.FromArgb(120, 200, 255);
    public GameColor DimText => GameColor.FromArgb(140, 180, 180, 180);
    public GameColor Text    => GameColor.FromArgb(215, 215, 215);
    public GameColor Echo    => GameColor.FromArgb(180, 180, 180);
    public GameColor Prompt  => GameColor.FromArgb(120, 200, 255);

    public GameColor WindowBack    => GameColor.FromArgb(18, 18, 24);
    public GameColor ConsoleBack   => GameColor.FromArgb(28, 28, 36);
    public GameColor LogBack       => GameColor.FromArgb(20, 20, 28);
    public GameColor InputBarBack  => GameColor.FromArgb(40, 40, 52);
    public GameColor InputText     => GameColor.White;
}
