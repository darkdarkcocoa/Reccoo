using System.IO;
using System.Windows;
using NAudio.Wave;

namespace CocoaRecorder;

/// <summary>
/// 카운트다운의 냥 소리. 다섯 클립을 켤 때 한 번 PCM 으로 풀어 두고, 숫자가 바뀔 때마다 하나씩 튼다.
/// 마지막 셋은 언제나 1→2→3 이라 끝맺음이 같고, 그 앞의 초는 4·5 를 번갈아 쓴다.
/// 0 이 되는 순간은 녹음이 시작되므로 아무것도 틀지 않는다 — 이 앱은 스피커로 나가는 소리를 그대로 담는다.
/// </summary>
public sealed class CountdownChime
{
    public const int ClipCount = 5;

    /// <summary>끝맺음으로 쓰는 클립 수 — 남은 초가 이 값 이하이면 1→2→3 으로 간다.</summary>
    private const int ClosingClips = 3;

    /// <summary>재생 버퍼 크기. 기본 300ms 는 숫자가 바뀐 뒤 소리가 늦게 들리고, 너무 작으면 끊긴다.</summary>
    private const int LatencyMs = 100;

    private readonly (byte[] Pcm, WaveFormat Format)[] _clips;
    private WaveOutEvent? _device;

    public CountdownChime()
    {
        var clips = new List<(byte[], WaveFormat)>();
        for (int i = 1; i <= ClipCount; i++)
        {
            try
            {
                clips.Add(Decode($"pack://application:,,,/Sounds/meow-{i}.mp3"));
            }
            catch
            {
                // MP3 코덱이 없거나 리소스가 빠졌으면 소리 없이 간다 — 녹음기는 녹음기다.
                break;
            }
        }
        _clips = clips.Count == ClipCount ? clips.ToArray() : [];
    }

    public bool IsAvailable => _clips.Length > 0;

    /// <summary>남은 초와 전체 길이로 클립 번호(0 기준)를 고른다.</summary>
    public static int ClipFor(int remaining, int total)
        => remaining <= ClosingClips
            ? ClosingClips - remaining
            : ClosingClips + (total - remaining) % (ClipCount - ClosingClips);

    public void Play(int index)
    {
        if (index < 0 || index >= _clips.Length) return;
        Stop();

        var (pcm, format) = _clips[index];
        var stream = new RawSourceWaveStream(new MemoryStream(pcm), format);
        var device = new WaveOutEvent { DesiredLatency = LatencyMs };
        try
        {
            device.Init(stream);
            device.PlaybackStopped += (_, _) =>
            {
                device.Dispose();
                stream.Dispose();
            };
            device.Play();
            _device = device;
        }
        catch
        {
            // 출력 장치가 없으면 조용히 넘어간다 — 만들다 만 것은 흘리지 말고 정리한다.
            device.Dispose();
            stream.Dispose();
        }
    }

    public void Stop()
    {
        var device = _device;
        _device = null;
        try { device?.Stop(); } catch { }
    }

    private static (byte[] Pcm, WaveFormat Format) Decode(string packUri)
    {
        var info = Application.GetResourceStream(new Uri(packUri))
                   ?? throw new FileNotFoundException(packUri);

        // Mp3FileReader 는 탐색 가능한 스트림을 원하므로 한 번 메모리로 옮긴다.
        using var source = new MemoryStream();
        using (info.Stream) info.Stream.CopyTo(source);
        source.Position = 0;

        using var reader = new Mp3FileReader(source);
        using var pcm = new MemoryStream();
        reader.CopyTo(pcm);
        return (pcm.ToArray(), reader.WaveFormat);
    }
}
