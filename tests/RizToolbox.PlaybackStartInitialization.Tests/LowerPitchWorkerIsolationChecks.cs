using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

sealed partial class FakeNative
{
    // Test-only ownership tracking for the foreign Unity wrapper allocated by the real fixture.
    internal IntPtr CreateLowerForeignSource()
    {
        var value = CreateSharedConstructorSource();
        UnityTrack(value, 1, registered: true);
        return value;
    }
}

internal static partial class StartChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LowerWorkerBoundary(IntPtr self, uint bits, uint at);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LowerWorkerConfigure(uint flags, LowerWorkerBoundary? callback);

    private static int RunLowerWorkerIsolation(string[] a, string mode)
    {
        try
        {
            Check(a.Length == 5 && mode is "lower-pitch-worker" or "lower-pitch-foreign"
                && Hash(a[1]) == NativePlaybackLowerPitchTarget.OwnedWrapperImageSha256
                && Hash(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(a[1]))!, "RizToolbox.PlaybackLowerPitchFixture.dll"))
                    == NativePlaybackLowerPitchTarget.OwnedLowerImageSha256,
                "Lower worker isolation requires the exact two owned fixture images.");
            var native = new ConstructorContext(a);
            Check(native.Targets.HasLowerPitch && native.Targets.HasUnityBacking && native.PhysicalMemberCount == 21
                && native.Broker.ReadActualPlaybackDobbyCallCounts() == (21, 21)
                && Marshal.SizeOf<NativePlaybackSourceInformation>() == 136
                && Marshal.SizeOf<ConstructorWorkerSingleStats>() == 112,
                "The lower worker scope did not install its current actual twenty-one-member group and raw native layouts.");
            var read = Export<LowerFixtureRead>(native.LowerModule, "StockLowerFixtureRead");
            var configure = Export<LowerWorkerConfigure>(native.LowerModule, "StockLowerFixtureConfigure");
            var lower = Marshal.GetDelegateForFunctionPointer<LowerSetPitch>(native.Targets.SourceTarget(NativePlaybackSourceRole.LowerSetPitch).Address);
            object evidence;
            using (var sdk = new ConstructorSdk(native))
            {
                sdk.Speed(1.5);
                evidence = mode == "lower-pitch-worker" ? CheckLowerCurrentWorker(native, sdk, read, configure, lower)
                    : CheckLowerForeignFlood(native, sdk, read, configure, lower);
            }
            native.Router.PumpCompleted();
            var unknown = mode == "lower-pitch-worker";
            if (unknown)
            {
                native.Dispose();
                Check(!native.Router.RootsReleased && native.Api.ActiveRoots == 7
                    && native.Group.ReadActivity() is { Known: false, InFlight: 0 },
                    "Disposing the SDK and native router dropped the seven actual Unknown source roots.");
            }
            else
            {
                native.Router.Dispose(); native.Router.PumpCompleted(); native.Group.PumpCompleted();
                Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0 && native.Observations.All(value => value.IsReleased),
                    "The known foreign flood failed to release its exact actual source roots.");
            }
            Check(native.CallbackFailure is null && native.UniqueFinalClock
                && native.Broker.ReadActualPlaybackDobbyCallCounts() == (21, 21),
                "Lower isolation changed its physical hooks, unique final Clock or callback health: " + native.CallbackFailure);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPrepare = 21, actualCommit = 21, physicalSources = 20, nativeSourceVersion = 4,
                sdkExecuted = true, independentPluginScopes = 2, sourceUnknown = unknown,
                retainedRoots = native.Api.ActiveRoots, evidence, gameExecuted = false, installed = false, productionAdmission = false }));
            if (!unknown) native.Dispose();
            GC.KeepAlive(native);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() }));
            return 1;
        }
    }

    private static (NativePlaybackStockSourceHub Hub, IntPtr Owner, DirectOwnerInfo ReadSource,
        ConstructorWorkerReadStats ReadSingle, uint Slot) LowerRawChannels(ConstructorContext native)
    {
        var getTimeOwner = Private<object>(native.Group.GetTimeLease, "_owner");
        var bridge = Private<NativePlaybackSingleReturnBridge>(getTimeOwner, "_bridge");
        var singleLibrary = Private<IntPtr>(bridge, "_library"); var slot = Private<uint>(bridge, "_slot");
        var groupOwner = Private<object>(native.Group, "_owner");
        var hub = Private<NativePlaybackStockSourceHub>(groupOwner, "_hub");
        return (hub, hub.NativeOwner, Export<DirectOwnerInfo>(hub.NativeLibraryHandle, "RtPlaybackSourceReadOwner"),
            Export<ConstructorWorkerReadStats>(singleLibrary, "RizSingleReturnPrototype_GetStats"), slot);
    }

    private static object CheckLowerCurrentWorker(ConstructorContext native, ConstructorSdk sdk, LowerFixtureRead read,
        LowerWorkerConfigure configure, LowerSetPitch lower)
    {
        var source = native.Api.GameplaySource; var player = native.Api.GameplayPlayer; var oldClock = native.Clock;
        var rooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
        var actual = Private<NativePlaybackActualSource>(rooted, "_actual");
        var roots = Private<List<NativeRuntime.RootHandle>>(actual, "_roots");
        var initialIdentity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
        Check(initialIdentity is { NativeDescriptor: not null } && roots.Count == 7,
            "Current lower worker needs the original exact native descriptor and seven actual source roots.");
        var journalView = Private<IPluginContext>(sdk.Lease, "Context")
            .GetService<IRunModificationService>(RunModificationCapabilityIds.Read).Service!;
        var journal = Need(journalView.Read());
        using (var captured = sdk.Provider.Capture(native.Origin))
        using (var writer = captured.PreparePitchWrite())
        using (var prepared = DirectSdkPrepare(native, sdk))
        {
            var raw = LowerRawChannels(native); var initial = new NativePlaybackSourceInformation();
            var single = new ConstructorWorkerSingleStats { Version = 1, Size = 112 };
            Check(raw.ReadSource(raw.Owner, ref initial) == 1 && initial.Owner == raw.Owner && initial.ConfiguredSlots == 20
                && initial.Flags == NativePlaybackSourceFlags.Armed && initial.InFlight == 0
                && raw.ReadSingle(raw.Slot, ref single) == 0 && single.AuthorityActive == 1,
                "Current lower worker did not start with the actual known source and armed GetTime policy.");
            var bodyCalls = read(0); var stores = read(1);
            var pitchCalls = Marshal.ReadInt32(source, native.SourceField(20));
            var observed = native.Observations.Count; var clockReads = native.ClockReads; var activeRoots = native.Api.ActiveRoots;
            var ownerThread = ConstructorWorkerThreadId();
            var originalGetTimeOffset = checked((int)native.Api.UnityFixtureRead(12));
            var originalGetTimeMethodOffset = checked((int)native.Api.UnityFixtureRead(13));
            var originalGetTimeCalls = unchecked((ulong)Marshal.ReadInt64(source, originalGetTimeOffset));
            const uint rawPitch = 0x40000000; const float rawGetTime = 17.25f;
            Check(oldClock.Capture().MediaAnchorSeconds != rawGetTime, "Raw lower-worker GetTime must differ from the retired Clock anchor.");
            Marshal.WriteInt64(source, native.SourceField(24), BitConverter.DoubleToInt64Bits(rawGetTime));
            var time = native.Time; var timeMethod = native.Targets.GetTime.MethodInfo;
            var count = 0; var mask = 0; var error = 0; var beforeValid = 0; var afterValid = 0;
            var getTimeCalls = 0; var getTimeBits = 0; var getTimeValid = 0;
            long boundaryThread = -1, beforeFlags = -1, beforeActive = -1, beforeApplied = -1, afterApplied = -1, beforeOriginal = -1, afterOriginal = -1;
            LowerWorkerBoundary boundary = (self, bits, at) =>
            {
                // Worker: native POD/export reads, exact patched GetTime target and atomic recording only.
                // No owner Router/Group/NativeRuntime/SDK/provider/writer/prepared API is touched here.
                try
                {
                    Interlocked.Increment(ref count); if (at is 1 or 2) Interlocked.Or(ref mask, 1 << (int)at);
                    var info = new NativePlaybackSourceInformation();
                    var stats = new ConstructorWorkerSingleStats { Version = 1, Size = 112 };
                    var sourceStatus = raw.ReadSource(raw.Owner, ref info); var singleStatus = raw.ReadSingle(raw.Slot, ref stats);
                    var thread = read(4);
                    var valid = self == source && bits == rawPitch && sourceStatus == 1 && singleStatus == 0
                        && info.Owner == raw.Owner && info.OwnerEpoch == initial.OwnerEpoch && info.OwnerThreadId == ownerThread
                        && (info.Flags & NativePlaybackSourceFlags.Unknown) != 0 && (info.Flags & NativePlaybackSourceFlags.Armed) == 0
                        && info.InFlight == 1 && info.Entered == initial.Entered + 1 && info.Exited == initial.Exited
                        && info.WriterSequence == initial.WriterSequence + 1 && info.SourceSequence == initial.SourceSequence
                        && info.SchedulingSequence == initial.SchedulingSequence && info.Faults > initial.Faults
                        && read(0) == bodyCalls + 1 && read(1) == stores + (at == 1 ? 0ul : 1ul)
                        && Marshal.ReadInt32(source, 28) == pitchCalls + (at == 1 ? 0 : 1)
                        && thread != ownerThread && thread == ConstructorWorkerThreadId();
                    if (at == 1)
                    {
                        Interlocked.Exchange(ref beforeValid, valid ? 1 : -1);
                        Interlocked.Exchange(ref boundaryThread, unchecked((long)thread));
                        Interlocked.Exchange(ref beforeFlags, (long)info.Flags); Interlocked.Exchange(ref beforeActive, stats.AuthorityActive);
                        var value = time(player, timeMethod);
                        var after = new ConstructorWorkerSingleStats { Version = 1, Size = 112 };
                        var status = raw.ReadSingle(raw.Slot, ref after);
                        Interlocked.Exchange(ref getTimeBits, BitConverter.SingleToInt32Bits(value));
                        Interlocked.Exchange(ref beforeApplied, unchecked((long)stats.Applied));
                        Interlocked.Exchange(ref afterApplied, unchecked((long)after.Applied));
                        Interlocked.Exchange(ref beforeOriginal, unchecked((long)stats.OriginalCalls));
                        Interlocked.Exchange(ref afterOriginal, unchecked((long)after.OriginalCalls));
                        Interlocked.Exchange(ref getTimeValid, status == 0 ? 1 : -1); Interlocked.Increment(ref getTimeCalls);
                    }
                    else if (at == 2) Interlocked.Exchange(ref afterValid, valid ? 1 : -1);
                }
                catch (Exception) { Interlocked.Exchange(ref error, 1); }
            };
            configure(0, boundary); native.Api.BeginLowerIl2CppAudit();
            var worker = Task.Run(() => lower(source, BitConverter.Int32BitsToSingle(unchecked((int)rawPitch))));
            Check(worker.Wait(10000) && worker.IsCompletedSuccessfully, "The actual two-parameter current lower worker failed or timed out.");
            var il2CppCalls = Interlocked.Read(ref native.Api.LowerIl2CppCalls);
            var workerIl2CppCalls = Interlocked.Read(ref native.Api.LowerWorkerIl2CppCalls);
            native.Api.EndLowerIl2CppAudit(); configure(0, null); GC.KeepAlive(boundary);
            Check(error == 0 && count == 2 && mask == 6 && beforeValid == 1 && afterValid == 1,
                $"Lower worker did not become sticky native Unknown before store: flags={beforeFlags}, boundaries={beforeValid}/{afterValid}, count={count}, error={error}.");
            var value = BitConverter.Int32BitsToSingle(getTimeBits); var appliedDelta = afterApplied - beforeApplied;
            var originalCallsDelta = afterOriginal - beforeOriginal; var clockReadsDelta = native.ClockReads - clockReads;
            var fixtureGetTimeDelta = unchecked((ulong)Marshal.ReadInt64(source, originalGetTimeOffset)) - originalGetTimeCalls;
            Check(getTimeCalls == 1 && getTimeValid == 1 && value == rawGetTime && appliedDelta == 0 && originalCallsDelta == 1
                && fixtureGetTimeDelta == 1 && Marshal.ReadIntPtr(source, originalGetTimeMethodOffset) == timeMethod
                && clockReadsDelta == 0 && il2CppCalls == 0 && workerIl2CppCalls == 0,
                $"Lower worker crossed freshness/IL2CPP: GetTime={value:R}, calls={getTimeCalls}, nativeOriginal={originalCallsDelta}/{fixtureGetTimeDelta}, applied={appliedDelta}, clock={clockReadsDelta}, IL2CPP={il2CppCalls}/{workerIl2CppCalls}.");
            var afterSource = new NativePlaybackSourceInformation();
            Check(raw.ReadSource(raw.Owner, ref afterSource) == 1 && (afterSource.Flags & NativePlaybackSourceFlags.Unknown) != 0
                && (afterSource.Flags & NativePlaybackSourceFlags.Armed) == 0 && afterSource.InFlight == 0
                && afterSource.Entered == initial.Entered + 1 && afterSource.Exited == initial.Exited + 1
                && afterSource.WriterSequence == initial.WriterSequence + 1 && afterSource.SourceSequence == initial.SourceSequence
                && afterSource.SchedulingSequence == initial.SchedulingSequence && afterSource.Faults > initial.Faults
                && read(0) == bodyCalls + 1 && read(1) == stores + 1 && read(2) == unchecked((ulong)source.ToInt64())
                && read(3) == rawPitch && read(4) == unchecked((ulong)boundaryThread)
                && Marshal.ReadInt32(source, native.SourceField(20)) == pitchCalls + 1
                && unchecked((uint)Marshal.ReadInt32(source, native.SourceField(16))) == rawPitch
                && native.Observations.Count == observed && native.ClockReads == clockReads && native.CallbackFailure is null
                && native.Api.ActiveRoots == activeRoots,
                "The current lower worker lost raw original/store/physical drain or entered a managed source/Clock/IL2CPP path.");
            var observationDelta = native.Observations.Count - observed;
            native.Router.PumpCompleted(); sdk.Provider.PumpCompleted(); native.Router.PumpCompleted();
            Check(oldClock.Capture().Phase == NativePlaybackPhase.Withdrawn && !sdk.Provider.Probe().Supported,
                "Owner pump kept the worker-faulted lower Clock/provider alive.");
            Refuse(() => { _ = rooted.Current; }, "Old rooted descriptor survived current lower worker Unknown.");
            Refuse(() => { _ = sdk.Provider.CaptureSourceDescriptor(); }, "Provider reissued an old native descriptor after current lower worker Unknown.");
            Refuse(captured.VerifyAuthority, "Old provider source survived current lower worker Unknown.");
            Refuse(writer.VerifyCurrent, "Old continuous pitch writer survived current lower worker Unknown.");
            Refuse(prepared.Commit, "Old prepared control committed after current lower worker Unknown.");
            prepared.Restore();
            CheckLowerUnknownWithdrawalViews(native, sdk, raw.ReadSingle, raw.Slot);
            CheckUnityBackingJournalUnchanged(sdk, journalView, journal);
            sdk.Lease.Dispose();
            var acquired = sdk.Other.Acquire(true); var otherAcquired = acquired.Success;
            if (acquired.Success)
            {
                using var lease = Need(acquired);
                Check(!lease.Execute(new GameplayControlRequest(GameplayControlAction.SetSpeed, lease.ControllerGeneration, lease.ObservedRunId,
                    3, GameplayControlValueUnit.PlaybackRateMultiplier)).Success,
                    "The second SDK scope recovered speed after sticky lower worker Unknown.");
            }
            Check(Marshal.ReadInt32(source, native.SourceField(20)) == pitchCalls + 1
                && unchecked((uint)Marshal.ReadInt32(source, native.SourceField(16))) == rawPitch
                && !rooted.RootsReleased && !native.Router.RootsReleased && roots.Count == 7 && native.Api.ActiveRoots >= 7,
                "Old SDK/prepared disposal wrote into the worker-mutated source or released its seven Unknown roots.");
            return new { ownerThread, workerThread = boundaryThread, boundaryCount = count, beforeStoreSourceFlags = beforeFlags,
                beforeStorePolicyActive = beforeActive, beforeStoreGetTime = value, expectedRawGetTime = rawGetTime,
                appliedDelta, originalCallsDelta, fixtureGetTimeDelta, clockReadsDelta, il2CppCalls, workerIl2CppCalls,
                managedSourceObserverDelta = observationDelta, nativeWriterDelta = afterSource.WriterSequence - initial.WriterSequence,
                sourceFenceDelta = afterSource.SourceSequence - initial.SourceSequence, schedulingFenceDelta = afterSource.SchedulingSequence - initial.SchedulingSequence,
                nativePhysicalExit = afterSource.InFlight == 0, oldProviderDescriptorWriterPreparedSdkRevoked = true, otherSdkRawLeaseAcquired = otherAcquired };
        }
    }

    // A normal backing replacement keeps a known physical group, whereas a
    // current worker writer makes the group sticky Unknown. The latter must
    // reject broker authority reads. Inspect retained native POD separately;
    // no active receipt may be manufactured just to query its inactive policy.
    private static void CheckLowerUnknownWithdrawalViews(ConstructorContext native, ConstructorSdk sdk,
        ConstructorWorkerReadStats readSingle, uint slot)
    {
        var providerSupported = sdk.Provider.Probe().Supported;
        var first = Need(sdk.ClockView.Read());
        var second = Need(sdk.OtherClock.Read());
        var identity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
        sdk.SampleControls();
        var state = Need(sdk.Controls.GetState());
        var speedSupported = state.Operations.Single(operation => operation.Action == GameplayControlAction.SetSpeed).Supported;
        var oldLease = sdk.Lease.Execute(sdk.Request(3));
        var brokerRejected = false;
        try { _ = native.Group.GetTimeLease.ReadCurrent(); }
        catch (UnsupportedSignatureException) { brokerRejected = true; }
        var stats = new ConstructorWorkerSingleStats { Version = 1, Size = 112 };
        var nativeStatus = readSingle(slot, ref stats);
        Console.WriteLine(JsonSerializer.Serialize(new { diagnostic = "unknown-lower-native-authority-independent-clock-views",
            firstGeneration = first.ControllerGeneration, secondGeneration = second.ControllerGeneration,
            predecessorGeneration = sdk.Lease.ControllerGeneration,
            nativeDescriptor = identity?.NativeDescriptor is not null, providerSupported, speedSupported,
            oldLeaseSucceeded = oldLease.Success, oldLeaseError = oldLease.Error?.Code.ToString(),
            brokerRejected, nativeStatus, actualNativePolicyActive = stats.AuthorityActive }));
        Check(first.ControllerGeneration > sdk.Lease.ControllerGeneration
            && second.ControllerGeneration > sdk.Lease.ControllerGeneration
            && first.AudioDspTime.Available && second.AudioDspTime.Available
            && first.HostMonotonicTime.Available && second.HostMonotonicTime.Available
            && identity is { NativeDescriptor: null } && !providerSupported && !speedSupported
            && !oldLease.Success && oldLease.Error?.Code == ErrorCode.TargetExpired
            && brokerRejected && nativeStatus == 0 && stats.AuthorityActive == 0,
            "Unknown lower worker revived native authority, lost public observations or admitted a broker authority read.");
    }

    private static object CheckLowerForeignFlood(ConstructorContext native, ConstructorSdk sdk, LowerFixtureRead read,
        LowerWorkerConfigure configure, LowerSetPitch lower)
    {
        var foreign = native.Api.CreateLowerForeignSource(); var current = native.Api.GameplaySource;
        Check(foreign != current && Marshal.ReadIntPtr(foreign) == Marshal.ReadIntPtr(current)
            && Marshal.ReadIntPtr(foreign, 16) != IntPtr.Zero,
            "Lower foreign flood needs a distinct actual backed wrapper of the same exact class.");
        var rooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source"); var receipt = rooted.Current;
        var identity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
        Check(identity is { NativeDescriptor: not null }, "Foreign lower flood started without the existing SDK native descriptor.");
        var journalView = Private<IPluginContext>(sdk.Lease, "Context").GetService<IRunModificationService>(RunModificationCapabilityIds.Read).Service!;
        var journal = Need(journalView.Read()); var journalSequence = Private<long>(sdk.Journal, "_sequence");
        var journalRevision = Private<long>(sdk.Journal, "_revision");
        const int ownerIterations = 3, workerIterations = 320; const uint bits = 0x7fc12345;
        var calls = read(0); var stores = read(1); var foreignCalls = Marshal.ReadInt32(foreign, native.SourceField(20));
        var currentCalls = Marshal.ReadInt32(current, native.SourceField(20));
        var raw = LowerRawChannels(native); var initialRaw = new NativePlaybackSourceInformation();
        var initialSingle = new ConstructorWorkerSingleStats { Version = 1, Size = 112 };
        Check(raw.ReadSource(raw.Owner, ref initialRaw) == 1 && initialRaw.ConfiguredSlots == 20
            && raw.ReadSingle(raw.Slot, ref initialSingle) == 0 && initialSingle.AuthorityActive == 1,
            "Foreign flood raw source/policy baseline was unavailable.");
        var boundaryCount = 0; var boundaryError = 0; long workerThread = -1; var ownerThread = ConstructorWorkerThreadId();
        LowerWorkerBoundary boundary = (self, actualBits, at) =>
        {
            try
            {
                Interlocked.Increment(ref boundaryCount);
                var info = new NativePlaybackSourceInformation(); var single = new ConstructorWorkerSingleStats { Version = 1, Size = 112 };
                if (self != foreign || actualBits != bits || at is not (1 or 2)
                    || raw.ReadSource(raw.Owner, ref info) != 1 || !info.Equals(initialRaw)
                    || raw.ReadSingle(raw.Slot, ref single) != 0 || !single.Equals(initialSingle))
                    Interlocked.Exchange(ref boundaryError, 1);
                var thread = ConstructorWorkerThreadId(); if (thread != ownerThread) Interlocked.Exchange(ref workerThread, thread);
            }
            catch (Exception) { Interlocked.Exchange(ref boundaryError, 1); }
        };
        var observed = native.Observations.Count; var clockReads = native.ClockReads; var rootsBeforePreparation = native.Api.ActiveRoots;
        var health = native.Group.ReadSourceHealth(); var initialization = native.Group.ReadInitialization(); var clock = native.Clock.Capture();
        long il2CppCalls, workerIl2CppCalls;
        using (var captured = sdk.Provider.Capture(native.Origin))
        using (var writer = captured.PreparePitchWrite())
        using (var prepared = DirectSdkPrepare(native, sdk))
        {
            // These two actual temporary captures own additional roots.
            // Compare foreign originals with the state immediately before
            // those originals, after preparation has completed.
            var roots = native.Api.ActiveRoots;
            configure(0, boundary); native.Api.BeginLowerIl2CppAudit();
            for (var iteration = 0; iteration < ownerIterations; ++iteration) lower(foreign, BitConverter.Int32BitsToSingle(unchecked((int)bits)));
            var worker = Task.Run(() =>
            {
                for (var iteration = 0; iteration < workerIterations; ++iteration) lower(foreign, BitConverter.Int32BitsToSingle(unchecked((int)bits)));
            });
            Check(worker.Wait(10000) && worker.IsCompletedSuccessfully, "The same-class foreign lower worker flood did not physically return.");
            il2CppCalls = Interlocked.Read(ref native.Api.LowerIl2CppCalls); workerIl2CppCalls = Interlocked.Read(ref native.Api.LowerWorkerIl2CppCalls);
            native.Api.EndLowerIl2CppAudit(); configure(0, null); GC.KeepAlive(boundary);
            var afterRaw = new NativePlaybackSourceInformation();
            Check(boundaryCount == 2 * (ownerIterations + workerIterations) && boundaryError == 0 && workerThread != ownerThread && workerThread > 0
                && raw.ReadSource(raw.Owner, ref afterRaw) == 1 && afterRaw.Equals(initialRaw)
                && read(0) == calls + ownerIterations + workerIterations && read(1) == stores + ownerIterations + workerIterations
                && read(2) == unchecked((ulong)foreign.ToInt64()) && read(3) == bits && read(4) == unchecked((ulong)workerThread)
                && Marshal.ReadInt32(foreign, native.SourceField(20)) == foreignCalls + ownerIterations + workerIterations
                && unchecked((uint)Marshal.ReadInt32(foreign, native.SourceField(16))) == bits
                && Marshal.ReadInt32(current, native.SourceField(20)) == currentCalls
                && native.Group.ReadSourceHealth() == health && native.Group.ReadInitialization().Equals(initialization)
                && native.Observations.Count == observed && native.ClockReads == clockReads && native.Clock.Capture() == clock
                && native.Api.ActiveRoots == roots && il2CppCalls == 0 && workerIl2CppCalls == 0
                && sdk.Journal.Counters == journal.Counters && Private<long>(sdk.Journal, "_sequence") == journalSequence
                && Private<long>(sdk.Journal, "_revision") == journalRevision
                && native.CallbackFailure is null && !native.Router.PhysicalFaultPending,
                "Foreign lower owner/worker originals changed current raw native owner, callbacks, fences, roots, journal, Clock or IL2CPP traffic.");
            Check(ReferenceEquals(rooted.Current, receipt) && native.Group.GetTimeLease.ReadCurrent().Active
                && sdk.Provider.Probe().Supported && Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity") == identity,
                "Foreign lower flood expired the registered rooted descriptor or existing formal provider.");
            captured.VerifyAuthority(); writer.VerifyCurrent();
            Check(captured.Read().Binding == native.Binding && sdk.ClockView.Read().Success && sdk.OtherClock.Read().Success
                && !sdk.Other.Acquire(true).Success,
                "Foreign lower flood invalidated an old source/writer capture or the existing exclusive SDK scopes.");
            // The uncommitted older prepared value is disposed without a native write.
            prepared.Restore();
        }
        Check(native.Api.ActiveRoots == rootsBeforePreparation,
            "Temporary foreign-test preparation did not release its own roots after the unchanged foreign originals.");
        CheckUnityBackingJournalUnchanged(sdk, journalView, journal);
        var positiveCalls = read(0); var positiveStores = read(1); var positivePitchCalls = Marshal.ReadInt32(current, native.SourceField(20));
        var positiveWriter = native.Group.ReadSourceHealth().WriterSequence;
        sdk.Speed(2); // One subsequent formal SDK Execute/transaction, not a fabricated native completion.
        Check(read(0) == positiveCalls + 1 && read(1) == positiveStores + 1
            && Marshal.ReadInt32(current, native.SourceField(20)) == positivePitchCalls + 1
            && native.Group.ReadSourceHealth().WriterSequence == positiveWriter + 1
            && native.Clock.Capture().Rate == 2 && native.Group.ReadSourceHealth() is { Known: true, Unknown: false, InFlight: 0 }
            && sdk.Provider.Probe().Supported && native.Api.ActiveRoots == 14,
            "The old formal SDK could not confirm exactly one actual lower pitch/Clock speed=2 after foreign isolation.");
        CheckUnityBackingSpeedJournal(sdk, journalView, journal, sdk.Lease);
        return new { ownerIterations, workerIterations, totalForeignCalls = ownerIterations + workerIterations, ownerThread, workerThread,
            boundaryCount, boundaryError, raw32 = bits, sameExactClass = true, nativeRawOwnerUnchanged = true,
            managedSourceObserverDelta = 0, currentFencesUnchanged = true, journalUnchangedDuringFlood = true,
            rootsUnchangedDuringFlood = true, temporaryPreparationRootsReleased = true, rootsBeforePreparation, il2CppCalls, workerIl2CppCalls, subsequentFormalSdkRate = 2,
            subsequentFormalSdkNativeCalls = read(0) - positiveCalls, subsequentFormalSdkWriterDelta = native.Group.ReadSourceHealth().WriterSequence - positiveWriter };
    }
}
