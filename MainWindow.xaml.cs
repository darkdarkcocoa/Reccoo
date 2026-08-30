using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using IOPath = System.IO.Path;

namespace CocoaRecorder;

public partial class MainWindow : Window
{
    private const string ProductFolderName = "Cocoa Recorder";
    private const string LegacyProductFolderName = "Reccoo";

    /// <summary>밤하늘 난수 seed — 시안과 같은 별자리가 나오도록 고정한다.</summary>
    private const int SkySeed = 11;
    private const int MoonCell = 6;
    private const int CatCell = 5;

    /// <summary>도움말 하늘은 같은 난수식을 다른 씨앗과 비율로 돌려 세로로 길게 뿌린다.</summary>
    private const int HelpSkySeed = 23;
    private const int HelpCatCell = 7;
    private const int HelpStepCount = 4;
    private int _helpStep;

    private readonly AudioRecorder _recorder = new();
    private readonly AppPreferences _preferences = AppPreferences.Load();
    private string _saveFolder = string.Empty;

    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _waveformTimer;

    /// <summary>선택한 장치의 샘플레이트 — 녹음 전에도 히어로에 적어 두기 위해 미리 읽어 둔다.</summary>
    private int _deviceSampleRate;

    private readonly Random _rng = new();
    private readonly ObservableCollection<WaveformBar> _bars = new();
    private readonly ObservableCollection<LevelCell> _levelCells = new();
    private readonly ObservableCollection<RecordingItem> _recordings = new();

    /// <summary>녹음 화면의 "TONIGHT'S TAPES" 는 최근 것만 보여준다. 전체는 보관함 탭이 맡는다.</summary>
    private readonly ObservableCollection<RecordingItem> _recent = new();
    private const int RecentCount = 4;

    private const int BarCount = 56;
    private const int LevelCellCount = 18;

    private readonly object _peakLock = new();
    private float _peakAccumulator;
    private readonly double[] _peakHistory = new double[BarCount];
    private int _peakWriteIdx;
    private double _smoothedLevel;

    /// <summary>화면이 보여주는 네 가지 상태. 트랜스포트 버튼과 히어로 색이 여기서 갈린다.</summary>
    private enum Transport { Idle, Countdown, Recording, Paused }

    private Transport State =>
        _countdownTimer != null ? Transport.Countdown
        : !_recorder.IsRecording ? Transport.Idle
        : _recorder.IsPaused ? Transport.Paused
        : Transport.Recording;

    public MainWindow()
    {
        InitializeComponent();
        UpdateLangToggle();
        L10n.LanguageChanged += OnLanguageChanged;

        _saveFolder = ResolveInitialSaveFolder();
        Directory.CreateDirectory(_saveFolder);
        FolderText.Text = _saveFolder;
        LibraryFolderHint.Text = _saveFolder;

        var restBar = (Brush)FindResource("WaveLowBrush");
        for (int i = 0; i < BarCount; i++)
            _bars.Add(new WaveformBar { Height = 6, Fill = restBar });
        WaveformHost.ItemsSource = _bars;

        var emptyCell = (Brush)FindResource("WellBrush");
        for (int i = 0; i < LevelCellCount; i++)
            _levelCells.Add(new LevelCell { Fill = emptyCell });
        LevelMeter.ItemsSource = _levelCells;

        RecordingsList.ItemsSource = _recordings;
        RecentList.ItemsSource = _recent;

        UpdateCountdownVisual();

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _uiTimer.Tick += (_, _) => { UpdateTimerLabels(); UpdateSizeLabel(); };

        _waveformTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _waveformTimer.Tick += (_, _) => TickWaveform();

        _recorder.RecordingFinished += OnRecordingFinished;
        _recorder.LevelChanged += OnLevelChanged;

        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += (_, _) =>
        {
            L10n.LanguageChanged -= OnLanguageChanged;
            _waveformTimer.Stop();
            _uiTimer.Stop();
            StopPlayback();
            _recorder.Dispose();
        };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 글자를 치는 중일 때만 비켜선다 (이름 바꾸기 등).
        if (Keyboard.FocusedElement is TextBox) return;

        // Space 는 이 앱에서 언제나 녹음이다. Windows 관례상 Space 는 포커스된 버튼을 누르는 키지만,
        // 녹음기에서 가장 중요한 동작이 방금 누른 토글에 가려지는 편이 더 이상하다.
        // PreviewKeyDown 에서 Handled 로 막으므로 버튼에는 키가 닿지 않는다.
        // 키보드로 고르는 길은 남아 있다 — 라디오 묶음은 방향키로, 버튼은 Enter 로 눌린다.

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        switch (e.Key)
        {
            case Key.Space:
                switch (State)
                {
                    case Transport.Countdown: CancelCountdown(); break;
                    case Transport.Idle: StartRecording(); break;
                    default: StopRecording(); break;
                }
                e.Handled = true;
                break;

            case Key.Escape when State == Transport.Countdown:
                CancelCountdown();
                e.Handled = true;
                break;

            case Key.P:
                if (_recorder.IsRecording)
                {
                    TogglePause();
                    e.Handled = true;
                }
                break;

            case Key.O when ctrl:
                OpenSaveFolder();
                e.Handled = true;
                break;

            case Key.F1:
                TabHelp.IsChecked = true;
                e.Handled = true;
                break;

            case Key.F5:
                RefreshRecordings();
                e.Handled = true;
                break;
        }
    }

    private const string RepoUrl = "https://github.com/darkdarkcocoa/cocoa-recorder";

    /// <summary>하고 싶은 말이 있을 때 갈 곳 — 새 이슈 작성 화면을 바로 연다.</summary>
    private void Feedback_Click(object sender, RoutedEventArgs e)
        => OpenLink(RepoUrl + "/issues/new", "MsgFeedback");

    /// <summary>앱이 만들어진 곳.</summary>
    private void Github_Click(object sender, RoutedEventArgs e)
        => OpenLink(RepoUrl, "MsgGithub");

