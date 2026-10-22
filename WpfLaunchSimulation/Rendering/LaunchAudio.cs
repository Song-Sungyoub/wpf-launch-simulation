using System.IO;
using System.Windows.Media;

namespace WpfLaunchSimulation.Rendering;

// Procedural sound: no recorded audio is shipped. Short WAV clips (engine rumble, near-field crackle, countdown beeps,
// separation thud) are synthesised once into %TEMP% and played with MediaPlayer, which mixes several players and has
// per-player volume. Levels are chosen for the effect, not measured.
public sealed class LaunchAudio
{
    private const int Rate = 22050;
    private readonly MediaPlayer rumble = new(), crackle = new();
    private readonly List<MediaPlayer> oneShots = [];
    private readonly Uri beepUri, beepLongUri, thudUri;
    private bool loopsPlaying;

    public LaunchAudio()
    {
        var folder = Path.Combine(Path.GetTempPath(), "WpfLaunchSimulation", "audio-v1");
        Directory.CreateDirectory(folder);
        Uri Clip(string name, Func<short[]> make)
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path)) WriteWav(path, make());
            return new Uri(path);
        }
        var rumbleUri = Clip("rumble.wav", () => Loop(Rumble(6.5)));
        var crackleUri = Clip("crackle.wav", () => Loop(Crackle(4.5)));
        beepUri = Clip("beep.wav", () => Tone(1000, 0.14));
        beepLongUri = Clip("beep-long.wav", () => Tone(1000, 0.7));
        thudUri = Clip("thud.wav", ThudClip);
        foreach (var (player, uri) in new[] { (rumble, rumbleUri), (crackle, crackleUri) })
        {
            player.Open(uri); player.Volume = 0;
            player.MediaEnded += (_, _) => { player.Position = TimeSpan.Zero; player.Play(); };
        }
    }

    // Continuous levels 0..1; active = false pauses the loops (paused, fast time warp or sound off).
    public void SetLevels(bool active, double rumbleLevel, double crackleLevel)
    {
        if (active != loopsPlaying)
        {
            loopsPlaying = active;
            if (active) { rumble.Play(); crackle.Play(); } else { rumble.Pause(); crackle.Pause(); }
        }
        rumble.Volume = Math.Clamp(rumbleLevel, 0, 1);
        crackle.Volume = Math.Clamp(crackleLevel, 0, 1);
    }

    public void Beep(bool longBeep, double volume) => PlayOnce(longBeep ? beepLongUri : beepUri, volume);
    public void Thud(double volume) => PlayOnce(thudUri, volume);

    private void PlayOnce(Uri uri, double volume)
    {
        if (volume <= 0.01) return;
        var player = new MediaPlayer();
        player.Open(uri); player.Volume = Math.Clamp(volume, 0, 1); player.Play();
        player.MediaEnded += (_, _) => { player.Close(); oneShots.Remove(player); };
        oneShots.Add(player);
    }

    // ---- Synthesis ----------------------------------------------------------------------------------------
    // Brown-ish noise (leaky integrated white noise) with slow amplitude flutter: a deep, uneven roar.
    private static double[] Rumble(double seconds)
    {
        var random = new Random(11); int n = (int)(seconds * Rate);
        var s = new double[n]; double brown = 0, low = 0;
        for (int i = 0; i < n; i++)
        {
            brown = brown * 0.995 + (random.NextDouble() * 2 - 1) * 0.06;
            low += (brown - low) * 0.08;
            double t = (double)i / Rate;
            double flutter = 0.8 + 0.12 * Math.Sin(t * 2 * Math.PI * 3.1) + 0.08 * Math.Sin(t * 2 * Math.PI * 7.3 + 1);
            s[i] = low * flutter * 4.5;
        }
        return s;
    }

    // Band-limited noise with random pops: the crackle of a rocket exhaust heard from close by.
    private static double[] Crackle(double seconds)
    {
        var random = new Random(23); int n = (int)(seconds * Rate);
        var s = new double[n]; double lp = 0, hp = 0, pop = 0;
        for (int i = 0; i < n; i++)
        {
            double white = random.NextDouble() * 2 - 1;
            lp += (white - lp) * 0.35; double band = lp - hp; hp += (lp - hp) * 0.05;
            if (random.NextDouble() < 0.0025) pop = (random.NextDouble() * 0.6 + 0.4) * (random.Next(2) * 2 - 1);
            pop *= 0.93;
            s[i] = band * 0.55 + pop * 0.7;
        }
        return s;
    }

    private static short[] Tone(double hz, double seconds)
    {
        int n = (int)(seconds * Rate); var s = new short[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate, envelope = Math.Min(1, Math.Min(t / 0.005, (seconds - t) / 0.02));
            s[i] = (short)(Math.Sin(2 * Math.PI * hz * t) * envelope * 0.45 * short.MaxValue);
        }
        return s;
    }

    // Low thump: a falling 55→35 Hz sine plus a short noise burst, decaying over ~0.6 s.
    private static short[] ThudClip()
    {
        var random = new Random(5); int n = (int)(0.9 * Rate); var s = new short[n]; double phase = 0, lp = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            phase += 2 * Math.PI * (35 + 20 * Math.Exp(-t / 0.15)) / Rate;
            lp += ((random.NextDouble() * 2 - 1) - lp) * 0.1;
            double v = Math.Sin(phase) * Math.Exp(-t / 0.22) + lp * 1.6 * Math.Exp(-t / 0.05);
            s[i] = (short)(Math.Clamp(v * 0.8, -1, 1) * short.MaxValue);
        }
        return s;
    }

    // Seamless loop: cross-fade the last 0.5 s into the start, then normalise.
    private static short[] Loop(double[] s)
    {
        int fade = Rate / 2, n = s.Length - fade;
        var r = new double[n];
        for (int i = 0; i < n; i++) r[i] = s[i];
        for (int i = 0; i < fade; i++) { double w = (double)i / fade; r[i] = s[i] * w + s[n + i] * (1 - w); }
        double peak = r.Max(Math.Abs);
        return r.Select(v => (short)(v / peak * 0.85 * short.MaxValue)).ToArray();
    }

    private static void WriteWav(string path, short[] samples)
    {
        var temp = path + ".tmp";
        using (var w = new BinaryWriter(File.Create(temp)))
        {
            int bytes = samples.Length * 2;
            w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(bytes);
            foreach (var v in samples) w.Write(v);
        }
        File.Move(temp, path, true);
    }
}
