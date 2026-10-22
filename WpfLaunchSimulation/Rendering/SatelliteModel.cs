using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WpfLaunchSimulation.Rendering;

// Performance verification satellite, illustrative: KARI publishes the body size (930 × 892 × 903 mm) and mass, not
// the panel layout or deployment sequence. Two solar panels are hinged at the ±X edges of the top face; stowed they
// stand upright (inside the fairing, clear of stage 3), deployed they extend outward. Local metres, +Y along the launch vehicle axis; the model sits
// where the payload rides (above stage 3) so it continues from the separation pose.
public sealed class SatelliteModel
{
    public const double BaseY = NuriModel.Stage3Top + 0.45, Size = 0.92, PanelLength = 1.6, PanelWidth = 0.86;
    public Model3DGroup Root { get; } = new();
    private readonly AxisAngleRotation3D left = new(new Vector3D(0, 0, 1), 0), right = new(new Vector3D(0, 0, 1), 0);
    public double Deployed { get; private set; } = -1;

    public SatelliteModel()
    {
        var foil = NuriModel.Lit(Color.FromRgb(214, 170, 72));
        var dark = NuriModel.Lit(Color.FromRgb(60, 62, 70));
        var cells = new MaterialGroup();
        cells.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(26, 44, 104))));
        cells.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(120, 140, 200)), 60));
        cells.Freeze();
        double cy = BaseY + Size / 2;
        Root.Children.Add(NuriModel.Box(0, cy, 0, Size, Size, Size, foil));
        Root.Children.Add(NuriModel.Box(0, BaseY + 0.03, 0, Size * 0.7, 0.06, Size * 0.7, dark));          // separation ring
        Root.Children.Add(NuriModel.Cylinder(BaseY + Size, BaseY + Size + 0.25, 0.04, 0.04, dark, 0.25, 0.25, 12)); // antenna
        Root.Children.Add(NuriModel.Box(-0.2, BaseY + Size + 0.03, -0.2, 0.22, 0.06, 0.22, dark));         // sensor
        foreach (var (rotation, sign) in new[] { (left, -1.0), (right, 1.0) })
        {
            // Panel built deployed (extending along ±X from the hinge), then rotated down about the hinge to fold.
            var panel = new Model3DGroup();
            panel.Children.Add(NuriModel.Box(sign * (PanelLength / 2 + 0.05), 0, 0, PanelLength, 0.025, PanelWidth, cells));
            panel.Children.Add(NuriModel.Box(sign * 0.03, 0, 0, 0.08, 0.05, 0.12, dark));                     // hinge
            var transform = new Transform3DGroup();
            transform.Children.Add(new RotateTransform3D(rotation));
            transform.Children.Add(new TranslateTransform3D(sign * Size / 2, BaseY + Size - 0.02, 0));
            panel.Transform = transform;
            Root.Children.Add(panel);
        }
        SetDeployment(0);
    }

    // 0 = stowed upright, 1 = fully deployed.
    public void SetDeployment(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction == Deployed) return;
        Deployed = fraction;
        double eased = fraction * fraction * (3 - 2 * fraction);
        double fold = 90 * (1 - eased);
        left.Angle = -fold; right.Angle = fold;
    }
}
