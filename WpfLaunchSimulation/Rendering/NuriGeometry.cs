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
    private static readonly Material Dark = Lit(Color.FromRgb(46, 48, 54));
    private static readonly Material Nozzle = Lit(Color.FromRgb(120, 92, 70));
    private static readonly Material Gold = Lit(Color.FromRgb(205, 165, 70));

    public NuriModel()
    {
        var engine = Lit(Color.FromRgb(58, 58, 62)); var chamber = Lit(Color.FromRgb(150, 104, 70));
        // Stage 1: textured skin (flag, "대한민국", seams), dark base band, interstage cone 3.5 m → 2.6 m, raceway, four engines.
        Stage1.Children.Add(Cylinder(0, 2.2, Stage1Radius, Stage1Radius, Dark, segments: 48));
        Stage1.Children.Add(Skin(2.2, Stage1Top, Stage1Radius, NuriTextures.Stage1.Value));
        Stage1.Children.Add(Cylinder(Stage1Top, AdapterTop, Stage1Radius, UpperRadius, Dark, segments: 48));
        Stage1.Children.Add(Box(Stage1Radius + 0.06, (2.2 + Stage1Top) / 2, 0, 0.14, Stage1Top - 2.6, 0.24, Grey));
        foreach (var (x, z) in new[] { (0.8, 0.8), (-0.8, 0.8), (0.8, -0.8), (-0.8, -0.8) })
            foreach (var part in EngineParts(x, z, 1.0, engine, chamber)) Stage1.Children.Add(part);
        // Stage 2 (its vacuum nozzle sits inside the interstage until separation).
        foreach (var part in EngineParts(0, 0, 1.45, engine, chamber, top: AdapterTop)) Stage2.Children.Add(part);
        Stage2.Children.Add(Skin(AdapterTop, Stage2Top, UpperRadius, NuriTextures.Stage2.Value));
        Stage2.Children.Add(Box(UpperRadius + 0.05, (AdapterTop + Stage2Top) / 2, 0, 0.12, Stage2Top - AdapterTop - 0.6, 0.2, Grey));
        Stage2.Children.Add(Cylinder(Stage2Top - 0.5, Stage2Top, UpperRadius + 0.01, UpperRadius + 0.01, Dark, segments: 48));
        // Stage 3 with a small nozzle.
        foreach (var part in EngineParts(0, 0, 0.5, engine, chamber, top: Stage2Top)) Stage3.Children.Add(part);
        Stage3.Children.Add(Skin(Stage2Top, Stage3Top, UpperRadius, NuriTextures.Stage3.Value));
        // Payload (gold box) inside the fairing.
        Payload.Children.Add(Box(0, Stage3Top + 0.9, 0, 0.9, 0.9, 0.9, Gold));
        // Fairing halves: textured cylinder + ogive nose, split along the X axis.
        double[] noseY = [44.2, 45.3, 46.2, 46.8, FairingTop];
        double[] noseR = [UpperRadius, 1.18, 0.86, 0.5, 0.1];
        foreach (var (half, sign) in new[] { (FairingLeft, -1.0), (FairingRight, 1.0) })
        {
            half.Children.Add(HalfShell(Stage3Top, 44.2, UpperRadius, UpperRadius, sign, new DiffuseMaterial(NuriTextures.Fairing.Value), textured: true));
            for (int k = 0; k + 1 < noseY.Length; k++) half.Children.Add(HalfShell(noseY[k], noseY[k + 1], noseR[k], noseR[k + 1], sign, White));
        }
        double[] plumeBase = [-2.0, AdapterTop - 2.9, Stage2Top - 1.0];
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

    // Bell nozzle of exit radius `scale`·0.55 m with a short combustion chamber; `top` is the engine mount plane.
    private static IEnumerable<GeometryModel3D> EngineParts(double x, double z, double scale, Material bell, Material chamber, double top = 0)
    {
        double[] y = [0, -0.25, -0.55, -1.0, -1.5, -2.0];
        double[] r = [0.26, 0.2, 0.13, 0.3, 0.45, 0.55];
        yield return Cylinder(top + y[0] * scale, top + y[2] * scale, r[0] * scale, r[2] * scale, chamber, x, z, 24);
        for (int k = 2; k + 1 < y.Length; k++)
            yield return Cylinder(top + y[k] * scale, top + y[k + 1] * scale, r[k] * scale, r[k + 1] * scale, bell, x, z, 24);
    }

    private static GeometryModel3D Skin(double y0, double y1, double radius, ImageBrush texture, int segments = 64)
    {
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(texture));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(70, 70, 70)), 40));
        material.Freeze();
        var mesh = new MeshGeometry3D();
        for (int i = 0; i <= segments; i++)
        {
            double a = 2 * Math.PI * i / segments, c = Math.Cos(a), s = Math.Sin(a);
            var normal = new Vector3D(c, 0, s);
            mesh.Positions.Add(new Point3D(radius * c, y0, radius * s)); mesh.Normals.Add(normal); mesh.TextureCoordinates.Add(new System.Windows.Point(1 - (double)i / segments, 1));
            mesh.Positions.Add(new Point3D(radius * c, y1, radius * s)); mesh.Normals.Add(normal); mesh.TextureCoordinates.Add(new System.Windows.Point(1 - (double)i / segments, 0));
        }
        for (int i = 0; i < segments; i++) { int a = i * 2, b = a + 2; foreach (int k in new[] { a, b, a + 1, a + 1, b, b + 1 }) mesh.TriangleIndices.Add(k); }
        mesh.Freeze();
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
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

    private static GeometryModel3D HalfShell(double y0, double y1, double r0, double r1, double sign, Material material, int segments = 24, bool textured = false)
    {
        var mesh = new MeshGeometry3D();
        for (int i = 0; i <= segments; i++)
        {
            double a = Math.PI * i / segments - Math.PI / 2, c = Math.Cos(a) * sign, s = Math.Sin(a);
            var normal = new Vector3D(c, (r0 - r1) / (y1 - y0), s); normal.Normalize();
            mesh.Positions.Add(new Point3D(r0 * c, y0, r0 * s)); mesh.Normals.Add(normal);
            mesh.Positions.Add(new Point3D(r1 * c, y1, r1 * s)); mesh.Normals.Add(normal);
            if (textured)
            {
                double u = 1 - ((double)i / segments * 0.5 + (sign > 0 ? 0 : 0.5));
                mesh.TextureCoordinates.Add(new System.Windows.Point(u, 1)); mesh.TextureCoordinates.Add(new System.Windows.Point(u, 0));
            }
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
