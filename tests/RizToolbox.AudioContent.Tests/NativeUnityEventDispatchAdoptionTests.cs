using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using static Assert;

/// <summary>Calls the actual production CaptureDispatch/AdoptDispatchReference
/// methods over the existing NativeRuntime export seam. Only backend startup
/// metadata/known bindings are seeded; Initialize, Ready, native callbacks and
/// IL2CPP GC are outside this controlled test. No production test flag is added.</summary>
internal static class NativeUnityEventDispatchAdoptionTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Dispatch production adoption retains its consumed slot when the first Root fails", () => RootFailure(1)),
        ("Dispatch production adoption preserves the first root when the second Root fails", () => RootFailure(2)),
        ("Dispatch production adoption preserves two roots when the third Root fails", () => RootFailure(3)),
        ("Dispatch production adopts all three references before the first Target can fail", FirstTargetFailure),
        ("Dispatch production keeps all three roots when its second Class read fails", SecondClassFailure),
        ("Dispatch production adoption deduplicates shared references before any Target read", SharedReferencePrefix),
        ("Dispatch production capture without RDX completes actual list and row index validation", CompleteWithoutArguments),
        ("Dispatch production capture rejects a wrong index after actual list observation", WrongIndex),
        ("Dispatch production root budget preserves full and partial batches without reads or retries", RootBudget),
        ("Dispatch production adopts published references before rejecting a source fault raised during Root", SourceFaultDuringRoot)
    };

    // Unknown process responsibilities and controlled exports deliberately stay
    // alive until the isolated case exits. Never clear a production owner table,
    // dispose an unknown root, or recycle a retained pointer to satisfy a quota.
    private static readonly List<Fixture> Owners = new();
    private static Fixture Own() { var fixture = new Fixture(); Owners.Add(fixture); return fixture; }
    private static void Check(bool value, string message = "Dispatch adoption assertion failed.")
    { if (!value) throw new InvalidOperationException(message); }

    private static void RootFailure(int failure)
    {
        var f = Own(); f.Native.ListenerDispatchFailRoot = failure;
        Throws<TargetExpiredException>(() => f.Backend.CaptureDispatch(0, f.Frame));
        Equal(failure, f.Native.ListenerDispatchRootCalls);
        Equal(0, f.Native.ListenerDispatchTargetReads); Equal(0, f.Native.ListenerDispatchClassReads);
        Equal(0, f.Native.FieldReads); Equal(0, f.Native.Invocations);
        f.VerifyRetained(failure, failure - 1, new[] { f.Frame.Invokable, f.Frame.List }.Take(failure - 1));
        Check(f.Native.ListenerDispatchTrace.SequenceEqual(new[] {
            Root(f.Frame.Invokable), Root(f.Frame.List), Root(f.Frame.ArgRdx) }.Take(failure)));
        f.VerifyUnknownStopsFurtherReads();
    }

    private static void FirstTargetFailure()
    {
        var f = Own(); f.Native.ListenerDispatchFailTarget = 1;
        Throws<TargetExpiredException>(() => f.Backend.CaptureDispatch(0, f.Frame));
        f.VerifyAdoptionPrefix(f.Frame.Invokable, f.Frame.List, f.Frame.ArgRdx);
        Equal(1, f.Native.ListenerDispatchTargetReads); Equal(0, f.Native.ListenerDispatchClassReads);
        Equal(0, f.Native.FieldReads); Equal(0, f.Native.Invocations);
        f.VerifyRetained(3, 3, new[] { f.Frame.Invokable, f.Frame.List, f.Frame.ArgRdx });
        f.VerifyUnknownStopsFurtherReads();
    }

    private static void SecondClassFailure()
    {
        var f = Own(); f.Native.ListenerDispatchFailClass = 2;
        Throws<UnsupportedSignatureException>(() => f.Backend.CaptureDispatch(0, f.Frame));
        f.VerifyAdoptionPrefix(f.Frame.Invokable, f.Frame.List, f.Frame.ArgRdx);
        Equal(2, f.Native.ListenerDispatchTargetReads); Equal(2, f.Native.ListenerDispatchClassReads);
        Check(f.Native.ListenerDispatchTrace.SequenceEqual(new[] {
            Root(f.Frame.Invokable), Root(f.Frame.List), Root(f.Frame.ArgRdx),
            Target(f.Frame.Invokable), Target(f.Frame.List), Class(f.Frame.List), Class(f.Frame.Invokable) }));
        Equal(0, f.Native.FieldReads); Equal(0, f.Native.Invocations);
        f.VerifyRetained(3, 3, new[] { f.Frame.Invokable, f.Frame.List, f.Frame.ArgRdx });
        f.VerifyUnknownStopsFurtherReads();
    }

    private static void SharedReferencePrefix()
    {
        foreach (var allSame in new[] { false, true })
        {
            var f = Own(); var frame = f.Frame;
            if (allSame) frame.List = frame.Invokable;
            frame.ArgRdx = frame.Invokable;
            f.Native.ListenerDispatchFailTarget = 1;
            // Stop at the first Target deliberately. These alias identities
            // exercise the actual adoption prefix, not a valid complete graph.
            Throws<TargetExpiredException>(() => f.Backend.CaptureDispatch(0, frame));
            var unique = new[] { frame.Invokable, frame.List, frame.ArgRdx }.Distinct().ToArray();
            f.VerifyAdoptionPrefix(unique); Equal(unique.Length, f.Native.ListenerDispatchRootCalls);
            Equal(1, f.Native.ListenerDispatchTargetReads); Equal(0, f.Native.ListenerDispatchClassReads);
            f.VerifyRetained(unique.Length, unique.Length, unique);
            f.VerifyUnknownStopsFurtherReads();
        }
    }

    private static void CompleteWithoutArguments()
    {
        var f = Own(); var frame = f.Frame; frame.ArgRdx = IntPtr.Zero;
        var captured = f.Backend.CaptureDispatch(0, frame);
        Check(!f.Backend.CleanupUnknown && !f.Authority.Unknown);
        Equal(frame.List.ToInt64(), captured.Executing.Object); Equal(1, captured.Executing.Version);
        Equal(1, captured.Executing.Rows.Count); Equal(frame.Invokable.ToInt64(), captured.Invokable.Object);
        Check(ReferenceEquals(captured.Invokable, captured.Executing.Rows[0]));
        Check(captured.Invokable.Opaque && captured.Invokable.Leaves.Count == 0);
        Equal(0L, captured.ArgumentArray); Equal(0, f.Backend.RetainedCallArgumentRecords);
        f.VerifyAdoptionPrefix(frame.Invokable, frame.List);
        Check(f.Native.FieldReads > 0 && f.Native.Invocations > 0,
            "A completed Capture must execute the production list observation, not stop at adoption.");
        f.VerifyRetained(2, 2, new[] { frame.Invokable, frame.List }, allowTemporaryFrees: true);
        Check(f.Native.NewRoots > 2 && f.Native.FreeHandleAttempts == f.Native.NewRoots - 2,
            "Successful temporary list-read roots must remain distinct from the two retained dispatch roots.");
    }

    private static void WrongIndex()
    {
        var f = Own(); var frame = f.Frame; frame.ArgRdx = IntPtr.Zero; frame.Index = 1;
        Throws<InvalidOperationException>(() => f.Backend.CaptureDispatch(0, frame));
        Check(f.Native.FieldReads > 0 && f.Native.Invocations > 0);
        f.VerifyRetained(2, 2, new[] { frame.Invokable, frame.List }, allowTemporaryFrees: true);
        f.VerifyUnknownStopsFurtherReads();
    }

    private static void RootBudget()
    {
        foreach (var remaining in new[] { 0, 1 })
        {
            var f = Own(); var initial = 2048 - remaining;
            var prior = Enumerable.Range(0, initial).Select(index => new IntPtr(0x800000 + index * 16)).ToArray();
            foreach (var pointer in prior) f.AdoptActualReference(pointer);
            f.VerifyRetained(initial, initial, prior);
            Equal(initial, f.Native.ListenerDispatchRootCalls);
            Equal(0, f.Native.ListenerDispatchTargetReads); Equal(0, f.Native.ListenerDispatchClassReads);
            var nativeCalls = f.Native.ListenerDispatchRootCalls;
            Throws<InvalidOperationException>(() => f.Backend.CaptureDispatch(0, f.Frame));
            Equal(nativeCalls + remaining, f.Native.ListenerDispatchRootCalls);
            Equal(0, f.Native.ListenerDispatchTargetReads); Equal(0, f.Native.ListenerDispatchClassReads);
            var retained = remaining == 0 ? prior : prior.Append(f.Frame.Invokable).ToArray();
            f.VerifyRetained(2048, 2048, retained);
            Check(!f.Slots.ContainsKey(f.Frame.List.ToInt64()) && !f.Slots.ContainsKey(f.Frame.ArgRdx.ToInt64()));
            f.VerifyUnknownStopsFurtherReads();
        }
    }

    private static void SourceFaultDuringRoot()
    {
        var f = Own(); f.Native.ListenerDispatchOnRoot = (count, _) => { if (count == 2) f.Authority.Faulted = true; };
        Throws<InvalidOperationException>(() => f.Backend.CaptureDispatch(0, f.Frame));
        // These are already-published source registers. Completing their
        // necessary root adoption is allowed; Target/Class/list reads are not.
        Equal(3, f.Native.ListenerDispatchRootCalls);
        Check(f.Native.ListenerDispatchTrace.SequenceEqual(new[] { Root(f.Frame.Invokable), Root(f.Frame.List), Root(f.Frame.ArgRdx) }));
        Equal(0, f.Native.ListenerDispatchTargetReads); Equal(0, f.Native.ListenerDispatchClassReads);
        f.VerifyRetained(3, 3, new[] { f.Frame.Invokable, f.Frame.List, f.Frame.ArgRdx });
        Check(f.Authority.Unknown); f.VerifyUnknownStopsFurtherReads();
    }

    private static string Root(IntPtr pointer) => "root:" + pointer.ToInt64();
    private static string Target(IntPtr pointer) => "target:" + pointer.ToInt64();
    private static string Class(IntPtr pointer) => "class:" + pointer.ToInt64();

    private sealed class Fixture
    {
        private static long _identity = 0x7e0000000;
        internal readonly MainThreadDispatcher Main = new();
        internal readonly FakeNative Native = new();
        internal readonly Authority Authority = new();
        internal readonly NativeLocalUnityEventBackend Backend;
        internal readonly NativeListenerFrame Frame;
        private readonly NativeLocalUnityEventLedger _owner;
        private readonly ActivityEngine _engine = new();
        internal Fixture()
        {
            var block = Interlocked.Add(ref _identity, 0x10000);
            Native.FieldType = Native.Int32; Native.FieldBytes = BitConverter.GetBytes(1);
            Native.ReturnType = Native.Object; Native.Parameters = new[] { Native.Int32 }; Native.Result = Native.OtherObject;
            var api = Native.CreateListenerDispatchAdoptionApi();
            var broker = new ExactHookBroker(_engine, Main, new Logger());
            var profile = new AdapterBuildProfile("controlled-dispatch-adoption", "", "test", "test", "test", "test", new Dictionary<string, TypeBinding>());
            var registry = new LocalRunSessionRegistry(Main); var bundle = new EmptyBundle();
            var session = registry.RegisterPrepared(new(new("dispatch.adoption", "song"), 1, "chart"), block + 0x400, bundle);
            var launch = new LocalRunLaunchCommitState(Main, registry, registry.BeginCommit(session), bundle);
            var source = NativeLocalCutoutEventSource.CaptureBackend(Main, new SourceBackend(block));
            Check(launch.Read().Phase == LocalRunLaunchCommitPhase.Held && !launch.IsCommittedReady && !source.IsFor(launch));
            // Initialize and its fixed-build/metadata/Ready front are explicitly
            // not invoked. These normally constructed dependencies grant no
            // production source/Ready authority. Only the actual method and its
            // real root arrays are exercised after explicit controlled seeding.
            Backend = new(api, Main, profile, IntPtr.Zero, broker, launch, source);
            _owner = NativeLocalUnityEventLedger.CaptureBackend(Main, new OwnerBackend(), Authority, block);
            Check(!_owner.IsFor(null!));
            var invoke = broker.BindNativeVoidObservation<EmptyContext>(
                new("dispatch.adoption.invoke", new(block + 0x10), new(0x11), new(0x12), ExactHookAbi.NativeVoid0),
                () => new ActivityBridge(), _ => null, (_, _) => { }, (_, _) => { });
            var prepare = broker.BindNativeVoidObservation<EmptyContext>(
                new("dispatch.adoption.prepare-activity", new(block + 0x20), new(0x21), new(0x22), ExactHookAbi.NativeVoid0),
                () => new ActivityBridge(), _ => null, (_, _) => { }, (_, _) => { });
            SeedControlledInitializedBackend(invoke, prepare, block + 0x100);
            Frame = new() { Version = 1, Size = 176, Slot = 19, Site = 2, Stage = NativeListenerStage.Before,
                Event = new(block + 0x100), Invokable = Native.OtherObject, ArgRcx = Native.OtherObject,
                List = Native.Instance, ArgRdx = new(block + 0x200), ActualTarget = new(block + 0x300), Index = 0 };
            Equal(0, Native.ListenerDispatchRootCalls); Equal(2, _engine.Prepared); Equal(2, _engine.Committed);
        }

        // All private initialization seeding is confined here and checks the
        // exact declared type. It creates no native receipt or private owner.
        private void SeedControlledInitializedBackend(ExactHookBroker.Binding invoke, ExactHookBroker.Binding prepare, long eventIdentity)
        {
            SeedField("_owner", _owner); SeedField("_invokeBinding", invoke); SeedField("_prepareBinding", prepare);
            SeedField("_baseListClass", Native.ClassOf(Native.Receiver)); SeedField("_baseCallClass", Native.ClassOf(Native.Object));
            // Controlled rows are opaque, avoiding unrelated delegate metadata.
            SeedField("_invokableClass", new IntPtr(0x666666)); SeedField("_baseGetItem", Native.Method);
            Field<long[]>("_events")[0] = eventIdentity; SeedField("_eventCount", 1); SeedField("_ready", true);
        }
        private void SeedField<T>(string name, T value)
        {
            var field = RequiredField<T>(name); Check(!field.IsInitOnly, "Cannot seed readonly backend field: " + name);
            field.SetValue(Backend, value);
        }
        private static FieldInfo RequiredField<T>(string name)
        {
            var field = typeof(NativeLocalUnityEventBackend).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The controlled backend field was removed: " + name);
            Check(field.FieldType == typeof(T), "Controlled backend field type changed: " + name); return field;
        }
        private T Field<T>(string name) => (T)RequiredField<T>(name).GetValue(Backend)!;
        internal Dictionary<long, int> Slots => Field<Dictionary<long, int>>("_rootSlots");
        internal void AdoptActualReference(IntPtr pointer)
        {
            var method = typeof(NativeLocalUnityEventBackend).GetMethod("AdoptDispatchReference", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(IntPtr) }, null) ?? throw new InvalidOperationException("Actual dispatch adoption method was removed.");
            Check(method.ReturnType == typeof(long)); Equal(pointer.ToInt64(), (long)method.Invoke(Backend, new object[] { pointer })!);
        }
        internal void VerifyAdoptionPrefix(params IntPtr[] references)
        {
            Check(Native.ListenerDispatchTrace.Take(references.Length).SequenceEqual(references.Select(Root)));
            Check(Native.ListenerDispatchTrace.Count > references.Length
                && Native.ListenerDispatchTrace[references.Length] == Target(references[0]));
        }
        internal void VerifyRetained(int attempts, int expectedRoots, IEnumerable<IntPtr> identities, bool allowTemporaryFrees = false)
        {
            Equal(attempts, Field<int>("_rootAttempts")); Equal(expectedRoots, Backend.RootRecords);
            Equal(expectedRoots, Slots.Count); Equal(expectedRoots, Native.ActiveRoots);
            var roots = Field<NativeRuntime.RootHandle?[]>("_roots"); Equal(2048, roots.Length);
            var expected = identities.ToArray(); Equal(expectedRoots, expected.Length);
            for (var index = 0; index < expected.Length; ++index)
            {
                Check(Slots.TryGetValue(expected[index].ToInt64(), out var slot) && slot == index);
                Check(roots[index] is not null && Native.HasRootFor(expected[index]));
            }
            Check(roots.Skip(expectedRoots).All(root => root is null));
            if (!allowTemporaryFrees) Equal(0, Native.FreeHandleAttempts);
        }
        internal void VerifyUnknownStopsFurtherReads()
        {
            Check(Backend.CleanupUnknown);
            var trace = Native.ListenerDispatchTrace.ToArray(); var roots = Native.ActiveRoots;
            var attempts = Field<int>("_rootAttempts"); var fields = Native.FieldReads; var invokes = Native.Invocations;
            var frees = Native.FreeHandleAttempts; var arguments = Backend.RetainedCallArgumentRecords;
            Throws<InvalidOperationException>(() => Backend.CaptureDispatch(0, Frame));
            Throws<InvalidOperationException>(() => Backend.ReadGraph(0));
            Throws<InvalidOperationException>(() => Backend.CapturePrepared(0, Frame.List.ToInt64()));
            Check(Native.ListenerDispatchTrace.SequenceEqual(trace)); Equal(fields, Native.FieldReads); Equal(invokes, Native.Invocations);
            Equal(roots, Native.ActiveRoots); Equal(frees, Native.FreeHandleAttempts); Equal(attempts, Field<int>("_rootAttempts"));
            Equal(arguments, Backend.RetainedCallArgumentRecords);
        }
    }

    private sealed class Authority : ICutoutListenerAuthority
    {
        internal bool Faulted;
        public bool Unknown => Faulted;
        public void VerifyBeforeCapture() { if (Faulted) throw new InvalidOperationException("Controlled owner faulted before capture."); }
        public void VerifyObservation() { if (Faulted) throw new InvalidOperationException("Controlled source authority is unknown."); }
        public bool TryConfirmSourceIdle() => true;
        public void ObserveFaultFromAnyThread() => Faulted = true;
    }
    private sealed class OwnerBackend : ICutoutListenerBackend
    {
        public bool CleanupUnknown => false;
        public int RootRecords => 0;
        public void Initialize(NativeLocalUnityEventLedger owner) { }
        public NativeUnityEventGraph ReadGraph(int eventSlot) => throw new InvalidOperationException("The controlled owner has no registered graphs.");
        public NativeUnityEventPrepared CapturePrepared(int eventSlot, long returnedList) => throw new InvalidOperationException("No synthetic Prepare is supplied.");
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(0, true);
    }
    private sealed class SourceBackend : INativeCutoutEventSourceBackend
    {
        private readonly NativeCutoutEventObservation _source;
        internal SourceBackend(long block) => _source = new(block + 0x500, block + 0x510, block + 0x520,
            block + 0x530, block + 0x540, 0, true, Array.Empty<NativeCutoutTrigger>());
        public bool CleanupUnknown => false;
        public int RootRecords => 0;
        public NativeCutoutEventObservation Capture() => _source;
        public void Bind(NativeLocalCutoutEventSource owner) { }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(0, true);
        public NativeCutoutEventObservation Refresh() => _source;
    }
    private sealed class ActivityEngine : INativeDetourEngine, IPreparedNativeDetourEngine
    {
        internal int Prepared, Committed;
        public Delegate Install(VerifiedHookTarget target, Delegate replacement) => throw new InvalidOperationException("Only the existing injected activity bridge is allowed.");
        public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement) { ++Prepared; return new(0x1234); }
        public void Commit(VerifiedHookTarget target) => ++Committed;
    }
    private sealed class ActivityBridge : INativeVoidContextBridge
    {
        public IntPtr EntryAddress => new(0x1235);
        public void Configure(IntPtr original, Func<NativeVoidFrame, nuint> enter, Action<NativeVoidFrame, nuint, NativeVoidExitFlags> exit) { }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(0, true);
    }
    private sealed class EmptyContext : IDisposable { public void Dispose() { } }
    private sealed class EmptyBundle : IDisposable { public void Dispose() { } }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
}

