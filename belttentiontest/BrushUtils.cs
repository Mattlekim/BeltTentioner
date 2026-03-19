using System;
using System.Drawing;

public static class BrushUtils
{
    public static Brush LerpBrush(Brush from, Brush to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        if (from is SolidBrush s1 && to is SolidBrush s2)
        {
            Color c = LerpColor(s1.Color, s2.Color, t);
            return new SolidBrush(c);
        }

        // Fallback: clone the nearer brush (so caller can dispose safely)
        try
        {
            return (Brush)((t < 0.5f ? from : to)?.Clone() ?? (from ?? to));
        }
        catch
        {
            // If cloning not supported, return the original (caller should not dispose)
            return t < 0.5f ? from ?? to : to ?? from;
        }
    }

    private static Color LerpColor(Color a, Color b, float t)
    {
        int A = (int)Math.Round(a.A + (b.A - a.A) * t);
        int R = (int)Math.Round(a.R + (b.R - a.R) * t);
        int G = (int)Math.Round(a.G + (b.G - a.G) * t);
        int B = (int)Math.Round(a.B + (b.B - a.B) * t);
        return Color.FromArgb(A, R, G, B);
    }
}