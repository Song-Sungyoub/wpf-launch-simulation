using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace WpfLaunchSimulation.Rendering;

// Scattered fair-weather cumulus around the pad in pad-local metres (+X east, +Y up, +Z south). Illustrative layout:
// records describe the flight-2 launch day as mostly clear, so the cover is sparse. Camera-facing billboards with flat
// bases, drawn far-to-near each frame like the exhaust cloud. They live in the far layer: the near layer's 0.2 m near
// plane (onboard camera) leaves no depth precision for clouds tens of km away.
public sealed class CloudLayer
{
    private const int Count = 260;
    public Model3DGroup Root { get; } = new();
    private readonly (Point3D Center, double Width, int Variant)[] clouds = new (Point3D, double, int)[Count];
    private readonly (MeshGeometry3D Mesh, ImageBrush Brush, ImageBrush Glow)[] slots = new (MeshGeometry3D, ImageBrush, ImageBrush)[Count];
    private static readonly BitmapSource[] Textures = Enumerable.Range(1, 6).Select(CloudTexture).ToArray();
    private readonly (double Distance, int Index)[] order = new (double, int)[Count];
    // Cirrus must be drawn before whatever lies beyond it: first from below, last from above (its transparent texels
    // still write depth, so drawn first above the camera's clouds it would hide them all).
    private const double CirrusHeight = 9000;
    private readonly GeometryModel3D cirrus = Cirrus(CirrusHeight, 140000);
    // Seen from above, upright billboards turn edge-on; flat top-view quads at cloud-top height fade in instead.
    private readonly (MeshGeometry3D Mesh, ImageBrush Brush, ImageBrush Glow)[] tops = new (MeshGeometry3D, ImageBrush, ImageBrush)[Count];
    private static readonly BitmapSource[] TopTextures = Enumerable.Range(1, 4).Select(TopTexture).ToArray();

