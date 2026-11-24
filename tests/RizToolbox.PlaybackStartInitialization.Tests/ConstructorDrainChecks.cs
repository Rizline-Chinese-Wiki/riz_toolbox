using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;

internal static partial class StartChecks
{
    // Fail after native Stage has retained a watch but before the joint
    // GetTime/source activation can complete. The throwing constructor never
    // hands its source object back to Router; this is the otherwise lost owner.
    private static int RunConstructorRootDrain(string[] a, string mode)
    {
        Task<float>? original = null; VoidNative? unblock = null;
        try
        {
            Check(a.Length == 5 && mode is "ctor-root-drain" or "ctor-root-free-fault", "Unknown constructor root-drain mode.");
            Check(Hash(a[1]) == PlaybackSourceGroupTargets.ConstructorFixtureSha256, "Constructor root drain needs its exact fixed fixture.");
            var native = NativeLibrary.Load(Path.GetFullPath(a[1]));
            using var api = new FakeNative(); api.ConfigureInitializer(native); api.ConfigureSharedConstructor(native);
            var targets = PlaybackSourceGroupTargets.ResolveOwnedConstructorFixture(native);
            var state = new GameEventState(); state.Scene("Level"); state.SetAvailability(scenes: true, runs: true, pause: true);
            var started = state.Controller(api.GameplayInformation.ToInt64(), true, false, "constructor-root-drain", api.GameplayController.ToInt64())!;
            state.ObservePause(true);
            var binding = new NativePlaybackBinding(started.RunId, state.SceneGeneration, 1, 1,
                api.GameplayController.ToInt64(), api.GameplayInformation.ToInt64(), api.GameplayProgress.ToInt64(), api.GameplayPlayer.ToInt64(),
                api.GameplaySource.ToInt64(), api.GameplayClip.ToInt64(), targets.GetTime.MethodInfo.ToInt64());
            var origin = new NativePlaybackRateOrigin(binding.RunId, binding.ControllerGeneration, binding.Controller,
                binding.Information, binding.Progress, api.GameplayPause.ToInt64());
            var run = state.CapturePlaybackRun(binding.Information, binding.Controller);
            using var main = new MainThreadDispatcher(); var log = new Logger();
            using var router = new NativePlaybackStockClockRouter(api.GameplayApi, binding, origin, run);
            var broker = new ExactHookBroker(Path.GetFullPath(a[4]), main, log);
            var group = broker.BindPlaybackSourceGroup(targets, Path.GetFullPath(a[2]), Hash(a[2]), Path.GetFullPath(a[3]), Hash(a[3]),
                () => throw new InvalidOperationException("No Clock may be read during failed first activation."),
                router.OnGetTimeFault, router.OnSourceFault, router.ObserveBefore, router.ObserveReturned);
            Check(broker.ReadActualPlaybackDobbyCallCounts() == (13, 13), "Root-drain fixture did not install all exact physical members.");
            var getTime = Marshal.GetDelegateForFunctionPointer<GetTime>(targets.GetTime.Address);
            unblock = Export<VoidNative>(native, "StockFixtureReleaseGetTime");
            var startedOriginal = false;
            api.DuringRootedDspRead = () =>
            {
                api.DuringRootedDspRead = null;
                Check(api.ActiveRoots == 7, "Activation fault did not start from seven independently retained objects.");
                Export<VoidNative>(native, "StockFixtureBlockGetTime")();
                original = Task.Run(() => getTime(api.GameplayPlayer, targets.GetTime.MethodInfo));
                Check(Export<WaitNative>(native, "StockFixtureWaitGetTime")() == 0, "The independent original did not actually block.");
                startedOriginal = true;
            };
            Refuse(() => router.Attach(group), "A busy actual GetTime admitted fresh watch activation.");
            Check(startedOriginal && original is { IsCompleted: false }, "Expected failure did not cross a genuinely in-flight native original.");
            Check(group.ReadActivity() is { Known: true, InFlight: 1 } && api.ActiveRoots == 7
                && group.UnresolvedPlayerRootOwnerCount == 1 && !router.RootsReleased,
                "Failed construction lost the native watch's exact seven-root owner or reported premature release.");
            var workerPump = Task.Run(() => group.PumpCompleted());
            Check(workerPump.Wait(10000) && api.ActiveRoots == 7 && group.UnresolvedPlayerRootOwnerCount == 1,
                "Worker journal cleanup must return while retaining the owner-thread seven-root graph.");
            router.Dispose(); router.PumpCompleted();
            Check(api.ActiveRoots == 7 && group.UnresolvedPlayerRootOwnerCount == 1 && !router.RootsReleased,
                "Stopped Router released the unreturned source before the original's physical exit.");
            unblock(); var value = original!.GetAwaiter().GetResult();
            Check(value == 3 && group.ReadActivity() is { Known: true, InFlight: 0 }, "The retained original did not physically return its real media value.");
            var unknownFree = mode == "ctor-root-free-fault";
            if (unknownFree) api.FailFreeHandle = Private<Dictionary<uint, IntPtr>>(api, "_handles").Single(row => row.Value == api.GameplayClip).Key;
            router.PumpCompleted(); group.PumpCompleted();
            Check(api.ActiveRoots == 0 && group.UnresolvedPlayerRootOwnerCount == (unknownFree ? 1 : 0)
                && router.RootsReleased == !unknownFree,
                "Actual drain lost the unreturned owner, or an uncertain native free was reported as safely completed.");
            if (unknownFree)
            {
                // The fake native export removes the handle and THEN throws.
                // Retrying that consumed ID would double-free a real handle;
                // no later successful no-op proves the earlier call completed.
                api.FailFreeHandle = 0; var attempts = api.FreeHandleAttempts;
                for (var count = 0; count < 3; ++count) { router.PumpCompleted(); group.PumpCompleted(); }
                Check(api.FreeHandleAttempts == attempts && group.UnresolvedPlayerRootOwnerCount == 1 && !router.RootsReleased,
                    "Uncertain native GC free was replayed or silently promoted to proven root release.");
            }
            Check(group.GetTimeLease.ReadCurrent().Authority is null && broker.ReadActualPlaybackDobbyCallCounts() == (13, 13),
                "Failed activation created Clock authority or installed a replacement group.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                retainedDuringOriginal = 7, releaseCallsAfterPhysicalExit = 7,
                unresolvedOwners = group.UnresolvedPlayerRootOwnerCount, unknownFreeRetained = unknownFree,
                actualPrepare = 13, actualCommit = 13, gameExecuted = false, productionAdmission = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() }));
            return 1;
        }
        finally
        {
            if (original is { IsCompleted: false }) { unblock?.Invoke(); original.GetAwaiter().GetResult(); }
        }
    }
}
