using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class BrokerChecks
{
    internal static int Run(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "invalid";
        try
        {
            Check(args.Length == 4 && OperatingSystem.IsWindows() && IntPtr.Size == 8, "Expected mode owned-fixture helper dobby.");
            Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero && Path.GetFileName(args[1]) == "OwnedPlayback.dll",
                "Only an isolated owned fixture may be executed.");
            if (mode == "memory-ranges")
            {
                CheckMemoryRanges(); Done(mode, new { allocationCoverageVerified = true }); return 0;
            }
            var image = NativeLibrary.Load(Path.GetFullPath(args[1]));
            var target = Export<Pointer>(image, "RizOwnedPlayback_GetTarget")();
            var method = Export<Pointer>(image, "RizOwnedPlayback_GetMethod")();
            var cls = Export<Pointer>(image, "RizOwnedPlayback_GetClass")();
            var invoke = Marshal.GetDelegateForFunctionPointer<GetTime>(target);
            var set = Export<Set>(image, "RizOwnedPlayback_Set");
            var read = Export<Read>(image, "RizOwnedPlayback_Read");
            var last = Export<Last>(image, "RizOwnedPlayback_Last");
            var targetInfo = new VerifiedHookTarget("owned-playback.GetTime", target, method, cls, ExactHookAbi.InstanceSingleReturn0);
            var bodyHash = Convert.ToHexString(SHA256.HashData(Bytes(target, 640))).ToLowerInvariant();
            var imageHash = Hash(args[1]); var helperHash = Hash(args[2]);
            var main = new Main(); var logger = new Logger();
            var broker = mode == "fake-engine-reject" ? new ExactHookBroker(new FakeEngine(), main, logger) : new ExactHookBroker(args[3], main, logger);
            var binding = new NativePlaybackBinding("owned-broker-run", 1, 2, 3, 0x101, 0x102, 0x103, 0x104, 0x105, 0x106, method.ToInt64());
            var initial = new NativePlaybackClockState(binding, 1, NativePlaybackPhase.Paused, 10, 6, 2, 100);
            NativePlaybackClock? clock = null; var faults = 0; var before = 0; var after = 0;
            var identity = NativePlaybackClockPublication.Identity(binding);
            NativePlaybackSingleReturnSample Sample()
            {
                var state = clock?.Capture() ?? initial;
                return new(identity, state.Revision, NativePlaybackClockPublication.Phase(state), 11);
            }
            NativePlaybackGetTimeHookLease Install() => broker.BindPlaybackGetTime(targetInfo, image, imageHash, 640, bodyHash,
                Path.GetFullPath(args[2]), helperHash, Sample, _ => Interlocked.Increment(ref faults), _ => ++before, (_, _) => ++after);
            if (mode.EndsWith("-reject", StringComparison.Ordinal))
            {
                Rejection(mode, image, target, method, ref bodyHash, ref imageHash, Install, args);
                Done(mode, new { rejected = true }); return 0;
            }
            set(0, 0x3f800000u, 0);
            Check(invoke((IntPtr)binding.Player, method) == 1 && read(1) == 0, "Cold owned branch0 original failed.");
            set(1, 0x3f800000u, 0);
            Check(invoke((IntPtr)binding.Player, IntPtr.Zero) == 1 && read(1) == 1 && last(1) == IntPtr.Zero, "Cold owned null method branch1 failed.");
            var lease = Install();
            lease.VerifyCurrent(image, target, method);
            Check(lease.IsActive && !lease.ProductionAdmission && lease.PatchedByteCount == 6 && lease.OriginalAddress != target
                && lease.OriginalAddress != lease.DobbyOriginalAddress,
                "Actual broker receipt is not active or has the wrong exact patch/trampoline.");
            Check(lease.MatchesOriginalBody(bodyHash, 640) && !lease.MatchesOriginalBody(new string('0', 64), 640)
                && !lease.MatchesOriginalBody(bodyHash, 639), "Original-body pin accepted mismatched caller data.");
            Reject(() => lease.VerifyCurrent(image, target, IntPtr.Add(method, 8)), "Wrong MethodInfo borrowed the receipt.");
            Reject(() => lease.VerifyCurrent(IntPtr.Add(image, 0x1000), target, method), "Wrong module borrowed the receipt.");
            Reject(() => broker.Bind(targetInfo, _ => true, (_, _, _, _) => { }), "Generic Bind admitted Single-return ABI.");
            Reject(() => Install(), "Duplicate native broker owner was admitted.");
            Check(invoke((IntPtr)binding.Player, method) == 1 && read(1) == 1, "Hooked stock branch1 did not traverse the exact original.");
            set(0, 0x3f800000u, 0);
            Check(invoke((IntPtr)binding.Player, method) == 1 && read(1) == 0, "Hooked stock branch0 did not preserve source CMP.");
            var publication = new NativePlaybackClockPublication(lease, initial, IntPtr.Zero, () => lease.VerifyCurrent(image, target, method));
            clock = new NativePlaybackClock(binding, 10, 6, 2, 100, publication);
            Check(publication.IsCurrent(initial) && clock.TryGetTime(binding, 11, out var media) && media == 6,
                "Actual broker receipt did not adopt the N2 immutable clock publication.");
            Check(invoke((IntPtr)binding.Player, method) == 6 && before == 3 && after == 3 && read(0) == 5,
                "Actual native Single return did not use the broker-owned N2 policy with one original call.");
            var policy = lease.ReadCurrent().Authority!;
            Check(lease.OwnsPublication(policy) && !lease.OwnsPublication(policy with { }), "Equal caller-made policy became a private publication ticket.");
            Exercise(mode, args, image, target, method, lease, invoke, set, read, binding, policy);
            Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero, "GameAssembly was unexpectedly loaded.");
            Done(mode, new { originalCalls = read(0), before, after, faults, patchedByteCount = lease.PatchedByteCount }); return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "playback-broker-checks", mode, passed = false, checks = _checks, error = error.Message })); return 1;
        }
    }
    private static void Done(string mode, object details) => Console.WriteLine(JsonSerializer.Serialize(new
    { kind = "playback-broker-checks", mode, passed = true, checks = _checks, details,
        actualDobbyPrepared = mode != "memory-ranges" && !mode.EndsWith("-reject", StringComparison.Ordinal),
        actualDobbyCommitted = mode != "memory-ranges" && !mode.EndsWith("-reject", StringComparison.Ordinal), gameCodeExecuted = false, productionAdmission = false }));
}
