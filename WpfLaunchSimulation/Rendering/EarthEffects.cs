using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using WpfLaunchSimulation.Models;

namespace WpfLaunchSimulation.Rendering;

// Night-side city lights and the atmosphere limb glow. Scene units are Earth radii.
public static class EarthEffects
{
    // City lights (NASA Black Marble) masked by darkness for a fixed sun direction (Earth-fixed, unit vector).
    // The mask is baked once for liftoff; over a ~2 h session the terminator moves ~30° (accepted simplification).
    public static GeometryModel3D NightLights(Uri nightImage, Vec sunFixed)
    {
        var source = new BitmapImage(); source.BeginInit(); source.UriSource = nightImage; source.EndInit(); source.Freeze();
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = converted.PixelWidth, h = converted.PixelHeight;
        var pixels = new byte[w * h * 4]; converted.CopyPixels(pixels, w * 4, 0);
        var output = new byte[w * h * 4];
        var cosLon = new double[w]; var sinLon = new double[w];
        for (int x = 0; x < w; x++) { double lon = ((x + 0.5) / w * 360 - 180) * Math.PI / 180; cosLon[x] = Math.Cos(lon); sinLon[x] = Math.Sin(lon); }
        for (int y = 0; y < h; y++)
        {
            double lat = (90 - (y + 0.5) / h * 180) * Math.PI / 180, cl = Math.Cos(lat), sl = Math.Sin(lat);
            for (int x = 0; x < w; x++)
            {
                double sunDot = cl * cosLon[x] * sunFixed.X + cl * sinLon[x] * sunFixed.Y + sl * sunFixed.Z;
                double night = Math.Clamp((-sunDot + 0.02) / 0.16, 0, 1);
                int p = (y * w + x) * 4;
                double lum = Math.Max(pixels[p], Math.Max(pixels[p + 1], pixels[p + 2])) / 255.0;
                double a = night * Math.Clamp((lum - 0.06) * 1.6, 0, 1);
                output[p] = (byte)(pixels[p] * a); output[p + 1] = (byte)(pixels[p + 1] * a); output[p + 2] = (byte)(pixels[p + 2] * a); output[p + 3] = (byte)(255 * a);
            }
        }
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, output, w * 4); bitmap.Freeze();
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))));
        material.Children.Add(new EmissiveMaterial(new ImageBrush(bitmap)));
        material.Freeze();
        var sphere = SceneGeometry.Sphere(1.0004, material, 160, 80); sphere.Freeze();
        return sphere;
    }

    // Limb glow: a camera-facing ring centred on the silhouette circle of the globe.
    public sealed class LimbGlow
    {
        private const double Extent = 1.08;
        private readonly MeshGeometry3D mesh = new();
        public GeometryModel3D Model { get; }
        public LimbGlow()
        {
            for (int c = 0; c < 4; c++) mesh.Positions.Add(new Point3D());
            mesh.TextureCoordinates = new PointCollection([new(0, 0), new(1, 0), new(1, 1), new(0, 1)]);
            mesh.TriangleIndices = new Int32Collection([0, 2, 1, 0, 3, 2]);
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))));
            material.Children.Add(new EmissiveMaterial(new ImageBrush(RingTexture())));
            material.Freeze();
            Model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        }

        private static BitmapSource RingTexture()
        {
            const int size = 512; double limb = 1 / Extent;
            var pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    double dx = (x + 0.5) / size * 2 - 1, dy = (y + 0.5) / size * 2 - 1, r = Math.Sqrt(dx * dx + dy * dy);
                    double a = r < limb ? 0.55 * Math.Exp(-Math.Pow((limb - r) / 0.01, 2)) : 0.85 * Math.Exp(-(r - limb) / 0.012);
                    if (r >= 1) a = 0;
                    double white = Math.Exp(-Math.Abs(r - limb) / 0.006);
                    byte R = (byte)(110 + 145 * white), G = (byte)(170 + 85 * white), B = 255;
                    int p = (y * size + x) * 4;
                    pixels[p] = (byte)(B * a); pixels[p + 1] = (byte)(G * a); pixels[p + 2] = (byte)(R * a); pixels[p + 3] = (byte)(255 * a);
                }
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4); bitmap.Freeze();
            return bitmap;
        }

        // eye: camera position in scene units with the Earth centre at the origin.
        public void Update(Point3D eye)
        {
            var toEye = (Vector3D)eye; double d = toEye.Length;
            if (d < 1.01) { for (int c = 0; c < 4; c++) mesh.Positions[c] = new Point3D(); return; }
            toEye.Normalize();
            var center = (Point3D)(toEye / d); double ring = Math.Sqrt(1 - 1 / (d * d)) * Extent;
            var a = Vector3D.CrossProduct(toEye, Math.Abs(toEye.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0)); a.Normalize();
            var b = Vector3D.CrossProduct(toEye, a);
            a *= ring; b *= ring;
            mesh.Positions[0] = center - a + b; mesh.Positions[1] = center + a + b; mesh.Positions[2] = center + a - b; mesh.Positions[3] = center - a - b;
        }
    }
}
