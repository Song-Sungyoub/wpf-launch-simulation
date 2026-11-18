using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WpfLaunchSimulation.Rendering;

// PVSAT-sized model after photographs; dimensions are approximate.
public sealed class SatelliteModel
{
    public const double BaseY = MassSimulatorModel.TopY + 0.15, SizeX = 0.93, SizeY = 0.903, SizeZ = 0.892;
    public static readonly Point3D Center = new(0, BaseY + SizeY / 2, 0);
    public static readonly Vector3D PanelNormal = new(1, 0, 0);
    public Model3DGroup Root { get; } = new();
    private static readonly Material Gold = NuriModel.Lit(Color.FromRgb(192, 143, 48));
    private static readonly Material Metal = NuriModel.Lit(Color.FromRgb(154, 151, 132));
    private static readonly Material Dark = NuriModel.Lit(Color.FromRgb(29, 33, 39));
    private static readonly Material White = NuriModel.Lit(Color.FromRgb(221, 224, 218));
    private static readonly Material Bolt = NuriModel.Lit(Color.FromRgb(193, 196, 190));
    private double Cy => Center.Y;
    private double Top => BaseY + SizeY;

    public SatelliteModel()
    {
        Box(0, Cy, 0, SizeX, SizeY, SizeZ, SatelliteMaterials.Foil);
        AddAdapter();
        AddSolarFace(1);
        AddEquipmentFace();
        AddRearEquipmentFace();
        AddNarrowPanels();
        AddFoilSeams();
        AddRoofHardware();
    }

    private void Box(double x, double y, double z, double sx, double sy, double sz, Material material) =>
        Root.Children.Add(NuriModel.Box(x, y, z, sx, sy, sz, material));

    private void AddAdapter()
    {
        // Embed only the top of the mount; no exposed face is coplanar with the bus foil.
        Root.Children.Add(NuriModel.Cylinder(BaseY - 0.075, BaseY + 0.012, 0.29, 0.29, Dark, segments: 48));
        Root.Children.Add(NuriModel.Cylinder(BaseY - 0.082, BaseY - 0.062, 0.315, 0.315, Metal, segments: 48));
        foreach (double x in new[] { -0.34, 0.34 })
            foreach (double z in new[] { -0.31, 0.31 })
            {
                Box(x, BaseY - 0.012, z, 0.10, 0.065, 0.095, Gold);
                Pin(new Point3D(x, BaseY - 0.047, z), new Vector3D(0, -1, 0), 0.012, 0.014, Bolt);
            }
        Box(-0.20, BaseY - 0.017, -SizeZ / 2 - 0.025, 0.12, 0.055, 0.05, Metal);
        Box(0.20, BaseY - 0.017, -SizeZ / 2 - 0.025, 0.12, 0.055, 0.05, Metal);
    }

    private void AddSolarFace(double sign)
    {
        double x = sign * (SizeX / 2 + 0.025);
        double height = SizeY * 0.98, width = SizeZ * 1.01;
        Box(x, Cy, 0, 0.025, height, width, SatelliteMaterials.Cells);
        // Warm metal perimeter, protruding slightly beyond the opaque cell substrate.
        double face = x + sign * 0.019;
        foreach (double z in new[] { -width / 2, width / 2 })
            Box(face, Cy, z, 0.016, height + 0.018, 0.018, Gold);
        foreach (double y in new[] { Cy - height / 2, Cy + height / 2 })
            Box(face, y, 0, 0.016, 0.018, width + 0.018, Gold);
        // Two straps divide the array into three cell banks.
        foreach (double dy in new[] { -0.18, 0.18 })
        {
            Box(face + sign * 0.004, Cy + dy, 0, 0.018, 0.052, width, Dark);
            foreach (double z in new[] { -width * 0.43, 0.0, width * 0.43 })
                Pin(new Point3D(face + sign * 0.015, Cy + dy, z), new Vector3D(sign, 0, 0), 0.004, 0.006, Bolt);
        }
        foreach (double dy in new[] { -0.38, -0.28, -0.08, 0.08, 0.28, 0.38 })
            foreach (double z in new[] { -width / 2 + 0.012, width / 2 - 0.012 })
                Pin(new Point3D(face + sign * 0.010, Cy + dy, z), new Vector3D(sign, 0, 0), 0.004, 0.004, White);
        foreach (double dy in new[] { -height / 2, height / 2 })
            foreach (double z in new[] { -width / 2, width / 2 })
                Pin(new Point3D(face + sign * 0.011, Cy + dy, z), new Vector3D(sign, 0, 0), 0.005, 0.007, Bolt);
    }

