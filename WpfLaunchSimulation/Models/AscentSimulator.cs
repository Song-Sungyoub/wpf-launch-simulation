namespace WpfLaunchSimulation.Models;

public enum FlightPhase { Stage1, Stage1Coast, Stage2, Stage2Coast, Stage3, Orbit }
public sealed record FlightEvent(double Time, string Name, double AltitudeKm, double SpeedKms);

// Separated hardware (spent stages, fairing halves): ballistic with gravity and drag until impact.
public sealed class Debris(string name, double mass, double area, Vec position, Vec velocity, int kind)
{
    public string Name { get; } = name;
    public int Kind { get; } = kind; // 1, 2 = spent stage, 3 = fairing half, 4 = satellite, 5 = mass simulator
    public Vec Position { get; private set; } = position;
    public Vec Velocity { get; private set; } = velocity;
    public bool Landed { get; private set; }
    public double LandedTime { get; private set; } = double.NaN;
    public double Tumble { get; private set; }
    public void Step(double t, double dt)
    {
        if (Landed) return;
        double r = Position.Length, h = r - Earth.Radius;
        var vrel = Velocity - Vec.Cross(new Vec(0, 0, Earth.OmegaRadPerSecond), Position);
        double q = 0.5 * Earth.Density(h) * Vec.Dot(vrel, vrel);
        var a = Position * (-Earth.Mu / (r * r * r)) - vrel.Unit * (q * 1.0 * area / mass);
        Velocity += a * dt; Position += Velocity * dt; Tumble += dt * 0.6;
        if (Position.Length - Earth.Radius <= 0) { Landed = true; LandedTime = t; }
    }
}

// 3-DoF point-mass ascent in the T0 Earth-fixed inertial frame (see Earth). Semi-implicit Euler at a fixed
// 0.05 s step, ported from the tuning prototype so its results carry over. Guidance [근사]: vertical rise,
// pitch kick at T+10 s, gravity turn through stages 1-2, then a stage-3 law that keeps the orbital plane and
// steers the vertical speed so the ballistic apex meets 700 km as horizontal speed reaches circular speed.
public sealed class AscentSimulator
{
    public const double Step = 0.05;
    private const double PlaneTau = 40, VzTau = 30, VerticalFraction = 0.5;
    private readonly Vec padFixed, kickHeading, planeNormal;
    private double coastUntil = double.NaN, maxQ, maxQTime, maxQAlt;
    private bool maxQReported, sonicReported, fairingOn = true, engineOff, payloadSeparated, dummySeparated;

    public double Time { get; private set; }
    public Vec Position { get; private set; }
    public Vec Velocity { get; private set; }
    public Vec ThrustDirection { get; private set; }
    public double Mass { get; private set; }
    public double Propellant { get; private set; }
    public int Stage { get; private set; } = 1;
    public FlightPhase Phase { get; private set; } = FlightPhase.Stage1;
    public double Thrust { get; private set; }
    public double DynamicPressure { get; private set; }
    public double SensedAccelerationG { get; private set; }
    public double MaxAccelerationG { get; private set; }
    public bool FairingAttached => fairingOn;
    public bool PayloadAttached => !payloadSeparated;
    public bool DummyAttached => !dummySeparated;
    public bool EngineBurning => Thrust > 0;
    public List<FlightEvent> Events { get; } = [];
    public List<Debris> Debris { get; } = [];
    public event Action<FlightEvent>? EventRaised;

    public double Altitude => Position.Length - Earth.Radius;
    public Vec RelativeVelocity => Velocity - Vec.Cross(new Vec(0, 0, Earth.OmegaRadPerSecond), Position);
    public Vec FixedPosition => Earth.InertialToFixed(Position, Time);
    public Vec PadFixed => padFixed;
    public double Downrange => Earth.SurfaceDistance(padFixed, FixedPosition);
    public double Mach => RelativeVelocity.Length / Earth.SpeedOfSound(Altitude);
    public double FlightPathAngleDeg => Math.Asin(Math.Clamp(Vec.Dot(Velocity.Unit, Position.Unit), -1, 1)) * 180 / Math.PI;
    public StageSpec CurrentStage => Stage switch { 1 => NuriFlight2.Stage1, 2 => NuriFlight2.Stage2, _ => NuriFlight2.Stage3 };
    public double PropellantFraction => Math.Clamp(Propellant / CurrentStage.PropellantKg, 0, 1);

