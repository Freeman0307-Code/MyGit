using System.Drawing.Drawing2D;

namespace DarkTerminalUI;

/// <summary>WinForms 的 <see cref="ICanvas2D"/> 实现：把抽象绘制调用转成 GDI+。</summary>
public sealed class GdiCanvas : ICanvas2D
{
    private readonly Graphics _g;
    private readonly IThemePalette _palette;

    /// <summary>字体缓存（字体名, size, bold）→ Font，避免每帧创建。</summary>
    private static readonly Dictionary<(string Name, float Size, bool Bold), Font> FontCache = new();

    /// <param name="g">目标 Graphics（通常来自 PaintEventArgs.Graphics）。</param>
    /// <param name="palette">主题（决定字体名）；为 null 时使用默认深色主题。</param>
    public GdiCanvas(Graphics g, IThemePalette? palette = null)
    {
        _g = g;
        _palette = palette ?? DarkPalette.Instance;
    }

    public void FillEllipse(GameColor color, float x, float y, float w, float h)
    {
        using var b = new SolidBrush(color.ToColor());
        _g.FillEllipse(b, x, y, w, h);
    }

    public void DrawEllipse(GameColor color, float thickness, float x, float y, float w, float h, bool dashed = false)
    {
        using var p = new Pen(color.ToColor(), thickness);
        if (dashed) p.DashStyle = DashStyle.Dash;
        _g.DrawEllipse(p, x, y, w, h);
    }

    public void FillRectangle(GameColor color, float x, float y, float w, float h)
    {
        using var b = new SolidBrush(color.ToColor());
        _g.FillRectangle(b, x, y, w, h);
    }

    public void DrawLine(GameColor color, float thickness, float x1, float y1, float x2, float y2, bool dashed = false)
    {
        using var p = new Pen(color.ToColor(), thickness);
        if (dashed) p.DashStyle = DashStyle.Dash;
        _g.DrawLine(p, x1, y1, x2, y2);
    }

    public void DrawString(string text, float x, float y, float size, bool bold, GameColor color)
    {
        using var b = new SolidBrush(color.ToColor());
        _g.DrawString(text, GetFont(size, bold), b, x, y);
    }

    public float MeasureStringWidth(string text, float size, bool bold)
    {
        // 注意：不能 Dispose 缓存字体（GetFont 返回的是共享缓存），否则后续同尺寸 DrawString 会因字体已释放而崩溃
        return _g.MeasureString(text, GetFont(size, bold)).Width;
    }

    private Font GetFont(float size, bool bold) =>
        FontCache.TryGetValue((_palette.FontName, size, bold), out var f)
            ? f
            : FontCache[(_palette.FontName, size, bold)] =
                new Font(_palette.FontName, size / 1.33f, bold ? FontStyle.Bold : FontStyle.Regular);
}
