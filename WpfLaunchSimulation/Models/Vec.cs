namespace WpfLaunchSimulation.Models;

// Small double-precision vector for the flight dynamics (metres, m/s); WPF types stay in the rendering layer.
public readonly record struct Vec(double X, double Y, double Z)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec operator -(Vec a) => new(-a.X, -a.Y, -a.Z);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public static Vec operator *(double k, Vec a) => a * k;
    public static Vec operator /(Vec a, double k) => new(a.X / k, a.Y / k, a.Z / k);
    public static double Dot(Vec a, Vec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vec Cross(Vec a, Vec b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public double Length => Math.Sqrt(Dot(this, this));
    public Vec Unit { get { double l = Length; return l > 1e-12 ? this / l : new Vec(0, 0, 0); } }
    public static readonly Vec Zero = new(0, 0, 0);
}
