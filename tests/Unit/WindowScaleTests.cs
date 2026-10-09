using Xunit;

namespace Prowl.Launcher.Test;

public sealed class WindowScaleTests
{
    [Theory]
    [InlineData(1, 1, 1, 1, 1)]
    [InlineData(1, 1.25f, 1, 1.25f, 1.25f)]
    [InlineData(1, 1.5f, 1, 1.5f, 1.5f)]
    [InlineData(1.5f, 1.25f, 1, 1.875f, 1.875f)]
    [InlineData(1, 2, 2, 2, 1)]
    [InlineData(1.5f, 2, 2, 3, 1.5f)]
    [InlineData(1, 1.5f, 2, 1.5f, 0.75f)]
    public void MonitorScalingAndInterfaceSizeUseTheCorrectDrawingAndMouseCoordinates(
        float interfaceScale, float contentScale, float framebufferRatio, float rendering, float input)
    {
        WindowScale scale = WindowScale.Calculate(interfaceScale, contentScale, framebufferRatio);
        Assert.Equal(rendering, scale.Rendering);
        Assert.Equal(input, scale.Input);
        // A point drawn at this logical position must receive clicks at the same position.
        float windowPosition = 100 * scale.Rendering / framebufferRatio;
        Assert.Equal(100, windowPosition / scale.Input);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void MissingMonitorScalingFallsBackToTheFramebufferRatio(float contentScale) => Assert.Equal(new WindowScale(2, 1), WindowScale.Calculate(1, contentScale, 2));

    [Fact]
    public void MovingBetweenMonitorsChangesScaleWithoutChangingTheUserPreference()
    {
        const float preference = 1.15f;
        WindowScale first = WindowScale.Calculate(preference, 1, 1);
        WindowScale second = WindowScale.Calculate(preference, 1.5f, 1);
        Assert.Equal(first.Rendering * 1.5f, second.Rendering);
        Assert.Equal(preference, WindowScale.Calculate(preference, 1, 1).Rendering);
    }
}