    private void AddEquipmentFace()
    {
        double z = -SizeZ / 2;
        // Gold side: corner rails, access plates and vertical apertures.
        foreach (double x in new[] { -SizeX / 2 + 0.025, SizeX / 2 - 0.025 })
        {
            Box(x, Cy, z - 0.017, 0.032, SizeY + 0.025, 0.038, Gold);
            foreach (double dy in new[] { -0.36, -0.12, 0.12, 0.36 })
                Pin(new Point3D(x, Cy + dy, z - 0.040), new Vector3D(0, 0, -1), 0.005, 0.006, Bolt);
        }
        foreach (double x in new[] { -0.24, 0.24 })
        {
            Box(x, Cy + 0.23, z - 0.018, 0.085, 0.15, 0.042, Metal);
            Box(x, Cy + 0.23, z - 0.044, 0.049, 0.12, 0.014, Dark);
        }
        Box(-0.08, Cy - 0.015, z - 0.024, 0.24, 0.22, 0.056, Metal);
        Box(-0.08, Cy - 0.015, z - 0.057, 0.20, 0.18, 0.014, White);
        // Equipment plate slats.
        foreach (double dy in new[] { -0.052, -0.026, 0, 0.026, 0.052 })
            Box(-0.08, Cy - 0.015 + dy, z - 0.066, 0.16, 0.0025, 0.004, Metal);
        Box(0.19, Cy - 0.23, z - 0.030, 0.32, 0.18, 0.065, SatelliteMaterials.Foil);
        Beam(new Point3D(-0.40, Cy - 0.38, z - 0.035), new Point3D(-0.25, Cy - 0.11, z - 0.035), 0.012, Gold);
        Beam(new Point3D(0.40, Cy - 0.38, z - 0.035), new Point3D(0.26, Cy - 0.11, z - 0.035), 0.012, Gold);
        foreach (double x in new[] { -0.30, 0.30 })
            Pin(new Point3D(x, Cy - 0.34, z - 0.055), new Vector3D(0, 0, -1), 0.015, 0.018, Dark);
    }

    private void AddRearEquipmentFace()
    {
        double x = -SizeX / 2;
        void Rear(double y, double z, double h, double w, double depth, Material material, double offset = 0.025) =>
            Box(x - offset, Cy + y, z, depth, h, w, material);
        Rear(0.02, 0, 0.23, 0.32, 0.045, Metal);
        Rear(0.02, 0, 0.18, 0.27, 0.012, Dark, 0.054);
        foreach (double z in new[] { -0.15, 0.15 }) Rear(0.02, z, 0.23, 0.014, 0.015, Bolt, 0.062);
        Beam(new Point3D(x - 0.065, Cy - 0.075, -0.13), new Point3D(x - 0.065, Cy + 0.115, 0), 0.011, Metal);
        Beam(new Point3D(x - 0.065, Cy + 0.115, 0), new Point3D(x - 0.065, Cy - 0.075, 0.13), 0.011, Metal);
        Rear(-0.29, 0, 0.10, 0.22, 0.016, Metal);
        Rear(-0.29, 0, 0.076, 0.18, 0.008, White, 0.039);
        foreach (double z in new[] { -0.29, 0.29 })
        {
            foreach (double dy in new[] { -0.08, 0.18 })
            {
                Rear(dy, z, 0.20, 0.15, 0.045, Metal, 0.039);
                Rear(dy, z, 0.16, 0.11, 0.012, Dark, 0.069);
                foreach (double rib in new[] { -0.073, 0.0, 0.073 }) Rear(dy + rib, z, 0.012, 0.15, 0.016, Bolt, 0.079);
                Rear(dy, z, 0.035, 0.042, 0.008, Gold, 0.091);
                var start = new Point3D(x - 0.093, Cy + dy + 0.07, z);
                var bend = new Point3D(x - 0.105, Cy + dy + 0.11, z * 0.62);
                Beam(start, bend, 0.005, White);
                Beam(bend, new Point3D(x - 0.075, Cy + 0.10, z * 0.55), 0.005, White);
            }
        }
        Pin(new Point3D(x - 0.02, Cy - 0.17, 0), new Vector3D(-1, 0, 0), 0.012, 0.015, Dark);
        foreach (double y in new[] { -0.40, 0.40 })
            foreach (double z in new[] { -0.38, -0.18, 0.18, 0.38 })
                Pin(new Point3D(x - 0.012, Cy + y, z), new Vector3D(-1, 0, 0), 0.008, 0.006, Dark);
    }

