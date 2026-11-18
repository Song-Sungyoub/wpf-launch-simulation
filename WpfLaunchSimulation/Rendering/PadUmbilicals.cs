using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WpfLaunchSimulation.Rendering;

// Seven ground umbilicals that swing clear at liftoff; shape and timing are illustrative.
public sealed class PadUmbilicals
{
    public Model3DGroup Root { get; } = new();
    private readonly List<AxisAngleRotation3D> rotations = [];
    public PadUmbilicals()
    {
        var cable = NuriModel.Lit(Color.FromRgb(115, 121, 124));
        var connector = NuriModel.Lit(Color.FromRgb(187, 191, 187));
        foreach (double y in new[] { 8.0, 12.0, 18.0, 25.0, 31.0, 36.5, 42.0 })
        {
            double radius = y < NuriModel.Stage1Top ? NuriModel.Stage1Radius : NuriModel.UpperRadius;
            var pivot = new Point3D(6.6, y, 0);
            var rotation = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0); rotations.Add(rotation);
            var assembly = new Model3DGroup { Transform = new RotateTransform3D(rotation, pivot) };
            var mid = new Point3D(3.7, y - 0.18, 0);
            var end = new Point3D(radius + 0.08, y, 0);
            assembly.Children.Add(NuriModel.DetailBeam(pivot, mid, 0.08, cable));
            assembly.Children.Add(NuriModel.DetailBeam(mid, end, 0.065, cable));
            assembly.Children.Add(NuriModel.Box(end.X + 0.03, end.Y, end.Z, 0.18, 0.34, 0.30, connector));
            Root.Children.Add(assembly);
        }
    }
    public void Update(double t)
    {
        double f = Math.Clamp(t / 1.5, 0, 1); f = f * f * (3 - 2 * f);
        foreach (var rotation in rotations) rotation.Angle = 90 * f;
    }
}
