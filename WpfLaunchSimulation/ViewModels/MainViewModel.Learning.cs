using System.Windows;
using System.Windows.Input;
using WpfLaunchSimulation.Models;

namespace WpfLaunchSimulation.ViewModels;

// Education layer: a clickable mission timeline and short explanation cards at key events.
public sealed partial class MainViewModel
{
    public sealed record TimelineMark(double Time, string Label);

    // Published actual times for flight 2 (Max-Q: planned value); the timeline jumps to a few seconds before each.
    public IReadOnlyList<TimelineMark> TimelineMarks { get; } =
    [
        new(-60, "T−1분"), new(0, "이륙"), new(64, "Max-Q"), new(123, "1단 분리"), new(227, "페어링"),
        new(269, "2단 분리"), new(790, "궤도 진입"), new(875, "위성 분리"), new(945, "모사체"), new(1250, "자세 안정화"), new(1900, "태양 지향"), new(FirstBeacon, "첫 교신"),
    ];
    public const double TimelineStart = -60, TimelineEnd = 2600;
    public double MissionTime => missionTime;

    // Piecewise scale so the busy first five minutes get half of the bar.
    public static double TimelineFraction(double t)
    {
        t = Math.Clamp(t, TimelineStart, TimelineEnd);
        if (t < 0) return 0.06 * (t - TimelineStart) / -TimelineStart;
        if (t < 300) return 0.06 + 0.49 * t / 300;
        return 0.55 + 0.45 * (t - 300) / (TimelineEnd - 300);
    }
    public static double TimelineTime(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction < 0.06) return TimelineStart + fraction / 0.06 * -TimelineStart;
        if (fraction < 0.55) return (fraction - 0.06) / 0.49 * 300;
        return 300 + (fraction - 0.55) / 0.45 * (TimelineEnd - 300);
    }

    // Jumps re-run the deterministic flight from liftoff; a backward jump restarts the simulation.
    public void JumpTo(double t)
    {
        t = Math.Clamp(t, CountdownStart, TimelineEnd + 3 * 3600);
        bool wasPlaying = playing; double keepSpeed = speed;
        if (t < missionTime) Reset();
        missionTime = t; playing = wasPlaying || missionTime <= CountdownStart; speed = keepSpeed;
        LogCountdown(Math.Min(missionTime, 0)); // countdown entries first, then the replayed flight events
        if (missionTime > 0) sim.Advance(missionTime);
        Notify(nameof(PlayText)); Notify(nameof(SpeedText));
    }
    public ICommand JumpCommand => jumpCommand ??= new ActionCommand(p => { if (p is TimelineMark m) JumpTo(m.Time - 3); });
    private ICommand? jumpCommand;

    // ---- Explanation cards -------------------------------------------------------------------------------
    private bool showCards = true;
    public bool ShowCards { get => showCards; set { showCards = value; if (!value) HideCard(); Notify(); } }
    public string CardTitle { get; private set; } = "";
    public string CardText { get; private set; } = "";
    public Visibility CardVisibility { get; private set; } = Visibility.Collapsed;
    private double cardUntil = double.NegativeInfinity;

    private static readonly (string Prefix, string Title, string Text)[] Cards =
    [
        ("이륙", "이륙", "1단 75톤급 엔진 4기의 추력이 기체 무게를 넘으면 지상고정장치가 풀립니다. 이륙 질량은 약 201.5톤, 추력 대 중량비는 약 1.4입니다."),
        ("음속 돌파", "음속 돌파", "기체 속도가 그 고도의 음속(초속 약 300~340 m)을 넘었습니다. 이 무렵 공기 저항과 진동이 크게 바뀝니다."),
        ("Max-Q", "Max-Q · 최대 동압", "동압(½ × 공기 밀도 × 속도²)이 가장 큰 순간입니다. 속도는 계속 빨라지지만 공기가 급격히 옅어져, 이후 기체가 받는 공기 힘은 줄어듭니다."),
        ("1단 분리", "1단 분리", "추진제를 다 쓴 1단을 버려 무게를 줄입니다. 빈 탱크를 계속 싣고 가면 같은 추진제로 얻는 속도가 줄어들기 때문에 로켓을 여러 단으로 만듭니다."),
        ("페어링 분리", "페어링 분리", "공기가 거의 없는 고도 200 km 부근에서, 위성을 공기 마찰과 열에서 지켜 주던 덮개(페어링)를 버립니다."),
        ("2단 분리", "2단 분리 · 3단 점화", "이제 3단(7톤급 엔진 1기)이 약 9분 동안 탑니다. 높이는 거의 다 올라왔고, 남은 일은 수평 속도를 초속 7.5 km까지 늘리는 것입니다."),
        ("3단 엔진 종료", "궤도 진입", "수평 속도가 초속 약 7.5 km에 이르면, 떨어지는 만큼 지구 표면도 휘어져 멀어지므로 계속 지구를 돌게 됩니다. 목표는 고도 700 km 태양동기궤도(경사각 약 98°)입니다."),
        ("성능검증위성 분리", "성능검증위성 분리", "위성이 3단에서 떨어져 나와 약 99분마다 지구를 한 바퀴 돕니다. 오른쪽 '정밀 조정'에서 분사 방향에 따라 궤도가 어떻게 바뀌는지 해 볼 수 있습니다."),
        ("자세 안정화", "자세 안정화", "분리 직후 위성은 천천히 뒹굽니다. 먼저 자기토커(지구 자기장을 이용)로, 이어 자이로와 반작용휠로 회전을 초속 0.3° 이하까지 줄였습니다. 단계별 시각은 예시입니다."),
        ("태양 지향", "태양 지향", "성능검증위성은 태양전지판을 펼치지 않고 몸체 옆면에 붙인 형태입니다. 그래서 위성 전체를 돌려 전지판이 태양을 향하게 해서 전력을 얻습니다."),
        ("첫 교신", "첫 교신", "발사 41분 36초 뒤 남극 세종기지 지상국이 위성의 첫 비콘(상태 신호)을 받았습니다. 위성이 살아 있다는 첫 확인입니다."),
        ("위성모사체 분리", "위성모사체 분리", "위성모사체(약 1.3톤)는 실제 위성 대신 무게를 맞춘 금속 구조물로, 누리호가 목표 무게를 궤도에 올릴 수 있는지 검증하는 데 쓰였습니다."),
    ];

    private void ShowCardFor(string eventName)
    {
        if (!showCards) return;
        var card = Cards.FirstOrDefault(c => eventName.StartsWith(c.Prefix));
        if (card.Title is null) return;
        CardTitle = card.Title; CardText = card.Text; CardVisibility = Visibility.Visible;
        cardUntil = lastWall + 12;
        Notify(nameof(CardTitle)); Notify(nameof(CardText)); Notify(nameof(CardVisibility));
    }
    private void HideCard()
    {
        if (CardVisibility == Visibility.Collapsed) return;
        CardVisibility = Visibility.Collapsed; Notify(nameof(CardVisibility));
    }
    private void UpdateCard() { if (lastWall > cardUntil) HideCard(); Notify(nameof(MissionTime)); }
}
