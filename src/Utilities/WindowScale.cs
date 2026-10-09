namespace Prowl.Launcher;

internal readonly record struct WindowScale(float Rendering, float Input)
{
    internal static WindowScale Calculate(float interfaceScale, float contentScale, float framebufferRatio)
    {
        interfaceScale = float.IsFinite(interfaceScale) ? Math.Clamp(interfaceScale, 0.5f, 1.5f) : 1;
        framebufferRatio = float.IsFinite(framebufferRatio) && framebufferRatio > 0 ? framebufferRatio : 1;
        contentScale = float.IsFinite(contentScale) && contentScale > 0 ? contentScale : framebufferRatio;
        float rendering = contentScale * interfaceScale;
        // Mouse coordinates use window units; drawing uses framebuffer pixels.
        return new WindowScale(rendering, rendering / framebufferRatio);
    }
}
