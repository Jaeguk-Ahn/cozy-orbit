using CozyOrbit.Core.Art;

namespace CozyOrbit.Core.Tests.Art;

public class DayNightTests
{
    private static double At(int h, int m = 0) => DayNight.Night(new TimeSpan(h, m, 0));

    [Theory]
    [InlineData(12, 0)]
    [InlineData(18, 0)]
    [InlineData(21, 29)]
    [InlineData(6, 31)]
    [InlineData(9, 0)]
    public void DaytimeIsFullyBright(int h, int m) => Assert.Equal(0, At(h, m));

    [Theory]
    [InlineData(22, 31)]
    [InlineData(23, 59)]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(5, 29)]
    public void DeepNightIsFullyDark(int h, int m) => Assert.Equal(1, At(h, m));

    [Fact]
    public void DuskAndDawnAreHalfwayAtTheBoundaryHours()
    {
        Assert.Equal(0.5, At(22), 6);
        Assert.Equal(0.5, At(6), 6);
        Assert.True(DayNight.IsSleepy(At(22)));
        Assert.False(DayNight.IsSleepy(At(21, 45)));
    }

    [Fact]
    public void DuskDarkensAndDawnBrightensSmoothly()
    {
        double prev = -1;
        for (int m = 0; m <= 90; m += 5)
        {
            double n = DayNight.Night(new TimeSpan(21, 0, 0) + TimeSpan.FromMinutes(m));
            Assert.True(n >= prev);
            prev = n;
        }
        prev = 2;
        for (int m = 0; m <= 90; m += 5)
        {
            double n = DayNight.Night(new TimeSpan(5, 0, 0) + TimeSpan.FromMinutes(m));
            Assert.True(n <= prev);
            prev = n;
        }
    }

    [Fact]
    public void TimesPastMidnightWrapAround()
    {
        Assert.Equal(DayNight.Night(new TimeSpan(3, 0, 0)), DayNight.Night(TimeSpan.FromHours(27)));
        Assert.Equal(DayNight.Night(new TimeSpan(12, 0, 0)), DayNight.Night(TimeSpan.FromHours(-12)));
    }
}
