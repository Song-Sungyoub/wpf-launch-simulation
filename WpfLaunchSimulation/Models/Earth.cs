namespace WpfLaunchSimulation.Models;

// Spherical Earth for the flight model. The inertial frame is the Earth-fixed frame at liftoff (T0):
// +Z north pole, +X Greenwich meridian at T0. The Earth then turns by OmegaRadPerSecond * t about +Z.
public static class Earth
{
    public const double Radius = 6378137.0;
    public const double Mu = 3.986004418e14;
    public const double OmegaRadPerSecond = 7.2921159e-5;
    public const double G0 = 9.80665;

    public static Vec FixedPosition(double latitudeDeg, double longitudeDeg, double altitudeM)
    {
        double lat = latitudeDeg * Math.PI / 180, lon = longitudeDeg * Math.PI / 180, r = Radius + altitudeM;
        return new Vec(r * Math.Cos(lat) * Math.Cos(lon), r * Math.Cos(lat) * Math.Sin(lon), r * Math.Sin(lat));
    }

    // Inertial position (T0 frame) of an Earth-fixed point at mission time t.
    public static Vec FixedToInertial(Vec fixedPosition, double t)
    {
        double a = OmegaRadPerSecond * t, c = Math.Cos(a), s = Math.Sin(a);
        return new Vec(c * fixedPosition.X - s * fixedPosition.Y, s * fixedPosition.X + c * fixedPosition.Y, fixedPosition.Z);
    }
    public static Vec InertialToFixed(Vec inertial, double t) => FixedToInertial(inertial, -t);

    public static (double Latitude, double Longitude, double AltitudeM) Geodetic(Vec fixedPosition)
    {
        double r = fixedPosition.Length;
        return (Math.Asin(fixedPosition.Z / r) * 180 / Math.PI, Math.Atan2(fixedPosition.Y, fixedPosition.X) * 180 / Math.PI, r - Radius);
    }

    // Great-circle distance between two Earth-fixed directions, in metres along the surface.
    public static double SurfaceDistance(Vec a, Vec b) => Radius * Math.Acos(Math.Clamp(Vec.Dot(a.Unit, b.Unit), -1, 1));

    // Elevation of a target above a station's local horizon (spherical Earth), in degrees.
    public static double Elevation(Vec stationFixed, Vec targetFixed)
    {
        var los = targetFixed - stationFixed;
        return Math.Asin(Math.Clamp(Vec.Dot(los.Unit, stationFixed.Unit), -1, 1)) * 180 / Math.PI;
    }

    // Exponential atmosphere (sea-level density 1.225 kg/m³, scale height 8.5 km); zero above 150 km.
    public static double Density(double altitudeM) => altitudeM < 150e3 ? 1.225 * Math.Exp(-altitudeM / 8500.0) : 0;
    public static double PressureRatio(double altitudeM) => Math.Exp(-altitudeM / 7200.0);
    public static double SpeedOfSound(double altitudeM) => altitudeM < 11e3 ? 340.3 - 0.0041 * altitudeM : 295.0;
}
