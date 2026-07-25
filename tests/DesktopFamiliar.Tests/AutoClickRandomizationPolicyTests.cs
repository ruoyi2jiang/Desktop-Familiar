using DesktopFamiliar.Services.AutoClick;
using DrawingRectangle = System.Drawing.Rectangle;

namespace DesktopFamiliar.Tests;

public sealed class AutoClickRandomizationPolicyTests
{
    [Fact]
    public void RandomTarget_AlwaysStaysInsideTheSelectedArea()
    {
        var area = new DrawingRectangle(100, 200, 25, 14);
        var random = new Random(42);

        for (var index = 0; index < 100; index++)
        {
            var target = AutoClickRandomizationPolicy.GetRandomTarget(area, random);

            Assert.InRange(target.X, area.Left, area.Right - 1);
            Assert.InRange(target.Y, area.Top, area.Bottom - 1);
        }
    }

    [Fact]
    public void RandomInterval_AlwaysStaysInsideTheConfiguredInclusiveRange()
    {
        var random = new Random(7);

        for (var index = 0; index < 100; index++)
        {
            var interval = AutoClickRandomizationPolicy.GetRandomInterval(125, 180, random);

            Assert.InRange(interval, 125, 180);
        }
    }

    [Fact]
    public void RandomInterval_UsesOneValueWhenBoundsMatch()
    {
        Assert.Equal(250, AutoClickRandomizationPolicy.GetRandomInterval(250, 250, new Random(1)));
    }
}
