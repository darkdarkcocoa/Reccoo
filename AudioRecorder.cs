using System.Diagnostics;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Lame;
using NAudio.Wave;

namespace CocoaRecorder;

public enum RecordingFormat { Wav, Mp3 }
public enum Mp3Quality { Low, Medium, High }

public sealed class RecordingFinishedEventArgs : EventArgs
{
    public required string OutputPath { get; init; }
    public Exception? Error { get; init; }
}

public sealed class AudioRecorder : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private WaveFileWriter? _wavWriter;
    private string _tempWavPath = string.Empty;
    private string _finalPath = string.Empty;
    private RecordingFormat _format;
    private readonly Stopwatch _stopwatch = new();

    public bool IsRecording { get; private set; }
    public bool IsPaused { get; private set; }
    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public Mp3Quality Mp3Quality { get; set; } = Mp3Quality.Medium;

    /// <summary>Sample rate of the endpoint being captured, 0 when idle.</summary>
    public int SampleRate { get; private set; }

    private long _capturedBytes;

    /// <summary>Raw PCM bytes written so far. Display only — the UI shows it as a running size.</summary>
    public long CapturedBytes => Interlocked.Read(ref _capturedBytes);

    public event EventHandler<RecordingFinishedEventArgs>? RecordingFinished;
    public event EventHandler<float>? LevelChanged;

    public static List<MMDevice> GetRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
        // Move the default device to the front so the UI selects it by default.
        var def = devices.FirstOrDefault(d => d.ID == defaultId);
        if (def != null)
        {
            devices.Remove(def);
            devices.Insert(0, def);
        }
        return devices;
    }

    /// <summary>엔드포인트의 믹스 포맷 샘플레이트. 읽지 못하면 0 — 히어로 표시에만 쓴다.</summary>
    public static int TryGetSampleRate(MMDevice device)
    {
        try { return device.AudioClient.MixFormat.SampleRate; }
        catch { return 0; }
    }

    public void Start(MMDevice device, RecordingFormat format, string finalPath)
    {
        if (IsRecording) throw new InvalidOperationException("Already recording.");

        _format = format;
        _finalPath = finalPath;
        _tempWavPath = Path.Combine(Path.GetTempPath(), $"cocoa_recorder_{Guid.NewGuid():N}.wav");

        _capture = new WasapiLoopbackCapture(device);
        _wavWriter = new WaveFileWriter(_tempWavPath, _capture.WaveFormat);
        SampleRate = _capture.WaveFormat.SampleRate;
        Interlocked.Exchange(ref _capturedBytes, 0);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        _stopwatch.Restart();
        IsRecording = true;
        IsPaused = false;
        _capture.StartRecording();
    }

    public void Stop()
    {
        if (!IsRecording) return;
        _stopwatch.Stop();
        IsPaused = false;
        _capture?.StopRecording();
    }

    public void Pause()
    {
        if (!IsRecording || IsPaused) return;
        IsPaused = true;
        _stopwatch.Stop();
    }

    public void Resume()
    {
        if (!IsRecording || !IsPaused) return;
        IsPaused = false;
        _stopwatch.Start();
    }

    private const int TransientSkipMs = 100;

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (IsPaused) return;

        // Skip ~100ms of WASAPI startup so the click from StartRecording does not get baked in.
        if (_stopwatch.ElapsedMilliseconds >= TransientSkipMs)
        {
            _wavWriter?.Write(e.Buffer, 0, e.BytesRecorded);
            Interlocked.Add(ref _capturedBytes, e.BytesRecorded);
        }

        var fmt = _capture?.WaveFormat;
        if (fmt != null && LevelChanged != null)
        {
            float peak = ComputePeak(e.Buffer, e.BytesRecorded, fmt);
            LevelChanged.Invoke(this, peak);
        }
    }

    private static float ComputePeak(byte[] buffer, int bytes, WaveFormat fmt)
    {
        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
        {
            float peak = 0f;
            for (int i = 0; i + 3 < bytes; i += 4)
            {
                float v = MathF.Abs(BitConverter.ToSingle(buffer, i));
                if (v > peak) peak = v;
            }
            return Math.Min(1f, peak);
        }
        if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 16)
        {
            int peak = 0;
            for (int i = 0; i + 1 < bytes; i += 2)
            {
                int v = BitConverter.ToInt16(buffer, i);
                int abs = v == short.MinValue ? short.MaxValue : Math.Abs(v);
                if (abs > peak) peak = abs;
            }
            return peak / 32768f;
        }
        return 0f;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        IsRecording = false;
        Exception? error = e.Exception;
        string output = _finalPath;

        try
        {
            _wavWriter?.Flush();
            _wavWriter?.Dispose();
            _wavWriter = null;
            _capture?.Dispose();
            _capture = null;

            if (error == null)
            {
                if (_format == RecordingFormat.Mp3)
                {
                    EncodeWavToMp3(_tempWavPath, _finalPath, Mp3Quality);
                }
                else
                {
                    File.Copy(_tempWavPath, _finalPath, overwrite: true);
                }
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            try { if (File.Exists(_tempWavPath)) File.Delete(_tempWavPath); } catch { /* ignore */ }
        }

        RecordingFinished?.Invoke(this, new RecordingFinishedEventArgs
        {
            OutputPath = output,
            Error = error
        });
    }

    private static void EncodeWavToMp3(string wavPath, string mp3Path, Mp3Quality quality)
    {
        var preset = quality switch
        {
            Mp3Quality.Low => LAMEPreset.MEDIUM,    // ~150 kbps VBR
            Mp3Quality.High => LAMEPreset.EXTREME,  // ~245 kbps VBR
            _ => LAMEPreset.STANDARD,               // ~190 kbps VBR
        };
        using var reader = new WaveFileReader(wavPath);
        using var writer = new LameMP3FileWriter(mp3Path, reader.WaveFormat, preset);
        reader.CopyTo(writer);
    }

    public static TimeSpan? TryGetDuration(string path)
    {
        try
        {
            var ext = Path.GetExtension(path);
            if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                using var r = new WaveFileReader(path);
                return r.TotalTime;
            }
            if (ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                using var r = new Mp3FileReader(path);
                return r.TotalTime;
            }
        }
        catch { /* corrupt or in-use — skip */ }
        return null;
    }

    public void Dispose()
    {
        try { _capture?.Dispose(); } catch { }
        try { _wavWriter?.Dispose(); } catch { }
        try { if (!string.IsNullOrEmpty(_tempWavPath) && File.Exists(_tempWavPath)) File.Delete(_tempWavPath); } catch { }
    }
}
