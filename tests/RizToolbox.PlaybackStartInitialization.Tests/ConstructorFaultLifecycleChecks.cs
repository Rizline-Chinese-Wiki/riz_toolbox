using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    private static int RunConstructorFaultLifecycle(string[] a, string mode)
    {
        try
        {
            Check(a.Length == 5 && mode is "ctor-sdk-reentry" or "ctor-sdk-inflight", "Unknown constructor lifecycle mode.");
            Check(Hash(a[1]) == PlaybackSourceGroupTargets.ConstructorFixtureSha256,
                "Constructor lifecycle needs its separately fixed owned fixture.");
            using var native = new ConstructorContext(a);
            using (var sdk = new ConstructorSdk(native))
            {
                sdk.Speed(1.5);
                if (mode == "ctor-sdk-reentry") ConstructorReentry(native, sdk);
                else ConstructorInFlightDrain(native, sdk);
            }
            native.Router.PumpCompleted(); native.Group.PumpCompleted();
            var health = native.Group.ReadSourceHealth();
            Check(native.CallbackFailure is null && health.Known && !health.Unknown && health.InFlight == 0
                && health.Entered == health.Exited && native.Router.RootsReleased && native.Api.ActiveRoots == 0
                && native.Observations.All(observation => observation.IsReleased)
                && native.UniqueFinalClock && native.Broker.ReadActualPlaybackDobbyCallCounts() == (13, 13),
                "Constructor lifecycle did not finish with proved physical exit and a complete seven-root drain: " + native.CallbackFailure);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                constructorBefores = native.ConstructorBefores, physicalExited = true, sourceUnknown = false,
                retainedRoots = native.Api.ActiveRoots, actualPrepare = 13, actualCommit = 13,
                gameExecuted = false, productionAdmission = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() }));
            return 1;
        }
    }

    private static void ConstructorReentry(ConstructorContext native, ConstructorSdk sdk)
    {
        var player = native.Api.GameplayPlayer;
        var oldSource = native.Api.GameplaySource;
        var nestedSource = native.Api.CreateSharedConstructorSource();
        var method = native.Targets.SourceTarget(NativePlaybackSourceRole.PlayerConstructor).MethodInfo;
        var initial = native.Group.ReadSourceHealth();
        var calls = native.Read(0); var returns = native.Read(1);
        var pitchCalls = Marshal.ReadInt32(oldSource, 20);
        var rooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
        using var captured = sdk.Provider.Capture(native.Origin);
        using var writer = captured.PreparePitchWrite();
        using var prepared = NativePlaybackRateProviderAdoption.Prepare(sdk.Provider, native.Origin,
            new(GameplayControlAction.SetSpeed, native.Binding.ControllerGeneration, native.Binding.RunId, 2,
                GameplayControlValueUnit.PlaybackRateMultiplier), () => native.Run.VerifyCurrent(native.Binding));
        var order = new List<(uint At, IntPtr Reference, IntPtr Stored)>();
        var beforeCount = 0; var nestedCalls = 0; var nestedStatus = 0; uint nestedCode = uint.MaxValue;
        NativePlaybackSourceCompletion? nestedExit = null;
        native.AfterConstructorBefore = observation =>
        {
            ++beforeCount;
            var outer = beforeCount == 1;
            var health = native.Group.ReadSourceHealth();
            Check(observation.Self == player && observation.IncomingMethod == method
                && observation.ReferenceArgument == (outer ? oldSource : nestedSource)
                && observation.Depth == (outer ? 1u : 2u)
                && observation.ParentSequence == (outer ? 0ul : native.Observations.First(value => value.Role == NativePlaybackSourceRole.PlayerConstructor).Sequence)
                && health.InFlight == (outer ? 1ul : 2ul)
                && health.SourceSequence == initial.SourceSequence + (ulong)beforeCount
                && Marshal.ReadIntPtr(player, 16) == oldSource,
                "Nested current-player constructor did not enter the actual outer original before either store.");
            Check(native.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn && !sdk.Provider.Probe().Supported
                && !sdk.Lease.Execute(sdk.Request(3)).Success,
                "A nested constructor retained the old Clock, provider, or formal SDK control lease.");
            Refuse(() => { _ = rooted.Current; }, "Nested current constructor retained rooted source authority.");
            Refuse(captured.VerifyAuthority, "Nested current constructor retained captured source authority.");
            Refuse(writer.VerifyCurrent, "Nested current constructor retained the continuous writer.");
            if (outer)
            {
                Refuse(prepared.Commit, "Prepared old rate transaction committed during constructor reentry.");
                prepared.Restore();
            }
            Check(Marshal.ReadInt32(oldSource, 20) == pitchCalls,
                "A stale SDK transaction changed native pitch during constructor reentry.");
        };
        ConstructorBoundary boundary = (self, reference, incoming, at) => native.RecordBoundary(() =>
        {
            var stored = Marshal.ReadIntPtr(self, 16);
            order.Add((at, reference, stored));
            Check(self == player && incoming == method &&
                (reference == oldSource || reference == nestedSource),
                "Nested constructor changed the raw receiver, reference, or MethodInfo.");
            if (at == 1 && reference == oldSource && nestedCalls == 0)
            {
                Check(stored == oldSource && native.Group.ReadSourceHealth().InFlight == 1,
                    "Outer original did not reach its own native pre-store boundary.");
                ++nestedCalls;
                nestedStatus = native.Ctor(player, nestedSource, method, out nestedCode);
                // The following health read may legitimately acknowledge the
                // inner observation while the outer original remains in flight.
                // Retain its actual completed projection before that pump.
                var inner = native.Observations.Last(value => value.Role == NativePlaybackSourceRole.PlayerConstructor);
                Check(inner.TryReadCompletion(out nestedExit) && nestedExit is
                    { PhysicalExited: true, Returned: true, Abnormal: false, Unknown: false },
                    "The inner original has no real physical exit before the outer resumes.");
                Check(nestedStatus == 1 && nestedCode == 0 && Marshal.ReadIntPtr(player, 16) == nestedSource
                    && native.Group.ReadSourceHealth().InFlight == 1,
                    "The inner current-player original did not store and physically exit before the outer store.");
            }
        });
        native.Configure(0, boundary);
        var status = native.Ctor(player, oldSource, method, out var code);
        native.Configure(0, null); GC.KeepAlive(boundary);
        var constructors = native.Observations.Where(value => value.Role == NativePlaybackSourceRole.PlayerConstructor).ToArray();
        // Verify the physical receipt before a group-health read pumps and
        // acknowledges this observation's already requested release.
        foreach (var observation in constructors)
        {
            var physical = nestedExit is not null && nestedExit.Sequence == observation.Sequence ? nestedExit : null;
            var retained = physical is not null || observation.TryReadCompletion(out physical);
            Check(retained && physical is { PhysicalExited: true, Returned: true, Abnormal: false, Unknown: false }
                && physical.Sequence == observation.Sequence,
                "A nested current-player constructor lacks its own normal physical-exit receipt.");
        }
        var healthAfter = native.Group.ReadSourceHealth();
        Check(native.CallbackFailure is null && status == 1 && code == 0 && nestedCalls == 1 && nestedStatus == 1 && nestedCode == 0
            && beforeCount == 2 && native.ConstructorBefores == 2 && constructors.Length == 2
            && constructors[0].ParentSequence == 0 && constructors[0].Depth == 1
            && constructors[1].ParentSequence == constructors[0].Sequence && constructors[1].Depth == 2
            && order.SequenceEqual(new[] { (1u, oldSource, oldSource), (1u, nestedSource, oldSource),
                (2u, nestedSource, nestedSource), (2u, oldSource, oldSource) })
            && native.Read(0) == calls + 2 && native.Read(1) == returns + 2
            && native.Read(2) == unchecked((ulong)player.ToInt64())
            && native.Read(3) == unchecked((ulong)oldSource.ToInt64())
            && native.Read(4) == unchecked((ulong)method.ToInt64())
            && native.Read(5) == unchecked((ulong)oldSource.ToInt64())
            && native.Read(6) == unchecked((ulong)oldSource.ToInt64())
            && Marshal.ReadIntPtr(player, 16) == oldSource
            && healthAfter.Known && !healthAfter.Unknown && healthAfter.InFlight == 0
            && healthAfter.Entered == initial.Entered + 2 && healthAfter.Exited == initial.Exited + 2
            && healthAfter.SourceSequence == initial.SourceSequence + 2,
            "Actual constructor reentry did not preserve nested parentage, original call order, raw args, or physical drain: " + native.CallbackFailure);
        native.AfterConstructorBefore = null;
    }

    private static void ConstructorInFlightDrain(ConstructorContext native, ConstructorSdk sdk)
    {
        var player = native.Api.GameplayPlayer;
        var oldSource = native.Api.GameplaySource;
        var method = native.Targets.SourceTarget(NativePlaybackSourceRole.PlayerConstructor).MethodInfo;
        var initial = native.Group.ReadSourceHealth();
        var calls = native.Read(0); var returns = native.Read(1);
        var rooted = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
        var actual = Private<NativePlaybackActualSource>(rooted, "_actual");
        var roots = Private<List<RizToolbox.Runtime.NativeRuntime.RootHandle>>(actual, "_roots");
        var atBefore = false; var atAfter = false; var beforeObserved = false;
        native.AfterConstructorBefore = observation =>
        {
            var health = native.Group.ReadSourceHealth();
            Check(observation.Self == player && observation.ReferenceArgument == oldSource && observation.IncomingMethod == method
                && observation.Depth == 1 && observation.ParentSequence == 0 && health.InFlight == 1
                && Marshal.ReadIntPtr(player, 16) == oldSource && roots.Count == 7
                && native.Clock.Capture().Phase == NativePlaybackPhase.Withdrawn
                && !sdk.Provider.Probe().Supported && !sdk.Lease.Execute(sdk.Request(3)).Success,
                "In-flight constructor failed to revoke SDK/source authority before its original store.");
            beforeObserved = true;
        };
        ConstructorBoundary boundary = (self, reference, incoming, at) => native.RecordBoundary(() =>
        {
            Check(self == player && reference == oldSource && incoming == method
                && Marshal.ReadIntPtr(player, 16) == oldSource,
                "In-flight constructor changed its raw arguments or same-value store.");
            if (at == 1)
            {
                var activity = native.Group.ReadActivity();
                Check(beforeObserved && activity.Known && activity.InFlight == 1
                    && native.Api.ActiveRoots >= 7 && !native.Router.RootsReleased,
                    "Pre-store original had no retained physical activity or seven-root owner.");
                native.Router.Dispose(); native.Router.PumpCompleted(); native.Group.PumpCompleted();
                Check(native.Group.ReadActivity() is { Known: true, InFlight: 1 }
                    && !rooted.RootsReleased && !native.Router.RootsReleased
                    && roots.Count == 7 && native.Api.ActiveRoots >= 7,
                    "Dispose/Pump released the current player's seven roots while its original was still in flight.");
                atBefore = true;
            }
            else if (at == 2)
            {
                Check(atBefore && native.Group.ReadActivity() is { Known: true, InFlight: 1 }
                    && !native.Router.RootsReleased && roots.Count == 7 && native.Api.ActiveRoots >= 7,
                    "The current-player roots escaped before the native original's physical exit.");
                atAfter = true;
            }
        });
        native.Configure(0, boundary);
        var status = native.Ctor(player, oldSource, method, out var code);
        native.Configure(0, null); GC.KeepAlive(boundary);
        var constructor = native.Observations.Single(value => value.Role == NativePlaybackSourceRole.PlayerConstructor);
        Check(constructor.TryReadCompletion(out var physical) && physical is { PhysicalExited: true, Returned: true, Abnormal: false, Unknown: false }
            && physical.Sequence == constructor.Sequence,
            "The in-flight constructor lacks its own normal physical-exit receipt.");
        var healthAfter = native.Group.ReadSourceHealth();
        Check(native.CallbackFailure is null && status == 1 && code == 0 && beforeObserved && atBefore && atAfter
            && native.Read(0) == calls + 1 && native.Read(1) == returns + 1
            && healthAfter.Known && !healthAfter.Unknown && healthAfter.InFlight == 0
            && healthAfter.Entered == initial.Entered + 1 && healthAfter.Exited == initial.Exited + 1
            && Marshal.ReadIntPtr(player, 16) == oldSource,
            "The in-flight constructor failed to return normally with a real physical exit: " + native.CallbackFailure);
        native.Router.PumpCompleted(); native.Group.PumpCompleted();
        Check(rooted.RootsReleased && native.Router.RootsReleased && roots.Count == 0 && native.Api.ActiveRoots == 7
            && native.Observations.Where(value => value.Role == NativePlaybackSourceRole.PlayerConstructor).All(value => value.IsReleased),
            "Post-return drain failed to release only the seven source roots while SDK views still retain their own roots.");
        native.AfterConstructorBefore = null;
    }
}
