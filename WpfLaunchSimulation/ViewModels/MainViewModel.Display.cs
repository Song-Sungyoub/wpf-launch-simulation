using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using WpfLaunchSimulation.Models;

namespace WpfLaunchSimulation.ViewModels;

public sealed partial class MainViewModel
{
    public string PerformanceText { get; private set; } = "UI 프레임 측정 대기";
    public void SetPerformance(double fps, double maxGapMs, double updateMs)
    {
        PerformanceText = $"UI {fps:0} FPS · 최대 간격 {maxGapMs:0.0} ms · 갱신 {updateMs:0.00} ms";
        Notify(nameof(PerformanceText));
    }
    public Visibility SceneVisibility => IsControlRoom ? Visibility.Collapsed : Visibility.Visible;
    public string ControlRoomTitle => $"나로우주센터 발사통제 · {Clock(missionTime)}";
    private bool postProcessingOn = true;
    private double bloomStrength = 0.35;
    public bool PostProcessingOn { get => postProcessingOn; set { postProcessingOn = value; Notify(); } }
    public double BloomStrength { get => bloomStrength; set { bloomStrength = Math.Clamp(value, 0, 1); Notify(); } }

    private readonly List<(double Time, double Altitude, double Speed, double Lat, double Lon)> chartSamples = [];
    private double chartEnd = 1100;
    private bool worldMap;
    public double ChartCursorX => Math.Clamp(missionTime / chartEnd, 0, 1) * ChartWidth;
    public string ChartRangeText => $"0 — {chartEnd:N0} s · 현재 {Math.Max(0, missionTime):N0} s";
    public string TrackTargetText => Satellite is null ? "발사체 · 지상궤적" : "성능검증위성 · 지상궤적";
    public string MapExtentText => worldMap ? "세계 지도 · 날짜변경선에서 경로 분리" : "경도 100°~150°E · 위도 20°S~45°N";
    public Rect MapViewbox => worldMap ? new Rect(0, 0, 1, 1) : new Rect(100.0 / 360 + 0.5, 0.25, 50.0 / 360, 65.0 / 180);
    public double MapDisplayHeight => worldMap ? 150 : MapHeight;
    public ObservableCollection<PointCollection> MapTrackSegments { get; } = [];
    private Point ProjectMap(double latitude, double longitude) => worldMap
        ? new Point((longitude + 180) / 360 * MapWidth, (90 - latitude) / 180 * MapDisplayHeight)
        : MapPoint(latitude, longitude);

    private void AppendMapPoint(double latitude, double longitude)
    {
        var point = ProjectMap(latitude, longitude);
        if (MapTrackSegments.Count == 0 || (worldMap && MapTrackSegments[^1].Count > 0 &&
            Math.Abs(MapTrackSegments[^1][^1].X - point.X) > MapWidth / 2)) MapTrackSegments.Add([]);
        MapTrackSegments[^1].Add(point);
    }

    private void RecordDisplaySample(double t)
    {
        var fixedPosition = Earth.InertialToFixed(FocusPosition, sim.Time);
        var geo = Earth.Geodetic(fixedPosition);
        chartSamples.Add((t, geo.AltitudeM, FocusVelocity.Length, geo.Latitude, geo.Longitude));
        bool rescale = t > chartEnd;
        if (rescale) chartEnd = Math.Ceiling(t / 600) * 600;
        bool changeMap = worldMap != (Satellite is not null);
        worldMap = Satellite is not null;
        if (rescale)
        {
            AltitudeChart.Clear(); SpeedChart.Clear();
            foreach (var sample in chartSamples) AppendChartPoint(sample.Time, sample.Altitude, sample.Speed);
        }
        else AppendChartPoint(t, geo.AltitudeM, FocusVelocity.Length);
        if (changeMap)
        {
            MapTrackSegments.Clear();
            foreach (var sample in chartSamples) AppendMapPoint(sample.Lat, sample.Lon);
            foreach (var name in new[] { nameof(MapViewbox), nameof(MapExtentText), nameof(MapDisplayHeight),
                nameof(NaroMapPoint), nameof(JejuMapPoint), nameof(PalauMapPoint) }) Notify(name);
        }
        else AppendMapPoint(geo.Latitude, geo.Longitude);
        Notify(nameof(TrackTargetText));
    }

    private void AppendChartPoint(double t, double altitude, double velocity)
    {
        AltitudeChart.Add(new Point(t / chartEnd * ChartWidth, ChartHeight * (1 - Math.Clamp(altitude / 800e3, 0, 1))));
        SpeedChart.Add(new Point(t / chartEnd * ChartWidth, ChartHeight * (1 - Math.Clamp(velocity / 8000, 0, 1))));
    }

    private void ResetDisplayHistory()
    {
        chartSamples.Clear(); MapTrackSegments.Clear(); chartEnd = 1100; worldMap = false;
        foreach (var name in new[] { nameof(MapViewbox), nameof(MapExtentText), nameof(MapDisplayHeight),
            nameof(NaroMapPoint), nameof(JejuMapPoint), nameof(PalauMapPoint), nameof(ChartRangeText), nameof(ChartCursorX), nameof(TrackTargetText) }) Notify(name);
    }

    // Sample on absolute two-second boundaries even when the user jumps or runs at high speed.
    // Stop at a scheduled burn before continuing the rest of the frame.
    private void AdvanceMission(double until)
    {
        int previousSampleCount = chartSamples.Count;
        while (sim.Time + AscentSimulator.Step <= until + 1e-9)
        {
            double sampleAt = (Math.Floor((sim.Time + 1e-7) / 2) + 1) * 2;
            double stop = Math.Min(until, sampleAt);
            if (pendingBurn is double burnAt) stop = Math.Min(stop, Math.Max(sim.Time, burnAt));
            double before = sim.Time;
            sim.Advance(stop);
            if (pendingBurn is double scheduled && sim.Time >= scheduled - 1e-7)
            {
                double displayTime = missionTime;
                missionTime = sim.Time;
                try { ExecuteAutoBurn(); } finally { missionTime = displayTime; }
            }
            if (sim.Time >= sampleAt - 1e-7) SampleTrack(sim.Time);
            if (sim.Time <= before) break;
        }
        if (chartSamples.Count != previousSampleCount)
        {
            // Publish new Freezable instances once per update so deferred WPF views see the complete history.
            AltitudeChart = new PointCollection(AltitudeChart);
            SpeedChart = new PointCollection(SpeedChart);
            Notify(nameof(AltitudeChart)); Notify(nameof(SpeedChart));
        }
    }
}
