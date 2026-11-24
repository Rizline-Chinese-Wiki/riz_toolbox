using System.Runtime.InteropServices;
using RizToolbox.Runtime;

internal static partial class ListenerMetadataRegression
{
    internal static (string Name, Action Run)[] TweenDeathCases => new (string Name, Action Run)[] {
        ("Rooted Tween death observation pairs exact official alive cached field and frame metadata without touching the old native object", TweenDeathStableObservation),
        ("Rooted Tween death observation accepts stable zero only as copied evidence and preserves the original strong root", TweenDeathStableZero),
        ("Rooted Tween death observation rejects changed alive frame cached field and actual class across its bounded reads", TweenDeathChangedSample),
        ("Rooted Tween death observation rejects changed pinned Unity method metadata before actual invocation", TweenDeathMethodMetadata),
        ("Rooted Tween death observation rejects foreign class disposed root and worker access before value invocation", TweenDeathAdmission)
    };
    private static void TweenDeathStableObservation()
    {
        using var f = new Fixture(tween: true); f.InitializeTweenDeathMetadata();
        using var root = f.AdmissionApi.Root(f.DeathReceiver); var roots = f.Base.ActiveRoots;
        var observed = f.AdmissionApi.ObserveRootedTweenDeath(root, f.Module);
        Check(observed.ManagedReceiver == f.DeathReceiver.ToInt64() && observed.ManagedClass == f.DeathManagedClass
            && observed.CachedPointerField == f.CachedPointer.Pointer && observed.CachedNative == Fixture.DeathNative.ToInt64()
            && observed.Alive && observed.Frame == 10 && f.DeathAliveReads == 2 && f.DeathFrameReads == 2);
        Check(f.DeathInvocationFailure is null && f.Base.ActiveRoots == roots && root.Target == f.DeathReceiver
            && f.FieldReads >= 5 && Marshal.ReadIntPtr(f.DeathReceiver, 16) == Fixture.DeathNative);
    }
    private static void TweenDeathStableZero()
    {
        using var f = new Fixture(tween: true); f.InitializeTweenDeathMetadata();
        Marshal.WriteIntPtr(f.DeathReceiver, 16, IntPtr.Zero);
        using var root = f.AdmissionApi.Root(f.DeathReceiver);
        var observed = f.AdmissionApi.ObserveRootedTweenDeath(root, f.Module);
        Check(!observed.Alive && observed.CachedNative == 0 && observed.Frame == 10 && f.Base.ActiveRoots == 1
            && root.Target == f.DeathReceiver && f.DeathAliveReads == 2 && f.DeathInvocationFailure is null);
    }
    private static void TweenDeathChangedSample()
    {
        foreach (var change in new[] { "frame", "alive", "cached", "class", "field", "disagreement" })
        {
            using var f = new Fixture(tween: true); f.InitializeTweenDeathMetadata();
            using var root = f.AdmissionApi.Root(f.DeathReceiver);
            if (change == "frame") f.AfterDeathInvocation = () => { if (f.DeathFrameReads == 1) ++f.DeathFrame; };
            if (change == "alive") f.AfterDeathInvocation = () => { if (f.DeathAliveReads == 1) f.DeathAliveOverride = false; };
            if (change == "cached") f.AfterDeathInvocation = () => { if (f.DeathAliveReads == 1) Marshal.WriteIntPtr(f.DeathReceiver, 16, IntPtr.Zero); };
            if (change == "class") f.AfterDeathInvocation = () => { if (f.DeathAliveReads == 1) Marshal.WriteIntPtr(f.DeathReceiver, f.TargetClass); };
            if (change == "field") f.AfterDeathInvocation = () => { if (f.DeathAliveReads == 1) f.CachedPointer.Offset = 24; };
            if (change == "disagreement") f.DeathAliveOverride = false;
            var rejected = false;
            try { f.AdmissionApi.ObserveRootedTweenDeath(root, f.Module); }
            catch (Exception error) when (error is InvalidOperationException or UnsupportedSignatureException) { rejected = true; }
            Check(rejected && f.Base.ActiveRoots == 1 && f.DeathInvocationFailure is null && root.Target == f.DeathReceiver);
        }
    }
    private static void TweenDeathMethodMetadata()
    {
        foreach (var change in new[] { "entry", "static", "generic", "inflated", "return" })
        {
            using var f = new Fixture(tween: true); f.InitializeTweenDeathMetadata();
            using var root = f.AdmissionApi.Root(f.DeathReceiver); var method = f.DeathAliveMethod;
            if (change == "entry") method.Entry = f.EventInvoke.Entry;
            if (change == "static") method.Flags = 0;
            if (change == "generic") method.Generic = true;
            if (change == "inflated") method.Inflated = true;
            if (change == "return") method.Return = f.IntType;
            f.Sync(method); var rejected = false;
            try { f.AdmissionApi.ObserveRootedTweenDeath(root, f.Module); }
            catch (Exception error) when (error is UnsupportedSignatureException or MissingMethodException) { rejected = true; }
            Check(rejected && f.DeathFrameReads == 0 && f.DeathAliveReads == 0 && f.Base.ActiveRoots == 1 && f.DeathInvocationFailure is null);
        }
    }
    private static void TweenDeathAdmission()
    {
        using var f = new Fixture(tween: true); f.InitializeTweenDeathMetadata();
        using var root = f.AdmissionApi.Root(f.DeathReceiver);
        Task.Run(() => Throws<InvalidOperationException>(() => f.AdmissionApi.ObserveRootedTweenDeath(root, f.Module))).GetAwaiter().GetResult();
        Throws<UnsupportedSignatureException>(() => f.AdmissionApi.ObserveRootedTweenDeath(root, IntPtr.Zero));
        using var foreign = f.AdmissionApi.Root(f.Target);
        Throws<UnsupportedSignatureException>(() => f.AdmissionApi.ObserveRootedTweenDeath(foreign, f.Module));
        var disposed = f.AdmissionApi.Root(f.DeathReceiver); disposed.Dispose();
        Throws<TargetExpiredException>(() => f.AdmissionApi.ObserveRootedTweenDeath(disposed, f.Module));
        Check(f.DeathAliveReads == 0 && f.DeathFrameReads == 0 && f.DeathInvocationFailure is null);
    }
    private sealed partial class Fixture
    {
        internal static readonly IntPtr DeathNative = new(unchecked((long)0x8123456789abcdefUL));
        internal IntPtr DeathReceiver;
        internal IntPtr DeathManagedClass => ClassOf(TweenType);
        internal MethodRow DeathAliveMethod = null!;
        private MethodRow _deathFrameMethod = null!;
        internal int DeathFrame = 10, DeathAliveReads, DeathFrameReads;
        internal bool? DeathAliveOverride; internal Action? AfterDeathInvocation; internal string? DeathInvocationFailure;
        private IntPtr _deathBooleanBox, _deathFrameBox;
        internal void InitializeTweenDeathMetadata()
        {
            InitializeAdmissionMetadata();
            var unityObject = _classes.Values.Single(row => row.Name == "UnityEngine.Object").Class;
            var time = ClassOf(AddType("UnityEngine.Time", 18, false, 8));
            DeathAliveMethod = AddMethod(unityObject, "op_Implicit", _boolean, 0x15409B0, _classes[unityObject].Type);
            _deathFrameMethod = AddMethod(time, "get_frameCount", IntType, 0x155EFD0);
            DeathAliveMethod.Flags = _deathFrameMethod.Flags = 0x10; Sync(DeathAliveMethod); Sync(_deathFrameMethod);
            DeathReceiver = Allocate(256); Marshal.WriteIntPtr(DeathReceiver, ClassOf(TweenType)); Marshal.WriteIntPtr(DeathReceiver, 16, DeathNative);
            _deathBooleanBox = Allocate(32); Marshal.WriteIntPtr(_deathBooleanBox, ClassOf(_boolean));
            _deathFrameBox = Allocate(32); Marshal.WriteIntPtr(_deathFrameBox, ClassOf(IntType));
            Bind("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(TweenDeathInvoke));
            Bind("il2cpp_object_unbox", "Ptr1", nameof(TweenDeathUnbox));
            AdmissionApi = new NativeRuntime(name => _exports.TryGetValue(name, out var export) ? Marshal.GetFunctionPointerForDelegate(export) : Base.ExportForListenerTests(name));
            AdmissionApi.BindCurrentThread();
        }
        private IntPtr TweenDeathUnbox(IntPtr value) => IntPtr.Add(value, 16);
        private IntPtr TweenDeathInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception)
        {
            Marshal.WriteIntPtr(exception, IntPtr.Zero);
            IntPtr result;
            if (receiver != IntPtr.Zero) { DeathInvocationFailure = "A death getter received an instance target."; return IntPtr.Zero; }
            if (method == DeathAliveMethod.Pointer)
            {
                ++DeathAliveReads;
                if (Marshal.ReadIntPtr(arguments) != DeathReceiver) { DeathInvocationFailure = "Alive received a foreign wrapper."; return IntPtr.Zero; }
                var alive = DeathAliveOverride ?? Marshal.ReadIntPtr(DeathReceiver, 16) != IntPtr.Zero;
                Marshal.WriteByte(_deathBooleanBox, 16, alive ? (byte)1 : (byte)0); result = _deathBooleanBox;
            }
            else if (method == _deathFrameMethod.Pointer)
            { ++DeathFrameReads; Marshal.WriteInt32(_deathFrameBox, 16, DeathFrame); result = _deathFrameBox; }
            else { DeathInvocationFailure = "An unrelated native/game method was invoked."; return IntPtr.Zero; }
            AfterDeathInvocation?.Invoke(); return result;
        }
    }
}
