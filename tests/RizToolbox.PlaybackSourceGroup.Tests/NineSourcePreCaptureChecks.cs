using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class SourceGroupChecks
{
    [DllImport("ntdll", EntryPoint = "RtlLookupFunctionEntry")]
    private static extern IntPtr PreCaptureFunction(ulong pc, out ulong imageBase, IntPtr history);
    private static int RunPreCapture(string[] args)
    {
        var mode = args[0];
        Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero, "The negative fixture must not map GameAssembly.");
        var module = NativeLibrary.Load(Path.GetFullPath(args[1]));
        var targets = PlaybackSourceGroupTargets.ResolveOwnedFixture(module);
        var target = mode switch
        {
            "precapture-pitch-uw" => targets.PitchInstance.Address,
            "precapture-replay-chain-uw" => targets.ReplayScheduled.Address,
            "precapture-gettime-uw" => targets.GetTime.Address,
            _ => throw new InvalidOperationException("Unknown closed pre-capture negative mode.")
        };
        var function = PreCaptureFunction(unchecked((ulong)target.ToInt64()), out var imageBase, IntPtr.Zero);
        Check(function != IntPtr.Zero && imageBase == unchecked((ulong)module.ToInt64()), "The negative target has no actual image-owned runtime function.");
        if (mode == "precapture-replay-chain-uw")
        {
            var firstEnd = (uint)Marshal.ReadInt32(function, 4);
            var late = PreCaptureFunction(imageBase + firstEnd, out var lateBase, IntPtr.Zero);
            Check(late != IntPtr.Zero && late != function && lateBase == imageBase, "Replay has no distinct actual late runtime function."); function = late;
        }
        var unwind = new IntPtr(checked((long)imageBase + (uint)Marshal.ReadInt32(function, 8)));
        var offset = mode == "precapture-gettime-uw" ? 9 : mode == "precapture-replay-chain-uw" ? 6 : 5;
        if (mode == "precapture-replay-chain-uw")
            Check((Marshal.ReadByte(unwind) >> 3 & 4) != 0, "The late Replay unwind record has no CHAININFO evidence.");
        var address = IntPtr.Add(unwind, offset); var before = Marshal.ReadByte(address);
        var changed = (byte)(before ^ (mode == "precapture-replay-chain-uw" ? 1 : 0x10));
        var bodies = PlaybackSourceGroupTargets.SourceRoles().Select(role =>
            (Address: targets.SourceTarget(role).Address, Bytes: Bytes(targets.SourceTarget(role).Address, targets.SourceBodyLength(role))))
            .Append((Address: targets.GetTime.Address, Bytes: Bytes(targets.GetTime.Address, targets.GetTimeBodyLength))).ToArray();
        var broker = new ExactHookBroker(Path.GetFullPath(args[4]), new Main(), new Logger());
        PlaybackSourceGroupLease Bind() => broker.BindPlaybackSourceGroup(targets,
            Path.GetFullPath(args[2]), HashFile(args[2]), Path.GetFullPath(args[3]), HashFile(args[3]),
            () => throw new InvalidOperationException("A rejected group must never read its clock."), _ => { }, _ => { });
        string? reason = null;
        Write(address, changed);
        try
        {
            try { Bind(); }
            catch (UnsupportedSignatureException error) { reason = error.Message; }
            Check(reason is not null && reason.Contains("Source RF/xdata was changed before capture and differs from its pinned file.", StringComparison.Ordinal),
                "Dirty pre-capture source unwind was accepted or failed without its exact pinned-file reason: " + reason);
            Check(broker.ReadActualPlaybackDobbyCallCounts() == (0L, 0L),
                "A pre-capture unwind failure reached an actual source Prepare or Commit.");
            Check(bodies.All(body => Bytes(body.Address, body.Bytes.Length).AsSpan().SequenceEqual(body.Bytes)), "A pre-capture unwind failure modified a stock-shaped source body.");
        }
        finally { Write(address, before); }
        var counts = broker.ReadActualPlaybackDobbyCallCounts();
        Reject(() => Bind(), "Restoring dirty source metadata revived its retained failed private installation.");
        Check(broker.ReadActualPlaybackDobbyCallCounts() == counts && counts == (0L, 0L),
            "A restored pre-capture failure performed another Prepare/Commit.");
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "actual-nine-member-playback-source-group", mode, passed = true,
            checks = _checks, preCaptureRejected = true, actualGroupArmed = false,
            actualDobbyPrepareCount = counts.Prepared, actualDobbyCommitCount = counts.Committed,
            originalUnwindByte = before, changedUnwindByte = changed, unwindOffset = offset, reason,
            nativeEntriesInvoked = false, gameCodeExecuted = false, productionAdmission = false }));
        return 0;
    }
}
