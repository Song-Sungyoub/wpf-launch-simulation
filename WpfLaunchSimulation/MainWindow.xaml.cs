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
    private TimeSpan lastRenderingTime = TimeSpan.MinValue;
    private double performanceStart = double.NaN, previousFrame = double.NaN, maxFrameGap, updateTotal;
    private int performanceFrames;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.InsetVisibility)) UpdateSceneInsets(); };
        // Screen-space bloom, tone and vignette on the 3D layers only (not the HUD panels drawn over them).
        if (Rendering.PostEffect.Available)
        {
            var effect = new Rendering.PostEffect();
            System.Windows.Data.BindingOperations.SetBinding(effect, Rendering.PostEffect.BloomProperty,
                new System.Windows.Data.Binding(nameof(MainViewModel.BloomStrength)) { Source = viewModel });
            var insetEffect = new Rendering.PostEffect { Vignette = 0.12, Bloom = 0 };
            void ApplyEffects() { MainScene.Effect = viewModel.PostProcessingOn ? effect : null; InsetScene.Effect = viewModel.PostProcessingOn ? insetEffect : null; TrackingScene.Effect = viewModel.PostProcessingOn ? insetEffect : null; }
            ApplyEffects();
            viewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.PostProcessingOn)) ApplyEffects(); };
        }
        CompositionTarget.Rendering += OnRendering;
        Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // WPF may raise Rendering repeatedly for one composition timestamp.
        if (e is RenderingEventArgs rendering)
        {
            if (rendering.RenderingTime == lastRenderingTime) return;
            lastRenderingTime = rendering.RenderingTime;
        }
        double now = clock.Elapsed.TotalSeconds;
        if (double.IsNaN(performanceStart)) performanceStart = now;
        if (!double.IsNaN(previousFrame)) maxFrameGap = Math.Max(maxFrameGap, now - previousFrame);
        previousFrame = now;
        long started = Stopwatch.GetTimestamp();
        viewModel.Tick(now);
        if (timelineProgress is not null) timelineProgress.Width = MainViewModel.TimelineFraction(viewModel.MissionTime) * TimelineCanvas.ActualWidth;
        updateTotal += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        performanceFrames++;
        if (now - performanceStart >= 1)
        {
            viewModel.SetPerformance(performanceFrames / (now - performanceStart), maxFrameGap * 1000, updateTotal / performanceFrames);
            performanceStart = now; performanceFrames = 0; maxFrameGap = updateTotal = 0;
        }
    }

    // Timeline drawing is view-only: track, progress, event ticks and labels.
    private System.Windows.Shapes.Rectangle? timelineProgress;
    private void OnTimelineSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var canvas = TimelineCanvas; canvas.Children.Clear();
        double w = canvas.ActualWidth;
        var track = new System.Windows.Shapes.Rectangle { Width = w, Height = 4, Fill = new SolidColorBrush(Color.FromRgb(36, 51, 75)), RadiusX = 2, RadiusY = 2 };
        System.Windows.Controls.Canvas.SetTop(track, 54); canvas.Children.Add(track);
        timelineProgress = new System.Windows.Shapes.Rectangle { Height = 4, Fill = new SolidColorBrush(Color.FromRgb(101, 222, 224)), RadiusX = 2, RadiusY = 2 };
        System.Windows.Controls.Canvas.SetTop(timelineProgress, 54); canvas.Children.Add(timelineProgress);
        double[] rowTops = [35, 19, 3]; var rowRight = new double[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        foreach (var mark in viewModel.TimelineMarks)
        {
            double x = MainViewModel.TimelineFraction(mark.Time) * w;
            var tick = new System.Windows.Shapes.Rectangle { Width = 2, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(255, 211, 90)), IsHitTestVisible = false };
            System.Windows.Controls.Canvas.SetLeft(tick, x - 1); System.Windows.Controls.Canvas.SetTop(tick, 51); canvas.Children.Add(tick);
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
            int row = 0; while (row < rowTops.Length - 1 && left < rowRight[row] + 2) row++;
            double top = rowTops[row];
            System.Windows.Controls.Canvas.SetLeft(button, left); System.Windows.Controls.Canvas.SetTop(button, top); canvas.Children.Add(button);
            rowRight[row] = left + button.DesiredSize.Width;
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

    private void OnSceneSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (InsetButton is null) return;
        InsetButton.Width = Math.Min(384, Math.Max(120, e.NewSize.Width * 0.42));
        InsetButton.Height = Math.Min(InsetButton.Width * 9 / 16, Math.Max(70, e.NewSize.Height * 0.28));
        UpdateSceneInsets();
    }

    private void UpdateSceneInsets()
    {
        if (MainScene is null || InsetButton is null) return;
        // Reserve a bottom strip rather than painting the inset over the vehicle.
        MainScene.Margin = new Thickness(0, 0, 0, viewModel.InsetVisibility == Visibility.Visible ? InsetButton.Height + 96 : 0);
    }

    private void OnMainSceneSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0) viewModel.SetViewportAspect(e.NewSize.Width / e.NewSize.Height);
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
