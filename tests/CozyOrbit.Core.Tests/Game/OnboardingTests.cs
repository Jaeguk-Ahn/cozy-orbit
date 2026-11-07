using System.Text.Json;
using CozyOrbit.Core.Game;

namespace CozyOrbit.Core.Tests.Game;

public class OnboardingTests
{
    private static GameState NewGame() => GameState.New(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void NewCaptainSeesTheIntroEvenAfterFlyingABit()
    {
        var s = NewGame();
        s.Distance = 500; // 안내를 닫기 전에 껐다 켜도 아직 새 선장
        Onboarding.SkipIfAlreadySailing(s);
        Assert.False(s.Settings.OnboardingDone);
    }

    [Fact]
    public void SavesWithProgressSkipTheIntro()
    {
        var discovered = NewGame();
        discovered.DiscoveryCount = 1;
        var nextVoyage = NewGame();
        nextVoyage.Voyage = 1;

        Onboarding.SkipIfAlreadySailing(discovered);
        Onboarding.SkipIfAlreadySailing(nextVoyage);

        Assert.True(discovered.Settings.OnboardingDone);
        Assert.True(nextVoyage.Settings.OnboardingDone);
    }

    [Fact]
    public void OldSettingsWithoutTheFieldLoadAsNotDone()
    {
        var settings = JsonSerializer.Deserialize<GameSettings>("""{"language":"en"}""",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        Assert.False(settings.OnboardingDone);
    }
}
