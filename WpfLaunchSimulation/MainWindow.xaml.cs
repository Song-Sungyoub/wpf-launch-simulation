using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WpfLaunchSimulation.ViewModels;

namespace WpfLaunchSimulation;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private Point? lastPointer;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
        CompositionTarget.Rendering += OnRendering;
        Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e) => viewModel.Tick(clock.Elapsed.TotalSeconds);

    private void OnPointerDown(object sender, MouseButtonEventArgs e) { lastPointer = e.GetPosition(InputSurface); InputSurface.CaptureMouse(); }
    private void OnPointerUp(object sender, MouseButtonEventArgs e) { lastPointer = null; InputSurface.ReleaseMouseCapture(); }
    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (lastPointer is not Point last || e.LeftButton != MouseButtonState.Pressed) return;
        var now = e.GetPosition(InputSurface);
        viewModel.Rotate(now.X - last.X, now.Y - last.Y); lastPointer = now;
    }
    private void OnMouseWheel(object sender, MouseWheelEventArgs e) => viewModel.Zoom(e.Delta);
}
