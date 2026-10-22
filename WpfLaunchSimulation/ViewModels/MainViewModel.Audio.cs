using WpfLaunchSimulation.Models;
using WpfLaunchSimulation.Rendering;

namespace WpfLaunchSimulation.ViewModels;

// Sound by view and flight phase. Onboard/3rd-person: loud roar near the ground, crackle and buffeting while the air is
// dense, only a low structure-borne rumble once the upper stages burn in vacuum, silence after cutoff. Pad and control
// room: heard from the ground, fading with distance and gone above the atmosphere. Countdown beeps from T−10 s and thuds
// at separations/ignitions. Muted while paused, at time warp above 5× and during jumps. Levels illustrative.
public sealed partial class MainViewModel
{
    private LaunchAudio? audio;
    private bool soundOn = true;
    private double lastAudioTime = double.NaN;
    public bool SoundOn { get => soundOn; set { soundOn = value; Notify(); } }

    private void UpdateAudio(double t)
    {
        try { audio ??= new LaunchAudio(); }
        catch (Exception) { soundOn = false; Notify(nameof(SoundOn)); return; }   // no audio device or temp folder
        double previous = lastAudioTime; lastAudioTime = t;
        bool live = soundOn && playing && speed <= 5;
        bool continuous = live && !double.IsNaN(previous) && t >= previous && t - previous < 1;   // not a jump
        double rumble = 0, crackle = 0;
        bool engine = t > -4 && (t <= 0 || sim.EngineBurning);
        if (live && engine)
        {
            double ramp = t <= 0 ? (t + 4) / 4 : 1;
            double air = Math.Exp(-Math.Max(sim.Altitude, 0) / 8000);   // relative air density
            double nearGround = t <= 0 ? 1 : Math.Exp(-sim.Altitude / 2000);
            double q = Math.Clamp(sim.DynamicPressure / QReference, 0, 1);
            switch (activeMode)
            {
                case CameraMode.Onboard or CameraMode.Chase:
                    rumble = ramp * (sim.Stage == 1 ? 0.55 + 0.4 * nearGround : 0.25);
                    crackle = ramp * (0.7 * nearGround + 0.35 * q);
                    break;
                case CameraMode.Pad or CameraMode.ControlRoom:
                {
                    double km = t <= 0 ? 0.4 : Math.Sqrt(Math.Pow(sim.Altitude / 1000, 2) + Math.Pow(sim.Downrange / 1000, 2)) + 0.4;
                    double level = ramp * air / (1 + km / 3) * (activeMode == CameraMode.ControlRoom ? 0.35 : 1);
                    rumble = level; crackle = level * 0.6 * nearGround;
                    break;
                }
            }
        }
        audio.SetLevels(live && engine, rumble, crackle);
        if (!live || !continuous) return;
        // Countdown beeps: short each second from T−10 s, long at T−0.
        for (int s = -10; s <= 0; s++)
            if (previous < s && t >= s) audio.Beep(s == 0, activeMode == CameraMode.ControlRoom ? 0.5 : 0.3);
        foreach (var e in sim.Events)
        {
            if (e.Time <= previous || e.Time > t) continue;
            double strength = e.Name.Contains("분리") ? 0.9 : e.Name.Contains("점화") ? 0.5 : 0;
            if (activeMode is CameraMode.Onboard or CameraMode.Chase) audio.Thud(strength);
        }
    }
}
