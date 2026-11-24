using RizToolbox.Runtime;

namespace RizToolbox.Adapter.GameAppearance;

/// <summary>Every method binds its declared receiver, complete parameter list and return type. No inherited color fallback.</summary>
internal sealed record NativeAppearanceColorAccessors(IntPtr Read, IntPtr Write)
{
    internal Rgba ReadRendered(NativeRuntime api, NativeRuntime.RootHandle renderer)
        => api.Unbox<Rgba>(api.Invoke(Read, renderer.Target));
    internal Rgba ReadTrack(NativeRuntime api, NativeRuntime.RootHandle line, NativeExactReadField cache,
        NativeRuntime.RootHandle renderer)
    {
        // Check the actual game-owned cache through official field metadata, then read
        // the visible renderer property. Read-back cannot succeed on the cache alone.
        _ = api.ReadExactField<Rgba>(line, cache);
        return ReadRendered(api, renderer);
    }
    internal void WriteColor(NativeRuntime api, NativeRuntime.RootHandle owner, Rgba color)
        => api.Invoke(Write, owner.Target, NativeRuntime.Arg.Value(color));

    internal bool TryReadCoupled(NativeRuntime api, NativeRuntime.RootHandle owner, NativeExactReadField cache,
        NativeRuntime.RootHandle renderer, out Rgba color)
    {
        var cached = api.ReadExactField<Rgba>(owner, cache);
        color = ReadRendered(api, renderer);
        return cached == color && float.IsFinite(color.R) && float.IsFinite(color.G)
            && float.IsFinite(color.B) && float.IsFinite(color.Alpha);
    }

    internal Rgba ReadCoupled(NativeRuntime api, NativeRuntime.RootHandle owner, NativeExactReadField cache,
        NativeRuntime.RootHandle renderer)
        => TryReadCoupled(api, owner, cache, renderer, out var color) ? color
            : throw new InvalidOperationException("The game color cache and rendered color differ; a reversible single-color binding is unavailable.");

    internal void WriteCoupled(NativeRuntime api, NativeRuntime.RootHandle owner, NativeExactReadField cache,
        NativeRuntime.RootHandle renderer, Rgba color, Action requireCurrent)
    {
        requireCurrent();
        var before = ReadCoupled(api, owner, cache, renderer);
        try
        {
            requireCurrent(); WriteColor(api, owner, color); requireCurrent();
            if (ReadCoupled(api, owner, cache, renderer) != color)
                throw new InvalidOperationException("The game setter did not update both the color cache and rendered color.");
        }
        catch
        {
            // A partially applied setter still owns only its observed before/requested
            // values. Restore both effects through the same game setter, provided the
            // run and full pool graph still match and neither value became external.
            requireCurrent();
            var cached = api.ReadExactField<Rgba>(owner, cache); var rendered = ReadRendered(api, renderer);
            if ((cached == before || cached == color) && (rendered == before || rendered == color))
            {
                requireCurrent(); WriteColor(api, owner, before); requireCurrent();
                if (ReadCoupled(api, owner, cache, renderer) != before)
                    throw new InvalidOperationException("The game setter could not restore both original color values.");
            }
            throw;
        }
    }

    internal static NativeAppearanceColorAccessors Bind(NativeRuntime api, IntPtr readClass, string readNamespace,
        string readType, string getter, IntPtr writeClass, string writeNamespace, string writeType, string setter)
        => Bind((name, parameters, writing) => api.Method(writing ? writeClass : readClass, name, parameters.Length, false, parameters),
            (method, name, writing) => api.IsMethod(method, writing ? writeNamespace : readNamespace, writing ? writeType : readType, name),
            api.DescribeReturnType, getter, setter);

    internal static NativeAppearanceColorAccessors Bind(Func<string, string[], bool, IntPtr> resolve,
        Func<IntPtr, string, bool, bool> declaredByExpectedType, Func<IntPtr, string> returnType,
        string getter, string setter)
    {
        var read = resolve(getter, Array.Empty<string>(), false);
        var write = resolve(setter, new[] { "UnityEngine.Color" }, true);
        if (read == IntPtr.Zero || write == IntPtr.Zero || !declaredByExpectedType(read, getter, false)
            || !declaredByExpectedType(write, setter, true) || returnType(read) != "UnityEngine.Color" || returnType(write) != "System.Void")
            throw new UnsupportedSignatureException("Appearance accessors differ from the exact declared getter/setter and Color/void signatures.");
        return new(read, write);
    }
}
