namespace Prowl.Launcher;

internal readonly record struct WindowScale(float Rendering, float Input)
{
    /// <summary>The interface size setting limited to the supported range.</summary>
    internal static float ClampInterfaceScale(float scale) =>
        float.IsFinite(scale)
            ? Math.Clamp(scale, Constants.Defaults.MinUiScale, Constants.Defaults.MaxUiScale)
            : Constants.Defaults.UiScale;

    internal static WindowScale Calculate(float interfaceScale, float contentScale, float framebufferRatio)
    {
        interfaceScale = ClampInterfaceScale(interfaceScale);
        framebufferRatio = float.IsFinite(framebufferRatio) && framebufferRatio > 0 ? framebufferRatio : 1;
        contentScale = float.IsFinite(contentScale) && contentScale > 0 ? contentScale : framebufferRatio;
        float rendering = contentScale * interfaceScale;
        // Mouse coordinates use window units; drawing uses framebuffer pixels.
        return new WindowScale(rendering, rendering / framebufferRatio);
    }
}
