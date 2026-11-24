using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    private static int RunDirectSdkIsolation(string[] a, string mode)
    {
        try
        {
            Check(a.Length == 5 && (mode is "direct-sdk-foreign" or "direct-sdk-worker"
                || mode.StartsWith("direct-sdk-worker-", StringComparison.Ordinal))
                && Hash(a[1]) == PlaybackSourceGroupTargets.DirectAudioFixtureSha256,
                "Formal direct isolation requires the exact independently fixed nineteen-member fixture.");
            using var native = new ConstructorContext(a);
            Check(native.Targets.HasDirectAudioControls && native.PhysicalMemberCount == 19
                && native.Broker.ReadActualPlaybackDobbyCallCounts() == (19, 19)
                && Marshal.SizeOf<NativePlaybackSourceFrame>() == 168
                && Marshal.SizeOf<NativePlaybackSourceInformation>() == 136
                && Marshal.SizeOf<ConstructorWorkerSingleStats>() == 112,
                "Formal direct isolation did not install the exact v4 nineteen-member physical group.");
            var read = Export<DirectRead>(native.Module, "StockFixtureDirectRead");
            object evidence;
            using (var sdk = new ConstructorSdk(native))
            {
                sdk.Speed(1.5);
                evidence = mode == "direct-sdk-foreign" ? DirectSdkForeign(native, sdk, read)
                    : DirectSdkWorker(native, sdk, read, mode == "direct-sdk-worker" ? 0 : int.Parse(mode[(mode.LastIndexOf('-') + 1)..]));
            }
            native.Router.PumpCompleted();
            var unknown = mode != "direct-sdk-foreign";
            Check(native.CallbackFailure is null && native.UniqueFinalClock && native.ClockReads > 0
                && native.Broker.ReadActualPlaybackDobbyCallCounts() == (19, 19),
                "Formal direct isolation changed its actual hooks, final Clock or managed observer health: " + native.CallbackFailure);
            Check(unknown
                ? !native.Router.RootsReleased && native.Api.ActiveRoots == 7 && native.Group.ReadActivity() is { Known: false, InFlight: 0 }
                : native.Router.RootsReleased && native.Api.ActiveRoots == 0 && native.Observations.All(value => value.IsReleased),
                "Formal direct isolation lost the exact seven-root Unknown responsibility or the known foreign-source drain.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPrepare = 19, actualCommit = 19, physicalSources = 18, nativeSourceVersion = 4,
                playerWatchVersion = 2, sdkExecuted = true, independentPluginScopes = 2,
                playerControlForeignCovered = mode == "direct-sdk-foreign",
                progressControlForeignCovered = mode == "direct-sdk-foreign",
                retainedRoots = native.Api.ActiveRoots, sourceUnknown = unknown, evidence,
                gameExecuted = false, productionAdmission = false, fullBCompleted = false, seekCompleted = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() }));
            return 1;
        }
    }

    private static GameplayPreparedControl DirectSdkPrepare(ConstructorContext native, ConstructorSdk sdk)
        => NativePlaybackRateProviderAdoption.Prepare(sdk.Provider, native.Origin,
            new(GameplayControlAction.SetSpeed, native.Binding.ControllerGeneration, native.Binding.RunId, 2, GameplayControlValueUnit.PlaybackRateMultiplier),
            () => native.Run.VerifyCurrent(native.Binding));

    private static object DirectSdkForeign(ConstructorContext native, ConstructorSdk sdk, DirectRead read)
    {
        var source = native.Api.CreateSharedConstructorSource(); var current = native.Api.GameplaySource;
        Check(source != current && Marshal.ReadIntPtr(source) == Marshal.ReadIntPtr(current),
            "Formal foreign isolation needs a distinct actual AudioSource of the same exact class.");
        // The same formal SDK-positive case covers the older five leaf entries.
        // The first source snapshot/writer below must remain usable afterward.
        CheckLegacyForeign(native, source);
        CheckPlayerControlForeign(native, source);
        CheckActualProgressForeign(native.Api, native.Module, native.Targets, native.Group,
            () => native.Observations.Count, () => native.ClockReads);
        var rooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
        var baselineReceipt = rooted.Current;
        var sourceIdentity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
        Check(sourceIdentity is { NativeDescriptor: not null }, "Formal foreign isolation started without the exact SDK native source descriptor.");
        var calls = new ulong[6]; var returns = new ulong[6];
        for (var index = 0; index < 6; ++index) { calls[index] = read((uint)index + 13, 0); returns[index] = read((uint)index + 13, 1); }
        var currentCalls = Marshal.ReadInt32(current, DirectCallsOffset);
        var foreignCalls = Marshal.ReadInt32(source, DirectCallsOffset);
        const int ownerIterations = 3, workerIterations = 320;
        using (var captured = sdk.Provider.Capture(native.Origin))
        using (var writer = captured.PreparePitchWrite())
        using (var prepared = DirectSdkPrepare(native, sdk))
        {
            var health = native.Group.ReadSourceHealth(); var initialization = native.Group.ReadInitialization();
            var observed = native.Observations.Count; var clock = native.Clock.Capture(); var clockReads = native.ClockReads;
            var journal = sdk.Journal.Counters; var journalSequence = Private<long>(sdk.Journal, "_sequence");
            var journalRevision = Private<long>(sdk.Journal, "_revision"); var activeRoots = native.Api.ActiveRoots;
            var pitchCalls = Marshal.ReadInt32(current, 20);
            void ForeignCall(int index, int iteration)
            {
                var role = (NativePlaybackSourceRole)(13 + index); var target = native.Targets.SourceTarget(role);
                var incoming = iteration % 3 == 0 ? target.MethodInfo : iteration % 3 == 1 ? IntPtr.Zero : native.Targets.Pause.MethodInfo;
                var kind = (uint)(index == 0 ? 8 : index == 1 ? 3 : index < 4 ? 1 : 5);
                if (native.Invoke(kind, target.Address, source, DirectBits(index), incoming, out var error) != 1 || error != 0
                    || read((uint)role, 2) != unchecked((ulong)source.ToInt64()) || read((uint)role, 3) != DirectBits(index)
                    || read((uint)role, 4) != unchecked((ulong)incoming.ToInt64()))
                    throw new InvalidOperationException("An actual foreign direct AudioSource original changed raw self/argument/MethodInfo.");
            }
            for (var iteration = 0; iteration < ownerIterations; ++iteration)
                for (var index = 0; index < 6; ++index) ForeignCall(index, iteration);
            var worker = Task.Run(() =>
            {
                for (var iteration = 0; iteration < workerIterations; ++iteration)
                    for (var index = 0; index < 6; ++index) ForeignCall(index, iteration);
            });
            Check(worker.Wait(10000) && worker.IsCompletedSuccessfully, "Formal foreign-source worker/flood did not return transparently.");
            for (var index = 0; index < 6; ++index)
                Check(read((uint)index + 13, 0) == calls[index] + ownerIterations + workerIterations
                    && read((uint)index + 13, 1) == returns[index] + ownerIterations + workerIterations
                    && read((uint)index + 13, 2) == unchecked((ulong)source.ToInt64())
                    && read((uint)index + 13, 3) == DirectBits(index) && read((uint)index + 13, 4) == 0,
                    "Formal foreign-source calls lost exact raw originals, native counts or final null MI.");
            Check(native.Group.ReadSourceHealth() == health && native.Group.ReadInitialization().Equals(initialization)
                && native.Observations.Count == observed && native.Clock.Capture() == clock && native.ClockReads == clockReads
                && sdk.Journal.Counters == journal && Private<long>(sdk.Journal, "_sequence") == journalSequence
                && Private<long>(sdk.Journal, "_revision") == journalRevision
                && native.Api.ActiveRoots == activeRoots && Marshal.ReadInt32(current, DirectCallsOffset) == currentCalls
                && Marshal.ReadInt32(source, DirectCallsOffset) == foreignCalls + 6 * (ownerIterations + workerIterations)
                && Marshal.ReadInt32(current, 20) == pitchCalls && native.CallbackFailure is null && !native.Router.PhysicalFaultPending,
                "Foreign same-class correct/null/wrong-MI worker/flood altered the current journal, fences, roots, Clock or native source.");
            Check(ReferenceEquals(rooted.Current, baselineReceipt) && native.Group.GetTimeLease.ReadCurrent().Active
                && sdk.Provider.Probe().Supported && Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity") == sourceIdentity,
                "Foreign direct attempts withdrew the registered rooted source, GetTime policy or formal provider.");
            captured.VerifyAuthority(); writer.VerifyCurrent();
            Check(captured.Read().Binding == native.Binding && sdk.ClockView.Read().Success && sdk.OtherClock.Read().Success
                && !sdk.Other.Acquire(true).Success,
                "Foreign direct attempts expired an independent source/writer capture or the existing exclusive SDK scopes.");
            prepared.Commit(); var confirmed = prepared.Confirm();
            Check(confirmed is { Observed: true, Value: 2, Unit: GameplayControlValueUnit.PlaybackRateMultiplier }
                && native.Clock.Capture().Rate == 2
                && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(current, 16)) == 2
                && Marshal.ReadInt32(current, 20) == pitchCalls + 1
                && native.Group.ReadSourceHealth().WriterSequence == health.WriterSequence + 1,
                "Foreign direct attempts invalidated the previously prepared actual audio/Clock rate transaction.");
        }
        sdk.Speed(1.25);
        Check(native.Group.ReadSourceHealth() is { Known: true, InFlight: 0, Unknown: false }
            && sdk.Provider.Probe().Supported && native.Api.ActiveRoots == 14,
            "The pre-existing formal SDK could not continue actual SetSpeed after foreign direct isolation.");
        return new { roles = 6, ownerIterations, workerIterations, callsPerRole = ownerIterations + workerIterations,
            methodInfos = new[] { "correct", "null", "wrong" }, sameExactClass = true, currentJournalUnchanged = true,
            currentFencesUnchanged = true, oldSourceAndWriterSurvived = true, preparedRateConfirmed = 2,
            subsequentSdkRateConfirmed = 1.25 };
    }

    private static object DirectSdkWorker(ConstructorContext native, ConstructorSdk sdk, DirectRead read, int index)
    {
        Check(index is >= 0 and < 6, "Unknown formal direct worker role.");
        var role = (NativePlaybackSourceRole)(13 + index); var target = native.Targets.SourceTarget(role);
        var player = native.Api.GameplayPlayer; var source = native.Api.GameplaySource; var oldClock = native.Clock;
        var rooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
        var actual = Private<NativePlaybackActualSource>(rooted, "_actual");
        var roots = Private<List<NativeRuntime.RootHandle>>(actual, "_roots");
        var initialIdentity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
        Check(initialIdentity is { NativeDescriptor: not null } && roots.Count == 7,
            "Formal direct worker started without the original exact SDK descriptor and seven actual source roots.");
        object evidence;
        using (var captured = sdk.Provider.Capture(native.Origin))
        using (var writer = captured.PreparePitchWrite())
        using (var prepared = DirectSdkPrepare(native, sdk))
        {
            var getTimeOwner = Private<object>(native.Group.GetTimeLease, "_owner");
            var bridge = Private<NativePlaybackSingleReturnBridge>(getTimeOwner, "_bridge");
            var singleLibrary = Private<IntPtr>(bridge, "_library"); var slot = Private<uint>(bridge, "_slot");
            var groupOwner = Private<object>(native.Group, "_owner");
            var hub = Private<NativePlaybackStockSourceHub>(groupOwner, "_hub");
            var sourceOwner = hub.NativeOwner;
            var readSource = Export<DirectOwnerInfo>(hub.NativeLibraryHandle, "RtPlaybackSourceReadOwner");
            var readSingle = Export<ConstructorWorkerReadStats>(singleLibrary, "RizSingleReturnPrototype_GetStats");
            var configure = Export<ConfigureDirectBoundary>(native.Module, "StockFixtureConfigureDirect");
            var initialSource = new NativePlaybackSourceInformation();
            var initialSingle = new ConstructorWorkerSingleStats { Version = 1, Size = 112 };
            Check(readSource(sourceOwner, ref initialSource) == 1 && initialSource.Owner == sourceOwner
                && initialSource.Flags == NativePlaybackSourceFlags.Armed && initialSource.ConfiguredSlots == 18
                && initialSource.InFlight == 0 && readSingle(slot, ref initialSingle) == 0 && initialSingle.AuthorityActive == 1,
                "Formal direct worker did not start with the actual known v4 source and armed GetTime policy.");
            var calls = read((uint)role, 0); var returns = read((uint)role, 1);
            var bodyCalls = Marshal.ReadInt32(source, DirectCallsOffset); var pitchCalls = Marshal.ReadInt32(source, 20);
            var observations = native.Observations.Count; var clockReads = native.ClockReads;
            var ownerThread = ConstructorWorkerThreadId(); var initialActiveRoots = native.Api.ActiveRoots;
            const float rawGetTime = 17.25f;
            Check(oldClock.Capture().MediaAnchorSeconds != rawGetTime,
                "Formal worker raw GetTime must differ from the actual old Clock anchor.");
            // Owner-side fixture setup only. The worker's actual original reads this POD value.
            Marshal.Copy(new[] { (double)rawGetTime }, 0, IntPtr.Add(source, 24), 1);
            var detouredGetTime = native.Time; var getTimeMethod = native.Targets.GetTime.MethodInfo;
            var boundaryCount = 0; var boundaryMask = 0; var callbackError = 0; var beforeValid = 0; var afterValid = 0;
            long boundaryThread = -1, beforeActive = -1, beforeFlags = -1;
            var getTimeCalls = 0; var getTimeBits = 0; var getTimeStatsValid = 0;
            long beforeApplied = -1, afterApplied = -1, beforeOriginal = -1, afterOriginal = -1;
            DirectBoundary boundary = (self, actualRole, bits, incoming, stage) =>
            {
                // No owner-only Router, Group, NativeRuntime, SDK, source, writer
                // or prepared-control API is accessed here. Only native POD reads,
                // the actual patched GetTime target and atomic result recording.
                try
                {
                    Interlocked.Increment(ref boundaryCount);
                    if (stage is 1 or 2) Interlocked.Or(ref boundaryMask, 1 << (int)stage);
                    var info = new NativePlaybackSourceInformation();
                    var single = new ConstructorWorkerSingleStats { Version = 1, Size = 112 };
                    var sourceStatus = readSource(sourceOwner, ref info); var singleStatus = readSingle(slot, ref single);
                    var thread = read((uint)role, 5);
                    var valid = self == source && actualRole == (uint)role && bits == DirectBits(index) && incoming == target.MethodInfo
                        && sourceStatus == 1 && info.Owner == sourceOwner && info.OwnerEpoch == initialSource.OwnerEpoch
                        && info.OwnerThreadId == ownerThread && (info.Flags & NativePlaybackSourceFlags.Unknown) != 0
                        && (info.Flags & NativePlaybackSourceFlags.Armed) == 0 && info.InFlight == 1
                        && info.Entered == initialSource.Entered + 1 && info.Exited == initialSource.Exited
                        && info.WriterSequence == initialSource.WriterSequence && info.SourceSequence == initialSource.SourceSequence
                        && info.SchedulingSequence == initialSource.SchedulingSequence + 1 && info.Faults > initialSource.Faults
                        && read((uint)role, 0) == calls + 1 && read((uint)role, 1) == returns + (stage == 1 ? 0ul : 1ul)
                        && Marshal.ReadInt32(source, DirectCallsOffset) == bodyCalls + (stage == 1 ? 0 : 1)
                        && thread != ownerThread && thread == ConstructorWorkerThreadId() && singleStatus == 0;
                    if (stage == 1)
                    {
                        Interlocked.Exchange(ref beforeValid, valid ? 1 : -1);
                        Interlocked.Exchange(ref beforeActive, single.AuthorityActive);
                        Interlocked.Exchange(ref beforeFlags, (long)info.Flags);
                        Interlocked.Exchange(ref boundaryThread, unchecked((long)thread));
                        var result = detouredGetTime(player, getTimeMethod);
                        var after = new ConstructorWorkerSingleStats { Version = 1, Size = 112 };
                        var afterStatus = readSingle(slot, ref after);
                        Interlocked.Exchange(ref getTimeBits, BitConverter.SingleToInt32Bits(result));
                        Interlocked.Exchange(ref beforeApplied, unchecked((long)single.Applied));
                        Interlocked.Exchange(ref afterApplied, unchecked((long)after.Applied));
                        Interlocked.Exchange(ref beforeOriginal, unchecked((long)single.OriginalCalls));
                        Interlocked.Exchange(ref afterOriginal, unchecked((long)after.OriginalCalls));
                        Interlocked.Exchange(ref getTimeStatsValid, singleStatus == 0 && afterStatus == 0 ? 1 : -1);
                        Interlocked.Increment(ref getTimeCalls);
                    }
                    else if (stage == 2) Interlocked.Exchange(ref afterValid, valid ? 1 : -1);
                }
                catch (Exception) { Interlocked.Exchange(ref callbackError, 1); }
            };
            configure(0, boundary);
            var kind = (uint)(index == 0 ? 8 : index == 1 ? 3 : index < 4 ? 1 : 5);
            var worker = Task.Run(() =>
            {
                var status = native.Invoke(kind, target.Address, source, DirectBits(index), target.MethodInfo, out var code);
                return (status, code);
            });
            Check(worker.Wait(10000) && worker.Result is { status: 1, code: 0 },
                "Actual current-source worker direct original failed or timed out.");
            configure(0, null); GC.KeepAlive(boundary);
            Check(callbackError == 0 && boundaryCount == 2 && boundaryMask == 6 && beforeValid == 1 && afterValid == 1,
                $"Direct worker did not become native Unknown before the original store: flags={beforeFlags}, valid={beforeValid}/{afterValid}, callbacks={boundaryCount}, callbackError={callbackError}.");
            var actualGetTime = BitConverter.Int32BitsToSingle(getTimeBits);
            var appliedDelta = afterApplied - beforeApplied; var originalCallsDelta = afterOriginal - beforeOriginal;
            var clockReadsDelta = native.ClockReads - clockReads;
            Check(getTimeCalls == 1 && getTimeStatsValid == 1 && actualGetTime == rawGetTime
                && appliedDelta == 0 && originalCallsDelta == 1 && clockReadsDelta == 0,
                $"Direct worker Unknown crossed the actual GetTime policy gate: rawActive={beforeActive}, returned={actualGetTime:R}, expected={rawGetTime:R}, appliedDelta={appliedDelta}, originalCallsDelta={originalCallsDelta}, clockReadsDelta={clockReadsDelta}.");
            var afterSource = new NativePlaybackSourceInformation();
            Check(readSource(sourceOwner, ref afterSource) == 1 && afterSource.Owner == sourceOwner
                && afterSource.OwnerEpoch == initialSource.OwnerEpoch && (afterSource.Flags & NativePlaybackSourceFlags.Unknown) != 0
                && (afterSource.Flags & NativePlaybackSourceFlags.Armed) == 0 && afterSource.InFlight == 0
                && afterSource.Entered == initialSource.Entered + 1 && afterSource.Exited == initialSource.Exited + 1
                && afterSource.WriterSequence == initialSource.WriterSequence && afterSource.SourceSequence == initialSource.SourceSequence
                && afterSource.SchedulingSequence == initialSource.SchedulingSequence + 1
                && read((uint)role, 0) == calls + 1 && read((uint)role, 1) == returns + 1
                && read((uint)role, 2) == unchecked((ulong)source.ToInt64()) && read((uint)role, 3) == DirectBits(index)
                && read((uint)role, 4) == unchecked((ulong)target.MethodInfo.ToInt64())
                && read((uint)role, 5) == unchecked((ulong)boundaryThread)
                && Marshal.ReadInt32(source, DirectCallsOffset) == bodyCalls + 1
                && native.Observations.Count == observations && native.ClockReads == clockReads && native.CallbackFailure is null
                && native.Api.ActiveRoots == initialActiveRoots,
                "Direct worker lost raw ABI/original/physical exit, entered managed observers or released roots on the worker.");
            // Formal authority assertions occur only now, on the original owner.
            native.Router.PumpCompleted(); sdk.Provider.PumpCompleted(); native.Router.PumpCompleted();
            Check(oldClock.Capture().Phase == NativePlaybackPhase.Withdrawn && !sdk.Provider.Probe().Supported
                && !sdk.Lease.Execute(sdk.Request(3)).Success,
                "Owner pump retained the worker-faulted final Clock, provider or original formal SDK lease.");
            Refuse(() => { _ = rooted.Current; }, "Owner pump retained the old rooted source after direct worker Unknown.");
            Refuse(captured.VerifyAuthority, "Owner pump retained the captured old source after direct worker Unknown.");
            Refuse(writer.VerifyCurrent, "Owner pump retained the continuous old writer after direct worker Unknown.");
            Refuse(prepared.Commit, "Owner pump committed a prepared old rate after direct worker Unknown.");
            prepared.Restore();
            var firstClock = Need(sdk.ClockView.Read()); var secondClock = Need(sdk.OtherClock.Read());
            var freshIdentity = Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity");
            Check(firstClock.ControllerGeneration > sdk.Lease.ControllerGeneration
                && secondClock.ControllerGeneration > sdk.Lease.ControllerGeneration
                && freshIdentity != initialIdentity && freshIdentity is { NativeDescriptor: null },
                "A formal SDK clock view reacquired the retired native source descriptor after owner pump.");
            sdk.Lease.Dispose();
            var acquired = sdk.Other.Acquire(true); var otherAcquired = acquired.Success;
            if (acquired.Success)
            {
                using var otherLease = acquired.Value!;
                var request = new GameplayControlRequest(GameplayControlAction.SetSpeed, otherLease.ControllerGeneration,
                    otherLease.ObservedRunId, 2, GameplayControlValueUnit.PlaybackRateMultiplier);
                Check(otherLease.ControllerGeneration > sdk.Lease.ControllerGeneration && !otherLease.Execute(request).Success,
                    "The other formal SDK scope reacquired old-source speed authority after direct worker Unknown.");
            }
            sdk.SampleControls();
            var otherState = Need(sdk.Other.GetState());
            Check(otherState.Operations.Single(operation => operation.Action == GameplayControlAction.SetSpeed).Supported == false
                && Private<GameplaySourceIdentity?>(sdk.Service, "_sourceIdentity") is { NativeDescriptor: null }
                && Marshal.ReadInt32(source, 20) == pitchCalls
                && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(source, 16)) == 1.5f
                && !rooted.RootsReleased && !native.Router.RootsReleased && roots.Count == 7 && native.Api.ActiveRoots >= 7,
                "Owner-thread SDK/prepared restore wrote old native pitch, recovered speed support or dropped the seven unknown source roots.");
            evidence = new { index, role = role.ToString(), ownerThread, workerThread = boundaryThread,
                boundaryCount, beforeStorePolicyActive = beforeActive, beforeStoreSourceFlags = beforeFlags,
                beforeStoreGetTime = actualGetTime, expectedRawGetTime = rawGetTime, appliedDelta, originalCallsDelta,
                clockReadsDelta, nativeSourcePhysicalExited = true, managedObserverSuppressed = true,
                ownerThreadProviderSupported = false, oldSourceWriterAndPreparedRevoked = true,
                otherSdkNativeDescriptor = false, otherSdkRawControlAcquired = otherAcquired, otherSdkSpeedSupported = false };
        }
        Check(!rooted.RootsReleased && roots.Count == 7 && native.Api.ActiveRoots == 14,
            "Disposing independent prepared/source captures lost or multiplied the seven unknown owner roots.");
        return evidence;
    }
}
