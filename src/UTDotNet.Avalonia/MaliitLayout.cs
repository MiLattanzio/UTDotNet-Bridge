using Avalonia;
using UTDotNet.UbuntuTouch;

namespace UTDotNet.Avalonia;

public static class MaliitLayout
{
    /// <summary>Reserve only the measured intersection; do not double-shrink a compositor-resized window.</summary>
    public static double BottomInset(InputArea keyboard, PixelRect viewport, double renderScale)
    {
        if (renderScale <= 0 || !double.IsFinite(renderScale) || keyboard.Width <= 0 || keyboard.Height <= 0) return 0;
        if ((long)keyboard.X + keyboard.Width <= viewport.X || keyboard.X >= viewport.Right ||
            (long)keyboard.Y + keyboard.Height < viewport.Bottom - 2) return 0;
        var overlap = Math.Max(0, viewport.Bottom - Math.Max(viewport.Y, keyboard.Y));
        return Math.Min(overlap, viewport.Height) / renderScale;
    }
}