    public AscentSimulator()
    {
        padFixed = Earth.FixedPosition(NuriFlight2.PadLatitude, NuriFlight2.PadLongitude, NuriFlight2.PadAltitudeM);
        Position = padFixed;
        Velocity = Vec.Cross(new Vec(0, 0, Earth.OmegaRadPerSecond), Position);
        var up = Position.Unit; var east = Vec.Cross(new Vec(0, 0, 1), up).Unit; var north = Vec.Cross(up, east);
        double az = NuriFlight2.LaunchAzimuthDeg * Math.PI / 180, lat = NuriFlight2.PadLatitude * Math.PI / 180;
        kickHeading = north * Math.Cos(az) + east * Math.Sin(az);
        double beta = Math.PI - Math.Asin(Math.Cos(NuriFlight2.TargetInclinationDeg * Math.PI / 180) / Math.Cos(lat));
        planeNormal = Vec.Cross(Position, north * Math.Cos(beta) + east * Math.Sin(beta)).Unit;
        ThrustDirection = up;
        Mass = Total(NuriFlight2.Stage1) + Total(NuriFlight2.Stage2) + Total(NuriFlight2.Stage3) + NuriFlight2.FairingKg + NuriFlight2.PayloadKg;
        Propellant = NuriFlight2.Stage1.PropellantKg;
    }
    private static double Total(StageSpec s) => s.PropellantKg + s.DryKg;

    public void Advance(double until)
    {
        while (Time + Step <= until + 1e-9) StepOnce();
    }

    private void StepOnce()
    {
        double t = Time, h = Altitude; var up = Position.Unit;
        var vrel = RelativeVelocity; double vr = vrel.Length;
        double q = 0.5 * Earth.Density(h) * vr * vr; DynamicPressure = q;
        if (q > maxQ) { maxQ = q; maxQTime = t; maxQAlt = h; }
        else if (!maxQReported && maxQ > 20e3 && q < maxQ * 0.97) { maxQReported = true; Raise(maxQTime, $"Max-Q · {maxQ / 1000:0.0} kPa", maxQAlt); }
        if (!sonicReported && t > 5 && vr > Earth.SpeedOfSound(h)) { sonicReported = true; Raise(t, "음속 돌파", h); }

        bool thrusting = double.IsNaN(coastUntil) && Propellant > 0 && !engineOff;
        double force = 0, mdot = 0;
        if (thrusting)
        {
            var s = CurrentStage;
            force = Stage == 1 ? s.ThrustVacuumN - (s.ThrustVacuumN - s.ThrustSeaLevelN) * Earth.PressureRatio(h) : s.ThrustVacuumN;
            mdot = s.MassFlowKgPerS;
        }
        Thrust = force;
        double accel = force / Mass;
        var d = Attitude(t, h, up, vrel, accel);
        if (t >= NuriFlight2.PitchKickStart)
            d = (d - planeNormal * (Vec.Dot(d, planeNormal) + Vec.Dot(Velocity, planeNormal) / PlaneTau / Math.Max(accel, 1e-3))).Unit;
        ThrustDirection = d;

        double r = Position.Length;
        var gravity = Position * (-Earth.Mu / (r * r * r));
        double area = Math.PI * Math.Pow((Stage == 1 ? NuriFlight2.Stage1.DiameterM : NuriFlight2.Stage2.DiameterM) / 2, 2);
        var drag = vr > 0 ? vrel.Unit * (-q * NuriFlight2.DragCoefficient * area / Mass) : Vec.Zero;
        var a = gravity + drag + d * accel;
        Velocity += a * Step; Position += Velocity * Step;
        SensedAccelerationG = (d * accel + drag).Length / Earth.G0;
        MaxAccelerationG = Math.Max(MaxAccelerationG, accel / Earth.G0);
        Propellant -= mdot * Step; Mass -= mdot * Step;
        Time = t + Step;
        foreach (var piece in Debris) piece.Step(Time, Step);

        if (fairingOn && Time >= NuriFlight2.FairingSeparation)
        {
            fairingOn = false; Mass -= NuriFlight2.FairingKg;
            var side = Vec.Cross(Velocity, Position).Unit;
            Debris.Add(new Debris("페어링", NuriFlight2.FairingKg / 2, 6, Position, Velocity + side * 4, 3));
            Debris.Add(new Debris("페어링", NuriFlight2.FairingKg / 2, 6, Position, Velocity - side * 4, 3));
            Raise(Time, "페어링 분리", Altitude);
        }
        if (!double.IsNaN(coastUntil) && Time >= coastUntil)
        {
            coastUntil = double.NaN;
            Phase = Stage == 2 ? FlightPhase.Stage2 : FlightPhase.Stage3;
            Raise(Time, $"{Stage}단 점화", Altitude);
        }
        if (thrusting && Propellant <= 0) Separate();
        if (Stage == 3 && thrusting && !engineOff && Propellant > 0) CheckCutoff();
        if (!payloadSeparated && Time >= NuriFlight2.SatelliteSeparation)
        {
            payloadSeparated = true;
            Debris.Add(new Debris("성능검증위성", 180, 1, Position, Velocity + Velocity.Unit * 0.5, 4));
            Raise(Time, "성능검증위성 분리", Altitude);
        }
        if (!dummySeparated && Time >= NuriFlight2.DummySeparation)
        {
            dummySeparated = true;
            Debris.Add(new Debris("위성모사체", 1320, 2, Position, Velocity + Velocity.Unit * 0.3, 5));
            Raise(Time, "위성모사체 분리", Altitude);
        }
    }

