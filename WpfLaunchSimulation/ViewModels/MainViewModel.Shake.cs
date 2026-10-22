using System.Windows.Media.Media3D;
using WpfLaunchSimulation.Models;

namespace WpfLaunchSimulation.ViewModels;

// Onboard camera vibration by flight phase. Sources: liftoff acoustics reflected from the pad (strong, fades with altitude),
// aerodynamic buffeting (follows dynamic pressure, with a transonic bump), engine/structure vibration while burning (weak in
// vacuum), short shocks at separations and ignitions, nothing after engine cutoff. Amplitudes are illustrative [근사].
// The shake is a function of simulation time only, so pause, jumps and replays look the same.
public sealed partial class MainViewModel
{
    private const double QReference = 30e3;    // Pa, roughly the flight's Max-Q

    // Angular shake amplitude in degrees.
    private double ShakeAmplitudeDeg(double t)
    {
        if (t <= -4) return 0;
        if (t <= 0) return 0.25 * (t + 4) / 4;                 // ignition and thrust build-up on the pad
        double a = 0;
        if (sim.EngineBurning)
        {
            a += sim.Stage == 1 ? 0.12 : 0.04;                   // engine/structure; weaker for the upper stages in vacuum
            if (sim.Stage == 1) a += 0.6 * Math.Exp(-sim.Altitude / 1500);   // acoustic load near the ground
        }
        double q = Math.Clamp(sim.DynamicPressure / QReference, 0, 1.2);
        a += 0.4 * q + 0.2 * q * Math.Exp(-Math.Pow((sim.Mach - 1) / 0.15, 2)); // buffeting, transonic peak
        foreach (var e in sim.Events)
        {
            double dt = t - e.Time;
            if (dt < 0 || dt > 2) continue;
            double strength = e.Name.Contains("분리") ? 0.9 : e.Name.Contains("점화") ? 0.45 : 0;
            a += strength * Math.Exp(-dt / 0.25);
        }
        return a;
    }

    // Small rotation of the look direction; sums of incommensurate frequencies stand in for broadband vibration.
    private Vector3D ApplyShake(Vector3D look, Vector3D side, double t)
    {
        double amp = ShakeAmplitudeDeg(t) * Math.PI / 180;
        if (amp <= 0) return look;
        double yaw = amp * (0.55 * Math.Sin(t * 71.3) + 0.3 * Math.Sin(t * 43.7 + 1.1) + 0.15 * Math.Sin(t * 113.9 + 2.3));
        double pitch = amp * (0.55 * Math.Sin(t * 67.1 + 0.7) + 0.3 * Math.Sin(t * 39.3 + 2.9) + 0.15 * Math.Sin(t * 121.7 + 0.4));
        side -= look * Vector3D.DotProduct(side, look); side.Normalize();
        var normal = Vector3D.CrossProduct(look, side); normal.Normalize();
        var result = look + side * yaw + normal * pitch;
        result.Normalize();
        return result;
    }
}
