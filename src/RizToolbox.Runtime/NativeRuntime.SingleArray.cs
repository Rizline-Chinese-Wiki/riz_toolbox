using System.Runtime.InteropServices;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    internal const int MaximumSingleArraySamples = 16 * 1024 * 1024;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ArrayNew(IntPtr elementClass, ulong length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ArrayClassGet(IntPtr elementClass, uint rank);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ArraySize(IntPtr array);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ArrayElementSize(IntPtr arrayClass);

    /// <summary>Creates only a pinned, one-dimensional System.Single[] owned by this runtime's main thread.
    /// Size and header are derived from exported APIs and cross-checked; no fixed array-header offset is used.</summary>
    internal RootHandle CreateSingleArray(float[] samples)
    {
        ValidateSamples(samples); VerifyAccess();
        return CreateSingleArrayForClass(samples, Class("mscorlib.dll", "System", "Single"));
    }
    /// <summary>Transfers a validated, copied output before any empty-array temporary root is freed.
    /// The publisher must synchronously record its root; notification transfers responsibility even
    /// if the publisher or later cleanup throws. Unknown temporary frees remain process-owned.</summary>
    internal RootHandle CreateSingleArrayWithPublication(float[] samples, Action<RootHandle> publish)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(publish); ValidateSamples(samples);
        // Reserve before even class lookup can allocate native metadata/string temporaries.
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try { return CreateSingleArrayForClassCore(samples, Class("mscorlib.dll", "System", "Single"), publish, call); }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }
    internal RootHandle CreateSingleArrayForClassWithPublication(float[] samples, IntPtr elementClass, Action<RootHandle> publish)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(publish); ValidateSamples(samples);
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try { return CreateSingleArrayForClassCore(samples, elementClass, publish, call); }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }
    // Narrow export seam for the actual production copy path; tests supply synthetic class/export metadata.
    internal RootHandle CreateSingleArrayForClass(float[] samples, IntPtr elementClass)
        => CreateSingleArrayForClassCore(samples, elementClass, null, null);
    private RootHandle CreateSingleArrayForClassCore(float[] samples, IntPtr elementClass,
        Action<RootHandle>? publish, NativePublicationCall? publication)
    {
        VerifyAccess(); ValidateSamples(samples);
        if (elementClass == IntPtr.Zero) throw new UnsupportedSignatureException("Single array element class is absent.");
        var element = Describe(_classType(elementClass));
        if (element.Kind != NativeValueKind.Single || element.Name != "System.Single" || element.Size != sizeof(float))
            throw new UnsupportedSignatureException("Audio array element must be exactly System.Single with four-byte storage.");
        var arrayClass = Api<ArrayClassGet>("il2cpp_array_class_get")(elementClass, 1);
        if (arrayClass == IntPtr.Zero || _typeCode(_classType(arrayClass)) != 29 || TypeName(_classType(arrayClass)) != "System.Single[]" ||
            Api<ArrayElementSize>("il2cpp_array_element_size")(arrayClass) != sizeof(float))
            throw new UnsupportedSignatureException("Single-dimensional System.Single[] metadata differs from the supported layout.");
        var create = Api<ArrayNew>("il2cpp_array_new");
        var length = Api<ArraySize>("il2cpp_array_length");
        var bytes = Api<ArraySize>("il2cpp_array_get_byte_length");
        var objectSize = Api<ArraySize>("il2cpp_object_get_size");
        RootHandle Pin(IntPtr value)
        {
            RequireObject(value);
            if (_objectClass(value) != arrayClass) throw new UnsupportedSignatureException("Array allocation returned the wrong native class.");
            return new RootHandle(_newHandle(value, true), _freeHandle, _handleTarget, VerifyAccess);
        }
        using var empty = publication is null ? Pin(create(elementClass, 0)) : null;
        var emptyRoot = empty ?? publication!.Root(() => Pin(create(elementClass, 0)));
        uint headerSize = objectSize(emptyRoot.Target);
        if (length(emptyRoot.Target) != 0 || bytes(emptyRoot.Target) != 0 || headerSize < 2 * IntPtr.Size || headerSize > 4096)
            throw new UnsupportedSignatureException("Zero-length Single array does not expose a bounded, consistent header.");
        RootHandle? result = null;
        try
        {
            result = publication is null ? Pin(create(elementClass, (ulong)samples.Length))
                : publication.Root(() => Pin(create(elementClass, (ulong)samples.Length)));
            uint expectedBytes = checked((uint)samples.Length * sizeof(float));
            var target = result.Target;
            if (length(target) != (uint)samples.Length || bytes(target) != expectedBytes ||
                objectSize(target) != checked(headerSize + expectedBytes))
                throw new UnsupportedSignatureException("Single array length, byte length and allocated object size disagree.");
            if (samples.Length != 0) Marshal.Copy(samples, 0, IntPtr.Add(target, checked((int)headerSize)), samples.Length);
            if (publish is not null)
            {
                var transferred = result; publication!.TransferRoot(transferred); result = null;
                publication.BeginPublication(transferred);
                try { publish(transferred); publication.PublicationAccepted(); }
                catch { publication.PublicationFailed(); throw; }
                return transferred;
            }
            var retained = result; result = null; return retained;
        }
        finally { if (publication is null) result?.Dispose(); }
    }
    private static void ValidateSamples(float[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length > MaximumSingleArraySamples) throw new ArgumentOutOfRangeException(nameof(samples), "Single array exceeds the 64 MiB upload limit.");
        foreach (var sample in samples) if (!float.IsFinite(sample)) throw new ArgumentException("Single arrays cannot contain NaN or infinity.", nameof(samples));
    }
}
