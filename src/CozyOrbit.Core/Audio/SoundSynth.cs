namespace CozyOrbit.Core.Audio;

/// <summary>효과음 종류 (docs/design.md §15)</summary>
public enum SoundId
{
    /// <summary>새 천체 발견 — 위로 올라가는 반짝 아르페지오</summary>
    Discovery,
    /// <summary>햄찌 쓰다듬기 — 짧은 "찍"</summary>
    Squeak,
    /// <summary>별똥별 잡기·보상 받기 — 높은 차임 두 번</summary>
    Star,
    /// <summary>구매 — 동전 두 음</summary>
    Buy,
    /// <summary>새 항해 출발 — 상승 팡파레</summary>
    Voyage,
}

/// <summary>
/// 외부 샘플 없이 파형(삼각파·사각파·노이즈) + ADSR 로 소리를 계산한다. 결과는 16bit 모노 PCM.
/// 같은 입력이면 늘 같은 소리 (노이즈도 고정 시드) — 테스트·캐시가 쉽다.
/// </summary>
public static class SoundSynth
{
    public const int SampleRate = 44100;
    /// <summary>정규화 목표 최댓값 (잘림 없이 살짝 여유)</summary>
    private const float Peak = 0.8f;

    /// <summary>배경음 템포와 길이: BPM 76, 8마디 — 루프 길이가 마디에 딱 맞아 끊김 없이 이어진다</summary>
    public const double MusicBpm = 76;
    public const int MusicBars = 8;

    public static short[] Effect(SoundId id)
    {
        var rng = new Random(7 + (int)id);
        float[] buf;
        switch (id)
        {
            case SoundId.Discovery:
                buf = new float[Samples(1.2)];
                int[] arp = [79, 84, 88, 91];
                for (int i = 0; i < arp.Length; i++) Note(buf, i * 0.07, 0.12, Midi(arp[i]), 0.3, Tri, (0.005, 0.1, 0.4, 0.5));
                break;
            case SoundId.Squeak:
                buf = new float[Samples(0.35)];
                double ph = 0;
                for (int i = 0; i < buf.Length; i++)
                {
                    double t = i / (double)SampleRate;
                    ph += (900 + 1400 * Math.Min(1, t / 0.08)) / SampleRate;
                    buf[i] = (float)(Square(ph, 0.3) * 0.25 * Env(t, 0.005, 0.05, 0.5, 0.12, 0.15));
                }
                break;
            case SoundId.Star:
                buf = new float[Samples(0.9)];
                Note(buf, 0, 0.08, Midi(96), 0.3, Tri, (0.002, 0.05, 0.3, 0.4));
                Note(buf, 0.09, 0.08, Midi(100), 0.3, Tri, (0.002, 0.05, 0.3, 0.5));
                Noise(buf, rng, 0, 0.4, 0.08, 12, 0.95);
                break;
            case SoundId.Buy:
                buf = new float[Samples(0.6)];
                Note(buf, 0, 0.06, Midi(83), 0.3, p => Square(p, 0.5), (0.001, 0.04, 0.5, 0.1));
                Note(buf, 0.07, 0.2, Midi(88), 0.3, p => Square(p, 0.5), (0.001, 0.1, 0.5, 0.25));
                break;
            default:
                buf = new float[Samples(2.4)];
                int[] fan = [60, 64, 67, 72, 76, 79, 84];
                for (int i = 0; i < fan.Length; i++)
                    Note(buf, i * 0.11, i == fan.Length - 1 ? 0.9 : 0.1, Midi(fan[i]), 0.25, p => Square(p, 0.25) * 0.4 + Tri(p) * 0.6, (0.005, 0.1, 0.6, 0.6), 0.005);
                foreach (var c in new[] { 60, 64, 67, 72 }) Note(buf, 0.77, 1.0, Midi(c), 0.1, Tri, (0.05, 0.3, 0.6, 0.6));
                break;
        }
        return ToPcm(buf);
    }

