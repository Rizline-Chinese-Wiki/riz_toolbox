using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static class NativePlaybackStockSourceBridgeChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateSource();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreatePlayer(IntPtr source);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Method(uint role);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(uint kind, IntPtr target, IntPtr self, ulong bits, IntPtr method, out uint exception);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Configure(IntPtr source, uint flags);
    private sealed record Row(NativePlaybackSourceRole Role, NativePlaybackSourceKind Kind, string Export);
    private static readonly Row[] Rows = {
        new(NativePlaybackSourceRole.SetPitch, NativePlaybackSourceKind.Single1, "StockSetPitch"),
        new(NativePlaybackSourceRole.Pause, NativePlaybackSourceKind.Void0, "StockPause"),
        new(NativePlaybackSourceRole.Replay, NativePlaybackSourceKind.Single1, "StockReplay"),
        new(NativePlaybackSourceRole.Resume, NativePlaybackSourceKind.Single1, "StockResume"),
        new(NativePlaybackSourceRole.PlayScheduled, NativePlaybackSourceKind.Double1, "StockPlayScheduled"),
        new(NativePlaybackSourceRole.SetClip, NativePlaybackSourceKind.Reference1, "StockSetClip"),
        new(NativePlaybackSourceRole.ProgressPlay, NativePlaybackSourceKind.Boolean1, "StockProgressPlay"),
        new(NativePlaybackSourceRole.PauseLeaf, NativePlaybackSourceKind.Void0, "StockPauseLeaf") };

    internal static int Run(string helper, string fixture, string dobby, string mode)
    {
        var checks = 0;
        void Check(bool condition, string detail)
        { if (!condition) throw new InvalidOperationException(detail); ++checks; }
        void Reject(Action action)
        {
            try { action(); } catch (UnsupportedSignatureException) { ++checks; return; }
            throw new InvalidOperationException("Expected private current-scope rejection.");
        }
        var module = NativeLibrary.Load(Path.GetFullPath(fixture));
        T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
        var source = Export<CreateSource>("StockFixtureCreateSource")();
        var player = Export<CreatePlayer>("StockFixtureCreatePlayer")(source);
        var progress = Export<CreatePlayer>("StockFixtureCreateProgress")(player);
        var method = Export<Method>("StockMethod"); var invoke = Export<Invoke>("StockFixtureInvoke");
        var configure = Export<Configure>("StockFixtureConfigure");
        var observations = new List<NativePlaybackSourceObservation>();
        var returns = new List<NativePlaybackSourceCompletion>();
        var fault = new ManualResetEventSlim();
        var engine = new DobbyDetourEngine(dobby);
        var hub = new NativePlaybackStockSourceHub(Path.GetFullPath(helper), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(helper))), _ => fault.Set());
        var bridges = new Dictionary<NativePlaybackSourceRole, NativePlaybackStockSourceBridge>();
        var targets = new List<VerifiedHookTarget>();
        foreach (var row in Rows)
        {
            var targetAddress = NativeLibrary.GetExport(module, row.Export); var metadata = method((uint)row.Role);
            var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(metadata);
            // These independent bridge checks install actual Dobby patches in
            // owned storage. Full group/metadata admission is tested separately.
            var abi = row.Kind switch
            {
                NativePlaybackSourceKind.Void0 => ExactHookAbi.PlaybackVoid0,
                NativePlaybackSourceKind.Single1 => row.Role == NativePlaybackSourceRole.SetPitch
                    ? ExactHookAbi.PlaybackPitchSingle1Void : ExactHookAbi.PlaybackSingle1Void,
                NativePlaybackSourceKind.Double1 => ExactHookAbi.PlaybackDouble1Void,
                NativePlaybackSourceKind.Reference1 => ExactHookAbi.PlaybackReference1Void,
                NativePlaybackSourceKind.Boolean1 => ExactHookAbi.PlaybackBoolean1Void,
                _ => throw new UnsupportedSignatureException("Unknown owned source ABI.")
            };
            var target = new VerifiedHookTarget("owned-source-bridge/" + row.Role, targetAddress, metadata, layout.Class, abi);
            var bridge = new NativePlaybackStockSourceBridge(hub, row.Kind, row.Role, targetAddress, entry => engine.Prepare(target, entry),
                before: observation =>
                {
                    observation.VerifyCurrent();
                    Check(!observation.TryReadCompletion(out _), "A Before scope was mistaken for physical exit.");
                    var copied = observation.ActualOrigin(bridges[observation.Role]);
                    Reject(() => NativePlaybackSourceObservation.CaptureActual(bridges[observation.Role], copied, new object()));
                    copied.Token++;
                    Reject(() => bridges[observation.Role].ReadCurrentActual(copied));
                    observations.Add(observation);
                    if (mode == "deferred-release") observation.Dispose();
                    if (mode == "withdraw-inflight") hub.Withdraw();
                    if (mode == "callback-fault") throw new InvalidOperationException("owned observer failure");
                },
                after: completion =>
                {
                    completion.VerifyCurrent();
                    Check(!completion.PhysicalExited && completion.Returned, "A return callback asserted physical exit or lost original return.");
                    Check(!completion.Observation.TryReadCompletion(out _), "A callback saw an early completion journal row.");
                    if (mode == "withdraw-inflight") Reject(completion.VerifyAuthority);
                    returns.Add(completion);
                });
            bridges.Add(row.Role, bridge); targets.Add(target);
        }
        foreach (var target in targets) engine.Commit(target);
        Check(engine.ActualPreparedCallCounts == (8, 8), "The bridge fixture did not perform all eight actual native Prepare/Commit pairs.");
        int Call(NativePlaybackSourceRole role, IntPtr self, ulong bits = 0, IntPtr incoming = default)
        {
            var bridge = bridges[role]; var code = invoke((uint)bridge.Kind, bridge.TargetAddress, self, bits, incoming, out var exception);
            Check(code == 1 && exception == 0, "The actual owned source original did not return once."); return code;
        }
        Call(NativePlaybackSourceRole.SetPitch, source, 0x40000000, method(1));
        Check(observations.Count == 0 && !hub.ReadHealth().Armed, "A dormant entry issued an active source observation.");
        hub.Arm(); Check(hub.ReadHealth().Known && hub.ReadHealth().Armed, "The complete dormant source hub did not arm.");
        if (mode == "callback-fault")
        {
            Call(NativePlaybackSourceRole.SetPitch, source, 0x40400000, method(1));
            Check(Marshal.PtrToStructure<float>(IntPtr.Add(source, 16)) == 3, "Observer failure replaced the actual original setter.");
            Check(!hub.ReadHealth().Known && !hub.ReadHealth().Armed && fault.Wait(2000), "A source callback fault did not withdraw native authority.");
            var faults = hub.ReadHealth().NativeFaults;
            Check(hub.ReadHealth().NativeFaults == faults && hub.ReadHealth().NativeFaults == faults,
                "Reading the same sticky source fault created additional native fault events.");
            Check(observations[0].TryReadCompletion(out var failed) && failed is { PhysicalExited: true, Unknown: true },
                "A faulted source call lost its actual physical-exit responsibility.");
            Reject(failed!.VerifyCurrent);
            observations[0].Dispose();
            Check(observations[0].IsReleased, "A failed source callback lost its exact completed native journal cleanup ticket.");
        }
        else if (mode == "withdraw-inflight")
        {
            Call(NativePlaybackSourceRole.SetPitch, source, 0x40000000, method(1));
            Check(observations.Count == 1 && returns.Count == 1 && hub.ReadHealth() is { Known: true, Armed: false, InFlight: 0 },
                "Clean logical withdrawal incorrectly faulted an already captured native return responsibility.");
            Check(observations[0].TryReadCompletion(out var completion) && completion is { Returned: true, PhysicalExited: true, Unknown: false },
                "Clean withdrawal lost the actual physical-exit journal row.");
            completion!.VerifyCurrent(); Reject(completion.VerifyAuthority);
            observations[0].Dispose();
            Check(observations[0].IsReleased && hub.ReadHealth().Known, "Clean withdrawal lost its exact completed cleanup ticket.");
        }
        else if (mode == "deferred-release")
        {
            Call(NativePlaybackSourceRole.SetPitch, source, 0x40000000, method(1));
            Check(observations.Count == 1 && observations[0].ReleaseRequested && !observations[0].IsReleased,
                "Disposal inside Before dropped the native in-flight journal responsibility.");
            Check(observations[0].TryReadCompletion(out var completion) && completion is { PhysicalExited: true },
                "Deferred cleanup did not retain the actual completed call.");
            bridges[NativePlaybackSourceRole.SetPitch].PumpCompleted();
            Check(observations[0].IsReleased && hub.ReadHealth().Known && hub.ReadHealth().InFlight == 0,
                "Deferred source cleanup did not release its exact row after actual native exit.");
            bridges[NativePlaybackSourceRole.SetPitch].PumpCompleted();
            Check(hub.ReadHealth().Known, "Repeated cleanup changed the source owner's native authority.");
        }
        else if (mode == "normal")
        {
            Call(NativePlaybackSourceRole.SetPitch, source, 0x80000000, method(1));
            Check(observations[^1].ArgBits == 0x80000000 && unchecked((uint)Marshal.ReadInt64(source, 104)) == 0x80000000,
                "Single sign bits were not preserved through actual native entry and setter.");
            Check(observations[^1].IncomingMethod == method(1) && Marshal.ReadIntPtr(source, 72) == method(1), "Incoming MethodInfo was replaced.");
            Call(NativePlaybackSourceRole.PlayScheduled, source, unchecked((ulong)BitConverter.DoubleToInt64Bits(100.375)));
            Check(observations[^1].DoubleArgument == 100.375 && Marshal.ReadInt64(source, 112) == BitConverter.DoubleToInt64Bits(100.375),
                "The actual Double1 schedule argument was truncated or reconstructed.");
            Call(NativePlaybackSourceRole.SetClip, source, 0x1122334455667788, method(6));
            Check(Marshal.ReadIntPtr(source, 40).ToInt64() == 0x1122334455667788, "Reference argument bits changed.");
            Call(NativePlaybackSourceRole.ProgressPlay, progress, 0xa5, method(7));
            Check(Marshal.ReadInt64(player, 80) == 0xa5 && observations.Any(value => value.Role == NativePlaybackSourceRole.PauseLeaf
                && value.ParentSequence != 0), "Boolean/inline Pause did not retain its actual raw argument and parent.");
            configure(source, 2); Call(NativePlaybackSourceRole.SetPitch, source, 0x40000000, method(1));
            var nested = observations.TakeLast(2).ToArray();
            Check(nested[1].ParentSequence == nested[0].Sequence && nested[1].WriterSequence == nested[0].WriterSequence + 1,
                "A nested equal-value physical setter did not advance the same native owner fence.");
            Marshal.WriteInt32(source, 48, 0);
            Call(NativePlaybackSourceRole.Replay, player, 0x3e800000, method(3));
            var replay = observations.Last(value => value.Role == NativePlaybackSourceRole.Replay);
            Check(observations.Any(value => value.Role == NativePlaybackSourceRole.PlayScheduled && value.ParentSequence == replay.Sequence
                && value.DoubleArgument == 100.25), "Replay did not produce its actual paired schedule leaf.");
            Marshal.WriteInt32(source, 48, 0); Marshal.WriteInt64(source, 24, BitConverter.DoubleToInt64Bits(4));
            Call(NativePlaybackSourceRole.Resume, player, 0x3ec00000, method(4));
            var resume = observations.Last(value => value.Role == NativePlaybackSourceRole.Resume);
            Check(observations.Any(value => value.Role == NativePlaybackSourceRole.PlayScheduled && value.ParentSequence == resume.Sequence
                && value.DoubleArgument == 100.375), "Resume did not preserve the actual schedule argument independently of its origin.");
            Call(NativePlaybackSourceRole.Pause, player, incoming: method(2));
            Check(observations.Count == returns.Count && hub.ReadHealth() is { Known: true, InFlight: 0 }, "An actual native scope was lost or did not physically exit.");
            foreach (var observation in observations)
            {
                Check(observation.TryReadCompletion(out var completion) && completion is { Returned: true, PhysicalExited: true, Abnormal: false },
                    "No exact native physical-exit receipt exists after actual caller return.");
                completion!.VerifyCurrent();
                Reject(observation.VerifyCurrent);
                observation.Dispose();
                Check(observation.IsReleased, "The exact completed source receipt was not released once.");
            }
            Check(hub.ReadHealth().Known, "Normal receipt release changed native source authority.");
            hub.Withdraw();
            Call(NativePlaybackSourceRole.SetPitch, source, 0x40000000);
            Check(!hub.ReadHealth().Armed, "Logical source withdrawal revived itself on the next call.");
        }
        else throw new ArgumentException("Unknown actual source bridge mode.");
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "actual-native-source-clr-bridge", mode, passed = true, checks,
            nativeBefore = observations.Count, nativeReturned = returns.Count, sourceHealth = hub.ReadHealth(),
            actualDobbyPrepares = engine.ActualPreparedCallCounts.Prepared,
            actualDobbyCommits = engine.ActualPreparedCallCounts.Committed,
            gameExecuted = false, productionAdmission = false, installed = false }));
        return 0;
    }
}
