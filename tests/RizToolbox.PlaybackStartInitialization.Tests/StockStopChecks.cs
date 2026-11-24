using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;

internal static partial class StartChecks
{
    // The native original contains the real Stop call. Calling from an observer
    // would be interference and cannot demonstrate this stock-positive path.
    private static int RunStockStop(string[] arguments, string mode)
    {
        try
        {
            using var native = new ConstructorContext(arguments);
            native.Arm();
            var before = native.Clock.Capture();
            var directCalls = Marshal.ReadInt32(native.Api.GameplaySource, DirectCallsOffset);
            native.Stock(mode == "stock-stop-replay" ? NativePlaybackSourceRole.Replay : NativePlaybackSourceRole.Resume, .125f);
            var after = native.Clock.Capture();
            var stops = native.Observations.Where(value => value.Role == NativePlaybackSourceRole.AudioStop).ToArray();
            Console.WriteLine(JsonSerializer.Serialize(new { diagnostic = "actual-stock-stop-before-schedule",
                mode, before = before.Phase.ToString(), after = after.Phase.ToString(),
                failure = native.Router.LastFailure, actualStopCalls = Marshal.ReadInt32(native.Api.GameplaySource, DirectCallsOffset) - directCalls,
                stops = stops.Select(value => new { value.Sequence, value.ParentSequence, caller = value.Caller.ToInt64(), method = value.IncomingMethod.ToInt64() }),
                actualPrepare = 19, actualCommit = 19, gameExecuted = false }));
            Check(Marshal.ReadInt32(native.Api.GameplaySource, DirectCallsOffset) == directCalls + 1 && stops.Length == 1,
                "The actual owned stock original did not execute exactly one hooked AudioSource.Stop.");
            Check(after.Phase == NativePlaybackPhase.Scheduled && after.Rate == before.Rate,
                "Actual stock Stop ended the normal Replay/Resume Clock before schedule: " + native.Router.LastFailure);
            native.Router.PumpCompleted();
            Check(native.Group.GetTimeLease.ReadCurrent().Active, "Actual stock Stop withdrew the final native Clock publication.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                actualPrepare = 19, actualCommit = 19, actualStockStop = true, gameExecuted = false, productionAdmission = false }));
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