    /// <summary>느긋한 로파이 배경음 루프 (C 장조, Cmaj7–Am7–Fmaj7–G). 끝에서 넘치는 꼬리는 앞에 겹쳐 이음매를 없앤다.</summary>
    public static short[] Music()
    {
        var rng = new Random(76);
        double beat = 60 / MusicBpm;
        int length = Samples(MusicBars * 4 * beat);
        var buf = new float[length + Samples(2)]; // 꼬리 자리
        int[][] chords = [[48, 52, 55, 59], [45, 48, 52, 55], [41, 45, 48, 52], [43, 47, 50, 55]];
        for (int bar = 0; bar < MusicBars; bar++)
        {
            var c = chords[bar % 4];
            double t0 = bar * 4 * beat;
            foreach (var n in c) Note(buf, t0, 4 * beat * 0.95, Midi(n), 0.10, Tri, (0.3, 0.6, 0.6, 0.8), 0.002);   // 패드
            Note(buf, t0, beat * 1.8, Midi(c[0] - 12), 0.22, Tri, (0.01, 0.3, 0.5, 0.2));                         // 베이스
            Note(buf, t0 + 2 * beat, beat * 1.8, Midi(c[2] - 12), 0.18, Tri, (0.01, 0.3, 0.5, 0.2));
            for (int b = 0; b < 4; b++)
            {
                if (b % 2 == 0) Noise(buf, rng, t0 + b * beat, 0.25, 0.35, 18, 0.08);     // 부드러운 킥
                Noise(buf, rng, t0 + b * beat + beat / 2, 0.06, 0.10, 60, 0.9);             // 하이햇
                if (b % 2 == 1) Noise(buf, rng, t0 + b * beat, 0.18, 0.18, 25, 0.35);      // 브러시 스네어
            }
        }
        int[] melody = [72, 76, 79, 76, 74, 72, 69, 72, 77, 76, 74, 72, 74, 76, 79, 81, 79, 76, 74, 76, 72, 69, 67, 69, 72, 74, 76, 74, 72, -1, 72, -1];
        for (int i = 0; i < melody.Length; i++)
            if (melody[i] > 0) Note(buf, i * beat, beat * 0.8, Midi(melody[i]), 0.12, p => Square(p, 0.25) * 0.5 + Tri(p) * 0.5, (0.01, 0.15, 0.4, 0.25), 0.004);

        var loop = new float[length];
        Array.Copy(buf, loop, length);
        for (int i = length; i < buf.Length; i++) loop[i - length] += buf[i];
        return ToPcm(loop);
    }

    // ── 합성 도구 ──

    private static int Samples(double seconds) => (int)Math.Round(seconds * SampleRate);
    private static double Midi(int n) => 440 * Math.Pow(2, (n - 69) / 12.0);
    private static double Tri(double ph) => 2 * Math.Abs(2 * (ph - Math.Floor(ph + 0.5))) - 1;
    private static double Square(double ph, double duty) => ph - Math.Floor(ph) < duty ? 1 : -1;

    private static double Env(double t, double a, double d, double s, double len, double r)
    {
        if (t < 0) return 0;
        if (t < a) return t / a;
        if (t < a + d) return 1 - (1 - s) * (t - a) / d;
        if (t < len) return s;
        return t < len + r ? s * (1 - (t - len) / r) : 0;
    }

    private static void Note(float[] buf, double start, double len, double freq, double vol, Func<double, double> wave,
        (double A, double D, double S, double R) env, double vibrato = 0)
    {
        int i0 = Samples(start), n = Samples(len + env.R);
        double ph = 0;
        for (int i = 0; i < n && i0 + i < buf.Length; i++)
        {
            double t = i / (double)SampleRate;
            ph += freq * (1 + vibrato * Math.Sin(2 * Math.PI * 5 * t)) / SampleRate;
            buf[i0 + i] += (float)(wave(ph) * vol * Env(t, env.A, env.D, env.S, len, env.R));
        }
    }

    /// <summary>저역 통과(lowpass 0..1, 작을수록 둥근 소리) 노이즈를 지수 감쇠로</summary>
    private static void Noise(float[] buf, Random rng, double start, double len, double vol, double decay, double lowpass)
    {
        int i0 = Samples(start), n = Samples(len);
        double y = 0;
        for (int i = 0; i < n && i0 + i < buf.Length; i++)
        {
            y += lowpass * (rng.NextDouble() * 2 - 1 - y);
            buf[i0 + i] += (float)(y * vol * Math.Exp(-i / (double)SampleRate * decay));
        }
    }

    private static short[] ToPcm(float[] buf)
    {
        float max = 0;
        foreach (var v in buf) max = Math.Max(max, Math.Abs(v));
        float gain = max > 0 ? Peak / max : 1;
        var pcm = new short[buf.Length];
        for (int i = 0; i < buf.Length; i++) pcm[i] = (short)Math.Clamp(buf[i] * gain * short.MaxValue, short.MinValue, short.MaxValue);
        return pcm;
    }
}
