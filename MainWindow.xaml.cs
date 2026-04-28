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
using Microsoft.Win32;
using NAudio.CoreAudioApi;
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

        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _waveformTimer.Stop();
            _uiTimer.Stop();
            _blinkTimer.Stop();
            _recorder.Dispose();
        };
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
        if (!_recorder.IsRecording) return;
        _uiTimer.Stop();
        MascotSpeech.Text = "저장 중...\n잠깐만!";
        _recorder.Stop();
    }

    private void StartRecording()
    {
        if (DeviceCombo.SelectedItem is not MMDevice device)
        {
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
            MascotSpeech.Text = $"시작 실패ㅠ\n{ex.Message}";
            return;
        }

        SetRecordingVisual(true);
        _uiTimer.Start();
        _blinkTimer.Start();
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
            _blinkOn = true;
        }
        UpdateStatusDot();
    }

    private void UpdateStatusDot()
    {
        if (_recorder.IsRecording)
        {
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
    private void TickWaveform()
    {
        bool recording = _recorder.IsRecording;
        var mintFill = (Brush)FindResource("MintDeepBrush");
        var goldFill = (Brush)FindResource("GoldBrush");
        var coralFill = (Brush)FindResource("AccentDeepBrush");
        var emptyFill = (Brush)FindResource("CreamDeepBrush");

        double now = Environment.TickCount;
        double tFast = now / 180.0;
        double tIdle = now / 420.0;
        const double maxBarHeight = 110.0;

        for (int i = 0; i < BarCount; i++)
        {
            double h;
            if (recording)
            {
                double tt = tFast + i * 0.32;
                double env = 0.55 + 0.4 * Math.Sin(tt) + 0.25 * Math.Sin(tt * 1.7) + (_rng.NextDouble() - 0.5) * 0.3;
                h = Math.Max(0.08, Math.Min(1.0, env));
            }
            else
            {
                h = 0.12 + Math.Abs(Math.Sin(tIdle + i * 0.4)) * 0.18;
            }

            _bars[i].Height = Math.Max(4, h * maxBarHeight);
            _bars[i].Fill = h < 0.4 ? mintFill : (h < 0.75 ? goldFill : coralFill);
        }

        for (int i = 0; i < LevelCellCount; i++)
        {
            bool lit = recording && i < (8 + _rng.Next(0, 6));
            Brush color = i < 12 ? mintFill : (i < 15 ? goldFill : coralFill);
            _levelCells[i].Fill = lit ? color : emptyFill;
        }
    }

    // =================== Recordings library ===================
    private void RefreshRecordings()
    {
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
            _recordings.Add(new RecordingItem
            {
                Name = fi.Name,
                Path = fi.FullName,
                Meta = $"{fmt} · {FormatBytes(fi.Length)} · {fi.LastWriteTime:M월 d일 HH:mm}",
                TapeColor = palette[i % palette.Length]
            });
        }
        UpdateLibraryUi();
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

    private void PlayRecording_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string path && File.Exists(path))
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MascotSpeech.Text = $"재생 실패ㅠ\n{ex.Message}"; }
        }
    }

    private void DeleteRecording_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string path && File.Exists(path))
        {
            try { File.Delete(path); RefreshRecordings(); }
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

public sealed class RecordingItem
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Meta { get; init; }
    public required Brush TapeColor { get; init; }
}
