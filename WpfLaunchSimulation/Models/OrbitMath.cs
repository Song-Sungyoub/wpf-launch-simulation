namespace WpfLaunchSimulation.Models;

public readonly record struct OrbitElements(double SemiMajorAxis, double Eccentricity, double PerigeeKm, double ApogeeKm,
    double InclinationDeg, double PeriodSeconds, double TimeToApoapsis, double TimeToPeriapsis)
{
    public bool Bound => double.IsFinite(PeriodSeconds);
}

// Two-body elements from an inertial state, used for the live orbit read-out and to schedule burns.
public static class OrbitMath
{
    public static OrbitElements Elements(Vec r, Vec v)
    {
        double rl = r.Length, energy = Vec.Dot(v, v) / 2 - Earth.Mu / rl;
        var h = Vec.Cross(r, v);
        double inc = Math.Acos(Math.Clamp(h.Z / h.Length, -1, 1)) * 180 / Math.PI;
        if (energy >= 0) return new(double.NaN, 1, double.NaN, double.PositiveInfinity, inc, double.NaN, double.NaN, double.NaN);
        double a = -Earth.Mu / (2 * energy);
        var eVec = Vec.Cross(v, h) / Earth.Mu - r / rl; double e = eVec.Length;
        double n = Math.Sqrt(Earth.Mu / (a * a * a)), period = 2 * Math.PI / n;
        // Mean anomaly from the eccentric anomaly; time to apoapsis (M = π) and periapsis (M = 0).
        double cosE = (1 - rl / a) / Math.Max(e, 1e-9), sinE = Vec.Dot(r, v) / Math.Sqrt(Earth.Mu * a) / Math.Max(e, 1e-9);
        double E = Math.Atan2(sinE, cosE), M = E - e * Math.Sin(E);
        double toApo = Mod(Math.PI - M, 2 * Math.PI) / n, toPeri = Mod(-M, 2 * Math.PI) / n;
        return new(a, e, (a * (1 - e) - Earth.Radius) / 1000, (a * (1 + e) - Earth.Radius) / 1000, inc, period, toApo, toPeri);
    }
    private static double Mod(double x, double m) => ((x % m) + m) % m;

    // Two-body propagation for drawing one revolution (no drag; fine above ~400 km over one orbit).
    public static List<Vec> Revolution(Vec r, Vec v, int samples = 240)
    {
        var points = new List<Vec>();
        var el = Elements(r, v); if (!el.Bound) return points;
        double dt = el.PeriodSeconds / samples;
        for (int i = 0; i <= samples; i++)
        {
            points.Add(r);
            for (int k = 0; k < 10; k++) { double rl = r.Length; v += r * (-Earth.Mu / (rl * rl * rl)) * (dt / 10); r += v * (dt / 10); }
        }
        return points;
    }
}
