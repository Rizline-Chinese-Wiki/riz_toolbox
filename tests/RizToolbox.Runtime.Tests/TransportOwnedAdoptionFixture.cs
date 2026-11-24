using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// hostfxr calls this only after the ordinary five-image loader chain published
// a real V5 record in this PID. This entry never loads/configures native hooks,
// executes a copied game body. The isolated live-tamper negative mode temporarily
// changes one byte of the generated owned image and restores it in finally.
public static class TransportOwnedAdoptionFixture
{
    private static readonly List<object> Roots = new();
    private static int _invoked;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(IntPtr output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint VerifyNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NodeInfo(uint kind, uint slot, ref NativeEngineNodeInformation value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InlineInfo(uint slot, ref NativeEngineInlineInformation value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint SchedulerInfo(uint kind, uint slot, ref NativeEngineSchedulerInformation value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint WaitInfo(uint slot, ref NativeEngineWaitInformation value);

    private sealed record FamilyObservation(int Family, string Name, uint Attached, uint Unknown, ulong Epoch,
        ulong[] Counters, string InformationSha256, string WithoutAttachmentSha256);
    private sealed class ProgressLogger : ILogger
    {
        private readonly Action<string> _emit;
        internal ProgressLogger(Action<string> emit) => _emit = emit;
        public void Log(LogLevel level, string message, Exception? exception = null)
            => _emit("coordinator: " + message);
    }
    private sealed class LiveTamperLogger : ILogger
    {
        private readonly IntPtr _target, _module;
        private readonly Action<string> _stage;
        private bool _writeAttempted;
        public ulong TargetAddress => Address(_target);
        public byte OriginalByte { get; }
        public byte AlteredByte { get; }
        public byte ObservedAlteredByte { get; private set; }
        public byte RestoredByte { get; private set; }
        public uint OriginalProtection { get; private set; }
        public uint ObservedProtectionBeforeInner { get; private set; }
        public uint RestoredProtection { get; private set; }
        public int TriggerCount { get; private set; }
        public bool MutationApplied { get; private set; }
        public bool AlterationObserved { get; private set; }
        public bool ProtectionRestoredBeforeInner { get; private set; }
        public bool FlushCompleted { get; private set; }
        public bool RestoreAttempted { get; private set; }
        public bool Restored { get; private set; }
        public bool RestoreVerified { get; private set; }
        public string? Failure { get; private set; }
        public string? RestoreFailure { get; private set; }
        internal LiveTamperLogger(IntPtr target, IntPtr module, byte original, Action<string> stage)
            => (_target, _module, OriginalByte, AlteredByte, _stage) = (target, module, original, unchecked((byte)(original ^ 1)), stage);
        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            _stage("coordinator: " + message);
            if (message != "V5 adoption begin-full-adoption.begin") return;
            ++TriggerCount;
            if (TriggerCount != 1) { Failure ??= "The exact pre-adoption logger stage repeated."; return; }
            _stage("live-tamper.actual-mutation.begin");
            try
            {
                var memory = Query();
                if (memory.Protect != 0x20 || Marshal.ReadByte(_target) != OriginalByte)
                    throw new InvalidOperationException("The real owned typed window differs before the negative probe.");
                if (!VirtualProtect(_target, 1, 0x40, out var prior)) throw Win32("Initial VirtualProtect");
                OriginalProtection = prior;
                try
                {
                    if (prior != 0x20) throw new InvalidOperationException("The exact source page was not RX before mutation.");
                    _writeAttempted = true; Marshal.WriteByte(_target, AlteredByte);
                    ObservedAlteredByte = Marshal.ReadByte(_target);
                    MutationApplied = true; AlterationObserved = ObservedAlteredByte == AlteredByte && AlteredByte != OriginalByte;
                    FlushCompleted = FlushInstructionCache(GetCurrentProcess(), _target, 1);
                    if (!FlushCompleted) throw Win32("Mutation FlushInstructionCache");
                }
                finally
                {
                    if (!VirtualProtect(_target, 1, prior, out _)) throw Win32("Mutation protection restore");
                }
                var after = Query(); ObservedProtectionBeforeInner = after.Protect;
                ProtectionRestoredBeforeInner = after.Protect == OriginalProtection && after.Protect == 0x20;
            }
            catch (Exception error) { Failure = error.ToString(); }
            _stage("live-tamper.actual-mutation.end");
            // Never call native Verify while the byte is altered. The actual
            // next managed inner verifier must independently catch the byte.
        }
        internal void Restore()
        {
            RestoreAttempted = true; _stage("live-tamper.finally-restore.begin");
            try
            {
                if (!_writeAttempted) throw new InvalidOperationException("No actual owned source write was attempted.");
                var before = Query();
                var expectedProtection = OriginalProtection;
                if (expectedProtection != 0x20 || before.Protect is not (0x20 or 0x40))
                    throw new InvalidOperationException("Restoration lost the exact original RX page contract.");
                if (!VirtualProtect(_target, 1, 0x40, out _)) throw Win32("Restore VirtualProtect");
                try
                {
                    Marshal.WriteByte(_target, OriginalByte); RestoredByte = Marshal.ReadByte(_target);
                    if (!FlushInstructionCache(GetCurrentProcess(), _target, 1)) throw Win32("Restore FlushInstructionCache");
                }
                finally
                {
                    if (!VirtualProtect(_target, 1, expectedProtection, out _)) throw Win32("Final source protection restore");
                }
                var after = Query(); RestoredProtection = after.Protect;
                Restored = RestoredByte == OriginalByte;
                RestoreVerified = Restored && after.Protect == 0x20 && Marshal.ReadByte(_target) == OriginalByte;
            }
            catch (Exception error) { RestoreFailure = error.ToString(); }
            _stage("live-tamper.finally-restore.end");
        }
        private ProbeMemory Query()
        {
            if (VirtualQuery(_target, out var value, (nuint)Marshal.SizeOf<ProbeMemory>()) != (nuint)Marshal.SizeOf<ProbeMemory>()
                || value.AllocationBase != _module || value.State != 0x1000 || value.Type != 0x1000000
                || Address(_target) < Address(value.BaseAddress)
                || Address(_target) - Address(value.BaseAddress) >= (ulong)value.RegionSize)
                throw new InvalidOperationException("The probe target is not the exact independently verified owned MEM_IMAGE page.");
            return value;
        }
        private static Exception Win32(string operation) => new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), operation);
    }
    private sealed class UnusedContext : IDisposable
    { public void Dispose() { } }

    private sealed class PlaybackImageObservation
    {
        public int FixedFunctionsCompared { get; init; }
        public ulong CoveredWindows { get; init; }
        public bool CaptureAndReverifyAccepted { get; init; }
        public bool ChangedPreimageRejected { get; init; }
        public bool RejectedPreimageUnchanged { get; init; }
        public bool WrongHashRejected { get; init; }
        public bool ForeignModuleRejected { get; init; }
        public bool OverlappingMemberRejected { get; init; }
        public bool PartialWindowRejected { get; init; }
        public bool ForeignByteOutsideWindowsRejected { get; init; }
        public bool NativeImageWritten => false;
        public bool StaleOwnerRejected { get; set; }
    }

    // Read-only integration against the actual already-published owned image.
    // Negative probes change managed expected arrays, never image bytes or page
    // protections. The existing duplicate mode supplies terminal managed Unknown.
    private static (NativePlaybackExecutableSeal Seal, PlaybackImageObservation Observation) CheckPlaybackImage(
        NativeEngineTransportFirstInstall owner, string path, string hash, Action<bool, string> check)
    {
        var module = new IntPtr(checked((long)owner.TargetModule));
        check(ReferenceEquals(NativeEngineTransportFirstInstall.RetainPlaybackImage(module, hash), owner),
            "Playback must retain the actual independently verified V5 target owner.");
        var file = File.ReadAllBytes(path);
        check(Hash(file) == hash, "Playback expected bytes require the parent-frozen owned target file.");
        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at, 4));
        ushort Half(int at) => BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at, 2));
        var nt = checked((int)Word(0x3c)); var optional = checked(nt + 24); var sections = checked(optional + Half(nt + 20));
        byte[] Disk(uint rva, int bytes)
        {
            for (var index = 0; index < Half(nt + 6); ++index)
            {
                var section = checked(sections + index * 40); var begin = Word(section + 12); var size = Word(section + 16);
                if (rva >= begin && (ulong)rva - begin + (ulong)bytes <= size)
                    return file.AsSpan(checked((int)(Word(section + 20) + rva - begin)), bytes).ToArray();
            }
            throw new InvalidOperationException("The playback test needs complete independent raw function bytes.");
        }
        void Reject(Action action, string reason)
        {
            var rejected = false;
            try { action(); } catch (UnsupportedSignatureException) { rejected = true; }
            check(rejected, reason);
        }
        var seal = NativePlaybackExecutableSeal.Capture(module, hash); seal.Verify(); seal.VerifyOperationModule();
        ulong covered = 0;
        for (var index = 0; index < 13; ++index)
        {
            var fact = NativeEngineTransportFixedFacts.Function(index);
            var expected = Disk(fact.Begin, checked((int)(fact.End - fact.Begin)));
            check(Hash(expected) == fact.BodySha256, "Each playback composition starts from its independently fixed full function.");
            var mask = owner.ComposeVerifiedPlaybackWindows(module, hash, fact.Begin, expected);
            var expectedMask = 1UL << index;
            for (var site = 0; site < 23; ++site)
                if (NativeEngineTransportFixedFacts.SiteFunction(site) == index) expectedMask |= 1UL << (13 + site);
            check(mask == expectedMask, "Playback must compose every and only the fixed windows in each function.");
            var actual = new byte[expected.Length]; Marshal.Copy(IntPtr.Add(module, checked((int)fact.Begin)), actual, 0, actual.Length);
            check(expected.AsSpan().SequenceEqual(actual), "Full playback expected functions must match actual retained V5 code, including MDrain.");
            covered |= mask;
        }
        check(covered == NativeEngineTransportPublicationLayout.AllWindows, "Playback integration must retain all 36 fixed V5 windows.");
        var drain = NativeEngineTransportFixedFacts.Function(8);
        check(drain.Begin == 0x447750 && drain.Name == "MDrain", "The playback regression must include the observed B17 MDrain location.");
        var original = Disk(drain.Begin, checked((int)(drain.End - drain.Begin)));
        var altered = (byte[])original.Clone();
        altered[checked((int)(NativeEngineTransportFixedFacts.Site(4).Physical - drain.Begin))] ^= 1;
        var before = (byte[])altered.Clone();
        Reject(() => owner.ComposeVerifiedPlaybackWindows(module, hash, drain.Begin, altered),
            "A later changed window preimage must be rejected before composing an earlier window.");
        check(altered.AsSpan().SequenceEqual(before), "Rejected playback preimages must not be partially changed.");
        var wrongHash = new string(hash[0] == '0' ? '1' : '0', 64);
        Reject(() => NativeEngineTransportFirstInstall.RetainPlaybackImage(module, wrongHash),
            "The retained V5 owner cannot authorize another playback file hash.");
        Reject(() => owner.ComposeVerifiedPlaybackWindows(owner.KernelModule, hash, drain.Begin, (byte[])original.Clone()),
            "A retained target owner cannot compose a different native image.");
        Reject(() => owner.VerifyPlaybackMembersDisjoint(module, hash,
                new[] { (IntPtr.Add(module, checked((int)drain.Begin)), original.Length) }),
            "Playback members cannot overlap any retained transport window.");
        owner.VerifyPlaybackMembersDisjoint(module, hash, new[] { (IntPtr.Add(module, 0x2000), 1) });
        Reject(() => owner.ComposeVerifiedPlaybackWindows(module, hash, drain.Begin + 1,
                original.AsSpan(1, checked((int)drain.PrefixBytes - 1)).ToArray()),
            "A transport window split at a playback section boundary must be rejected.");
        var foreign = (byte[])original.Clone(); foreign[^1] ^= 1;
        owner.ComposeVerifiedPlaybackWindows(module, hash, drain.Begin, foreign);
        check(!NativePlaybackExecutableSeal.RangeSeal.TryMatchSealedRange(IntPtr.Add(module, checked((int)drain.Begin)), foreign,
                out var first, out _) && first == foreign.Length - 1,
            "Full executable comparison must still reject a foreign byte outside the transport windows.");
        check(!owner.Unknown, "A caller's invalid expected array cannot invalidate a healthy native producer.");
        owner.VerifyReady(); seal.Verify();
        return (seal, new PlaybackImageObservation { FixedFunctionsCompared = 13, CoveredWindows = covered,
            CaptureAndReverifyAccepted = true, ChangedPreimageRejected = true, RejectedPreimageUnchanged = true,
            WrongHashRejected = true, ForeignModuleRejected = true, OverlappingMemberRejected = true,
            PartialWindowRejected = true, ForeignByteOutsideWindowsRejected = true });
    }

    // These probes alter only this process's parent-frozen generated owned
    // image. No logger/callback mutates an activity scope. Restore precedes
    // every subsequent full native Verify, including the host's final check.
    private sealed class ActivityTamperProbe
    {
        private readonly IntPtr _target, _module;
        private bool _attempted;
        public string Kind { get; }
        public ulong TargetAddress => Address(_target);
        public uint ExportFunctionSlotRva { get; }
        public uint ExportOriginalFunctionRva { get; }
        public ImportProbeFacts? Import { get; }
        public ulong ObservedImportValue { get; private set; }
        public ulong RestoredImportValue { get; private set; }
        public byte OriginalByte { get; }
        public byte ObservedByte { get; private set; }
        public byte RestoredByte { get; private set; }
        public uint OriginalProtection { get; private set; }
        public uint ObservedProtection { get; private set; }
        public uint RestoredProtection { get; private set; }
        public bool MutationApplied { get; private set; }
        public bool FlushCompleted { get; private set; }
        public bool RestoreAttempted { get; private set; }
        public bool RestoreVerified { get; private set; }
        public string? RestoreFailure { get; private set; }
        internal ActivityTamperProbe(string kind, IntPtr target, IntPtr module, byte original, uint exportSlot = 0, uint exportFunction = 0, ImportProbeFacts? import = null)
            => (Kind, _target, _module, OriginalByte, ExportFunctionSlotRva, ExportOriginalFunctionRva, Import)
                = (kind, target, module, original, exportSlot, exportFunction, import);
        internal void Apply()
        {
            var before = Query();
            var expectedProtection = Kind is "export" or "iat" or "ilt" ? 0x02u : 0x20u;
            if (before.Protect != expectedProtection || Marshal.ReadByte(_target) != OriginalByte)
                throw new InvalidOperationException("Activity tamper must start at the exact original RX owned source.");
            if (Kind is "source" or "export" or "iat" or "ilt")
            {
                if (!VirtualProtect(_target, 1, expectedProtection == 0x02 ? 0x04u : 0x40u, out var prior)) throw Win32("Activity byte VirtualProtect");
                OriginalProtection = prior; _attempted = true;
                try
                {
                    if (prior != expectedProtection) throw new InvalidOperationException("The activity source/export lost its exact original protection.");
                    Marshal.WriteByte(_target, unchecked((byte)(OriginalByte ^ 1)));
                    FlushCompleted = FlushInstructionCache(GetCurrentProcess(), _target, 1);
                    if (!FlushCompleted) throw Win32("Activity source FlushInstructionCache");
                }
                finally
                {
                    if (!VirtualProtect(_target, 1, prior, out _)) throw Win32("Activity mutation RX restore");
                }
            }
            else if (Kind == "protection")
            {
                // Readable, so the verifier can reject the actual protection
                // rather than relying on an access violation or changed byte.
                if (!VirtualProtect(_target, 1, 0x02, out var prior)) throw Win32("Activity protection VirtualProtect");
                OriginalProtection = prior; _attempted = true;
                if (prior != 0x20) throw new InvalidOperationException("The activity protection probe lost its original RX page.");
            }
            else throw new InvalidOperationException("Unknown actual activity probe kind.");
            ObservedByte = Marshal.ReadByte(_target); ObservedProtection = Query().Protect;
            if (Import is not null) ObservedImportValue = unchecked((ulong)Marshal.ReadInt64(_target));
            MutationApplied = Kind is "source" or "export" or "iat" or "ilt"
                ? ObservedByte == unchecked((byte)(OriginalByte ^ 1)) && ObservedByte != OriginalByte && ObservedProtection == expectedProtection && FlushCompleted
                : ObservedByte == OriginalByte && ObservedProtection == 0x02;
            if (Import is not null) MutationApplied &= ObservedImportValue == (Import.OriginalValue ^ 1UL);
            if (!MutationApplied) throw new InvalidOperationException("The real activity mutation was not independently observed.");
        }
        internal void Restore()
        {
            RestoreAttempted = true;
            try
            {
                var expectedProtection = Kind is "export" or "iat" or "ilt" ? 0x02u : 0x20u;
                if (!_attempted || OriginalProtection != expectedProtection) throw new InvalidOperationException("No exact original owned page is available for restoration.");
                _ = Query();
                if (!VirtualProtect(_target, 1, expectedProtection == 0x02 ? 0x04u : 0x40u, out _)) throw Win32("Activity final restore VirtualProtect");
                try
                {
                    Marshal.WriteByte(_target, OriginalByte); RestoredByte = Marshal.ReadByte(_target);
                    if (!FlushInstructionCache(GetCurrentProcess(), _target, 1)) throw Win32("Activity restore FlushInstructionCache");
                }
                finally
                {
                    if (!VirtualProtect(_target, 1, OriginalProtection, out _)) throw Win32("Activity final RX restore");
                }
                RestoredProtection = Query().Protect;
                RestoreVerified = RestoredByte == OriginalByte && Marshal.ReadByte(_target) == OriginalByte && RestoredProtection == expectedProtection;
                if (Import is not null)
                {
                    RestoredImportValue = unchecked((ulong)Marshal.ReadInt64(_target));
                    RestoreVerified &= RestoredImportValue == Import.OriginalValue;
                }
            }
            catch (Exception error) { RestoreFailure = error.ToString(); }
        }
        private ProbeMemory Query()
        {
            if (VirtualQuery(_target, out var value, (nuint)Marshal.SizeOf<ProbeMemory>()) != (nuint)Marshal.SizeOf<ProbeMemory>()
                || value.AllocationBase != _module || value.State != 0x1000 || value.Type != 0x1000000
                || Address(_target) < Address(value.BaseAddress)
                || Address(_target) - Address(value.BaseAddress) >= (ulong)value.RegionSize)
                throw new InvalidOperationException("The activity target is not the exact parent-frozen owned MEM_IMAGE page.");
            return value;
        }
        private static Exception Win32(string operation) => new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), operation);
    }

    private sealed record ImportProbeFacts(int Source, int Destination, string Symbol, uint LookupRva, uint IatRva,
        uint ExportRva, ulong SourceModule, ulong DestinationModule, uint SlotRva, ulong OriginalValue);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int Run(IntPtr input, int length)
    {
        var watch = Stopwatch.StartNew();
        var stages = new List<object>();
        void Stage(string name)
        {
            var elapsedMilliseconds = watch.ElapsedMilliseconds;
            stages.Add(new { name, elapsedMilliseconds });
            // Parent captures stderr even if the bounded child is terminated.
            // One fixed line per real stage, never a heartbeat/timer thread.
            try { Console.Error.WriteLine($"V5 CLR pid={Environment.ProcessId} elapsedMs={elapsedMilliseconds} stage={name}"); Console.Error.Flush(); } catch { }
        }
        Stage("entry");
        var checks = 0; var output = ""; var mode = ""; var factsSha256 = ""; var publicationSha256 = "";
        NativePlaybackExecutableSeal? playbackSeal = null;
        PlaybackImageObservation? playbackImage = null;
        var failures = new List<string>();
        var before = Array.Empty<FamilyObservation>(); var after = Array.Empty<FamilyObservation>();
        var allFamiliesAttached = false; var coordinatorReused = false; var countersUnchanged = false;
        var publicationUnchanged = false; var rootsRetained = false; var duplicateRejected = false; var unknownRetained = false;
        LiveTamperLogger? liveTamper = null;
        var liveTamperRejected = false; var liveTamperOwnerVerifyRejected = false;
        var liveTamperReacquireOwnerRejected = false; var liveTamperReacquireCoordinatorRejected = false;
        var nativeReadyRetained = false;
        var liveTamperRejection = "";
        object? activityCost = null;
        ActivityTamperProbe? activityTamper = null;
        var activityTamperRejected = false; var activityDetectionBeforeFullVerify = false;
        var activityOwnerVerifyRejected = false; var activityReacquireRejected = false;
        void Check(bool condition, string reason) { ++checks; if (!condition) throw new InvalidOperationException(reason); }
        void Write(bool passed)
        {
            if (!Path.IsPathFullyQualified(output)) return;
            Stage(passed ? "evidence.write-pass.begin" : "evidence.write-failure.begin");
            var evidence = new { schemaVersion = 1, kind = "engine-transport-owned-managed-adoption", passed,
                pid = Environment.ProcessId, mode, checks, allFamiliesAttached, playbackImage,
                attachedFamilies = after.Count(row => row.Attached == 1), coordinatorReused, countersUnchanged,
                publicationUnchanged, rootsRetained, duplicateRejected, unknownRetained,
                liveTamper, liveTamperRejected, liveTamperOwnerVerifyRejected,
                liveTamperReacquireOwnerRejected, liveTamperReacquireCoordinatorRejected, nativeReadyRetained, liveTamperRejection,
                activityCost, activityTamper, activityTamperRejected, activityDetectionBeforeFullVerify,
                activityOwnerVerifyRejected, activityReacquireRejected,
                factsSha256, publicationSha256, familiesBefore = before, familiesAfter = after, failures,
                elapsedMilliseconds = watch.ElapsedMilliseconds, stages,
                gameBodiesInvoked = false, capturedCompletionExercised = false,
                productionAdmissionGranted = false, gameExecuted = false, installed = false };
            using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(stream, evidence, JsonOptions);
            stream.Flush();
            Stage(passed ? "evidence.write-pass.end" : "evidence.write-failure.end");
        }
        try
        {
            if (input == IntPtr.Zero || length is <= 0 or > 1048576) return 2;
            var bytes = new byte[length]; Marshal.Copy(input, bytes, 0, length);
            using var arguments = JsonDocument.Parse(bytes); var args = arguments.RootElement;
            Stage("arguments.parsed");
            string Arg(string name) => args.GetProperty(name).GetString() ?? throw new InvalidOperationException(name);
            output = Arg("output"); mode = Arg("mode"); factsSha256 = Digest(Arg("factsSha256"));
            Check(args.GetProperty("schemaVersion").GetInt32() == 1
                && mode is "adopt" or "duplicate" or "live-tamper" or "activity-cost" or "activity-source-tamper" or "activity-protection-tamper" or "activity-export-tamper" or "activity-iat-tamper" or "activity-ilt-tamper"
                && Path.IsPathFullyQualified(output) && !File.Exists(output), "Exact fresh managed fixture arguments are required.");
            Check(Interlocked.Exchange(ref _invoked, 1) == 0, "The unmanaged managed-adoption entry is one attempt per PID.");
            Check(OperatingSystem.IsWindows() && IntPtr.Size == 8 && !NativeEngineTransportFirstInstall.ProductionStartupAdmissionKnown,
                "The fixture requires Windows x64 and must retain the closed production admission gate.");
            var factsPath = Arg("factsPath");
            Check(Path.IsPathFullyQualified(factsPath) && !string.Equals(Path.GetFullPath(factsPath), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase),
                "Independent absolute facts and evidence paths are required.");
            var heldFacts = new FileStream(factsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Roots.Add(heldFacts);
            Check(heldFacts.Length is > 0 and <= 1048576, "Frozen owned build facts exceed the fixture bound.");
            var factsBytes = new byte[checked((int)heldFacts.Length)]; heldFacts.ReadExactly(factsBytes);
            Check(Hash(factsBytes) == factsSha256, "Parent-frozen facts SHA256 differs; publication values cannot supply expected identity.");
            using var factsDocument = JsonDocument.Parse(factsBytes); var facts = factsDocument.RootElement;
            Stage("facts.read-hash-parse.end");
            Check(facts.GetProperty("schemaVersion").GetInt32() == 1
                && facts.GetProperty("kind").GetString() == "engine-transport-owned-managed-build-facts"
                && facts.GetProperty("nativeProfile").GetInt32() == 1
                && facts.GetProperty("sourceHashesUnchanged").GetBoolean()
                && !facts.GetProperty("productionAdmissionGranted").GetBoolean()
                && !facts.GetProperty("gameExecuted").GetBoolean() && !facts.GetProperty("installed").GetBoolean(),
                "Frozen facts must identify the independent owned build with no game admission claim.");
            var images = facts.GetProperty("images").EnumerateArray().ToArray();
            var rvas = facts.GetProperty("functionRvas").EnumerateArray().Select(value => value.GetUInt32()).ToArray();
            var functions = facts.GetProperty("functions").EnumerateArray().ToArray();
            Check(images.Length == 5 && rvas.Length == 13 && functions.Length == 13
                && facts.GetProperty("fixedContractSha256").GetString() == NativeEngineTransportFixedFacts.FixedContractSha256,
                "The independently frozen five-image/thirteen-function contract is incomplete.");
            var paths = new string[5]; var hashes = new string[5];
            for (var index = 0; index < 5; ++index)
            {
                var image = images[index]; paths[index] = image.GetProperty("path").GetString()!;
                hashes[index] = Digest(image.GetProperty("sha256").GetString()!);
                Check(image.GetProperty("index").GetInt32() == index && Path.IsPathFullyQualified(paths[index])
                    && Path.GetFileName(paths[index]) == image.GetProperty("module").GetString(), "Frozen image role/path membership differs.");
            }
            for (var index = 0; index < 13; ++index)
            {
                var fact = NativeEngineTransportFixedFacts.Function(index); var row = functions[index];
                Check(rvas[index] == fact.Begin && row.GetProperty("index").GetInt32() == index
                    && row.GetProperty("beginRva").GetUInt32() == fact.Begin && row.GetProperty("endRva").GetUInt32() == fact.End
                    && row.GetProperty("prefixBytes").GetUInt32() == fact.PrefixBytes
                    && row.GetProperty("bodySha256").GetString() == fact.BodySha256,
                    "A frozen function RVA/body/prefix differs from the independently compiled fixed contract.");
            }

            // No fabricated publication enters RequireOwnedPublished. It reads
            // the actual already-loaded publisher and independently checks disk,
            // memory, exports, graph, registered unwind data and all 12 families.
            Stage("facts.fixed-contract.end");
            Stage("acquire-owned-publication.begin");
            var owner = NativeEngineTransportFirstInstall.RequireOwnedPublished(paths, hashes);
            Stage("acquire-owned-publication.end");
            Check(owner.OwnedPublication && !owner.Unknown, "Actual sealed owned V5 publication acquisition failed.");
            var bootstrap = GetModuleHandle(paths[0]);
            Check(bootstrap != IntPtr.Zero && owner.KernelModule == GetModuleHandle(paths[1]), "Native modules must already be loaded in this PID.");
            var read = Export<ReadNative>(bootstrap, "RizEngineTransport_GetInfoV5");
            var verify = Export<VerifyNative>(bootstrap, "RizEngineTransport_VerifyV5");
            byte[] ReadStable()
            {
                var first = ReadPublication(read); Check(verify() == 0, "Actual native V5 Verify refused its publication.");
                var second = ReadPublication(read); Check(first.AsSpan().SequenceEqual(second), "All 91376 native bytes must remain equal across GetInfo/Verify/GetInfo.");
                return first;
            }
            Stage("native-stable-snapshot.begin");
            var originalPublication = ReadStable(); publicationSha256 = Hash(originalPublication);
            Stage("native-stable-snapshot.end");
            var wire = NativeEngineTransportPublication.Decode(originalPublication).CopyWire();
            Check(wire.Profile == 1 && wire.State == 4 && wire.ProcessId == unchecked((uint)Environment.ProcessId)
                && wire.MainThread == GetCurrentThreadId() && wire.ConfiguredFamilies == 4095 && wire.RetainedFamilies == 4095,
                "Real native startup ownership must belong to this PID, caller thread and all twelve families.");
            Check(HashBytes(wire.DependencyGraphSha256) == facts.GetProperty("dependencyGraphSha256").GetString(),
                "Actual complete native graph differs from independently resolved final-five-image ILT/IAT/EAT tuples.");
            for (var index = 0; index < 5; ++index)
                Check(HashBytes(wire.Modules[index].Sha256) == hashes[index], "Published image identity differs from the independent final PE hash.");
            for (var index = 0; index < 13; ++index)
                Check(wire.Typed[index].Rva == rvas[index] && wire.Typed[index].Target == owner.TargetModule + rvas[index],
                    "Actual native typed target differs from the independent final fixed RVA.");

            var nodeInfo = Export<NodeInfo>(owner.KernelModule, "RizEngineNodeContext_GetInfo");
            var inlineInfo = Export<InlineInfo>(owner.KernelModule, "RizEngineNodeContext_GetInlineInfo");
            var schedulerInfo = Export<SchedulerInfo>(owner.KernelModule, "RizEngineSchedulerContext_GetInfo");
            var waitInfo = Export<WaitInfo>(owner.KernelModule, "RizEngineWaitContext_GetInfo");
            FamilyObservation[] Observe()
            {
                var rows = new List<FamilyObservation>();
                for (var family = 0; family < 12; ++family)
                {
                    var receipt = wire.Families[family];
                    Check(receipt.ConfigureAttempts == 1 && receipt.ConfigureSucceeded == 1 && receipt.RetainedClaim == 1
                        && receipt.HelperUnknown == 0 && receipt.LastWin32Error == 0, "Exactly one successful retained native Configure is required.");
                    if (family < 4)
                    {
                        var value = new NativeEngineNodeInformation { Version = 1, Size = 112 };
                        Check(nodeInfo(checked((uint)family), receipt.Slot, ref value) == 0
                            && value.Kind == family && value.Slot == receipt.Slot && value.Epoch == receipt.Epoch
                            && Address(value.Target) == receipt.Target && Address(value.Original) == receipt.Original && Address(value.Entry) == receipt.Entry
                            && value.Unknown == 0 && value.Attached <= 1 && value.InFlight == 0 && value.Entered == value.Exited,
                            "Actual Node information differs or is in flight.");
                        rows.Add(ObserveRow(family, ((NativeEngineNodeKind)family).ToString(), value, nameof(value.Attached), value.Attached, value.Unknown, value.Epoch,
                            value.Entered, value.Exited, value.InFlight, value.EnterFaults, value.ExitFaults, value.SnapshotFaults, value.OutputFaults));
                    }
                    else if (family == 4)
                    {
                        var value = new NativeEngineInlineInformation { Version = 1, Size = 160 };
                        Check(inlineInfo(receipt.Slot, ref value) == 0 && value.Slot == receipt.Slot && value.Epoch == receipt.Epoch
                            && value.Configured == 1 && value.Attached <= 1 && value.Unknown == 0 && value.StartsInFlight == 0
                            && value.ProbesInFlight == 0 && value.StartsEntered == value.StartsExited, "Actual Inline information differs or is in flight.");
                        rows.Add(ObserveRow(family, "Inline", value, nameof(value.Attached), value.Attached, value.Unknown, value.Epoch,
                            value.StartsEntered, value.StartsExited, value.StartsInFlight, value.Begins, value.EndsFree, value.EndsRetained,
                            value.ProbesInFlight, value.Faults, value.SnapshotFaults, value.OrphanProbes, value.UnmatchedStarts, value.Events));
                    }
                    else if (family < 11)
                    {
                        var kind = checked((uint)family - 5); var value = new NativeEngineSchedulerInformation { Version = 1, Size = 144 };
                        Check(schedulerInfo(kind, receipt.Slot, ref value) == 0 && value.Kind == kind && value.Slot == receipt.Slot && value.Epoch == receipt.Epoch
                            && value.Configured == 1 && value.Attached <= 1 && value.Unknown == 0 && value.InFlight == 0
                            && value.RowsInFlight == 0 && value.Entered == value.Exited, "Actual Scheduler information differs or is in flight.");
                        rows.Add(ObserveRow(family, ((NativeEngineSchedulerKind)kind).ToString(), value, nameof(value.Attached), value.Attached, value.Unknown, value.Epoch,
                            value.Entered, value.Exited, value.InFlight, value.Faults, value.SnapshotFaults, value.RowBegins, value.RowEnds, value.RowsInFlight, value.UnmatchedRows));
                    }
                    else
                    {
                        var value = new NativeEngineWaitInformation { Version = 1, Size = 176 };
                        Check(waitInfo(receipt.Slot, ref value) == 0 && value.Slot == receipt.Slot && value.Epoch == receipt.Epoch
                            && value.Configured == 1 && value.Attached <= 1 && value.Unknown == 0 && value.InFlight == 0 && value.Entered == value.Exited,
                            "Actual Wait information differs or is in flight.");
                        rows.Add(ObserveRow(family, "Wait", value, nameof(value.Attached), value.Attached, value.Unknown, value.Epoch,
                            value.Entered, value.Exited, value.InFlight, value.Faults, value.SnapshotFaults, value.Phases, value.Unmatched));
                    }
                }
                return rows.ToArray();
            }
            before = Observe(); Check(before.All(row => row.Attached == 0), "All twelve native families must begin without a CLR attachment.");
            Stage("family-baseline.end");
            if (mode is "adopt" or "duplicate")
            {
                Stage("playback-image.read-only-checks.begin");
                (playbackSeal, playbackImage) = CheckPlaybackImage(owner, paths[2], hashes[2], Check);
                Roots.Add(playbackSeal);
                Stage("playback-image.read-only-checks.end");
            }
            if (mode == "live-tamper")
            {
                Stage("live-tamper.begin");
                var target = new IntPtr(checked((long)wire.Typed[0].Target));
                Check(wire.Typed[0].Target == owner.TargetModule + rvas[0]
                    && wire.Windows[0].Source == wire.Typed[0].Target && wire.Windows[0].PhysicalBytes >= 5,
                    "The negative probe must use the exact generated owned first typed source window.");
                liveTamper = new(target, new IntPtr(checked((long)owner.TargetModule)), wire.Windows[0].PatchBytes[0], Stage);
                Roots.AddRange(new object[] { owner, liveTamper, read, verify, nodeInfo, inlineInfo, schedulerInfo, waitInfo });
                try
                {
                    try { _ = NativeEngineRuntimeCoordinator.AcquireOwnedPublished(owner, liveTamper); }
                    catch (UnsupportedSignatureException error) { liveTamperRejected = true; liveTamperRejection = error.Message; }
                }
                finally { liveTamper.Restore(); }
                Check(liveTamper.TriggerCount == 1 && liveTamper.MutationApplied && liveTamper.AlterationObserved
                    && liveTamper.ProtectionRestoredBeforeInner && liveTamper.FlushCompleted && liveTamper.Failure is null,
                    "The real logger boundary must expose one actual changed source byte, restored RX protection and flushed code to the next inner verifier.");
                Check(liveTamper.RestoreAttempted && liveTamper.Restored && liveTamper.RestoreVerified && liveTamper.RestoreFailure is null,
                    "The owned image byte/protection must be restored and flushed even when nested verification refuses adoption.");
                Check(liveTamperRejected && owner.Unknown,
                    "The next actual inner verifier must detect temporary owned source tampering and make managed ownership permanently Unknown.");
                try { owner.VerifyReady(); } catch (UnsupportedSignatureException) { liveTamperOwnerVerifyRejected = true; }
                try { _ = NativeEngineTransportFirstInstall.RequireOwnedPublished(paths, hashes); }
                catch (UnsupportedSignatureException) { liveTamperReacquireOwnerRejected = true; }
                try { _ = NativeEngineRuntimeCoordinator.AcquireOwnedPublished(owner, liveTamper); }
                catch (UnsupportedSignatureException) { liveTamperReacquireCoordinatorRejected = true; }
                unknownRetained = owner.Unknown && liveTamperOwnerVerifyRejected
                    && liveTamperReacquireOwnerRejected && liveTamperReacquireCoordinatorRejected;
                Check(unknownRetained, "Restoring the physical byte must not restore managed admission or permit a later acquisition.");
                after = Observe();
                allFamiliesAttached = after.All(row => row.Attached == 1);
                countersUnchanged = before.Zip(after).All(pair => pair.First.InformationSha256 == pair.Second.InformationSha256);
                Check(after.Length == 12 && after.All(row => row.Attached == 0 && row.Unknown == 0) && !allFamiliesAttached && countersUnchanged,
                    "Failure before BeginFullAdoption must leave all twelve actual native Attach flags and full information records unchanged.");
                publicationUnchanged = originalPublication.AsSpan().SequenceEqual(ReadStable());
                var retainedWire = NativeEngineTransportPublication.Decode(ReadPublication(read)).CopyWire();
                nativeReadyRetained = publicationUnchanged && retainedWire.Profile == 1 && retainedWire.State == 4
                    && retainedWire.Failure == 0 && retainedWire.ConfiguredFamilies == 4095 && retainedWire.BoundFamilies == 4095
                    && retainedWire.RetainedFamilies == 4095 && retainedWire.CommittedWindows == NativeEngineTransportPublicationLayout.AllWindows
                    && retainedWire.SourceWriteAttempts == 36 && retainedWire.SourceWriteCompletions == 36
                    && retainedWire.RuntimeFunctionCount == 67 && retainedWire.ResourceCount == 1 && retainedWire.RetainedResources == 1
                    && retainedWire.Resources[0].RootRetained == 1 && retainedWire.Resources[0].TableRegistered == 1;
                rootsRetained = nativeReadyRetained && Roots.Contains(owner) && Roots.Contains(liveTamper);
                Check(rootsRetained && !NativeEngineTransportFirstInstall.ProductionStartupAdmissionKnown,
                    "The managed refusal must retain native Ready/12 Configure/36 writes/67 RF and all claims without Abort or production admission.");
                Stage("live-tamper.end"); Write(true); return 0;
            }
            var log = new ProgressLogger(Stage);
            Stage("acquire-coordinator.begin");
            var coordinator = NativeEngineRuntimeCoordinator.AcquireOwnedPublished(owner, log);
            Stage("acquire-coordinator.end");
            Roots.AddRange(new object[] { owner, coordinator, log, read, verify, nodeInfo, inlineInfo, schedulerInfo, waitInfo });
            Stage("post-adoption.owner-verify.begin");
            owner.VerifyReady();
            Stage("post-adoption.owner-verify.end");
            var activity = coordinator.ReadActivity();
            Stage("post-adoption.activity.end");
            Check(activity.Known && activity.InFlight == 0 && !coordinator.Unknown && coordinator.InlineAvailable,
                "The exact production composition must adopt all family transports and remain known/quiescent.");
            coordinator.RequireInlineObservation(); _ = coordinator.RequireSchedulerObservation(); _ = coordinator.RequireWaitObservation();
            Check(Address(coordinator.RequirePublishedWaitImage()) == owner.TargetModule,
                "All attached family dispatchers must refer to the same exact published owned image.");
            Stage("post-adoption.full-family-image.end");
            Stage("sharing.reacquire-owner.begin");
            var repeatedOwner = NativeEngineTransportFirstInstall.RequireOwnedPublished(paths, hashes);
            Stage("sharing.reacquire-owner.end");
            var repeatedCoordinator = NativeEngineRuntimeCoordinator.AcquireOwnedPublished(repeatedOwner, log);
            coordinatorReused = ReferenceEquals(owner, repeatedOwner) && ReferenceEquals(coordinator, repeatedCoordinator);
            Check(coordinatorReused, "Repeated sharing must reuse the same sealed owner and coordinator without a second Attach.");
            Stage("sharing.reacquire-coordinator.end");

            // Logical membership is reversible; permanent physical callbacks and
            // retained source/unwind responsibility must survive its retirement.
            var unexpectedCallbacks = 0;
            using (var subscription = coordinator.Bind<UnusedContext>(_ => { ++unexpectedCallbacks; return new(); },
                (_, _, _) => ++unexpectedCallbacks, (_, _) => ++unexpectedCallbacks)) { }
            Stage("logical-binding.disposed");
            Stage("gc.begin");
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Stage("gc.end");
            Stage("post-gc.owner-verify.begin");
            owner.VerifyReady();
            Stage("post-gc.owner-verify.end");
            after = Observe();
            Stage("family-post-adoption.end");
            allFamiliesAttached = after.Length == 12 && after.All(row => row.Attached == 1 && row.Unknown == 0);
            countersUnchanged = before.Zip(after).All(pair => pair.First.Counters.SequenceEqual(pair.Second.Counters)
                && pair.First.WithoutAttachmentSha256 == pair.Second.WithoutAttachmentSha256);
            Check(allFamiliesAttached && countersUnchanged && unexpectedCallbacks == 0,
                "Actual attachment may change only Attached; all twelve complete information records and activity counts must otherwise remain equal.");
            rootsRetained = wire.RetainedFamilies == 4095 && wire.RetainedResources == 1
                && Roots.Contains(owner) && Roots.Contains(coordinator) && !coordinator.Unknown;
            Check(rootsRetained, "Logical unsubscribe and collection must retain every permanent callback/publication/resource responsibility.");
            Stage("post-adoption.native-stable.begin");
            publicationUnchanged = originalPublication.AsSpan().SequenceEqual(ReadStable());
            Stage("post-adoption.native-stable.end");
            Check(publicationUnchanged, "Managed family adoption must preserve the complete native publication receipt.");
            if (mode == "activity-cost")
            {
                Stage("activity-cost.begin");
                // Real coordinator/bridges, same PID, one prewarm followed by
                // exactly twenty measured synchronous reads. No log, Check,
                // sorting or JSON allocation occurs inside a timed interval.
                var warm = coordinator.ReadActivity();
                Check(warm.Known && warm.InFlight == 0, "The actual activity cost prewarm must be known and idle.");
                var microseconds = new double[20]; var allocatedBytes = new long[20]; var knownIdle = new bool[20];
                for (var index = 0; index < microseconds.Length; ++index)
                {
                    var bytesBefore = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
                    var measured = coordinator.ReadActivity();
                    var end = Stopwatch.GetTimestamp(); var bytesAfter = GC.GetAllocatedBytesForCurrentThread();
                    microseconds[index] = (end - start) * (1000000.0 / Stopwatch.Frequency);
                    allocatedBytes[index] = bytesAfter - bytesBefore; knownIdle[index] = measured.Known && measured.InFlight == 0;
                }
                Check(knownIdle.All(value => value) && microseconds.All(value => double.IsFinite(value) && value >= 0)
                    && allocatedBytes.All(value => value >= 0) && !owner.Unknown && !coordinator.Unknown,
                    "All twenty real activity measurements must remain known and idle.");
                var sorted = microseconds.Order().ToArray();
                double Percentile(double fraction) => sorted[checked((int)Math.Ceiling(fraction * sorted.Length)) - 1];
                activityCost = new { samples = 20, stopwatchFrequency = Stopwatch.Frequency,
                    microseconds, allocatedBytes, allKnownIdle = true, percentileMethod = "nearest-rank",
                    p50Microseconds = Percentile(0.50), p95Microseconds = Percentile(0.95), p99Microseconds = Percentile(0.99),
                    totalAllocatedBytes = allocatedBytes.Sum(), meanAllocatedBytes = allocatedBytes.Average(),
                    currentThreadOnly = true, externalSuccessCallbacks = false, fullNativeVerifyInsideSamples = false };
                var retained = Observe();
                Check(retained.Zip(after).All(pair => pair.First.InformationSha256 == pair.Second.InformationSha256),
                    "Twenty actual reads must preserve every full native family record.");
                after = retained;
                publicationUnchanged = originalPublication.AsSpan().SequenceEqual(ReadStable());
                Check(publicationUnchanged, "Full native verification after measurement must confirm the unchanged publication.");
                Stage("activity-cost.end");
            }
            if (mode is "activity-source-tamper" or "activity-protection-tamper" or "activity-export-tamper" or "activity-iat-tamper" or "activity-ilt-tamper")
            {
                Stage("activity-tamper.begin");
                Check(wire.Typed[0].Target == owner.TargetModule + rvas[0] && wire.Windows[0].Source == wire.Typed[0].Target
                    && wire.Windows[0].PhysicalBytes >= 5, "Actual activity tamper must target the exact owned first typed source patch.");
                if (mode == "activity-export-tamper")
                {
                    var kernelFile = File.ReadAllBytes(paths[1]);
                    Check(Hash(kernelFile) == hashes[1], "The export probe must independently retain the exact parent-frozen Kernel file hash.");
                    var export = OwnedExportSlot(kernelFile, owner.KernelModule, "RizEngineNodeContext_GetLimit");
                    var slot = new IntPtr(checked(owner.KernelModule.ToInt64() + export.Slot));
                    Check(unchecked((uint)Marshal.ReadInt32(slot)) == export.Function,
                        "The actual owned Kernel EAT slot must equal its independently parsed held function RVA before mutation.");
                    activityTamper = new("export", slot, owner.KernelModule, unchecked((byte)export.Function), export.Slot, export.Function);
                }
                else if (mode is "activity-iat-tamper" or "activity-ilt-tamper")
                {
                    var edge = facts.GetProperty("edges").EnumerateArray().Single(value => value.GetProperty("index").GetInt32() == 0);
                    Check(edge.GetProperty("source").GetInt32() == 3 && edge.GetProperty("destination").GetInt32() == 2
                        && edge.GetProperty("symbol").GetString() == "UnityMain2", "The import probe requires the independently resolved host-to-owned-target edge.");
                    var hostFile = File.ReadAllBytes(paths[3]);
                    Check(Hash(hostFile) == hashes[3], "The actual import probe must retain the independent host PE hash.");
                    var import = OwnedImportSlot(hostFile, Path.GetFileName(paths[2]), "UnityMain2");
                    var exportRva = edge.GetProperty("exportRva").GetUInt32();
                    Check(import.Lookup == edge.GetProperty("lookupRva").GetUInt32() && import.Iat == edge.GetProperty("iatRva").GetUInt32()
                        && Address(NativeLibrary.GetExport(new IntPtr(checked((long)wire.TargetModule)), "UnityMain2")) == wire.TargetModule + exportRva,
                        "Independently parsed held import slots and actual export must equal the frozen parent graph.");
                    var expectedIat = wire.TargetModule + exportRva;
                    Check(unchecked((ulong)Marshal.ReadInt64(new IntPtr(checked((long)(wire.ExecutableModule + import.Lookup))))) == import.NameRva
                        && unchecked((ulong)Marshal.ReadInt64(new IntPtr(checked((long)(wire.ExecutableModule + import.Iat))))) == expectedIat,
                        "Both actual import tuples must equal their independently held/resolved values before alteration.");
                    var isIat = mode == "activity-iat-tamper"; var slotRva = isIat ? import.Iat : import.Lookup;
                    var originalValue = isIat ? expectedIat : import.NameRva;
                    var probeFacts = new ImportProbeFacts(3, 2, "UnityMain2", import.Lookup, import.Iat, exportRva,
                        wire.ExecutableModule, wire.TargetModule, slotRva, originalValue);
                    activityTamper = new(isIat ? "iat" : "ilt", new IntPtr(checked((long)(wire.ExecutableModule + slotRva))),
                        new IntPtr(checked((long)wire.ExecutableModule)), unchecked((byte)originalValue), import: probeFacts);
                }
                else activityTamper = new(mode == "activity-source-tamper" ? "source" : "protection",
                    new IntPtr(checked((long)wire.Typed[0].Target)), new IntPtr(checked((long)owner.TargetModule)), wire.Windows[0].PatchBytes[0]);
                Roots.Add(activityTamper);
                try
                {
                    activityTamper.Apply();
                    // No native Verify, logger or owner.VerifyReady intervenes.
                    // This next real aggregate activity must detect the fault.
                    var refused = coordinator.ReadActivity();
                    activityTamperRejected = !refused.Known && owner.Unknown && coordinator.Unknown;
                    activityDetectionBeforeFullVerify = activityTamperRejected && activityTamper.MutationApplied;
                }
                finally { activityTamper.Restore(); }
                Check(activityTamper.MutationApplied && activityTamperRejected && activityDetectionBeforeFullVerify,
                    "The next real activity must reject the actual changed source/protection before any full native Verify.");
                Check(activityTamper.RestoreAttempted && activityTamper.RestoreVerified && activityTamper.RestoreFailure is null,
                    "Actual activity tamper must restore the original byte, RX protection and instruction cache before native verification.");
                try { owner.VerifyReady(); } catch (UnsupportedSignatureException) { activityOwnerVerifyRejected = true; }
                try { _ = NativeEngineRuntimeCoordinator.AcquireOwnedPublished(owner, log); }
                catch (UnsupportedSignatureException) { activityReacquireRejected = true; }
                unknownRetained = owner.Unknown && coordinator.Unknown && activityOwnerVerifyRejected && activityReacquireRejected;
                Check(unknownRetained, "Physical restoration must never revive managed activity or acquisition.");
                var retained = Observe();
                Check(retained.Zip(after).All(pair => pair.First.InformationSha256 == pair.Second.InformationSha256)
                    && retained.All(row => row.Attached == 1 && row.Unknown == 0), "Activity refusal must retain all twelve real physical attachments and counters.");
                after = retained;
                publicationUnchanged = originalPublication.AsSpan().SequenceEqual(ReadStable());
                var retainedWire = NativeEngineTransportPublication.Decode(ReadPublication(read)).CopyWire();
                nativeReadyRetained = publicationUnchanged && retainedWire.Profile == 1 && retainedWire.State == 4 && retainedWire.Failure == 0
                    && retainedWire.ConfiguredFamilies == 4095 && retainedWire.BoundFamilies == 4095 && retainedWire.RetainedFamilies == 4095
                    && retainedWire.CommittedWindows == NativeEngineTransportPublicationLayout.AllWindows
                    && retainedWire.SourceWriteAttempts == 36 && retainedWire.SourceWriteCompletions == 36
                    && retainedWire.RuntimeFunctionCount == 67 && retainedWire.ResourceCount == 1 && retainedWire.RetainedResources == 1
                    && retainedWire.Resources[0].RootRetained == 1 && retainedWire.Resources[0].TableRegistered == 1;
                rootsRetained = nativeReadyRetained && Roots.Contains(owner) && Roots.Contains(coordinator) && Roots.Contains(activityTamper);
                Check(rootsRetained, "The activity fault must retain native Ready, source/RF resources and all permanent managed roots.");
                Stage("activity-tamper.end");
            }
            else if (mode == "duplicate")
            {
                Stage("duplicate.begin");
                try { owner.BeginFullAdoption(); } catch (UnsupportedSignatureException) { duplicateRejected = true; }
                Check(duplicateRejected && owner.Unknown && coordinator.Unknown, "A second whole-family attempt must be refused and conservatively retain managed Unknown.");
                var verifyRejected = false; try { owner.VerifyReady(); } catch (UnsupportedSignatureException) { verifyRejected = true; }
                var reacquireRejected = false;
                try { _ = NativeEngineRuntimeCoordinator.AcquireOwnedPublished(owner, log); } catch (UnsupportedSignatureException) { reacquireRejected = true; }
                unknownRetained = verifyRejected && reacquireRejected && owner.Unknown && coordinator.Unknown;
                Check(unknownRetained, "Terminal duplicate refusal must not allow another managed ownership grant.");
                if (playbackSeal is not null && playbackImage is not null)
                {
                    var retainedRejected = false; var sealRejected = false; var operationRejected = false;
                    try { _ = NativeEngineTransportFirstInstall.RetainPlaybackImage(playbackSeal.Module, hashes[2]); }
                    catch (UnsupportedSignatureException) { retainedRejected = true; }
                    try { playbackSeal.Verify(); } catch (UnsupportedSignatureException) { sealRejected = true; }
                    try { playbackSeal.VerifyOperationModule(); } catch (UnsupportedSignatureException) { operationRejected = true; }
                    playbackImage.StaleOwnerRejected = retainedRejected && sealRejected && operationRejected;
                    Check(playbackImage.StaleOwnerRejected, "Unchanged native bytes cannot restore playback authority after its producer becomes Unknown.");
                }
                var retained = Observe();
                Check(retained.Zip(after).All(pair => pair.First.InformationSha256 == pair.Second.InformationSha256)
                    && retained.All(row => row.Attached == 1 && row.Unknown == 0), "Managed refusal must retain the exact twelve physical native attachments.");
                after = retained;
                publicationUnchanged = originalPublication.AsSpan().SequenceEqual(ReadStable());
                Check(publicationUnchanged && Roots.Contains(owner) && Roots.Contains(coordinator),
                    "Managed Unknown must retain native Ready bytes and roots without native Abort, rollback or detach.");
                Stage("duplicate.end");
            }
            else Check(!owner.Unknown && !coordinator.Unknown, "Normal adoption must remain usable after sharing and logical detach.");
            Check(!NativeEngineTransportFirstInstall.ProductionStartupAdmissionKnown, "Owned evidence must not open production admission.");
            Write(true); return 0;
        }
        catch (Exception error)
        {
            Stage("failure: " + error.GetType().Name);
            failures.Add(error.ToString());
            try { Write(false); } catch { /* Preserve any earlier evidence; the host also records the failing exit. */ }
            return 1;
        }
    }

    private static FamilyObservation ObserveRow<T>(int family, string name, T value, string attachedField,
        uint attached, uint unknown, ulong epoch, params ulong[] counters) where T : struct
    {
        var size = Marshal.SizeOf<T>(); var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, buffer, size); Marshal.StructureToPtr(value, buffer, false);
            var bytes = new byte[size]; Marshal.Copy(buffer, bytes, 0, size);
            var originalHash = Hash(bytes); var offset = checked((int)Marshal.OffsetOf<T>(attachedField));
            bytes.AsSpan(offset, 4).Clear();
            return new(family, name, attached, unknown, epoch, counters, originalHash, Hash(bytes));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static (uint Lookup, uint Iat, ulong NameRva) OwnedImportSlot(byte[] file, string expectedDll, string expectedSymbol)
    {
        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at, 4));
        ushort Half(int at) => BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at, 2));
        var nt = checked((int)Word(0x3c)); var optional = checked(nt + 24);
        if (Half(0) != 0x5a4d || Word(nt) != 0x4550 || Half(nt + 4) != 0x8664 || Half(optional) != 0x20b)
            throw new InvalidOperationException("The import probe requires the independently frozen AMD64 host PE.");
        var headers = Word(optional + 60); var sections = checked(optional + Half(nt + 20));
        int Raw(uint rva, int length)
        {
            if (length <= 0) throw new InvalidOperationException("Invalid bounded import file extent.");
            if ((ulong)rva + (ulong)length <= headers) { _ = file.AsSpan(checked((int)rva), length); return checked((int)rva); }
            for (var index = 0; index < Half(nt + 6); ++index)
            {
                var section = checked(sections + index * 40); var begin = Word(section + 12); var size = Word(section + 16);
                if (rva < begin || (ulong)rva - begin + (ulong)length > size) continue;
                var raw = checked((int)(Word(section + 20) + rva - begin)); _ = file.AsSpan(raw, length); return raw;
            }
            throw new InvalidOperationException("The import probe range is outside its held file-backed image.");
        }
        string Name(uint rva)
        {
            var result = new System.Text.StringBuilder();
            for (uint index = 0; index < 512; ++index)
            {
                var value = file[Raw(checked(rva + index), 1)];
                if (value == 0) return result.ToString();
                if (value is < 0x20 or > 0x7e) throw new InvalidOperationException("The import name is not bounded ASCII.");
                result.Append((char)value);
            }
            throw new InvalidOperationException("The import name lacks its bounded terminator.");
        }
        var importRva = Word(optional + 120); var importBytes = Word(optional + 124);
        if (importRva == 0 || importBytes is < 20 or > 1048576) throw new InvalidOperationException("The import descriptor table is not bounded.");
        var descriptors = 0; var matches = 0; var ended = false;
        (uint Lookup, uint Iat, ulong NameRva) selected = default;
        for (uint offset = 0; offset + 20 <= importBytes; offset += 20)
        {
            var descriptor = Raw(checked(importRva + offset), 20);
            if (file.AsSpan(descriptor, 20).IndexOfAnyExcept((byte)0) < 0) { ended = true; break; }
            if (!string.Equals(Name(Word(descriptor + 12)), expectedDll, StringComparison.OrdinalIgnoreCase)) continue;
            ++descriptors; var lookup = Word(descriptor); var iat = Word(descriptor + 16); var thunkEnded = false;
            if (lookup == 0 || iat == 0) throw new InvalidOperationException("The held host import lacks distinct ILT/IAT.");
            for (uint index = 0; index <= 65536; ++index)
            {
                var lookupSlot = checked(lookup + index * 8); var iatSlot = checked(iat + index * 8);
                var nameRva = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(Raw(lookupSlot, 8), 8)); _ = Raw(iatSlot, 8);
                if (nameRva == 0) { thunkEnded = true; break; }
                if (nameRva > uint.MaxValue) throw new InvalidOperationException("The selected import cannot use an ordinal or unbounded name RVA.");
                if (Name(checked((uint)nameRva + 2)) != expectedSymbol) continue;
                ++matches; selected = (lookupSlot, iatSlot, nameRva);
            }
            if (!thunkEnded) throw new InvalidOperationException("The held import thunks are unterminated.");
        }
        if (!ended || descriptors != 1 || matches != 1)
            throw new InvalidOperationException("The independent host import must have one exact named DLL/symbol edge.");
        return selected;
    }
    private static (uint Slot, uint Function) OwnedExportSlot(byte[] file, IntPtr module, string expectedName)
    {
        uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at, 4));
        ushort Half(int at) => BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at, 2));
        var nt = checked((int)Word(0x3c)); var optional = checked(nt + 24);
        if (Half(0) != 0x5a4d || Word(nt) != 0x4550 || Half(nt + 4) != 0x8664 || Half(optional) != 0x20b)
            throw new InvalidOperationException("The export corruption probe requires the independently frozen AMD64 Kernel PE.");
        var headers = Word(optional + 60); var sections = checked(optional + Half(nt + 20));
        int Raw(uint rva, int length)
        {
            if (length <= 0) throw new InvalidOperationException("Invalid owned export file extent.");
            if ((ulong)rva + (ulong)length <= headers) return checked((int)rva);
            for (var index = 0; index < Half(nt + 6); ++index)
            {
                var section = checked(sections + index * 40); var begin = Word(section + 12); var bytes = Word(section + 16);
                if (rva >= begin && (ulong)rva - begin + (ulong)length <= bytes)
                {
                    var raw = checked((int)(Word(section + 20) + rva - begin));
                    _ = file.AsSpan(raw, length); return raw;
                }
            }
            throw new InvalidOperationException("The exact owned export range has no complete held file backing.");
        }
        var exportRva = Word(optional + 112); var exportBytes = Word(optional + 116); var export = Raw(exportRva, 40);
        var functionCount = Word(export + 20); var nameCount = Word(export + 24);
        if (functionCount is 0 or > 65536 || nameCount is 0 or > 65536) throw new InvalidOperationException("Invalid bounded owned export table.");
        var functions = Word(export + 28); var names = Word(export + 32); var ordinals = Word(export + 36);
        uint slot = 0, function = 0; var matches = 0;
        for (uint index = 0; index < nameCount; ++index)
        {
            var nameRva = Word(Raw(checked(names + index * 4), 4)); var name = new System.Text.StringBuilder(); var ended = false;
            for (uint character = 0; character < 512; ++character)
            {
                var value = file[Raw(checked(nameRva + character), 1)];
                if (value == 0) { ended = true; break; }
                if (value < 0x20 || value > 0x7e) throw new InvalidOperationException("Invalid owned export ASCII name.");
                name.Append((char)value);
            }
            if (!ended) throw new InvalidOperationException("The owned export name is unterminated.");
            if (name.ToString() != expectedName) continue;
            ++matches; var ordinal = Half(Raw(checked(ordinals + index * 2), 2));
            if (ordinal >= functionCount) throw new InvalidOperationException("The owned export ordinal exceeds its table.");
            slot = checked(functions + ordinal * 4u); function = Word(Raw(slot, 4));
        }
        if (matches != 1 || function == 0 || function >= Word(optional + 56)
            || function >= exportRva && (ulong)function < (ulong)exportRva + exportBytes
            || Address(NativeLibrary.GetExport(module, expectedName)) != Address(module) + function)
            throw new InvalidOperationException("The actual named Kernel export differs from the unique independent EAT function slot.");
        return (slot, function);
    }
    private static byte[] ReadPublication(ReadNative read)
    {
        var bytes = new byte[NativeEngineTransportPublicationLayout.ByteSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 5); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)bytes.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), NativeEngineTransportPublicationLayout.Magic);
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            if (read(buffer) != 0) throw new InvalidOperationException("The actual publisher did not return its complete V5 record.");
            Marshal.Copy(buffer, bytes, 0, bytes.Length); _ = NativeEngineTransportPublication.Decode(bytes); return bytes;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static T Export<T>(IntPtr module, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    private static string Digest(string value)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character))) throw new InvalidOperationException("An exact SHA256 digest is required.");
        return value.ToLowerInvariant();
    }
    private static string Hash(byte[] bytes) => HashBytes(SHA256.HashData(bytes));
    private static string HashBytes(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
    private static ulong Address(IntPtr value) => unchecked((ulong)value.ToInt64());
    [StructLayout(LayoutKind.Sequential)]
    private struct ProbeMemory
    {
        internal IntPtr BaseAddress, AllocationBase;
        internal uint AllocationProtect;
        internal nuint RegionSize;
        internal uint State, Protect, Type;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(IntPtr address, nuint bytes, uint protection, out uint oldProtection);
    [DllImport("kernel32.dll")]
    private static extern nuint VirtualQuery(IntPtr address, out ProbeMemory information, nuint bytes);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint bytes);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string module);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
