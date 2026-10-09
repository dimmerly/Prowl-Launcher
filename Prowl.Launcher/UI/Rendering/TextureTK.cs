// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using OpenTK.Graphics.OpenGL4;

using Prowl.Vector;
using Prowl.Vector.Geometry;


namespace Prowl.Launcher;

internal sealed class TextureTK(int glHandle) : IDisposable
{
    public readonly int Handle = glHandle;

    public uint Width;
    public uint Height;


    public static TextureTK FromImage(Prowl.Aperture.Image image)
    {
        TextureTK texture = CreateNew((uint)image.Width, (uint)image.Height);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, image.Width, image.Height,
            PixelFormat.Rgba, PixelType.UnsignedByte, image.Pixels.ToArray());
        return texture;
    }

    // Create a new texture with the specified width and height
    // This is used for when we want to create a texture from scratch, like for a fonts.
    // This is a bit different from the LoadFromFile method, since we don't have any pixels to load.
    // We just create an empty texture with the specified width and height.
    public static TextureTK CreateNew(uint width, uint height)
    {
        // Generate handle
        int handle = GL.GenTexture();
        // Bind the handle
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, handle);
        // Create empty texture
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, (int)width, (int)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        // Set texture parameters 
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return new TextureTK(handle) { Width = width, Height = height };
    }

    // Set the texture data for a specific region of the texture
    // This is used for when we want to update a specific region of the texture
    public void SetData(IntRect bounds, byte[] data)
    {
        // Bind the texture
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, Handle);
        // Set the texture data
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, bounds.Min.X, bounds.Min.Y, bounds.Size.X, bounds.Size.Y, PixelFormat.Rgba, PixelType.UnsignedByte, data);
    }

    // Activate texture
    // Multiple textures can be bound, if your shader needs more than just one.
    // If you want to do that, use GL.ActiveTexture to set which slot GL.BindTexture binds to.
    // The OpenGL standard requires that there be at least 16, but there can be more depending on your graphics card.
    public void Use(TextureUnit unit)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2D, Handle);
    }

    // Dispose of the texture
    public void Dispose()
    {
        // Delete the texture
        GL.DeleteTexture(Handle);
    }
}
