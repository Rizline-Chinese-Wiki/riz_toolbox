using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class StartChecks
{
    private static readonly NativePlaybackSourceRole[] LegacyAudioRoles =
    {
        NativePlaybackSourceRole.SetPitch, NativePlaybackSourceRole.PlayScheduled,
        NativePlaybackSourceRole.SetClip, NativePlaybackSourceRole.PauseLeaf, NativePlaybackSourceRole.SetTime,
    };
    private static readonly int[] LegacyAudioCounterOffsets = { 20, 60, 64, 56, 120 };
    private static readonly int[] LegacyAudioMethodOffsets = { 72, 80, 96, 88, 128 };
    private static ulong LegacyAudioBits(ConstructorContext native, int index)
        => index == 0 || index == 4 ? 0x3FA00000UL : index == 1 ? 0x4069000000000000UL
            : index == 2 ? unchecked((ulong)native.Api.GameplayClip.ToInt64()) : 0UL;

    private static void LegacyAudioCall(ConstructorContext native, IntPtr source, int index, int iteration)
    {
        var target = native.Targets.SourceTarget(LegacyAudioRoles[index]);
        var method = iteration % 3 == 0 ? target.MethodInfo : iteration % 3 == 1 ? IntPtr.Zero : native.Targets.Pause.MethodInfo;
        var kind = index == 1 ? 3u : index == 2 ? 4u : index == 3 ? 1u : 2u;
        if (native.Invoke(kind, target.Address, source, LegacyAudioBits(native, index), method, out var code) != 1 || code != 0)
            throw new InvalidOperationException("An actual legacy AudioSource original failed: " + LegacyAudioRoles[index]);
    }

    private static void CheckLegacyForeign(ConstructorContext native, IntPtr foreign)
    {
        var health = native.Group.ReadSourceHealth(); var observed = native.Observations.Count;
        var clockReads = native.ClockReads; var activeRoots = native.Api.ActiveRoots;
        var calls = LegacyAudioCounterOffsets.Select(offset => Marshal.ReadInt32(foreign, offset)).ToArray();
        for (var iteration = 0; iteration < 3; ++iteration)
            for (var index = 0; index < LegacyAudioRoles.Length; ++index) LegacyAudioCall(native, foreign, index, iteration);
        var worker = Task.Run(() =>
        {
            for (var iteration = 0; iteration < 320; ++iteration)
                for (var index = 0; index < LegacyAudioRoles.Length; ++index) LegacyAudioCall(native, foreign, index, iteration);
        });
        Check(worker.Wait(10000) && worker.IsCompletedSuccessfully, "Legacy same-class foreign worker flood did not physically return.");
        for (var index = 0; index < LegacyAudioRoles.Length; ++index)
            Check(Marshal.ReadInt32(foreign, LegacyAudioCounterOffsets[index]) == calls[index] + 323
                && Marshal.ReadIntPtr(foreign, LegacyAudioMethodOffsets[index]) == IntPtr.Zero,
                "Foreign legacy original count or last raw null MethodInfo changed: " + LegacyAudioRoles[index]);
        Check(unchecked((uint)Marshal.ReadInt32(foreign, 16)) == (uint)LegacyAudioBits(native, 0)
            && unchecked((ulong)Marshal.ReadInt64(foreign, 112)) == LegacyAudioBits(native, 1)
            && Marshal.ReadIntPtr(foreign, 40) == native.Api.GameplayClip
            && Marshal.ReadInt32(foreign, 48) == 0
            && unchecked((ulong)Marshal.ReadInt64(foreign, 136)) == LegacyAudioBits(native, 4),
            "Foreign legacy originals lost raw pitch/schedule/clip/Pause/time values.");
        Check(native.Group.ReadSourceHealth() == health && native.Observations.Count == observed
            && native.ClockReads == clockReads && native.Api.ActiveRoots == activeRoots
            && native.CallbackFailure is null && !native.Router.PhysicalFaultPending,
            "A foreign legacy AudioSource call touched current fences, journal, roots or Clock reader.");
    }

    private static int RunLegacyAudioCurrent(string[] arguments, string mode)
    {
        try
        {
            Check(arguments.Length == 5 && Hash(arguments[1]) == PlaybackSourceGroupTargets.DirectAudioFixtureSha256,
                "Legacy current checks require the exact nineteen-member owned fixture.");
            var index = int.Parse(mode[(mode.LastIndexOf('-') + 1)..]);
            Check(index is >= 0 and < 5, "Unknown legacy AudioSource role.");
            using var native = new ConstructorContext(arguments); native.Arm();
            var clock = native.Clock; var source = Private<NativePlaybackStockRootedSource>(native.Router, "_source");
            var health = native.Group.ReadSourceHealth(); var calls = Marshal.ReadInt32(native.Api.GameplaySource, LegacyAudioCounterOffsets[index]);
            var observations = native.Observations.Count;
            var target = native.Targets.SourceTarget(LegacyAudioRoles[index]); var before = 0;
            native.AfterLegacyAudioBefore = observation =>
            {
                Check(observation.Role == LegacyAudioRoles[index] && observation.Self == native.Api.GameplaySource
                    && observation.IncomingMethod == target.MethodInfo && observation.ArgBits == LegacyAudioBits(native, index)
                    && Marshal.ReadInt32(native.Api.GameplaySource, LegacyAudioCounterOffsets[index]) == calls,
                    "Current legacy Before did not precede the original's actual native store with unchanged raw arguments.");
                Check(clock.Capture().Phase == NativePlaybackPhase.Withdrawn && !native.Group.GetTimeLease.ReadCurrent().Active,
                    "Current legacy source retained old final Clock authority before the original's store.");
                Refuse(() => { _ = source.Current; }, "Current legacy Before retained old rooted authority.");
                ++before;
            };
            LegacyAudioCall(native, native.Api.GameplaySource, index, 0);
            var after = native.Group.ReadSourceHealth();
            Check(after.Entered == health.Entered + 1 && after.Exited == health.Exited + 1 && after.InFlight == 0
                && after.WriterSequence == health.WriterSequence + (index == 0 ? 1UL : 0UL)
                && after.SourceSequence == health.SourceSequence + (index == 2 ? 1UL : 0UL)
                && after.SchedulingSequence == health.SchedulingSequence + (index is 0 or 2 ? 0UL : 1UL)
                && native.Observations.Count == observations + 1 && before == 1 && native.CallbackFailure is null
                && Marshal.ReadInt32(native.Api.GameplaySource, LegacyAudioCounterOffsets[index]) == calls + 1,
                "Current legacy source did not acquire one exact attempt and physical completion.");
            Check(clock.Capture().Phase == NativePlaybackPhase.Withdrawn && !native.Group.GetTimeLease.ReadCurrent().Active,
                "An external current legacy leaf retained the old final Clock.");
            native.AfterLegacyAudioBefore = null;
            Refuse(() => { _ = source.Current; }, "An external legacy leaf retained old rooted authority.");
            native.Router.Dispose();native.Router.PumpCompleted();
            Check(native.Router.RootsReleased && native.Api.ActiveRoots == 0,
                "Known current legacy physical completion did not drain seven roots.");
            Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = true, checks = _checks, role = LegacyAudioRoles[index].ToString(),
                actualPrepare = 19, actualCommit = 19, beforeOriginalStoreWithdrawn = true,
                sdkExecuted = false, retainedRoots = native.Api.ActiveRoots, gameExecuted = false }));
            return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine(error);Console.WriteLine(JsonSerializer.Serialize(new { mode, passed = false, checks = _checks, failure = error.ToString() }));return 1; }
    }
}
