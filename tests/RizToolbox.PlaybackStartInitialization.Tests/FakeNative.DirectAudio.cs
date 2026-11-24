using System.Runtime.InteropServices;
using RizToolbox.Runtime;

sealed partial class FakeNative
{
    private readonly Dictionary<IntPtr, (uint Kind, IntPtr Entry, bool Static)> _directAudioMethods = new();
    internal int DirectAudioRuntimeInvocations;
    internal void ConfigureDirectAudio(IntPtr module)
    {
        var sourceType = _types.Single(row => row.Value.Name == "UnityEngine.AudioSource").Key;
        var unsigned = _types.FirstOrDefault(row => row.Value.Name == "System.UInt64").Key;
        if (unsigned == IntPtr.Zero) unsigned = AddType("System.UInt64", 11, true, 8);
        var exports = new[] { "AudioPlayHelper", "AudioPlayDelayed", "AudioPlay", "AudioStop", "AudioStopBoolean", "AudioSetLoop" };
        var names = new[] { "PlayHelper", "Play", "Play", "Stop", "Stop", "set_loop" };
        var arguments = new[] { new[] { sourceType, unsigned }, new[] { _gameplayDouble }, Array.Empty<IntPtr>(),
            Array.Empty<IntPtr>(), new[] { Boolean }, new[] { Boolean } };
        for (var index = 0; index < exports.Length; ++index)
        {
            var method = Marshal.GetDelegateForFunctionPointer<RootedMethod>(NativeLibrary.GetExport(module, "StockMethod" + exports[index]))();
            var cls = ClassOf(sourceType);
            foreach (var prior in _gameplayMethods.Where(row => row.Value.Class == cls && row.Value.Name == names[index]
                && row.Value.Static == (index == 0) && row.Value.Parameters.SequenceEqual(arguments[index])).Select(row => row.Key).ToArray())
                _gameplayMethods.Remove(prior);
            _gameplayMethods[method] = new(cls, names[index], index == 0, Void, arguments[index]);
            Marshal.WriteIntPtr(method, 32, cls);
            _directAudioMethods.Add(method, ((uint)(index == 0 ? 8 : index == 1 ? 3 : index < 4 ? 1 : 5),
                NativeLibrary.GetExport(module, "Stock" + exports[index]), index == 0));
        }
        // NativeRuntime retains its actual runtime-invoke export at creation.
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(DirectAudioInvoke));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
    }
    private IntPtr DirectAudioInvoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        if (!_directAudioMethods.TryGetValue(method, out var row)) return SharedConstructorInvoke(method, instance, arguments, exception);
        Marshal.WriteIntPtr(exception, IntPtr.Zero);
        var source = row.Static ? Marshal.ReadIntPtr(arguments) : instance;
        AllGameplayReceiversRooted &= HasRootFor(source);
        var raw = row.Kind == 1 ? IntPtr.Zero : Marshal.ReadIntPtr(arguments, row.Static ? 8 : 0);
        var bits = row.Kind == 1 ? 0UL : row.Kind == 5 ? Marshal.ReadByte(raw) : unchecked((ulong)Marshal.ReadInt64(raw));
        ++DirectAudioRuntimeInvocations;
        var invoke = Marshal.GetDelegateForFunctionPointer<RootedInvoke>(NativeLibrary.GetExport(_stockModule, "StockFixtureInvoke"));
        if (invoke(row.Kind, row.Entry, source, bits, method, out var code) != 1)
            throw new InvalidOperationException("Actual direct audio Runtime invocation failed: " + code.ToString("x"));
        return IntPtr.Zero;
    }
}
