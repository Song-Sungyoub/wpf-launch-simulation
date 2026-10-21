using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WpfLaunchSimulation.Rendering;

// Liftoff exhaust cloud in pad-local metres (+X east, +Y up, +Z south; the flame duct is drawn exiting south, +Z [근사]).
// Purely a function of mission time, so time warp, pause and replays always show the same cloud.
// Illustrative: puff counts, growth and lifetimes are chosen for the look, not measured.
public sealed class LaunchSmoke
{
    private const int Count = 160;
    private const double FirstPuff = -4.0, Interval = 0.13, Lifetime = 34;
    public Model3DGroup Root { get; } = new();
    private readonly (Vector3D Direction, double Speed, double Rise, double Size, bool Duct)[] puffs = new (Vector3D, double, double, double, bool)[Count];
    private readonly (ScaleTransform3D Scale, TranslateTransform3D Move, SolidColorBrush Brush, GeometryModel3D Model)[] parts;
    private readonly GeometryModel3D ductGlow;
    private readonly SolidColorBrush[] emissive = new SolidColorBrush[Count];
    private readonly SolidColorBrush glowBrush = new(Color.FromArgb(0, 255, 170, 70));
    private bool attached;

    public LaunchSmoke()
    {
        var random = new Random(4021);
        var sphere = (MeshGeometry3D)SceneGeometrySphere();
        parts = new (ScaleTransform3D, TranslateTransform3D, SolidColorBrush, GeometryModel3D)[Count];
        for (int i = 0; i < Count; i++)
        {
            bool duct = i % 3 != 0;
            double a = duct ? (random.NextDouble() - 0.5) * 1.1 : random.NextDouble() * 2 * Math.PI;
            var dir = new Vector3D(Math.Sin(a), 0, Math.Cos(a));
            puffs[i] = (dir, duct ? 11 + random.NextDouble() * 8 : 4 + random.NextDouble() * 4, 1.2 + random.NextDouble() * 2.2, 0.8 + random.NextDouble() * 0.6, duct);
            byte shade = (byte)(205 + random.Next(40));
            var brush = new SolidColorBrush(Color.FromRgb(shade, shade, (byte)(shade - 6))) { Opacity = 0 };
            // A grey emissive term keeps the cloud white in shadow instead of taking the sky colour.
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(brush));
            material.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(70, 70, 68)) { Opacity = 0 }));
            var scale = new ScaleTransform3D(0, 0, 0); var move = new TranslateTransform3D();
            var transform = new Transform3DGroup(); transform.Children.Add(scale); transform.Children.Add(move);
            var model = new GeometryModel3D(sphere, material) { Transform = transform };
            parts[i] = (scale, move, brush, model);
            emissive[i] = (SolidColorBrush)((EmissiveMaterial)material.Children[1]).Brush;
        }
        var glowMaterial = new MaterialGroup();
        glowMaterial.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))));
        glowMaterial.Children.Add(new EmissiveMaterial(glowBrush));
        ductGlow = NuriModel.Box(0, 2.5, 40, 9, 5, 4, glowMaterial);
    }

    private static Geometry3D SceneGeometrySphere()
    {
        var mesh = new MeshGeometry3D();
        const int slices = 18, stacks = 10;
        for (int y = 0; y <= stacks; y++)
        {
            double lat = Math.PI / 2 - Math.PI * y / stacks;
            for (int x = 0; x <= slices; x++)
            {
                double lon = 2 * Math.PI * x / slices;
                var n = new Vector3D(Math.Cos(lat) * Math.Cos(lon), Math.Sin(lat), -Math.Cos(lat) * Math.Sin(lon));
                mesh.Positions.Add((Point3D)n); mesh.Normals.Add(n);
            }
        }
        for (int y = 0; y < stacks; y++)
            for (int x = 0; x < slices; x++)
            {
                int a = y * (slices + 1) + x, b = a + slices + 1;
                foreach (int k in new[] { a, b, a + 1, a + 1, b, b + 1 }) mesh.TriangleIndices.Add(k);
            }
        mesh.Freeze(); return mesh;
    }

    public void Update(double t)
    {
        bool active = t > FirstPuff && t < FirstPuff + Count * Interval + Lifetime;
        if (active != attached)
        {
            attached = active;
            if (active) { foreach (var p in parts) Root.Children.Add(p.Model); Root.Children.Add(ductGlow); }
            else Root.Children.Clear();
        }
        if (!active) return;
        // Duct flame while the engines are at the pad (ignition to a few seconds after liftoff).
        double glow = t < 0 ? (t + 4) / 4 : Math.Clamp(1 - t / 8, 0, 1);
        glowBrush.Color = Color.FromArgb((byte)(200 * Math.Clamp(glow, 0, 1)), 255, 170, 70);
        for (int i = 0; i < Count; i++)
        {
            double age = t - (FirstPuff + i * Interval);
            var (scale, move, brush, _) = parts[i];
            if (age <= 0 || age >= Lifetime) { scale.ScaleX = scale.ScaleY = scale.ScaleZ = 0; continue; }
            var puff = puffs[i];
            // Fast expansion that slows down (drag), slow buoyant rise, steady growth.
            double travel = puff.Speed * 6 * (1 - Math.Exp(-age / 6));
            double radius = (puff.Duct ? 3.5 : 2.5) * puff.Size * (1 + 1.2 * Math.Pow(age, 0.7));
            var origin = puff.Duct ? new Vector3D(0, 2, 40) : new Vector3D(0, 1, 0);
            var position = origin + puff.Direction * travel + new Vector3D(0, radius * 0.6 + puff.Rise * age, 0);
            move.OffsetX = position.X; move.OffsetY = position.Y; move.OffsetZ = position.Z;
            scale.ScaleX = scale.ScaleZ = radius; scale.ScaleY = radius * 0.8;
            emissive[i].Opacity = brush.Opacity = 0.8 * Math.Clamp(age / 0.6, 0, 1) * Math.Pow(Math.Clamp(1 - age / Lifetime, 0, 1), 1.5);
        }
    }
}
