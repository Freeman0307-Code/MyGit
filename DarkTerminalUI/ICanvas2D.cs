namespace DarkTerminalUI;

/// <summary>
/// 2D 绘制抽象：WinForms（<see cref="GdiCanvas"/>）与 Avalonia（外部实现）各自实现，
/// 逻辑层的 Draw 方法只依赖本接口，不依赖任何 UI 框架。
/// 坐标均为屏幕像素（float）。
/// </summary>
public interface ICanvas2D
{
    void FillEllipse(GameColor color, float x, float y, float w, float h);
    void DrawEllipse(GameColor color, float thickness, float x, float y, float w, float h, bool dashed = false);
    void FillRectangle(GameColor color, float x, float y, float w, float h);
    void DrawLine(GameColor color, float thickness, float x1, float y1, float x2, float y2, bool dashed = false);
    void DrawString(string text, float x, float y, float size, bool bold, GameColor color);

    /// <summary>测量文字宽度（用于居中）。</summary>
    float MeasureStringWidth(string text, float size, bool bold);
}
