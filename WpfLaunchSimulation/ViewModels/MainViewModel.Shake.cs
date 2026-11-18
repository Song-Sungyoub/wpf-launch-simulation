using System.Windows.Media.Media3D;
using WpfLaunchSimulation.Models;

namespace WpfLaunchSimulation.ViewModels;

// Onboard camera shake by phase: pad acoustics at liftoff, buffeting around Max-Q, engine vibration until
// fairing release, then only separation shocks. Amplitudes are illustrative; depends on mission time only.
public sealed partial class MainViewModel
{
    private const double QReference = 50e3;    // Pa, about the simulated Max-Q (50.4 kPa)
    private bool shakeOn = true;
    public bool ShakeOn { get => shakeOn; set { shakeOn = value; displayDirty = true; Notify(); } }

    // Angular shake amplitude in degrees.
    private double ShakeAmplitudeDeg(double t)
    {
        if (t <= -4) return 0;
        if (t <= 0) return 0.25 * (t + 4) / 4;                 // ignition and thrust build-up on the pad
        double a = 0;
        if (sim.FairingAttached && sim.EngineBurning)
        {
            a += sim.Stage == 1 ? 0.12 : 0.04;                   // engine/structure; weaker for the upper stages in vacuum
            if (sim.Stage == 1) a += 0.6 * Math.Exp(-sim.Altitude / 1500);   // acoustic load near the ground
        }
        double q = sim.FairingAttached ? Math.Clamp(sim.DynamicPressure / QReference, 0, 1.2) : 0;
        a += 0.4 * q + 0.2 * q * Math.Exp(-Math.Pow((sim.Mach - 1) / 0.15, 2)); // buffeting, transonic peak
        foreach (var e in sim.Events)
        {
            double dt = t - e.Time;
            if (dt < 0 || dt > 2) continue;
            double strength = e.Name.Contains("분리") ? 0.9 : sim.FairingAttached && e.Name.Contains("점화") ? 0.45 : 0;
            a += strength * Math.Exp(-dt / 0.25);
        }
        return a;
    }

    // Small rotation of the look direction; sums of incommensurate frequencies stand in for broadband vibration.
    private Vector3D ApplyShake(Vector3D look, Vector3D side, double t)
    {
        double amp = shakeOn ? ShakeAmplitudeDeg(t) * 0.15 * Math.PI / 180 : 0;
        if (amp <= 0) return look;
        // About 1–2.5 Hz, so it reads as sway rather than jumps.
        double yaw = amp * (0.55 * Math.Sin(t * 6.7) + 0.3 * Math.Sin(t * 10.9 + 1.1) + 0.15 * Math.Sin(t * 15.1 + 2.3));
        double pitch = amp * (0.55 * Math.Sin(t * 7.9 + 0.7) + 0.3 * Math.Sin(t * 12.3 + 2.9) + 0.15 * Math.Sin(t * 16.3 + 0.4));
        side -= look * Vector3D.DotProduct(side, look); side.Normalize();
        var normal = Vector3D.CrossProduct(look, side); normal.Normalize();
        var result = look + side * yaw + normal * pitch;
        result.Normalize();
        return result;
    }
}
