using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace WpfLaunchSimulation.Rendering;

// Liftoff exhaust cloud in pad-local metres (+X east, +Y up, +Z south; the flame duct is drawn exiting south, +Z [근사]).
// Puffs are soft camera-facing billboards. Positions are a pure function of mission time, so time warp, pause and
// replays show the same cloud; each frame the live puffs are assigned to draw slots far-to-near, because WPF draws
// translucent geometry in scene order without sorting. Puff counts, growth and lifetimes are chosen for the look.
public sealed class LaunchSmoke
{
    private const int Count = 180;
    private const double FirstPuff = -4.0, Interval = 0.12, Lifetime = 38;
    public Model3DGroup Root { get; } = new();
    private readonly (Vector3D Direction, double Speed, double Rise, double Size, bool Duct, int Variant, double Spin)[] puffs;
    private readonly (MeshGeometry3D Mesh, ImageBrush Brush, GeometryModel3D Model)[] slots;
    private readonly GeometryModel3D ductGlow;
    private readonly SolidColorBrush glowBrush = new(Color.FromArgb(0, 255, 170, 70));
    private readonly List<(double Distance, int Index, double Age)> live = new(Count);
    private static readonly BitmapSource[] Textures = [PuffTexture(1), PuffTexture(2), PuffTexture(3), PuffTexture(4)];
    private bool attached;

