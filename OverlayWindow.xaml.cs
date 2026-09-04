using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace CocoaRecorder;

/// <summary>
/// 미니 모드 창. 상태는 전부 본창(MainWindow)이 쥐고 있고, 이 창은 그 상태를 비추는 화면일 뿐이다 —
/// 버튼은 본창의 메서드를 부르고, 본창이 상태가 바뀔 때마다 Sync 계열 메서드로 되밀어 준다.
/// 녹음 중에는 코코아가 헤드폰을 끼고 고개를 까딱이며, 주변에 음표가 뿅뿅 떠오른다.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int HotkeyId = 0xC0C0;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const int WmHotkey = 0x0312;
    private const uint VkR = 0x52;
    private const uint VkF3 = 0x72;

    /// <summary>
    /// 선호 순서대로 시도한다 — 다른 프로그램이 선점한 조합은 등록이 거부되므로 다음 후보로 넘어간다.
    /// F3 이 1순위: 오버레이는 한 손으로 눌러야 맛이라 단독 키가 먼저다.
    /// </summary>
    private static readonly (uint Modifiers, uint Vk, string Label)[] HotkeyCandidates =
    [
        (0, VkF3, "F3"),
        (ModControl | ModAlt, VkR, "CTRL+ALT+R"),
        (ModControl | ModShift, VkR, "CTRL+SHIFT+R"),
        (ModControl | ModAlt | ModShift, VkR, "CTRL+ALT+SHIFT+R"),
    ];

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>image-2 로 뽑은 포즈 스프라이트 — 상태마다 갈아 끼운다.</summary>
    private static readonly Dictionary<MascotMood, BitmapImage> Poses = new()
    {
        [MascotMood.Idle] = LoadPose("idle"),
        [MascotMood.Countdown] = LoadPose("count"),
        [MascotMood.Vibing] = LoadPose("vibe"),
        [MascotMood.Paused] = LoadPose("sleep"),
    };

    private static BitmapImage LoadPose(string name)
        => new(new Uri($"pack://application:,,,/Art/cocoa-{name}.png"));

    private readonly MainWindow _main;
    private bool _closingSilently;
    private bool _hotkeyRegistered;
    private MascotMood _mood = MascotMood.Idle;
    private readonly List<Storyboard> _loops = [];
    private readonly List<Storyboard> _ambient = [];

    public OverlayWindow(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        CatImage.Source = Poses[MascotMood.Idle];
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) =>
        {
            if (!_closingSilently) _main.OnOverlayClosedByUser();
        };
        Loaded += (_, _) =>
        {
            StartAmbient();
            Pop(RootScale, from: 0.4, seconds: 0.4);
        };
    }

    // ------------------------------------------------- 전역 단축키
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        source.AddHook(WndProc);
        foreach (var (modifiers, vk, label) in HotkeyCandidates)
        {
            if (RegisterHotKey(source.Handle, HotkeyId, modifiers | ModNoRepeat, vk))
            {
                _hotkeyRegistered = true;
                HotkeyHint.Text = label;
                return;
            }
        }
        // 후보가 전부 선점되어 있으면 안내 문구를 지운다 — 없는 기능을 약속하지 않는다.
        HotkeyHint.Visibility = Visibility.Collapsed;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            _main.MiniPrimary();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>본창으로 돌아가거나 앱이 닫힐 때 — 이 창이 앱을 끌고 내려가지 않게 한다.</summary>
    public void CloseSilently()
    {
        _closingSilently = true;
        Close();
    }

    /// <summary>어느 길로 닫히든 여기를 지난다 — 단축키를 돌려주고 무한 반복 애니메이션을 세운다.</summary>
    protected override void OnClosed(EventArgs e)
    {
        StopLoops();
        foreach (var loop in _ambient) loop.Stop();
        _ambient.Clear();
        if (_hotkeyRegistered && PresentationSource.FromVisual(this) is HwndSource source)
            UnregisterHotKey(source.Handle, HotkeyId);
        base.OnClosed(e);
    }

    // ------------------------------------------------- 본창이 되밀어 주는 상태
    public void SyncState(MascotMood mood)
    {
        if (mood == _mood) return;
        _mood = mood;

        StepperRow.Visibility = mood == MascotMood.Idle ? Visibility.Visible : Visibility.Collapsed;
        CountDigit.Visibility = mood == MascotMood.Countdown ? Visibility.Visible : Visibility.Collapsed;
        LiveRow.Visibility = mood is MascotMood.Recording or MascotMood.Paused ? Visibility.Visible : Visibility.Collapsed;

        var night = (Brush)FindResource("NightBrush");
        var lilac = (Brush)FindResource("LilacBrush");
        var pink = (Brush)FindResource("PinkBrush");
        var amber = (Brush)FindResource("AmberBrush");

        switch (mood)
        {
            case MascotMood.Idle:
                CatImage.Source = Poses[MascotMood.Idle];
                StyleAction(pink, Brushes.Transparent, night, "REC", dot: true);
                ActionButton.ToolTip = null;
                StopLoops();
                break;

            case MascotMood.Countdown:
                CatImage.Source = Poses[MascotMood.Countdown];
                StyleAction(Brushes.Transparent, lilac, lilac, L10n.T("CancelBtn"), dot: false);
                ActionButton.ToolTip = L10n.T("MiniCancelHint");
                StopLoops();
                break;

            case MascotMood.Recording:
                // 헤드폰을 끼고 소리에 폭 빠진 코코아 — 고개를 까딱이고 음표가 떠오른다.
                CatImage.Source = Poses[MascotMood.Vibing];
                StyleAction(pink, Brushes.Transparent, night, L10n.T("StopBtn"), dot: false, square: true);
                ActionButton.ToolTip = L10n.T("MiniStopHint");
                RecLabel.Text = "REC";
                RecLabel.Foreground = pink;
                RecDot.Fill = pink;
                StartLoops();
                break;

            case MascotMood.Paused:
                CatImage.Source = Poses[MascotMood.Paused];
                StyleAction(amber, Brushes.Transparent, night, L10n.T("StopBtn"), dot: false, square: true);
                ActionButton.ToolTip = L10n.T("MiniStopHint");
                RecLabel.Text = "PAUSED";
                RecLabel.Foreground = amber;
                RecDot.Fill = amber;
                StopLoops();
                break;
        }

        Pop(CardScale);
    }

    private void StyleAction(Brush background, Brush border, Brush foreground, string text, bool dot, bool square = false)
    {
        ActionButton.Background = background;
        ActionButton.BorderBrush = border;
        ActionButton.Foreground = foreground;
        ActionText.Text = text;
        ActionText.Foreground = foreground;
        ActionGlyph.Visibility = dot || square ? Visibility.Visible : Visibility.Collapsed;
        ActionGlyph.Fill = foreground;
        // 원(녹음 시작)이냐 네모(정지)냐 — 트랜스포트의 오래된 약속을 그대로 따른다.
        ActionGlyph.RadiusX = ActionGlyph.RadiusY = square ? 0 : 4.5;
    }

    public void ShowCountdown(int value)
    {
        CountDigit.Text = value.ToString();
        Pop(DigitScale, from: 1.55, seconds: 0.4);
    }

    public void SetElapsed(TimeSpan ts)
        => LiveElapsed.Text = $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";

    public void SetCountdown(string label, bool editable, bool canGoDown, bool canGoUp)
    {
        OverlaySeconds.Text = label;
        OverlayDown.IsEnabled = editable && canGoDown;
        OverlayUp.IsEnabled = editable && canGoUp;
    }

    // ------------------------------------------------- 입력
    private void Action_Click(object sender, RoutedEventArgs e) => _main.MiniPrimary();
    private void Down_Click(object sender, RoutedEventArgs e) => _main.MiniStepCountdown(-1);
    private void Up_Click(object sender, RoutedEventArgs e) => _main.MiniStepCountdown(+1);
    private void Restore_Click(object sender, RoutedEventArgs e) => _main.ExitMiniMode();

    private void Drag_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space: _main.MiniPrimary(); e.Handled = true; break;
            case Key.Escape: _main.MiniCancel(); e.Handled = true; break;
            case Key.P: _main.MiniPause(); e.Handled = true; break;
        }
        base.OnPreviewKeyDown(e);
    }

    // ------------------------------------------------- 상시 잔잔한 움직임
    /// <summary>프레임을 살아 있게 한다 — 테두리 별들이 각자 깜빡이고, 귀가 가끔 쫑긋한다.</summary>
    private void StartAmbient()
    {
        // 원본에서 지운 일곱 별의 자리 (1/4 축척 중심 좌표)
        var stars = new (string Brush, double X, double Y)[]
        {
            ("MintBrush", 100, 48), ("AmberBrush", 285, 48), ("PinkBrush", 34, 104),
            ("MintBrush", 332, 106), ("PinkBrush", 332, 163), ("MintBrush", 95, 231),
            ("AmberBrush", 269, 231),
        };
        for (int i = 0; i < stars.Length; i++)
        {
            var (brushKey, x, y) = stars[i];
            var star = new Canvas { Width = 9, Height = 9 };
            var color = (Brush)FindResource(brushKey);
            var cream = (Brush)FindResource("CreamBrush");
            foreach (var (cx, cy) in new[] { (1, 0), (0, 1), (1, 1), (2, 1), (1, 2) })
            {
                var r = new WpfRectangle
                {
                    Width = 3,
                    Height = 3,
                    Fill = cx == 1 && cy == 1 ? cream : color,
                    SnapsToDevicePixels = true,
                };
                Canvas.SetLeft(r, cx * 3);
                Canvas.SetTop(r, cy * 3);
                star.Children.Add(r);
            }
            Canvas.SetLeft(star, x - 4);
            Canvas.SetTop(star, y - 4);
            StarLayer.Children.Add(star);

            var twinkle = new DoubleAnimation(0.15, 1, TimeSpan.FromSeconds(1.6 + i * 0.37))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            var sb = new Storyboard { BeginTime = TimeSpan.FromSeconds(i * 0.5) };
            Storyboard.SetTarget(twinkle, star);
            Storyboard.SetTargetProperty(twinkle, new PropertyPath(OpacityProperty));
            sb.Children.Add(twinkle);
            sb.Begin();
            _ambient.Add(sb);
        }
    }

    // ------------------------------------------------- 애니메이션
    /// <summary>뿅 — 작게 시작해 통 튀며 제자리로.</summary>
    private static void Pop(ScaleTransform scale, double from = 0.88, double seconds = 0.28)
    {
        var anim = new DoubleAnimation(from, 1, TimeSpan.FromSeconds(seconds))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    /// <summary>고개 까딱까딱 + 음표 뿅뿅. 녹음 동안만 돈다.</summary>
    private void StartLoops()
    {
        StopLoops();

        // 박자 타는 고개 — 좌우로 갸웃갸웃
        var nod = new DoubleAnimation(-4, 4, TimeSpan.FromSeconds(0.55))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        var nodBoard = new Storyboard();
        Storyboard.SetTarget(nod, CatImage);
        Storyboard.SetTargetProperty(nod, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        nodBoard.Children.Add(nod);
        nodBoard.Begin();
        _loops.Add(nodBoard);

        // 음표 세 개 — 각자 다른 박자로 떠오르며 사라진다
        var tints = new[] { "MintBrush", "AmberBrush", "PinkSoftBrush" };
        var notes = new[] { Note1, Note2, Note3 };
        var timing = new[] { (Dur: 1.7, Delay: 0.0), (Dur: 2.1, Delay: 0.7), (Dur: 1.9, Delay: 1.3) };
        for (int i = 0; i < notes.Length; i++)
        {
            var note = notes[i];
            Mascot.DrawNote(note, 3, (Brush)FindResource(tints[i]));
            note.RenderTransform = new TranslateTransform();

            var duration = TimeSpan.FromSeconds(timing[i].Dur);
            var begin = TimeSpan.FromSeconds(timing[i].Delay);

            var rise = new DoubleAnimation(6, -34, duration);
            Storyboard.SetTarget(rise, note);
            Storyboard.SetTargetProperty(rise, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

            // 떠오르면서 나타났다가 위에서 사라진다
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.25)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.65)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            fade.Duration = duration;
            Storyboard.SetTarget(fade, note);
            Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));

            var sb = new Storyboard { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever, Duration = duration };
            sb.Children.Add(rise);
            sb.Children.Add(fade);
            sb.Begin();
            _loops.Add(sb);
        }
    }

    private void StopLoops()
    {
        foreach (var loop in _loops) loop.Stop();
        _loops.Clear();
        CatTilt.BeginAnimation(RotateTransform.AngleProperty, null);
        CatTilt.Angle = 0;
        foreach (var note in new[] { Note1, Note2, Note3 }) note.Opacity = 0;
    }
}
