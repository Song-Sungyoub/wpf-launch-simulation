using System.Windows.Media.Media3D;

namespace WpfLaunchSimulation.Rendering;

// Conservative whole-object culling between separately composited depth layers. Scene Earth radius = 1.
public static class EarthOcclusion
{
    public static bool Hidden(Point3D eye, Point3D center, double radius)
    {
        var toEarth = -(Vector3D)eye; double eyeRadius = toEarth.Length;
        var toTarget = center - eye; double distance = toTarget.Length;
        if (eyeRadius <= 1 || distance <= radius) return false;
        // Only objects entirely beyond the tangent plane can be fully hidden.
        if (distance - radius <= Math.Sqrt(eyeRadius * eyeRadius - 1)) return false;
        double angle = Math.Acos(Math.Clamp(Vector3D.DotProduct(toEarth, toTarget) / (eyeRadius * distance), -1, 1));
        return angle + Math.Asin(Math.Clamp(radius / distance, 0, 1)) < Math.Asin(1 / eyeRadius);
    }
}
