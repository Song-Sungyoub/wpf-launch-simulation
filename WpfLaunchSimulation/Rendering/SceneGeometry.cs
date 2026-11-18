using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using WpfLaunchSimulation.Models;

namespace WpfLaunchSimulation.Rendering;

// Scene units are Earth radii. Earth-fixed/inertial vectors (x, y, z) map to the right-handed WPF scene as
// (x, z, -y) / R, so the scene's +Y is the north pole.
public static class SceneGeometry
{
    public static Point3D Point(Vec v) => new(v.X / Earth.Radius, v.Z / Earth.Radius, -v.Y / Earth.Radius);
    public static Vector3D Direction(Vec v) { var d = new Vector3D(v.X, v.Z, -v.Y); d.Normalize(); return d; }

    public static GeometryModel3D Sphere(double radius, Material material, int slices = 96, int stacks = 48)
    {
        var mesh = new MeshGeometry3D();
        for (int y = 0; y <= stacks; y++)
        {
            double lat = Math.PI / 2 - Math.PI * y / stacks;
            for (int x = 0; x <= slices; x++)
            {
                double lon = 2 * Math.PI * x / slices - Math.PI;
                var normal = new Vector3D(Math.Cos(lat) * Math.Cos(lon), Math.Sin(lat), -Math.Cos(lat) * Math.Sin(lon));
                mesh.Positions.Add(new Point3D(normal.X * radius, normal.Y * radius, normal.Z * radius));
                mesh.Normals.Add(normal);
                mesh.TextureCoordinates.Add(new Point((double)x / slices, (double)y / stacks));
            }
        }
        for (int y = 0; y < stacks; y++)
            for (int x = 0; x < slices; x++)
            {
                int a = y * (slices + 1) + x, b = a + slices + 1;
                foreach (int index in new[] { a, b, a + 1, a + 1, b, b + 1 }) mesh.TriangleIndices.Add(index);
            }
        mesh.Freeze();
        return new GeometryModel3D(mesh, material);
    }

    // High-resolution spherical cap around the pad, so the coarse globe facets (~150 km) do not show at close range.
    // It lies on the true sphere, above the globe's chords, and samples the same texture.
    // UVs map into the given latitude/longitude texture bounds (an equirectangular regional image).
    public static GeometryModel3D GroundPatch(double latitude, double longitude, double radiusKm, Material material,
        (double LatMin, double LatMax, double LonMin, double LonMax) bounds, int rings = 60, int segments = 90)
    {
        var mesh = new MeshGeometry3D();
        var center = Earth.FixedPosition(latitude, longitude, 0).Unit;
        var east = Vec.Cross(new Vec(0, 0, 1), center).Unit; var north = Vec.Cross(center, east);
        double maxAngle = radiusKm * 1000 / Earth.Radius;
        for (int ring = 0; ring <= rings; ring++)
        {
            // Denser rings near the pad.
            double angle = maxAngle * Math.Pow((double)ring / rings, 2);
            for (int k = 0; k <= segments; k++)
            {
                double az = 2 * Math.PI * k / segments;
                var dir = (center * Math.Cos(angle) + (north * Math.Cos(az) + east * Math.Sin(az)) * Math.Sin(angle)).Unit;
                var geo = Earth.Geodetic(dir * Earth.Radius);
                mesh.Positions.Add(Point(dir * (Earth.Radius + 2)));
                mesh.Normals.Add(Direction(dir));
                mesh.TextureCoordinates.Add(new Point((geo.Longitude - bounds.LonMin) / (bounds.LonMax - bounds.LonMin), (bounds.LatMax - geo.Latitude) / (bounds.LatMax - bounds.LatMin)));
            }
        }
        for (int ring = 0; ring < rings; ring++)
            for (int k = 0; k < segments; k++)
            {
                int a = ring * (segments + 1) + k, b = a + segments + 1;
                foreach (int index in new[] { a, a + 1, b, b, a + 1, b + 1 }) mesh.TriangleIndices.Add(index);
            }
        mesh.Freeze();
        return new GeometryModel3D(mesh, material);
    }

