using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WpfLaunchSimulation.Rendering;

// Body textures drawn once at start-up: white skin with panel seams, a raceway shadow line, and the Korean flag
// with a vertical "대한민국" on stage 1. Marking layout is illustrative (from public launch photos), not a drawing.
// Texture u runs around the body (0..1 = 360°), v runs from the top (0) to the bottom (1) of a section.
internal static class NuriTextures
{
    public static readonly Lazy<ImageBrush> Stage1 = new(() => Body(1.75, 19.4, seams: [3.0, 9.5, 14.5, 18.4], flag: true));
    public static readonly Lazy<ImageBrush> Stage2 = new(() => Body(1.3, 12.4, seams: [2.2, 8.5, 11.6], flag: false));
    public static readonly Lazy<ImageBrush> Stage3 = new(() => Body(1.3, 6.2, seams: [1.0, 5.4], flag: false));
    public static readonly Lazy<ImageBrush> Fairing = new(() => Body(1.3, 5.0, seams: [], flag: false, fairing: true));

    private const double PixelsPerMetre = 80;

    private static ImageBrush Body(double radius, double length, double[] seams, bool flag, bool fairing = false)
    {
        int width = (int)Math.Round(2 * Math.PI * radius * PixelsPerMetre), height = (int)Math.Round(length * PixelsPerMetre);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(236, 238, 240)), null, new Rect(0, 0, width, height));
            // Faint vertical streaks so large white areas do not look flat.
            var random = new Random(radius > 1.5 ? 11 : fairing ? 33 : 22);
            for (int i = 0; i < 70; i++)
            {
                double x = random.NextDouble() * width, w = 2 + random.NextDouble() * 10;
                byte shade = (byte)(220 + random.Next(16));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, shade, shade, (byte)(shade + 4))), null, new Rect(x, 0, w, height));
            }
            var seamPen = new Pen(new SolidColorBrush(Color.FromRgb(168, 172, 178)), 2.2);
            foreach (double s in seams) dc.DrawLine(seamPen, new Point(0, s * PixelsPerMetre), new Point(width, s * PixelsPerMetre));
            if (fairing) dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(150, 154, 160)), 3), new Point(width * 0.25, 0), new Point(width * 0.25, height));
            if (fairing) dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(150, 154, 160)), 3), new Point(width * 0.75, 0), new Point(width * 0.75, height));
            // Raceway shadow along u = 0 (the raceway box itself is geometry).
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 90, 94, 100)), null, new Rect(0, 0, 10, height));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 90, 94, 100)), null, new Rect(width - 10, 0, 10, height));
            if (flag)
                foreach (double u in new[] { 0.25, 0.75 })
                {
                    double cx = u * width, top = 1.2 * PixelsPerMetre;
                    DrawFlag(dc, new Rect(cx - 0.9 * PixelsPerMetre, top, 1.8 * PixelsPerMetre, 1.2 * PixelsPerMetre));
                    DrawVerticalText(dc, "대한민국", cx, top + 1.6 * PixelsPerMetre, 0.95 * PixelsPerMetre);
                }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        var brush = new ImageBrush(bitmap) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
        brush.Freeze(); return brush;
    }

    private static void DrawVerticalText(DrawingContext dc, string text, double centerX, double top, double size)
    {
        var typeface = new Typeface(new FontFamily("Malgun Gothic"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        double y = top;
        foreach (char c in text)
        {
            var glyph = new FormattedText(c.ToString(), CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight, typeface, size, new SolidColorBrush(Color.FromRgb(28, 30, 36)), 1.0);
            dc.DrawText(glyph, new Point(centerX - glyph.Width / 2, y)); y += size * 1.15;
        }
    }

    // Taegukgi (3:2): red-over-blue taeguk tilted along the diagonal, four trigrams in the corners.
    private static void DrawFlag(DrawingContext dc, Rect r)
    {
        dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(200, 200, 200)), 1), r);
        var center = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        double radius = r.Height / 4, angle = Math.Atan2(r.Height, r.Width) * 180 / Math.PI;
        var red = new SolidColorBrush(Color.FromRgb(205, 46, 58)); var blue = new SolidColorBrush(Color.FromRgb(0, 71, 160));
        dc.PushTransform(new RotateTransform(angle, center.X, center.Y));
        dc.DrawEllipse(blue, null, center, radius, radius);
        var top = new PathGeometry([new PathFigure(new Point(center.X - radius, center.Y),
            [new ArcSegment(new Point(center.X + radius, center.Y), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true)], true)]);
        dc.DrawGeometry(red, null, top);
        dc.DrawEllipse(red, null, new Point(center.X - radius / 2, center.Y), radius / 2, radius / 2);
        dc.DrawEllipse(blue, null, new Point(center.X + radius / 2, center.Y), radius / 2, radius / 2);
        dc.Pop();
        // Trigrams: geon (☰) upper-left, gam (☵) upper-right, ri (☲) lower-left, gon (☷) lower-right; bars broken where 0.
        void Trigram(double dx, double dy, int[] solid)
        {
            var c = new Point(center.X + dx * r.Width * 0.33, center.Y + dy * r.Height * 0.32);
            double barW = r.Height * 0.25, barH = r.Height * 0.042, gap = r.Height * 0.063;
            double rot = Math.Atan2(dy * r.Height, dx * r.Width) * 180 / Math.PI + 90;
            dc.PushTransform(new RotateTransform(rot, c.X, c.Y));
            for (int k = 0; k < 3; k++)
            {
                double y = c.Y + (k - 1) * gap - barH / 2;
                if (solid[k] == 1) dc.DrawRectangle(Brushes.Black, null, new Rect(c.X - barW / 2, y, barW, barH));
                else { dc.DrawRectangle(Brushes.Black, null, new Rect(c.X - barW / 2, y, barW * 0.45, barH)); dc.DrawRectangle(Brushes.Black, null, new Rect(c.X + barW * 0.05, y, barW * 0.45, barH)); }
            }
            dc.Pop();
        }
        Trigram(-1, -1, [1, 1, 1]); Trigram(1, -1, [0, 1, 0]); Trigram(-1, 1, [1, 0, 1]); Trigram(1, 1, [0, 0, 0]);
    }
}
