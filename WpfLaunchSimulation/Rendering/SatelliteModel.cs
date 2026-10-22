using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WpfLaunchSimulation.Rendering;

// Performance verification satellite (PVSAT). Public: body 930 × 892 × 903 mm, 162.5 kg, solar panels that had to be
// turned to the Sun after separation (KSAS 2023 FA5 abstracts). From released photos [추정]: no deployable wings; flat
// panels fixed on the two sides next to the cubesat-ejector face, with two metal straps across each; gold MLI elsewhere.
// Local metres, +Y along the launch vehicle axis, placed where the payload rides (above stage 3).
public sealed class SatelliteModel
{
    public const double BaseY = NuriModel.Stage3Top + 0.45, SizeX = 0.93, SizeY = 0.903, SizeZ = 0.892;
    public static readonly Point3D Center = new(0, BaseY + SizeY / 2, 0);
    public Model3DGroup Root { get; } = new();

    public SatelliteModel()
    {
        var foil = NuriModel.Lit(Color.FromRgb(214, 170, 72));
        var dark = NuriModel.Lit(Color.FromRgb(60, 62, 70));
        var metal = NuriModel.Lit(Color.FromRgb(170, 172, 178));
        var cells = new MaterialGroup();
        cells.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(24, 40, 96))));
        cells.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(130, 150, 210)), 60));
        cells.Freeze();
        double cy = Center.Y;
        Root.Children.Add(NuriModel.Box(0, cy, 0, SizeX, SizeY, SizeZ, foil));
        Root.Children.Add(NuriModel.Box(0, BaseY + 0.03, 0, SizeX * 0.7, 0.06, SizeZ * 0.7, dark));   // separation interface
        // Body-mounted panels on ±X, slightly stood off, with two straps each.
        foreach (double sign in new[] { -1.0, 1.0 })
        {
            double x = sign * (SizeX / 2 + 0.025);
            Root.Children.Add(NuriModel.Box(x, cy, 0, 0.025, SizeY * 0.94, SizeZ * 0.94, cells));
            foreach (double y in new[] { -0.17, 0.17 })
                Root.Children.Add(NuriModel.Box(x + sign * 0.014, cy + y, 0, 0.012, 0.035, SizeZ * 0.96, metal));
        }
        // Cubesat ejector face (+Z): five ports.
        foreach (var (px, py) in new[] { (-0.25, 0.18), (0.0, 0.18), (0.25, 0.18), (-0.13, -0.12), (0.13, -0.12) })
            Root.Children.Add(NuriModel.Box(px, cy + py, SizeZ / 2 + 0.01, 0.17, 0.17, 0.03, dark));
        // Top: S-band antenna and sun sensor (positions illustrative).
        Root.Children.Add(NuriModel.Cylinder(BaseY + SizeY, BaseY + SizeY + 0.18, 0.035, 0.035, dark, 0.28, -0.25, 12));
        Root.Children.Add(NuriModel.Box(-0.22, BaseY + SizeY + 0.03, 0.22, 0.12, 0.06, 0.12, metal));
    }

    // Local direction of the panel normal used for sun pointing (+X panel).
    public static readonly Vector3D PanelNormal = new(1, 0, 0);
}