    private void AddNarrowPanels()
    {
        // Narrow outboard panels beside the equipment face.
        foreach (double sign in new[] { -1.0, 1.0 })
        {
            double z = sign * (SizeZ / 2 + 0.045), x = -SizeX / 2 - 0.11;
            Box(x, Cy + 0.005, z, 0.27, 0.81, 0.018, Dark);
            Box(x, Cy + 0.005, z + sign * 0.013, 0.25, 0.79, 0.008, SatelliteMaterials.Cells);
            foreach (double edge in new[] { -0.135, 0.135 }) Box(x + edge, Cy + 0.005, z, 0.010, 0.82, 0.024, Gold);
            foreach (double dy in new[] { -0.39, 0.0, 0.39 })
            {
                Box(x, Cy + dy, z + sign * 0.020, 0.27, 0.018, 0.009, Dark);
                Pin(new Point3D(-SizeX / 2 + 0.015, Cy + dy, z), new Vector3D(0, 1, 0), 0.035, 0.012, Metal);
            }
            foreach (double dy in new[] { -0.32, -0.22, -0.12, 0.12, 0.22, 0.32 })
                foreach (double dx in new[] { -0.11, 0.11 })
                    Pin(new Point3D(x + dx, Cy + dy, z + sign * 0.021), new Vector3D(0, 0, sign), 0.005, 0.004, Gold);
        }
    }

    private void AddFoilSeams()
    {
        foreach (double z in new[] { -SizeZ / 2 - 0.006, SizeZ / 2 + 0.006 })
        {
            foreach (double y in new[] { -0.40, 0.40 })
                Beam(new Point3D(-0.43, Cy + y, z), new Point3D(0.43, Cy + y, z), 0.008, Gold);
            foreach (double x in new[] { -0.42, 0.42 })
                Beam(new Point3D(x, Cy - 0.40, z), new Point3D(x, Cy + 0.40, z), 0.008, Gold);
        }
    }

    private void AddRoofHardware()
    {
        // Rounded white antenna on a gold pedestal.
        Box(-0.11, Top + 0.052, -0.20, 0.15, 0.12, 0.15, SatelliteMaterials.Foil);
        Beam(new Point3D(-0.11, Top + 0.08, -0.20), new Point3D(0.015, Top + 0.12, -0.20), 0.023, Metal);
        var antenna = NuriModel.Cylinder(0, 0.16, 0.072, 0.029, White, segments: 40);
        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), -68)));
        transform.Children.Add(new TranslateTransform3D(-0.06, Top + 0.12, -0.20));
        antenna.Transform = transform; Root.Children.Add(antenna);
        Box(0.20, Top + 0.018, 0.17, 0.12, 0.048, 0.12, Metal);
        Box(0.20, Top + 0.048, 0.17, 0.09, 0.018, 0.09, White);
        Root.Children.Add(NuriModel.Cylinder(Top, Top + 0.10, 0.033, 0.033, White, segments: 24));
        foreach (double z in new[] { -0.34, 0.31 })
            Box(-0.30, Top + 0.025, z, 0.07, 0.07, 0.065, Gold);
        Beam(new Point3D(-0.30, Top + 0.045, -0.34), new Point3D(-0.30, Top + 0.045, 0.31), 0.005, Metal);
    }

    private void Beam(Point3D from, Point3D to, double radius, Material material) => Pin(from, to - from, (to - from).Length, radius, material);

    private void Pin(Point3D start, Vector3D direction, double length, double radius, Material material)
    {
        direction.Normalize();
        var part = NuriModel.Cylinder(0, length, radius, radius, material, segments: 12);
        var axis = Vector3D.CrossProduct(new Vector3D(0, 1, 0), direction);
        double dot = Math.Clamp(direction.Y, -1, 1);
        var m = Matrix3D.Identity;
        if (axis.LengthSquared > 1e-12) m.Rotate(new Quaternion(axis, Math.Acos(dot) * 180 / Math.PI));
        else if (dot < 0) m.Rotate(new Quaternion(new Vector3D(1, 0, 0), 180));
        m.Translate((Vector3D)start); part.Transform = new MatrixTransform3D(m); Root.Children.Add(part);
    }
}
