using CozyOrbit.Core.Audio;

namespace CozyOrbit.Core.Tests.Audio;

public class SoundSynthTests
{
    [Theory]
    [InlineData(SoundId.Discovery)]
    [InlineData(SoundId.Squeak)]
    [InlineData(SoundId.Star)]
    [InlineData(SoundId.Buy)]
    [InlineData(SoundId.Voyage)]
    [InlineData(SoundId.Whale)]
    public void EffectsAreShortAudibleAndNeverClip(SoundId id)
    {
        var pcm = SoundSynth.Effect(id);
        Assert.InRange(pcm.Length, SoundSynth.SampleRate / 10, SoundSynth.SampleRate * 3);
        int peak = pcm.Max(v => Math.Abs((int)v));
        // 정규화 목표 0.8 — 잘림(32767) 없이 충분히 크게
        Assert.InRange(peak, (int)(short.MaxValue * 0.75), (int)(short.MaxValue * 0.85));
    }

    [Fact]
    public void SameInputGivesTheSameSound()
    {
        Assert.Equal(SoundSynth.Effect(SoundId.Star), SoundSynth.Effect(SoundId.Star));
    }

    [Fact]
    public void MusicLoopIsExactlyEightBarsLong()
    {
        var pcm = SoundSynth.Music();
        double seconds = SoundSynth.MusicBars * 4 * 60 / SoundSynth.MusicBpm;
        Assert.Equal((int)Math.Round(seconds * SoundSynth.SampleRate), pcm.Length);
        Assert.True(pcm.Max(v => Math.Abs((int)v)) < short.MaxValue);
    }
}
