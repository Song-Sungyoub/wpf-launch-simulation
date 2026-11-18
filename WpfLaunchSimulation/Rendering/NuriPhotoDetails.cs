using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WpfLaunchSimulation.Rendering;

// Visual details after photographs; dimensions are approximate.
public sealed class MassSimulatorModel
{
    public const double BaseY = NuriModel.Stage3Top + 0.32, Height = 1.75, Width = 0.98;
    public const double TopY = BaseY + Height;
    public Model3DGroup Root { get; } = new();
    public MassSimulatorModel()
    {
        var gold = NuriModel.Lit(Color.FromRgb(176, 141, 47));
        var rail = NuriModel.Lit(Color.FromRgb(133, 117, 62));
        var bolt = NuriModel.Lit(Color.FromRgb(76, 78, 73));
        Root.Children.Add(NuriModel.Box(0, BaseY + Height / 2, 0, Width, Height, Width, gold));
        foreach (double y in new[] { BaseY, TopY })
            Root.Children.Add(NuriModel.Box(0, y, 0, Width + 0.06, 0.045, Width + 0.06, rail));
        foreach (double sign in new[] { -1.0, 1.0 })
        {
            foreach (double x in new[] { -Width / 2 + 0.025, Width / 2 - 0.025 })
                Root.Children.Add(NuriModel.Box(x, BaseY + Height / 2, sign * (Width / 2 + 0.009), 0.025, Height, 0.025, rail));
            for (int row = 0; row <= 12; row++)
                foreach (double x in new[] { -Width / 2 + 0.035, Width / 2 - 0.035 })
                    Root.Children.Add(NuriModel.Box(x, BaseY + 0.06 + row * (Height - 0.12) / 12, sign * (Width / 2 + 0.026), 0.012, 0.012, 0.008, bolt));
            Root.Children.Add(NuriModel.Box(sign * (Width / 2 + 0.16), BaseY + 0.035, 0, 0.33, 0.045, 0.14, gold));
        }
    }
}

public sealed partial class NuriModel
{
    private void AddUpperStageDeck()
    {
        const double topRadius = 0.65, baseRadius = UpperRadius - 0.06;
        Stage3.Children.Add(Cylinder(Stage3Top - 1.4, Stage3Top, baseRadius, topRadius, Grey, segments: 64));
        Stage3.Children.Add(Cylinder(Stage3Top, MassSimulatorModel.BaseY, topRadius, 0.50, Dark, segments: 48));
        var metal = Lit(Color.FromRgb(141, 145, 148));
        for (int k = 0; k < 24; k++)
        {
            double a = k * 2 * Math.PI / 24;
            Stage3.Children.Add(DetailBeam(new Point3D((baseRadius + 0.015) * Math.Cos(a), Stage3Top - 1.4, (baseRadius + 0.015) * Math.Sin(a)),
                new Point3D((topRadius + 0.015) * Math.Cos(a), Stage3Top, (topRadius + 0.015) * Math.Sin(a)), 0.025, metal));
        }
        foreach (double a in new[] { 0.2, 2.3, 4.4 })
        {
            var equipment = Box(0, Stage3Top - 0.65, 1.0, 0.27, 0.35, 0.14, Dark);
            equipment.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), a * 180 / Math.PI));
            Stage3.Children.Add(equipment);
        }
    }

    private void AddExternalDetails()
    {
        var metal = Lit(Color.FromRgb(183, 187, 190));
        foreach (var (stage, y, radius) in new[] { (Stage1, 3.2, Stage1Radius), (Stage1, 19.9, Stage1Radius), (Stage2, 25.0, UpperRadius), (Stage2, 34.7, UpperRadius), (Stage3, 37.4, UpperRadius) })
            foreach (double angle in new[] { 0.45, 2.55, 4.65 })
            {
                // External equipment and antenna housings.
                var housing = Box(0, y, radius + 0.035, 0.18, 0.30, 0.10, metal);
                housing.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), angle * 180 / Math.PI));
                stage.Children.Add(housing);
            }
        foreach (var (stage, y, radius) in new[] { (Stage1, 2.1, Stage1Radius), (Stage1, 20.9, Stage1Radius), (Stage2, 24.0, UpperRadius), (Stage2, 35.5, UpperRadius) })
            for (int k = 0; k < 12; k++)
            {
                var bracket = Box(0, y, radius + 0.025, 0.075, 0.22, 0.08, metal);
                bracket.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), k * 30)); stage.Children.Add(bracket);
            }
    }

    internal static GeometryModel3D DetailBeam(Point3D a, Point3D b, double width, Material material)
    {
        var axis = b - a; double length = axis.Length; axis.Normalize();
        var x = Vector3D.CrossProduct(axis, Math.Abs(axis.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0)); x.Normalize();
        var z = Vector3D.CrossProduct(x, axis);
        var model = Box(0, 0.5, 0, width, 1, width, material);
        model.Transform = new MatrixTransform3D(new Matrix3D(x.X, x.Y, x.Z, 0, axis.X * length, axis.Y * length, axis.Z * length, 0, z.X, z.Y, z.Z, 0, a.X, a.Y, a.Z, 1));
        return model;
    }
}

