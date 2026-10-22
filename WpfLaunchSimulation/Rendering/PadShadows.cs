using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace WpfLaunchSimulation.Rendering;

// Fake cast shadows on the pad (WPF 3D has no shadow maps), in pad-local metres (+Y up). Static structures are projected
// along the sunlight onto the apron and deck planes and rasterised into one soft texture per plane, so overlapping
// parts do not darken twice; contact darkening around footprints goes into the same texture. The shadows are painted
// into the apron/deck surface brushes themselves (a separate translucent layer would fight the surface in the depth
// buffer, and extra material layers in WPF add light, so they cannot darken). The vehicle's shadow is a strip drawing
// in those brushes that follows it until it is too high to cast a visible one.
public static class PadShadows
{
    public const double ApronY = -1.0, DeckY = 0.0;
    private const double PixelsPerMetre = 10, ShadowAlpha = 0.5;   // 10 px/m keeps the 0.2–0.5 m lattice members visible

    // pad: the pad model without overlays; sun: direction toward the Sun in pad-local axes (Y > 0).
    // Returns ready surface brushes for the apron (concrete) and the deck (plain), each with the static shadows and the
    // vehicle shadow drawing.
    public static (Brush Apron, Brush Deck) Surfaces(Model3D pad, Vector3D sun, Drawing vehicle)
    {
        sun.Normalize();
        var triangles = new List<Point3D[]>();
        Collect(pad, Matrix3D.Identity, triangles);
        return (SurfaceBrush(PadModel.ApronArea, PadSurroundings.Concrete(), Plane(triangles, sun, ApronY, PadModel.ApronArea, contact: true), vehicle),
                SurfaceBrush(PadModel.DeckArea, new SolidColorBrush(PadModel.DeckColor), Plane(triangles, sun, DeckY, PadModel.DeckArea, contact: false), vehicle));
    }

