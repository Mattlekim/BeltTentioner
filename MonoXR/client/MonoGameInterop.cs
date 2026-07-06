using System.Reflection;

namespace MonoXR.Client;

/// <summary>
/// Pulls the raw D3D11 native pointers out of MonoGame's WindowsDX backend via
/// reflection, so MonoXR.Client needs no compile-time dependency on MonoGame or
/// SharpDX. MonoGame keeps its device in <c>GraphicsDevice._d3dDevice</c> and
/// each texture's <c>ID3D11Resource</c> behind <c>Texture.GetTexture()</c>,
/// both as SharpDX wrappers exposing <c>NativePointer</c>.
///
/// These pointers are borrowed: MonoGame owns their lifetime. They stay valid
/// until the device is reset/recreated (e.g. after a resolution change a
/// RenderTarget2D may be recreated — just call <see cref="GetTexturePointer"/>
/// again each frame; it is cheap after the first call).
/// </summary>
public static class MonoGameInterop
{
    private static readonly Dictionary<Type, MethodInfo> _getTextureCache = new();
    private static readonly Dictionary<Type, PropertyInfo> _nativePtrCache = new();
    private static FieldInfo? _d3dDeviceField;

    /// <summary>
    /// Native ID3D11Device* of a Microsoft.Xna.Framework.Graphics.GraphicsDevice.
    /// Pass the result to <see cref="OverlayManager(IntPtr)"/>.
    /// </summary>
    public static IntPtr GetDevicePointer(object graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        if (_d3dDeviceField is null)
        {
            _d3dDeviceField = FindField(graphicsDevice.GetType(), "_d3dDevice")
                ?? throw new NotSupportedException(
                    "GraphicsDevice has no '_d3dDevice' field — not the MonoGame WindowsDX backend?");
        }
        object sharpDxDevice = _d3dDeviceField.GetValue(graphicsDevice)
            ?? throw new InvalidOperationException("MonoGame's D3D11 device is not created yet.");
        return NativePointer(sharpDxDevice);
    }

    /// <summary>
    /// Native ID3D11Texture2D* of a Microsoft.Xna.Framework.Graphics.Texture
    /// (e.g. a RenderTarget2D). Pass the result to <see cref="Overlay.Update(IntPtr)"/>.
    /// </summary>
    public static IntPtr GetTexturePointer(object texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        var type = texture.GetType();
        if (!_getTextureCache.TryGetValue(type, out var getTexture))
        {
            getTexture = FindMethod(type, "GetTexture")
                ?? throw new NotSupportedException(
                    $"{type.Name} has no 'GetTexture()' method — not the MonoGame WindowsDX backend?");
            _getTextureCache[type] = getTexture;
        }
        object sharpDxResource = getTexture.Invoke(texture, null)
            ?? throw new InvalidOperationException("MonoGame texture has no native resource yet.");
        return NativePointer(sharpDxResource);
    }

    private static IntPtr NativePointer(object sharpDxObject)
    {
        var type = sharpDxObject.GetType();
        if (!_nativePtrCache.TryGetValue(type, out var prop))
        {
            prop = type.GetProperty("NativePointer", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new NotSupportedException($"{type.Name} has no NativePointer property.");
            _nativePtrCache[type] = prop;
        }
        return (IntPtr)prop.GetValue(sharpDxObject)!;
    }

    private static FieldInfo? FindField(Type? type, string name)
    {
        for (; type is not null; type = type.BaseType)
        {
            var f = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f is not null) return f;
        }
        return null;
    }

    private static MethodInfo? FindMethod(Type? type, string name)
    {
        for (; type is not null; type = type.BaseType)
        {
            var m = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null, Type.EmptyTypes, modifiers: null);
            if (m is not null) return m;
        }
        return null;
    }
}
