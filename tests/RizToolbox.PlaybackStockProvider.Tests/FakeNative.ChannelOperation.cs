using System.Diagnostics;
using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

// The existing stock fixture supplies the native originals and actual writer
// fences. These additions count API samples, record retained handle identities,
// and inject at actual getter or source-callback boundaries in this test shim.
sealed partial class FakeNative
{
    internal int ChannelMediaReads;
    internal Action? DuringChannelMediaRead;
    internal int ChannelAudioDspReads, ChannelClipReads, ChannelWholeCaptureClipReads;
    internal Func<string?, bool>? ChannelReadException;
    internal Action? DuringChannelFinalCaptureClipRead;
    internal string[] ChannelFinalCaptureStack = Array.Empty<string>();
    internal Exception? ChannelFinalCaptureInjectionError;
    internal IntPtr ChannelFinalCaptureClipReceiver, ChannelFinalCaptureClipResult;
    internal int ChannelFinalCaptureThread;
    internal Exception? ChannelSourceCallbackInjectionError;
    internal string? ChannelSourceCallbackInjectionStage;

    internal KeyValuePair<uint, IntPtr>[] CaptureChannelRootHandles(IEnumerable<uint> ids)
        => ids.Select(id => new KeyValuePair<uint, IntPtr>(id, _handles[id])).ToArray();
    internal bool RetainsChannelRootHandles(KeyValuePair<uint, IntPtr>[] roots)
        => roots.All(row => _handles.TryGetValue(row.Key, out var target) && target == row.Value);

    internal void RunChannelSourceCallbackInjection(string stage, Action inject)
    {
        // The real source bridge catches callback failures. Keep the first
        // fixture failure for the test to rethrow after the SDK command returns.
        try { inject(); }
        catch (Exception error)
        {
            ChannelSourceCallbackInjectionStage ??= stage;
            ChannelSourceCallbackInjectionError ??= error;
            throw;
        }
    }

    internal void ObserveChannelOperationSamples()
    {
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(ChannelOperationInvoke));
        GameplayApi = new NativeRuntime(ExportPointer);
        GameplayApi.BindCurrentThread();
    }

    private IntPtr ChannelOperationInvoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        var name = _gameplayMethods.TryGetValue(method, out var row) ? row.Name : null;
        if (name == "get_time") ++ChannelMediaReads;
        if (name == "get_dspTime") ++ChannelAudioDspReads;
        if (name == "get_clip") ++ChannelClipReads;
        var result = RootedStockInvoke(method, instance, arguments, exception);
        if (name == "get_time") DuringChannelMediaRead?.Invoke();
        if (ChannelReadException?.Invoke(name) == true)
        {
            // Report an actual runtime_invoke exception result across the
            // native delegate boundary. Do not throw managed exceptions from
            // an unmanaged callback or counterfeit a successful boxed value.
            Marshal.WriteIntPtr(exception, MakeString("Controlled completed-writer API read failure."));
            return IntPtr.Zero;
        }
        if (name == "get_clip" && DuringChannelFinalCaptureClipRead is { } inject)
        {
            // The actual service performs two whole-capture checks after its
            // playback sample. Locate their real SameCapture stack so earlier
            // provider/accessor get_clip calls cannot trigger this boundary.
            var serviceFrames = new StackTrace(false).GetFrames()
                .Select(frame => frame.GetMethod())
                .Where(method => method?.DeclaringType == typeof(GameplayService))
                .Select(method => method!.Name).ToArray();
            if (serviceFrames.Contains("SameCapture") && serviceFrames.Contains("Read")
                && ++ChannelWholeCaptureClipReads == 2)
            {
                DuringChannelFinalCaptureClipRead = null;
                ChannelFinalCaptureStack = serviceFrames;
                ChannelFinalCaptureClipReceiver = instance;
                ChannelFinalCaptureClipResult = result;
                ChannelFinalCaptureThread = Environment.CurrentManagedThreadId;
                // SameCapture catches native getter errors. Retain an injection
                // assertion failure so the outer test cannot mistake it for a
                // real native fault successfully caught by the product.
                try { inject(); } // The actual get_clip return value is already captured.
                catch (Exception error) { ChannelFinalCaptureInjectionError = error; throw; }
            }
        }
        return result;
    }

    internal IntPtr CreateChannelSuccessorPlayer()
    {
        var create = Marshal.GetDelegateForFunctionPointer<RootedChild>(
            NativeLibrary.GetExport(_stockModule, "StockFixtureCreatePlayer"));
        var player = create(GameplaySource);
        _boxedTypes[player] = _boxedTypes[GameplayPlayer];
        Marshal.WriteIntPtr(player, ClassOf(_boxedTypes[player]));
        return player;
    }
}
