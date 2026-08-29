using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SonyBraviaControl.Infrastructure;

public static class AppIconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon CreateIcon(int size = 128)
    {
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        var scale = size / 128f;
        float S(float value) => value * scale;

        using var backgroundPath = RoundedRect(S(5), S(5), S(118), S(118), S(23));
        using var backgroundBrush = new LinearGradientBrush(
            new RectangleF(S(5), S(5), S(118), S(118)),
            Color.FromArgb(255, 28, 35, 42),
            Color.FromArgb(255, 8, 12, 16),
            90f);
        using var outerPen = new Pen(Color.FromArgb(255, 47, 58, 68), Math.Max(1f, S(2)));
        graphics.FillPath(backgroundBrush, backgroundPath);
        graphics.DrawPath(outerPen, backgroundPath);

        // TV body and screen.
        using var tvPath = RoundedRect(S(18), S(20), S(92), S(58), S(8));
        using var tvBrush = new SolidBrush(Color.FromArgb(255, 31, 39, 46));
        using var tvPen = new Pen(Color.FromArgb(255, 83, 96, 107), Math.Max(1f, S(1.5f)));
        graphics.FillPath(tvBrush, tvPath);
        graphics.DrawPath(tvPen, tvPath);

        using var screenPath = RoundedRect(S(23), S(25), S(82), S(45), S(4));
        using var screenBrush = new LinearGradientBrush(
            new RectangleF(S(23), S(25), S(82), S(45)),
            Color.FromArgb(255, 15, 36, 42),
            Color.FromArgb(255, 4, 22, 27),
            90f);
        using var cyanPen = new Pen(Color.FromArgb(255, 25, 218, 219), Math.Max(1.5f, S(2.2f)));
        graphics.FillPath(screenBrush, screenPath);
        graphics.DrawPath(cyanPen, screenPath);

        // TV feet.
        using var feetBrush = new SolidBrush(Color.FromArgb(255, 67, 78, 87));
        graphics.FillPolygon(feetBrush,
        [
            new PointF(S(31), S(78)), new PointF(S(42), S(78)),
            new PointF(S(36), S(87)), new PointF(S(27), S(87))
        ]);
        graphics.FillPolygon(feetBrush,
        [
            new PointF(S(86), S(78)), new PointF(S(97), S(78)),
            new PointF(S(101), S(87)), new PointF(S(92), S(87))
        ]);

        // Green connection/status LED.
        using var ledGlow = new SolidBrush(Color.FromArgb(75, 78, 255, 62));
        using var led = new SolidBrush(Color.FromArgb(255, 92, 255, 69));
        graphics.FillEllipse(ledGlow, S(98), S(65), S(10), S(10));
        graphics.FillEllipse(led, S(100), S(67), S(6), S(6));

        // Remote control in front of the TV.
        using var remotePath = RoundedRect(S(39), S(53), S(50), S(68), S(12));
        using var remoteBrush = new LinearGradientBrush(
            new RectangleF(S(39), S(53), S(50), S(68)),
            Color.FromArgb(255, 39, 47, 54),
            Color.FromArgb(255, 17, 22, 27),
            90f);
        using var remotePen = new Pen(Color.FromArgb(255, 29, 200, 202), Math.Max(1f, S(1.5f)));
        graphics.FillPath(remoteBrush, remotePath);
        graphics.DrawPath(remotePen, remotePath);

        // D-pad ring.
        using var padFill = new SolidBrush(Color.FromArgb(255, 27, 34, 40));
        using var padPen = new Pen(Color.FromArgb(255, 80, 95, 104), Math.Max(1f, S(1.2f)));
        graphics.FillEllipse(padFill, S(48), S(66), S(32), S(32));
        graphics.DrawEllipse(padPen, S(48), S(66), S(32), S(32));
        using var centerPen = new Pen(Color.FromArgb(255, 30, 232, 232), Math.Max(1.5f, S(2.4f)));
        graphics.DrawEllipse(centerPen, S(56), S(74), S(16), S(16));

        // Direction arrows.
        using var arrowPen = new Pen(Color.FromArgb(255, 35, 229, 232), Math.Max(2f, S(3f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawLines(arrowPen,
        [new PointF(S(60), S(71)), new PointF(S(64), S(67)), new PointF(S(68), S(71))]);
        graphics.DrawLines(arrowPen,
        [new PointF(S(60), S(93)), new PointF(S(64), S(97)), new PointF(S(68), S(93))]);
        graphics.DrawLines(arrowPen,
        [new PointF(S(53), S(78)), new PointF(S(49), S(82)), new PointF(S(53), S(86))]);
        graphics.DrawLines(arrowPen,
        [new PointF(S(75), S(78)), new PointF(S(79), S(82)), new PointF(S(75), S(86))]);

        // Small cyan button near the bottom of the remote.
        using var smallButtonPath = RoundedRect(S(52), S(105), S(24), S(7), S(3.5f));
        using var smallButtonBrush = new SolidBrush(Color.FromArgb(255, 27, 218, 220));
        graphics.FillPath(smallButtonBrush, smallButtonPath);

        var hIcon = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(hIcon);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static GraphicsPath RoundedRect(float x, float y, float width, float height, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2f;

        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
