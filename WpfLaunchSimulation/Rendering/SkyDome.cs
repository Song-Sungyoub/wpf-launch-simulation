using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace WpfLaunchSimulation.Rendering;

// Sky drawn in the star layer (camera at the origin): a gradient dome aligned with the local vertical — deeper blue
// at the zenith, pale haze at the horizon, fading out with altitude — and the Sun as a disc with glare.
// Colours are illustrative (no scattering model).
public sealed class SkyDome
{
    public Model3DGroup Root { get; } = new();
    private readonly AxisAngleRotation3D tilt = new(new Vector3D(1, 0, 0), 0);
    private readonly GradientStop zenith = new(Colors.Transparent, 0), upper = new(Colors.Transparent, 0.3), lower = new(Colors.Transparent, 0.45), horizon = new(Colors.Transparent, 0.5), below = new(Colors.Transparent, 1);
    private readonly MeshGeometry3D sunMesh = new();
    private readonly ImageBrush sunGlare;

    public SkyDome(Vector3D sunDirection)
    {
        var gradient = new LinearGradientBrush(new GradientStopCollection([zenith, upper, lower, horizon, below]), new Point(0, 0), new Point(0, 1));
        var domeMaterial = new MaterialGroup();
        domeMaterial.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))));
        domeMaterial.Children.Add(new EmissiveMaterial(gradient));
        var dome = SceneGeometry.Sphere(500, domeMaterial, 48, 48);
        dome.BackMaterial = domeMaterial; dome.Material = null;
        dome.Transform = new RotateTransform3D(tilt);
        Root.Children.Add(dome);
        // Sun billboard 450 units away (inside the dome so the haze does not hide it).
        sunDirection.Normalize();
        var a = Vector3D.CrossProduct(sunDirection, Math.Abs(sunDirection.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0)); a.Normalize();
        var b = Vector3D.CrossProduct(sunDirection, a);
        var c = (Point3D)(sunDirection * 450); double size = 60;
        sunMesh.Positions = new Point3DCollection([c - a * size + b * size, c + a * size + b * size, c + a * size - b * size, c - a * size - b * size]);
        sunMesh.TextureCoordinates = new PointCollection([new(0, 0), new(1, 0), new(1, 1), new(0, 1)]);
        sunMesh.TriangleIndices = new Int32Collection([0, 2, 1, 0, 3, 2]);
        sunGlare = new ImageBrush(SunTexture());
        var sunMaterial = new MaterialGroup();
        sunMaterial.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))));
        sunMaterial.Children.Add(new EmissiveMaterial(sunGlare));
        Root.Children.Add(new GeometryModel3D(sunMesh, sunMaterial) { BackMaterial = sunMaterial });
    }

    private static BitmapSource SunTexture()
    {
        const int size = 256; var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = (x + 0.5) / size * 2 - 1, dy = (y + 0.5) / size * 2 - 1, r = Math.Sqrt(dx * dx + dy * dy);
                double disc = r < 0.045 ? 1 : Math.Exp(-(r - 0.045) / 0.012);       // the solar disc (~0.5°)
                double glare = 0.55 * Math.Exp(-r / 0.16) + 0.18 * Math.Exp(-r / 0.45); // halo
                double a = Math.Clamp(disc + glare * (1 - r), 0, 1);
                int p = (y * size + x) * 4;
                pixels[p] = (byte)(225 * a); pixels[p + 1] = (byte)(245 * a); pixels[p + 2] = (byte)(255 * a); pixels[p + 3] = (byte)(255 * a);
            }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4); bitmap.Freeze();
        return bitmap;
    }

    // up: local vertical at the camera; day: 0..1 sunlight; altitudeKm: camera altitude.
    public void Update(Vector3D up, double day, double altitudeKm, Color zenithColor)
    {
        up.Normalize();
        var axis = Vector3D.CrossProduct(new Vector3D(0, 1, 0), up);
        if (axis.LengthSquared > 1e-12) { axis.Normalize(); tilt.Axis = axis; }
        tilt.Angle = Math.Acos(Math.Clamp(up.Y, -1, 1)) * 180 / Math.PI;
        double air = Math.Clamp(1 - altitudeKm / 90, 0, 1) * day;      // how much atmosphere glows above the camera
        double haze = Math.Clamp(1 - altitudeKm / 25, 0, 1);            // pale horizon band near the ground
        Color Haze(Color c, double k) => Color.FromRgb((byte)(c.R + (225 - c.R) * k), (byte)(c.G + (236 - c.G) * k), (byte)(c.B + (245 - c.B) * k));
        Color WithAlpha(Color c, double a) => Color.FromArgb((byte)(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B);
        // Zenith deeper than the reference colour, a broad lighter band toward the horizon, a pale haze line at it.
        Color Deep(Color c) => Color.FromRgb((byte)(c.R * 0.55), (byte)(c.G * 0.68), (byte)(c.B * 0.9));
        zenith.Color = WithAlpha(Deep(zenithColor), air);
        upper.Color = WithAlpha(Haze(zenithColor, 0.12 * haze), air);
        lower.Color = WithAlpha(Haze(zenithColor, 0.22 * haze + 0.04), air);
        horizon.Color = WithAlpha(Haze(zenithColor, 0.55 * haze + 0.12), air);
        below.Color = WithAlpha(Haze(zenithColor, 0.6 * haze + 0.2), air);
        // Glare is strongest in the blue sky; in space the disc stays and the halo shrinks.
        sunGlare.Opacity = 0.85 + 0.15 * air;
    }
}
