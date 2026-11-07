using CozyOrbit.Core.Audio;
using CozyOrbit.Core.Game;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Infra;

/// <summary>
/// 배경음·효과음 (docs/design.md §15). 기본은 꺼짐 — 설정에서 켜야 소리 장치를 연다.
/// 소리는 Core 의 SoundSynth 가 계산하고(배경음은 백그라운드에서), OpenAL Soft 로 낸다.
/// 장치를 못 열면 조용히 꺼진 채로 두고 이유를 Error 로 알린다. UI 스레드에서만 부른다.
/// </summary>
public static class Sound
{
    private const int EffectVoices = 4;
    /// <summary>배경음은 효과음보다 작게</summary>
    private const float MusicLevel = 0.45f;
    /// <summary>같은 발견 소리가 한꺼번에 겹치지 않게</summary>
    private static readonly TimeSpan DiscoveryGap = TimeSpan.FromMilliseconds(600);

    private static GameHost? _host;
    private static bool _initTried;
    private static nint _device, _context;
    private static readonly Dictionary<SoundId, uint> Effects = new();
    private static readonly uint[] Voices = new uint[EffectVoices];
    private static int _nextVoice;
    private static uint _musicSource, _musicBuffer;
    private static bool _musicLoading;
    private static DateTime _lastDiscovery;

    /// <summary>장치를 열 수 있었는지 (아직 안 열어 봤으면 true)</summary>
    public static bool Available => !_initTried || _context != 0;
    public static string? Error { get; private set; }

    private static GameSettings? Settings => _host?.State.Settings;

    public static void Attach(GameHost host)
    {
        _host = host;
        host.Discovered += _ =>
        {
            if (DateTime.UtcNow - _lastDiscovery < DiscoveryGap) return;
            _lastDiscovery = DateTime.UtcNow;
            Play(SoundId.Discovery);
        };
        host.VoyageStarted += (_, _) => Play(SoundId.Voyage);
        Apply();
    }

    /// <summary>설정이 바뀌면 부른다: 장치 열기·음량·배경음 켜고 끄기</summary>
    public static void Apply()
    {
        var s = Settings;
        if (s == null) return;
        if (!s.SoundEnabled)
        {
            StopMusic();
            return;
        }
        if (!EnsureDevice()) return;
        float volume = (float)s.Volume;
        foreach (var v in Voices) OpenAl.alSourcef(v, OpenAl.Gain, volume);
        if (s.MusicEnabled) StartMusic(volume * MusicLevel);
        else StopMusic();
    }

    public static void Play(SoundId id)
    {
        var s = Settings;
        if (s is not { SoundEnabled: true, EffectsEnabled: true } || !EnsureDevice()) return;
        if (!Effects.TryGetValue(id, out var buffer))
        {
            buffer = Upload(SoundSynth.Effect(id));
            Effects[id] = buffer;
        }
        uint voice = Voices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % Voices.Length;
        OpenAl.alSourceStop(voice);
        OpenAl.alSourcei(voice, OpenAl.Buffer, (int)buffer);
        OpenAl.alSourcef(voice, OpenAl.Gain, (float)s.Volume);
        OpenAl.alSourcePlay(voice);
    }

    private static bool EnsureDevice()
    {
        if (_initTried) return _context != 0;
        _initTried = true;
        try
        {
            OpenAl.EnsureResolver();
            _device = OpenAl.alcOpenDevice(null);
            if (_device == 0) throw new InvalidOperationException("alcOpenDevice failed");
            _context = OpenAl.alcCreateContext(_device, 0);
            if (_context == 0 || !OpenAl.alcMakeContextCurrent(_context)) throw new InvalidOperationException("alcCreateContext failed");
            for (int i = 0; i < Voices.Length; i++)
            {
                OpenAl.alGenSources(1, out var v);
                Voices[i] = v;
            }
            OpenAl.alGenSources(1, out _musicSource);
            OpenAl.alSourcei(_musicSource, OpenAl.Looping, 1);
            AppLog.Info("소리 장치 열림");
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or BadImageFormatException)
        {
            Error = L.T("소리 장치를 열지 못했어요.", "Couldn't open the sound device.");
            AppLog.Warn($"소리 장치를 열지 못함: {ex.Message}");
            if (_device != 0) OpenAl.alcCloseDevice(_device);
            _device = _context = 0;
            return false;
        }
    }

    private static uint Upload(short[] pcm)
    {
        OpenAl.alGenBuffers(1, out var buffer);
        OpenAl.alBufferData(buffer, OpenAl.FormatMono16, pcm, pcm.Length * sizeof(short), SoundSynth.SampleRate);
        return buffer;
    }

    private static void StartMusic(float gain)
    {
        if (_context == 0) return;
        OpenAl.alSourcef(_musicSource, OpenAl.Gain, gain);
        if (_musicBuffer == 0)
        {
            // 26초짜리 루프 계산은 수백 ms — 화면이 멈추지 않게 백그라운드에서
            if (_musicLoading) return;
            _musicLoading = true;
            Task.Run(SoundSynth.Music).ContinueWith(t =>
            {
                _musicLoading = false;
                if (t.IsFaulted)
                {
                    AppLog.Warn($"배경음을 만들지 못함: {t.Exception?.GetBaseException().Message}");
                    return;
                }
                if (_context == 0) return;
                _musicBuffer = Upload(t.Result);
                OpenAl.alSourcei(_musicSource, OpenAl.Buffer, (int)_musicBuffer);
                AppLog.Info($"배경음 준비 ({t.Result.Length / (double)SoundSynth.SampleRate:0.0}초 루프, 오류 {OpenAl.alGetError()})");
                Apply();
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return;
        }
        OpenAl.alGetSourcei(_musicSource, OpenAl.SourceState, out int state);
        if (state != OpenAl.Playing) OpenAl.alSourcePlay(_musicSource);
    }

    private static void StopMusic()
    {
        if (_context != 0 && _musicSource != 0) OpenAl.alSourceStop(_musicSource);
    }

    /// <summary>종료할 때 장치를 닫는다</summary>
    public static void Shutdown()
    {
        if (_context == 0) return;
        StopMusic();
        foreach (var v in Voices) { var x = v; OpenAl.alSourceStop(x); OpenAl.alDeleteSources(1, ref x); }
        if (_musicSource != 0) OpenAl.alDeleteSources(1, ref _musicSource);
        foreach (var b in Effects.Values) { var x = b; OpenAl.alDeleteBuffers(1, ref x); }
        if (_musicBuffer != 0) OpenAl.alDeleteBuffers(1, ref _musicBuffer);
        OpenAl.alcMakeContextCurrent(0);
        OpenAl.alcDestroyContext(_context);
        OpenAl.alcCloseDevice(_device);
        _context = _device = 0;
    }
}
