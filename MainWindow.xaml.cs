using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private readonly AudioRecorder _recorder = new();
    private readonly AppPreferences _preferences = AppPreferences.Load();
    private string _saveFolder = string.Empty;

    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _waveformTimer;
    private readonly DispatcherTimer _blinkTimer;
    private bool _blinkOn = true;

    private readonly Random _rng = new();
    private readonly ObservableCollection<WaveformBar> _bars = new();
    private readonly ObservableCollection<LevelCell> _levelCells = new();
    private readonly ObservableCollection<RecordingItem> _recordings = new();
    private const int BarCount = 56;
    private const int LevelCellCount = 18;

    private readonly object _peakLock = new();
    private float _peakAccumulator;
    private readonly double[] _peakHistory = new double[BarCount];
    private int _peakWriteIdx;
    private double _smoothedLevel;
    private double _mascotBobTarget;

    public MainWindow()
    {
        InitializeComponent();
        UpdateLangToggle();
        L10n.LanguageChanged += OnLanguageChanged;

        CountdownToggle.IsChecked = _preferences.CountdownEnabled;
        CountdownToggle.Checked += CountdownToggle_Changed;
        CountdownToggle.Unchecked += CountdownToggle_Changed;

        _saveFolder = ResolveInitialSaveFolder();
        Directory.CreateDirectory(_saveFolder);
        FolderText.Text = _saveFolder;
        LibraryFolderHint.Text = _saveFolder;

        var mintFill = (Brush)FindResource("MintDeepBrush");
        for (int i = 0; i < BarCount; i++)
            _bars.Add(new WaveformBar { Height = 12, Fill = mintFill });
        WaveformHost.ItemsSource = _bars;

        var emptyCell = (Brush)FindResource("CreamDeepBrush");
        for (int i = 0; i < LevelCellCount; i++)
            _levelCells.Add(new LevelCell { Fill = emptyCell });
        LevelMeter.ItemsSource = _levelCells;

        RecordingsList.ItemsSource = _recordings;

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _uiTimer.Tick += (_, _) => UpdateTimerLabels();

        _waveformTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _waveformTimer.Tick += (_, _) => TickWaveform();

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _blinkTimer.Tick += (_, _) => { _blinkOn = !_blinkOn; UpdateStatusDot(); };

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
            _blinkTimer.Stop();
            StopPlayback();
            _recorder.Dispose();
        };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Don't hijack typing inside text inputs (renames, future search box, etc.)
        if (Keyboard.FocusedElement is TextBox) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        switch (e.Key)
        {
            case Key.Space:
                if (_countdownTimer != null)
                {
                    StopButton_Click(this, new RoutedEventArgs());
                }
                else if (_recorder.IsRecording)
                {
                    StopButton_Click(this, new RoutedEventArgs());
                }
                else if (RecordButton.IsEnabled)
                {
                    RecordButton_Click(this, new RoutedEventArgs());
                }
                e.Handled = true;
                break;

            case Key.P:
                if (_recorder.IsRecording)
                {
                    PauseButton_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                break;

            case Key.O when ctrl:
                OpenSaveFolder();
                e.Handled = true;
                break;

            case Key.F5:
                RefreshRecordings();
                e.Handled = true;
                break;
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

        DrawMascot(MascotMood.Idle);
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

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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
        if (_countdownTimer != null)
        {
            StatusLabel.Text = L10n.T("StatusCountdown");
        }
        else if (_recorder.IsRecording)
        {
            StatusLabel.Text = _recorder.IsPaused ? L10n.T("StatusPaused") : L10n.T("StatusRecording");
            RecordButtonText.Text = L10n.T("Recording");
        }
        else
        {
            StatusLabel.Text = L10n.T("StatusIdle");
            RecordButtonText.Text = L10n.T("RecordStart");
            MascotSpeech.Text = Pick(L10n.IdleMessages);
        }

        UpdateFormatInfo();
        RefreshRecordings(); // 카드 Meta의 날짜 표기 언어 갱신
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
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

    // =================== Format toggle ===================
    private void Format_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateFormatInfo();
    }

    private void Bitrate_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (LoToggle.IsChecked == true) _recorder.Mp3Quality = Mp3Quality.Low;
        else if (HiToggle.IsChecked == true) _recorder.Mp3Quality = Mp3Quality.High;
        else _recorder.Mp3Quality = Mp3Quality.Medium;
        UpdateFormatInfo();
    }

    private void CountdownToggle_Changed(object sender, RoutedEventArgs e)
    {
        _preferences.CountdownEnabled = CountdownToggle.IsChecked == true;
        _preferences.Save();
    }

    private void UpdateFormatInfo()
    {
        bool isMp3 = Mp3Toggle.IsChecked == true;
        var fmt = isMp3 ? "MP3" : "WAV";
        var visibility = isMp3 ? Visibility.Visible : Visibility.Collapsed;
        BitrateLabel.Visibility = visibility;
        BitrateGroup.Visibility = visibility;

        if (isMp3)
        {
            string q = LoToggle.IsChecked == true ? "LO"
                     : HiToggle.IsChecked == true ? "HI" : "MED";
            FormatInfoText.Text = $"MP3 {q} · {L10n.T("SystemSound")}";
        }
        else
        {
            FormatInfoText.Text = $"WAV · {L10n.T("SystemSound")}";
        }
    }

    // =================== Transport ===================
    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder.IsRecording) return;
        StartRecording();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_countdownTimer != null)
        {
            CancelCountdownIfRunning();
            MascotSpeech.Text = L10n.T("MsgCancelled");
            return;
        }
        if (!_recorder.IsRecording) return;
        MascotSpeech.Text = L10n.T("MsgSaving");
        _recorder.Stop();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_recorder.IsRecording) return;
        if (_recorder.IsPaused) _recorder.Resume();
        else _recorder.Pause();
        UpdatePauseVisual();
    }

    private void UpdatePauseVisual()
    {
        bool paused = _recorder.IsPaused;
        PauseIconGroup.Visibility = paused ? Visibility.Collapsed : Visibility.Visible;
        ResumeIconGroup.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;

        if (paused)
        {
            StatusLabel.Text = L10n.T("StatusPaused");
            MascotSpeech.Text = L10n.T("MsgPaused");
            DrawMascot(MascotMood.Paused);
            _blinkTimer.Stop();
            _blinkOn = true;
        }
        else
        {
            StatusLabel.Text = L10n.T("StatusRecording");
            MascotSpeech.Text = L10n.T("MsgResumed");
            DrawMascot(MascotMood.Recording);
            _blinkTimer.Start();
        }
        UpdateStatusDot();
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
        if (CountdownToggle.IsChecked == true)
            BeginCountdown(3);
        else
            ActuallyStartRecording();
    }

    private void BeginCountdown(int from)
    {
        _countdownValue = from;
        DeviceCombo.IsEnabled = false;
        WavToggle.IsEnabled = false;
        Mp3Toggle.IsEnabled = false;
        RecordButton.IsEnabled = false;
        CountdownToggle.IsEnabled = false;
        StopButton.IsEnabled = true; // Lets the user cancel mid-count via the stop button or Space.
        PauseButton.Visibility = Visibility.Collapsed;

        ApplyCountdownVisual();

        _countdownTimer?.Stop();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _countdownTimer.Tick += CountdownTick;
        _countdownTimer.Start();
    }

    private void CountdownTick(object? sender, EventArgs e)
    {
        _countdownValue--;
        if (_countdownValue <= 0)
        {
            _countdownTimer?.Stop();
            _countdownTimer = null;
            EndCountdownVisual();
            ActuallyStartRecording();
        }
        else
        {
            ApplyCountdownVisual();
        }
    }

    private void ApplyCountdownVisual()
    {
        TimerMin.Text = _countdownValue.ToString();
        TimerSec.Visibility = Visibility.Collapsed;
        TimerColon.Visibility = Visibility.Collapsed;
        TimerCs.Visibility = Visibility.Collapsed;
        StatusLabel.Text = L10n.T("StatusCountdown");
        MascotSpeech.Text = _countdownValue switch
        {
            3 => L10n.T("Count3"),
            2 => L10n.T("Count2"),
            1 => L10n.T("Count1"),
            _ => "..."
        };
    }

    private void EndCountdownVisual()
    {
        TimerSec.Visibility = Visibility.Visible;
        TimerColon.Visibility = Visibility.Visible;
        TimerCs.Visibility = Visibility.Visible;
        ResetTimerLabels();
    }

    private void ActuallyStartRecording()
    {
        if (DeviceCombo.SelectedItem is not MMDevice device)
        {
            // Re-enable controls and bail.
            SetRecordingVisual(false);
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
            SetRecordingVisual(false);
            MascotSpeech.Text = $"{L10n.T("MsgStartFail")}\n{ex.Message}";
            return;
        }

        SetRecordingVisual(true);
        _uiTimer.Start();
        _blinkTimer.Start();
    }

    private void CancelCountdownIfRunning()
    {
        if (_countdownTimer != null)
        {
            _countdownTimer.Stop();
            _countdownTimer = null;
            EndCountdownVisual();
            SetRecordingVisual(false);
        }
    }

    private void OnRecordingFinished(object? sender, RecordingFinishedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            SetRecordingVisual(false);
            ResetTimerLabels();

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
    private void SetRecordingVisual(bool recording)
    {
        DeviceCombo.IsEnabled = !recording;
        WavToggle.IsEnabled = !recording;
        Mp3Toggle.IsEnabled = !recording;
        LoToggle.IsEnabled = !recording;
        MedToggle.IsEnabled = !recording;
        HiToggle.IsEnabled = !recording;
        RecordButton.IsEnabled = !recording;
        CountdownToggle.IsEnabled = !recording;
        StopButton.IsEnabled = recording;
        PauseButton.IsEnabled = recording;
        PauseButton.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        PauseIconGroup.Visibility = Visibility.Visible;
        ResumeIconGroup.Visibility = Visibility.Collapsed;

        if (recording)
        {
            StatusLabel.Text = L10n.T("StatusRecording");
            RecordButtonText.Text = L10n.T("Recording");
            MascotSpeech.Text = Pick(L10n.StartMessages);
            DrawMascot(MascotMood.Recording);
        }
        else
        {
            StatusLabel.Text = L10n.T("StatusIdle");
            RecordButtonText.Text = L10n.T("RecordStart");
            DrawMascot(MascotMood.Idle);
            _blinkTimer.Stop();
            _uiTimer.Stop();
            _blinkOn = true;
        }
        UpdateStatusDot();
    }

    private void UpdateStatusDot()
    {
        if (_recorder.IsRecording)
        {
            if (_recorder.IsPaused)
                StatusDot.Background = (Brush)FindResource("LilacDeepBrush");
            else
                StatusDot.Background = _blinkOn
                    ? (Brush)FindResource("AccentDeepBrush")
                    : (Brush)FindResource("AccentSoftBrush");
        }
        else
        {
            StatusDot.Background = (Brush)FindResource("InkSoftBrush");
        }
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
        bool active = _recorder.IsRecording && !_recorder.IsPaused;
        var mintFill = (Brush)FindResource("MintDeepBrush");
        var goldFill = (Brush)FindResource("GoldBrush");
        var coralFill = (Brush)FindResource("AccentDeepBrush");
        var emptyFill = (Brush)FindResource("CreamDeepBrush");

        double now = Environment.TickCount;
        double tIdle = now / 420.0;
        const double maxBarHeight = 110.0;

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

        for (int i = 0; i < BarCount; i++)
        {
            double h;
            if (active)
            {
                int idx = (_peakWriteIdx + i) % BarCount;
                double p = _peakHistory[idx];
                h = Math.Max(0.04, Math.Min(1.0, p));
            }
            else
            {
                h = 0.12 + Math.Abs(Math.Sin(tIdle + i * 0.4)) * 0.18;
            }

            _bars[i].Height = Math.Max(4, h * maxBarHeight);
            _bars[i].Fill = h < 0.4 ? mintFill : (h < 0.75 ? goldFill : coralFill);
        }

        int litCount = (int)Math.Round(_smoothedLevel * LevelCellCount);
        for (int i = 0; i < LevelCellCount; i++)
        {
            bool lit = i < litCount;
            Brush color = i < 12 ? mintFill : (i < 15 ? goldFill : coralFill);
            _levelCells[i].Fill = lit ? color : emptyFill;
        }

        // Mascot bob — react to audio while recording, gentle breathing while idle.
        if (active)
        {
            _mascotBobTarget = -Math.Min(7.0, _smoothedLevel * 9.0);
        }
        else if (_recorder.IsRecording && _recorder.IsPaused)
        {
            _mascotBobTarget = 0;
        }
        else
        {
            _mascotBobTarget = Math.Sin(now / 900.0) * 1.8;
        }
        MascotBob.Y = MascotBob.Y * 0.7 + _mascotBobTarget * 0.3;

        // Status dot pumps when actively recording.
        double pumpTarget = active ? 1.0 + 0.18 * Math.Abs(Math.Sin(now / 220.0)) : 1.0;
        StatusDotScale.ScaleX = StatusDotScale.ScaleX * 0.7 + pumpTarget * 0.3;
        StatusDotScale.ScaleY = StatusDotScale.ScaleX;

        // Periodically rotate idle chatter (~ every 18s) so the mascot feels alive.
        if (!_recorder.IsRecording && _countdownTimer == null)
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

        var palette = new Brush[]
        {
            (Brush)FindResource("CoralBrush"),
            (Brush)FindResource("MintBrush"),
            (Brush)FindResource("LilacBrush"),
            (Brush)FindResource("GoldBrush"),
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
                TapeColor = palette[StableHash(fi.Name) % palette.Length]
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
        _dragCandidate = null;
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

    private static int StableHash(string s)
    {
        // String.GetHashCode is randomized per-process; this keeps the
        // tape color attached to the same filename across launches.
        unchecked
        {
            int h = 5381;
            foreach (var c in s) h = h * 33 ^ c;
            return h & 0x7fffffff;
        }
    }

    private void UpdateLibraryUi()
    {
        RecordingCountText.Text = string.Format(L10n.T("CountFmt"), _recordings.Count);
        EmptyStateText.Visibility = _recordings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private void DrawMascot(MascotMood mood) => Mascot.Draw(MascotCanvas, mood);

    private sealed class AppPreferences
    {
        private static readonly string SettingsPath = IOPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CocoaRecorder",
            "settings.json");

        public bool CountdownEnabled { get; set; } = true;
        public string? SaveFolder { get; set; }

        public AppPreferences()
        {
        }

        public static AppPreferences Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new AppPreferences();
                return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(SettingsPath))
                       ?? new AppPreferences();
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
