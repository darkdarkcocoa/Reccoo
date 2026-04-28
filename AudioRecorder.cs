using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Lame;
using NAudio.Wave;

namespace Reccoo;

public enum RecordingFormat { Wav, Mp3 }

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
    private DateTime _startedAt;

    public bool IsRecording { get; private set; }
    public TimeSpan Elapsed => IsRecording ? DateTime.UtcNow - _startedAt : TimeSpan.Zero;

    public event EventHandler<RecordingFinishedEventArgs>? RecordingFinished;

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

    public void Start(MMDevice device, RecordingFormat format, string finalPath)
    {
        if (IsRecording) throw new InvalidOperationException("Already recording.");

        _format = format;
        _finalPath = finalPath;
        _tempWavPath = Path.Combine(Path.GetTempPath(), $"recsound_{Guid.NewGuid():N}.wav");

        _capture = new WasapiLoopbackCapture(device);
        _wavWriter = new WaveFileWriter(_tempWavPath, _capture.WaveFormat);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        _startedAt = DateTime.UtcNow;
        IsRecording = true;
        _capture.StartRecording();
    }

    public void Stop()
    {
        if (!IsRecording) return;
        _capture?.StopRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _wavWriter?.Write(e.Buffer, 0, e.BytesRecorded);
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
                    EncodeWavToMp3(_tempWavPath, _finalPath);
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

    private static void EncodeWavToMp3(string wavPath, string mp3Path)
    {
        using var reader = new WaveFileReader(wavPath);
        using var writer = new LameMP3FileWriter(mp3Path, reader.WaveFormat, LAMEPreset.STANDARD);
        reader.CopyTo(writer);
    }

    public void Dispose()
    {
        try { _capture?.Dispose(); } catch { }
        try { _wavWriter?.Dispose(); } catch { }
    }
}