    public LaunchSmoke()
    {
        var random = new Random(4021);
        puffs = new (Vector3D, double, double, double, bool, int, double)[Count];
        for (int i = 0; i < Count; i++)
        {
            bool duct = i % 3 != 0;
            double a = duct ? (random.NextDouble() - 0.5) * 1.1 : random.NextDouble() * 2 * Math.PI;
            puffs[i] = (new Vector3D(Math.Sin(a), 0, Math.Cos(a)), duct ? 11 + random.NextDouble() * 8 : 4 + random.NextDouble() * 4,
                1.2 + random.NextDouble() * 2.2, 0.8 + random.NextDouble() * 0.6, duct, random.Next(Textures.Length), random.NextDouble() * Math.PI * 2);
        }
        slots = new (MeshGeometry3D, ImageBrush, GeometryModel3D)[Count];
        for (int k = 0; k < Count; k++)
        {
            var mesh = new MeshGeometry3D();
            for (int c = 0; c < 4; c++) { mesh.Positions.Add(new Point3D()); mesh.Normals.Add(new Vector3D(0, 1, 0)); }
            mesh.TextureCoordinates = new PointCollection([new(0, 0), new(1, 0), new(1, 1), new(0, 1)]);
            mesh.TriangleIndices = new Int32Collection([0, 2, 1, 0, 3, 2]);
            var brush = new ImageBrush(Textures[0]) { Opacity = 0 };
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(brush));
            material.Children.Add(new EmissiveMaterial(new ImageBrush(Textures[0]) { Opacity = 0 }));
            slots[k] = (mesh, brush, new GeometryModel3D(mesh, material) { BackMaterial = material });
        }
        var glowMaterial = new MaterialGroup();
        glowMaterial.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))));
        glowMaterial.Children.Add(new EmissiveMaterial(glowBrush));
        ductGlow = NuriModel.Box(0, 1.5, 40, 9, 3, 3, glowMaterial);
    }

    // Soft round puff with a little turbulence; alpha falls to zero at the edge.
    private static BitmapSource PuffTexture(int seed)
    {
        const int size = 128;
        var pixels = new byte[size * size * 4];
        var random = new Random(seed * 97);
        var blobs = Enumerable.Range(0, 7).Select(_ => (X: 0.3 + random.NextDouble() * 0.4, Y: 0.3 + random.NextDouble() * 0.4, R: 0.18 + random.NextDouble() * 0.15)).ToArray();
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double u = (x + 0.5) / size, v = (y + 0.5) / size, density = 0;
                foreach (var b in blobs) { double d = Math.Sqrt((u - b.X) * (u - b.X) + (v - b.Y) * (v - b.Y)) / b.R; density += Math.Max(0, 1 - d * d); }
                double edge = Math.Clamp(1 - Math.Sqrt((u - 0.5) * (u - 0.5) + (v - 0.5) * (v - 0.5)) / 0.5, 0, 1);
                double alpha = Math.Clamp(density * 0.6, 0, 1) * Math.Pow(edge, 0.6);
                byte shade = (byte)(214 + 30 * Math.Clamp(1 - v, 0, 1)); // a little brighter on top
                int p = (y * size + x) * 4; byte a = (byte)(alpha * 255);
                pixels[p] = pixels[p + 1] = (byte)(shade * alpha); pixels[p + 2] = (byte)(Math.Min(255, shade + 4) * alpha); pixels[p + 3] = a;
            }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
        bitmap.Freeze(); return bitmap;
    }

    // camera, right, up: camera position and screen axes in pad-local metres.
    public void Update(double t, Point3D camera, Vector3D right, Vector3D up)
    {
        bool active = t > FirstPuff && t < FirstPuff + Count * Interval + Lifetime;
        if (active != attached)
        {
            attached = active;
            if (active) { foreach (var s in slots) Root.Children.Add(s.Model); Root.Children.Add(ductGlow); }
            else Root.Children.Clear();
        }
        if (!active) return;
        double glow = t < 0 ? (t + 4) / 4 : Math.Clamp(1 - t / 8, 0, 1);
        glowBrush.Color = Color.FromArgb((byte)(200 * Math.Clamp(glow, 0, 1)), 255, 170, 70);
        right.Normalize(); up.Normalize();
        live.Clear();
        for (int i = 0; i < Count; i++)
        {
            double age = t - (FirstPuff + i * Interval);
            if (age > 0 && age < Lifetime) live.Add(((Center(i, age) - camera).LengthSquared, i, age));
        }
        live.Sort((a, b) => b.Distance.CompareTo(a.Distance));
        for (int k = 0; k < Count; k++)
        {
            var (mesh, brush, model) = slots[k];
            var emissive = (ImageBrush)((EmissiveMaterial)((MaterialGroup)model.Material).Children[1]).Brush;
            if (k >= live.Count) { brush.Opacity = emissive.Opacity = 0; continue; }
            var (_, i, age) = live[k];
            var puff = puffs[i];
            double radius = Radius(i, age);
            var center = Center(i, age);
            double c = Math.Cos(puff.Spin), s = Math.Sin(puff.Spin);
            var ax = (right * c + up * s) * radius; var ay = (-right * s + up * c) * radius;
            mesh.Positions[0] = center - ax + ay; mesh.Positions[1] = center + ax + ay; mesh.Positions[2] = center + ax - ay; mesh.Positions[3] = center - ax - ay;
            var normal = camera - center; normal.Normalize();
            for (int n = 0; n < 4; n++) mesh.Normals[n] = normal;
            if (brush.ImageSource != Textures[puff.Variant]) { brush.ImageSource = Textures[puff.Variant]; emissive.ImageSource = Textures[puff.Variant]; }
            double opacity = 0.9 * Math.Clamp(age / 0.6, 0, 1) * Math.Pow(Math.Clamp(1 - age / Lifetime, 0, 1), 1.4);
            brush.Opacity = opacity; emissive.Opacity = opacity * 0.35;
        }
    }

    // Fast expansion that slows down (drag), slow buoyant rise, steady growth.
    private double Radius(int i, double age) => (puffs[i].Duct ? 4.5 : 3.2) * puffs[i].Size * (1 + 1.2 * Math.Pow(age, 0.7));
    private Point3D Center(int i, double age)
    {
        var puff = puffs[i];
        double travel = puff.Speed * 6 * (1 - Math.Exp(-age / 6)), radius = Radius(i, age);
        var origin = puff.Duct ? new Point3D(0, 2, 40) : new Point3D(0, 1, 0);
        // Centres sit high enough that the visible part of each puff stays above the deck; a billboard cutting into the
        // ground shows a hard straight edge.
        return origin + puff.Direction * travel + new Vector3D(0, radius * 0.75 + puff.Rise * age, 0);
    }
}
