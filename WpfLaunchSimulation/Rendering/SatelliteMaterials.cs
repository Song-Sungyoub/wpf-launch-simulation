using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;

namespace WpfLaunchSimulation.Rendering;

// Small procedural vector textures.
public static class SatelliteMaterials
{
    public static readonly MaterialGroup Foil = MakeFoil();
    public static readonly MaterialGroup Cells = MakeCells();

    private static MaterialGroup Finish(DrawingGroup drawing, Color specular, double power)
    {
        drawing.Freeze();
        var brush = SceneGeometry.BakeBrush(drawing, 512, 512);
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(brush));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(specular), power));
        material.Freeze(); return material;
    }

    private static MaterialGroup MakeFoil()
    {
        // Fine warped creases, baked into one opaque texture.
        const int size = 384;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double u = (double)x / size, v = (double)y / size;
                double broad = PadSurroundings.Fbm(u * 8 + 11, v * 8 + 3, 3);
                double fine = PadSurroundings.Fbm(u * 42, v * 42 + 17, 2);
                double wave = Math.Sin(u * 115 + Math.Sin(v * 31) * 2.5 + broad * 18);
                double cross = Math.Sin(v * 141 + Math.Sin(u * 24) * 3 + broad * 12);
                double ridge = Math.Pow(Math.Abs(wave), 10) * 0.20 + Math.Pow(Math.Abs(cross), 14) * 0.10;
                double shade = 0.70 + broad * 0.32 + fine * 0.13 + ridge;
                int p = (y * size + x) * 4;
                pixels[p] = (byte)Math.Clamp(48 * shade, 0, 255);
                pixels[p + 1] = (byte)Math.Clamp(157 * shade, 0, 255);
                pixels[p + 2] = (byte)Math.Clamp(219 * shade, 0, 255);
                pixels[p + 3] = 255;
            }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4); bitmap.Freeze();
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(new ImageBrush(bitmap), null, new RectangleGeometry(new Rect(0, 0, 256, 256))));
        return Finish(drawing, Color.FromRgb(176, 143, 74), 38);
    }

    private static MaterialGroup MakeCells()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(12, 18, 29)), null, new RectangleGeometry(new Rect(0, 0, 256, 256))));
        const int rows = 24, columns = 14;
        double cellWidth = 248.0 / columns, cellHeight = 248.0 / rows;
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                double x = 4 + col * cellWidth, y = 4 + row * cellHeight;
                byte variation = (byte)((col * 7 + row * 3) % 9);
                var fill = new LinearGradientBrush(Color.FromRgb(13, 22, (byte)(39 + variation)), Color.FromRgb(28, 38, (byte)(65 + variation)), 80);
                drawing.Children.Add(new GeometryDrawing(fill, new Pen(new SolidColorBrush(Color.FromRgb(92, 98, 109)), 0.35),
                    new RectangleGeometry(new Rect(x + 0.5, y + 0.5, cellWidth - 1, cellHeight - 1), 0.7, 0.7)));
                foreach (double fraction in new[] { 0.25, 0.5, 0.75 })
                    drawing.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromRgb(83, 94, 120)), 0.18),
                        new LineGeometry(new Point(x + cellWidth * fraction, y + 1), new Point(x + cellWidth * fraction, y + cellHeight - 1))));
                drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(162, 166, 177)), null,
                    new RectangleGeometry(new Rect(x + cellWidth * 0.5 - 0.4, y + cellHeight - 1.3, 0.8, 0.7))));
            }
        return Finish(drawing, Color.FromRgb(100, 118, 155), 75);
    }
}