    // area in metres; the brush is stretched over a surface covering exactly that area.
    public static Brush SurfaceBrush(Rect area, Brush fill, Drawing? shadows = null, Drawing? vehicle = null)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(fill, null, new RectangleGeometry(area)));
        if (shadows is not null) group.Children.Add(shadows);
        if (vehicle is not null) group.Children.Add(vehicle);
        group.ClipGeometry = new RectangleGeometry(area);
        var brush = new DrawingBrush(group) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = area, Stretch = Stretch.Fill };
        // Keep the rasterised texture between frames instead of redrawing the drawing every frame.
        RenderOptions.SetCachingHint(brush, CachingHint.Cache);
        RenderOptions.SetCacheInvalidationThresholdMinimum(brush, 0.5); RenderOptions.SetCacheInvalidationThresholdMaximum(brush, 2);
        return brush;
    }

    private static void Collect(Model3D model, Matrix3D parent, List<Point3D[]> triangles)
    {
        var m = parent; if (model.Transform is { } t) m.Prepend(t.Value);
        if (model is Model3DGroup g) { foreach (var child in g.Children) Collect(child, m, triangles); return; }
        if (model is not GeometryModel3D { Geometry: MeshGeometry3D mesh }) return;
        var p = mesh.Positions; var idx = mesh.TriangleIndices;
        for (int i = 0; i + 2 < idx.Count; i += 3)
        {
            var tri = new[] { m.Transform(p[idx[i]]), m.Transform(p[idx[i + 1]]), m.Transform(p[idx[i + 2]]) };
            if (tri.All(v => v.Y <= DeckY + 0.05)) continue;   // apron, deck and trench: nothing to cast
            triangles.Add(tri);
        }
    }

    // Project onto y = plane along the sunlight; parts below the plane stay where they are.
    private static Point Project(Point3D v, Vector3D sun, double plane)
    {
        double h = Math.Max(v.Y - plane, 0);
        return new Point(v.X - sun.X / sun.Y * h, v.Z - sun.Z / sun.Y * h);
    }

    private static Drawing Plane(List<Point3D[]> triangles, Vector3D sun, double plane, Rect area, bool contact)
    {
        int w = (int)(area.Width * PixelsPerMetre), h = (int)(area.Height * PixelsPerMetre);
        Point ToPixel(Point p) => new((p.X - area.X) * PixelsPerMetre, (p.Y - area.Y) * PixelsPerMetre);
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = geometry.Open())
            foreach (var tri in triangles)
            {
                if (tri.All(v => v.Y < plane + 0.05)) continue;
                Point a = ToPixel(Project(tri[0], sun, plane)), b = ToPixel(Project(tri[1], sun, plane)), c = ToPixel(Project(tri[2], sun, plane));
                // Front and back faces of a solid project with opposite winding and would cancel under the nonzero rule;
                // give every triangle the same winding so overlaps add up instead.
                if ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) < 0) (b, c) = (c, b);
                ctx.BeginFigure(a, true, true);
                ctx.PolyLineTo([b, c], false, false);
            }
        geometry.Freeze();
        var visual = new DrawingVisual { Effect = new BlurEffect { Radius = 2.5, KernelType = KernelType.Gaussian } };
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (contact)
            {
                // Contact darkening: soft dark pads under footprints (tower legs, masts, tank, buildings, deck rim).
                var soft = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
                foreach (var (x, z, sx, sz) in new[] { (9.0, 0.0, 7.0, 7.0), (-52.0, 32.0, 25.0, 17.0), (48.0, 28.0, 17.0, 13.0), (-55.0, -40.0, 12.0, 12.0),
                                                        (-34.0, -30.0, 3.0, 3.0), (34.0, -30.0, 3.0, 3.0), (-34.0, 34.0, 3.0, 3.0), (0.0, 0.0, 33.0, 33.0) })
                    dc.DrawRoundedRectangle(soft, null, new Rect(ToPixel(new Point(x - sx / 2, z - sz / 2)), new Size(sx * PixelsPerMetre, sz * PixelsPerMetre)), 12, 12);
            }
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(255 * ShadowAlpha), 0, 0, 0)), null, geometry);
            // The lattice tower: from a distance its members and their shadows merge into a dappled band, so add the
            // projected outline of the whole tower at low density on top of the member shadows.
            var tower = new StreamGeometry();
            using (var ctx = tower.Open())
            {
                const double tx = 9, half = 2.4, top = 48;
                foreach (var (x0, z0, x1, z1) in new[] { (tx - half, -half, tx + half, -half), (tx + half, -half, tx + half, half), (tx + half, half, tx - half, half), (tx - half, half, tx - half, -half) })
                {
                    ctx.BeginFigure(ToPixel(Project(new Point3D(x0, plane, z0), sun, plane)), true, true);
                    ctx.PolyLineTo([ToPixel(Project(new Point3D(x1, plane, z1), sun, plane)), ToPixel(Project(new Point3D(x1, top, z1), sun, plane)),
                        ToPixel(Project(new Point3D(x0, top, z0), sun, plane))], false, false);
                }
            }
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)), null, tower);
        }
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
        var drawing = new ImageDrawing(bitmap, area); drawing.Freeze();
        return drawing;
    }

    // The vehicle's shadow: a soft strip from where its base projects, along the sunlight, as long as its projected
    // length; it fades as the vehicle climbs (the umbra shrinks and the shadow leaves the pad). The axis tilt during the
    // first ~250 m is small and ignored. One drawing is shared by the deck and apron brushes.
    public sealed class Rocket
    {
        private readonly RectangleGeometry strip = new();
        private readonly RotateTransform rotation = new();
        private readonly DrawingGroup group = new() { Opacity = 0 };
        private (double X, double Z) last = (double.NaN, 0);
        public Drawing Drawing => group;

        public Rocket()
        {
            var brush = new ImageBrush(StripTexture()); brush.Freeze();
            group.Children.Add(new GeometryDrawing(brush, null, strip));
            group.Transform = rotation;
        }

        // Dark across the middle, soft at the sides, rounded at the nose end (v = 1).
        private static BitmapSource StripTexture()
        {
            const int w = 32, h = 256; var pixels = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double u = (x + 0.5) / w * 2 - 1, v = (y + 0.5) / h;
                    double a = ShadowAlpha * Math.Clamp((1 - Math.Abs(u)) / 0.35, 0, 1) * Math.Clamp((1 - v) / 0.04, 0, 1);
                    pixels[(y * w + x) * 4 + 3] = (byte)(255 * a);
                }
            var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4); bitmap.Freeze();
            return bitmap;
        }

        // baseLocal: vehicle base in pad-local metres; length/radius: vehicle size; sun: toward the Sun (pad-local).
        public void Update(Point3D baseLocal, double length, double radius, Vector3D sun)
        {
            sun.Normalize();
            double opacity = Math.Clamp(1 - baseLocal.Y / 250, 0, 1);
            if (opacity <= 0) { if (group.Opacity != 0) group.Opacity = 0; return; }
            double horizontal = Math.Sqrt(sun.X * sun.X + sun.Z * sun.Z), stretch = horizontal / sun.Y;
            double dx = -sun.X / horizontal, dz = -sun.Z / horizontal, rise = Math.Max(baseLocal.Y, 0);
            double x0 = baseLocal.X + dx * stretch * rise, z0 = baseLocal.Z + dz * stretch * rise;
            // Re-rasterising the surface brushes is not free: skip changes under 5 cm.
            if (Math.Abs(x0 - last.X) < 0.05 && Math.Abs(z0 - last.Z) < 0.05 && Math.Abs(opacity - group.Opacity) < 0.02) return;
            last = (x0, z0);
            group.Opacity = opacity;
            strip.Rect = new Rect(x0 - radius, z0, 2 * radius, length * stretch);
            // Turn the strip's +z axis onto the shadow direction about its base.
            rotation.Angle = Math.Atan2(-dx, dz) * 180 / Math.PI; rotation.CenterX = x0; rotation.CenterY = z0;
        }
    }
}
