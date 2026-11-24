using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Runtime.Hooks;

internal static partial class StartChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DirectOwnerInfo(IntPtr owner, ref NativePlaybackSourceInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DirectBoundary(IntPtr self, uint role, ulong bits, IntPtr method, uint stage);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ConfigureDirectBoundary(uint flags, DirectBoundary? boundary);

    private static int RunDirectIsolation(string[] a, string mode)
    {
        try
        {
            Check(a.Length == 5 && mode is "direct-foreign" or "direct-worker"
                && Hash(a[1]) == PlaybackSourceGroupTargets.DirectAudioFixtureSha256,
                "Direct isolation requires an exact fixed nineteen-member owned fixture.");
            using var native = new ConstructorContext(a);
            var read = Export<DirectRead>(native.Module, "StockFixtureDirectRead");
            var initial = native.Group.ReadSourceHealth(); var observed = native.Observations.Count;
            if (mode == "direct-foreign")
            {
                var source = native.Api.CreateSharedConstructorSource();
                CheckPlayerControlForeign(native, source);
                Check(source != native.Api.GameplaySource && Marshal.ReadIntPtr(source) == Marshal.ReadIntPtr(native.Api.GameplaySource),
                    "Foreign isolation needs a distinct actual source of the same exact class.");
                var calls = new ulong[6]; var returns = new ulong[6];
                for (var index = 0; index < 6; ++index) { calls[index] = read((uint)index + 13, 0); returns[index] = read((uint)index + 13, 1); }
                void ForeignCall(int index, int iteration)
                {
                    var role = (NativePlaybackSourceRole)(13 + index); var target = native.Targets.SourceTarget(role);
                    var incoming = iteration % 3 == 0 ? target.MethodInfo : iteration % 3 == 1 ? IntPtr.Zero : native.Targets.Pause.MethodInfo;
                    var kind = (uint)(index == 0 ? 8 : index == 1 ? 3 : index < 4 ? 1 : 5);
                    if (native.Invoke(kind, target.Address, source, DirectBits(index), incoming, out var error) != 1 || error != 0)
                        throw new InvalidOperationException("An actual foreign direct source original failed.");
                }
                for (var index = 0; index < 6; ++index) ForeignCall(index, 0);
                var worker = Task.Run(() => { for (var iteration = 0; iteration < 320; ++iteration) for (var index = 0; index < 6; ++index) ForeignCall(index, iteration); });
                Check(worker.Wait(10000) && worker.IsCompletedSuccessfully, "Foreign direct worker/flood did not return.");
                for (var index = 0; index < 6; ++index)
                    Check(read((uint)index + 13, 0) == calls[index] + 321 && read((uint)index + 13, 1) == returns[index] + 321
                        && read((uint)index + 13, 2) == unchecked((ulong)source.ToInt64()) && read((uint)index + 13, 3) == DirectBits(index),
                        "Foreign source lost its unmodified original/arguments.");
                Check(native.Group.ReadSourceHealth() == initial && native.Observations.Count == observed
                    && native.Api.ActiveRoots == 7 && native.ClockReads == 0 && native.CallbackFailure is null
                    && !native.Router.PhysicalFaultPending,
                    "Foreign same-class/MI/worker/flood affected the registered source's journal, fences, roots or transport.");
                native.Router.Dispose(); native.Router.PumpCompleted();
                Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0,
                    "Known foreign source isolation prevented the real owner's root drain.");
            }
            else
            {
                var role = NativePlaybackSourceRole.AudioPlayHelper; var target = native.Targets.SourceTarget(role);
                var channel = native.Group.SourceLease(role);
                var sourceOwner = Private<ExactHookBroker.PlaybackSourceOwner>(channel, "_owner");
                var hub = Private<NativePlaybackStockSourceHub>(sourceOwner, "_hub");
                var owner = Private<IntPtr>(hub, "_owner");
                var helper = NativeLibrary.Load(Path.GetFullPath(a[3]));
                var readOwner = Export<DirectOwnerInfo>(helper, "RtPlaybackSourceReadOwner");
                var configure = Export<ConfigureDirectBoundary>(native.Module, "StockFixtureConfigureDirect");
                var calls = read((uint)role, 0); var returns = read((uint)role, 1);
                // Read the exact retained native owner before the worker starts.
                var ownerBefore = new NativePlaybackSourceInformation();
                Check(readOwner(owner, ref ownerBefore) == 1 && ownerBefore.Owner == owner,
                    "The actual nineteen-member source owner could not be read.");
                var beforeFlags = 0; var afterFlags = 0; var boundaries = 0; string? error = null;
                DirectBoundary boundary = (self, actualRole, bits, method, stage) =>
                {
                    try
                    {
                        var info = new NativePlaybackSourceInformation();
                        if (readOwner(owner, ref info) != 1 || info.Owner != owner || info.OwnerEpoch != ownerBefore.OwnerEpoch
                            || self != native.Api.GameplaySource || actualRole != (uint)role || bits != DirectBits(0)
                            || method != target.MethodInfo || info.InFlight != 1 || (info.Flags & NativePlaybackSourceFlags.Unknown) == 0)
                            throw new InvalidOperationException("Worker direct source was not Unknown before the original's store.");
                        if (stage == 1) Interlocked.Exchange(ref beforeFlags, (int)info.Flags);
                        else if (stage == 2) Interlocked.Exchange(ref afterFlags, (int)info.Flags);
                        else throw new InvalidOperationException("Unknown actual direct boundary.");
                        Interlocked.Increment(ref boundaries);
                    }
                    catch (Exception failure) { Interlocked.CompareExchange(ref error, failure.ToString(), null); }
                };
                configure(0, boundary);
                var worker = Task.Run(() => native.Invoke(8, target.Address, native.Api.GameplaySource, DirectBits(0), target.MethodInfo, out var code) == 1 && code == 0);
                Check(worker.Wait(10000) && worker.Result, "Current direct source worker original did not return.");
                configure(0, null); GC.KeepAlive(boundary);
                var after = new NativePlaybackSourceInformation();
                Check(error is null && boundaries == 2 && (beforeFlags & 4) != 0 && (afterFlags & 4) != 0
                    && readOwner(owner, ref after) == 1 && after.OwnerEpoch == ownerBefore.OwnerEpoch
                    && after.InFlight == 0 && after.Entered == ownerBefore.Entered + 1 && after.Exited == ownerBefore.Exited + 1
                    && after.SchedulingSequence == ownerBefore.SchedulingSequence + 1
                    && read((uint)role, 0) == calls + 1 && read((uint)role, 1) == returns + 1
                    && native.Observations.Count == observed && native.ClockReads == 0,
                    "Worker direct source lost precise before-store Unknown, raw original or physical exit: " + error);
                native.Router.Dispose(); native.Router.PumpCompleted();
                Check(native.Group.ReadActivity() is { Known: false, InFlight: 0 } && !native.Router.RootsReleased && native.Api.ActiveRoots == 7,
                    "Worker direct Unknown released seven-root responsibility after its physical exit.");
            }
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks, actualPrepare = 19, actualCommit = 19,
                retainedRoots = native.Api.ActiveRoots, playerWatchVersion = 2, sdkExecuted = false, gameExecuted = false, productionAdmission = false }));
            return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine(error); Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() })); return 1; }
    }
}
