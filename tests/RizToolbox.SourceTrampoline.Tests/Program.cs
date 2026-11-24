using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

return SourceTrampolineChecks.Run(args);

internal static class SourceTrampolineChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Create();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateFrom(IntPtr parent);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Method(uint role);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint BodyLength(uint role);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(uint kind, IntPtr target, IntPtr self, ulong argument, IntPtr method, out uint exception);
    private sealed record Shape(NativePlaybackSourceRole Role, NativePlaybackSourceKind Kind, string Export);
    private static readonly Shape[] Shapes = {
        new(NativePlaybackSourceRole.SetPitch, NativePlaybackSourceKind.Single1, "StockSetPitch"),
        new(NativePlaybackSourceRole.Pause, NativePlaybackSourceKind.Void0, "StockPause"),
        new(NativePlaybackSourceRole.Replay, NativePlaybackSourceKind.Single1, "StockReplay"),
        new(NativePlaybackSourceRole.Resume, NativePlaybackSourceKind.Single1, "StockResume"),
        new(NativePlaybackSourceRole.PlayScheduled, NativePlaybackSourceKind.Double1, "StockPlayScheduled"),
        new(NativePlaybackSourceRole.SetClip, NativePlaybackSourceKind.Reference1, "StockSetClip"),
        new(NativePlaybackSourceRole.ProgressPlay, NativePlaybackSourceKind.Boolean1, "StockProgressPlay"),
        new(NativePlaybackSourceRole.PauseLeaf, NativePlaybackSourceKind.Void0, "StockPauseLeaf") };
    private static int _checks;
    private static void Check(bool value, string reason) { ++_checks; if (!value) throw new InvalidOperationException(reason); }
    private static void Reject(Action action)
    { ++_checks; try { action(); } catch (UnsupportedSignatureException) { return; } throw new InvalidOperationException("Expected exact mechanical proof rejection."); }
    private static byte[] Bytes(IntPtr address, int length) { var bytes = new byte[length]; Marshal.Copy(address, bytes, 0, length); return bytes; }
    private static void Write(IntPtr address, byte[] bytes)
    {
        Check(VirtualProtect(address, (nuint)bytes.Length, 0x40, out var old), "Could not open the owned test byte.");
        Marshal.Copy(bytes, 0, address, bytes.Length);
        Check(VirtualProtect(address, (nuint)bytes.Length, old, out _) && FlushInstructionCache((IntPtr)(-1), address, (nuint)bytes.Length), "Could not restore the owned byte's protection/cache.");
    }
    internal static int Run(string[] args)
    {
        try
        {
            Check(args.Length == 4 && OperatingSystem.IsWindows() && IntPtr.Size == 8, "Expected owned fixture, source helper, Dobby, mode.");
            var mode = args[3];
            Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero && Path.GetFileName(args[0]) == "RizToolbox.PlaybackStockFixture.dll", "Only the owned stock-shaped fixture may execute.");
            var selected = mode == "normal-ab" ? Shapes.Where(row => row.Role is not (NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume or NativePlaybackSourceRole.ProgressPlay)).ToArray() : Shapes;
            var module = NativeLibrary.Load(Path.GetFullPath(args[0]));
            T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
            var source = Export<Create>("StockFixtureCreateSource")();
            var player = Export<CreateFrom>("StockFixtureCreatePlayer")(source);
            var progress = Export<CreateFrom>("StockFixtureCreateProgress")(player);
            Check(source != IntPtr.Zero && player != IntPtr.Zero && progress != IntPtr.Zero, "The fixture could not create its distinct native objects.");
            var method = Export<Method>("StockMethod"); var lengths = Export<BodyLength>("StockFixtureGetBodyLength"); var invoke = Export<Invoke>("StockFixtureInvoke");
            var engine = new DobbyDetourEngine(args[2]);
            var helperHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1])));
            var hub = new NativePlaybackStockSourceHub(args[1], helperHash, _ => { });
            var observations = new List<NativePlaybackSourceObservation>();
            var returns = new List<NativePlaybackSourceCompletion>();
            var proofs = new Dictionary<NativePlaybackSourceRole, NativePlaybackSourceTrampolineProof>();
            var channels = new Dictionary<NativePlaybackSourceRole, NativePlaybackStockSourceBridge>();
            var targets = new List<VerifiedHookTarget>();
            foreach (var shape in selected)
            {
                var address = NativeLibrary.GetExport(module, shape.Export); var metadata = method((uint)shape.Role);
                var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(metadata);
                var abi = shape.Kind switch {
                    NativePlaybackSourceKind.Void0 => ExactHookAbi.PlaybackVoid0,
                    NativePlaybackSourceKind.Double1 => ExactHookAbi.PlaybackDouble1Void,
                    NativePlaybackSourceKind.Reference1 => ExactHookAbi.PlaybackReference1Void,
                    NativePlaybackSourceKind.Boolean1 => ExactHookAbi.PlaybackBoolean1Void,
                    _ => shape.Role == NativePlaybackSourceRole.SetPitch ? ExactHookAbi.PlaybackPitchSingle1Void : ExactHookAbi.PlaybackSingle1Void };
                var target = new VerifiedHookTarget("owned.prologue/" + shape.Role, address, metadata, layout.Class, abi);
                var body = Bytes(address, checked((int)lengths((uint)shape.Role)));
                var prefix = NativePlaybackSourcePrologue.ForRole(shape.Role);
                var incorrect = (byte[])body.Clone(); incorrect[0] ^= 1;
                Reject(() => prefix.RequirePrefix(incorrect)); prefix.RequirePrefix(body);
                var bridge = new NativePlaybackStockSourceBridge(hub, shape.Kind, shape.Role, address,
                    entry => {
                        var original = engine.Prepare(target, entry);
                        Reject(() => NativePlaybackSourceTrampolineProof.Create(address, (NativePlaybackSourceRole)999, body, original));
                        var proof = NativePlaybackSourceTrampolineProof.Create(address, shape.Role, body, original);
                        Check(proof.RelocatedByteCount == (shape.Role == NativePlaybackSourceRole.ProgressPlay ? 7 : 6)
                            && proof.OriginalAddress != original && proof.InformationAddress != IntPtr.Zero, "The exact complete source prefix was not independently relocated and unwound.");
                        proofs.Add(shape.Role, proof); return proof.OriginalAddress;
                    }, observation => { observation.VerifyCurrent(); observations.Add(observation); }, completion => {
                        completion.VerifyCurrent(); Check(!completion.PhysicalExited, "An After callback issued a premature physical-exit claim."); returns.Add(completion);
                    });
                channels.Add(shape.Role, bridge); targets.Add(target);
            }
            foreach (var target in targets) engine.Commit(target);
            Check(engine.ActualPreparedCallCounts == (selected.Length, selected.Length), "The claimed source hooks lack actual Prepare/Commit calls.");
            hub.Arm();
            if (mode is "normal" or "normal-ab" or "seh")
            {
                foreach (var shape in selected)
                {
                    var self = shape.Role is NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume ? player
                        : shape.Role == NativePlaybackSourceRole.ProgressPlay ? progress : source;
                    Marshal.WriteInt32(source, 68, 0); Marshal.WriteInt32(player, 44, 0); Marshal.WriteInt32(progress, 76, 0);
                    Marshal.WriteInt32(source, 48, shape.Role is NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay ? 1 : 0);
                    var bits = shape.Kind switch { NativePlaybackSourceKind.Double1 => unchecked((ulong)BitConverter.DoubleToInt64Bits(100.375)),
                        NativePlaybackSourceKind.Single1 => 0x3ec00000UL, NativePlaybackSourceKind.Reference1 => 0x1122334455667788UL,
                        NativePlaybackSourceKind.Boolean1 => 1UL, _ => 0UL };
                    if (mode == "seh") SetFault(shape.Role, source, player, progress);
                    var old = observations.Count;
                    var result = invoke((uint)shape.Kind, channels[shape.Role].TargetAddress, self, bits, method((uint)shape.Role), out var exception);
                    Check(mode == "seh" ? result == 2 && exception >= 0xe2400000 : result == 1 && exception == 0,
                        "The actual original return/SEH did not cross its exact source relocation: " + shape.Role);
                    var own = observations.Skip(old).Single(value => value.Role == shape.Role);
                    Check(own.Self == self && own.IncomingMethod == method((uint)shape.Role) && own.ArgBits == bits,
                        "An exact source prefix changed self, incoming metadata or raw argument bits.");
                    Check(own.TryReadCompletion(out var completed) && completed!.PhysicalExited
                        && completed.Returned == (mode != "seh") && completed.Abnormal == (mode == "seh"), "The actual original lost its matching native physical-exit receipt.");
                    proofs[shape.Role].Verify();
                    foreach (var observed in observations.Skip(old)) {
                        Check(observed.TryReadCompletion(out _), "A nested original still owns an unaccounted native exit.");
                        observed.Dispose(); Check(observed.IsReleased, "A native finally receipt was not released exactly once.");
                    }
                }
            }
            else
            {
                Check(mode is "dobby-tamper" or "thunk-tamper" or "unwind-tamper", "Unknown mechanical proof mode.");
                foreach (var proof in proofs.Values)
                {
                    var address = mode == "dobby-tamper" ? proof.DobbyOriginalAddress : mode == "thunk-tamper" ? proof.OriginalAddress : proof.InformationAddress;
                    var original = Bytes(address, 1); Write(address, new[] { (byte)(original[0] ^ 1) }); Reject(proof.Verify);
                    Write(address, original); proof.Verify();
                    // Restoration here verifies a mechanical byte seal only.
                    // A group fault is sticky and is tested by its concrete owner.
                }
            }
            Check(hub.ReadHealth() is { Known: true, InFlight: 0 }, "A mechanical source check lost actual native in-flight responsibility.");
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "exact-source-trampoline-checks", passed = true, mode, checks = _checks,
                physicalTargets = selected.Length, actualDobbyPrepare = engine.ActualPreparedCallCounts.Prepared,
                actualDobbyCommit = engine.ActualPreparedCallCounts.Committed, nativeBefore = observations.Count, nativeCompletion = returns.Count,
                progressRelocationBytes = proofs.TryGetValue(NativePlaybackSourceRole.ProgressPlay, out var boolProof) ? boolProof.RelocatedByteCount : 0,
                gameCodeExecuted = false, gameDllMainExecuted = false, productionAdmission = false, installed = false }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); Console.WriteLine(JsonSerializer.Serialize(new { passed = false, checks = _checks, error = error.Message })); return 1; }
    }
    private static void SetFault(NativePlaybackSourceRole role, IntPtr source, IntPtr player, IntPtr progress)
    {
        if (role == NativePlaybackSourceRole.SetPitch) Marshal.WriteInt32(source, 68, 1);
        else if (role == NativePlaybackSourceRole.Pause) Marshal.WriteInt32(player, 44, 1);
        else if (role == NativePlaybackSourceRole.Replay) Marshal.WriteInt32(player, 44, 2);
        else if (role == NativePlaybackSourceRole.Resume) Marshal.WriteInt32(player, 44, 4);
        else if (role == NativePlaybackSourceRole.PlayScheduled) Marshal.WriteInt32(source, 68, 4);
        else if (role == NativePlaybackSourceRole.SetClip) Marshal.WriteInt32(source, 68, 8);
        else if (role == NativePlaybackSourceRole.ProgressPlay) Marshal.WriteInt32(progress, 76, 8);
        else if (role == NativePlaybackSourceRole.PauseLeaf) Marshal.WriteInt32(source, 68, 32);
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint size, uint protection, out uint old);
    [DllImport("kernel32")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint size);
}
