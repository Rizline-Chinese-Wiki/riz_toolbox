using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using System.Reflection;
using static Assert;

/// <summary>Real session registry with a controlled actual-result backend. No game
/// ControllerOver, save, Steam operation or native consumer-end proof is performed.</summary>
internal static class NativeLocalResultCaptureRegression
{
    internal static readonly (string Name, Action Test)[] Cases = new (string Name, Action Test)[] {
        ("Native actual-result capture rejects unsupported build before resolving caller roots", NativeAdmission),
        ("Actual result capture requires an uncaptured committed exact receiver and rejects duplicate owners", Admission),
        ("Actual result capture consumes its claim before one invocation publishes and caches the actual root", Success),
        ("Actual result capture cannot fabricate an observed start or replace counters with best-score data", CopiedValues),
        ("LOCAL result reports the terminal reason from its own captured flags", CapturedEndReason),
        ("Actual result capture worker and nested entry cannot duplicate capture or release current roots", ThreadAndReentry),
        ("Actual result source mismatch before invocation preserves the once-only claim for a valid source", SourceChanged),
        ("Actual result roots remain held until exact registry retirement and then free independently", Retirement),
        ("Actual result preparation respects combined 32-session ownership bounds and returns only confirmed slots", Capacity),
        // Unknown calls deliberately remain strongly owned. Never reset or force-free.
        ("Actual native result-source preclaim field cleanup failure becomes sticky bounded responsibility", NativePreclaimReadFailure),
        ("Actual ControllerOver failure after publication retains the result root and never retries or falls back", InvocationFailure),
        ("Actual result metadata and nonfinite decoding failure preserve protection and unadopted output", InvalidResult),
        ("Actual result independent root free failure retains its failed record and never retries", FreeFailure),
        ("Actual result capture wraps ControllerOver with its tracked named Cutout publication window", TerminalCapture),
        ("Actual result capture retains unconfirmed named Cutout or original failure without retry", TerminalCaptureFailure)
    }.Concat(NativeLocalResultCapturePendingRegression.Cases).ToArray();
    private static void NativeAdmission()
    {
        using var main = new MainThreadDispatcher(); using var native = new FakeNative(); var reads = 0;
        using var controller = new NativeRuntime.RootHandle(1, _ => { }, _ => { reads++; return new(123); }, main.VerifyAccess);
        var profile = new AdapterBuildProfile("test", "", "test", "test", "test", "test", new Dictionary<string, TypeBinding>());
        Throws<UnsupportedSignatureException>(() => NativeLocalResultCapture.Create(native.Api, main, profile,
            IntPtr.Zero, new(main), null!, controller));
        Equal(0, reads); Equal(0, native.Invocations);
    }
    private static void Admission()
    {
        using var f = new Fixture(commit: false);
        Throws<InvalidOperationException>(() => f.Create()); Equal(0, f.Backend.SourceReads); Equal(0, f.Backend.Roots);
        f.Registry.Commit(f.Commit); var owner = f.Create();
        Throws<InvalidOperationException>(() => NativeLocalResultCapture.CreateBackend(f.Main, f.Backend, f.Registry, f.Session));
        Equal(2, f.Backend.Roots); Equal(0, f.Backend.Invocations); Equal(2, owner.RootRecords);
    }
    private static void Success()
    {
        using var f = new Fixture(); var owner = f.Create();
        f.Backend.BeforeInvoke = () => {
            Check(f.Registry.Read(f.Session).ResultCallClaimed);
            Equal(LocalRunResultClaimKind.InProgress, f.Registry.TryClaimResult(f.Session).Kind);
        };
        f.Backend.AfterPublish = () => { Equal(3, owner.RootRecords); Equal(0, f.Backend.Frees.Count); };
        var result = owner.Capture()!; Equal(1, f.Backend.Invocations); Equal(2, owner.RootRecords);
        Equal(f.Backend.ResultIdentity, result.NativeResultIdentity); Equal(LocalRunSessionPhase.ResultReady, f.Registry.Read(f.Session).Phase);
        Check(ReferenceEquals(result, owner.Capture())); Equal(1, f.Backend.Invocations);
        f.Registry.MarkProviderWithdrawn(f.Session); Check(ReferenceEquals(result, owner.Capture()));
        Check(f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, f.Backend.Source.Controller));
    }
    private static void TerminalCapture()
    {
        using var f = new Fixture(); using var terminal = new NativeLocalTerminalCoroutineRegression.Fixture(f.Backend.Source.Controller);
        var owner = f.Create(); owner.AttachTerminalCoroutines(terminal.Owner);
        Throws<InvalidOperationException>(() => owner.AttachTerminalCoroutines(terminal.Owner));
        f.Backend.BeforeInvoke = () => {
            Equal(LocalRunSessionPhase.ResultCapturing, f.Registry.Read(f.Session).Phase);
            terminal.Backend.Named(TerminalCoroutineRole.Cutout); Check(!terminal.Owner.TryConfirmObservedEnded());
        };
        var result = owner.Capture(); Check(result is not null && terminal.Owner.Read().ResultReturned && terminal.Owner.Read().CutoutNamedReturned);
        Check(!terminal.Owner.TryConfirmObservedEnded()); terminal.Backend.Move(TerminalCoroutineRole.Cutout, terminal.Backend.LastEnum, -1, 0);
        Check(terminal.Owner.TryConfirmObservedEnded()); Check(ReferenceEquals(result, owner.Capture())); Equal(1, f.Backend.Invocations);
        Check(!terminal.Owner.IsFor(f.Registry, f.Session)); // This composition uses a controlled authority, never a production release receipt.
    }
    private static void TerminalCaptureFailure()
    {
        foreach (var missing in new[] { true, false })
        {
            using var f = new Fixture(); using var terminal = new NativeLocalTerminalCoroutineRegression.Fixture(f.Backend.Source.Controller);
            var owner = f.Create(); owner.AttachTerminalCoroutines(terminal.Owner); f.PreserveUnknown = true;
            if (!missing) { f.Backend.BeforeInvoke = () => terminal.Backend.Named(TerminalCoroutineRole.Cutout); f.Backend.ThrowAfterPublish = true; }
            Throws<NativeLocalResultCaptureException>(() => owner.Capture());
            Check(terminal.Owner.Read().Unknown && !terminal.Owner.Read().ResultReturned && owner.CleanupUnknown);
            Equal(1, f.Backend.Invocations); Equal(3, owner.RootRecords);
            Throws<InvalidOperationException>(() => owner.Capture()); Equal(1, f.Backend.Invocations);
        }
    }
    private static void CopiedValues()
    {
        using var f = new Fixture(); f.Backend.Values = new(f.Backend.Source.Metadata, 123456, .123456f, 1, 2, 3, 4, true, false);
        var result = f.Create().Capture()!;
        Equal(123456L, result.Copy.Score); Equal((double).123456f, result.Copy.RawCompletion);
        Equal(1, result.Copy.Perfect); Equal(2, result.Copy.MaxPerfect); Equal(3, result.Copy.Bad); Equal(4, result.Copy.Miss);
        Check(result.Copy.Crashed && !result.Copy.Skipped); Check(result.Copy.RunId is null && result.Copy.ChartId is null);
        Check(!result.Copy.AssociatedWithObservedStart); Equal(f.Registry.Read(f.Session).Identity, result.Session);
    }
    private static void CapturedEndReason()
    {
        foreach (var (crashed, skipped, expected) in new[] {
            (false, false, RunEndReason.Completed), (false, true, RunEndReason.Skipped),
            (true, false, RunEndReason.Failed), (true, true, RunEndReason.Failed) })
        {
            using var fixture = new Fixture();
            fixture.Backend.Values = fixture.Backend.Values with { Crashed = crashed, Skipped = skipped };
            var copy = fixture.Create().Capture()!.Copy;
            Equal(expected, copy.EndReason);
            Check(copy.Crashed == crashed && copy.Skipped == skipped);
            Check(copy.RunId is null && !copy.AssociatedWithObservedStart);
        }
    }
    private static void ThreadAndReentry()
    {
        using var f = new Fixture(); var owner = f.Create();
        Task.Run(() => {
            Throws<InvalidOperationException>(() => owner.Capture()); Throws<InvalidOperationException>(() => owner.Dispose());
        }).GetAwaiter().GetResult();
        Equal(0, f.Backend.Invocations); Equal(0, f.Backend.Frees.Count);
        f.Backend.BeforeInvoke = () => {
            Throws<InvalidOperationException>(() => owner.Capture()); Throws<InvalidOperationException>(() => owner.Dispose());
        };
        owner.Capture(); Equal(1, f.Backend.Invocations); Check(!owner.CleanupUnknown);
    }
    private static void SourceChanged()
    {
        using var f = new Fixture(); var owner = f.Create(); var source = f.Backend.Source;
        f.Backend.Source = source with { Parameters = source.Parameters + 1 };
        Throws<NativeLocalResultCaptureException>(() => owner.Capture());
        Equal(0, f.Backend.Invocations); Check(!f.Registry.Read(f.Session).ResultCallClaimed && !owner.CleanupUnknown);
        f.Backend.Source = source; Check(owner.Capture() is not null); Equal(1, f.Backend.Invocations);
    }
    private static void Retirement()
    {
        using var f = new Fixture(); var owner = f.Create(); owner.Capture();
        Throws<InvalidOperationException>(() => owner.Dispose()); Equal(0, f.Backend.Frees.Count);
        f.Retire(); Equal("3", string.Join(",", f.Backend.Frees)); Check(owner.Capture() is null); owner.Dispose(); owner.Dispose();
        Equal("3,2,1", string.Join(",", f.Backend.Frees)); Check(owner.IsClosed); Equal(0, owner.RootRecords);
    }
    private static void Capacity()
    {
        var fixtures = new List<Fixture>();
        var available = Math.Min(32 - NativeLocalResultCapture.RetainedProcessCaptureCount,
            LocalRunSessionRegistry.MaximumProcessSessions - LocalRunSessionRegistry.RetainedProcessSessionCount);
        try
        {
            for (var i = 0; i < available; i++) { var f = new Fixture(); fixtures.Add(f); f.Create(); }
            Equal(LocalRunSessionRegistry.MaximumProcessSessions, LocalRunSessionRegistry.RetainedProcessSessionCount);
            Throws<InvalidOperationException>(() => new Fixture());
            Check(NativeLocalResultCapture.RetainedProcessCaptureCount <= 32);
            fixtures[0].Dispose(); fixtures.RemoveAt(0);
            using var refused = new Fixture();
            refused.Create(); Equal(2, refused.Backend.Roots);
        }
        finally { foreach (var f in fixtures) f.Dispose(); }
    }
    private static void InvocationFailure()
    {
        using var f = new Fixture(); var owner = f.Create(); f.Backend.ThrowAfterPublish = true;
        var error = ThrowsCapture(() => owner.Capture()); Check(ReferenceEquals(owner, error.Responsibility));
        Equal(3, owner.RootRecords); Check(owner.CleanupUnknown && owner.InvocationAttempted);
        Equal(LocalRunSessionPhase.CleanupUnknown, f.Registry.Read(f.Session).Phase);
        Equal(LocalRunResultClaimKind.Failed, f.Registry.TryClaimResult(f.Session).Kind);
        Throws<InvalidOperationException>(() => owner.Capture()); Equal(1, f.Backend.Invocations);
        f.Retire(); Throws<InvalidOperationException>(() => owner.Dispose()); Equal(0, f.Backend.Frees.Count); f.PreserveUnknown = true;
    }
    private static void NativePreclaimReadFailure()
    {
        // Skip only the pinned-image discovery for controlled exports, not the native
        // ReadSource implementation. This is explicitly not game build/metadata acceptance.
        using var main = new MainThreadDispatcher(); using var native = new FakeNative();
        using var borrowed = native.Api.Root(native.Instance); native.FieldType = native.Object;
        native.FieldBytes = BitConverter.GetBytes(native.OtherObject.ToInt64()); native.FailFreeHandle = 2;
        var profile = new AdapterBuildProfile("fixture", WorkshopChartModule.Fingerprint, "fixture", "2022.3.62f2", "fixture", "fixture", new Dictionary<string, TypeBinding>());
        var backend = new NativeLocalResultCaptureBackend(native.Api, main, profile, new IntPtr(1), borrowed);
        typeof(NativeLocalResultCaptureBackend).GetField("_verified", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(backend, true);
        typeof(NativeLocalResultCaptureBackend).GetField("_normal", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(backend, native.Api.ObjectClass(native.Instance));
        var registry = new LocalRunSessionRegistry(main);
        var session = registry.RegisterPrepared(new(new("result.test", "native-read-failure"), 1, "chart"), 29_999_999, new Lease());
        var commit = registry.BeginCommit(session); registry.BindPendingController(commit, native.Instance.ToInt64(), new Lease()); registry.Commit(commit);
        var baseline = NativePublicationCall.RetainedCallCount;
        var failure = ThrowsCapture(() => NativeLocalResultCapture.CreateBackend(main, backend, registry, session));
        Check(failure.Responsibility.CleanupUnknown && backend.CleanupUnknown); Equal(1, native.FieldReads);
        Equal(baseline + 1, NativePublicationCall.RetainedCallCount); Equal(1, native.FreeHandleAttempts);
        Throws<InvalidOperationException>(() => failure.Responsibility.Capture());
        Throws<InvalidOperationException>(() => backend.ReadSource()); Equal(1, native.FieldReads); Equal(0, native.Invocations);
        registry.BeginReturn(session); var proof = registry.VerifyConsumersEnded(session, new Verifier())!; Check(registry.Close(session, proof));
        Throws<InvalidOperationException>(() => failure.Responsibility.Dispose()); Equal(1, native.FreeHandleAttempts);
    }
    private static void InvalidResult()
    {
        foreach (var mismatch in new[] { true, false })
        {
            using var f = new Fixture(); var owner = f.Create(); var old = f.Backend.Values;
            f.Backend.Values = mismatch ? old with { Metadata = old.Metadata + 1 } : old with { RawCompletion = double.NaN };
            Throws<NativeLocalResultCaptureException>(() => owner.Capture());
            Equal(1, f.Backend.Invocations); Equal(3, owner.RootRecords); Check(owner.CleanupUnknown);
            Equal(LocalRunResultClaimKind.Failed, f.Registry.TryClaimResult(f.Session).Kind);
            Check(f.Registry.IsProtected(LocalBoundaryKind.ControllerRetry, f.Backend.Source.Controller)); f.PreserveUnknown = true;
        }
    }
    private static void FreeFailure()
    {
        using var f = new Fixture(); var owner = f.Create(); owner.Capture(); f.Retire(); f.Backend.FailFree = 2;
        Throws<AggregateException>(() => owner.Dispose()); Check(owner.CleanupUnknown && !owner.IsClosed);
        Equal("3,2,1", string.Join(",", f.Backend.Frees)); Equal(1, owner.RootRecords);
        Throws<InvalidOperationException>(() => owner.Dispose()); Equal("3,2,1", string.Join(",", f.Backend.Frees)); f.PreserveUnknown = true;
    }
    private static NativeLocalResultCaptureException ThrowsCapture(Action action)
    { try { action(); } catch (NativeLocalResultCaptureException error) { return error; } throw new Exception("Expected capture failure."); }
    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly LocalRunSessionRegistry Registry;
        internal readonly LocalRunSessionRegistry.SessionHandle Session;
        internal readonly LocalRunSessionRegistry.CommitClaim Commit;
        internal readonly Backend Backend;
        internal NativeLocalResultCapture? Owner;
        internal bool PreserveUnknown;
        internal Fixture(bool commit = true)
        {
            Registry = new(Main); Backend = new(Main);
            Session = Registry.RegisterPrepared(new(new("result.test", "song"), 1, "chart"), Backend.Source.Metadata, new Lease());
            Commit = Registry.BeginCommit(Session); Registry.BindPendingController(Commit, Backend.Source.Controller, new Lease());
            if (commit) Registry.Commit(Commit);
        }
        internal NativeLocalResultCapture Create() => Owner = NativeLocalResultCapture.CreateBackend(Main, Backend, Registry, Session);
        internal void Retire()
        {
            if (Registry.Read(Session).Phase == LocalRunSessionPhase.Closed) return;
            Registry.BeginReturn(Session); var proof = Registry.VerifyConsumersEnded(Session, new Verifier())!;
            Check(Registry.Close(Session, proof));
        }
        public void Dispose()
        { try { Retire(); if (!PreserveUnknown) Owner?.Dispose(); } finally { Main.Dispose(); } }
    }
    private sealed class Lease : IDisposable { public void Dispose() { } }
    private sealed class Verifier : ILocalRunConsumersEndedVerifier
    { public bool Verify(LocalRunRetirementRequest request, out string evidence) { evidence = "Controlled fixture only; not actual native consumer evidence."; return true; } }
    internal sealed class Backend : INativeLocalResultCaptureBackend
    {
        private static long NextIdentity = 20_000_000;
        private readonly MainThreadDispatcher _main;
        internal NativeLocalResultSource Source;
        internal NativeLocalResultValues Values;
        internal readonly long ResultIdentity;
        internal readonly List<uint> Frees = new();
        internal int Roots, Invocations, SourceReads;
        internal uint FailFree;
        internal bool ThrowAfterPublish;
        public bool CleanupUnknown { get; private set; }
        internal Action? BeforeInvoke, AfterPublish;
        internal Backend(MainThreadDispatcher main)
        {
            _main = main; var id = Interlocked.Add(ref NextIdentity, 10);
            Source = new(id + 1, id + 2, id + 3, id + 4); ResultIdentity = id + 5;
            Values = new(Source.Metadata, 12345, .5, 1, 2, 3, 4, false, false);
        }
        public void RequireSupportedBuild() => _main.VerifyAccess();
        public NativeLocalResultSource ReadSource() { _main.VerifyAccess(); SourceReads++; return Source; }
        private NativeRuntime.RootHandle Root(uint id, long target)
        {
            Roots++; return new(id, value => { Frees.Add(value); if (value == FailFree) throw new IOException("Unknown result root free."); }, _ => new(target), _main.VerifyAccess);
        }
        public NativeRuntime.RootHandle RetainController() => Root(1, Source.Controller);
        public NativeRuntime.RootHandle RetainInformation() => Root(2, Source.Information);
        public void InvokeControllerOver(Action<NativeRuntime.RootHandle> publish)
        {
            _main.VerifyAccess(); Invocations++; BeforeInvoke?.Invoke(); publish(Root(3, ResultIdentity)); AfterPublish?.Invoke();
            if (ThrowAfterPublish) throw new IOException("Controlled temporary cleanup failure after actual output publication.");
        }
        public NativeLocalResultValues ReadResult(NativeRuntime.RootHandle result)
        { Equal(ResultIdentity, result.Target.ToInt64()); return Values; }
    }
}
