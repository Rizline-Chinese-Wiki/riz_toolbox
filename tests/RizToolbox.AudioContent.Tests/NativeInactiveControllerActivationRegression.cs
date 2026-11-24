using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Sdk;
using static Assert;

/// <summary>Controlled authority/native-operation seams. These exercise the real
/// activation transaction; they do not prove Unity activation, Awake or game acceptance.</summary>
internal static class NativeInactiveControllerActivationRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Activation accounts before disable, detaches an inactive clone then activates once", OrderedActivation),
        ("Activation requires zero Awake and an exact bound inactive graph before mutation", Preflight),
        ("Activation retains each unconfirmed mutation without retry or a completed receipt", MutationFailures),
        ("Activation rejects an early terminal claim between native mutations", PendingChanges),
        ("Activation requires a moved loaded scene and one complete exact Awake", PostActivationEvidence),
        ("Activation requires actual complete globals and rejects subsequent successor changes", GlobalsEvidence),
        ("Activation rejects worker entry and reentry before touching native state", ThreadAndReentry),
        ("Attempted activation cannot invoke the inactive owner's destruction path", InactiveCleanupFence),
        ("Installed globals snapshots preserve exactly retained verified delegate identities", InstalledSnapshot),
        ("Completed exact scene handoff permits only the two origin staging objects to expire", StagingExpiry)
    };

    private static void OrderedActivation()
    {
        using var f = new Fixture(); var receipt = f.Activation.ActivateOnce();
        Equal("account,disable,detach,activate,capture-globals", string.Join(',', f.Steps));
        Equal(f.Authority.Identity, receipt.Session); Equal(101L, receipt.Controller); Equal(201L, receipt.Parameters);
        Check(receipt.Graph.ControllerActiveSelf && receipt.Graph.ControllerActiveInHierarchy);
        Check(!receipt.Graph.ParentActiveSelf && !receipt.Graph.ParentActiveInHierarchy);
        Equal(0L, receipt.Graph.ActualParentTransform); Equal(1L, receipt.Awake.NativeCalls);
        Check(f.Activation.ActivationAttempted && f.Activation.ActivationCompleted && !f.Activation.CleanupUnknown);
        Throws<InvalidOperationException>(() => f.Activation.ActivateOnce()); Equal(5, f.Steps.Count);
        // Initialization may commit later. An observation receipt never calls the
        // pending-only setter or consumes another activation permission.
        f.Authority.Pending = false; var later = f.Activation.Read();
        Equal(receipt.Controller, later.Controller); Equal(1, f.Authority.ParameterReads); Equal(5, f.Steps.Count);
    }
    private static void Preflight()
    {
        foreach (var change in new Action<Fixture>[] {
            f => f.Backend.Graph = f.Backend.Graph with { Parameters = 999 },
            f => f.Backend.Graph = f.Backend.Graph with { LevelInformation = 998 },
            f => f.Backend.Graph = f.Backend.Graph with { ActualParentTransform = 0 },
            f => f.Backend.Graph = f.Backend.Graph with { ControllerActiveInHierarchy = true },
            f => f.Backend.Graph = f.Backend.Graph with { ParentSceneValidLoaded = false },
            f => f.Authority.Awake = f.Authority.Awake with { SuppressedWrongThread = 1 },
            f => f.Authority.Awake = f.Authority.Awake with { CloneBefore = 1 },
            f => f.Authority.CurrentGlobals = f.Authority.CurrentGlobals with { OwnerIdentity = 999 },
            f => f.Authority.Pending = false
        })
        {
            using var f = new Fixture(); change(f);
            Throws<InvalidOperationException>(() => f.Activation.ActivateOnce());
            Equal(0, f.Steps.Count); Check(!f.Activation.ActivationAttempted && !f.Activation.ActivationCompleted);
        }
    }
    private static void MutationFailures()
    {
        foreach (var stage in new[] { "disable", "detach", "activate" })
        {
            using var f = new Fixture(); f.Backend.FailAfter = stage;
            Throws<IOException>(() => f.Activation.ActivateOnce()); var count = f.Steps.Count;
            Check(f.Activation.ActivationAttempted && f.Activation.CleanupUnknown && !f.Activation.ActivationCompleted);
            Equal(1, f.Authority.UnknownCalls); Check(f.Activation.Failure!.Contains(nameof(IOException)));
            Throws<InvalidOperationException>(() => f.Activation.ActivateOnce());
            Throws<InvalidOperationException>(() => f.Activation.Read()); Equal(count, f.Steps.Count);
            Check(!f.Steps.Contains("capture-globals"));
        }
    }
    private static void PendingChanges()
    {
        using var f = new Fixture(); f.Backend.AfterDisable = () => f.Authority.Pending = false;
        Throws<InvalidOperationException>(() => f.Activation.ActivateOnce());
        Equal("account,disable", string.Join(',', f.Steps)); Check(f.Activation.CleanupUnknown);
        Equal(1, f.Authority.UnknownCalls); Check(!f.Activation.ActivationCompleted);
    }
    private static void PostActivationEvidence()
    {
        foreach (var change in new Action<Fixture>[] {
            f => f.Backend.Graph = f.Backend.Graph with { ControllerScene = 10 },
            f => f.Backend.Graph = f.Backend.Graph with { ControllerSceneValidLoaded = false },
            f => f.Backend.Graph = f.Backend.Graph with { ParentScene = 99 },
            f => f.Authority.Awake = f.Authority.Awake with { NativeCalls = 2 },
            f => f.Authority.Awake = f.Authority.Awake with { CloneIdentity = 999 },
            f => f.Authority.Awake = f.Authority.Awake with { InFlight = 1 },
            f => f.Authority.Awake = f.Authority.Awake with { CloneRows = new[] { new ControllerAwakeLedgerRow(101, 1, 7, 2, 8, true, false) } }
        })
        {
            using var f = new Fixture(); f.Backend.AfterActivate = () => { f.Authority.Install(); change(f); };
            Throws<InvalidOperationException>(() => f.Activation.ActivateOnce());
            Check(f.Activation.CleanupUnknown); Equal(1, f.Authority.UnknownCalls);
            Equal("account,disable,detach,activate", string.Join(',', f.Steps));
        }
    }
    private static void GlobalsEvidence()
    {
        using (var f = new Fixture())
        {
            f.Authority.CaptureComplete = false;
            Throws<InvalidOperationException>(() => f.Activation.ActivateOnce()); Check(f.Activation.CleanupUnknown);
        }
        using (var f = new Fixture())
        {
            f.Backend.AfterActivate = () => { f.Authority.Install(); f.Authority.CurrentGlobals = f.Authority.CurrentGlobals with {
                DelegateIdentities = new long[] { 2000, 2001, 9000, 2003, 2004, 2005 } }; };
            Throws<InvalidOperationException>(() => f.Activation.ActivateOnce()); Check(f.Activation.CleanupUnknown);
            Equal(1, f.Authority.UnknownCalls);
        }
        using (var f = new Fixture())
        {
            f.Activation.ActivateOnce(); f.Authority.CurrentGlobals = f.Authority.CurrentGlobals with {
                DelegateIdentities = new long[] { 2000, 2001, 9000, 2003, 2004, 2005 } };
            Throws<InvalidOperationException>(() => f.Activation.Read()); Check(f.Activation.CleanupUnknown);
            Equal(1, f.Authority.UnknownCalls); Equal(5, f.Steps.Count);
        }
        using (var f = new Fixture())
        {
            f.Backend.AfterActivate = () => { f.Authority.Install(); f.Authority.CurrentGlobals = f.Authority.CurrentGlobals with { ControllerSet = false }; };
            Throws<InvalidOperationException>(() => f.Activation.ActivateOnce()); Check(f.Activation.CleanupUnknown);
        }
    }
    private static void ThreadAndReentry()
    {
        using var f = new Fixture();
        Task.Run(() => Throws<InvalidOperationException>(() => f.Activation.ActivateOnce())).GetAwaiter().GetResult();
        Equal(0, f.Backend.BuildChecks); Equal(0, f.Steps.Count);
        var rejected = 0; f.Backend.AfterDisable = () => {
            Throws<InvalidOperationException>(() => f.Activation.ActivateOnce());
            Throws<InvalidOperationException>(() => f.Activation.Read()); rejected++;
        };
        f.Activation.ActivateOnce(); Equal(1, rejected); Equal(0, f.Authority.UnknownCalls); Check(f.Activation.ActivationCompleted);
    }
    private static void InactiveCleanupFence()
    {
        using var f = new Fixture(); var roots = new InactiveControllerRootRecords(f.Main); var destroys = 0;
        var owner = new OwnedNativeInactiveController(f.Main, roots,
            () => throw new Exception("Inactive reads must not execute after activation."), () => destroys++, () => true,
            () => throw new Exception("No extra root may be exported after activation."));
        owner.MarkReady();
        typeof(OwnedNativeInactiveController).GetField("_activation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(owner, f.Activation);
        f.Activation.ActivateOnce();
        Throws<ObjectDisposedException>(() => owner.Read()); Throws<ObjectDisposedException>(() => owner.RetainController());
        Throws<InvalidOperationException>(() => owner.RequestDestroy()); Check(!owner.TryCompleteDestroy()); Equal(0, destroys);
        // Controlled seam owns no Unity objects or roots. Remove only that test
        // association to release its empty process slot; no production retirement is asserted.
        typeof(OwnedNativeInactiveController).GetField("_activation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(owner, null);
        owner.RequestDestroy(); Check(owner.TryCompleteDestroy()); Equal(1, destroys);
    }
    private static void InstalledSnapshot()
    {
        using var main = new MainThreadDispatcher(); var backend = new GlobalsBackend();
        var owner = OwnedNativeControllerGlobals.CaptureBackend(main, backend);
        Throws<InvalidOperationException>(() => owner.InstalledSnapshot());
        backend.Values = Enumerable.Range(100, 6).Select(i => (long)i).ToArray(); backend.Flag = true;
        using var root = new RizToolbox.Runtime.NativeRuntime.RootHandle(1, _ => { }, _ => new(77), main.VerifyAccess);
        owner.CaptureInstalledFor(root); var snapshot = owner.InstalledSnapshot();
        Equal(backend.OwnerIdentity, snapshot.OwnerIdentity); Check(snapshot.ControllerSet && snapshot.IsCurrentOwner);
        Check(snapshot.DelegateIdentities.SequenceEqual(Enumerable.Range(100, 6).Select(i => (long)i)));
        backend.Values[2] = 999; Equal(102L, snapshot.DelegateIdentities[2]);
        Equal(102L, owner.InstalledSnapshot().DelegateIdentities[2]); Equal(999L, owner.ObserveCurrent().DelegateIdentities[2]);
        Task.Run(() => Throws<InvalidOperationException>(() => owner.InstalledSnapshot())).GetAwaiter().GetResult();
        backend.Values[2] = 102; owner.RestoreIfOwned(); Throws<InvalidOperationException>(() => owner.InstalledSnapshot());
        var ended = owner.VerifyConsumersEnded(new GlobalsEnded())!; owner.ReleaseAfterConsumersEnded(ended); Check(owner.IsClosed);
    }
    private static void StagingExpiry()
    {
        NativeLocalRunSceneTransitionReceipt Completed(Fixture f) => new(f.Authority.Identity, 101, 501, 601,
            new(10, "LevelList", true, true), new(40, "Level", true, true));
        void Expire(Fixture f) => f.Backend.Graph = f.Backend.Graph with {
            FiveObjectsAlive = false, StagingObjectsAlive = false, ParentScene = 0, ParentSceneValidLoaded = false };
        using (var f = new Fixture())
        {
            f.Activation.ActivateOnce(); Expire(f);
            var receipt = f.Activation.ReadAfterControlledSceneTransition(() => Completed(f));
            Check(receipt.Graph.ControllerObjectsAlive && !receipt.Graph.StagingObjectsAlive);
            Check(!receipt.Graph.FiveObjectsAlive); Check(f.Activation.ActivationCompleted); Equal(5, f.Steps.Count);
            Equal(20, receipt.Graph.ControllerScene); Equal(0, receipt.Graph.ParentScene);
        }
        using (var f = new Fixture())
        {
            f.Activation.ActivateOnce(); Expire(f); Throws<InvalidOperationException>(() => f.Activation.Read());
            Check(f.Activation.CleanupUnknown); // Initial five-object proof remains strict.
        }
        using (var f = new Fixture())
        {
            f.Activation.ActivateOnce(); Expire(f); f.Backend.Graph = f.Backend.Graph with { ControllerObjectsAlive = false };
            Throws<InvalidOperationException>(() => f.Activation.ReadAfterControlledSceneTransition(() => Completed(f)));
            Check(f.Activation.CleanupUnknown);
        }
        using (var f = new Fixture())
        {
            f.Activation.ActivateOnce(); Expire(f);
            Throws<InvalidOperationException>(() => f.Activation.ReadAfterControlledSceneTransition(
                () => Completed(f) with { Origin = new(11, "LevelList", true, true) }));
            Check(f.Activation.CleanupUnknown);
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly List<string> Steps = new();
        internal readonly Backend Backend;
        internal readonly Authority Authority;
        internal readonly NativeInactiveControllerActivation Activation;
        internal Fixture()
        {
            Backend = new(Steps); Authority = new(Steps); Backend.AfterActivate = Authority.Install;
            Activation = new(Main, Backend, Authority);
        }
        public void Dispose() => Main.Dispose();
    }
    private sealed class Backend : IInactiveControllerActivationBackend
    {
        private readonly List<string> _steps;
        internal int BuildChecks; internal string? FailAfter;
        internal Action? AfterDisable, AfterActivate;
        internal NativeInactiveControllerActivationGraph Graph = new(101, 102, 103, 104, 105, true,
            false, false, true, false, 105, 201, 0, 10, 10, true, true);
        internal Backend(List<string> steps) => _steps = steps;
        public bool CleanupUnknown => false;
        public int RetainedCallArgumentRecords => 0;
        public void RequireSupportedBuild() => BuildChecks++;
        public NativeInactiveControllerActivationGraph ReadGraph() => Graph;
        public ControllerAwakeLedgerSnapshot ReadAwake(NativeControllerAwakeLedger.CaptureWindow window) => throw new Exception("Controlled authority supplies evidence.");
        public void CaptureInstalledGlobals(OwnedNativeControllerGlobals globals) => throw new Exception("Controlled authority supplies evidence.");
        public void DisableClone() { Step("disable"); Graph = Graph with { ControllerActiveSelf = false }; AfterDisable?.Invoke(); Fail("disable"); }
        public void DetachClone() { Step("detach"); Graph = Graph with { ActualParentTransform = 0 }; Fail("detach"); }
        public void ActivateClone()
        { Step("activate"); Graph = Graph with { ControllerActiveSelf = true, ControllerActiveInHierarchy = true, ControllerScene = 20 }; AfterActivate?.Invoke(); Fail("activate"); }
        private void Step(string step) => _steps.Add(step);
        private void Fail(string stage) { if (FailAfter == stage) throw new IOException("Controlled unknown native outcome after " + stage); }
    }
    private sealed class Authority : IInactiveControllerActivationAuthority
    {
        private readonly List<string> _steps;
        internal bool Pending = true, CaptureComplete = true;
        internal int UnknownCalls, ParameterReads;
        internal ControllerAwakeLedgerSnapshot Awake = new("pre-instantiate", 101, true, true, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            Array.Empty<ControllerAwakeLedgerRow>(), Array.Empty<string>());
        internal ControllerGlobalsSnapshot CurrentGlobals = new(500, true, false, Enumerable.Range(1000, 6).Select(i => (long)i).ToArray());
        internal Authority(List<string> steps) => _steps = steps;
        public LocalRunSessionIdentity Identity { get; } = new("controlled-activation", new("activation.fixture", "song"), 1, "chart", 1);
        public long Controller => 101;
        public long Information => 0;
        public bool CleanupUnknown => false;
        public bool InstalledGlobalsCaptured => CaptureComplete;
        public void VerifyPending() { if (!Pending) throw new InvalidOperationException("Controlled pending identity was withdrawn or reached terminal state."); }
        public NativeInactiveControllerParameterBindingObservation ReadParameters()
        {
            ParameterReads++; return new(101, 201, new(301, 302, 303, 304, 305, 0, 0),
                new(101, 102, 103, false, true, false, true, 201, 0));
        }
        public void NoteActivationAttempt() => _steps.Add("account");
        public ControllerAwakeLedgerSnapshot ReadAwake() => Awake;
        public ControllerGlobalsSnapshot BaselineGlobals() => new(500, true, false, Enumerable.Range(1000, 6).Select(i => (long)i).ToArray());
        public ControllerGlobalsSnapshot InstalledGlobals() => new(500, true, true, Enumerable.Range(2000, 6).Select(i => (long)i).ToArray());
        public ControllerGlobalsSnapshot ObserveGlobals() => CurrentGlobals;
        public void CaptureInstalledGlobals() => _steps.Add("capture-globals");
        public void MarkUnknown(string failure) { UnknownCalls++; Pending = false; }
        internal void Install()
        {
            Awake = new("pre-instantiate", 101, true, true, 1, 1, 0, 0, 1, 1, 1, 1, 0,
                new[] { new ControllerAwakeLedgerRow(101, 1, 7, 2, 8, true, true) }, Array.Empty<string>());
            CurrentGlobals = new(500, true, true, Enumerable.Range(2000, 6).Select(i => (long)i).ToArray());
        }
    }
    private sealed class GlobalsBackend : IControllerGlobalsBackend
    {
        internal long[] Values = new long[6]; internal bool Flag;
        public long OwnerIdentity => 71_901_300;
        public bool IsCurrentOwner => true;
        public IControllerGlobalsReference RetainController(IntPtr controller) => new GlobalReference(controller.ToInt64());
        public IControllerGlobalsReference? CaptureDelegate(int index) => Values[index] == 0 ? null : new GlobalReference(Values[index]);
        public void VerifyBaseline(int index, IControllerGlobalsReference? value) { }
        public bool TargetsController(int index, IControllerGlobalsReference value, long controller)
            => controller == 77 && value.Identity == 100 + index;
        public bool ReadControllerSet() => Flag;
        public bool WriteDelegateIfSame(int index, long expected, IControllerGlobalsReference? baseline)
        { if (Values[index] != expected) return false; Values[index] = baseline?.Identity ?? 0; return true; }
        public bool WriteControllerSetIfSame(bool expected, bool baseline, IReadOnlyList<long> delegates)
        { if (Flag != expected || !Values.SequenceEqual(delegates)) return false; Flag = baseline; return true; }
        public void Dispose() { }
    }
    private sealed record GlobalReference(long Identity) : IControllerGlobalsReference { public void Dispose() { } }
    private sealed class GlobalsEnded : IControllerGlobalsConsumersEndedVerifier
    {
        public bool Verify(ControllerGlobalsRetirementRequest request, out string evidence)
        { evidence = "Controlled globals fixture owns no Unity consumers; test-only exact owner has ended."; return true; }
    }
}
