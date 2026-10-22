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
        Payload.Children.Add(new SatelliteModel().Root); // stowed satellite inside the fairing
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
            // Hot white core and a longer orange envelope, both fading to transparent toward the tail.
            var core = PlumeCone(plumeRadius[i], plumeRadius[i] * 0.25, 1.0, Glow(Color.FromArgb(150, 255, 196, 110), Color.FromArgb(70, 255, 150, 70), Color.FromArgb(0, 255, 120, 60)));
            var inner = PlumeCone(plumeRadius[i] * 0.6, plumeRadius[i] * 0.12, 0.4, Glow(Color.FromArgb(240, 255, 252, 235), Color.FromArgb(170, 255, 228, 160), Color.FromArgb(0, 255, 210, 140)));
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

    private static Material Glow(Color start, Color middle, Color end)
    {
        var gradient = new LinearGradientBrush(new GradientStopCollection([new(start, 0), new(middle, 0.45), new(end, 1)]), new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))));
        material.Children.Add(new EmissiveMaterial(gradient));
        material.Freeze(); return material;
    }

    // Open cone from y = 0 (nozzle exit, v = 0) down to y = -length (v = 1), double-sided.
    private static GeometryModel3D PlumeCone(double r0, double r1, double length, Material material, int segments = 32)
    {
        var mesh = new MeshGeometry3D();
        for (int i = 0; i <= segments; i++)
        {
            double a = 2 * Math.PI * i / segments, c = Math.Cos(a), s = Math.Sin(a);
            mesh.Positions.Add(new Point3D(r0 * c, 0, r0 * s)); mesh.TextureCoordinates.Add(new System.Windows.Point((double)i / segments, 0));
            mesh.Positions.Add(new Point3D(r1 * c, -length, r1 * s)); mesh.TextureCoordinates.Add(new System.Windows.Point((double)i / segments, 1));
        }
        for (int i = 0; i < segments; i++) { int a = i * 2, b = a + 2; foreach (int k in new[] { a, a + 1, b, b, a + 1, b + 1 }) mesh.TriangleIndices.Add(k); }
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

// Naro launch complex 2 in local metres: +Y up, +X east, +Z south. From public photos: a green lattice umbilical
// tower (45~48 m, sources differ) with four service arms beside a raised launch deck. Truss layout, lightning masts,
// flame-trench direction and service buildings are illustrative, not drawings.
public static class PadModel
{
    public static Model3DGroup Create()
    {
        var group = new Model3DGroup();
        var concrete = NuriModel.Lit(Color.FromRgb(168, 168, 162));
        var darkConcrete = NuriModel.Lit(Color.FromRgb(118, 116, 110));
        var scorched = NuriModel.Lit(Color.FromRgb(58, 54, 50));
        var green = NuriModel.Lit(Color.FromRgb(46, 118, 68));
        var steel = NuriModel.Lit(Color.FromRgb(120, 124, 130));
        var white = NuriModel.Lit(Color.FromRgb(226, 226, 222));
        var red = NuriModel.Lit(Color.FromRgb(190, 60, 52));

        // Apron, raised deck and launch table (rocket base at y = 0).
        group.Children.Add(NuriModel.Box(0, -1.4, 0, 170, 0.8, 150, darkConcrete));
        group.Children.Add(PadSurroundings.Apron(-0.99, 170, 150));
        group.Children.Add(NuriModel.Box(0, -0.6, 0, 30, 1.2, 30, darkConcrete));
        group.Children.Add(NuriModel.Box(0, -0.15, 0, 9, 0.3, 9, steel));
        foreach (var (x, z) in new[] { (2.6, 2.6), (-2.6, 2.6), (2.6, -2.6), (-2.6, -2.6) })
            group.Children.Add(NuriModel.Box(x, 0.6, z, 0.7, 1.5, 0.7, steel));                // hold-down posts
        // Flame trench: opening under the vehicle and a covered channel to a deflector exit on the south side.
        group.Children.Add(NuriModel.Box(0, -0.25, 0, 4.2, 0.12, 4.2, scorched));
        group.Children.Add(NuriModel.Box(0, -1.0, 26, 9, 0.2, 32, scorched));
        foreach (double x in new[] { -5.0, 5.0 }) group.Children.Add(NuriModel.Box(x, -0.4, 26, 1.2, 2.2, 32, darkConcrete));
        group.Children.Add(NuriModel.Box(0, 0.6, 12, 11, 0.6, 6, darkConcrete));
        group.Children.Add(NuriModel.Box(0, 1.0, 43, 12, 3.5, 1.5, darkConcrete));          // deflector wall

        // Umbilical tower: four legs, horizontals every 3 m and X-bracing on each face.
        const double tx = 9.0, half = 2.4, height = 48;
        var legs = new[] { (tx - half, -half), (tx + half, -half), (tx + half, half), (tx - half, half) };
        foreach (var (x, z) in legs) group.Children.Add(Beam(new(x, -1, z), new(x, height, z), 0.45, green));
        for (double y = 2; y <= height; y += 3)
            for (int k = 0; k < 4; k++)
            {
                var (x0, z0) = legs[k]; var (x1, z1) = legs[(k + 1) % 4];
                group.Children.Add(Beam(new(x0, y, z0), new(x1, y, z1), 0.22, green));
                if (y + 3 <= height)
                {
                    group.Children.Add(Beam(new(x0, y, z0), new(x1, y + 3, z1), 0.14, green));
                    group.Children.Add(Beam(new(x1, y, z1), new(x0, y + 3, z0), 0.14, green));
                }
            }
        group.Children.Add(NuriModel.Box(tx, height + 0.3, 0, 5.6, 0.6, 5.6, green));
        group.Children.Add(Beam(new(tx, height, 0), new(tx, height + 9, 0), 0.18, steel));        // lightning rod
        // Service arms: truss from the tower face to the vehicle, with an umbilical plate.
        foreach (var (y, reach) in new[] { (12.0, NuriModel.Stage1Radius), (24.5, NuriModel.UpperRadius), (34.0, NuriModel.UpperRadius), (41.0, NuriModel.UpperRadius) })
        {
            double x0 = tx - half, x1 = reach + 0.35;
            foreach (double dz in new[] { -0.6, 0.6 })
            {
                group.Children.Add(Beam(new(x0, y, dz), new(x1, y, dz), 0.16, green));
                group.Children.Add(Beam(new(x0, y + 1.2, dz), new(x1, y + 0.6, dz), 0.12, green));
            }
            for (double x = x1; x < x0; x += 1.4) group.Children.Add(Beam(new(x, y, -0.6), new(x, y, 0.6), 0.1, green));
            group.Children.Add(NuriModel.Box(x1 - 0.12, y + 0.3, 0, 0.25, 1.0, 1.0, steel));
        }
        // Lightning masts around the pad (white/red banded).
        foreach (var (x, z) in new[] { (-34.0, -30.0), (34.0, -30.0), (-34.0, 34.0) })
            for (int k = 0; k < 6; k++)
                group.Children.Add(Beam(new(x, -1 + k * 11, z), new(x, -1 + (k + 1) * 11, z), 0.7 - k * 0.07, k % 2 == 0 ? red : white));
        // Water tank and service buildings.
        group.Children.Add(NuriModel.Cylinder(-1, 14, 5, 5, white, -55, -40, 32));
        group.Children.Add(NuriModel.Cylinder(14, 15.5, 5, 0.8, white, -55, -40, 32));
        group.Children.Add(PadSurroundings.Building(-52, 3.5, 32, 22, 9, 14, Color.FromRgb(214, 216, 212)));
        group.Children.Add(NuriModel.Box(-52, 8.3, 32, 22.4, 0.6, 14.4, steel));
        group.Children.Add(PadSurroundings.Building(48, 2.5, 28, 14, 7, 10, Color.FromRgb(196, 204, 210)));
        group.Children.Add(NuriModel.Box(48, 6.1, 28, 14.3, 0.3, 10.3, steel));
        group.Children.Add(NuriModel.Box(40, 0, -55, 10, 2.5, 6, steel));
        group.Freeze();
        return group;
    }

    // Square-section beam between two points.
    private static GeometryModel3D Beam(Point3D a, Point3D b, double thickness, Material material)
    {
        var axis = b - a; double length = axis.Length; axis.Normalize();
        var side = Vector3D.CrossProduct(axis, Math.Abs(axis.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0)); side.Normalize();
        var up = Vector3D.CrossProduct(side, axis);
        var box = NuriModel.Box(0, 0.5, 0, thickness, 1, thickness, material);
        var m = new Matrix3D(side.X, side.Y, side.Z, 0, axis.X * length, axis.Y * length, axis.Z * length, 0, up.X, up.Y, up.Z, 0, a.X, a.Y, a.Z, 1);
        box.Transform = new MatrixTransform3D(m);
        return box;
    }
}
