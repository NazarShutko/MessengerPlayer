using NAudio.Wave;

namespace MessengerPlayer;

public sealed class AudioEngine : IDisposable
{
    WaveOutEvent? _out;
    AudioFileReader? _reader;
    int _gen;
    float _volume = 0.8f;

    public event Action? Ended;
    public event Action<string>? Failed;

    public bool HasTrack => _reader != null;
    public bool IsPlaying => _out?.PlaybackState == PlaybackState.Playing;
    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public TimeSpan Position
    {
        get => _reader?.CurrentTime ?? TimeSpan.Zero;
        set
        {
            if (_reader == null) return;
            if (value < TimeSpan.Zero) value = TimeSpan.Zero;
            if (value > Duration) value = Duration;
            _reader.CurrentTime = value;
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_reader != null) _reader.Volume = _volume;
        }
    }

    public void Load(string path)
    {
        Release();

        var reader = new AudioFileReader(path) { Volume = _volume };
        var output = new WaveOutEvent();
        try { output.Init(reader); }
        catch { output.Dispose(); reader.Dispose(); throw; }

        int gen = ++_gen;
        output.PlaybackStopped += (_, e) =>
        {
            if (gen != _gen) return;
            if (e.Exception != null)
                Failed?.Invoke(e.Exception.Message);
            else if (_reader != null && _reader.TotalTime.TotalSeconds > 3 && _reader.CurrentTime.TotalSeconds < 1)
                Failed?.Invoke("файл не содержит звука или его формат не читается (" + Path.GetExtension(path) + ")");
            else
                Ended?.Invoke();
        };

        _reader = reader;
        _out = output;
    }

    public void Play() => _out?.Play();
    public void Pause() => _out?.Pause();

    public void Stop()
    {
        _gen++;
        _out?.Stop();
        if (_reader != null) _reader.Position = 0;
    }

    void Release()
    {
        _gen++;
        _out?.Dispose();
        _reader?.Dispose();
        _out = null;
        _reader = null;
    }

    public void Dispose() => Release();
}
