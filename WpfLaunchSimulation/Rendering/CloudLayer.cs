using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace WpfLaunchSimulation.Rendering;

// Thin cirrus sheet over the pad, in pad-local metres. Kept in the far layer: the near layer's 0.2 m
// near plane leaves no depth precision tens of km out.
public sealed class CloudLayer
{
    public Model3DGroup Root { get; } = new();
    private const double CirrusHeight = 9000, CirrusSize = 140000;
    private const double HideAboveMetres = 200e3;   // from orbit-scale heights the global cloud texture carries the look
    private readonly GeometryModel3D cirrus = Cirrus(CirrusHeight, CirrusSize);
    private bool hidden;

    public CloudLayer() => Root.Children.Add(cirrus);

    // Thin cirrus streaks on a horizontal sheet, stretched along one direction.
    private static GeometryModel3D Cirrus(double height, double size)
    {
        const int n = 512; var pixels = new byte[n * n * 4];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double u = (double)x / n, v = (double)y / n;
                double streak = PadSurroundings.Fbm(u * 3, v * 22, 5), patch = PadSurroundings.Fbm(u * 2 + 5, v * 2 + 9, 3);
                double edge = Math.Clamp(Math.Min(Math.Min(u, 1 - u), Math.Min(v, 1 - v)) / 0.2, 0, 1);
                double a = Math.Clamp((streak - 0.47) * 3, 0, 1) * Math.Clamp((patch - 0.38) * 3, 0, 1) * edge * 0.75;
                int p = (y * n + x) * 4; byte c = (byte)(250 * a);
                pixels[p] = c; pixels[p + 1] = c; pixels[p + 2] = c; pixels[p + 3] = (byte)(255 * a);
            }
        var bitmap = BitmapSource.Create(n, n, 96, 96, PixelFormats.Pbgra32, null, pixels, n * 4); bitmap.Freeze();
        var mesh = new MeshGeometry3D();
        foreach (var (x, z) in new[] { (-1.0, -1.0), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0) })
        {
            mesh.Positions.Add(new Point3D(x * size / 2, height, z * size / 2)); mesh.Normals.Add(new Vector3D(0, -1, 0));
            mesh.TextureCoordinates.Add(new Point((x + 1) / 2, (z + 1) / 2));
        }
        mesh.TriangleIndices = new Int32Collection([0, 1, 2, 0, 2, 3]);
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new ImageBrush(bitmap) { Opacity = 0.6 }));
        material.Children.Add(new EmissiveMaterial(new ImageBrush(bitmap) { Opacity = 0.5 }));
        material.Freeze();
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    // camera, right: camera position and screen-right axis in pad-local metres.
    public void Update(Point3D camera, Vector3D right)
    {
        // Drop the sheet when it is behind the Earth or the camera is far above it.
        double earthRadius = Models.Earth.Radius;
        bool hide = EarthOcclusion.Hidden(new Point3D(camera.X / earthRadius, 1 + camera.Y / earthRadius, camera.Z / earthRadius),
            new Point3D(0, 1 + CirrusHeight / earthRadius, 0), CirrusSize * 0.75 / earthRadius) || camera.Y > HideAboveMetres;
        if (hide == hidden) return;
        hidden = hide;
        if (hide) Root.Children.Clear(); else Root.Children.Add(cirrus);
    }
}
