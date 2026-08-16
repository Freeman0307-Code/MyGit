namespace DarkTerminalUI;

/// <summary>
/// 与 UI 框架无关的颜色（R/G/B/A）。WinForms 与 Avalonia 各自转换成自己的颜色类型
/// （WinForms 用 <see cref="GameColorExtensions.ToColor"/>，Avalonia 用 Color.FromArgb）。
/// </summary>
public readonly struct GameColor : IEquatable<GameColor>
{
    public readonly byte A, R, G, B;

    public GameColor(byte a, byte r, byte g, byte b)
    {
        A = a; R = r; G = g; B = b;
    }

    public static GameColor FromArgb(int r, int g, int b) => new(255, (byte)r, (byte)g, (byte)b);
    public static GameColor FromArgb(int a, int r, int g, int b) => new((byte)a, (byte)r, (byte)g, (byte)b);
    public static GameColor FromArgb(int a, GameColor c) => new((byte)a, c.R, c.G, c.B);

    public static readonly GameColor White = FromArgb(255, 255, 255);
    public static readonly GameColor LightGray = FromArgb(211, 211, 211);
    public static readonly GameColor Transparent = new(0, 0, 0, 0);

    public bool Equals(GameColor other) => A == other.A && R == other.R && G == other.G && B == other.B;
    public override bool Equals(object? obj) => obj is GameColor c && Equals(c);
    public override int GetHashCode() => HashCode.Combine(A, R, G, B);
    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}

/// <summary>WinForms 颜色与 <see cref="GameColor"/> 互转的扩展方法。</summary>
public static class GameColorExtensions
{
    /// <summary>转成 WinForms / GDI+ 颜色（System.Drawing.Color）。</summary>
    public static System.Drawing.Color ToColor(this GameColor c) =>
        System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>从 WinForms 颜色转换。</summary>
    public static GameColor ToGameColor(this System.Drawing.Color c) =>
        new(c.A, c.R, c.G, c.B);
}
