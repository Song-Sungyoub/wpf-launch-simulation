namespace WpfLaunchSimulation.ViewModels;

// After the separations the satellite mostly turns slowly for about 25 minutes. Until the user picks a speed, those
// quiet stretches play at 20× and each later timeline event (attitude stabilisation, Sun pointing, first beacon)
// at 1× from a little before to a little after it. Picking any speed button turns this off until "처음부터".
public sealed partial class MainViewModel
{
    private const double QuietFrom = 1000, QuietSpeed = 20, EventLead = 15, EventTail = 20;
    private bool autoSpeed = true;

    private double AutoSpeedAt(double t)
    {
        if (t < QuietFrom) return 1;
        foreach (var mark in TimelineMarks)
            if (mark.Time >= QuietFrom && t >= mark.Time - EventLead && t <= mark.Time + EventTail) return 1;
        return t < FirstBeacon ? QuietSpeed : 1;
    }

    // Called every frame before the clock advances.
    private void UpdateAutoSpeed()
    {
        if (!autoSpeed || !playing) return;
        double next = AutoSpeedAt(missionTime);
        if (next == speed) return;
        speed = next; Notify(nameof(SpeedText));
    }
}
