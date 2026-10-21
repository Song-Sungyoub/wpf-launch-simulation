using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WpfLaunchSimulation.Rendering;

// Nuri in metres: +Y is the long axis, origin at the base of stage 1. Proportions from public figures
// (overall 47.2 m, stage 1 Ø3.5 m, stages 2-3 Ø2.6 m); internal layout and colours are illustrative.
public sealed class NuriModel
{
    public const double Stage1Top = 21.6, AdapterTop = 23.6, Stage2Top = 36.0, Stage3Top = 42.2, FairingTop = 47.2;
    public const double Stage1Radius = 1.75, UpperRadius = 1.3;
    public Model3DGroup Root { get; } = new();
    public Model3DGroup Stage1 { get; } = new();
    public Model3DGroup Stage2 { get; } = new();
    public Model3DGroup Stage3 { get; } = new();
    public Model3DGroup FairingLeft { get; } = new();
    public Model3DGroup FairingRight { get; } = new();
    public Model3DGroup Payload { get; } = new();
    public ScaleTransform3D[] Plumes { get; } = [new(), new(), new()];
    public Model3DGroup[] PlumeModels { get; } = [new(), new(), new()];

    private static readonly Material White = Lit(Color.FromRgb(232, 235, 238));
    private static readonly Material Grey = Lit(Color.FromRgb(70, 74, 82));
    private static readonly Material Dark = Lit(Color.FromRgb(38, 40, 46));
    private static readonly Material Nozzle = Lit(Color.FromRgb(120, 92, 70));
    private static readonly Material Gold = Lit(Color.FromRgb(205, 165, 70));

