using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LowerPitchBoundary(IntPtr self, uint bits, uint at);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LowerPitchConfigure(uint flags, LowerPitchBoundary? callback);

    private static void LowerActualStart(ConstructorContext native)
    {
        var target = native.Targets.ProgressStartMoveNext;
        var enumerator = Export<CreateStart>(native.Module, "StockFixtureCreateStart")(
            native.Api.GameplayProgress, native.Api.GameplayClip);
        native.Api.RegisterUnityEnumerator(enumerator);
        var invoke = Export<InvokeStart>(native.Module, "StockFixtureInvokeStart");
        Check(invoke(target.Address, enumerator, target.MethodInfo, out var first, out var code) == 1
            && code == 0 && first == 1 && native.Router.InitializationPending,
            "The actual post-lower initializer did not retain its real yielded occurrence.");
        Check(invoke(target.Address, enumerator, target.MethodInfo, out var final, out code) == 1
            && code == 0 && final == 0 && native.Group.ReadInitialization() is { Phase: 4, Blocked: 1 },
            "The actual post-lower initializer did not finish its native set_clip/constructor chain.");
        native.Api.AdoptUnityInitializedPlayer();
        native.Api.AdoptUnityClip(native.Api.GameplayClip);
    }

    private static int RunLowerPitchRecovery(string[] a, string mode)
    {
        try
        {
            Check(a.Length == 5 && mode == "lower-pitch-sdk-recover"
                && Hash(a[1]) == NativePlaybackLowerPitchTarget.OwnedWrapperImageSha256,
                "Lower recovery requires the exact independently sealed owned pair.");
            using var native = new ConstructorContext(a);
            var read = Export<LowerFixtureRead>(native.LowerModule, "StockLowerFixtureRead");
            var configure = Export<LowerPitchConfigure>(native.LowerModule, "StockLowerFixtureConfigure");
            var lower = Marshal.GetDelegateForFunctionPointer<LowerSetPitch>(
                native.Targets.SourceTarget(NativePlaybackSourceRole.LowerSetPitch).Address);
            long recorded = 0;
            using (var sdk = new ConstructorSdk(native))
            {
                var view = Private<IPluginContext>(sdk.Lease, "Context")
                    .GetService<IRunModificationService>(RunModificationCapabilityIds.Read).Service;
                Check(view is not null, "The original lower SDK scope lost its actual journal view.");
                var journal = Need(view!.Read());
                sdk.Speed(1.5);
                journal = CheckUnityBackingSpeedJournal(sdk, view, journal, sdk.Lease);
                var oldClock = native.Clock;
                var oldPlayer = native.Api.GameplayPlayer;
                var physical = (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress,
                    native.Group.GetTimeLease.OriginalAddress, native.Group.SetPitchLease.EntryAddress);
                var state = native.Group.ReadSourceHealth();
                var calls = read(0);
                var stores = read(1);
                var beforeCount = 0;
                var boundaryCount = 0;
                var callbackError = (string?)null;
                using (var capture = sdk.Provider.Capture(native.Origin))
                using (var writer = capture.PreparePitchWrite())
                using (var prepared = DirectSdkPrepare(native, sdk))
                {
                    native.AfterDirectAudioBefore = observation =>
                    {
                        if (observation.Role != NativePlaybackSourceRole.LowerSetPitch) return;
                        ++beforeCount;
                        Check(!observation.IsExactLowerPitchChild && observation.IncomingMethod == IntPtr.Zero
                            && observation.Self == native.Api.GameplaySource
                            && BitConverter.SingleToInt32Bits(observation.SingleArgument) == BitConverter.SingleToInt32Bits(1.5f)
                            && read(0) == calls && read(1) == stores
                            && oldClock.Capture().Phase == NativePlaybackPhase.Withdrawn
                            && !native.Group.GetTimeLease.ReadCurrent().Active,
                            "The direct lower Before did not withdraw the exact old Clock before its original entered.");
                        Refuse(capture.VerifyAuthority, "A direct lower Before retained the prior provider capture.");
                        Refuse(writer.VerifyCurrent, "A direct lower Before retained the prior private writer.");
                        Refuse(prepared.Commit, "A prepared predecessor committed before the direct lower store.");
                        prepared.Restore();
                    };
                    LowerPitchBoundary boundary = (self, bits, at) =>
                    {
                        try
                        {
                            ++boundaryCount;
                            Check(self == native.Api.GameplaySource && bits == unchecked((uint)BitConverter.SingleToInt32Bits(1.5f))
                                && at is 1 or 2 && !native.Group.GetTimeLease.ReadCurrent().Active
                                && oldClock.Capture().Phase == NativePlaybackPhase.Withdrawn
                                && read(0) == calls + 1 && read(1) == stores + (at == 2 ? 1ul : 0ul),
                                "The exact lower original changed its raw arguments, once-only store, or revocation ordering.");
                        }
                        catch (Exception error) { callbackError ??= error.ToString(); }
                    };
                    configure(0, boundary);
                    try { lower(native.Api.GameplaySource, 1.5f); }
                    finally { configure(0, null); native.AfterDirectAudioBefore = null; GC.KeepAlive(boundary); }
                    var changed = native.Group.ReadSourceHealth();
                    Check(beforeCount == 1 && boundaryCount == 2 && native.CallbackFailure is null && callbackError is null
                        && changed is { Known: true, Unknown: false, InFlight: 0 }
                        && changed.WriterSequence == state.WriterSequence + 1 && changed.SourceSequence == state.SourceSequence
                        && read(0) == calls + 1 && read(1) == stores + 1,
                        "The direct same-value attempt was not exactly one recognized lower writer: "
                            + native.CallbackFailure + callbackError);
                    CheckUnityBackingWithdrawalViews(native, sdk);
                    CheckUnityBackingJournalUnchanged(sdk, view, journal);
                }
                LowerActualStart(native);
                Check(native.Api.GameplayPlayer != oldPlayer && native.Router.TryCompleteInitialization()
                    && native.Group.ReadInitialization() is { Phase: 7, Blocked: 0 }
                    && !native.Group.GetTimeLease.ReadCurrent().Active,
                    "The actual new Start did not replace the lower-blocked source in dormant phase seven: " + native.Router.LastFailure);
                Refuse(() => native.Router.PreparePausedClock(), "The lower retry bypassed the later actual stock Pause.");
                native.Arm();
                Check(!ReferenceEquals(native.Clock, oldClock)
                    && native.Clock.Capture().Binding.SourceGeneration > oldClock.Capture().Binding.SourceGeneration
                    && sdk.Provider.Probe().Supported && sdk.Module.Tick().Ready,
                    "The original formal provider did not recover a distinct Clock after actual Start and Pause.");
                using (var lease = Need(sdk.Controls.Acquire(true)))
                {
                    Check(lease.ControllerGeneration > sdk.Lease.ControllerGeneration
                        && !sdk.Lease.Execute(sdk.Request(3)).Success && !sdk.Other.Acquire(true).Success,
                        "The predecessor SDK lease revived or successor exclusive ownership was lost.");
                    calls = read(0); stores = read(1); state = native.Group.ReadSourceHealth();
                    var request = new GameplayControlRequest(GameplayControlAction.SetSpeed, lease.ControllerGeneration,
                        lease.ObservedRunId, 2, GameplayControlValueUnit.PlaybackRateMultiplier);
                    var result = Need(lease.Execute(request));
                    Check(result.Modification.TransactionState == GameplayControlTransactionState.Confirmed
                        && result.Modification.ConfirmedValue == 2 && native.Clock.Capture().Rate == 2
                        && BitConverter.Int32BitsToSingle(Marshal.ReadInt32(native.Api.GameplaySource, native.SourceField(16))) == 2
                        && read(0) == calls + 1 && read(1) == stores + 1
                        && native.Group.ReadSourceHealth().WriterSequence == state.WriterSequence + 1
                        && sdk.ClockView.Read().Success && sdk.OtherClock.Read().Success,
                        "The same formal Module failed its successor SDK speed-two wrapper/lower transaction.");
                    journal = CheckUnityBackingSpeedJournal(sdk, view, journal, lease);
                }
                Check(physical == (native.Group.Epoch, native.Group.GetTimeLease.EntryAddress,
                        native.Group.GetTimeLease.OriginalAddress, native.Group.SetPitchLease.EntryAddress)
                    && native.Broker.ReadActualPlaybackDobbyCallCounts() == (21, 21),
                    "The actual lower recovery replaced a physical hook group.");
                recorded = journal.Counters.Recorded;
            }
            native.Router.PumpCompleted(); native.Group.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0 && native.CallbackFailure is null
                && native.Observations.All(observation => observation.IsReleased),
                "The normal lower SDK recovery did not drain all actual roots and physical receipts.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPrepare = 21, actualCommit = 21, retainedRoots = native.Api.ActiveRoots,
                journalRecorded = recorded, actualSameValueBeforeStore = true, actualNewStart = true,
                sdkExecuted = true, sameFormalModule = true, gameExecuted = false, productionAdmission = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() }));
            return 1;
        }
    }
}
