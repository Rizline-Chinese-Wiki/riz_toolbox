using RizToolbox.Runtime;

// Adds bounded call/free observation to the existing complete playback graph.
// Receivers, metadata, invocation and all seven owned handles still use NativeRuntime.
sealed partial class FakeNative
{
    internal Action<string, IntPtr>? DuringActualSourceNativeCall, BeforeActualSourceNativeReturn;
    internal int ActualSourceNativeCalls;
    internal readonly List<(string Method, IntPtr Receiver)> ActualSourceCallTrace = new();
    internal readonly List<uint> ActualSourceFreedHandles = new();

    internal void ConfigureActualSourceLifetime()
    {
        ConfigurePlaybackOccurrence();
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(ActualSourceLifetimeInvoke));
        Add("il2cpp_gchandle_free", "FreeHandle", nameof(ActualSourceLifetimeFreeHandle));
        GameplayApi = new NativeRuntime(ExportPointer);
        GameplayApi.BindCurrentThread();
    }

    internal uint[] SnapshotActualSourceHandles() => _handles.Keys.ToArray();

    private IntPtr ActualSourceLifetimeInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception)
    {
        ActualSourceNativeCalls++;
        var name = _gameplayMethods.TryGetValue(method, out var row) ? row.Name : "other";
        ActualSourceCallTrace.Add((name, receiver));
        try
        {
            DuringActualSourceNativeCall?.Invoke(name, receiver);
            return PlaybackOccurrenceInvoke(method, receiver, arguments, exception);
        }
        finally { BeforeActualSourceNativeReturn?.Invoke(name, receiver); }
    }

    private void ActualSourceLifetimeFreeHandle(uint handle)
    {
        ActualSourceFreedHandles.Add(handle);
        FreeHandle(handle);
    }
}
