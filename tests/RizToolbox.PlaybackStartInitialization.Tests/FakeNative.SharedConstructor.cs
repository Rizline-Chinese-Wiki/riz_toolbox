using System.Runtime.InteropServices;
using RizToolbox.Runtime;

sealed partial class FakeNative
{
    private IntPtr _sharedConstructor;
    internal int SharedConstructorInvocations;
    private HashSet<uint>? _sharedConstructorAuditBaseline;
    private uint[] _sharedConstructorTemporaryHandles = Array.Empty<uint>();
    internal int SharedConstructorTemporaryReceiverRoots, SharedConstructorTemporaryReferenceRoots;
    internal bool SharedConstructorTemporaryRootsReleased => _sharedConstructorTemporaryHandles.All(id => !_handles.ContainsKey(id));
    internal void BeginSharedConstructorRootAudit()
    {
        _sharedConstructorAuditBaseline = _handles.Keys.ToHashSet();
        _sharedConstructorTemporaryHandles = Array.Empty<uint>();
        SharedConstructorTemporaryReceiverRoots = SharedConstructorTemporaryReferenceRoots = 0;
    }
    internal IntPtr CreateSharedConstructorSource()
    {
        var source = Marshal.GetDelegateForFunctionPointer<RootedCreate>(NativeLibrary.GetExport(_stockModule, "StockFixtureCreateSource"))();
        _boxedTypes[source] = _boxedTypes[GameplaySource];
        Marshal.WriteIntPtr(source, ClassOf(_boxedTypes[source]));
        return source;
    }
    internal IntPtr ConfigureSharedConstructor(IntPtr module)
    {
        var playerType = _types.Single(row => row.Value.Name == "Pigeon.MUGToolkit.Core.MUGMusicPlayer").Key;
        var sourceType = _types.Single(row => row.Value.Name == "UnityEngine.AudioSource").Key;
        _sharedConstructor = Marshal.GetDelegateForFunctionPointer<InitializerMethod>(NativeLibrary.GetExport(module, "StockMethodPlayerConstructor"))();
        _gameplayMethods[_sharedConstructor] = new(ClassOf(playerType), ".ctor", false, Void, new[] { sourceType });
        Marshal.WriteIntPtr(_sharedConstructor, 32, ClassOf(playerType));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(SharedConstructorInvoke));
        // NativeRuntime caches the export delegate in its constructor. Retain a
        // runtime that actually routes .ctor calls through this new export.
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
        return _sharedConstructor;
    }
    private IntPtr SharedConstructorInvoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        if (TryProgressResourceInvoke(method, instance, arguments, exception, out var resourceResult)) return resourceResult;
        if (method != _sharedConstructor) return RootedStockInvoke(method, instance, arguments, exception);
        Marshal.WriteIntPtr(exception, IntPtr.Zero);
        var reference = Marshal.ReadIntPtr(arguments);
        ++SharedConstructorInvocations;
        AllGameplayReceiversRooted &= HasRootFor(instance) && (reference == IntPtr.Zero || HasRootFor(reference));
        if (_sharedConstructorAuditBaseline is { } baseline)
        {
            var temporary = _handles.Where(row => !baseline.Contains(row.Key) && (row.Value == instance || row.Value == reference)).ToArray();
            _sharedConstructorTemporaryHandles = temporary.Select(row => row.Key).ToArray();
            SharedConstructorTemporaryReceiverRoots = temporary.Count(row => row.Value == instance);
            SharedConstructorTemporaryReferenceRoots = temporary.Count(row => row.Value == reference);
        }
        var invoke = Marshal.GetDelegateForFunctionPointer<RootedInvoke>(NativeLibrary.GetExport(_stockModule, "StockFixtureInvoke"));
        if (invoke(4, NativeLibrary.GetExport(_stockModule, "StockPlayerConstructor"), instance,
            unchecked((ulong)reference.ToInt64()), method, out var code) != 1)
            throw new InvalidOperationException("Actual shared constructor Runtime invocation failed: " + code.ToString("x"));
        return IntPtr.Zero;
    }
}