    public NuriModel()
    {
        // Stage 1: body, dark base band, adapter cone 3.5 m → 2.6 m, four engines.
        Stage1.Children.Add(Cylinder(0, 2.2, Stage1Radius, Stage1Radius, Dark));
        Stage1.Children.Add(Cylinder(2.2, Stage1Top, Stage1Radius, Stage1Radius, White));
        Stage1.Children.Add(Cylinder(Stage1Top - 1.0, Stage1Top, Stage1Radius + 0.01, Stage1Radius + 0.01, Grey));
        Stage1.Children.Add(Cylinder(Stage1Top, AdapterTop, Stage1Radius, UpperRadius, Grey));
        foreach (var (x, z) in new[] { (0.8, 0.8), (-0.8, 0.8), (0.8, -0.8), (-0.8, -0.8) })
            Stage1.Children.Add(Cylinder(-2.0, 0, 0.55, 0.22, Nozzle, x, z));
        // Stage 2 (its vacuum nozzle sits inside the adapter until separation).
        Stage2.Children.Add(Cylinder(AdapterTop - 2.2, AdapterTop, 0.8, 0.3, Nozzle));
        Stage2.Children.Add(Cylinder(AdapterTop, Stage2Top, UpperRadius, UpperRadius, White));
        Stage2.Children.Add(Cylinder(Stage2Top - 0.8, Stage2Top, UpperRadius + 0.01, UpperRadius + 0.01, Grey));
        // Stage 3 with a small nozzle.
        Stage3.Children.Add(Cylinder(Stage2Top - 1.2, Stage2Top, 0.35, 0.12, Nozzle));
        Stage3.Children.Add(Cylinder(Stage2Top, Stage3Top, UpperRadius, UpperRadius, White));
        // Payload (gold box) inside the fairing.
        Payload.Children.Add(Box(0, Stage3Top + 0.9, 0, 0.9, 0.9, 0.9, Gold));
        // Fairing halves: cylinder + ogive cone, split along the X axis.
        foreach (var (half, sign) in new[] { (FairingLeft, -1.0), (FairingRight, 1.0) })
        {
            half.Children.Add(HalfShell(Stage3Top, 44.2, UpperRadius, UpperRadius, sign, White));
            half.Children.Add(HalfShell(44.2, FairingTop, UpperRadius, 0.12, sign, White));
        }
        double[] plumeBase = [-2.0, AdapterTop - 2.2, Stage2Top - 1.2];
        double[] plumeRadius = [1.6, 0.8, 0.35];
        for (int i = 0; i < 3; i++)
        {
            var glow = new MaterialGroup();
            glow.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))));
            glow.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(130, 255, 190, 90))));
            glow.Freeze();
            var core = Cylinder(0, -1, plumeRadius[i], plumeRadius[i] * 0.25, glow);
            core.BackMaterial = glow;
            var hot = new MaterialGroup();
            hot.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))));
            hot.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(200, 255, 245, 210))));
            hot.Freeze();
            var inner = Cylinder(0, -0.35, plumeRadius[i] * 0.55, plumeRadius[i] * 0.1, hot);
            PlumeModels[i].Children.Add(inner); PlumeModels[i].Children.Add(core);
            // Scale Y = plume length; translated to the nozzle exit.
            var transforms = new Transform3DGroup();
            transforms.Children.Add(Plumes[i]); transforms.Children.Add(new TranslateTransform3D(0, plumeBase[i], 0));
            PlumeModels[i].Transform = transforms;
        }
        foreach (var part in new Model3D[] { Stage1, Stage2, Stage3, Payload, FairingLeft, FairingRight }) Root.Children.Add(part);
    }

    public static Material Lit(Color color)
    {
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(60, 60, 60)), 30));
        material.Freeze(); return material;
    }

    public static GeometryModel3D Cylinder(double y0, double y1, double r0, double r1, Material material, double x = 0, double z = 0, int segments = 32)
    {
        var mesh = new MeshGeometry3D();
        for (int i = 0; i <= segments; i++)
        {
            double a = 2 * Math.PI * i / segments, c = Math.Cos(a), s = Math.Sin(a);
            var normal = new Vector3D(c, (r0 - r1) / Math.Max(Math.Abs(y1 - y0), 1e-6), s); normal.Normalize();
            mesh.Positions.Add(new Point3D(x + r0 * c, y0, z + r0 * s)); mesh.Normals.Add(normal);
            mesh.Positions.Add(new Point3D(x + r1 * c, y1, z + r1 * s)); mesh.Normals.Add(normal);
        }
        bool flip = y1 < y0;
        for (int i = 0; i < segments; i++)
        {
            int a = i * 2, b = a + 2;
            int[] tri = flip ? [a, a + 1, b, b, a + 1, b + 1] : [a, b, a + 1, a + 1, b, b + 1];
            foreach (int k in tri) mesh.TriangleIndices.Add(k);
        }
        // Caps.
        foreach (var (y, r, up) in new[] { (y0, r0, false), (y1, r1, true) })
        {
            if (r < 0.05) continue;
            int center = mesh.Positions.Count; var n = new Vector3D(0, up ^ flip ? 1 : -1, 0);
            mesh.Positions.Add(new Point3D(x, y, z)); mesh.Normals.Add(n);
            for (int i = 0; i <= segments; i++)
            {
                double a = 2 * Math.PI * i / segments;
                mesh.Positions.Add(new Point3D(x + r * Math.Cos(a), y, z + r * Math.Sin(a))); mesh.Normals.Add(n);
            }
            for (int i = 0; i < segments; i++)
            {
                int[] tri = (up ^ flip) ? [center, center + i + 2, center + i + 1] : [center, center + i + 1, center + i + 2];
                foreach (int k in tri) mesh.TriangleIndices.Add(k);
            }
        }
        mesh.Freeze();
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static GeometryModel3D HalfShell(double y0, double y1, double r0, double r1, double sign, Material material, int segments = 16)
    {
        var mesh = new MeshGeometry3D();
        for (int i = 0; i <= segments; i++)
        {
            double a = Math.PI * i / segments - Math.PI / 2, c = Math.Cos(a) * sign, s = Math.Sin(a);
            var normal = new Vector3D(c, (r0 - r1) / (y1 - y0), s); normal.Normalize();
            mesh.Positions.Add(new Point3D(r0 * c, y0, r0 * s)); mesh.Normals.Add(normal);
            mesh.Positions.Add(new Point3D(r1 * c, y1, r1 * s)); mesh.Normals.Add(normal);
        }
        for (int i = 0; i < segments; i++)
        {
            int a = i * 2, b = a + 2;
            int[] tri = sign > 0 ? [a, b, a + 1, a + 1, b, b + 1] : [a, a + 1, b, b, a + 1, b + 1];
            foreach (int k in tri) mesh.TriangleIndices.Add(k);
        }
        mesh.Freeze();
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    public static GeometryModel3D Box(double cx, double cy, double cz, double sx, double sy, double sz, Material material)
    {
        var mesh = new MeshGeometry3D();
        void Face(Vector3D n, Vector3D u, Vector3D v)
        {
            int b = mesh.Positions.Count; var c = new Point3D(cx, cy, cz) + new Vector3D(n.X * sx / 2, n.Y * sy / 2, n.Z * sz / 2);
            var du = new Vector3D(u.X * sx / 2, u.Y * sy / 2, u.Z * sz / 2); var dv = new Vector3D(v.X * sx / 2, v.Y * sy / 2, v.Z * sz / 2);
            foreach (var p in new[] { c - du - dv, c + du - dv, c + du + dv, c - du + dv }) { mesh.Positions.Add(p); mesh.Normals.Add(n); }
            foreach (int k in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(b + k);
        }
        Face(new(1, 0, 0), new(0, 0, -1), new(0, 1, 0)); Face(new(-1, 0, 0), new(0, 0, 1), new(0, 1, 0));
        Face(new(0, 1, 0), new(1, 0, 0), new(0, 0, -1)); Face(new(0, -1, 0), new(1, 0, 0), new(0, 0, 1));
        Face(new(0, 0, 1), new(1, 0, 0), new(0, 1, 0)); Face(new(0, 0, -1), new(-1, 0, 0), new(0, 1, 0));
        mesh.Freeze();
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    // Separate copies for separated hardware.
    public static Model3DGroup SpentStage(int stage)
    {
        var model = new NuriModel();
        return Detach(model, stage switch { 1 => model.Stage1, 2 => model.Stage2, 3 => model.Stage3, _ => model.Payload });
    }
    public static Model3DGroup FairingHalf(bool left)
    {
        var model = new NuriModel();
        return Detach(model, left ? model.FairingLeft : model.FairingRight);
    }
    private static Model3DGroup Detach(NuriModel model, Model3DGroup part)
    {
        model.Root.Children.Remove(part);
        var group = new Model3DGroup(); group.Children.Add(part); return group;
    }
}

// Naro launch complex 2 in local metres: +Y up, +X east, -Z north. Tower height 45~48 m (sources differ); green,
// with four arms. Pad deck, flame duct and service buildings are simplified.
public static class PadModel
{
    public static Model3DGroup Create()
    {
        var group = new Model3DGroup();
        var concrete = NuriModel.Lit(Color.FromRgb(150, 150, 146));
        var green = NuriModel.Lit(Color.FromRgb(48, 120, 70));
        var steel = NuriModel.Lit(Color.FromRgb(110, 114, 120));
        group.Children.Add(NuriModel.Box(0, -0.5, 0, 90, 1, 90, concrete));
        group.Children.Add(NuriModel.Box(0, 0.6, 0, 12, 1.2, 12, steel));      // launch table
        group.Children.Add(NuriModel.Box(0, -0.2, 26, 8, 0.6, 40, NuriModel.Lit(Color.FromRgb(70, 66, 60)))); // flame duct exit (direction illustrative)
        group.Children.Add(NuriModel.Box(9.5, 24, 0, 4.5, 48, 4.5, green));     // umbilical tower
        foreach (double y in new[] { 12.0, 24.0, 34.0, 41.0 })
            group.Children.Add(NuriModel.Box(5.6, y, 0, 3.6, 0.8, 1.2, green)); // arms
        group.Children.Add(NuriModel.Box(-40, 4, 30, 18, 8, 12, NuriModel.Lit(Color.FromRgb(200, 200, 196)))); // service building
        group.Freeze();
        return group;
    }
}
