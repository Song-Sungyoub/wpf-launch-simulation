using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace WpfLaunchSimulation.Rendering;

// Scattered fair-weather cumulus around the pad in pad-local metres (+X east, +Y up, +Z south). Illustrative layout:
// records describe the flight-2 launch day as mostly clear, so the cover is sparse. Camera-facing billboards with flat
// bases, drawn far-to-near each frame like the exhaust cloud.
public sealed class CloudLayer
{
    private const int Count = 70;
    public Model3DGroup Root { get; } = new();
    private readonly (Point3D Center, double Width, int Variant)[] clouds = new (Point3D, double, int)[Count];
    private readonly (MeshGeometry3D Mesh, ImageBrush Brush, ImageBrush Glow)[] slots = new (MeshGeometry3D, ImageBrush, ImageBrush)[Count];
    private static readonly BitmapSource[] Textures = [CloudTexture(1), CloudTexture(2), CloudTexture(3)];
    private readonly (double Distance, int Index)[] order = new (double, int)[Count];

    public CloudLayer()
    {
        var random = new Random(2206);
        for (int i = 0; i < Count; i++)
        {
            double r = 3000 + Math.Sqrt(random.NextDouble()) * 45000, a = random.NextDouble() * Math.PI * 2;
            double width = 700 + random.NextDouble() * 1600;
            clouds[i] = (new Point3D(r * Math.Cos(a), 1500 + random.NextDouble() * 900 + width * 0.22, r * Math.Sin(a)), width, random.Next(Textures.Length));
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
            var glow = new ImageBrush(Textures[0]) { Opacity = 0.45 };
            material.Children.Add(new EmissiveMaterial(glow));
            slots[k] = (mesh, brush, glow);
            Root.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
        }
    }

    // Puffy top, flat shaded base; alpha fades to zero at the edges.
    private static BitmapSource CloudTexture(int seed)
    {
        const int w = 256, h = 128; var pixels = new byte[w * h * 4];
        var random = new Random(seed * 131);
        var blobs = Enumerable.Range(0, 9).Select(_ => (X: 0.2 + random.NextDouble() * 0.6, Y: 0.35 + random.NextDouble() * 0.35, R: 0.12 + random.NextDouble() * 0.14)).ToArray();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double u = (x + 0.5) / w, v = (y + 0.5) / h, density = 0;
                foreach (var b in blobs) { double dx = (u - b.X) / b.R, dy = (v - b.Y) / (b.R * 2); double d = dx * dx + dy * dy; density += Math.Max(0, 1 - d); }
                double baseCut = Math.Clamp((0.82 - v) / 0.08, 0, 1);               // flat bottom
                double edge = Math.Clamp(Math.Min(Math.Min(u, 1 - u), 0.5) / 0.12, 0, 1);
                double a = Math.Clamp(density * 0.8, 0, 1) * baseCut * edge;
                double shade = 0.72 + 0.28 * Math.Clamp((0.8 - v) / 0.5, 0, 1);     // grey base, white top
                int p = (y * w + x) * 4; byte c = (byte)(255 * shade);
                pixels[p] = (byte)(Math.Min(255, c + 6) * a); pixels[p + 1] = (byte)(c * a); pixels[p + 2] = (byte)(c * a); pixels[p + 3] = (byte)(255 * a);
            }
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4); bitmap.Freeze();
        return bitmap;
    }

    // camera, right: camera position and screen-right axis in pad-local metres; clouds stay upright (local +Y).
    public void Update(Point3D camera, Vector3D right)
    {
        right.Y = 0; if (right.LengthSquared < 1e-9) right = new Vector3D(1, 0, 0); right.Normalize();
        for (int i = 0; i < Count; i++) order[i] = ((clouds[i].Center - camera).LengthSquared, i);
        Array.Sort(order, (a, b) => b.Distance.CompareTo(a.Distance));
        var up = new Vector3D(0, 1, 0);
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
        }
    }
}