    // Latitude/longitude rectangle on the true sphere (above the globe's chords), UVs spanning the rectangle.
    public static GeometryModel3D RegionPatch(double latMin, double latMax, double lonMin, double lonMax, double stepDeg, Material material)
    {
        var mesh = new MeshGeometry3D();
        int rows = (int)Math.Ceiling((latMax - latMin) / stepDeg), cols = (int)Math.Ceiling((lonMax - lonMin) / stepDeg);
        for (int r = 0; r <= rows; r++)
            for (int c = 0; c <= cols; c++)
            {
                double lat = latMax - (latMax - latMin) * r / rows, lon = lonMin + (lonMax - lonMin) * c / cols;
                var dir = Earth.FixedPosition(lat, lon, 0).Unit;
                mesh.Positions.Add(Point(dir * Earth.Radius));
                mesh.Normals.Add(Direction(dir));
                mesh.TextureCoordinates.Add(new Point((double)c / cols, (double)r / rows));
            }
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int a = r * (cols + 1) + c, b = a + cols + 1;
                foreach (int index in new[] { a, b, a + 1, a + 1, b, b + 1 }) mesh.TriangleIndices.Add(index);
            }
        mesh.Freeze();
        return new GeometryModel3D(mesh, material);
    }

    public static GeometryModel3D Clouds()
    {
        const int width = 2048, height = 1024;
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double value = 0, weight = 0.5;
                for (int cells = 8; cells <= 128; cells *= 2)
                {
                    double sx = (double)x / width * cells + 0.8 * Math.Sin((double)y / height * 18);
                    double sy = (double)y / height * cells;
                    int ix = (int)Math.Floor(sx), iy = (int)Math.Floor(sy);
                    double fx = sx - Math.Floor(sx), fy = sy - iy;
                    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
                    double a = Noise(ix, iy, cells) * (1 - fx) + Noise(ix + 1, iy, cells) * fx;
                    double b = Noise(ix, iy + 1, cells) * (1 - fx) + Noise(ix + 1, iy + 1, cells) * fx;
                    value += (a * (1 - fy) + b * fy) * weight; weight *= 0.5;
                }
                byte alpha = (byte)(Math.Clamp((value - 0.5) * 4, 0, 1) * 110);
                int index = (y * width + x) * 4;
                pixels[index] = pixels[index + 1] = pixels[index + 2] = pixels[index + 3] = alpha;
            }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4); bitmap.Freeze();
        var material = new DiffuseMaterial(new ImageBrush(bitmap)); material.Freeze();
        var clouds = Sphere(1.0016, material, 128, 64); clouds.Freeze(); return clouds;
    }

    private static double Noise(int x, int y, int cells)
    {
        uint h = unchecked((uint)(((x % cells + cells) % cells) * 374761393 + y * 668265263));
        h = unchecked((h ^ (h >> 13)) * 1274126177); h ^= h >> 16;
        return (h & 0xffff) / 65535.0;
    }

    // Translucent shells (~38 and ~76 km) tinting the Earth seen from above. Their flat facets sag ~7 km,
    // so each shell fades out while the camera passes through it; otherwise a hard line crosses the sky.
    public sealed class AtmosphereShells
    {
        private const double SagKm = 7.5, FadeBelowKm = 8, FadeAboveKm = 15;
        private readonly (double TopKm, byte Alpha, SolidColorBrush Brush)[] layers;
        public Model3DGroup Model { get; } = new();
        public AtmosphereShells()
        {
            var specs = new[] { (Radius: 1.006, Alpha: (byte)55), (Radius: 1.012, Alpha: (byte)18) };
            layers = new (double, byte, SolidColorBrush)[specs.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                var brush = new SolidColorBrush(Color.FromArgb(specs[i].Alpha, 65, 155, 255));
                var sphere = Sphere(specs[i].Radius, new EmissiveMaterial(brush), 96, 48);
                sphere.BackMaterial = sphere.Material; sphere.Material = null; Model.Children.Add(sphere);
                layers[i] = ((specs[i].Radius - 1) * Earth.Radius / 1000, specs[i].Alpha, brush);
            }
        }
        public void Update(double cameraAltitudeKm)
        {
            foreach (var (top, alpha, brush) in layers)
            {
                double bottom = top - SagKm;
                double f = Math.Max(1 - Smooth((cameraAltitudeKm - (bottom - FadeBelowKm)) / FadeBelowKm),
                                    Smooth((cameraAltitudeKm - (top + 1)) / FadeAboveKm));
                var a = (byte)Math.Round(alpha * f);
                if (brush.Color.A != a) brush.Color = Color.FromArgb(a, 65, 155, 255);
            }
        }
        private static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    }

    // Flat ribbon along a polyline, oriented to face outward from the Earth centre.
    public static MeshGeometry3D Ribbon(IReadOnlyList<Point3D> points, double width)
    {
        // Bound display mesh size during long time-warp sessions; retain the complete source history.
        int count = Math.Min(points.Count, 4096);
        var mesh = new MeshGeometry3D { Positions = new Point3DCollection(count * 2), TriangleIndices = new Int32Collection(Math.Max(0, count - 1) * 6) };
        for (int i = 0; i < count; i++)
        {
            int index = count <= 1 ? 0 : (int)((long)i * (points.Count - 1) / (count - 1));
            var tangent = points[Math.Min(index + 1, points.Count - 1)] - points[Math.Max(0, index - 1)];
            var side = Vector3D.CrossProduct((Vector3D)points[index], tangent);
            if (side.LengthSquared < 1e-30) side = new Vector3D(1, 0, 0);
            side.Normalize();
            mesh.Positions.Add(points[index] + side * width); mesh.Positions.Add(points[index] - side * width);
            if (i > 0)
            {
                int n = (i - 1) * 2;
                mesh.TriangleIndices.Add(n); mesh.TriangleIndices.Add(n + 2); mesh.TriangleIndices.Add(n + 1);
                mesh.TriangleIndices.Add(n + 1); mesh.TriangleIndices.Add(n + 2); mesh.TriangleIndices.Add(n + 3);
            }
        }
        mesh.Freeze(); return mesh;
    }

    public static Material MarkerMaterial(Color color)
    {
        var material = new MaterialGroup();
        material.Children.Add(new EmissiveMaterial(new SolidColorBrush(color)));
        material.Freeze();
        return material;
    }

    internal static ImageBrush BakeBrush(Drawing drawing, int width, int height)
    {
        var bounds = drawing.Bounds;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(width / bounds.Width, height / bounds.Height));
            dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            dc.DrawDrawing(drawing);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        var brush = new ImageBrush(bitmap); brush.Freeze();
        return brush;
    }
    internal static ImageBrush BakeBrush(Brush fill, int width, int height)
        => BakeBrush(new GeometryDrawing(fill, null, new RectangleGeometry(new Rect(0, 0, width, height))), width, height);
}