    public CloudLayer()
    {
        Root.Children.Add(cirrus);
        var random = new Random(2206);
        for (int i = 0; i < Count; i++)
        {
            // Out to ~120 km so views from the climbing vehicle still find some.
            double r = 3000 + Math.Sqrt(random.NextDouble()) * 117000, a = random.NextDouble() * Math.PI * 2;
            double width = 700 + random.NextDouble() * 1600;
            clouds[i] = (new Point3D(r * Math.Cos(a), 1500 + random.NextDouble() * 900 + width * 0.22, r * Math.Sin(a)), width, random.Next(Textures.Length));
        }
        for (int i = 0; i < Count; i++)
        {
            var (center, width, variant) = clouds[i];
            double half = width * 0.45, y = center.Y + width * 0.12;
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection([new(center.X - half, y, center.Z - half), new(center.X + half, y, center.Z - half), new(center.X + half, y, center.Z + half), new(center.X - half, y, center.Z + half)]),
                Normals = new Vector3DCollection([new(0, 1, 0), new(0, 1, 0), new(0, 1, 0), new(0, 1, 0)]),
                TextureCoordinates = new PointCollection([new(0, 0), new(1, 0), new(1, 1), new(0, 1)]),
                TriangleIndices = new Int32Collection([0, 2, 1, 0, 3, 2]),
            };
            var brush = new ImageBrush(TopTextures[variant % TopTextures.Length]) { Opacity = 0 };
            var glow = new ImageBrush(TopTextures[variant % TopTextures.Length]) { Opacity = 0 };
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(brush)); material.Children.Add(new EmissiveMaterial(glow));
            tops[i] = (mesh, brush, glow);
            Root.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
        }
        for (int k = 0; k < Count; k++)
        {
            var mesh = new MeshGeometry3D();
            for (int c = 0; c < 4; c++) { mesh.Positions.Add(new Point3D()); mesh.Normals.Add(new Vector3D(0, 1, 0)); }
            mesh.TextureCoordinates = new PointCollection([new(0, 0), new(1, 0), new(1, 1), new(0, 1)]);
            mesh.TriangleIndices = new Int32Collection([0, 2, 1, 0, 3, 2]);
            var brush = new ImageBrush(Textures[0]);
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(brush));
            var glow = new ImageBrush(Textures[0]) { Opacity = 0.95 };   // far-layer ambient is dim; the lighting is baked in
            material.Children.Add(new EmissiveMaterial(glow));
            slots[k] = (mesh, brush, glow);
            Root.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
        }
    }

    // Cumulus: a heap of lobes with a cauliflower edge from noise, a flat base, sunlit white tops and a blue-grey
    // underside (lighting baked in; the billboard always faces the camera).
    private static BitmapSource CloudTexture(int seed)
    {
        const int w = 256, h = 128; var pixels = new byte[w * h * 4];
        var random = new Random(seed * 131);
        int lobes = 6 + random.Next(6);
        var blobs = Enumerable.Range(0, lobes).Select(k =>
        {
            double x = 0.18 + random.NextDouble() * 0.64;
            double r = 0.10 + random.NextDouble() * 0.13 * (1 - Math.Abs(x - 0.5));   // taller in the middle
            return (X: x, Y: 0.78 - r * 1.6 - random.NextDouble() * 0.12, R: r);
        }).ToArray();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double u = (x + 0.5) / w, v = (y + 0.5) / h, density = 0;
                foreach (var b in blobs) { double dx = (u - b.X) / b.R, dy = (v - b.Y) / (b.R * 2); density += Math.Max(0, 1 - (dx * dx + dy * dy)); }
                density += (PadSurroundings.Fbm(u * 14 + seed * 3.1, v * 7, 4) - 0.5) * 0.55;
                double baseCut = Math.Clamp((0.8 - v) / 0.035, 0, 1);
                double edge = Math.Clamp(Math.Min(u, 1 - u) / 0.08, 0, 1) * Math.Clamp(v / 0.06, 0, 1);
                double a = Math.Clamp((density - 0.18) / 0.35, 0, 1) * baseCut * edge;
                if (a <= 0) continue;
                double lit = Math.Clamp((0.8 - v) / 0.55, 0, 1);                       // 0 at the base, 1 at the top
                lit = Math.Clamp(lit + (0.5 - Math.Clamp(density, 0, 1)) * 0.25, 0, 1);  // thin edges catch light
                double r = 172 + 83 * lit, g = 182 + 72 * lit, bl = 198 + 54 * lit;
                int p = (y * w + x) * 4;
                pixels[p] = (byte)(bl * a); pixels[p + 1] = (byte)(g * a); pixels[p + 2] = (byte)(r * a); pixels[p + 3] = (byte)(255 * a);
            }
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4); bitmap.Freeze();
        return bitmap;
    }

    // Cumulus from above: a lumpy round patch, brightest in the middle.
    private static BitmapSource TopTexture(int seed)
    {
        const int n = 128; var pixels = new byte[n * n * 4];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double u = (x + 0.5) / n * 2 - 1, v = (y + 0.5) / n * 2 - 1, r = Math.Sqrt(u * u + v * v);
                double density = 1 - r * 1.25 + (PadSurroundings.Fbm(u * 3 + seed * 7, v * 3, 4) - 0.5) * 0.9;
                double a = Math.Clamp(density / 0.3, 0, 1);
                double lit = 0.82 + 0.18 * Math.Clamp(density, 0, 1);
                int p = (y * n + x) * 4;
                pixels[p] = (byte)(252 * lit * a); pixels[p + 1] = (byte)(250 * lit * a); pixels[p + 2] = (byte)(248 * lit * a); pixels[p + 3] = (byte)(255 * a);
            }
        var bitmap = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, pixels, n * 4); bitmap.Freeze();
        return bitmap;
    }

    // Thin cirrus streaks on a horizontal sheet, stretched along one direction.
    private static GeometryModel3D Cirrus(double height, double size)
    {
        const int n = 512; var pixels = new byte[n * n * 4];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double u = (double)x / n, v = (double)y / n;
                double streak = PadSurroundings.Fbm(u * 3, v * 22, 5), patch = PadSurroundings.Fbm(u * 2 + 5, v * 2 + 9, 3);
                double edge = Math.Clamp(Math.Min(Math.Min(u, 1 - u), Math.Min(v, 1 - v)) / 0.2, 0, 1);
                double a = Math.Clamp((streak - 0.47) * 3, 0, 1) * Math.Clamp((patch - 0.38) * 3, 0, 1) * edge * 0.75;
                int p = (y * n + x) * 4; byte c = (byte)(250 * a);
                pixels[p] = c; pixels[p + 1] = c; pixels[p + 2] = c; pixels[p + 3] = (byte)(255 * a);
            }
        var bitmap = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, pixels, n * 4); bitmap.Freeze();
        var mesh = new MeshGeometry3D();
        foreach (var (x, z) in new[] { (-1.0, -1.0), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0) })
        {
            mesh.Positions.Add(new Point3D(x * size / 2, height, z * size / 2)); mesh.Normals.Add(new Vector3D(0, -1, 0));
            mesh.TextureCoordinates.Add(new Point((x + 1) / 2, (z + 1) / 2));
        }
        mesh.TriangleIndices = new Int32Collection([0, 1, 2, 0, 2, 3]);
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new ImageBrush(bitmap) { Opacity = 0.6 }));
        material.Children.Add(new EmissiveMaterial(new ImageBrush(bitmap) { Opacity = 0.5 }));
        material.Freeze();
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    // camera, right: camera position and screen-right axis in pad-local metres; clouds stay upright (local +Y).
    public void Update(Point3D camera, Vector3D right)
    {
        right.Y = 0; if (right.LengthSquared < 1e-9) right = new Vector3D(1, 0, 0); right.Normalize();
        for (int i = 0; i < Count; i++) order[i] = ((clouds[i].Center - camera).LengthSquared, i);
        Array.Sort(order, (a, b) => b.Distance.CompareTo(a.Distance));
        bool above = camera.Y > CirrusHeight;
        if (above != (Root.Children[^1] == cirrus)) { Root.Children.Remove(cirrus); if (above) Root.Children.Add(cirrus); else Root.Children.Insert(0, cirrus); }
        var up = new Vector3D(0, 1, 0);
        // Elevation of the camera above each cloud decides upright billboard vs. flat top.
        double TopWeight(Point3D c) { var d = camera - c; double e = Math.Atan2(d.Y, Math.Sqrt(d.X * d.X + d.Z * d.Z)) * 180 / Math.PI; return Math.Clamp((e - 20) / 25, 0, 1); }
        for (int i = 0; i < Count; i++) { double w = TopWeight(clouds[i].Center); tops[i].Brush.Opacity = w; tops[i].Glow.Opacity = w * 0.95; }
        for (int k = 0; k < Count; k++)
        {
            var (center, width, variant) = clouds[order[k].Index];
            var (mesh, brush, glow) = slots[k];
            var toCamera = camera - center; toCamera.Y = 0;
            var side = toCamera.LengthSquared > 1 ? Vector3D.CrossProduct(up, toCamera) : right; side.Normalize();
            var ax = side * (width / 2); var ay = up * (width / 4);
            mesh.Positions[0] = center - ax + ay; mesh.Positions[1] = center + ax + ay; mesh.Positions[2] = center + ax - ay; mesh.Positions[3] = center - ax - ay;
            var normal = camera - center; normal.Normalize();
            for (int n = 0; n < 4; n++) mesh.Normals[n] = normal;
            if (brush.ImageSource != Textures[variant]) { brush.ImageSource = Textures[variant]; glow.ImageSource = Textures[variant]; }
            double upright = 1 - TopWeight(center); brush.Opacity = upright; glow.Opacity = upright * 0.95;
        }
    }
}
