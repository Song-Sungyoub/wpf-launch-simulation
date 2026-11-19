using System.Runtime.InteropServices;
using WpfLaunchSimulation.Models;

namespace WpfLaunchSimulation.ViewModels;

// Keeps the display on while the simulation plays, like a video player; paused, Windows' normal timeouts apply.
// With the display off Windows stops composing the window, so the view would freeze and the clock fall behind.
public sealed partial class MainViewModel
{
    [Flags]
    private enum ExecutionState : uint { SystemRequired = 0x1, DisplayRequired = 0x2, Continuous = 0x80000000 }

    [DllImport("kernel32.dll")]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState state);

    private bool keepingAwake;

    // Called every frame on the UI thread; the request belongs to this thread.
    private void UpdateKeepAwake()
    {
        bool want = playing && missionTime < NuriFlight2.FlightEnd + 3 * 3600;
        if (want == keepingAwake) return;
        keepingAwake = want;
        SetThreadExecutionState(want
            ? ExecutionState.Continuous | ExecutionState.DisplayRequired | ExecutionState.SystemRequired
            : ExecutionState.Continuous);
    }
}
