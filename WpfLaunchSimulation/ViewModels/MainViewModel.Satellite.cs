using System.Windows.Media.Media3D;
using WpfLaunchSimulation.Models;
using WpfLaunchSimulation.Rendering;

namespace WpfLaunchSimulation.ViewModels;

// Performance verification satellite attitude after separation. Public (KSAS 2023 FA5 abstracts): the satellite left the
// vehicle tumbling, was de-spun (magnetometer/magnetorquer, then gyro/reaction wheels) to below 0.3°/s and then turned its
// solar panels to the Sun; the first beacon was received at King Sejong Station 41 min 36 s after launch. The phase
// timings and the initial tumble rate below are illustrative [근사] — only the sequence is published.
public sealed partial class MainViewModel
{
    private const double TumbleRateDeg = 3.0;                   // initial tumble [근사]
    private const double DespinStart = 1250, DespinEnd = 1700;   // coarse de-spin to 0.3°/s
    private const double FineEnd = 1900, SunPointEnd = 2100;     // fine de-spin to rest, then slew to the Sun
    public const double FirstBeacon = 41 * 60 + 36;              // King Sejong Station (published)
    private static readonly Vector3D TumbleAxis = Normalized(new Vector3D(0.35, 0.8, 0.48));
    private bool[] attitudeLogged = new bool[4];

    private static Vector3D Normalized(Vector3D v) { v.Normalize(); return v; }

    // Rotation angle accumulated since separation (closed form, so jumps and time warp stay deterministic).
    private static double TumbleAngleDeg(double t)
    {
        double s = NuriFlight2.SatelliteSeparation;
        if (t <= s) return 0;
        double a1 = TumbleRateDeg * (Math.Min(t, DespinStart) - s);
        if (t <= DespinStart) return a1;
        double tau = (DespinEnd - DespinStart) / Math.Log(TumbleRateDeg / 0.3);
        double d2 = Math.Min(t, DespinEnd) - DespinStart;
        double a2 = TumbleRateDeg * tau * (1 - Math.Exp(-d2 / tau));
        if (t <= DespinEnd) return a1 + a2;
        double d3 = Math.Min(t, FineEnd) - DespinEnd, span = FineEnd - DespinEnd;
        return a1 + a2 + 0.3 * d3 - 0.3 * d3 * d3 / (2 * span);
    }
    public static double SatelliteRateDeg(double t) =>
        t <= NuriFlight2.SatelliteSeparation ? 0 : t <= DespinStart ? TumbleRateDeg
        : t <= DespinEnd ? TumbleRateDeg * Math.Exp(-(t - DespinStart) / ((DespinEnd - DespinStart) / Math.Log(TumbleRateDeg / 0.3)))
        : t <= FineEnd ? 0.3 * (1 - (t - DespinEnd) / (FineEnd - DespinEnd)) : 0;

    // Full model pose: separation frame, then body rotation about the satellite centre.
    private Matrix3D SatellitePose(Debris sat, double t)
    {
        var basePose = Pose(sat.Position, rocketAxis, 1);
        var tumble = new Quaternion(TumbleAxis, TumbleAngleDeg(t));
        var rotation = tumble;
        if (t > FineEnd)
        {
            // Target: +X panel normal toward the Sun, expressed in the separation frame.
            var toModel = basePose; toModel.OffsetX = toModel.OffsetY = toModel.OffsetZ = 0;
            if (toModel.HasInverse) toModel.Invert();
            var sun = toModel.Transform(SunDirection()); sun.Normalize();
            var target = RotationBetween(SatelliteModel.PanelNormal, sun);
            double f = Math.Clamp((t - FineEnd) / (SunPointEnd - FineEnd), 0, 1);
            rotation = Quaternion.Slerp(tumble, target, f * f * (3 - 2 * f));
        }
        var m = Matrix3D.Identity;
        m.RotateAt(rotation, SatelliteModel.Center);
        m.Append(basePose);
        return m;
    }

    private static Quaternion RotationBetween(Vector3D from, Vector3D to)
    {
        from.Normalize(); to.Normalize();
        var axis = Vector3D.CrossProduct(from, to);
        double dot = Math.Clamp(Vector3D.DotProduct(from, to), -1, 1);
        if (axis.LengthSquared < 1e-12) return dot > 0 ? Quaternion.Identity : new Quaternion(new Vector3D(0, 1, 0), 180);
        return new Quaternion(axis, Math.Acos(dot) * 180 / Math.PI);
    }

    private void LogSatellitePhases(double t)
    {
        (double Time, string Text, string? Card)[] phases =
        [
            (DespinStart, "자세 안정화 시작 · 회전 감쇠 (자기토커 → 반작용휠)", "자세 안정화"),
            (FineEnd, "회전 속도 0.3°/s 이하 · 태양 지향 기동 시작", null),
            (SunPointEnd, "태양 지향 완료 · 태양전지판이 태양을 향함", "태양 지향"),
            (FirstBeacon, "남극 세종기지 첫 비콘 수신 (실제 41분 36초)", "첫 교신"),
        ];
        for (int i = 0; i < phases.Length; i++)
        {
            if (t < phases[i].Time || attitudeLogged[i]) continue;
            attitudeLogged[i] = true;
            AddLog(phases[i].Time, phases[i].Text + (i < 3 ? " [시각은 예시]" : ""));
            LatestEvent = phases[i].Text.Split(" · ")[0]; Notify(nameof(LatestEvent)); Notify(nameof(LatestEventVisibility));
            if (phases[i].Card is { } card) ShowCardFor(card);
        }
    }
    private void ResetSatellitePhases() => attitudeLogged = new bool[4];
}
