using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class StartChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ProgressResourceIsolationBoundary(IntPtr self, IntPtr reference, IntPtr method, uint stage);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ConfigureProgressResourceIsolation(uint flags, ProgressResourceIsolationBoundary? boundary);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadProgressResourceIsolationOwner(IntPtr owner, ref NativePlaybackSourceInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadProgressResourceIsolationTicket(IntPtr owner, IntPtr receipt,
        ref NativePlaybackStockSourceHub.ProgressResourceInformation info);

    private static int RunProgressResourceIsolation(string[] arguments, string mode)
    {
        try
        {
            Check(arguments.Length == 5 && mode is "resource-foreign" or "resource-worker" or "resource-seh"
                && Hash(arguments[1]) == PlaybackSourceGroupTargets.ProgressResourceFixtureSha256,
                "Resource isolation requires its exact fixed twenty-member owned fixture.");
            using var native = new ConstructorContext(arguments);
            Check(native.Targets.HasProgressResource && native.Group.HasProgressResource
                && native.Broker.ReadActualPlaybackDobbyCallCounts() == (20, 20),
                "Resource isolation did not install all twenty actual physical members.");
            native.Arm();
            var rooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
            var actual = Private<NativePlaybackActualSource>(rooted, "_actual");
            var sourceRoots = Private<List<NativeRuntime.RootHandle>>(actual, "_roots");
            using var future = native.Group.CaptureProgressResource(rooted, native.Api.GameplayApi);
            var basis = future.Read();
            Check(basis.Phase == 1 && basis.Current == 1 && basis.Initializer == 0 && basis.InitializerOrdinal == 0
                && basis.Reference == unchecked((ulong)native.Api.ProgressResourceA.ToInt64())
                && future.RootsRetained && native.Api.HasRootFor(native.Api.ProgressResourceA)
                && sourceRoots.Count == 7 && native.Api.ActiveRoots == 8,
                "Resource future did not retain its own actual reference root beside the seven source roots.");
            var read = Export<ProgressResourceRead>(native.Module, "StockFixtureProgressResourceRead");
            var configure = Export<ConfigureProgressResourceIsolation>(native.Module, "StockFixtureConfigureProgressResource");
            var baseline = native.Clock.Capture();
            var initialHealth = native.Group.ReadSourceHealth();
            var observed = native.Observations.Count;
            var initialBarrier = read(0); var initialStored = read(6); var initialReturned = read(7);
            var clockReads = native.ClockReads;
            var clip = Marshal.ReadIntPtr(native.Api.GameplaySource, 40);
            var referenceGets = Export<ResourceObjectRead>(native.Module, "StockFixtureResourceRead");
            Check(referenceGets(native.Api.ProgressResourceA, 0) == 0 && referenceGets(native.Api.ProgressResourceB, 0) == 0,
                "Isolation setup loaded a future resource instead of observing the actual setter.");

            if (mode == "resource-foreign")
                ProgressResourceForeignIsolation(native, future, basis, read, configure, baseline, initialHealth,
                    observed, initialBarrier, initialStored, initialReturned, clockReads, clip);
            else
                ProgressResourceUnknownIsolation(native, rooted, sourceRoots, future, basis, read, configure,
                    baseline, initialHealth, observed, initialBarrier, initialStored, initialReturned, clockReads, clip,
                    worker: mode == "resource-worker");

            Check(native.CallbackFailure is null && native.UniqueFinalClock
                && native.Broker.ReadActualPlaybackDobbyCallCounts() == (20, 20)
                && referenceGets(native.Api.ProgressResourceA, 0) == 0 && referenceGets(native.Api.ProgressResourceB, 0) == 0,
                "Resource isolation lost original callback evidence, loaded a resource, or replaced its Clock/physical group: " + native.CallbackFailure);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPrepare = 20, actualCommit = 20, resourceVersion = 1,
                actualForeignOriginals = mode == "resource-foreign", actualCurrentWorker = mode == "resource-worker",
                actualCurrentSeh = mode == "resource-seh", sourceUnknown = mode != "resource-foreign",
                independentResourceRoot = true, retainedRoots = native.Api.ActiveRoots,
                rootsReleased = native.Router.RootsReleased, gameExecuted = false, productionAdmission = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() }));
            return 1;
        }
    }

    private static void ProgressResourceForeignIsolation(ConstructorContext native,
        NativePlaybackStockSourceHub.ProgressResourceTicket future,
        NativePlaybackStockSourceHub.ProgressResourceInformation basis, ProgressResourceRead read,
        ConfigureProgressResourceIsolation configure, NativePlaybackClockState baseline,
        NativePlaybackSourceHealth initialHealth, int observed, ulong initialBarrier, ulong initialStored,
        ulong initialReturned, int clockReads, IntPtr clip)
    {
        var target = native.Targets.ProgressClip;
        var receivers = Enumerable.Range(0, 33)
            .Select(index => native.Api.CreateProgressResourceReceiver((uint)index, native.Api.ProgressResourceA)).ToArray();
        Check(receivers.All(value => value != IntPtr.Zero && value != native.Api.GameplayProgress)
            && Marshal.ReadIntPtr(receivers[0]) == Marshal.ReadIntPtr(native.Api.GameplayProgress)
            && receivers.Skip(1).All(value => Marshal.ReadIntPtr(value) != Marshal.ReadIntPtr(native.Api.GameplayProgress)),
            "Foreign resource isolation needs one same-class Progress and thirty-two unrelated actual receiver classes.");
        var values = new[] { native.Api.ProgressResourceB, IntPtr.Zero, native.Api.ProgressResourceA };
        var methods = new[] { target.MethodInfo, IntPtr.Zero, native.Targets.Pause.MethodInfo };
        void ForeignCall(IntPtr self, int iteration)
        {
            var index = iteration % values.Length;
            if (native.Invoke(4, target.Address, self, unchecked((ulong)values[index].ToInt64()), methods[index], out var code) != 1 || code != 0)
                throw new InvalidOperationException("An actual foreign resource setter failed or changed its native exception outcome.");
        }
        foreach (var receiver in receivers)
            for (var iteration = 0; iteration < 3; ++iteration) ForeignCall(receiver, iteration);
        const int workerIterations = 16;
        var worker = Task.Run(() =>
        {
            foreach (var receiver in receivers)
                for (var iteration = 0; iteration < workerIterations; ++iteration) ForeignCall(receiver, iteration);
        });
        Check(worker.Wait(10000) && worker.IsCompletedSuccessfully, "Actual foreign resource worker/flood did not return.");
        var transparentCalls = checked((ulong)(receivers.Length * (3 + workerIterations)));
        Check(read(0) == initialBarrier + transparentCalls && read(6) == initialStored + transparentCalls
            && read(7) == initialReturned + transparentCalls
            && receivers.All(value => Marshal.ReadIntPtr(value, 0x30) == values[(workerIterations - 1) % values.Length])
            && read(1) == unchecked((ulong)IntPtr.Add(receivers[^1], 0x30).ToInt64())
            && read(2) == unchecked((ulong)values[(workerIterations - 1) % values.Length].ToInt64())
            && read(3) == unchecked((ulong)methods[(workerIterations - 1) % methods.Length].ToInt64()),
            "Foreign resource originals lost their once-only store/barrier, receiver, argument, or incoming MethodInfo.");
        Check(native.Group.ReadSourceHealth() == initialHealth && native.Observations.Count == observed
            && future.Read().Phase == 1 && future.Read().Current == 1 && future.Revision == basis.Revision
            && native.Clock.Capture() == baseline && native.Group.GetTimeLease.ReadCurrent().Active
            && native.ClockReads == clockReads && native.Api.ActiveRoots == 8
            && Marshal.ReadIntPtr(native.Api.GameplayProgress, 0x30) == native.Api.ProgressResourceA
            && Marshal.ReadIntPtr(native.Api.GameplaySource, 40) == clip && !native.Router.PhysicalFaultPending,
            "Foreign same-class/class/MI/worker/flood polluted current Clock, source fences, journal, resource identity, or roots.");
        future.VerifyCurrent();

        // The outer original is foreign and therefore has no managed parent or
        // journal row. Its actual tail deliberately calls the related current
        // setter, which must acquire its own parent-zero observation.
        var nestedCalls = 0; var nestedStatus = 0; uint nestedCode = uint.MaxValue;
        ProgressResourceIsolationBoundary boundary = (self, reference, method, stage) => native.RecordBoundary(() =>
        {
            if (self != receivers[0]) return;
            Check(stage == 1 && reference == native.Api.ProgressResourceB && method == target.MethodInfo
                && Marshal.ReadIntPtr(self, 0x30) == reference, "Foreign original did not reach its actual post-store boundary.");
            ++nestedCalls;
            nestedStatus = native.Invoke(4, target.Address, native.Api.GameplayProgress,
                unchecked((ulong)native.Api.ProgressResourceA.ToInt64()), IntPtr.Zero, out nestedCode);
        });
        configure(0, boundary);
        int status; uint code;
        try { status = native.Invoke(4, target.Address, receivers[0], unchecked((ulong)native.Api.ProgressResourceB.ToInt64()), target.MethodInfo, out code); }
        finally { configure(0, null); GC.KeepAlive(boundary); }
        Check(native.CallbackFailure is null && status == 1 && code == 0 && nestedCalls == 1 && nestedStatus == 1 && nestedCode == 0,
            "Actual foreign-original/current-child reentry failed: " + native.CallbackFailure);
        var child = native.Observations.Skip(observed).Single();
        Check(child.Role == NativePlaybackSourceRole.ProgressClip && child.Self == native.Api.GameplayProgress
            && child.ReferenceArgument == native.Api.ProgressResourceA && child.IncomingMethod == IntPtr.Zero
            && child.ParentSequence == 0 && child.Depth == 1
            && child.TryReadCompletion(out var completion) && completion is
                { PhysicalExited: true, Returned: true, Abnormal: false, Unknown: false },
            "Related child under a foreign original did not own its independent parent-zero physical-exit receipt.");
        var after = native.Group.ReadSourceHealth();
        Check(after.Known && !after.Unknown && after.InFlight == 0 && after.Entered == initialHealth.Entered + 1
            && after.Exited == initialHealth.Exited + 1 && after.WriterSequence == initialHealth.WriterSequence
            && after.SourceSequence == initialHealth.SourceSequence && after.SchedulingSequence == initialHealth.SchedulingSequence
            && native.Observations.Count == observed + 1 && read(0) == initialBarrier + transparentCalls + 2
            && read(6) == initialStored + transparentCalls + 2 && read(7) == initialReturned + transparentCalls + 2
            && future.Read().Phase == 4 && future.Read().Current == 0
            && native.Clock.Capture() == baseline && native.Group.GetTimeLease.ReadCurrent().Active
            && Marshal.ReadIntPtr(native.Api.GameplaySource, 40) == clip && native.Api.ActiveRoots == 8,
            "Only the related child should advance the resource revision without changing audio fences or current Clock.");
        Refuse(() => future.VerifyCurrent(), "The related same-value child revived its old future resource ticket.");
        Check(future.TryRetire() && !future.RootsRetained && native.Api.ActiveRoots == 7
            && !native.Api.HasRootFor(native.Api.ProgressResourceA),
            "Known foreign/current-child physical drain failed to release the independent old reference root.");
        native.Router.Dispose(); native.Router.PumpCompleted();
        Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0 && native.Observations.All(value => value.IsReleased),
            "Foreign resource isolation prevented the known-drained seven-root and observation release.");
    }

    private static void ProgressResourceUnknownIsolation(ConstructorContext native, NativePlaybackStockRootedSource rooted,
        List<NativeRuntime.RootHandle> sourceRoots, NativePlaybackStockSourceHub.ProgressResourceTicket future,
        NativePlaybackStockSourceHub.ProgressResourceInformation basis, ProgressResourceRead read,
        ConfigureProgressResourceIsolation configure, NativePlaybackClockState baseline, NativePlaybackSourceHealth initialHealth,
        int observed, ulong initialBarrier, ulong initialStored, ulong initialReturned, int clockReads, IntPtr clip, bool worker)
    {
        var target = native.Targets.ProgressClip;
        var groupOwner = Private<object>(native.Group, "_owner");
        var hub = Private<NativePlaybackStockSourceHub>(groupOwner, "_hub");
        var readOwner = Export<ReadProgressResourceIsolationOwner>(hub.NativeLibraryHandle, "RtPlaybackSourceReadOwner");
        var readTicket = Export<ReadProgressResourceIsolationTicket>(hub.NativeLibraryHandle, "RtPlaybackSourceReadProgressResourceTicket");
        var receipt = Private<IntPtr>(future, "_receipt");
        var sourceBefore = new NativePlaybackSourceInformation();
        Check(readOwner(hub.NativeOwner, ref sourceBefore) == 1 && sourceBefore.Owner == hub.NativeOwner
            && sourceBefore.Flags == NativePlaybackSourceFlags.Armed && sourceBefore.InFlight == 0,
            "Current resource fault did not start with the actual known native owner.");
        var getTimeOwner = Private<object>(native.Group.GetTimeLease, "_owner");
        var bridge = Private<NativePlaybackSingleReturnBridge>(getTimeOwner, "_bridge");
        var readSingle = Export<ConstructorWorkerReadStats>(Private<IntPtr>(bridge, "_library"), "RizSingleReturnPrototype_GetStats");
        var slot = Private<uint>(bridge, "_slot");
        var ownerThread = ConstructorWorkerThreadId();
        var boundaryCount = 0; long boundaryThread = -1; string? boundaryFailure = null;
        var rawGetTime = 17.25f; var getTimeValue = float.NaN; var originalDelta = 0ul; var appliedDelta = 0ul;
        var retainedWhileInFlight = false;
        if (worker)
        {
            Check((float)baseline.MediaAnchorSeconds != rawGetTime, "Worker raw GetTime must differ from the old Clock anchor.");
            Marshal.Copy(new[] { (double)rawGetTime }, 0, IntPtr.Add(native.Api.GameplaySource, 24), 1);
        }
        ProgressResourceIsolationBoundary boundary = (self, reference, method, stage) =>
        {
            try
            {
                var info = new NativePlaybackSourceInformation();
                var ticket = new NativePlaybackStockSourceHub.ProgressResourceInformation { Version = 1, Size = 112 };
                var thread = ConstructorWorkerThreadId();
                var valid = stage == 1 && self == native.Api.GameplayProgress && reference == native.Api.ProgressResourceB
                    && method == target.MethodInfo && Marshal.ReadIntPtr(self, 0x30) == reference
                    && readOwner(hub.NativeOwner, ref info) == 1 && info.Owner == sourceBefore.Owner
                    && info.OwnerEpoch == sourceBefore.OwnerEpoch && info.InFlight == 1
                    && info.Entered == sourceBefore.Entered + 1 && info.Exited == sourceBefore.Exited
                    && info.WriterSequence == sourceBefore.WriterSequence && info.SourceSequence == sourceBefore.SourceSequence
                    && info.SchedulingSequence == sourceBefore.SchedulingSequence
                    && readTicket(hub.NativeOwner, receipt, ref ticket) == 1 && ticket.Phase == 4 && ticket.Current == 0
                    && ticket.Revision == basis.Revision && ticket.Reference == basis.Reference
                    && read(0) == initialBarrier + 1 && read(6) == initialStored + 1 && read(7) == initialReturned;
                if (worker)
                {
                    valid &= thread != ownerThread && (info.Flags & NativePlaybackSourceFlags.Unknown) != 0
                        && (info.Flags & NativePlaybackSourceFlags.Armed) == 0 && info.Faults > sourceBefore.Faults;
                    var before = new ConstructorWorkerSingleStats { Version = 1, Size = 112 };
                    var after = new ConstructorWorkerSingleStats { Version = 1, Size = 112 };
                    if (readSingle(slot, ref before) != 0) valid = false;
                    getTimeValue = native.Time(native.Api.GameplayPlayer, native.Targets.GetTime.MethodInfo);
                    if (readSingle(slot, ref after) != 0) valid = false;
                    originalDelta = after.OriginalCalls - before.OriginalCalls; appliedDelta = after.Applied - before.Applied;
                }
                else
                {
                    valid &= thread == ownerThread && info.Flags == NativePlaybackSourceFlags.Armed;
                    retainedWhileInFlight = !future.TryRetire() && future.RootsRetained && native.Api.ActiveRoots == 8;
                    valid &= retainedWhileInFlight;
                }
                if (!valid) throw new InvalidOperationException("Actual resource boundary lost its store/revision/identity/physical state.");
                Interlocked.Exchange(ref boundaryThread, thread); Interlocked.Increment(ref boundaryCount);
            }
            catch (Exception error) { Interlocked.CompareExchange(ref boundaryFailure, error.ToString(), null); }
        };
        configure(worker ? 0u : 1u, boundary);
        int status; uint code;
        try
        {
            if (worker)
            {
                var task = Task.Run(() =>
                {
                    var result = native.Invoke(4, target.Address, native.Api.GameplayProgress,
                        unchecked((ulong)native.Api.ProgressResourceB.ToInt64()), target.MethodInfo, out var error);
                    return (Status: result, Code: error);
                });
                Check(task.Wait(10000) && task.IsCompletedSuccessfully, "Actual current resource worker did not return.");
                status = task.Result.Status; code = task.Result.Code;
            }
            else status = native.Invoke(4, target.Address, native.Api.GameplayProgress,
                unchecked((ulong)native.Api.ProgressResourceB.ToInt64()), target.MethodInfo, out code);
        }
        finally { configure(0, null); GC.KeepAlive(boundary); }
        Check(boundaryFailure is null && boundaryCount == 1 && status == (worker ? 1 : 2)
            && code == (worker ? 0u : 0xe2400051u)
            && read(0) == initialBarrier + 1 && read(6) == initialStored + 1
            && read(7) == initialReturned + (worker ? 1ul : 0ul)
            && read(1) == unchecked((ulong)IntPtr.Add(native.Api.GameplayProgress, 0x30).ToInt64())
            && read(2) == unchecked((ulong)native.Api.ProgressResourceB.ToInt64())
            && read(3) == unchecked((ulong)target.MethodInfo.ToInt64()) && read(5) == unchecked((ulong)boundaryThread),
            "Current resource worker/SEH lost once-only original store/barrier, incoming arguments, or precise native exception: " + boundaryFailure);
        if (worker)
            Check(native.Observations.Count == observed && native.ClockReads == clockReads && getTimeValue == rawGetTime
                && originalDelta == 1 && appliedDelta == 0,
                "Current resource worker entered managed observers or consumed the old Clock instead of the actual raw GetTime original.");
        else
        {
            var attempt = native.Observations.Skip(observed).Single();
            Check(retainedWhileInFlight && attempt.Role == NativePlaybackSourceRole.ProgressClip
                && attempt.Self == native.Api.GameplayProgress && attempt.ParentSequence == 0 && attempt.Depth == 1
                && attempt.TryReadCompletion(out var completion) && completion is
                    { PhysicalExited: true, Returned: false, Abnormal: true, Unknown: true },
                "Resource SEH lacks its own abnormal physical-exit receipt or released the independent root while in flight.");
        }
        var sourceAfter = new NativePlaybackSourceInformation();
        Check(readOwner(hub.NativeOwner, ref sourceAfter) == 1 && (sourceAfter.Flags & NativePlaybackSourceFlags.Unknown) != 0
            && (sourceAfter.Flags & NativePlaybackSourceFlags.Armed) == 0 && sourceAfter.Faults > sourceBefore.Faults
            && sourceAfter.InFlight == 0 && sourceAfter.Entered == sourceBefore.Entered + 1 && sourceAfter.Exited == sourceBefore.Exited + 1
            && sourceAfter.WriterSequence == initialHealth.WriterSequence && sourceAfter.SourceSequence == initialHealth.SourceSequence
            && sourceAfter.SchedulingSequence == initialHealth.SchedulingSequence
            && Marshal.ReadIntPtr(native.Api.GameplayProgress, 0x30) == native.Api.ProgressResourceB
            && Marshal.ReadIntPtr(native.Api.GameplaySource, 40) == clip
            && future.Read().Phase == 4 && future.Read().Current == 0
            && future.RootsRetained && native.Api.HasRootFor(native.Api.ProgressResourceA) && native.Api.ActiveRoots == 8,
            "Actual resource fault lost sticky Unknown, exact physical exit, untouched audio graph/fences, or the independent old reference root.");
        native.Router.PumpCompleted();
        Check(native.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn && !native.Group.IsActive,
            "Owner pump retained the Clock or actual physical group after resource worker/SEH Unknown.");
        Refuse(() => future.VerifyCurrent(), "Unknown resource future retained capture authority.");
        Refuse(() => { _ = rooted.Current; }, "Unknown resource attempt retained current source authority.");
        Check(!future.TryRetire() && future.RootsRetained, "Unknown resource ticket fabricated native retirement at scalar zero in-flight.");
        native.Router.Dispose(); native.Router.PumpCompleted(); native.Group.PumpCompleted();
        Check(native.Group.ReadActivity() is { Known: false, InFlight: 0 }
            && !native.Router.RootsReleased && !rooted.RootsReleased && sourceRoots.Count == 7
            && future.RootsRetained && native.Api.HasRootFor(native.Api.ProgressResourceA) && native.Api.ActiveRoots == 8,
            "Unknown resource fault was promoted into a successful resource/seven-root release after its original physically exited.");
    }
}