// Test-local extension of the existing export seam. Delegate types are the
// actual NativeRuntime types, preserving their managed ABI and boolean marshal.
sealed partial class FakeNative
{
    internal readonly List<string> ListenerDispatchTrace = new();
    internal int ListenerDispatchRootCalls, ListenerDispatchTargetReads, ListenerDispatchClassReads;
    internal int ListenerDispatchFailRoot, ListenerDispatchFailTarget, ListenerDispatchFailClass;
    internal Action<int, IntPtr>? ListenerDispatchOnRoot;

    internal NativeRuntime CreateListenerDispatchAdoptionApi()
    {
        Add("il2cpp_gchandle_new", "NewHandle", nameof(ListenerDispatchNewHandle));
        Add("il2cpp_gchandle_get_target", "HandleTarget", nameof(ListenerDispatchHandleTarget));
        Add("il2cpp_object_get_class", "Ptr1", nameof(ListenerDispatchObjectClass));
        var api = new NativeRuntime(ExportPointer); api.BindCurrentThread(); return api;
    }
    private uint ListenerDispatchNewHandle(IntPtr pointer, bool pinned)
    {
        ListenerDispatchTrace.Add("root:" + pointer.ToInt64()); ++ListenerDispatchRootCalls;
        ListenerDispatchOnRoot?.Invoke(ListenerDispatchRootCalls, pointer);
        return ListenerDispatchRootCalls == ListenerDispatchFailRoot ? 0u : NewHandle(pointer, pinned);
    }
    private IntPtr ListenerDispatchHandleTarget(uint id)
    {
        var target = HandleTarget(id); ListenerDispatchTrace.Add("target:" + target.ToInt64()); ++ListenerDispatchTargetReads;
        return ListenerDispatchTargetReads == ListenerDispatchFailTarget ? IntPtr.Zero : target;
    }
    private IntPtr ListenerDispatchObjectClass(IntPtr pointer)
    {
        ListenerDispatchTrace.Add("class:" + pointer.ToInt64()); ++ListenerDispatchClassReads;
        return ListenerDispatchClassReads == ListenerDispatchFailClass ? IntPtr.Zero : ObjectClass(pointer);
    }
}