    private void OpenLink(string url, string speechKey)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            MascotSpeech.Text = L10n.T(speechKey);
        }
        catch (Exception ex)
        {
            MascotSpeech.Text = $"{L10n.T("MsgLinkFail")}\n{ex.Message}";
        }
    }

    private void OpenSaveFolder()
    {
        try
        {
            if (Directory.Exists(_saveFolder))
                Process.Start(new ProcessStartInfo(_saveFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MascotSpeech.Text = $"{L10n.T("MsgFolderOpenFail")}\n{ex.Message}";
        }
    }

    private string ResolveInitialSaveFolder()
    {
        if (!string.IsNullOrWhiteSpace(_preferences.SaveFolder))
            return _preferences.SaveFolder;

        var musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        var cocoaFolder = IOPath.Combine(musicFolder, ProductFolderName);
        var legacyFolder = IOPath.Combine(musicFolder, LegacyProductFolderName);

        // Existing users keep seeing their library without moving any files.
        return !Directory.Exists(cocoaFolder) && Directory.Exists(legacyFolder)
            ? legacyFolder
            : cocoaFolder;
    }

    private bool _closeAfterStop;
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_recorder.IsRecording || _closeAfterStop) return;
        e.Cancel = true;
        _closeAfterStop = true;
        MascotSpeech.Text = L10n.T("MsgClosing");
        EventHandler<RecordingFinishedEventArgs>? handler = null;
        handler = (_, _) =>
        {
            _recorder.RecordingFinished -= handler;
            Dispatcher.BeginInvoke(new Action(Close));
        };
        _recorder.RecordingFinished += handler;
        _recorder.Stop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NightSky.DrawStars(StarCanvas, SkySeed);
        NightSky.DrawMoon(MoonCanvas, MoonCell);
        NightSky.DrawMoonSparks(MoonSparks);
        NightSky.Breathe(MoonGlow, MoonGlowScale);

        NightSky.DrawStars(HelpStarCanvas, HelpSkySeed, xScale: 0.5, yScale: 2.2);
        NightSky.DrawMoon(HelpMoonCanvas, MoonCell);
        NightSky.DrawMoonSparks(HelpMoonSparks);
        NightSky.Breathe(HelpMoonGlow, HelpMoonGlowScale);
        ApplyHelpStep(0);

        try
        {
            var devices = AudioRecorder.GetRenderDevices();
            DeviceCombo.ItemsSource = devices;
            if (devices.Count > 0) DeviceCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MascotSpeech.Text = $"{L10n.T("MsgDeviceLoadFail")}\n{ex.Message}";
        }

        ShowTab(TabRecord);
        UpdateTransport();
        UpdateFormatInfo();
        RefreshRecordings();
        _waveformTimer.Start();
    }

    // =================== Title bar ===================
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2) ToggleMaximize();
        else DragMove();
    }

    // ---------------------------------------------------------------
    // 테두리 없는 창(WindowStyle="None")은 최대화할 때 모니터 전체를 덮는다 — 작업 표시줄까지.
    // WM_GETMINMAXINFO 에서 최대 크기를 작업 영역으로 깎아 주어야 표시줄이 살아 있는다.
    // ---------------------------------------------------------------
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(ClampMaximizeToWorkArea);
    }

    private IntPtr ClampMaximizeToWorkArea(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        mmi.MaxPosition.X = info.Work.Left - info.Monitor.Left;
        mmi.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
        mmi.MaxSize.X = info.Work.Right - info.Work.Left;
        mmi.MaxSize.Y = info.Work.Bottom - info.Work.Top;

        // 최소 크기는 WPF 의 MinWidth/MinHeight 를 화면 픽셀로 환산해 넘긴다.
        var toDevice = HwndSource.FromHwnd(hwnd)?.CompositionTarget?.TransformToDevice;
        double scaleX = toDevice?.M11 ?? 1.0;
        double scaleY = toDevice?.M22 ?? 1.0;
        mmi.MinTrackSize.X = (int)Math.Ceiling(MinWidth * scaleX);
        mmi.MinTrackSize.Y = (int)Math.Ceiling(MinHeight * scaleY);

        Marshal.StructureToPtr(mmi, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    // =================== Tabs ===================
    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        // XAML의 IsChecked="True"는 트리가 다 만들어지기 전에 발생한다.
        if (!IsInitialized) return;
        ShowTab(sender as RadioButton);
    }

    private void ShowTab(RadioButton? tab)
    {
        RecordPanel.Visibility = ReferenceEquals(tab, TabRecord) ? Visibility.Visible : Visibility.Collapsed;
        LibraryPanel.Visibility = ReferenceEquals(tab, TabLibrary) ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = ReferenceEquals(tab, TabSettings) ? Visibility.Visible : Visibility.Collapsed;

        // 도움말은 패널이 아니라 화면 전체다 — 시안 4a 그대로 히어로까지 덮는다.
        HelpScreen.Visibility = ReferenceEquals(tab, TabHelp) ? Visibility.Visible : Visibility.Collapsed;

        if (ReferenceEquals(tab, TabLibrary)) RefreshRecordings();
    }

    // =================== Help tour ===================
    private void HelpRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int step))
            ApplyHelpStep(step);
    }

    private void HelpBack_Click(object sender, RoutedEventArgs e) => ApplyHelpStep(_helpStep - 1);

    private void HelpNext_Click(object sender, RoutedEventArgs e)
    {
        if (_helpStep < HelpStepCount - 1)
        {
            ApplyHelpStep(_helpStep + 1);
            return;
        }
        // 마지막 칸의 "녹음하자!" — 시안은 처음으로 되감지만, 실제 앱에서는 녹음 화면으로 보내는 게 맞다.
        ApplyHelpStep(0);
        TabRecord.IsChecked = true;
    }

    /// <summary>
    /// 네 단계를 하나로 묶어 갈아끼운다 — 말풍선 글, 고른 줄의 테두리, 점, 버튼, 그리고 고양이의 자세와 목걸이 색.
    /// 시안의 HELP 배열(자세 + 강조색)을 그대로 옮긴 것이다.
    /// </summary>
    private void ApplyHelpStep(int step)
    {
        _helpStep = Math.Clamp(step, 0, HelpStepCount - 1);

        var accents = new[]
        {
            (Brush)FindResource("MintBrush"),
            (Brush)FindResource("PinkBrush"),
            (Brush)FindResource("AmberBrush"),
            (Brush)FindResource("LilacBrush"),
        };
        var poses = new[] { MascotMood.Idle, MascotMood.Recording, MascotMood.Paused, MascotMood.Countdown };
        var rows = new[] { HelpRow1, HelpRow2, HelpRow3, HelpRow4 };
        var texts = new[] { HelpRowText1, HelpRowText2, HelpRowText3, HelpRowText4 };
        var dots = new[] { HelpDot1, HelpDot2, HelpDot3, HelpDot4 };

        var idle = (Brush)FindResource("PanelSelBrush");
        var dim = (Brush)FindResource("LineSoftBrush");
        var cream = (Brush)FindResource("CreamBrush");
        var mute = (Brush)FindResource("Mute2Brush");

        for (int i = 0; i < HelpStepCount; i++)
        {
            bool on = i == _helpStep;
            rows[i].Background = on ? idle : Brushes.Transparent;
            rows[i].BorderBrush = on ? accents[i] : idle;
            texts[i].Foreground = on ? cream : mute;
            dots[i].Width = dots[i].Height = on ? 14 : 8;
            dots[i].Fill = on ? (Brush)FindResource("AmberBrush") : dim;
        }

        HelpSaysLabel.Text = $"{L10n.T("CocoaSays")} · {_helpStep + 1} / {HelpStepCount}";
        HelpStepTitle.Text = L10n.T($"Step{_helpStep + 1}Title");
        HelpStepBody.Text = L10n.T($"Step{_helpStep + 1}Body");
        HelpCounter.Text = $"{_helpStep + 1} / {HelpStepCount}";

        bool first = _helpStep == 0;
        HelpBackButton.IsEnabled = !first;
        HelpBackButton.BorderBrush = first ? idle : (Brush)FindResource("LilacBrush");
        HelpBackButton.Foreground = first ? dim : (Brush)FindResource("LilacBrush");

        bool last = _helpStep == HelpStepCount - 1;
        HelpNextButton.Background = (Brush)FindResource(last ? "MintBrush" : "AmberBrush");
        HelpNextText.Text = L10n.T(last ? "HelpDone" : "HelpNext");
        HelpNextArrow.Visibility = last ? Visibility.Collapsed : Visibility.Visible;

        Mascot.Draw(HelpCatCanvas, poses[_helpStep], HelpCatCell, accents[_helpStep]);
        NightSky.Drift(HelpCatBob, poses[_helpStep] == MascotMood.Paused ? 3.4 : 2.4);
    }

    // =================== Language toggle ===================
    private void LangToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return; // 초기 IsChecked 세팅(생성자)에서는 무시
        L10n.Set(ReferenceEquals(sender, KorToggle) ? AppLanguage.Korean : AppLanguage.English);
    }

    private void UpdateLangToggle()
    {
        if (L10n.IsKorean) KorToggle.IsChecked = true;
        else EnToggle.IsChecked = true;
    }

    private void OnLanguageChanged()
    {
        UpdateLangToggle();

        // XAML의 DynamicResource는 자동 갱신되므로, 코드가 직접 세팅하는
        // 상태 의존 텍스트만 현재 상태에 맞춰 다시 그린다.
        if (State == Transport.Idle) MascotSpeech.Text = Pick(L10n.IdleMessages);
        UpdateTransport();
        UpdateCountdownVisual(); // "3초" / "3s" 는 코드가 조립하므로 직접 다시 그린다
        ApplyHelpStep(_helpStep);
        UpdateFormatInfo();
        RefreshRecordings(); // 카드 Meta의 날짜 표기 언어 갱신
    }

    // =================== Folder picker ===================
    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = L10n.T("FolderDialogTitle"),
            InitialDirectory = _saveFolder
        };
        if (dialog.ShowDialog() == true)
        {
            _saveFolder = dialog.FolderName;
            _preferences.SaveFolder = _saveFolder;
            _preferences.Save();
            FolderText.Text = _saveFolder;
            LibraryFolderHint.Text = _saveFolder;
            RefreshRecordings();
        }
    }

    private void Device_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _deviceSampleRate = DeviceCombo.SelectedItem is MMDevice device
            ? AudioRecorder.TryGetSampleRate(device)
            : 0;
        if (IsInitialized) UpdateFormatInfo();
    }

    // =================== Format toggle ===================
    private void Format_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        UpdateFormatInfo();
    }

    private void Bitrate_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        if (LoToggle.IsChecked == true) _recorder.Mp3Quality = Mp3Quality.Low;
        else if (HiToggle.IsChecked == true) _recorder.Mp3Quality = Mp3Quality.High;
        else _recorder.Mp3Quality = Mp3Quality.Medium;
        UpdateFormatInfo();
    }

    // =================== Countdown length ===================
    private void CountdownDown_Click(object sender, RoutedEventArgs e) => StepCountdown(-1);
    private void CountdownUp_Click(object sender, RoutedEventArgs e) => StepCountdown(+1);

    private void StepCountdown(int delta)
    {
        SetCountdownSeconds(_preferences.CountdownSeconds + delta);
    }

    private bool _syncingCountdown;

    /// <summary>레일의 프리셋(off · 3s · 5s · 10s)은 스테퍼와 같은 값을 건드린다.</summary>
    private void CountdownPreset_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _syncingCountdown) return;
        if (sender is RadioButton { Tag: string tag } && int.TryParse(tag, out int seconds))
            SetCountdownSeconds(seconds);
    }

    private void SetCountdownSeconds(int seconds)
    {
        var next = Math.Clamp(seconds, 0, AppPreferences.MaxCountdownSeconds);
        if (next == _preferences.CountdownSeconds) return;
        _preferences.CountdownSeconds = next;
        _preferences.Save();
        UpdateCountdownVisual();
    }

    private void UpdateCountdownVisual()
    {
        int seconds = _preferences.CountdownSeconds;
        CountdownValueText.Text = seconds == 0
            ? L10n.T("CountdownZero")
            : string.Format(L10n.T("CountdownUnit"), seconds);

        // 프리셋에 없는 값(1·2·4…)이면 어느 칸도 켜지 않는다 — 스테퍼가 진실이다.
        _syncingCountdown = true;
        CountdownOff.IsChecked = seconds == 0;
        Countdown3.IsChecked = seconds == 3;
        Countdown5.IsChecked = seconds == 5;
        Countdown10.IsChecked = seconds == 10;
        _syncingCountdown = false;

        bool editable = State is Transport.Idle;
        CountdownDownButton.IsEnabled = editable && seconds > 0;
        CountdownUpButton.IsEnabled = editable && seconds < AppPreferences.MaxCountdownSeconds;
        foreach (var preset in new[] { CountdownOff, Countdown3, Countdown5, Countdown10 })
            preset.IsEnabled = editable;
    }

    private void UpdateFormatInfo()
    {
        bool isMp3 = Mp3Toggle.IsChecked == true;
        BitrateLabel.Visibility = isMp3 ? Visibility.Visible : Visibility.Collapsed;
        BitrateGroup.Visibility = isMp3 ? Visibility.Visible : Visibility.Collapsed;

        string label = isMp3
            ? "MP3 " + (LoToggle.IsChecked == true ? "LO" : HiToggle.IsChecked == true ? "HI" : "MED")
            : "WAV";

        int rate = _recorder.SampleRate > 0 ? _recorder.SampleRate : _deviceSampleRate;
        FormatInfoText.Text = rate > 0
            ? $" · {label} · {rate / 1000.0:0.#} kHz"
            : $" · {label}";
    }

    // =================== Transport ===================
    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        switch (State)
        {
            case Transport.Countdown: CancelCountdown(); break;
            case Transport.Idle: StartRecording(); break;
            case Transport.Paused: TogglePause(); break;
            default: StopRecording(); break;
        }
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (State == Transport.Recording) TogglePause();
        else if (State == Transport.Paused) StopRecording();
    }

    private void TogglePause()
    {
        if (!_recorder.IsRecording) return;
        if (_recorder.IsPaused) _recorder.Resume();
        else _recorder.Pause();

        MascotSpeech.Text = L10n.T(_recorder.IsPaused ? "MsgPaused" : "MsgResumed");
        UpdateTransport();
    }

    private void StopRecording()
    {
        if (!_recorder.IsRecording) return;
        MascotSpeech.Text = L10n.T("MsgSaving");
        _recorder.Stop();
    }

    private DispatcherTimer? _countdownTimer;
    private int _countdownValue;

    private void StartRecording()
    {
        if (DeviceCombo.SelectedItem is null)
        {
            MascotSpeech.Text = L10n.T("MsgPickDevice");
            return;
        }
        if (_preferences.CountdownSeconds > 0)
            BeginCountdown(_preferences.CountdownSeconds);
        else
            ActuallyStartRecording();
    }

    private void BeginCountdown(int from)
    {
        _countdownValue = from;

        _countdownTimer?.Stop();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _countdownTimer.Tick += CountdownTick;
        _countdownTimer.Start();

        CountdownOverlay.Visibility = Visibility.Visible;
        ApplyCountdownVisual();
        UpdateTransport();
    }

    private void CountdownTick(object? sender, EventArgs e)
    {
        _countdownValue--;
        if (_countdownValue <= 0)
        {
            EndCountdown();
            ActuallyStartRecording();
        }
        else
        {
            ApplyCountdownVisual();
        }
    }

    private void ApplyCountdownVisual()
    {
        CountdownDigit.Text = _countdownValue.ToString();
        MascotSpeech.Text = _countdownValue switch
        {
            3 => L10n.T("Count3"),
            2 => L10n.T("Count2"),
            1 => L10n.T("Count1"),
            _ => L10n.T("CountWait")
        };
    }

    private void EndCountdown()
    {
        _countdownTimer?.Stop();
        _countdownTimer = null;
        CountdownOverlay.Visibility = Visibility.Collapsed;
    }

    private void CancelCountdown()
    {
        if (_countdownTimer == null) return;
        EndCountdown();
        MascotSpeech.Text = L10n.T("MsgCancelled");
        UpdateTransport();
    }

    private void ActuallyStartRecording()
    {
        if (DeviceCombo.SelectedItem is not MMDevice device)
        {
            UpdateTransport();
            MascotSpeech.Text = L10n.T("MsgPickDevice");
            return;
        }

        var format = Mp3Toggle.IsChecked == true ? RecordingFormat.Mp3 : RecordingFormat.Wav;
        var ext = format == RecordingFormat.Mp3 ? "mp3" : "wav";
        var filename = $"CocoaRecorder_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
        var fullPath = IOPath.Combine(_saveFolder, filename);

        try
        {
            _recorder.Start(device, format, fullPath);
        }
        catch (Exception ex)
        {
            UpdateTransport();
            MascotSpeech.Text = $"{L10n.T("MsgStartFail")}\n{ex.Message}";
            return;
        }

        MascotSpeech.Text = Pick(L10n.StartMessages);
        UpdateTransport();
        UpdateFormatInfo();   // 이제 실제 샘플레이트를 알 수 있다
        _uiTimer.Start();
    }

    private void OnRecordingFinished(object? sender, RecordingFinishedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _uiTimer.Stop();
            ResetTimerLabels();
            UpdateTransport();

            if (e.Error != null)
            {
                MascotSpeech.Text = $"{L10n.T("MsgError")}\n{e.Error.Message}";
                return;
            }

            MascotSpeech.Text = Pick(L10n.StopMessages);
            RefreshRecordings();
        });
    }

    // =================== Visual state ===================
    /// <summary>
    /// 상태 하나가 히어로 색, 트랜스포트 두 버튼, 잠기는 컨트롤을 한꺼번에 결정한다.
    /// 시안의 3a~3d 프레임이 그대로 여기에 대응한다.
    /// </summary>
    private void UpdateTransport()
    {
        var state = State;
        bool locked = state != Transport.Idle;

        DeviceCombo.IsEnabled = !locked;
        WavToggle.IsEnabled = !locked;
        Mp3Toggle.IsEnabled = !locked;
        LoToggle.IsEnabled = !locked;
        MedToggle.IsEnabled = !locked;
        HiToggle.IsEnabled = !locked;
        UpdateCountdownVisual();

        HeroBackdrop.Background = (Brush)FindResource(state switch
        {
            Transport.Recording => "HeroRecBrush",
            Transport.Paused    => "HeroPauseBrush",
            _                   => "HeroBrush",
        });

        StatusLabel.Text = L10n.T(state switch
        {
            Transport.Countdown => "StatusCountdown",
            Transport.Recording => "StatusRecording",
            Transport.Paused    => "StatusPaused",
            _                   => "StatusIdle",
        });

        var night = (Brush)FindResource("NightBrush");
        var lilac = (Brush)FindResource("LilacBrush");

        switch (state)
        {
            case Transport.Countdown:
                PrimaryButton.Background = Brushes.Transparent;
                PrimaryButton.BorderBrush = lilac;
                PrimaryButton.Foreground = lilac;
                PrimaryDot.Fill = lilac;
                PrimaryText.Text = L10n.T("CancelBtn");
                SecondaryButton.Visibility = Visibility.Collapsed;
                break;

            case Transport.Recording:
                PrimaryButton.Background = (Brush)FindResource("PinkBrush");
                PrimaryButton.BorderBrush = Brushes.Transparent;
                PrimaryButton.Foreground = night;
                PrimaryDot.Fill = night;
                PrimaryText.Text = L10n.T("StopBtn");
                SecondaryButton.Visibility = Visibility.Visible;
                SecondaryText.Text = L10n.T("PauseBtn");
                SecondaryBars.Visibility = Visibility.Visible;
                SecondarySquare.Visibility = Visibility.Collapsed;
                break;

            case Transport.Paused:
                PrimaryButton.Background = (Brush)FindResource("AmberBrush");
                PrimaryButton.BorderBrush = Brushes.Transparent;
                PrimaryButton.Foreground = night;
                PrimaryDot.Fill = night;
                PrimaryText.Text = L10n.T("ResumeBtn");
                SecondaryButton.Visibility = Visibility.Visible;
                SecondaryText.Text = L10n.T("StopBtn");
                SecondaryBars.Visibility = Visibility.Collapsed;
                SecondarySquare.Visibility = Visibility.Visible;
                break;

            default:
                PrimaryButton.Background = (Brush)FindResource("PinkBrush");
                PrimaryButton.BorderBrush = Brushes.Transparent;
                PrimaryButton.Foreground = night;
                PrimaryDot.Fill = night;
                PrimaryText.Text = L10n.T("RecordStart");
                SecondaryButton.Visibility = Visibility.Collapsed;
                break;
        }

        DrawMascot(state switch
        {
            Transport.Countdown => MascotMood.Countdown,
            Transport.Recording => MascotMood.Recording,
            Transport.Paused    => MascotMood.Paused,
            _                   => MascotMood.Idle,
        });

        // 시안의 catAnim — 녹음일 때만 옆으로 꼬리를 살랑이고, 나머지는 제자리에서 떠다닌다.
        if (state == Transport.Recording) NightSky.Sway(MascotBob, 0.7);
        else NightSky.Drift(MascotBob, state switch
        {
            Transport.Countdown => 2.4,
            Transport.Paused    => 3.4,
            _                   => 3.0,
        });

        UpdateStatusDot();
        UpdateSizeLabel();
    }

    /// <summary>
    /// 상태 점은 상태마다 다르게 뛴다 — 시안의 twk · recPulse 키프레임을 그대로 옮겼다.
    /// 대기는 3초에 걸쳐 천천히, 카운트다운은 1초에 딱딱 끊어서, 녹음은 1.1초에 분홍 후광까지 같이.
    /// </summary>
    private void UpdateStatusDot()
    {
        StatusDot.BeginAnimation(UIElement.OpacityProperty, null);
        StatusDot.Effect = null;

        switch (State)
        {
            case Transport.Countdown:
                StatusDot.Background = (Brush)FindResource("AmberBrush");
                var steps = new DoubleAnimationUsingKeyFrames
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    Duration = TimeSpan.FromSeconds(1),
                };
                steps.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
                steps.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.18, KeyTime.FromPercent(0.5)));
                Timeline.SetDesiredFrameRate(steps, 20);
                StatusDot.BeginAnimation(UIElement.OpacityProperty, steps);
                break;

            case Transport.Recording:
                StatusDot.Background = (Brush)FindResource("PinkBrush");
                var glow = new DropShadowEffect
                {
                    Color = (Color)FindResource("PinkColor"),
                    ShadowDepth = 0,
                    BlurRadius = 10,
                    Opacity = 0.9,
                };
                StatusDot.Effect = glow;
                StatusDot.BeginAnimation(UIElement.OpacityProperty, Pulse(1.0, 0.35, 1.1));
                glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, Pulse(10, 22, 1.1));
                break;

            case Transport.Paused:
                StatusDot.Background = (Brush)FindResource("AmberBrush");
                StatusDot.Opacity = 1.0;
                break;

            default:
                StatusDot.Background = (Brush)FindResource("MintBrush");
                StatusDot.BeginAnimation(UIElement.OpacityProperty, Pulse(0.18, 1.0, 3));
                break;
        }
    }

    private static DoubleAnimation Pulse(double from, double to, double seconds)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds / 2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(anim, 30);
        return anim;
    }

    private void UpdateTimerLabels()
    {
        var ts = _recorder.Elapsed;
        TimerMin.Text = ((int)ts.TotalMinutes).ToString("D2");
        TimerSec.Text = ts.Seconds.ToString("D2");
        TimerCs.Text = "." + (ts.Milliseconds / 10).ToString("D2");
    }

    private void ResetTimerLabels()
    {
        TimerMin.Text = "00";
        TimerSec.Text = "00";
        TimerCs.Text = ".00";
    }

    /// <summary>
    /// 지금까지 담긴 양. WAV는 실제로 쓴 바이트, MP3는 프리셋의 평균 비트레이트로 어림한다
    /// (임시 WAV로 받아 두었다가 정지할 때 변환하므로 진행 중에는 최종 크기를 알 수 없다).
    /// </summary>
    private void UpdateSizeLabel()
    {
        if (!_recorder.IsRecording)
        {
            SizeText.Text = "0.0 MB";
            return;
        }

        long bytes;
        if (Mp3Toggle.IsChecked == true)
        {
            int kbps = _recorder.Mp3Quality switch
            {
                Mp3Quality.Low => 150,
                Mp3Quality.High => 245,
                _ => 190,
            };
            bytes = (long)(_recorder.Elapsed.TotalSeconds * kbps * 1000 / 8);
        }
        else
        {
            bytes = _recorder.CapturedBytes;
        }
        SizeText.Text = FormatBytes(bytes);
    }

    // =================== Waveform / level meter ===================
    private void OnLevelChanged(object? sender, float peak)
    {
        // Capture thread — keep this lock-light.
        lock (_peakLock)
        {
            if (peak > _peakAccumulator) _peakAccumulator = peak;
        }
    }

    private string Pick(string[] pool) => pool[_rng.Next(pool.Length)];

    private int _idleSpeechTick;

    private void TickWaveform()
    {
        var state = State;
        bool active = state == Transport.Recording;
        var low = (Brush)FindResource("WaveLowBrush");
        var mid = (Brush)FindResource("LilacBrush");
        var high = (Brush)FindResource("MintBrush");
        var levelOn = (Brush)FindResource("MintBrush");
        var levelWarm = (Brush)FindResource("AmberBrush");
        var emptyFill = (Brush)FindResource("WellBrush");

        double now = Environment.TickCount;
        double tIdle = now / 420.0;
        const double maxBarHeight = 96.0;   // 시안의 wave(colors, 96)

        // Pull the peak that capture thread has been accumulating since last tick.
        double currentPeak;
        lock (_peakLock)
        {
            currentPeak = _peakAccumulator;
            _peakAccumulator = 0f;
        }

        if (active)
        {
            // Gentle gamma curve so quiet audio is still readable on the meter.
            double shaped = Math.Pow(Math.Min(1.0, currentPeak), 0.55);
            _peakHistory[_peakWriteIdx] = shaped;
            _peakWriteIdx = (_peakWriteIdx + 1) % BarCount;

            // smooth current level for the cell meter (attack fast, release slow)
            double target = shaped;
            if (target > _smoothedLevel) _smoothedLevel = target;
            else _smoothedLevel = _smoothedLevel * 0.78 + target * 0.22;
        }
        else
        {
            _smoothedLevel *= 0.6;
        }

        // 녹음이 아닐 때는 시안의 flatWave처럼 낮고 고른 선으로 가라앉는다.
        // 상태마다 선의 높이·색·투명도가 달라서 소리가 들어오고 있는지 한눈에 갈린다.
        var (restFill, restHeight, waveOpacity) = state switch
        {
            Transport.Recording => (low, 0.0, 0.95),
            Transport.Paused    => ((Brush)FindResource("LineBrush"), 10.0, 0.70),
            Transport.Countdown => ((Brush)FindResource("LineSoftBrush"), 8.0, 0.50),
            _                   => ((Brush)FindResource("WellBrush"), 6.0, 0.60),
        };
        WaveformHost.Opacity = waveOpacity;

        for (int i = 0; i < BarCount; i++)
        {
            if (active)
            {
                int idx = (_peakWriteIdx + i) % BarCount;
                double h = Math.Max(0.04, Math.Min(1.0, _peakHistory[idx]));
                _bars[i].Height = Math.Max(4, h * maxBarHeight);
                _bars[i].Fill = h < 0.32 ? low : h < 0.62 ? mid : high;
            }
            else
            {
                double drift = state == Transport.Idle ? Math.Abs(Math.Sin(tIdle + i * 0.4)) * 2.0 : i % 3;
                _bars[i].Height = restHeight + drift;
                _bars[i].Fill = restFill;
            }
        }

        int litCount = (int)Math.Round(_smoothedLevel * LevelCellCount);
        for (int i = 0; i < LevelCellCount; i++)
        {
            bool lit = i < litCount;
            // 시안의 level(): 아래 아홉 칸은 민트, 그 위는 앰버. 세 번째 색은 쓰지 않는다.
            Brush color = i < 9 ? levelOn : levelWarm;
            _levelCells[i].Fill = lit ? color : emptyFill;
        }

        // 녹음 중 코코아는 두 축으로 움직인다 — 가로는 시안의 tailSway 애니메이션이 맡고,
        // 세로는 여기서 소리 크기에 맞춰 직접 밀어 준다. Sway가 Y의 애니메이션을 벗겨 두므로
        // 이 대입이 먹힌다 (다른 상태에서는 Drift가 Y를 쥐고 있어 손대지 않는다).
        if (active)
        {
            double lift = -Math.Min(7.0, _smoothedLevel * 9.0);
            MascotBob.Y = MascotBob.Y * 0.7 + lift * 0.3;
        }

        // Periodically rotate idle chatter (~ every 18s) so the mascot feels alive.
        if (state == Transport.Idle)
        {
            _idleSpeechTick++;
            if (_idleSpeechTick >= 300) // 300 * 60ms ≈ 18s
            {
                _idleSpeechTick = 0;
                MascotSpeech.Text = Pick(L10n.IdleMessages);
            }
        }
        else
        {
            _idleSpeechTick = 0;
        }
    }

    // =================== Recordings library ===================
    private void RefreshRecordings()
    {
        // RecordingItem instances get replaced on refresh; stop playback so the
        // player stays bound to a live item.
        StopPlayback();
        _recordings.Clear();
        if (!Directory.Exists(_saveFolder))
        {
            UpdateLibraryUi();
            return;
        }

        // 테이프 색은 시안의 네 가지 신호색을 줄 순서대로 돌려 쓴다.
        // (이름 해시로 뽑으면 비슷한 이름이 같은 색에 몰려 목록이 단색으로 보인다.)
        var palette = new Brush[]
        {
            (Brush)FindResource("PinkSoftBrush"),
            (Brush)FindResource("MintBrush"),
            (Brush)FindResource("AmberBrush"),
            (Brush)FindResource("LilacBrush"),
        };

        var files = new DirectoryInfo(_saveFolder)
            .EnumerateFiles()
            .Where(fi => fi.Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
                      || fi.Extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(fi => fi.LastWriteTime)
            .Take(40)
            .ToList();

        for (int i = 0; i < files.Count; i++)
        {
            var fi = files[i];
            var fmt = fi.Extension.TrimStart('.').ToUpperInvariant();
            var dur = FormatDuration(AudioRecorder.TryGetDuration(fi.FullName));
            _recordings.Add(new RecordingItem
            {
                Name = fi.Name,
                Path = fi.FullName,
                Meta = $"{fmt} · {dur} · {FormatBytes(fi.Length)} · {L10n.FormatMetaDate(fi.LastWriteTime)}",
                TapeColor = palette[i % palette.Length]
            });
        }
        UpdateLibraryUi();
    }

    private static string FormatDuration(TimeSpan? d)
    {
        if (d == null) return "?:??";
        var t = d.Value;
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
        return $"{t.Minutes}:{t.Seconds:D2}";
    }

    // =================== Rename + drag export ===================
    private string? _renameOriginal;
    private Point _dragStart;
    private RecordingItem? _dragCandidate;

    private void RecordingName_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is FrameworkElement fe && fe.DataContext is RecordingItem item)
        {
            _renameOriginal = item.Name;
            item.IsRenaming = true;
            e.Handled = true;
        }
    }

    private void RenameBox_Loaded(object sender, RoutedEventArgs e)
    {
        FocusRenameBox(sender as TextBox);
    }

    private void RenameBox_VisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.IsVisible) FocusRenameBox(tb);
    }

    private static void FocusRenameBox(TextBox? tb)
    {
        if (tb == null) return;
        // Defer to give WPF time to attach the visual.
        tb.Dispatcher.BeginInvoke(new Action(() =>
        {
            tb.Focus();
            Keyboard.Focus(tb);
            int dot = tb.Text.LastIndexOf('.');
            if (dot > 0) tb.Select(0, dot);
            else tb.SelectAll();
        }), DispatcherPriority.Input);
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not RecordingItem item) return;
        if (e.Key == Key.Escape)
        {
            item.Name = _renameOriginal ?? item.Name;
            item.IsRenaming = false;
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Keyboard.ClearFocus(); // triggers LostFocus -> commit
            e.Handled = true;
        }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is RecordingItem item && item.IsRenaming)
        {
            CommitRename(item);
        }
    }

    private void CommitRename(RecordingItem item)
    {
        item.IsRenaming = false;
        var trimmed = item.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            item.Name = _renameOriginal ?? item.Name;
            return;
        }

        var oldExt = IOPath.GetExtension(item.Path);
        if (!trimmed.EndsWith(oldExt, StringComparison.OrdinalIgnoreCase))
            trimmed += oldExt;

        var dir = IOPath.GetDirectoryName(item.Path);
        if (string.IsNullOrEmpty(dir)) return;
        var newPath = IOPath.Combine(dir, trimmed);

        if (string.Equals(newPath, item.Path, StringComparison.OrdinalIgnoreCase))
        {
            item.Name = trimmed; // canonicalise (extension may have changed)
            return;
        }
        if (File.Exists(newPath))
        {
            MascotSpeech.Text = L10n.T("MsgNameExists");
            item.Name = _renameOriginal ?? item.Name;
            return;
        }

        try
        {
            if (_playingItem == item) StopPlayback();
            File.Move(item.Path, newPath);
            RefreshRecordings();
        }
        catch (Exception ex)
        {
            MascotSpeech.Text = $"{L10n.T("MsgRenameFail")}\n{ex.Message}";
            item.Name = _renameOriginal ?? item.Name;
        }
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is RecordingItem item && !item.IsRenaming)
        {
            _dragStart = e.GetPosition(null);
            _dragCandidate = item;
        }
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 드래그로 빠져나가지 않은 진짜 클릭일 때만 선택을 토글한다
        // (드래그가 시작되면 Card_MouseMove가 _dragCandidate를 비운다).
        if (_dragCandidate != null && sender is FrameworkElement fe
            && fe.DataContext is RecordingItem item && !item.IsRenaming)
        {
            SelectRecording(item);
        }
        _dragCandidate = null;
    }

    /// <summary>
    /// 한 번에 한 줄만 열린다 — 그 줄에서만 재생 · 폴더 · 삭제 버튼이 나온다.
    /// 이미 열린 줄을 다시 눌러도 아무 일도 일어나지 않는다 (다시 튀어나오지 않게).
    /// 닫으려면 다른 줄을 고르면 된다.
    /// </summary>
    private void SelectRecording(RecordingItem item)
    {
        if (item.IsSelected) return;
        foreach (var other in _recordings) other.IsSelected = false;
        item.IsSelected = true;
    }

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate == null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(null);
        var dx = current.X - _dragStart.X;
        var dy = current.Y - _dragStart.Y;
        if (Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance) return;

        var item = _dragCandidate;
        _dragCandidate = null;
        if (!File.Exists(item.Path)) return;

        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        }
        catch { /* ignore drag aborts */ }
    }

    private void UpdateLibraryUi()
    {
        // 같은 RecordingItem 인스턴스를 둘이 나눠 쓰므로, 어느 화면에서 골라도 선택 상태가 함께 움직인다.
        _recent.Clear();
        foreach (var item in _recordings.Take(RecentCount)) _recent.Add(item);

        RecordingCountText.Text = string.Format(L10n.T("CountFmt"), _recordings.Count);

        int hidden = _recordings.Count - _recent.Count;
        RecentMoreText.Text = hidden > 0 ? string.Format(L10n.T("RecentMore"), hidden) : string.Empty;
        RecentMoreText.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;

        var empty = _recordings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Visibility = empty;
        LibraryEmptyText.Visibility = empty;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F1} GB";
    }

    private WaveOutEvent? _player;
    private AudioFileReader? _playerReader;
    private RecordingItem? _playingItem;
    private DispatcherTimer? _playerTimer;

    private void PlayRecording_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string path) return;
        if (!File.Exists(path)) return;

        var item = _recordings.FirstOrDefault(r => r.Path == path);
        if (item == null) return;

        if (_playingItem == item && _player != null)
        {
            // Same card — toggle pause/resume.
            if (_player.PlaybackState == PlaybackState.Playing) _player.Pause();
            else _player.Play();
            return;
        }

        StopPlayback();
        StartPlayback(item);
    }

    private void StartPlayback(RecordingItem item)
    {
        try
        {
            _playerReader = new AudioFileReader(item.Path);
            _player = new WaveOutEvent();
            _player.Init(_playerReader);
            _player.PlaybackStopped += OnPlaybackStopped;
            _player.Play();
            _playingItem = item;
            item.IsPlaying = true;
            item.Progress = 0;

            _playerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _playerTimer.Tick += UpdatePlayerProgress;
            _playerTimer.Start();
            MascotSpeech.Text = L10n.T("MsgPlaying");
        }
        catch (Exception ex)
        {
            MascotSpeech.Text = $"{L10n.T("MsgPlayFail")}\n{ex.Message}";
            StopPlayback();
            // Fall back to the OS default handler so the user is not stranded.
            try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }); } catch { }
        }
    }

    private void UpdatePlayerProgress(object? sender, EventArgs e)
    {
        if (_playerReader == null || _playingItem == null) return;
        double total = _playerReader.TotalTime.TotalSeconds;
        if (total <= 0) return;
        _playingItem.Progress = Math.Clamp(_playerReader.CurrentTime.TotalSeconds / total, 0, 1);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(StopPlayback));
    }

    private void StopPlayback()
    {
        _playerTimer?.Stop();
        if (_playerTimer != null) _playerTimer.Tick -= UpdatePlayerProgress;
        _playerTimer = null;

        if (_player != null)
        {
            _player.PlaybackStopped -= OnPlaybackStopped;
            try { _player.Stop(); } catch { }
            try { _player.Dispose(); } catch { }
            _player = null;
        }

        try { _playerReader?.Dispose(); } catch { }
        _playerReader = null;

        if (_playingItem != null)
        {
            _playingItem.IsPlaying = false;
            _playingItem.Progress = 0;
            _playingItem = null;
        }
    }

    private void RevealRecording_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string path) return;
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
                OpenSaveFolder();
        }
        catch (Exception ex)
        {
            MascotSpeech.Text = $"{L10n.T("MsgFolderOpenFail")}\n{ex.Message}";
        }
    }

    private void DeleteRecording_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string path && File.Exists(path))
        {
            if (_playingItem?.Path == path) StopPlayback();
            try
            {
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                RefreshRecordings();
            }
            catch (Exception ex) { MascotSpeech.Text = $"{L10n.T("MsgDeleteFail")}\n{ex.Message}"; }
        }
    }

    private void DrawMascot(MascotMood mood) => Mascot.Draw(MascotCanvas, mood, CatCell);

    private sealed class AppPreferences
    {
        private static readonly string SettingsPath = IOPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CocoaRecorder",
            "settings.json");

        public const int DefaultCountdownSeconds = 3;
        public const int MaxCountdownSeconds = 10;

        public int CountdownSeconds { get; set; } = DefaultCountdownSeconds;

        /// <summary>카운트다운 길이를 고를 수 없던 버전이 쓰던 값. 한 번 옮겨 담고 나면 더 쓰지 않는다.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? CountdownEnabled { get; set; }

        public string? SaveFolder { get; set; }

        public AppPreferences()
        {
        }

        public static AppPreferences Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new AppPreferences();
                var loaded = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(SettingsPath))
                             ?? new AppPreferences();
                if (loaded.CountdownEnabled is bool wasEnabled)
                {
                    loaded.CountdownSeconds = wasEnabled ? DefaultCountdownSeconds : 0;
                    loaded.CountdownEnabled = null;
                }
                loaded.CountdownSeconds = Math.Clamp(loaded.CountdownSeconds, 0, MaxCountdownSeconds);
                return loaded;
            }
            catch
            {
                return new AppPreferences();
            }
        }

        public void Save()
        {
            try
            {
                var directory = IOPath.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            }
            catch
            {
                // Preferences should never prevent recording.
            }
        }
    }
}

public class WaveformBar : INotifyPropertyChanged
{
    private double _height;
    private Brush _fill = Brushes.Transparent;

    public double Height
    {
        get => _height;
        set { if (_height != value) { _height = value; OnChanged(); } }
    }

    public Brush Fill
    {
        get => _fill;
        set { if (!ReferenceEquals(_fill, value)) { _fill = value; OnChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class LevelCell : INotifyPropertyChanged
{
    private Brush _fill = Brushes.Transparent;

    public Brush Fill
    {
        get => _fill;
        set { if (!ReferenceEquals(_fill, value)) { _fill = value; OnChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RecordingItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    public required string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnChanged(); } }
    }

    public required string Path { get; init; }
    public required string Meta { get; init; }
    public required Brush TapeColor { get; init; }

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set { if (_isPlaying != value) { _isPlaying = value; OnChanged(); } }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set { if (Math.Abs(_progress - value) > 0.0005) { _progress = value; OnChanged(); } }
    }

    private bool _isRenaming;
    public bool IsRenaming
    {
        get => _isRenaming;
        set { if (_isRenaming != value) { _isRenaming = value; OnChanged(); } }
    }

    /// <summary>이 줄이 열려 동작 버튼을 내보이고 있는지.</summary>
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
