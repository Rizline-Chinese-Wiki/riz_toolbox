using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;

internal static partial class StartChecks
{
    // Formal SDK foreign-source checks call this while the current native
    // provider and its independently rooted AudioSource remain usable.
    private static void CheckPlayerControlForeign(ConstructorContext native, IntPtr foreignSource)
    {
        var foreignPlayer = Export<CreateChild>(native.Module, "StockFixtureCreatePlayer")(foreignSource);
        Marshal.WriteIntPtr(foreignPlayer, Marshal.ReadIntPtr(native.Api.GameplayPlayer));
        Check(foreignPlayer != native.Api.GameplayPlayer
            && Marshal.ReadIntPtr(foreignPlayer) == Marshal.ReadIntPtr(native.Api.GameplayPlayer)
            && Marshal.ReadIntPtr(foreignPlayer, 16) == foreignSource
            && foreignSource != native.Api.GameplaySource,
            "Player-control isolation requires a distinct same-class player and a distinct actual foreign source.");
        var roles = new[] { NativePlaybackSourceRole.Pause, NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.Resume };
        var counterOffsets = new[] { 64, 56, 60 };
        var counters = counterOffsets.Select(offset => Marshal.ReadInt32(foreignPlayer, offset)).ToArray();
        var pauseCalls = Marshal.ReadInt32(foreignSource, 56);
        var pitchCalls = Marshal.ReadInt32(foreignSource, 20);
        var scheduleCalls = Marshal.ReadInt32(foreignSource, 60);
        var timeCalls = Marshal.ReadInt32(foreignSource, 120);
        var health = native.Group.ReadSourceHealth();
        var observations = native.Observations.Count;
        var clock = native.Clock?.Capture();
        var roots = native.Api.ActiveRoots;
        var reads = native.ClockReads;
        var currentPitchCalls = Marshal.ReadInt32(native.Api.GameplaySource, 20);

        void Call(int index, int iteration)
        {
            var target = native.Targets.SourceTarget(roles[index]);
            var incoming = iteration % 3 == 0 ? target.MethodInfo
                : iteration % 3 == 1 ? IntPtr.Zero : native.Targets.PitchInstance.MethodInfo;
            // Force the owned foreign Pause/Resume body through its real leaf
            // calls. No current object or current lifecycle state is seeded.
            if (index != 1) Marshal.WriteInt32(foreignSource, 48, index == 0 ? 1 : 0);
            if (native.Invoke(index == 0 ? 1u : 2u, target.Address, foreignPlayer,
                    index == 0 ? 0UL : 0x3E800000UL, incoming, out var code) != 1 || code != 0
                || Marshal.ReadIntPtr(foreignPlayer, 88) != incoming
                || index != 0 && unchecked((uint)Marshal.ReadInt64(foreignPlayer, 72)) != 0x3E800000u)
                throw new InvalidOperationException("A real foreign MUG original lost its raw argument/MethodInfo: " + roles[index]);
        }
        for (var iteration = 0; iteration < 3; ++iteration)
            for (var index = 0; index < roles.Length; ++index) Call(index, iteration);
        var worker = Task.Run(() =>
        {
            for (var iteration = 0; iteration < 320; ++iteration)
                for (var index = 0; index < roles.Length; ++index) Call(index, iteration);
        });
        Check(worker.Wait(10000) && worker.IsCompletedSuccessfully,
            "Foreign MUG worker flood did not physically return through its actual original and children.");
        for (var index = 0; index < roles.Length; ++index)
            Check(Marshal.ReadInt32(foreignPlayer, counterOffsets[index]) == counters[index] + 323,
                "An unrelated MUG wrapper was dropped or duplicated: " + roles[index]);
        Check(Marshal.ReadInt32(foreignSource, 56) == pauseCalls + 323
            && Marshal.ReadInt32(foreignSource, 20) == pitchCalls + 646
            && Marshal.ReadInt32(foreignSource, 60) == scheduleCalls + 646
            && Marshal.ReadInt32(foreignSource, 120) == timeCalls + 323
            && Marshal.ReadIntPtr(foreignPlayer, 88) == IntPtr.Zero,
            "Foreign MUG calls did not reach their real Pause/SetPitch/SetTime/Schedule leaves exactly once.");
        Check(native.Group.ReadSourceHealth() == health && native.Observations.Count == observations
            && native.Clock?.Capture() == clock && native.ClockReads == reads && native.Api.ActiveRoots == roots
            && Marshal.ReadInt32(native.Api.GameplaySource, 20) == currentPitchCalls
            && native.CallbackFailure is null && !native.Router.PhysicalFaultPending,
            "Foreign MUG wrappers or their actual foreign children changed the current fences, journal, Clock or roots.");
    }

    private static int RunPlayerControlNormal(string[] arguments, string mode)
    {
        try
        {
            Check(arguments.Length == 5 && Hash(arguments[1]) == PlaybackSourceGroupTargets.DirectAudioFixtureSha256,
                "Player-control normal checks require the exact nineteen-member owned fixture.");
            using var native = new ConstructorContext(arguments);
            using (var sdk = new ConstructorSdk(native))
            {
                sdk.Speed(1.5);
                var before = new[] { 64, 56, 60 }.Select(offset => Marshal.ReadInt32(native.Api.GameplayPlayer, offset)).ToArray();
                ConstructorNormal(native, sdk);
                Check(Marshal.ReadInt32(native.Api.GameplayPlayer, 64) == before[0] + 2
                    && Marshal.ReadInt32(native.Api.GameplayPlayer, 56) == before[1] + 1
                    && Marshal.ReadInt32(native.Api.GameplayPlayer, 60) == before[2] + 1,
                    "Current watched MUG Pause/Replay/Resume did not execute their exact original counts.");
            }
            native.Router.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0
                && native.Observations.All(value => value.IsReleased) && native.CallbackFailure is null
                && native.UniqueFinalClock && native.ClockReads > 0
                && native.Broker.ReadActualPlaybackDobbyCallCounts() == (19, 19),
                "Player-control normal lost its actual final Clock, hooks or known physical root drain.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks,
                sdkExecuted = true, currentPauseReplayResume = true, actualSetTimePitchSchedule = true,
                uniqueFinalClock = native.UniqueFinalClock, actualPrepare = 19, actualCommit = 19,
                retainedRoots = native.Api.ActiveRoots, gameExecuted = false, productionAdmission = false }));
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
