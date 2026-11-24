using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;

internal static partial class StartChecks
{
    private static int RunProgressResourceDormant(string[] arguments, string mode)
    {
        try
        {
            Check(arguments.Length == 5 && mode is "resource-dormant-setclip" or "resource-dormant-progressclip"
                && Hash(arguments[1]) == PlaybackSourceGroupTargets.ProgressResourceFixtureSha256,
                "Ordinary dormant resource checks require the exact fixed twenty-member owned fixture.");
            using var native = new ConstructorContext(arguments);
            native.Arm();
            var rooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
            using var future = native.Group.CaptureProgressResource(rooted, native.Api.GameplayApi);
            var resourceBefore = future.Read();
            var clip = Marshal.ReadIntPtr(native.Api.GameplaySource, 40);
            var reference = Marshal.ReadIntPtr(native.Api.GameplayProgress, 0x30);
            Check(resourceBefore.Phase == 1 && resourceBefore.Current == 1
                && reference == native.Api.ProgressResourceA && future.RootsRetained
                && native.Api.ActiveRoots == 8 && !native.Router.InitializationPending,
                "Ordinary handoff setup lost its real source and independent future reference root.");

            // This is the actual ordinary cross-run handoff entry. It closes
            // the native logical gate without starting an initializer or
            // lending any initializer physical-journal qualification.
            var handoff = native.Router.BeginHandoff(native.Session);
            Check(handoff.Read().Phase == 1 && native.Group.ReadInitialization() is { Phase: 0, Blocked: 0 }
                && native.Group.ReadSourceHealth() is { Known: true, Armed: true, Unknown: false, InFlight: 0 }
                && native.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn
                && !native.Group.GetTimeLease.ReadCurrent().Active && !native.Router.InitializationPending,
                "The actual ordinary closed gate was confused with a resource initializer hold or restored a Clock.");
            var health = native.Group.ReadSourceHealth();
            var observations = native.Observations.Count; var clockReads = native.ClockReads;
            var clipCalls = Marshal.ReadInt32(native.Api.GameplaySource, 64);
            var barrier = Export<ProgressResourceRead>(native.Module, "StockFixtureProgressResourceRead");
            var barrierCalls = barrier(0); var stores = barrier(6); var returns = barrier(7);
            var setClip = mode == "resource-dormant-setclip";
            var role = setClip ? NativePlaybackSourceRole.SetClip : NativePlaybackSourceRole.ProgressClip;
            var target = native.Targets.SourceTarget(role);
            var self = setClip ? native.Api.GameplaySource : native.Api.GameplayProgress;
            var value = setClip ? clip : reference;
            Check(native.Invoke(4, target.Address, self, unchecked((ulong)value.ToInt64()), target.MethodInfo, out var code) == 1
                && code == 0, "Actual ordinary-dormant same-value leaf failed: " + role);
            Check(Marshal.ReadIntPtr(native.Api.GameplaySource, 40) == clip
                && Marshal.ReadIntPtr(native.Api.GameplayProgress, 0x30) == reference
                && Marshal.ReadInt32(native.Api.GameplaySource, 64) == clipCalls + (setClip ? 1 : 0)
                && (setClip ? Marshal.ReadIntPtr(native.Api.GameplaySource, 96) == target.MethodInfo
                    : barrier(1) == unchecked((ulong)IntPtr.Add(self, 0x30).ToInt64())
                        && barrier(2) == unchecked((ulong)value.ToInt64()) && barrier(3) == unchecked((ulong)target.MethodInfo.ToInt64()))
                && barrier(0) == barrierCalls + (setClip ? 0ul : 1ul)
                && barrier(6) == stores + (setClip ? 0ul : 1ul) && barrier(7) == returns + (setClip ? 0ul : 1ul),
                "The ordinary-dormant leaf lost its actual original call, same-value store, barrier, or raw MethodInfo.");
            var after = native.Group.ReadSourceHealth();
            Check(after.Known && after.Armed && !after.Unknown && after.InFlight == 0
                && after.Entered == health.Entered + 1 && after.Exited == health.Exited + 1
                && after.WriterSequence == health.WriterSequence
                && after.SourceSequence == health.SourceSequence + (setClip ? 1ul : 0ul)
                && after.SchedulingSequence == health.SchedulingSequence
                && native.Observations.Count == observations && native.CallbackFailure is null
                && native.Group.ReadInitialization() is { Phase: 0, Blocked: 0 }
                && !native.Router.InitializationPending && handoff.Read().Phase == 1
                && native.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn
                && !native.Group.GetTimeLease.ReadCurrent().Active && native.ClockReads == clockReads
                && native.Api.ActiveRoots == 8 && !native.Router.PhysicalFaultPending,
                "An ordinary Dormant leaf fabricated an initializer, managed Before, phantom journal duty, fault, or current authority.");
            var resourceAfter = future.Read();
            Check(resourceAfter.Phase == (setClip ? 1u : 4u) && resourceAfter.Current == (setClip ? 1u : 0u)
                && resourceAfter.Revision == resourceBefore.Revision && resourceAfter.Reference == resourceBefore.Reference
                && resourceAfter.ContinuityIdentity == resourceBefore.ContinuityIdentity,
                "Ordinary Dormant isolation changed the wrong future-resource credential or revived a same-value resource attempt.");
            if (setClip) future.VerifyCurrent();
            else Refuse(() => future.VerifyCurrent(), "Ordinary same-value resource setter revived its predecessor future ticket.");
            Check(future.TryRetire() && !future.RootsRetained && native.Api.ActiveRoots == 7,
                "The known ordinary-dormant original did not retire its independently rooted future reference.");
            native.Router.Dispose(); native.Router.PumpCompleted(); native.Group.PumpCompleted();
            Check(native.Group.ReadActivity() is { Known: true, InFlight: 0 }
                && rooted.RootsReleased && native.Router.RootsReleased && native.Api.ActiveRoots == 0
                && native.Observations.All(value => value.IsReleased)
                && native.UniqueFinalClock && native.Broker.ReadActualPlaybackDobbyCallCounts() == (20, 20),
                "Ordinary closed-gate leaf retained phantom physical responsibility or blocked the actual seven-root drain.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPrepare = 20, actualCommit = 20, actualOrdinaryHandoff = true,
                sameValueOriginalExecuted = true, initializerCreated = false, managedLeafObservations = 0,
                phantomResponsibilities = false, knownPhysicalExit = true, retainedRoots = native.Api.ActiveRoots,
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
}
