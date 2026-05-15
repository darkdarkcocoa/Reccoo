using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
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
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace Reccoo;

public partial class MainWindow : Window
{
    private readonly AudioRecorder _recorder = new();
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

    public MainWindow()
    {
        InitializeComponent();

        _saveFolder = IOPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "Reccoo");
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
            MascotSpeech.Text = $"폴더 열기 실패ㅠ\n{ex.Message}";
        }
    }

    private bool _closeAfterStop;
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_recorder.IsRecording || _closeAfterStop) return;
        e.Cancel = true;
        _closeAfterStop = true;
        MascotSpeech.Text = "마무리 중...\n잠깐만!";
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
            MascotSpeech.Text = $"장치 로드 실패ㅠ\n{ex.Message}";
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

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    // =================== Folder picker ===================
    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "저장 폴더 선택",
            InitialDirectory = _saveFolder
        };
        if (dialog.ShowDialog() == true)
        {
            _saveFolder = dialog.FolderName;
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

    private void UpdateFormatInfo()
    {
        var fmt = WavToggle.IsChecked == true ? "WAV" : "MP3";
        FormatInfoText.Text = $"{fmt} · 시스템 사운드";
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
            MascotSpeech.Text = "취소했어!\n다시 누르면 시작~";
            return;
        }
        if (!_recorder.IsRecording) return;
        MascotSpeech.Text = "저장 중...\n잠깐만!";
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
            StatusLabel.Text = "‖ 일시정지";
            MascotSpeech.Text = "잠깐 멈췄어!\n준비되면 재개~";
            DrawMascot(MascotMood.Paused);
            _blinkTimer.Stop();
            _blinkOn = true;
        }
        else
        {
            StatusLabel.Text = "● 녹음 중...";
            MascotSpeech.Text = "♪ 다시 들어볼게!";
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
            MascotSpeech.Text = "장치를 먼저\n선택해줘!";
            return;
        }
        BeginCountdown(3);
    }

    private void BeginCountdown(int from)
    {
        _countdownValue = from;
        DeviceCombo.IsEnabled = false;
        WavToggle.IsEnabled = false;
        Mp3Toggle.IsEnabled = false;
        RecordButton.IsEnabled = false;
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
        StatusLabel.Text = "♪ 곧 시작...";
        MascotSpeech.Text = _countdownValue switch
        {
            3 => "셋!",
            2 => "둘!",
            1 => "하나!",
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
            MascotSpeech.Text = "장치를 먼저\n선택해줘!";
            return;
        }

        var format = Mp3Toggle.IsChecked == true ? RecordingFormat.Mp3 : RecordingFormat.Wav;
        var ext = format == RecordingFormat.Mp3 ? "mp3" : "wav";
        var filename = $"Reccoo_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
        var fullPath = IOPath.Combine(_saveFolder, filename);

        try
        {
            _recorder.Start(device, format, fullPath);
        }
        catch (Exception ex)
        {
            SetRecordingVisual(false);
            MascotSpeech.Text = $"시작 실패ㅠ\n{ex.Message}";
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
                MascotSpeech.Text = $"오류ㅠ\n{e.Error.Message}";
                return;
            }

            MascotSpeech.Text = "잘 저장됐어!\n또 녹음할까?";
            RefreshRecordings();
        });
    }

    // =================== Visual state ===================
    private void SetRecordingVisual(bool recording)
    {
        DeviceCombo.IsEnabled = !recording;
        WavToggle.IsEnabled = !recording;
        Mp3Toggle.IsEnabled = !recording;
        RecordButton.IsEnabled = !recording;
        StopButton.IsEnabled = recording;
        PauseButton.IsEnabled = recording;
        PauseButton.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        PauseIconGroup.Visibility = Visibility.Visible;
        ResumeIconGroup.Visibility = Visibility.Collapsed;

        if (recording)
        {
            StatusLabel.Text = "● 녹음 중...";
            RecordButtonText.Text = "녹음 중...";
            MascotSpeech.Text = "♪ 잘 들리고 있어!\n좋은 소리야~";
            DrawMascot(MascotMood.Recording);
        }
        else
        {
            StatusLabel.Text = "○ 대기 중";
            RecordButtonText.Text = "녹음 시작";
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
                Meta = $"{fmt} · {dur} · {FormatBytes(fi.Length)} · {fi.LastWriteTime:M월 d일 HH:mm}",
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
        RecordingCountText.Text = $" · {_recordings.Count}개";
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
            MascotSpeech.Text = "재생 중!\n♪~";
        }
        catch (Exception ex)
        {
            MascotSpeech.Text = $"재생 실패ㅠ\n{ex.Message}";
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
            catch (Exception ex) { MascotSpeech.Text = $"삭제 실패ㅠ\n{ex.Message}"; }
        }
    }

    // =================== Mascot pixel sprite ===================
    private enum MascotMood { Idle, Recording, Paused }

    private void DrawMascot(MascotMood mood)
    {
        MascotCanvas.Children.Clear();
        const int cell = 4;
        Brush ink   = (Brush)FindResource("InkDarkBrush");
        Brush dark  = (Brush)FindResource("InkBrush");
        Brush white = (Brush)FindResource("PaperBrush");
        Brush body  = mood switch
        {
            MascotMood.Recording => (Brush)FindResource("AccentBrush"),
            MascotMood.Paused    => (Brush)FindResource("LilacBrush"),
            _                    => (Brush)FindResource("MintBrush"),
        };
        Brush cheek = (Brush)FindResource("CoralBrush");
        Brush gold  = (Brush)FindResource("GoldBrush");

        void Put(int x, int y, Brush c)
        {
            var r = new WpfRectangle
            {
                Width = cell,
                Height = cell,
                Fill = c,
                SnapsToDevicePixels = true,
            };
            Canvas.SetLeft(r, x * cell);
            Canvas.SetTop(r, y * cell);
            MascotCanvas.Children.Add(r);
        }

        // head outline
        for (int x = 7; x <= 16; x++) Put(x, 2, ink);
        for (int x = 6; x <= 17; x++) Put(x, 3, ink);
        for (int x = 5; x <= 18; x++) Put(x, 4, ink);
        for (int y = 5; y <= 13; y++) { Put(5, y, ink); Put(18, y, ink); }
        for (int x = 6; x <= 17; x++) Put(x, 14, ink);
        for (int x = 7; x <= 16; x++) Put(x, 15, ink);

        // body fill
        for (int y = 4; y <= 14; y++)
        {
            int xMin = 6, xMax = 17;
            if (y == 14) { xMin = 7; xMax = 16; }
            for (int x = xMin; x <= xMax; x++)
            {
                if (y >= 5 && y <= 13 && (x == 5 || x == 18)) continue;
                Put(x, y, body);
            }
        }

        // top-left highlight
        for (int x = 7; x <= 14; x++) Put(x, 5, white);
        Put(6, 6, white); Put(6, 7, white);

        // mic grille dots
        foreach (int gy in new[] { 7, 9, 11 })
            for (int gx = 8; gx <= 15; gx += 2)
                Put(gx, gy, dark);

        // cheeks
        Put(7, 11, cheek); Put(8, 11, cheek);
        Put(15, 11, cheek); Put(16, 11, cheek);
        Put(7, 12, cheek); Put(16, 12, cheek);

        // eyes (sparkle = white pixel inside)
        Put(9, 8, ink);  Put(10, 8, ink);
        Put(9, 9, ink);  Put(10, 9, ink);
        Put(10, 8, white);
        Put(13, 8, ink); Put(14, 8, ink);
        Put(13, 9, ink); Put(14, 9, ink);
        Put(14, 8, white);

        // mouth
        if (mood == MascotMood.Recording)
        {
            Put(11, 12, ink); Put(12, 12, ink);
            Put(11, 13, ink); Put(12, 13, ink);
        }
        else if (mood == MascotMood.Paused)
        {
            Put(10, 12, ink); Put(11, 12, ink); Put(12, 12, ink); Put(13, 12, ink);
        }
        else
        {
            Put(10, 12, ink); Put(13, 12, ink);
            Put(11, 13, ink); Put(12, 13, ink);
        }

        // stand connector
        Put(11, 16, ink); Put(12, 16, ink);
        Put(11, 17, ink); Put(12, 17, ink);

        // base
        for (int x = 7; x <= 16; x++) Put(x, 18, ink);
        for (int x = 6; x <= 17; x++) Put(x, 19, ink);
        for (int x = 6; x <= 17; x++) Put(x, 20, ink);
        for (int x = 7; x <= 16; x++) Put(x, 21, ink);
        for (int x = 7; x <= 16; x++) Put(x, 19, dark);
        for (int x = 7; x <= 16; x++) Put(x, 20, dark);

        // mood ornaments
        if (mood == MascotMood.Idle)
        {
            Put(20, 7, gold); Put(19, 8, gold); Put(21, 8, gold); Put(20, 9, gold);
            Put(2, 13, gold); Put(2, 14, gold);
        }
        if (mood == MascotMood.Recording)
        {
            int nx = 19, ny = 4;
            Put(nx + 1, ny, ink); Put(nx + 2, ny, ink);
            Put(nx, ny + 1, ink); Put(nx + 3, ny + 1, cheek);
            Put(nx, ny + 2, ink); Put(nx + 3, ny + 2, cheek);
            Put(nx + 1, ny + 3, ink); Put(nx + 2, ny + 3, ink);
            Put(nx + 4, ny + 3, ink);
            Put(nx + 4, ny + 4, ink);
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
    public required string Name { get; init; }
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
