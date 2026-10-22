using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace WpfLaunchSimulation.Rendering;

// Ground around the pad, in pad-local metres (+X east, +Y up, +Z south). The colours come from the NASA Landsat pad-area
// image (land/sea and vegetation where they really are), sharpened with procedural canopy and wave detail. The hill
// relief is illustrative [근사]: no elevation data is shipped, so land rises with smooth noise away from the pad clearing
// (Naro sits on a hilly island; nearby peaks are a few hundred metres). Concrete and building textures are procedural.
public static class PadSurroundings
{
    private const double Extent = 3000;          // terrain covers ±3 km
    private const int Grid = 160, TexSize = 2048;

    // area: the pad-area image; bounds: its lat/lon box. padLat/padLon: pad position.
    public static GeometryModel3D Terrain(BitmapSource area, (double LatMin, double LatMax, double LonMin, double LonMax) bounds, double padLat, double padLon)
    {
        double mPerDegLat = 111320, mPerDegLon = 111320 * Math.Cos(padLat * Math.PI / 180);
        // Source pixels covering the terrain square.
        (double X, double Y) ToPixel(double x, double z)
        {
            double lat = padLat - z / mPerDegLat, lon = padLon + x / mPerDegLon;
            return ((lon - bounds.LonMin) / (bounds.LonMax - bounds.LonMin) * area.PixelWidth,
                    (bounds.LatMax - lat) / (bounds.LatMax - bounds.LatMin) * area.PixelHeight);
        }
        var (px0, py0) = ToPixel(-Extent, -Extent); var (px1, py1) = ToPixel(Extent, Extent);
        int sx = (int)Math.Floor(px0) - 1, sy = (int)Math.Floor(py0) - 1, sw = (int)Math.Ceiling(px1) - sx + 2, sh = (int)Math.Ceiling(py1) - sy + 2;
        var source = new FormatConvertedBitmap(area, PixelFormats.Bgra32, null, 0);
        var src = new byte[sw * sh * 4];
        source.CopyPixels(new Int32Rect(sx, sy, sw, sh), src, sw * 4, 0);
        (double R, double G, double B) Sample(double x, double z)
        {
            var (fx, fy) = ToPixel(x, z); fx -= sx + 0.5; fy -= sy + 0.5;
            int ix = Math.Clamp((int)Math.Floor(fx), 0, sw - 2), iy = Math.Clamp((int)Math.Floor(fy), 0, sh - 2);
            double ax = Math.Clamp(fx - ix, 0, 1), ay = Math.Clamp(fy - iy, 0, 1);
            double C(int c) { double P(int x0, int y0) => src[(y0 * sw + x0) * 4 + c];
                return (P(ix, iy) * (1 - ax) + P(ix + 1, iy) * ax) * (1 - ay) + (P(ix, iy + 1) * (1 - ax) + P(ix + 1, iy + 1) * ax) * ay; }
            return (C(2), C(1), C(0));
        }
        // Sea pixels have green ≈ blue; vegetation has clearly more green than blue.
        double Land(double x, double z) { var (_, g, b) = Sample(x, z); return Math.Clamp((g - b - 8) / 20, 0, 1); }

        // Texture: upsampled Landsat with canopy speckle on land and fine ripples on the sea.
        var pixels = new byte[TexSize * TexSize * 4];
        for (int j = 0; j < TexSize; j++)
            for (int i = 0; i < TexSize; i++)
            {
                double x = -Extent + (i + 0.5) / TexSize * 2 * Extent, z = -Extent + (j + 0.5) / TexSize * 2 * Extent;
                var (r, g, b) = Sample(x, z);
                double land = Math.Clamp((g - b - 8) / 20, 0, 1);
                double canopy = 0.75 + 0.5 * Fbm(x / 9, z / 9, 3);                  // tree crowns ~10 m
                double ripple = 0.92 + 0.16 * Fbm(x / 6, z / 30, 2);                 // swell lines
                double k = land * canopy + (1 - land) * ripple;
                int p = (j * TexSize + i) * 4;
                // Fade out toward the square's edge so it blends into the far-layer ground patch (same image underneath).
                double fade = 1 - Smooth(Extent * 0.55, Extent * 0.97, Math.Max(Math.Abs(x), Math.Abs(z)));
                pixels[p] = (byte)Math.Clamp(b * k, 0, 255); pixels[p + 1] = (byte)Math.Clamp(g * k, 0, 255);
                pixels[p + 2] = (byte)Math.Clamp(r * k, 0, 255); pixels[p + 3] = (byte)(255 * fade);
            }
        var texture = BitmapSource.Create(TexSize, TexSize, 96, 96, PixelFormats.Bgra32, null, pixels, TexSize * 4); texture.Freeze();
        // Drawn before the smoke and clouds (scene order), so its translucent rim does not hide them.
        var material = new DiffuseMaterial(new ImageBrush(texture) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) });
        material.Freeze();

        // Height: flat clearing around the pad, hills on land beyond, fading to sea level at the edge of the square.
        var mesh = new MeshGeometry3D();
        var heights = new double[Grid + 1, Grid + 1];
        for (int j = 0; j <= Grid; j++)
            for (int i = 0; i <= Grid; i++)
            {
                double x = -Extent + 2 * Extent * i / Grid, z = -Extent + 2 * Extent * j / Grid;
                double d = Math.Sqrt(x * x + z * z);
                // The clearing keeps the pad ground camera (~420 m out) in the open.
                double clearing = Smooth(450, 1300, d), edge = 1 - Smooth(Extent * 0.75, Extent, Math.Max(Math.Abs(x), Math.Abs(z)));
                double hill = 25 + 190 * Fbm(x / 900, z / 900, 4);
                heights[i, j] = -1.3 + Land(x, z) * clearing * edge * hill;
            }
        for (int j = 0; j <= Grid; j++)
            for (int i = 0; i <= Grid; i++)
            {
                double x = -Extent + 2 * Extent * i / Grid, z = -Extent + 2 * Extent * j / Grid;
                mesh.Positions.Add(new Point3D(x, heights[i, j], z));
                double hx = heights[Math.Min(i + 1, Grid), j] - heights[Math.Max(i - 1, 0), j], hz = heights[i, Math.Min(j + 1, Grid)] - heights[i, Math.Max(j - 1, 0)];
                var n = new Vector3D(-hx, 4 * Extent / Grid, -hz); n.Normalize(); mesh.Normals.Add(n);
                mesh.TextureCoordinates.Add(new Point((double)i / Grid, (double)j / Grid));
            }
        for (int j = 0; j < Grid; j++)
            for (int i = 0; i < Grid; i++)
            {
                int a = j * (Grid + 1) + i, b = a + 1, c = a + Grid + 1, d = c + 1;
                foreach (int k in new[] { a, c, b, b, c, d }) mesh.TriangleIndices.Add(k);
            }
        mesh.Freeze();
        return new GeometryModel3D(mesh, material);
    }

    // Concrete apron: a horizontal quad with 8 m slabs, joints, stains and darker scorch toward the flame trench.
    public static GeometryModel3D Apron(double y, double sizeX, double sizeZ)
    {
        var mesh = new MeshGeometry3D();
        foreach (var (x, z) in new[] { (-sizeX / 2, -sizeZ / 2), (sizeX / 2, -sizeZ / 2), (sizeX / 2, sizeZ / 2), (-sizeX / 2, sizeZ / 2) })
        {
            mesh.Positions.Add(new Point3D(x, y, z)); mesh.Normals.Add(new Vector3D(0, 1, 0));
            mesh.TextureCoordinates.Add(new Point(x / 8, z / 8));
        }
        mesh.TriangleIndices = new Int32Collection([0, 2, 1, 0, 3, 2]); mesh.Freeze();
        var brush = new ImageBrush(ConcreteSlab()) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
        var material = new DiffuseMaterial(brush); material.Freeze();
        return new GeometryModel3D(mesh, material);
    }

    private static BitmapSource ConcreteSlab()
    {
        const int size = 256; var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double u = (double)x / size, v = (double)y / size;
                double shade = 172 + 22 * (Fbm(u * 6, v * 6, 4) - 0.5) + 10 * (Fbm(u * 40, v * 40, 2) - 0.5);
                double stain = Math.Max(0, Fbm(u * 2 + 7, v * 2 + 3, 3) - 0.6) * 90;
                if (x < 2 || y < 2) shade = 120;                                   // expansion joints at the slab edges
                byte s = (byte)Math.Clamp(shade - stain, 0, 255);
                int p = (y * size + x) * 4; pixels[p] = (byte)Math.Max(0, s - 6); pixels[p + 1] = (byte)Math.Max(0, s - 2); pixels[p + 2] = s; pixels[p + 3] = 255;
            }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4); bitmap.Freeze();
        return bitmap;
    }

    // Box with metre-scaled UVs and a panelled facade (vertical siding, a window band, roll-up door on the long side).
    public static GeometryModel3D Building(double cx, double cy, double cz, double sx, double sy, double sz, Color wall)
    {
        var mesh = new MeshGeometry3D();
        void Face(Vector3D n, Vector3D u, Vector3D v, double width, double height)
        {
            int b = mesh.Positions.Count; var c = new Point3D(cx, cy, cz) + new Vector3D(n.X * sx / 2, n.Y * sy / 2, n.Z * sz / 2);
            var du = new Vector3D(u.X * sx / 2, u.Y * sy / 2, u.Z * sz / 2); var dv = new Vector3D(v.X * sx / 2, v.Y * sy / 2, v.Z * sz / 2);
            var corners = new[] { c - du - dv, c + du - dv, c + du + dv, c - du + dv };
            var uv = new[] { new Point(0, 12), new Point(width, 12), new Point(width, 12 - height), new Point(0, 12 - height) }; // base at the tile bottom
            for (int k = 0; k < 4; k++) { mesh.Positions.Add(corners[k]); mesh.Normals.Add(n); mesh.TextureCoordinates.Add(new Point(uv[k].X / 12, uv[k].Y / 12)); }
            foreach (int k in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(b + k);
        }
        Face(new(1, 0, 0), new(0, 0, -1), new(0, 1, 0), sz, sy); Face(new(-1, 0, 0), new(0, 0, 1), new(0, 1, 0), sz, sy);
        Face(new(0, 0, 1), new(1, 0, 0), new(0, 1, 0), sx, sy); Face(new(0, 0, -1), new(-1, 0, 0), new(0, 1, 0), sx, sy);
        mesh.Freeze();
        var brush = new ImageBrush(Facade(wall)) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(brush));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(40, 40, 40)), 20));
        material.Freeze();
        return new GeometryModel3D(mesh, material);
    }

    // One 12 m × 12 m tile of wall; the building base maps to the tile bottom.
    private static BitmapSource Facade(Color wall)
    {
        const int size = 192; double m = size / 12.0;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(wall), null, new Rect(0, 0, size, size));
            var rib = new SolidColorBrush(Color.FromArgb(38, 0, 0, 0));
            for (double x = 0; x < size; x += 0.5 * m) dc.DrawRectangle(rib, null, new Rect(x, 0, 1.2, size));
            var glass = new SolidColorBrush(Color.FromRgb(70, 92, 112));
            for (double x = 0.6 * m; x < size; x += 3 * m) dc.DrawRectangle(glass, null, new Rect(x, 6.5 * m, 1.8 * m, 1.1 * m));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, new Rect(0, size - 0.4 * m, size, 0.4 * m)); // grime at the base
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
        return bitmap;
    }

    private static double Smooth(double a, double b, double x) { double t = Math.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }

    // Value-noise fBm in 0..1.
    internal static double Fbm(double x, double y, int octaves)
    {
        double sum = 0, amp = 0.5, norm = 0;
        for (int o = 0; o < octaves; o++) { sum += amp * Value(x, y); norm += amp; x *= 2.03; y *= 2.03; amp *= 0.5; }
        return sum / norm;
    }
    private static double Value(double x, double y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y); double fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        double H(int a, int b) { uint h = (uint)(a * 374761393 + b * 668265263); h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0; }
        return (H(ix, iy) * (1 - fx) + H(ix + 1, iy) * fx) * (1 - fy) + (H(ix, iy + 1) * (1 - fx) + H(ix + 1, iy + 1) * fx) * fy;
    }
}
