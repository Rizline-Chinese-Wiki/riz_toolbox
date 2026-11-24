using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class StartChecks
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ConstructorWorkerSingleStats
    {
        internal uint Version, Size;
        internal ulong Entered, Exited, InFlight, OriginalCalls, EnterFaults, ExitFaults, SourceFaults, Applied, Declined, Withdrawals;
        internal uint Unknown, Configured, ObserverEnabled, AuthorityActive;
        internal ulong PolicyGeneration;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ConstructorWorkerSourceInfo
    {
        internal uint Version, Size, Kind, Role, Slot, Flags, OwnerThreadId, Reserved;
        internal ulong Owner, OwnerEpoch, Target, Original, Entry, Entered, Exited, InFlight, Writer, Source, Scheduling, Faults;
        internal uint JournalCapacity, ConfiguredSlots;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ConstructorWorkerReadStats(uint slot, ref ConstructorWorkerSingleStats stats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ConstructorWorkerReadSource(IntPtr owner, ref ConstructorWorkerSourceInfo info);
    [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    private static extern uint ConstructorWorkerThreadId();

    private static int RunConstructorWorker(string[] a, string mode)
    {
        try
        {
            Check(a.Length == 5 && mode == "ctor-sdk-worker", "Unknown constructor SDK worker mode.");
            Check(Hash(a[1]) == PlaybackSourceGroupTargets.ConstructorFixtureSha256, "Worker test requires the exact fixed constructor fixture.");
            Check(Marshal.SizeOf<ConstructorWorkerSingleStats>() == 112 && Marshal.SizeOf<ConstructorWorkerSourceInfo>() == 136,
                "Worker native health layouts differ.");
            // Sticky worker Unknown intentionally retains the actual source and
            // its API roots until process exit. Do not dispose FakeNative and
            // turn retained native responsibility into freed fixture storage.
            var native = new ConstructorContext(a);
            var sdk = new ConstructorSdk(native);
            sdk.Speed(1.5);
            var oldClock = native.Clock;
            var player = native.Api.GameplayPlayer; var oldSource = native.Api.GameplaySource;
            var method = native.Targets.SourceTarget(NativePlaybackSourceRole.PlayerConstructor).MethodInfo;
            var rooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
            var actual = Private<NativePlaybackActualSource>(rooted, "_actual");
            var roots = Private<List<NativeRuntime.RootHandle>>(actual, "_roots");
            var getTimeOwner = Private<object>(native.Group.GetTimeLease, "_owner");
            var bridge = Private<NativePlaybackSingleReturnBridge>(getTimeOwner, "_bridge");
            var singleLibrary = Private<IntPtr>(bridge, "_library");
            var slot = Private<uint>(bridge, "_slot");
            var groupOwner = Private<object>(native.Group, "_owner");
            var hub = Private<NativePlaybackStockSourceHub>(groupOwner, "_hub");
            var sourceOwner = hub.NativeOwner;
            var readSingle = Export<ConstructorWorkerReadStats>(singleLibrary, "RizSingleReturnPrototype_GetStats");
            var readSource = Export<ConstructorWorkerReadSource>(hub.NativeLibraryHandle, "RtPlaybackSourceReadOwner");
            var initialSingle = new ConstructorWorkerSingleStats { Version = 1, Size = 112 };
            var initialSource = new ConstructorWorkerSourceInfo();
            Check(readSingle(slot, ref initialSingle) == 0 && initialSingle.AuthorityActive == 1
                && readSource(sourceOwner, ref initialSource) == 1 && initialSource.Flags == 1 && initialSource.InFlight == 0,
                "Worker counterexample did not start with an armed actual policy and known source.");
            var calls = native.Read(0); var returns = native.Read(1);
            var observations = native.Observations.Count; var clockReads = native.ClockReads;
            var ownerThread = ConstructorWorkerThreadId(); var pitchCalls = Marshal.ReadInt32(oldSource, 20);
            const float rawGetTime = 17.25f;
            Check((float)oldClock.Capture().MediaAnchorSeconds != rawGetTime, "Worker raw GetTime must differ from the old Clock anchor.");
            // Set the owned fixture's source.Time on its owner before entering
            // the worker. Its actual native GetTime reads this POD double.
            Marshal.Copy(new[] { (double)rawGetTime }, 0, IntPtr.Add(oldSource, 24), 1);
            var detouredGetTime = native.Time; var getTimeMethod = native.Targets.GetTime.MethodInfo;
            var readConstructor = native.Read;
            var boundaryCount = 0; var boundaryMask = 0; var callbackError = 0; var beforeValid = 0; var afterValid = 0;
            long beforeActive = -1, beforeSourceFlags = -1, boundaryThread = -1;
            var getTimeCalls = 0; var getTimeBits = 0; var getTimeStatsValid = 0;
            long beforeGetTimeApplied = -1, afterGetTimeApplied = -1, beforeGetTimeOriginalCalls = -1, afterGetTimeOriginalCalls = -1;
            ConstructorBoundary boundary = (self, reference, incoming, at) =>
            {
                // Only native POD reads and atomic result recording occur on
                // this worker: no Router, Group, NativeRuntime or SDK access.
                try
                {
                    Interlocked.Increment(ref boundaryCount);
                    if (at is 1 or 2) Interlocked.Or(ref boundaryMask, 1 << (int)at);
                    var single = new ConstructorWorkerSingleStats { Version = 1, Size = 112 };
                    var source = new ConstructorWorkerSourceInfo();
                    var singleStatus = readSingle(slot, ref single);
                    var sourceStatus = readSource(sourceOwner, ref source);
                    var thread = readConstructor(7);
                    var valid = self == player && reference == oldSource && incoming == method
                        && Marshal.ReadIntPtr(self, 16) == oldSource && singleStatus == 0
                        && sourceStatus == 1 && (source.Flags & 4) != 0 && (source.Flags & 1) == 0
                        && source.InFlight == 1 && source.Entered == initialSource.Entered + 1 && source.Exited == initialSource.Exited
                        && source.Source == initialSource.Source + 1 && source.Writer == initialSource.Writer
                        && source.Scheduling == initialSource.Scheduling && source.Faults > initialSource.Faults
                        && readConstructor(0) == calls + 1 && readConstructor(1) == returns + (at == 1 ? 0ul : 1ul)
                        && thread != ownerThread && thread == ConstructorWorkerThreadId();
                    if (at == 1)
                    {
                        Interlocked.Exchange(ref beforeActive, single.AuthorityActive);
                        Interlocked.Exchange(ref beforeSourceFlags, source.Flags);
                        Interlocked.Exchange(ref boundaryThread, unchecked((long)thread));
                        Interlocked.Exchange(ref beforeValid, valid ? 1 : -1);
                        // Exercise the actual patched target with its exact MI
                        // while constructor Unknown precedes the original store.
                        // Raw authorityActive is diagnostic; effective freshness
                        // must decline before the managed Clock reader is called.
                        var result = detouredGetTime(player, getTimeMethod);
                        var after = new ConstructorWorkerSingleStats { Version = 1, Size = 112 };
                        var afterStatus = readSingle(slot, ref after);
                        Interlocked.Exchange(ref getTimeBits, BitConverter.SingleToInt32Bits(result));
                        Interlocked.Exchange(ref beforeGetTimeApplied, unchecked((long)single.Applied));
                        Interlocked.Exchange(ref afterGetTimeApplied, unchecked((long)after.Applied));
                        Interlocked.Exchange(ref beforeGetTimeOriginalCalls, unchecked((long)single.OriginalCalls));
                        Interlocked.Exchange(ref afterGetTimeOriginalCalls, unchecked((long)after.OriginalCalls));
                        Interlocked.Exchange(ref getTimeStatsValid, singleStatus == 0 && afterStatus == 0 ? 1 : -1);
                        Interlocked.Increment(ref getTimeCalls);
                    }
                    else if (at == 2) Interlocked.Exchange(ref afterValid, valid ? 1 : -1);
                }
                catch (Exception) { Interlocked.Exchange(ref callbackError, 1); }
            };
            native.Configure(0, boundary);
            var worker = Task.Run(() =>
            {
                var status = native.Ctor(player, oldSource, method, out var code);
                return (status, code);
            });
            Check(worker.Wait(10000) && worker.Result is { status: 1, code: 0 }, "Actual worker constructor original failed or timed out.");
            native.Configure(0, null); GC.KeepAlive(boundary);
            Check(callbackError == 0 && boundaryCount == 2 && boundaryMask == 6 && beforeValid == 1 && afterValid == 1,
                $"Worker native source did not become Unknown before original store: rawActive={beforeActive}, flags={beforeSourceFlags}, boundary={beforeValid}/{afterValid}, callbacks={boundaryCount}.");
            var actualGetTime = BitConverter.Int32BitsToSingle(getTimeBits);
            var appliedDelta = afterGetTimeApplied - beforeGetTimeApplied;
            var originalCallsDelta = afterGetTimeOriginalCalls - beforeGetTimeOriginalCalls;
            var clockReadsDelta = native.ClockReads - clockReads;
            Check(getTimeCalls == 1 && getTimeStatsValid == 1 && actualGetTime == rawGetTime
                && appliedDelta == 0 && originalCallsDelta == 1 && clockReadsDelta == 0,
                $"Worker Unknown crossed the effective GetTime gate: rawActive={beforeActive}, returned={actualGetTime:R}, expected={rawGetTime:R}, appliedDelta={appliedDelta}, originalCallsDelta={originalCallsDelta}, clockReadsDelta={clockReadsDelta}, calls={getTimeCalls}, stats={getTimeStatsValid}.");
            Check(native.Read(0) == calls + 1 && native.Read(1) == returns + 1 && native.Read(5) == unchecked((ulong)oldSource.ToInt64())
                && native.Read(6) == unchecked((ulong)oldSource.ToInt64()) && native.Read(7) == unchecked((ulong)boundaryThread)
                && Marshal.ReadIntPtr(player, 16) == oldSource && native.ConstructorBefores == 0
                && native.Observations.Count == observations && native.ClockReads == clockReads && native.CallbackFailure is null,
                "Relevant worker changed original arguments/store, entered managed source observers or consumed the Clock reader.");
            var afterSource = new ConstructorWorkerSourceInfo();
            Check(readSource(sourceOwner, ref afterSource) == 1 && (afterSource.Flags & 4) != 0 && (afterSource.Flags & 1) == 0
                && afterSource.Entered == initialSource.Entered + 1 && afterSource.Exited == initialSource.Exited + 1
                && afterSource.InFlight == 0 && afterSource.Source == initialSource.Source + 1,
                "Relevant worker lost its native physical exit or sticky Unknown responsibility.");
            native.Router.PumpCompleted(); sdk.Provider.PumpCompleted();
            Check(!sdk.Provider.Probe().Supported, "Owner-thread provider accepted the worker-faulted source.");
            native.Router.PumpCompleted();
            Check(oldClock.Capture().Phase == NativePlaybackPhase.Withdrawn && !sdk.Lease.Execute(sdk.Request(2)).Success
                && Marshal.ReadInt32(oldSource, 20) == pitchCalls,
                "Owner-thread pump retained old Clock/lease authority or wrote native pitch after worker Unknown.");
            Check(!rooted.RootsReleased && !native.Router.RootsReleased && roots.Count == 7 && native.Api.ActiveRoots >= 7,
                "Worker Unknown was mistaken for a safe actual root drain.");
            sdk.Dispose(); native.Router.PumpCompleted();
            Check(!native.Router.RootsReleased && roots.Count == 7 && native.Api.ActiveRoots >= 7,
                "SDK disposal dropped source roots while the actual worker transport remained Unknown.");
            Check(native.UniqueFinalClock && native.Broker.ReadActualPlaybackDobbyCallCounts() == (13, 13),
                "Worker test replaced the final Clock or actual Dobby group.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks, actualPrepare = 13, actualCommit = 13,
                workerThread = boundaryThread, ownerThread, beforeStorePolicyActive = beforeActive, beforeStoreSourceFlags = beforeSourceFlags,
                beforeStoreGetTime = actualGetTime, expectedRawGetTime = rawGetTime, appliedDelta, originalCallsDelta, clockReadsDelta,
                workerBoundaryCallbacks = boundaryCount, constructorBefores = native.ConstructorBefores, retainedRoots = native.Api.ActiveRoots,
                sourceUnknown = true, sourcePhysicalExited = true, ownerThreadProviderSupported = false, gameExecuted = false, productionAdmission = false }));
            GC.KeepAlive(native); return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine(error); Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() })); return 1; }
    }
}