public static partial class PadModel
{
    private static void AddPhotoDetails(Model3DGroup group, double tx, double half, Material green, Material steel, Material white)
    {
        // Work platforms clear of the vehicle; levels are approximate.
        foreach (double y in new[] { 9.0, 17.0, 25.0, 33.0, 41.0 })
        {
            double left = 3.35, right = tx + half + 1.15, front = 3.9, back = -3.9;
            group.Children.Add(NuriModel.Box((left + right) / 2, y, 0, right - left, 0.22, front - back, green));
            foreach (double z in new[] { front, back })
            {
                group.Children.Add(Beam(new Point3D(left, y + 1.05, z), new Point3D(right, y + 1.05, z), 0.065, green));
                group.Children.Add(Beam(new Point3D(left, y + 0.52, z), new Point3D(right, y + 0.52, z), 0.045, green));
                for (double x = left; x < right; x += 1.1)
                    group.Children.Add(Beam(new Point3D(x, y, z), new Point3D(x, y + 1.05, z), 0.045, green));
                group.Children.Add(Beam(new Point3D(tx + half, y - 2, z * 0.6), new Point3D(left, y, z), 0.13, green));
            }
            group.Children.Add(Beam(new Point3D(left, y + 1.05, back), new Point3D(left, y + 1.05, front), 0.065, green));
            for (double z = back; z <= front; z += 1.1)
                group.Children.Add(Beam(new Point3D(left, y, z), new Point3D(left, y + 1.05, z), 0.045, green));
        }
        // Vertical supply lines and ladder with a cage down the far side of the service tower.
        foreach (double z in new[] { -1.6, -0.8, 0.0 })
            group.Children.Add(NuriModel.Cylinder(0, 44, 0.07, 0.07, steel, tx + half + 0.18, z, 12));
        foreach (double x in new[] { tx - 0.45, tx + 0.45 }) group.Children.Add(Beam(new Point3D(x, 0, half + 0.2), new Point3D(x, 45, half + 0.2), 0.045, white));
        for (double y = 0.4; y < 45; y += 0.4) group.Children.Add(Beam(new Point3D(tx - 0.45, y, half + 0.2), new Point3D(tx + 0.45, y, half + 0.2), 0.025, steel));
        foreach (var (x, z) in new[] { (-34.0, -30.0), (34.0, -30.0), (-34.0, 34.0) })
        {
            foreach (double y in new[] { 31.0, 61.0 })
            {
                const int segments = 16; double radius = y > 50 ? 1.25 : 1.7;
                for (int k = 0; k < segments; k++)
                {
                    double a = k * 2 * Math.PI / segments, b = (k + 1) * 2 * Math.PI / segments;
                    var p = new Point3D(x + radius * Math.Cos(a), y, z + radius * Math.Sin(a));
                    var q = new Point3D(x + radius * Math.Cos(b), y, z + radius * Math.Sin(b));
                    group.Children.Add(Beam(p, q, 0.05, steel));
                    group.Children.Add(Beam(new Point3D(p.X, y + 0.9, p.Z), new Point3D(q.X, y + 0.9, q.Z), 0.035, steel));
                    if (k % 2 == 0)
                    {
                        group.Children.Add(Beam(new Point3D(x, y - 1.7, z), p, 0.055, steel));
                        group.Children.Add(Beam(p, new Point3D(p.X, y + 0.9, p.Z), 0.035, steel));
                    }
                }
            }
            group.Children.Add(Beam(new Point3D(x, 65, z), new Point3D(x, 69, z), 0.045, steel));
        }
    }
}
