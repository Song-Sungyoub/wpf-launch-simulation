using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using WpfLaunchSimulation.Models;
using WpfLaunchSimulation.Rendering;

namespace WpfLaunchSimulation.ViewModels;

public enum CameraMode { Auto, Chase, ControlRoom, Onboard, Pad, Globe }

public sealed class ActionCommand(Action<object?> execute) : ICommand
{
    public ActionCommand(Action execute) : this(_ => execute()) { }
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter);
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    public const double CountdownStart = -600; // PLO starts at T−10 min (YTN 사이언스, 2022-06-21)
    private const double R = Earth.Radius;

    // Scenes: stars (camera at the origin), the far layer (Earth, paths) and the near layer (pad, rocket, debris).
    // Both world layers are drawn relative to a moving origin so metre-scale geometry stays precise.
    public Model3DGroup SkyScene { get; } = new();
    public Model3DGroup FarScene { get; } = new();
    public Model3DGroup NearScene { get; } = new();
    private readonly TranslateTransform3D farOrigin = new(), nearOrigin = new();
    private readonly AxisAngleRotation3D earthAngle = new(new Vector3D(0, 1, 0), 0);
    private readonly Model3DGroup farEarthFixed = new(), farInertial = new(), groundTrackHost = new(), landingHost = new();
    private readonly GeometryModel3D flightPath = new() { Material = SceneGeometry.MarkerMaterial(Color.FromRgb(255, 200, 80)) };
    private readonly GeometryModel3D orbitPath = new() { Material = SceneGeometry.MarkerMaterial(Color.FromRgb(90, 220, 255)) };
    private readonly GeometryModel3D groundTrack = new() { Material = SceneGeometry.MarkerMaterial(Color.FromRgb(120, 255, 150)) };
    private readonly ScaleTransform3D markerScale = new(); private readonly TranslateTransform3D markerPosition = new();
    private readonly NuriModel rocket = new();
    private readonly MatrixTransform3D rocketPose = new();
    private readonly Dictionary<Debris, (Model3DGroup Model, MatrixTransform3D Pose)> debrisModels = [];
    private readonly HashSet<Debris> reportedLandings = [];
    private readonly List<Point3D> pathPoints = [], trackPoints = [];
    private double lastSample = double.NegativeInfinity, lastRibbon = double.NegativeInfinity, ribbonWidth;

    private AscentSimulator sim = new();
    private double missionTime = CountdownStart, speed = 1;
    private bool playing;

    public MainViewModel()
    {
        SkyScene.Children.Add(StarField.Create());
        var sun = SunDirection();
        foreach (var scene in new[] { FarScene, NearScene })
        {
            scene.Children.Add(new AmbientLight(Color.FromRgb(46, 50, 60)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(255, 250, 240), -sun));
        }
        // Far layer.
        var earthImage = new BitmapImage();
        earthImage.BeginInit(); earthImage.UriSource = new Uri("pack://application:,,,/WpfLaunchSimulation;component/Assets/earth.jpg"); earthImage.EndInit(); earthImage.Freeze();
        var earthMaterial = new DiffuseMaterial(new ImageBrush(earthImage)); earthMaterial.Freeze();
        farEarthFixed.Transform = new RotateTransform3D(earthAngle);
        farEarthFixed.Children.Add(SceneGeometry.Sphere(1, earthMaterial, 160, 80));
        farEarthFixed.Children.Add(SceneGeometry.GroundPatch(NuriFlight2.PadLatitude, NuriFlight2.PadLongitude, 80, earthMaterial));
        farEarthFixed.Children.Add(SceneGeometry.Clouds());
        groundTrack.BackMaterial = groundTrack.Material; groundTrackHost.Children.Add(groundTrack);
        farEarthFixed.Children.Add(groundTrackHost); farEarthFixed.Children.Add(landingHost);
        flightPath.BackMaterial = flightPath.Material; orbitPath.BackMaterial = orbitPath.Material;
        farInertial.Children.Add(flightPath); farInertial.Children.Add(orbitPath);
        var marker = SceneGeometry.Sphere(1, SceneGeometry.MarkerMaterial(Color.FromRgb(255, 240, 200)), 16, 8);
        var markerTransform = new Transform3DGroup(); markerTransform.Children.Add(markerScale); markerTransform.Children.Add(markerPosition);
        marker.Transform = markerTransform; farInertial.Children.Add(marker);
        var farRoot = new Model3DGroup { Transform = farOrigin };
        farRoot.Children.Add(farEarthFixed); farRoot.Children.Add(SceneGeometry.Atmosphere()); farRoot.Children.Add(farInertial);
        FarScene.Children.Add(farRoot);
        // Near layer: the pad rides on the Earth rotation; the rocket and debris are inertial.
        var pad = new Model3DGroup { Transform = new MatrixTransform3D(LocalFrame(Earth.FixedPosition(NuriFlight2.PadLatitude, NuriFlight2.PadLongitude, NuriFlight2.PadAltitudeM))) };
        pad.Children.Add(PadModel.Create());
        var nearEarthFixed = new Model3DGroup { Transform = new RotateTransform3D(earthAngle) };
        nearEarthFixed.Children.Add(pad);
        rocket.Root.Transform = rocketPose;
        var nearRoot = new Model3DGroup { Transform = nearOrigin };
        nearRoot.Children.Add(nearEarthFixed); nearRoot.Children.Add(rocket.Root);
        NearScene.Children.Add(nearRoot);
        nearDebris = new Model3DGroup(); nearRoot.Children.Add(nearDebris);

        StartCommand = new ActionCommand(() => { playing = !playing; Notify(nameof(PlayText)); });
        SkipCountdownCommand = new ActionCommand(() => { if (missionTime < -15) missionTime = -15; });
        ResetCommand = new ActionCommand(Reset);
        SpeedCommand = new ActionCommand(p => { if (double.TryParse(p as string, out var v)) { speed = v; Notify(nameof(SpeedText)); } });
        CameraCommand = new ActionCommand(p => { if (Enum.TryParse<CameraMode>(p as string, out var m)) { cameraMode = m; Notify(nameof(CameraText)); } });
        sim.EventRaised += OnFlightEvent;
        Update(0);
    }
    private readonly Model3DGroup nearDebris;

    public ICommand StartCommand { get; }
    public ICommand SkipCountdownCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand SpeedCommand { get; }
    public ICommand CameraCommand { get; }
    public string PlayText => playing ? "일시정지" : missionTime <= CountdownStart ? "카운트다운 시작" : "계속";
    public string SpeedText => $"{speed:0}×";

    private void Reset()
    {
        sim.EventRaised -= OnFlightEvent;
        sim = new AscentSimulator(); sim.EventRaised += OnFlightEvent;
        missionTime = CountdownStart; playing = false; speed = 1;
        rocket.Root.Children.Clear();
        foreach (var part in new Model3D[] { rocket.Stage1, rocket.Stage2, rocket.Stage3, rocket.Payload, rocket.FairingLeft, rocket.FairingRight }) rocket.Root.Children.Add(part);
        nearDebris.Children.Clear(); debrisModels.Clear(); reportedLandings.Clear(); landingHost.Children.Clear();
        pathPoints.Clear(); trackPoints.Clear(); lastSample = double.NegativeInfinity; lastRibbon = double.NegativeInfinity;
        flightPath.Geometry = null; orbitPath.Geometry = null; groundTrack.Geometry = null;
        AltitudeChart = []; SpeedChart = []; MapTrack = []; Log.Clear(); countdownLogged.Clear();
        Notify(nameof(PlayText)); Notify(nameof(SpeedText)); Notify(nameof(AltitudeChart)); Notify(nameof(SpeedChart)); Notify(nameof(MapTrack));
        Update(0);
    }

    // ---- Frame update -------------------------------------------------------------------------------------
    private double lastWall = double.NaN;
    public void Tick(double wallSeconds)
    {
        double dt = double.IsNaN(lastWall) ? 0 : Math.Clamp(wallSeconds - lastWall, 0, 0.1);
        lastWall = wallSeconds;
        if (playing && missionTime < NuriFlight2.FlightEnd + 600) missionTime += dt * speed;
        if (missionTime > 0) sim.Advance(missionTime);
        Update(wallSeconds);
    }

    private readonly HashSet<double> countdownLogged = [];
    private void Update(double wall)
    {
        double t = missionTime;
        foreach (var (time, text) in new[] { (-600.0, "발사자동운용(PLO) 시작 · 추진제 충전과 기립장치 철수는 완료된 상태 · 이후 이상 감지 시 자동 중단"), (-4.0, "1단 엔진 4기 점화"), (0.0, "이륙 · 지상고정장치 해제") })
            if (t >= time && countdownLogged.Add(time)) AddLog(Clock(time), text);
        earthAngle.Angle = Earth.OmegaRadPerSecond * t * 180 / Math.PI;
        UpdateRocket(t);
        UpdateDebris();
        if (t > 0 && t - lastSample >= 2) SampleTrack(t);
        UpdateCamera(t, wall);
        UpdateTelemetry(t);
    }

    private void UpdateRocket(double t)
    {
        Vec position, axis;
        if (t <= 0)
        {
            position = Earth.FixedToInertial(sim.PadFixed, t); axis = position.Unit;
        }
        else
        {
            position = sim.Position;
            axis = sim.EngineBurning || sim.Time < NuriFlight2.PitchKickStart ? sim.ThrustDirection : sim.Velocity.Unit;
        }
        rocketBase = position; rocketAxis = axis;
        rocketPose.Matrix = Pose(position, axis, 1.0);
        SetPart(rocket.Stage1, sim.Stage == 1);
        SetPart(rocket.Stage2, sim.Stage <= 2);
        SetPart(rocket.FairingLeft, sim.FairingAttached); SetPart(rocket.FairingRight, sim.FairingAttached);
        SetPart(rocket.Payload, sim.PayloadAttached);
        // Plumes: ignition from T−4 s, then the burning stage; length grows as ambient pressure falls.
        int burning = t > -4 && t <= 0 ? 0 : sim.EngineBurning ? sim.Stage - 1 : -1;
        for (int i = 0; i < 3; i++)
        {
            SetPart(rocket.PlumeModels[i], i == burning);
            if (i != burning) continue;
            double h = t > 0 ? sim.Altitude : 0, expand = 1 + Math.Min(h / 40e3, 2.5);
            double length = i switch { 0 => t <= 0 ? 6 + (t + 4) * 4 : 22 + h / 2500, 1 => 26, _ => 12 } * (0.92 + 0.08 * Math.Sin(t * 37));
            rocket.Plumes[i].ScaleX = rocket.Plumes[i].ScaleZ = expand; rocket.Plumes[i].ScaleY = Math.Min(length, 90);
        }
    }
    private Vec rocketBase, rocketAxis;
    private Vec RocketCenter => rocketBase + rocketAxis * (sim.Stage == 1 ? 22 : sim.Stage == 2 ? 34 : 42);
    private void SetPart(Model3D part, bool visible)
    {
        bool present = rocket.Root.Children.Contains(part);
        if (visible && !present) rocket.Root.Children.Add(part); else if (!visible && present) rocket.Root.Children.Remove(part);
    }

    private void UpdateDebris()
    {
        foreach (var piece in sim.Debris)
        {
            if (!debrisModels.TryGetValue(piece, out var entry))
            {
                var model = piece.Kind switch { 3 => NuriModel.FairingHalf(debrisModels.Keys.Count(k => k.Kind == 3) == 0), 4 or 5 => NuriModel.SpentStage(4), _ => NuriModel.SpentStage(piece.Kind) };
                var pose = new MatrixTransform3D(); model.Transform = pose;
                entry = (model, pose); debrisModels[piece] = entry; nearDebris.Children.Add(model);
            }
            // Tumble about an axis across the body; parts start aligned with the rocket.
            var axis = (rocketAxis * Math.Cos(piece.Tumble) + Vec.Cross(rocketAxis, piece.Position.Unit).Unit * Math.Sin(piece.Tumble)).Unit;
            entry.Pose.Matrix = Pose(piece.Position, piece.Kind <= 3 ? axis : rocketAxis, 1);
            if (piece.Landed && reportedLandings.Add(piece) && piece.Kind <= 3)
            {
                double km = Earth.SurfaceDistance(sim.PadFixed, Earth.InertialToFixed(piece.Position, piece.LandedTime)) / 1000;
                string plan = piece.Kind switch { 1 => "계획 약 413 km", 2 => "계획 약 2,800 km", _ => "계획 약 1,514 km" };
                AddLog(Clock(piece.LandedTime), $"{piece.Name} 해상 낙하 · 발사장에서 {km:N0} km ({plan})");
                var fixedPoint = SceneGeometry.Point(Earth.InertialToFixed(piece.Position, piece.LandedTime).Unit * (R + 3000));
                var dot = SceneGeometry.Sphere(0.0012, SceneGeometry.MarkerMaterial(Color.FromRgb(255, 120, 90)), 12, 6);
                dot.Transform = new TranslateTransform3D((Vector3D)fixedPoint); landingHost.Children.Add(dot);
            }
        }
    }

    // Model pose: metres → scene units, +Y along the axis, base at the given inertial position.
    private static Matrix3D Pose(Vec position, Vec axis, double scale)
    {
        var y = SceneGeometry.Direction(axis);
        var x = Vector3D.CrossProduct(y, new Vector3D(0, 1, 0));
        if (x.LengthSquared < 1e-8) x = Vector3D.CrossProduct(y, new Vector3D(1, 0, 0));
        x.Normalize(); var z = Vector3D.CrossProduct(x, y);
        double s = scale / R; var p = SceneGeometry.Point(position);
        return new Matrix3D(x.X * s, x.Y * s, x.Z * s, 0, y.X * s, y.Y * s, y.Z * s, 0, z.X * s, z.Y * s, z.Z * s, 0, p.X, p.Y, p.Z, 1);
    }
    // Local east/up/south frame at an Earth-fixed point (metres → scene units).
    private static Matrix3D LocalFrame(Vec fixedPosition)
    {
        var up = SceneGeometry.Direction(fixedPosition);
        var east = Vector3D.CrossProduct(new Vector3D(0, 1, 0), up); east.Normalize();
        var south = Vector3D.CrossProduct(east, up); south.Normalize();
        double s = 1 / R; var p = SceneGeometry.Point(fixedPosition);
        return new Matrix3D(east.X * s, east.Y * s, east.Z * s, 0, up.X * s, up.Y * s, up.Z * s, 0, south.X * s, south.Y * s, south.Z * s, 0, p.X, p.Y, p.Z, 1);
    }

    private static Vector3D SunDirection()
    {
        // 2022-06-21 07:00 UTC: solstice, subsolar point ≈ 23.44°N, 74.6°E (equation of time ≈ −1.6 min).
        var d = Earth.FixedPosition(23.44, 74.6, 0).Unit; return SceneGeometry.Direction(d);
    }

    private void SampleTrack(double t)
    {
        lastSample = t;
        pathPoints.Add(SceneGeometry.Point(sim.Position));
        var fixedPos = sim.FixedPosition; var geo = Earth.Geodetic(fixedPos);
        trackPoints.Add(SceneGeometry.Point(fixedPos.Unit * (R + 2500)));
        AltitudeChart.Add(new Point(t / 1100 * ChartWidth, ChartHeight - Math.Min(sim.Altitude / 800e3, 1) * ChartHeight));
        SpeedChart.Add(new Point(t / 1100 * ChartWidth, ChartHeight - Math.Min(sim.Velocity.Length / 8000, 1) * ChartHeight));
        MapTrack.Add(MapPoint(geo.Latitude, geo.Longitude));
    }

    // ---- Camera -------------------------------------------------------------------------------------------
    private CameraMode cameraMode = CameraMode.Auto, activeMode = CameraMode.Pad;
    private double orbitYaw = 0.6, orbitPitch = 0.15, chaseDistance = 120;
    public Point3D CameraPosition { get; private set; }
    public Vector3D LookDirection { get; private set; } = new(0, 0, -1);
    public Vector3D UpDirection { get; private set; } = new(0, 1, 0);
    public double FieldOfView { get; private set; } = 45;
    public double FarNearPlane { get; private set; } = 1e-4;
    public double FarFarPlane { get; private set; } = 10;
    public double NearNearPlane { get; private set; } = 1e-8;
    public double NearFarPlane { get; private set; } = 0.5;
    public bool IsControlRoom => activeMode == CameraMode.ControlRoom;
    public Visibility ControlRoomVisibility => IsControlRoom ? Visibility.Visible : Visibility.Collapsed;
    public string CameraText => (cameraMode == CameraMode.Auto ? "자동 연출 · " : "") + ModeName(activeMode);
    private static string ModeName(CameraMode m) => m switch
    {
        CameraMode.Chase => "3인칭 추적", CameraMode.ControlRoom => "관제소", CameraMode.Onboard => "탑재 카메라",
        CameraMode.Pad => "발사대 지상 카메라", CameraMode.Globe => "지구 전체", _ => "자동"
    };

    public void Rotate(double dx, double dy) { orbitYaw += dx * 0.008; orbitPitch = Math.Clamp(orbitPitch + dy * 0.006, -1.2, 1.4); }
    public void Zoom(double delta) => chaseDistance = Math.Clamp(chaseDistance * Math.Exp(-delta * 0.0012), 40, 4000);

    private CameraMode Director(double t)
    {
        if (cameraMode != CameraMode.Auto) return cameraMode;
        // Broadcast-style sequence around the published event times.
        if (t < -20) return CameraMode.ControlRoom;
        if (t < 15) return CameraMode.Pad;
        if (t < 115) return CameraMode.Chase;
        if (t < 135) return CameraMode.Onboard;   // stage-1 separation ≈ 123 s
        if (t < 215) return CameraMode.Chase;
        if (t < 240) return CameraMode.Onboard;   // fairing ≈ 227 s
        if (t < 262) return CameraMode.Chase;
        if (t < 286) return CameraMode.Onboard;   // stage-2 separation ≈ 270 s
        if (t < 420) return CameraMode.Chase;
        if (t < 770) return CameraMode.ControlRoom;
        if (t < 990) return CameraMode.Chase;      // cutoff ≈ 791 s, separations 875 / 945 s
        return CameraMode.Globe;
    }

    private void UpdateCamera(double t, double wall)
    {
        var mode = Director(t);
        if (mode != activeMode) { activeMode = mode; Notify(nameof(CameraText)); Notify(nameof(IsControlRoom)); Notify(nameof(ControlRoomVisibility)); }
        var view = mode == CameraMode.ControlRoom ? CameraMode.Chase : mode;
        var center = SceneGeometry.Point(RocketCenter);
        var up = SceneGeometry.Direction(RocketCenter);
        Point3D origin = center, eye; Vector3D look; double fov = 45, nearMeters;
        switch (view)
        {
            case CameraMode.Pad:
            {
                var padFixed = sim.PadFixed; var u = padFixed.Unit; var east = Vec.Cross(new Vec(0, 0, 1), u).Unit; var north = Vec.Cross(u, east);
                var camFixed = padFixed + east * 260 + north * 330 + u * 18;
                var camPos = SceneGeometry.Point(Earth.FixedToInertial(camFixed, t));
                eye = camPos; look = center - camPos; double dist = look.Length * R;
                fov = Math.Clamp(2 * Math.Atan(45 / dist) * 180 / Math.PI, 0.6, 32);
                origin = camPos; nearMeters = 2;
                break;
            }
            case CameraMode.Onboard:
            {
                var y = SceneGeometry.Direction(rocketAxis);
                var x = Vector3D.CrossProduct(y, new Vector3D(0, 1, 0)); if (x.LengthSquared < 1e-8) x = new Vector3D(1, 0, 0); x.Normalize();
                double top = sim.Stage == 1 ? NuriModel.Stage1Top : sim.Stage == 2 ? NuriModel.Stage2Top : NuriModel.Stage3Top;
                var basePoint = SceneGeometry.Point(rocketBase);
                double radius = sim.Stage == 1 ? NuriModel.Stage1Radius : NuriModel.UpperRadius;
                eye = basePoint + y * ((top - 1.0) / R) + x * ((radius + 1.6) / R);
                look = -y + x * 0.28; fov = 62; origin = eye; nearMeters = 0.2;
                break;
            }
            case CameraMode.Globe:
            {
                var p = (Vector3D)center; p.Normalize();
                var side = Vector3D.CrossProduct(p, new Vector3D(0, 1, 0)); if (side.LengthSquared < 1e-6) side = new Vector3D(1, 0, 0); side.Normalize();
                var dir = p * Math.Cos(orbitPitch + 0.3) + side * Math.Sin(orbitYaw);
                dir.Normalize(); eye = (Point3D)(dir * 3.4); look = -(Vector3D)eye; origin = new Point3D(); fov = 40; nearMeters = 20000;
                break;
            }
            default:
            {
                // 3rd-person: behind and above the rocket, user-adjustable with drag/wheel.
                var axis = SceneGeometry.Direction(rocketAxis);
                var behind = t > 0 ? -axis : -up;
                var side = Vector3D.CrossProduct(behind, up); if (side.LengthSquared < 1e-8) side = new Vector3D(1, 0, 0); side.Normalize();
                double dist = (sim.Stage == 3 ? chaseDistance * 1.6 : chaseDistance) / R;
                var offset = behind * Math.Cos(orbitYaw) + side * Math.Sin(orbitYaw);
                offset = offset * Math.Cos(orbitPitch) + up * Math.Sin(orbitPitch); offset.Normalize();
                if (t <= 0) offset = side * Math.Cos(orbitYaw) + up * 0.15 + (Vector3D)SceneGeometry.Direction(Vec.Cross(new Vec(0, 0, 1), rocketBase)) * Math.Sin(orbitYaw);
                offset.Normalize();
                eye = center + offset * dist; look = center - eye; fov = 40; nearMeters = 1;
                break;
            }
        }
        // Positions relative to the moving origin; double precision on the CPU keeps metre-scale detail.
        farOrigin.OffsetX = nearOrigin.OffsetX = -origin.X; farOrigin.OffsetY = nearOrigin.OffsetY = -origin.Y; farOrigin.OffsetZ = nearOrigin.OffsetZ = -origin.Z;
        CameraPosition = new Point3D(eye.X - origin.X, eye.Y - origin.Y, eye.Z - origin.Z);
        look.Normalize(); LookDirection = look;
        UpDirection = view == CameraMode.Globe ? new Vector3D(0, 1, 0) : up;
        FieldOfView = fov;
        double eyeAltitude = Math.Max(((Vector3D)eye).Length * R - R, 1);
        FarNearPlane = Math.Max(Math.Min(eyeAltitude * 0.5, ((Vector3D)(eye - center)).Length * R * 0.05), Math.Max(nearMeters, 2)) / R;
        FarFarPlane = ((Vector3D)eye).Length + 4;
        NearNearPlane = nearMeters / R; NearFarPlane = 3000e3 / R;
        // Ribbon and marker sizes follow the camera distance.
        double viewDist = ((Vector3D)(eye - center)).Length;
        markerScale.ScaleX = markerScale.ScaleY = markerScale.ScaleZ = Math.Max(viewDist * 0.006, 2e-7);
        markerPosition.OffsetX = center.X; markerPosition.OffsetY = center.Y; markerPosition.OffsetZ = center.Z;
        double width = Math.Clamp(viewDist * 0.0012, 3 / R, 0.004);
        if (wall - lastRibbon > 0.5 || Math.Abs(width - ribbonWidth) > ribbonWidth * 0.5)
        {
            lastRibbon = wall; ribbonWidth = width;
            if (pathPoints.Count > 1) flightPath.Geometry = SceneGeometry.Ribbon(pathPoints, width);
            if (trackPoints.Count > 1) groundTrack.Geometry = SceneGeometry.Ribbon(trackPoints, width);
            if (sim.Phase == FlightPhase.Orbit) orbitPath.Geometry = SceneGeometry.Ribbon(PredictOrbit(), width * 0.8);
        }
        UpdateSky(eye);
        foreach (var p in new[] { nameof(CameraPosition), nameof(LookDirection), nameof(UpDirection), nameof(FieldOfView), nameof(FarNearPlane), nameof(FarFarPlane), nameof(NearNearPlane), nameof(NearFarPlane) }) Notify(p);
    }

    // Daylight sky: blue near the ground when the Sun is up at the camera, fading to black space by ~80 km.
    public Brush SkyBrush { get; private set; } = Brushes.Black;
    public double StarOpacity { get; private set; } = 1;
    private void UpdateSky(Point3D eye)
    {
        var up = (Vector3D)eye; double altitude = up.Length * R - R; up.Normalize();
        double sunElevation = Vector3D.DotProduct(up, SunDirection());
        double day = Math.Clamp((sunElevation + 0.05) / 0.25, 0, 1) * Math.Clamp(1 - altitude / 80e3, 0, 1);
        byte Mix(byte a, byte b) => (byte)(a + (b - a) * day);
        var brush = new SolidColorBrush(Color.FromRgb(Mix(5, 104), Mix(8, 162), Mix(15, 214))); brush.Freeze();
        SkyBrush = brush; StarOpacity = 1 - day;
        Notify(nameof(SkyBrush)); Notify(nameof(StarOpacity));
    }

    private List<Point3D> PredictOrbit()
    {
        var points = new List<Point3D>(); var r = sim.Position; var v = sim.Velocity;
        double period = sim.Orbit.PeriodMin * 60; if (!double.IsFinite(period)) return points;
        double dt = period / 240;
        for (int i = 0; i <= 240; i++)
        {
            points.Add(SceneGeometry.Point(r));
            for (int k = 0; k < 10; k++) { double rl = r.Length; v += r * (-Earth.Mu / (rl * rl * rl)) * (dt / 10); r += v * (dt / 10); }
        }
        return points;
    }

    // ---- Telemetry ----------------------------------------------------------------------------------------
    public string MissionClock => Clock(missionTime);
    public string CountdownText => missionTime < 0 && missionTime > -11 ? $"{Math.Ceiling(-missionTime):0}" : "";
    public string StageText { get; private set; } = "";
    public string AltitudeText { get; private set; } = "";
    public string SpeedTextValue { get; private set; } = "";
    public string AccelerationText { get; private set; } = "";
    public string PressureText { get; private set; } = "";
    public string DownrangeText { get; private set; } = "";
    public string PropellantText { get; private set; } = "";
    public string OrbitText { get; private set; } = "";
    public string LatestEvent { get; private set; } = "";
    public string StationText { get; private set; } = "";
    public Point RocketMapPoint { get; private set; }
    public ObservableCollection<string> Log { get; } = [];
    public PointCollection AltitudeChart { get; private set; } = [];
    public PointCollection SpeedChart { get; private set; } = [];
    public PointCollection MapTrack { get; private set; } = [];
    public const double ChartWidth = 300, ChartHeight = 110, MapWidth = 300, MapHeight = 390;
    public static Point MapPoint(double latitude, double longitude) => new((longitude - 100) / 50 * MapWidth, (45 - latitude) / 65 * MapHeight);
    public Point NaroMapPoint => MapPoint(NuriFlight2.PadLatitude, NuriFlight2.PadLongitude);
    public Point JejuMapPoint => MapPoint(Stations[1].Lat, Stations[1].Lon);
    public Point PalauMapPoint => MapPoint(Stations[2].Lat, Stations[2].Lon);
    // Station positions: Naro (pad), Jeju and Palau are approximate [근사] — exact coordinates are not public.
    private static readonly (string Name, double Lat, double Lon)[] Stations = [("나로", 34.43, 127.53), ("제주", 33.30, 126.60), ("팔라우", 7.34, 134.48)];

    private double lastTelemetry = double.NegativeInfinity;
    private void UpdateTelemetry(double t)
    {
        Notify(nameof(MissionClock)); Notify(nameof(CountdownText)); Notify(nameof(PlayText));
        if (Math.Abs(t - lastTelemetry) < 0.1 && t > 0) return;
        lastTelemetry = t;
        bool flying = t > 0;
        StageText = !flying ? (t > -4 ? "점화 · 추력 상승" : "발사 대기") : sim.Phase switch
        {
            FlightPhase.Stage1 => "1단 연소", FlightPhase.Stage1Coast => "1·2단 분리 후 관성 비행", FlightPhase.Stage2 => "2단 연소",
            FlightPhase.Stage2Coast => "2·3단 분리 후 관성 비행", FlightPhase.Stage3 => "3단 연소", _ => "궤도 비행"
        };
        AltitudeText = flying ? $"{sim.Altitude / 1000:0.0} km" : "0.0 km";
        SpeedTextValue = flying ? $"{sim.Velocity.Length / 1000:0.000} km/s (대지 {sim.RelativeVelocity.Length / 1000:0.000}) · M{sim.Mach:0.0}" : "0.000 km/s";
        AccelerationText = flying ? $"{sim.SensedAccelerationG:0.00} G (최대 {sim.MaxAccelerationG:0.00})" : "1.00 G";
        PressureText = flying ? $"{sim.DynamicPressure / 1000:0.0} kPa" : "0.0 kPa";
        DownrangeText = flying ? $"{sim.Downrange / 1000:N0} km · 비행경로각 {sim.FlightPathAngleDeg:0.0}°" : "0 km";
        PropellantText = sim.Phase == FlightPhase.Orbit ? "-" : $"{sim.CurrentStage.Name} {sim.PropellantFraction * 100:0}%";
        var orbit = sim.Orbit;
        OrbitText = flying && sim.Altitude > 100e3
            ? (double.IsFinite(orbit.PerigeeKm) ? $"근지점 {orbit.PerigeeKm:N0} km · 원지점 {orbit.ApogeeKm:N0} km · 경사각 {orbit.InclinationDeg:0.00}°" : "탈출 궤도")
            : "목표: 700 km 태양동기궤도 · 경사각 98.2°";
        var fixedPos = flying ? sim.FixedPosition : sim.PadFixed; var geo = Earth.Geodetic(fixedPos);
        RocketMapPoint = MapPoint(geo.Latitude, geo.Longitude);
        StationText = string.Join("   ", Stations.Select(s =>
        {
            double el = Earth.Elevation(Earth.FixedPosition(s.Lat, s.Lon, 0), fixedPos + fixedPos.Unit * 50);
            return $"{s.Name} {(el >= 2 ? "수신" : "—")} {el:0}°";
        }));
        foreach (var p in new[] { nameof(StageText), nameof(AltitudeText), nameof(SpeedTextValue), nameof(AccelerationText), nameof(PressureText), nameof(DownrangeText), nameof(PropellantText), nameof(OrbitText), nameof(RocketMapPoint), nameof(StationText) }) Notify(p);
    }

    private void OnFlightEvent(FlightEvent e)
    {
        var reference = NuriFlight2.Reference.FirstOrDefault(r => e.Name.StartsWith(r.Event));
        string compare = reference.Event is null ? "" : $" (실제 {reference.Time:0} s / {reference.AltitudeKm:0} km)";
        AddLog(Clock(e.Time), $"{e.Name} · {e.AltitudeKm:0.0} km, {e.SpeedKms:0.00} km/s{compare}");
        LatestEvent = e.Name; Notify(nameof(LatestEvent));
    }

    private void AddLog(string clock, string text) => Log.Insert(0, $"{clock}  {text}");
    public static string Clock(double t)
    {
        var span = TimeSpan.FromSeconds(Math.Abs(t));
        return $"T{(t < 0 ? "−" : "+")}{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
