namespace WpfLaunchSimulation.Models;

public sealed record StageSpec(string Name, double PropellantKg, double DryKg, double MassFlowKgPerS,
    double ThrustVacuumN, double ThrustSeaLevelN, double DiameterM, double LengthM);

// Nuri (KSLV-II) flight 2, 2022-06-21 16:00:00 KST. Sources and the derivation of every value are in
// docs/research-nuri-flight2.md. Public: engine thrust, specific impulse, mass flow, burn times, liftoff mass,
// event times/altitudes. Not public, approximated [근사]: stage dry masses, pitch programme and guidance.
// The dry masses were tuned with an offline prototype so that liftoff mass is 201.5 t and the published
// event times/altitudes and a 700 km sun-synchronous orbit are reproduced.
public static class NuriFlight2
{
    public static readonly DateTimeOffset Liftoff = new(2022, 6, 21, 16, 0, 0, TimeSpan.FromHours(9));
    public const string Title = "누리호 2차 발사";

    // Naro Space Center launch complex 2 (en.wikipedia; KARI publishes 34.43°N 127.53°E).
    // Altitude [근사]: the scene terrain is a smooth sphere at sea level, so the pad sits just above it (the real pad
    // elevation is not published; launch complex 1 is reported at about 110 m).
    public const double PadLatitude = 34.431636, PadLongitude = 127.534411, PadAltitudeM = 3;

    // KRE-075 (KARI): sea-level thrust 674.7 kN, vacuum Isp 304.8 s, 255 kg/s per engine; stage 1 clusters four.
    public static readonly StageSpec Stage1 = new("1단", 125.5e3, 19.8e3, 1020.0, 1020.0 * 304.8 * Earth.G0, 4 * 674.7e3, 3.5, 21.6);
    // Vacuum KRE-075: 788 kN, Isp 315.4 s (≈254.8 kg/s), burn 143~148 s.
    public static readonly StageSpec Stage2 = new("2단", 36.4e3, 4.4e3, 254.8, 788e3, 788e3, 2.6, 13.6);
    // KRE-007: 68.7 kN, Isp 325.1 s (≈21.55 kg/s), planned burn 521 s.
    public static readonly StageSpec Stage3 = new("3단", 11.2e3, 1.8e3, 21.55, 68.7e3, 68.7e3, 2.6, 7.0);
    public const double FairingKg = 750;        // [근사]
    public const double PayloadKg = 1500;       // performance verification satellite + mass simulator/adapter (KARI)

    public const double TargetAltitudeM = 700e3, TargetInclinationDeg = 98.2;
    public const double LaunchAzimuthDeg = 192.8; // [근사] from the target inclination and Earth rotation

    // Guidance [근사]
    public const double PitchKickStart = 10, PitchKickEnd = 22, PitchKickDeg = 0.75; // pitch manoeuvre at T+10 s (KARI)
    public const double StageCoast = 4.0;          // separation to next ignition
    public const double FairingSeparation = 227;   // actual flight 2
    public const double DragCoefficient = 0.32;

    // Published actual times for events the dynamics do not decide (KARI: 875 s first separation, +70 s second).
    public const double SatelliteSeparation = 875, DummySeparation = 945, FlightEnd = 1095;

    // Published actual values used to compare the simulation on screen.
    public static readonly (string Event, double Time, double AltitudeKm)[] Reference =
    [
        ("1단 분리", 123, 62), ("페어링 분리", 227, 202), ("2단 분리", 269, 265), ("3단 엔진 종료", 790, 700),
        ("성능검증위성 분리", 875, 700), ("위성모사체 분리", 945, 700),
    ];
}
