using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using WpfLaunchSimulation.Rendering;

namespace WpfLaunchSimulation.ViewModels;

// Picture-in-picture: while the main view is the 3rd-person chase, a small window shows the onboard camera (and the
// reverse); clicking it swaps them. Both share the scene and the moving origin of the main view.
public sealed partial class MainViewModel
{
    private CameraMode insetMode = CameraMode.Onboard;
    public Visibility InsetVisibility { get; private set; } = Visibility.Collapsed;
    public string InsetLabel => insetMode == CameraMode.Onboard ? "탑재 카메라 · 클릭하면 전환" : "3인칭 · 클릭하면 전환";
    public Point3D InsetCameraPosition { get; private set; }
    public Vector3D InsetLookDirection { get; private set; } = new(0, 0, -1);
    public Vector3D InsetUpDirection { get; private set; } = new(0, 1, 0);
    public double InsetFieldOfView { get; private set; } = 60;
    public double InsetFarNearPlane { get; private set; } = 1e-4;
    public double InsetFarFarPlane { get; private set; } = 10;
    public double InsetNearNearPlane { get; private set; } = 1e-7;
    public double InsetNearFarPlane { get; private set; } = 0.01;
    public ICommand SwapInsetCommand => swapInsetCommand ??= new ActionCommand(() =>
    {
        if (InsetVisibility != Visibility.Visible) return;
        cameraMode = insetMode; Notify(nameof(CameraText));
    });
    private ICommand? swapInsetCommand;

    private void UpdateInset(CameraMode view, double t, Point3D center, Vector3D up, Point3D origin)
    {
        bool show = t > 0 && Satellite is null && activeMode != CameraMode.ControlRoom && view is CameraMode.Chase or CameraMode.Onboard;
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (visibility != InsetVisibility) { InsetVisibility = visibility; Notify(nameof(InsetVisibility)); }
        if (!show) return;
        var mode = view == CameraMode.Chase ? CameraMode.Onboard : CameraMode.Chase;
        if (mode != insetMode) { insetMode = mode; Notify(nameof(InsetLabel)); }
        var spec = ComputeView(mode, t, center, up);
        InsetCameraPosition = new Point3D(spec.Eye.X - origin.X, spec.Eye.Y - origin.Y, spec.Eye.Z - origin.Z);
        InsetLookDirection = spec.Look; InsetUpDirection = up; InsetFieldOfView = spec.Fov;
        double altitude = Math.Max(((Vector3D)spec.Eye).Length * R - R, 1);
        InsetFarNearPlane = Math.Max(altitude * 0.3, Math.Max(spec.NearMeters, 2)) / R;
        InsetFarFarPlane = ((Vector3D)spec.Eye).Length + 1.3;
        double focus = ((Vector3D)(spec.Eye - center)).Length * R;
        InsetNearNearPlane = Math.Max(spec.NearMeters, focus * 0.01) / R;
        InsetNearFarPlane = Math.Max(focus * 50, 70e3) / R;
        foreach (var p in new[] { nameof(InsetCameraPosition), nameof(InsetLookDirection), nameof(InsetUpDirection), nameof(InsetFieldOfView),
            nameof(InsetFarNearPlane), nameof(InsetFarFarPlane), nameof(InsetNearNearPlane), nameof(InsetNearFarPlane) }) Notify(p);
    }
}
