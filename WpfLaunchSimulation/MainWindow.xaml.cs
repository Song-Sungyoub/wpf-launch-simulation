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

    private void OnRendering(object? sender, EventArgs e)
    {
        viewModel.Tick(clock.Elapsed.TotalSeconds);
        if (timelineProgress is not null) timelineProgress.Width = MainViewModel.TimelineFraction(viewModel.MissionTime) * TimelineCanvas.ActualWidth;
    }

    // Timeline drawing is view-only: track, progress, event ticks and labels.
    private System.Windows.Shapes.Rectangle? timelineProgress;
    private void OnTimelineSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var canvas = TimelineCanvas; canvas.Children.Clear();
        double w = canvas.ActualWidth;
        var track = new System.Windows.Shapes.Rectangle { Width = w, Height = 4, Fill = new SolidColorBrush(Color.FromRgb(36, 51, 75)), RadiusX = 2, RadiusY = 2 };
        System.Windows.Controls.Canvas.SetTop(track, 36); canvas.Children.Add(track);
        timelineProgress = new System.Windows.Shapes.Rectangle { Height = 4, Fill = new SolidColorBrush(Color.FromRgb(101, 222, 224)), RadiusX = 2, RadiusY = 2 };
        System.Windows.Controls.Canvas.SetTop(timelineProgress, 36); canvas.Children.Add(timelineProgress);
        double previousRight = double.NegativeInfinity; double previousTop = 17;
        foreach (var mark in viewModel.TimelineMarks)
        {
            double x = MainViewModel.TimelineFraction(mark.Time) * w;
            var tick = new System.Windows.Shapes.Rectangle { Width = 2, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(255, 211, 90)), IsHitTestVisible = false };
            System.Windows.Controls.Canvas.SetLeft(tick, x - 1); System.Windows.Controls.Canvas.SetTop(tick, 33); canvas.Children.Add(tick);
            // Each mark is a button (keyboard and UI Automation can use it); close labels alternate between two rows.
            var button = new System.Windows.Controls.Button
            {
                Content = mark.Label, FontSize = 10.5, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(183, 204, 229)), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
                Command = viewModel.JumpCommand, CommandParameter = mark, ToolTip = $"{MainViewModel.Clock(mark.Time)} 직전으로 이동"
            };
            button.Template = TimelineButtonTemplate;
            button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double left = Math.Clamp(x - button.DesiredSize.Width / 2, 0, Math.Max(0, w - button.DesiredSize.Width));
            double top = left < previousRight + 2 ? (previousTop == 17 ? 2 : 17) : 17;
            System.Windows.Controls.Canvas.SetLeft(button, left); System.Windows.Controls.Canvas.SetTop(button, top); canvas.Children.Add(button);
            previousRight = left + button.DesiredSize.Width; previousTop = top;
        }
    }
    private static readonly System.Windows.Controls.ControlTemplate TimelineButtonTemplate = CreateTimelineButtonTemplate();
    private static System.Windows.Controls.ControlTemplate CreateTimelineButtonTemplate()
    {
        var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
        var border = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
        border.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BackgroundProperty));
        border.SetValue(System.Windows.Controls.Border.PaddingProperty, new TemplateBindingExtension(System.Windows.Controls.Control.PaddingProperty));
        border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(4));
        border.AppendChild(new FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter)));
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(46, 66, 98))));
        template.Triggers.Add(hover);
        return template;
    }
    private void OnTimelineClick(object sender, MouseButtonEventArgs e)
    {
        double w = TimelineCanvas.ActualWidth; if (w <= 0) return;
        double t = MainViewModel.TimelineTime(e.GetPosition(TimelineCanvas).X / w);
        // Snap to an event mark within 12 px so its separation scene starts just before the event.
        foreach (var mark in viewModel.TimelineMarks)
            if (Math.Abs(MainViewModel.TimelineFraction(mark.Time) * w - e.GetPosition(TimelineCanvas).X) < 12) { t = mark.Time - 3; break; }
        viewModel.JumpTo(t);
    }

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
