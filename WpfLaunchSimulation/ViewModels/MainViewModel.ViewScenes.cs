using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using WpfLaunchSimulation.Models;
using WpfLaunchSimulation.Rendering;

namespace WpfLaunchSimulation.ViewModels;

public sealed partial class MainViewModel
{
    public Model3DGroup InsetDomeScene { get; } = new();
    public double InsetStarOpacity { get; private set; } = 1;
    private readonly SkyDome insetSkyDome = new(SunDirection());
    private readonly EarthEffects.LimbGlow insetLimbGlow = new();
    private readonly SceneGeometry.AtmosphereShells insetAtmosphere = new();
    public Model3DGroup InsetNearScene { get; } = new();
    public Model3DGroup InsetFarScene { get; } = new();
    private readonly LaunchSmoke insetSmoke = new();
    private readonly CloudLayer insetClouds = new();
    private readonly Model3DGroup mainPad = new(), mainVehicle = new(), mainPieces = new(), mainSmoke = new();
    private readonly Model3DGroup insetPad = new(), insetVehicle = new(), insetPieces = new(), insetSmokeHost = new();
    private Model3DGroup padSource = null!, smokeSource = null!, insetSmokeSource = null!;

    private void InitializeViewScenes(Model3DGroup nearRoot, Model3DGroup pad, Model3DGroup smokeGroup,
        Model3DGroup farRoot, Model3DGroup cloudFrame)
    {
        InsetDomeScene.Children.Add(insetSkyDome.Root);
        padSource = pad; smokeSource = smokeGroup;
        nearRoot.Children.Clear();
        foreach (var group in new[] { mainPad, mainVehicle, mainPieces, mainSmoke }) nearRoot.Children.Add(group);
        foreach (var light in NearScene.Children.OfType<Light>()) InsetNearScene.Children.Add(light);
        var insetRoot = new Model3DGroup();
        foreach (var group in new[] { insetPad, insetVehicle, insetPieces, insetSmokeHost }) insetRoot.Children.Add(group);
        InsetNearScene.Children.Add(insetRoot);
        var localSmoke = new Model3DGroup { Transform = insetSmokePose };
        localSmoke.Children.Add(insetSmoke.Root);
        insetSmokeSource = new Model3DGroup();
        insetSmokeSource.Children.Add(localSmoke);

        // Share opaque geometry and its live transforms, but not camera-facing transparent geometry.
        foreach (var light in FarScene.Children.OfType<Light>()) InsetFarScene.Children.Add(light);
        var insetEarth = new Model3DGroup { Transform = farEarthFixed.Transform };
        foreach (var child in farEarthFixed.Children)
        {
            if (child != cloudFrame) insetEarth.Children.Add(child);
            else
            {
                var localClouds = new Model3DGroup { Transform = new MatrixTransform3D(padFrame) };
                localClouds.Children.Add(insetClouds.Root); insetEarth.Children.Add(localClouds);
            }
        }
        var insetFarRoot = new Model3DGroup { Transform = farOrigin };
        foreach (var child in farRoot.Children) insetFarRoot.Children.Add(child == farEarthFixed ? insetEarth : child == limbGlow.Model ? insetLimbGlow.Model : child == atmosphere.Model ? insetAtmosphere.Model : child);
        InsetFarScene.Children.Add(insetFarRoot);
    }

    private void UpdateViewOcclusion(Point3D eye, Point3D origin)
    {
        UpdateVisibleModels(eye, mainPad, mainVehicle, mainPieces, mainSmoke, smokeSource);
        if (InsetVisibility == Visibility.Visible)
        {
            var insetEye = new Point3D(InsetCameraPosition.X + origin.X, InsetCameraPosition.Y + origin.Y, InsetCameraPosition.Z + origin.Z);
            UpdateVisibleModels(insetEye, insetPad, insetVehicle, insetPieces, insetSmokeHost, insetSmokeSource);
            insetLimbGlow.Update(insetEye);
            var up = (Vector3D)insetEye; double km = (up.Length - 1) * R / 1000; up.Normalize();
            double day = Math.Clamp((Vector3D.DotProduct(up, SunDirection()) + 0.05) / 0.25, 0, 1);
            int k = 0; while (k < SkyStops.Length - 2 && km > SkyStops[k + 1].AltitudeKm) k++;
            double f = Math.Clamp((km - SkyStops[k].AltitudeKm) / (SkyStops[k + 1].AltitudeKm - SkyStops[k].AltitudeKm), 0, 1);
            var a = SkyStops[k].Color; var b = SkyStops[k + 1].Color; var night = SkyStops[^1].Color;
            byte Mix(byte x, byte y, byte n) => (byte)(n + (x + (y - x) * f - n) * day);
            insetSkyDome.Update(up, day, km, Color.FromRgb(Mix(a.R, b.R, night.R), Mix(a.G, b.G, night.G), Mix(a.B, b.B, night.B)));
            insetAtmosphere.Update(km);
            InsetStarOpacity = 1 - day * Math.Clamp(1 - km / 60, 0, 1); Notify(nameof(InsetStarOpacity));
        }
    }

    private void UpdateVisibleModels(Point3D eye, Model3DGroup pad, Model3DGroup vehicle, Model3DGroup pieces,
        Model3DGroup smokeHost, Model3DGroup smokeModel)
    {
        var padPoint = SceneGeometry.Point(Earth.FixedToInertial(sim.PadFixed, missionTime));
        SetModels(pad, EarthOcclusion.Hidden(eye, padPoint, 6000 / R) ? [] : [padSource]);
        SetModels(smokeHost, EarthOcclusion.Hidden(eye, padPoint, 60000 / R) ? [] : [smokeModel]);
        SetModels(vehicle, EarthOcclusion.Hidden(eye, SceneGeometry.Point(RocketCenter), 60 / R) ? [] : [rocket.Root]);
        SetModels(pieces, sim.Debris.Where(d => !EarthOcclusion.Hidden(eye, SceneGeometry.Point(d.Position), 60 / R))
            .Select(d => (Model3D)debrisModels[d].Model).ToArray());
    }

    private static void SetModels(Model3DGroup host, Model3D[] desired)
    {
        if (host.Children.SequenceEqual(desired)) return;
        host.Children.Clear(); foreach (var model in desired) host.Children.Add(model);
    }
}
