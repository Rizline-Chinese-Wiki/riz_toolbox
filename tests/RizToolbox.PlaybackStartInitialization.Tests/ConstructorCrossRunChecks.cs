using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    private static int RunConstructorCrossRun(string[] a, string mode)
    {
        try
        {
            Check(a.Length == 5 && mode == "ctor-sdk-crossrun", "Unknown constructor cross-run mode.");
            Check(Hash(a[1]) == PlaybackSourceGroupTargets.ConstructorFixtureSha256, "Cross-run needs the exact constructor fixture.");
            using var native = new ConstructorContext(a);
            using (var sdk = new ConstructorSdk(native))
            {
                sdk.Speed(1.5);
                var oldClock = native.Clock; var oldSession = native.Session;
                var oldPublication = Private<NativePlaybackClockPublication>(oldSession, "_publication");
                var oldPlayer = native.Api.GameplayPlayer; var oldSource = native.Api.GameplaySource;
                var physical = (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress, native.Group.GetTimeLease.OriginalAddress);
                var handoff = native.Router.BeginHandoff(oldSession);
                native.State.Scene("Menu"); native.State.Scene("Level");
                native.State.SetAvailability(scenes: true, runs: true, pause: true);
                native.Api.NextRootedStockGraph();
                native.Api.SetRootedGraphValue("progress.now", 2.8);
                native.Api.SetRootedGraphValue("info.nowTime", 5.6f);
                var started = native.State.Controller(native.Api.GameplayInformation.ToInt64(), true, false,
                    "constructor-successor", native.Api.GameplayController.ToInt64())!;
                native.State.ObservePause(true);
                var next = new NativePlaybackBinding(started.RunId, native.State.SceneGeneration, native.Binding.ControllerGeneration + 1, 1,
                    native.Api.GameplayController.ToInt64(), native.Api.GameplayInformation.ToInt64(), native.Api.GameplayProgress.ToInt64(),
                    native.Api.GameplayPlayer.ToInt64(), native.Api.GameplaySource.ToInt64(), native.Api.GameplayClip.ToInt64(),
                    native.Targets.GetTime.MethodInfo.ToInt64());
                var nextRun = native.State.CapturePlaybackRun(next.Information, next.Controller);
                Check(native.Api.GameplayPlayer != oldPlayer && native.Api.GameplaySource != oldSource && next.RunId != native.Binding.RunId,
                    "Cross-run did not allocate and associate an independent actual native graph.");
                Refuse(() => Copy(handoff).TryAdmit(nextRun, next, native.Api.GameplayPause.ToInt64()), "A copied handoff claimed the successor watch.");
                Check(handoff.TryAdmit(nextRun, next, native.Api.GameplayPause.ToInt64()), "Actual joint cross-run source-open failed: " + native.Router.LastFailure);
                var successor = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
                var actualSuccessor = Private<NativePlaybackActualSource>(successor, "_actual");
                var successorRoots = Private<List<RizToolbox.Runtime.NativeRuntime.RootHandle>>(actualSuccessor, "_roots");
                Check(successorRoots.Select(root => root.Target.ToInt64()).SequenceEqual(new[] { next.Controller, next.Information,
                        next.Progress, native.Api.GameplayPause.ToInt64(), next.Player, next.AudioSource, next.Clip })
                    && Marshal.ReadIntPtr(native.Api.GameplayProgress, 0x38) == native.Api.GameplayPlayer
                    && Marshal.ReadIntPtr(native.Api.GameplayPlayer, 0x10) == native.Api.GameplaySource,
                    "Cross-run source-open did not root and watch the actual successor's exact seven-object graph.");
                Check(oldClock.Capture().Phase == NativePlaybackPhase.Withdrawn && !sdk.Provider.Probe().Supported,
                    "Cross-run source-open revived old Clock or skipped its new Pause baseline.");
                Refuse(() => native.Router.PreparePausedClock(), "Cross-run admitted a Clock without actual successor Pause.");
                Marshal.WriteInt32(native.Api.GameplaySource, 48, 1); native.Stock(NativePlaybackSourceRole.Pause);
                native.Session = native.Router.PreparePausedClock(); Check(native.Session.TryArm(), "Successor constructor watch did not arm.");
                native.Clock = native.Session.Clock;
                native.SynchronizePausedFixtureProgress();
                Check(native.Clock.Capture().Binding.SourceGeneration > native.Binding.SourceGeneration && sdk.Module.Tick().Ready
                    && ReferenceEquals(sdk.Service, Private<GameplayService>(sdk.Module, "_service")),
                    "Cross-run lost the source epoch or replaced the original formal Module/Service.");
                using var lease = Need(sdk.Controls.Acquire(true));
                GameplayControlRequest Request(double rate) => new(GameplayControlAction.SetSpeed, lease.ControllerGeneration,
                    lease.ObservedRunId, rate, GameplayControlValueUnit.PlaybackRateMultiplier);
                Check(lease.ControllerGeneration > sdk.Lease.ControllerGeneration && lease.ObservedRunId == next.RunId
                    && !sdk.Lease.Execute(sdk.Request(2)).Success && !sdk.Other.Acquire(true).Success,
                    "Original SDK views retained the predecessor lease or lost exclusive successor ownership.");
                var result = Need(lease.Execute(Request(2)));
                Check(result.Modification.TransactionState == GameplayControlTransactionState.Confirmed && native.Clock.Capture().Rate == 2
                    && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(native.Api.GameplaySource, 16)) == 2,
                    "Original SDK view did not commit the successor's actual native pitch and final Clock.");

                // Both objects still exist in owned native storage, and both
                // have exactly the same class. Only the successor is watched.
                var health = native.Group.ReadSourceHealth(); var observed = native.Observations.Count;
                var calls = native.Read(0); var returns = native.Read(1);
                var ctor = native.Targets.SourceTarget(NativePlaybackSourceRole.PlayerConstructor);
                Check(native.Ctor(oldPlayer, oldSource, ctor.MethodInfo, out var code) == 1 && code == 0
                    && native.Read(0) == calls + 1 && native.Read(1) == returns + 1
                    && native.Group.ReadSourceHealth() == health && native.Observations.Count == observed
                    && native.Group.GetTimeLease.ReadCurrent().Active && sdk.Provider.Probe().Supported,
                    "A late same-class predecessor constructor advanced the successor's playback journal or source fence.");
                sdk.Lease.Dispose(); oldSession.Withdraw("Late constructor predecessor session cleanup");
                oldPublication.Withdraw("Late constructor predecessor publication cleanup");
                Check(native.Group.GetTimeLease.ReadCurrent().Active && sdk.Provider.Probe().Supported,
                    "Late predecessor cleanup withdrew the actual successor's watch or publication.");
                native.Stock(NativePlaybackSourceRole.Replay, .125f);
                var replay = native.Clock.Capture(); native.Api.SetRootedDsp(replay.DspAnchorSeconds + .5);
                Check(Math.Abs(native.Time(native.Api.GameplayPlayer, native.Targets.GetTime.MethodInfo) - 1f) < .00001f,
                    "Actual cross-run Replay did not consume the successor final Clock and SDK-selected rate.");
                native.Stock(NativePlaybackSourceRole.Pause);

                var beforeCurrent = 0; var oldStored = Marshal.ReadIntPtr(native.Api.GameplayPlayer, 16);
                native.AfterConstructorBefore = observation =>
                {
                    Check(observation.Self == native.Api.GameplayPlayer && observation.ReferenceArgument == oldStored
                        && native.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn
                        && Marshal.ReadIntPtr(native.Api.GameplayPlayer, 16) == oldStored
                        && !lease.Execute(Request(3)).Success && !sdk.Provider.Probe().Supported,
                        "Current successor constructor failed to revoke original SDK authority before its same-value store.");
                    ++beforeCurrent;
                };
                Check(native.Ctor(native.Api.GameplayPlayer, oldStored, ctor.MethodInfo, out code) == 1 && code == 0
                    && beforeCurrent == 1 && native.CallbackFailure is null,
                    "Source-open did not activate precisely the new player's constructor watch.");
                native.AfterConstructorBefore = null;
                Check(physical == (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress, native.Group.GetTimeLease.OriginalAddress)
                    && native.Broker.ReadActualPlaybackDobbyCallCounts() == (13, 13), "Cross-run replaced the retained physical installation.");
            }
            native.Router.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0 && native.Observations.All(value => value.IsReleased),
                "Cross-run constructor drain leaked independent roots or exact observations.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPrepare = 13, actualCommit = 13, sameModuleServiceSdk = true, independentNativeGraphs = 2,
                predecessorConstructorTransparent = true, successorConstructorRevokes = true, retainedRoots = native.Api.ActiveRoots,
                gameExecuted = false, productionAdmission = false })); return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine(error); Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() })); return 1; }
    }
}
