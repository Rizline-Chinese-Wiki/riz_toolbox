using System.Runtime.InteropServices;
using RizToolbox.Runtime;

internal static partial class ListenerMetadataRegression
{
    internal static (string Name, Action Run)[] TweenTargetDeathCases => new (string Name, Action Run)[] {
        ("Rooted concrete target death accepts its actual Transform class independently of TweenInfo metadata", TargetDeathActualClass),
        ("Rooted concrete target death rejects frame cached alive class and official field changes without releasing its root", TargetDeathChanges),
        ("Rooted concrete target death rejects a foreign non Unity object disposed handle and worker before invocation", TargetDeathAdmission)
    };
    private static void TargetDeathActualClass()
    {
        using var f = new Fixture(tween: true); var transform = f.InitializeTargetDeathClass();
        using var root = f.AdmissionApi.Root(f.DeathReceiver);
        var sample = f.AdmissionApi.ObserveRootedTweenTargetDeath(root, f.Module);
        Check(sample.Object == f.DeathReceiver.ToInt64() && sample.Class == transform && sample.Class != f.DeathManagedClass
            && sample.CachedPointerField == f.CachedPointer.Pointer && sample.Alive && sample.Frame == 10
            && f.DeathAliveReads == 2 && f.DeathFrameReads == 2 && f.Base.ActiveRoots == 1 && f.DeathInvocationFailure is null);
        Marshal.WriteIntPtr(f.DeathReceiver, 16, IntPtr.Zero);
        sample = f.AdmissionApi.ObserveRootedTweenTargetDeath(root, f.Module);
        Check(sample is { Alive: false, CachedNative: 0 } && root.Target == f.DeathReceiver && f.Base.ActiveRoots == 1);
    }
    private static void TargetDeathChanges()
    {
        foreach (var change in new[] { "frame", "alive", "cached", "class", "field", "disagreement" })
        {
            using var f = new Fixture(tween: true); _ = f.InitializeTargetDeathClass();
            using var root = f.AdmissionApi.Root(f.DeathReceiver);
            if (change == "frame") f.AfterDeathInvocation = () => { if (f.DeathFrameReads == 1) ++f.DeathFrame; };
            if (change == "alive") f.AfterDeathInvocation = () => { if (f.DeathAliveReads == 1) f.DeathAliveOverride = false; };
            if (change == "cached") f.AfterDeathInvocation = () => { if (f.DeathAliveReads == 1) Marshal.WriteIntPtr(f.DeathReceiver, 16, IntPtr.Zero); };
            if (change == "class") f.AfterDeathInvocation = () => { if (f.DeathAliveReads == 1) Marshal.WriteIntPtr(f.DeathReceiver, f.TargetClass); };
            if (change == "field") f.AfterDeathInvocation = () => { if (f.DeathAliveReads == 1) f.CachedPointer.Offset = 24; };
            if (change == "disagreement") f.DeathAliveOverride = false;
            var rejected = false;
            try { f.AdmissionApi.ObserveRootedTweenTargetDeath(root, f.Module); }
            catch (Exception error) when (error is InvalidOperationException or UnsupportedSignatureException) { rejected = true; }
            Check(rejected && f.Base.ActiveRoots == 1 && root.Target == f.DeathReceiver && f.DeathInvocationFailure is null);
        }
    }
    private static void TargetDeathAdmission()
    {
        using var f = new Fixture(tween: true); _ = f.InitializeTargetDeathClass();
        using var root = f.AdmissionApi.Root(f.DeathReceiver);
        Task.Run(() => Throws<InvalidOperationException>(() => f.AdmissionApi.ObserveRootedTweenTargetDeath(root, f.Module))).GetAwaiter().GetResult();
        using var foreign = f.AdmissionApi.Root(f.Target);
        Throws<UnsupportedSignatureException>(() => f.AdmissionApi.ObserveRootedTweenTargetDeath(foreign, f.Module));
        var disposed = f.AdmissionApi.Root(f.DeathReceiver); disposed.Dispose();
        Throws<TargetExpiredException>(() => f.AdmissionApi.ObserveRootedTweenTargetDeath(disposed, f.Module));
        Check(f.DeathAliveReads == 0 && f.DeathFrameReads == 0 && f.DeathInvocationFailure is null);
    }
    private sealed partial class Fixture
    {
        internal IntPtr InitializeTargetDeathClass()
        {
            InitializeTweenDeathMetadata();
            var unityObject = _classes.Values.Single(row => row.Name == "UnityEngine.Object").Class;
            var transform = ClassOf(AddType("UnityEngine.Transform", 18, false, 8, unityObject));
            Marshal.WriteIntPtr(DeathReceiver, transform); return transform;
        }
    }
}
