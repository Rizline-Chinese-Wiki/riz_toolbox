using System.Runtime.InteropServices;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    /// <summary>Copies a native string while its temporary root is recorded in
    /// bounded process responsibility, including an unconfirmed free.</summary>
    internal string TextWithPublication(IntPtr value)
    {
        VerifyAccess(); if (value == IntPtr.Zero) return "";
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            var rooted = call.Root(() => Root(value)).Target;
            if (Describe(_classType(_objectClass(rooted))).Name != "System.String")
                throw new UnsupportedSignatureException("Text publication requires an exact System.String object.");
            var length = _stringLength(rooted);
            if (length < 0 || length > 4 * 1024 * 1024)
                throw new UnsupportedSignatureException("String length exceeds the runtime read limit.");
            return Marshal.PtrToStringUni(_stringChars(rooted), length) ?? "";
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    /// <summary>Copies a validated value/reference-token field while the temporary owner
    /// root is held in bounded process responsibility. No object ownership is transferred.</summary>
    internal T ReadWithPublication<T>(IntPtr owner, string field) where T : struct
    {
        VerifyAccess();
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            RequireObject(owner); call.Root(() => Root(owner));
            return ReadField<T>(owner, Field(_objectClass(owner), field), false);
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    /// <summary>Validates the native return before entry, roots the actual boxed result,
    /// and copies it before independently cleaning every temporary guard/owned input.
    /// Unknown frees retain records; capacity/thread rejection leaves inputs caller-owned.</summary>
    internal T InvokeValueWithPublication<T>(IntPtr method, IntPtr receiver, params Arg[] args) where T : struct
        => InvokeValueWithPublicationCore<T>(method, receiver, false, args);

    /// <summary>Checks managed admission at the final invoke boundary and before
    /// consuming a returned box. A rejected unconsumed value remains GC-owned;
    /// only acquired handles and arguments have native cleanup responsibility.</summary>
    internal T InvokeValueWithPublicationGuarded<T>(IntPtr method, IntPtr receiver, Action guard, params Arg[] args) where T : struct
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard);
        return InvokeValueWithPublicationCore<T>(method, receiver, false, args, guard);
    }

    /// <summary>Copies only the already-whitelisted four-byte Scene.m_Handle layout.
    /// General value publication still rejects substituting Int32 for native structs.</summary>
    internal int InvokeSceneHandleWithPublication(IntPtr method, IntPtr receiver)
        => InvokeValueWithPublicationCore<int>(method, receiver, true, Array.Empty<Arg>());

    private T InvokeValueWithPublicationCore<T>(IntPtr method, IntPtr receiver, bool sceneHandle, Arg[] args, Action? guard = null) where T : struct
    {
        VerifyAccess(); guard?.Invoke();
        var call = new NativePublicationCall(VerifyAccess, args);
        try
        {
            if (method == IntPtr.Zero) throw new MissingMethodException("IL2CPP method pointer is null.");
            guard?.Invoke(); var returnType = _returnType(method); guard?.Invoke();
            var returns = Describe(returnType, guard);
            if (returns.Kind is NativeValueKind.Reference or NativeValueKind.Void)
                throw new UnsupportedSignatureException("Value publication requires a boxed non-reference native value return.");
            if (sceneHandle && returns.Name != "UnityEngine.SceneManagement.Scene")
                throw new UnsupportedSignatureException("Scene handle publication requires the exact native Scene return type.");
            NativeAbi.ValidateValue(typeof(T), returns, allowSceneProjection: sceneHandle);
            var guards = (receiver == IntPtr.Zero ? 0 : 1) + 1; // actual boxed result
            foreach (var argument in args) if (argument.IsReference && argument.Pointer != IntPtr.Zero) guards++;
            if (guards > NativePublicationCall.MaximumRoots)
                throw new UnsupportedSignatureException("Value publication exceeds its fixed temporary-root capacity before native entry.");
            var result = InvokeCore(method, receiver, null, call, false, args, guard, guard, guard);
            guard?.Invoke();
            RequireObject(result); guard?.Invoke();
            var root = call.Root(() => Root(result)); guard?.Invoke();
            var value = root.Target; guard?.Invoke();
            var cls = _objectClass(value);
            guard?.Invoke();
            var valueType = _isValueType(cls); guard?.Invoke();
            if (!valueType) throw new UnsupportedSignatureException("Native value invocation returned an object reference.");
            var boxedType = _classType(cls); guard?.Invoke();
            var actual = Describe(boxedType, guard); NativeAbi.ValidateValue(typeof(T), actual, allowSceneProjection: sceneHandle);
            guard?.Invoke();
            if (actual.Name != returns.Name)
                throw new UnsupportedSignatureException("Native boxed result differs from the declared exact return type.");
            var output = _unbox(value); guard?.Invoke();
            return ReadValue<T>(output);
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }
}
