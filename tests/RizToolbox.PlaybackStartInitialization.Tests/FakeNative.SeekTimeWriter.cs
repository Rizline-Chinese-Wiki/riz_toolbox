using System.Runtime.InteropServices;
using RizToolbox.Runtime;

// The fixed two-image fixture executes the real wrapper, lower original and
// native reservation. This shim only observes the actual IL2CPP API boundary.
sealed partial class FakeNative
{
    internal Action<string>? DuringSeekTimeRead;
    internal Func<string, bool>? SeekTimeReadException;
    internal int SeekTimeApiReads;
    internal Exception? SeekTimeInjectionFailure;

    internal void ObserveSeekTimeReads()
    {
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(SeekTimeInvoke));
        GameplayApi = new NativeRuntime(ExportPointer);
        GameplayApi.BindCurrentThread();
    }

    private IntPtr SeekTimeInvoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        var name = _gameplayMethods.TryGetValue(method, out var row) ? row.Name : null;
        var result = UnityInvoke(method, instance, arguments, exception);
        if (name is null || !name.StartsWith("get_", StringComparison.Ordinal)) return result;
        ++SeekTimeApiReads;
        try { DuringSeekTimeRead?.Invoke(name); }
        catch (Exception error)
        {
            // A managed assertion must not escape an unmanaged callback or be
            // mistaken for the product's expected readback refusal.
            SeekTimeInjectionFailure ??= error;
            Marshal.WriteIntPtr(exception, MakeString("Seek time test injection failed."));
            return IntPtr.Zero;
        }
        if (SeekTimeReadException?.Invoke(name) == true)
        {
            Marshal.WriteIntPtr(exception, MakeString("Controlled completed SetTime API read failure."));
            return IntPtr.Zero;
        }
        return result;
    }
}
