using System.Runtime.InteropServices;
using RizToolbox.Runtime;

sealed partial class FakeNative
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr InitializerCreate(IntPtr progress,IntPtr clip);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr InitializerMethod();
    internal IntPtr ConfigureInitializer(IntPtr module)
    {
        ConfigureRootedStock(module);
        var startType=AddType("InLevel.ProgressControl.<Start>d__25",18,false,8);
        var progressType=_types.Single(row=>row.Value.Name=="InLevel.ProgressControl").Key;
        var make=Marshal.GetDelegateForFunctionPointer<InitializerCreate>(NativeLibrary.GetExport(module,"StockFixtureCreateStart"));
        var value=make(GameplayProgress,GameplayClip);
        _boxedTypes[value]=startType;Marshal.WriteIntPtr(value,ClassOf(startType));
        var getter=Marshal.GetDelegateForFunctionPointer<InitializerMethod>(NativeLibrary.GetExport(module,"StockMethodStartMoveNext"));
        Marshal.WriteIntPtr(getter(),32,ClassOf(startType));
        GameplayField("initializer.state",startType,"<>1__state",Int32,value,false);
        var state=_gameplayFields["initializer.state"].Field;
        _fieldMetadata[state]=("<>1__state",Int32,0x10);
        GameplayField("initializer.progress",startType,"<>4__this",progressType,value,GameplayProgress);
        var progress=_gameplayFields["initializer.progress"].Field;
        _fieldMetadata[progress]=("<>4__this",progressType,0x20);
        return value;
    }
    internal void AdoptActuallyInitializedPlayer()
    {
        // Register synthetic metadata for the object allocated and stored by
        // the real Start original. Do not synthesize either native field store.
        var candidate = Marshal.ReadIntPtr(GameplayProgress, 0x38);
        if (candidate == IntPtr.Zero || candidate == GameplayPlayer)
            throw new InvalidOperationException("Actual Start did not replace the player.");
        _boxedTypes[candidate] = _boxedTypes[GameplayPlayer];
        if (Marshal.ReadIntPtr(candidate) != ClassOf(_boxedTypes[candidate]))
            throw new InvalidOperationException("Native allocation did not inherit the actual player class.");
        GameplayPlayer = candidate;
    }
}