    private Vec Attitude(double t, double h, Vec up, Vec vrel, double accel)
    {
        if (t < NuriFlight2.PitchKickStart) return up;
        if (t < NuriFlight2.PitchKickEnd)
        {
            double angle = NuriFlight2.PitchKickDeg * Math.PI / 180 * (t - NuriFlight2.PitchKickStart) / (NuriFlight2.PitchKickEnd - NuriFlight2.PitchKickStart);
            return (up * Math.Cos(angle) + kickHeading * Math.Sin(angle)).Unit;
        }
        if (Stage < 3) return h < 60e3 ? vrel.Unit : Velocity.Unit; // gravity turn (relative velocity in the atmosphere)
        if (accel <= 0) return Velocity.Unit;
        double vertical = Vec.Dot(Velocity, up);
        var horizontal = Velocity - up * vertical; double vh = horizontal.Length, r = Position.Length;
        double vcirc = Math.Sqrt(Earth.Mu / (Earth.Radius + NuriFlight2.TargetAltitudeM));
        double g = Earth.Mu / (r * r), centrifugal = vh * vh / r;
        double geff = Math.Max(g - centrifugal, 0.05), dh = NuriFlight2.TargetAltitudeM - h;
        double vzNeed = Math.CopySign(Math.Sqrt(2 * geff * Math.Abs(dh)), dh);
        double av = (vzNeed - vertical) / VzTau;
        if (vh > vcirc * 0.97) av += g - centrifugal;
        av = Math.Clamp(av, -accel * VerticalFraction, accel * VerticalFraction);
        double ah = Math.Sqrt(Math.Max(accel * accel - av * av, 0));
        return (up * av + horizontal.Unit * ah).Unit;
    }

    private void Separate()
    {
        var spent = CurrentStage; double leftover = Math.Max(Propellant, 0);
        Mass -= spent.DryKg + leftover;
        Debris.Add(new Debris(spent.Name, spent.DryKg + leftover, Math.PI * spent.DiameterM * spent.DiameterM / 4 * 1.5,
            Position, Velocity - Velocity.Unit * 1.5, Stage));
        if (Stage < 3)
        {
            Raise(Time, $"{Stage}단 분리", Altitude);
            Stage++; Propellant = CurrentStage.PropellantKg; coastUntil = Time + NuriFlight2.StageCoast;
            Phase = Stage == 2 ? FlightPhase.Stage1Coast : FlightPhase.Stage2Coast;
        }
        else { engineOff = true; Phase = FlightPhase.Orbit; Raise(Time, "3단 연소 종료(추진제 소진)", Altitude); }
    }

    private void CheckCutoff()
    {
        var orbit = Orbit;
        if (orbit.PerigeeKm * 1000 > NuriFlight2.TargetAltitudeM - 15e3 && Altitude > NuriFlight2.TargetAltitudeM - 20e3)
        {
            engineOff = true; Phase = FlightPhase.Orbit; Thrust = 0;
            Raise(Time, "3단 엔진 종료 · 궤도 진입", Altitude);
        }
    }

    public (double PerigeeKm, double ApogeeKm, double InclinationDeg, double PeriodMin) Orbit
    {
        get
        {
            double r = Position.Length, energy = Vec.Dot(Velocity, Velocity) / 2 - Earth.Mu / r;
            var hvec = Vec.Cross(Position, Velocity);
            double inc = Math.Acos(Math.Clamp(hvec.Z / hvec.Length, -1, 1)) * 180 / Math.PI;
            if (energy >= 0) return (double.NaN, double.PositiveInfinity, inc, double.NaN);
            double a = -Earth.Mu / (2 * energy);
            double e = Math.Sqrt(Math.Max(0, 1 - Vec.Dot(hvec, hvec) / (Earth.Mu * a)));
            return ((a * (1 - e) - Earth.Radius) / 1000, (a * (1 + e) - Earth.Radius) / 1000, inc, 2 * Math.PI * Math.Sqrt(a * a * a / Earth.Mu) / 60);
        }
    }

    private void Raise(double time, string name, double altitude)
    {
        var e = new FlightEvent(time, name, altitude / 1000, Velocity.Length / 1000);
        Events.Add(e); EventRaised?.Invoke(e);
    }
}
