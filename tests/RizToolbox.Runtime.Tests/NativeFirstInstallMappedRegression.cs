using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

/// <summary>Isolated CLR adoption of the real native producer over the pinned
/// mapped game PE. No game instruction, IL2CPP initializer or transport original
/// is invoked. Metadata identifiers are controlled opaque carriers because real
/// IL2CPP metadata cannot be resolved without executing the game initializer.</summary>
internal static partial class NativeFirstInstallMappedRegression
{
    internal const string TickAdmissionModeArgument = "--runtime-tick-admission";
    internal static readonly (string Name, Action Run)[] TickAdmissionCases = {
        ("Runtime tick Standard and unaudited Developer use live channels without a complete publication audit", TickStandardChannelContract),
        ("Runtime tick full audits require explicit immutable Developer configuration", TickExecutionOptions),
        ("Runtime tick admission ignores unrelated boot invokes without a full proof or SDK dispatch", TickIgnoredInvokes),
        ("Runtime tick admission skips reentry and unrelated bound methods before native checks or measurements", TickReentry),
        ("Runtime tick explicit Developer audit requires a fresh full proof for every accepted tick", TickProofRequired),
        ("Runtime tick Standard rechecks the actual native channel after its filter", TickChannelChangesDuringFilter),
        ("Runtime tick admission rejects native channel invalidation during full proof before dispatch", TickChannelChangesDuringProof),
        ("Runtime tick admission rejects invalid native frames and foreign threads before its filter", TickInvalidFrames),
        ("Runtime tick admission rejects foreign unknown idle and faulted native channels before its filter", TickInvalidChannels),
        ("Runtime tick admission poisons filter proof and callback exceptions while preserving native completion", TickExceptionBoundaries),
        ("Runtime tick admission binds only after channel checks and dispatches the current completion", TickCurrentCompletion),
        ("Runtime startup selects the profile Update before binding its thread and method", TickStartupProfileBinding),
        ("Runtime startup requires the exact non-generic instance void signature", TickStartupSignature),
        ("Runtime tick admission rejects bound foreign threads and abnormal original returns", TickThreadAndOriginalReturn),
        ("Runtime tick entry measurements include admission and explicit audit costs before host dispatch", TickEntryTiming),
        ("Runtime tick entry measurement failure does not change admission or native completion", TickMeasurementFailure)
    };
    internal static int RunTickAdmissionChecks()
    {
        var failed = 0;
        foreach (var test in TickAdmissionCases)
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { ++failed; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
        }
        Console.WriteLine($"Runtime tick admission checks: {TickAdmissionCases.Length - failed}/{TickAdmissionCases.Length} passed.");
        return failed == 0 ? 0 : 1;
    }
    private static void TickCheck(bool condition, string message = "Runtime tick admission contract failed")
    { if (!condition) throw new InvalidOperationException(message); }
    private sealed class TickAdmissionHarness
    {
        internal const uint ThreadId = 77;
        internal static readonly IntPtr TickMethod = new(0x401), OtherMethod = new(0x402);
        internal static NativeRuntimeInvokeInformation Information() => new() {
            Version = 1, Size = 80, Attached = 1, Target = new(0x101), Original = new(0x102),
            Entry = new(0x103), Epoch = 9, Entered = 1, InFlight = 1
        };
        internal static NativeRuntimeInvokeFrame Frame(IntPtr method = default) => new() {
            Version = 1, Size = 64, Method = method == IntPtr.Zero ? TickMethod : method,
            Self = new(0x501), Arguments = new(0x601), Result = new(0x801),
            Caller = new(0x901), OsThreadId = ThreadId
        };
        internal NativeRuntimeInvokeInformation Info = Information();
        internal uint ReadStatus;
        internal int Reads, Filters, Proofs, Dispatches, Faults, OwnershipChecks, TimestampReads;
        internal IntPtr BoundTick;
        internal Action? DuringFilter, DuringProof, DuringDispatch, DuringOwnership;
        internal bool ThrowInDiagnostic, ThrowInMeasurement;
        internal long Clock = 100;
        internal readonly List<string> Order = new();
        internal readonly List<NativeRuntimeInvokeFrame> Delivered = new();
        internal readonly List<long> Sequences = new();
        internal readonly List<RuntimeEntryMeasurement> Measurements = new();
        internal readonly NativeRuntimeInvokeBridge.AfterAdmission Admission;
        internal TickAdmissionHarness(RuntimeExecutionOptions? options = null, Func<IntPtr, bool>? matchesTick = null)
        {
            Admission = new(new(0x101), new(0x102), new(0x103), 9,
                (ref NativeRuntimeInvokeInformation info) => {
                    ++Reads; Order.Add("channel"); info = Info; return ReadStatus;
                }, method => {
                    ++Filters; Order.Add("filter"); DuringFilter?.Invoke();
                    return matchesTick?.Invoke(method) ?? method == TickMethod;
                }, () => {
                    ++Proofs; Order.Add("proof"); DuringProof?.Invoke();
                }, (frame, sequence) => {
                    ++Dispatches; Order.Add("dispatch"); BoundTick = frame.Method;
                    Delivered.Add(frame); Sequences.Add(sequence); DuringDispatch?.Invoke();
                }, _ => {
                    ++Faults;
                    if (ThrowInDiagnostic) throw new InvalidOperationException("controlled diagnostic fault");
                }, options, () => { ++OwnershipChecks; DuringOwnership?.Invoke(); }, sample => {
                    Measurements.Add(sample);
                    if (ThrowInMeasurement) throw new InvalidOperationException("controlled measurement fault");
                }, () => { ++TimestampReads; return Clock; });
        }
    }
    private static void TickStandardChannelContract()
    {
        foreach (var options in new[] { new RuntimeExecutionOptions(), new RuntimeExecutionOptions(DeveloperDiagnostics: true) })
        {
            var h = new TickAdmissionHarness(options);
            h.DuringProof = () => throw new InvalidOperationException("Standard entered a complete audit");
            for (var index = 0; index < 128; ++index)
                h.Admission.Observe(TickAdmissionHarness.Frame(), TickAdmissionHarness.ThreadId);
            TickCheck(h.Dispatches == 128 && h.Reads == 256 && h.OwnershipChecks == 256 && h.Proofs == 0
                && h.Faults == 0 && !h.Admission.Unknown);
            TickCheck(h.Sequences.SequenceEqual(Enumerable.Range(1, 128).Select(value => (long)value)));
            // A previous complete audit is never consulted. The current native
            // Unknown is still read on the very next accepted Standard tick.
            h.Info.Unknown = 1;
            h.Admission.Observe(TickAdmissionHarness.Frame(), TickAdmissionHarness.ThreadId);
            TickCheck(h.Dispatches == 128 && h.Faults == 1 && h.Admission.Unknown && h.Proofs == 0);
        }
        var retained = new TickAdmissionHarness();
        retained.DuringOwnership = () => throw new UnsupportedSignatureException("retained owner is unknown");
        retained.Admission.Observe(TickAdmissionHarness.Frame(), TickAdmissionHarness.ThreadId);
        TickCheck(retained.Admission.Unknown && retained.Dispatches == 0 && retained.Proofs == 0);
    }
    private static void TickExecutionOptions()
    {
        TickCheck(Rejected<ArgumentException>(() => _ = new RuntimeExecutionOptions(FullAuditOnTick: true)));
        var standard = new RuntimeExecutionOptions();
        var developer = new RuntimeExecutionOptions(DeveloperDiagnostics: true, FullAuditOnTick: true);
        TickCheck(!standard.DeveloperDiagnostics && !standard.FullAuditOnTick
            && developer.DeveloperDiagnostics && developer.FullAuditOnTick);
        TickCheck(typeof(RuntimeExecutionOptions).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .All(property => property.SetMethod is null), "An active Tick contract can be changed after attachment.");
    }
    private static void TickIgnoredInvokes()
    {
        var h = new TickAdmissionHarness();
        for (var index = 0; index < 4096; ++index)
            h.Admission.Observe(TickAdmissionHarness.Frame(TickAdmissionHarness.OtherMethod), TickAdmissionHarness.ThreadId);
        TickCheck(h.Reads == 4096 && h.Filters == 4096 && h.Measurements.Count == 0);
        TickCheck(h.Proofs == 0 && h.Dispatches == 0 && h.Faults == 0 && h.BoundTick == IntPtr.Zero && !h.Admission.Unknown);
    }
    private static void TickReentry()
    {
        var h = new TickAdmissionHarness();
        var frame = TickAdmissionHarness.Frame();
        h.DuringDispatch = () => h.Admission.Observe(frame, TickAdmissionHarness.ThreadId);
        h.Admission.Observe(frame, TickAdmissionHarness.ThreadId);
        var ignored = TickAdmissionHarness.Frame(TickAdmissionHarness.OtherMethod);
        for (var index = 0; index < 4096; ++index)
            h.Admission.Observe(ignored, TickAdmissionHarness.ThreadId);
        TickCheck(h.Reads == 2 && h.Filters == 1 && h.OwnershipChecks == 2 && h.Proofs == 0 && h.Dispatches == 1
            && h.TimestampReads == 3 && h.Measurements.Count == 1 && h.Faults == 0 && !h.Admission.Unknown);
        TickCheck(h.Sequences.SequenceEqual(new long[] { 1 }));
    }
    private static void TickProofRequired()
    {
        var h = new TickAdmissionHarness(new(true, true)); var frame = TickAdmissionHarness.Frame();
        h.DuringProof = () => TickCheck(h.BoundTick == IntPtr.Zero && h.Dispatches == 0,
            "The filter bound or dispatched the first tick before its proof.");
        h.Admission.Observe(frame, TickAdmissionHarness.ThreadId);
        TickCheck(h.Order.SequenceEqual(new[] { "channel", "filter", "proof", "channel", "dispatch" }));
        TickCheck(h.Proofs == 1 && h.Dispatches == 1 && h.Reads == 2 && h.BoundTick == frame.Method);
        h.DuringProof = () => throw new UnsupportedSignatureException("controlled changed or unknown producer");
        h.Admission.Observe(frame, TickAdmissionHarness.ThreadId);
        TickCheck(h.Proofs == 2 && h.Dispatches == 1 && h.Reads == 3 && h.Faults == 1 && h.Admission.Unknown);
        h.DuringProof = null;
        h.Admission.Observe(frame, TickAdmissionHarness.ThreadId);
        TickCheck(h.Proofs == 2 && h.Dispatches == 1 && h.Reads == 3,
            "A failed current proof was replaced by an older successful proof.");
    }
    private static void TickChannelChangesDuringFilter()
    {
        foreach (var change in new[] { "unknown", "epoch", "attached", "original", "idle", "status" })
        {
            var h = new TickAdmissionHarness();
            h.DuringFilter = () => {
                switch (change) {
                    case "unknown": h.Info.Unknown = 1; break;
                    case "epoch": ++h.Info.Epoch; break;
                    case "attached": h.Info.Attached = 0; break;
                    case "original": h.Info.Original = new(0x999); break;
                    case "idle": h.Info.InFlight = 0; break;
                    case "status": h.ReadStatus = 5; break;
                }
            };
            h.Admission.Observe(TickAdmissionHarness.Frame(), TickAdmissionHarness.ThreadId);
            TickCheck(h.Reads == 2 && h.Proofs == 0 && h.Dispatches == 0 && h.Faults == 1 && h.Admission.Unknown,
                "Standard accepted a channel changed by its filter: " + change);
        }
    }
    private static void TickChannelChangesDuringProof()
    {
        foreach (var change in new[] { "unknown", "epoch", "attached", "original", "idle", "status" })
        {
            var h = new TickAdmissionHarness(new(true, true));
            h.DuringProof = () => {
                var info = h.Info;
                switch (change) {
                    case "unknown": info.Unknown = 1; break;
                    case "epoch": ++info.Epoch; break;
                    case "attached": info.Attached = 0; break;
                    case "original": info.Original = new(0x999); break;
                    case "idle": info.InFlight = 0; break;
                    case "status": h.ReadStatus = 5; break;
                }
                h.Info = info;
            };
            h.Admission.Observe(TickAdmissionHarness.Frame(), TickAdmissionHarness.ThreadId);
            TickCheck(h.Reads == 2 && h.Filters == 1 && h.Proofs == 1 && h.Dispatches == 0
                && h.BoundTick == IntPtr.Zero && h.Faults == 1 && h.Admission.Unknown,
                "A changed native channel passed the post-proof check: " + change);
            h.Info = TickAdmissionHarness.Information(); h.ReadStatus = 0; h.DuringProof = null;
            h.Admission.Observe(TickAdmissionHarness.Frame(), TickAdmissionHarness.ThreadId);
            TickCheck(h.Reads == 2 && h.Dispatches == 0, "Channel invalidation was not permanent.");
        }
    }
    private static void TickInvalidFrames()
    {
        foreach (var change in new[] { "version", "size", "method", "caller", "thread-zero", "reserved", "foreign-thread" })
        {
            var h = new TickAdmissionHarness(); var frame = TickAdmissionHarness.Frame();
            switch (change) {
                case "version": frame.Version = 2; break;
                case "size": frame.Size = 63; break;
                case "method": frame.Method = IntPtr.Zero; break;
                case "caller": frame.Caller = IntPtr.Zero; break;
                case "thread-zero": frame.OsThreadId = 0; break;
                case "reserved": frame.Reserved = 1; break;
            }
            var thread = change == "foreign-thread" ? TickAdmissionHarness.ThreadId + 1 : TickAdmissionHarness.ThreadId;
            h.Admission.Observe(frame, thread);
            TickCheck(h.Reads == 0 && h.Filters == 0 && h.Proofs == 0 && h.Dispatches == 0 && h.Faults == 1 && h.Admission.Unknown,
                "An invalid frame reached channel/filter/proof: " + change);
        }
    }
    private static void TickInvalidChannels()
    {
        foreach (var change in new[] { "version", "size", "attached", "unknown", "target", "original", "entry", "epoch", "idle", "overflow", "fault", "status" })
        {
            var h = new TickAdmissionHarness(); var info = h.Info;
            switch (change) {
                case "version": info.Version = 2; break;
                case "size": info.Size = 79; break;
                case "attached": info.Attached = 0; break;
                case "unknown": info.Unknown = 1; break;
                case "target": info.Target = new(0x999); break;
                case "original": info.Original = new(0x999); break;
                case "entry": info.Entry = new(0x999); break;
                case "epoch": ++info.Epoch; break;
                case "idle": info.InFlight = 0; break;
                case "overflow": info.InFlight = ulong.MaxValue; break;
                case "fault": info.ObserverFaults = 1; break;
                case "status": h.ReadStatus = 2; break;
            }
            h.Info = info;
            h.Admission.Observe(TickAdmissionHarness.Frame(), TickAdmissionHarness.ThreadId);
            TickCheck(h.Reads == 1 && h.Filters == 0 && h.Proofs == 0 && h.Dispatches == 0 && h.Faults == 1 && h.Admission.Unknown,
                "A foreign or unknown channel reached the filter: " + change);
        }
    }
    private static void TickExceptionBoundaries()
    {
        foreach (var boundary in new[] { "filter", "proof", "dispatch" })
        {
            var h = new TickAdmissionHarness(new(true, true)) { ThrowInDiagnostic = true };
            Action fail = () => throw new InvalidOperationException("controlled " + boundary + " fault");
            if (boundary == "filter") h.DuringFilter = fail;
            else if (boundary == "proof") h.DuringProof = fail;
            else h.DuringDispatch = fail;
            var frame = TickAdmissionHarness.Frame(); var original = frame;
            h.Admission.Observe(frame, TickAdmissionHarness.ThreadId);
            TickCheck(h.Admission.Unknown && h.Faults == 1 && frame.Equals(original),
                "An observer fault escaped or altered the native completion: " + boundary);
            TickCheck(h.Proofs == (boundary == "filter" ? 0 : 1)
                && h.Dispatches == (boundary == "dispatch" ? 1 : 0));
            var counts = (h.Reads, h.Filters, h.Proofs, h.Dispatches, h.Faults);
            h.DuringFilter = h.DuringProof = h.DuringDispatch = null;
            frame.Result = new(0xabcdef);
            h.Admission.Observe(frame, TickAdmissionHarness.ThreadId);
            TickCheck(counts == (h.Reads, h.Filters, h.Proofs, h.Dispatches, h.Faults),
                "An exception allowed later input or a retained frame to run: " + boundary);
        }
    }
    private static void TickCurrentCompletion()
    {
        var h = new TickAdmissionHarness();
        var ignored = TickAdmissionHarness.Frame(TickAdmissionHarness.OtherMethod); ignored.Result = new(0x111);
        h.Admission.Observe(ignored, TickAdmissionHarness.ThreadId);
        h.DuringOwnership = () => TickCheck(h.BoundTick == IntPtr.Zero && h.Delivered.Count == 0);
        var current = TickAdmissionHarness.Frame(); current.Self = new(0x222); current.Result = new(0x333);
        current.Arguments = new(0x444); current.Caller = new(0x666);
        h.Admission.Observe(current, TickAdmissionHarness.ThreadId);
        TickCheck(h.Delivered.Count == 1 && h.Delivered[0].Equals(current)
            && h.Proofs == 0 && h.Dispatches == 1 && !h.Admission.Unknown);
        h.DuringOwnership = null;
        h.Admission.Observe(ignored, TickAdmissionHarness.ThreadId);
        TickCheck(h.Proofs == 0 && h.Dispatches == 1 && h.Filters == 2);
        var later = current; later.Result = new(0x777); later.Exception = IntPtr.Zero;
        h.Admission.Observe(later, TickAdmissionHarness.ThreadId);
        TickCheck(h.Proofs == 0 && h.Delivered.Count == 2 && h.Delivered[1].Equals(later)
            && current.Result == new IntPtr(0x333), "The accepted consumer reused or modified an earlier completion.");
        TickCheck(h.Sequences.SequenceEqual(new long[] { 1, 2 }));
    }

    private static void TickThreadAndOriginalReturn()
    {
        var h = new TickAdmissionHarness();
        var exception = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(exception, new(0x1234));
            var abnormal = TickAdmissionHarness.Frame(); abnormal.Exception = exception; abnormal.OsThreadId++;
            h.Admission.Observe(abnormal, abnormal.OsThreadId);
            TickCheck(h.Dispatches == 0 && h.Proofs == 0 && h.BoundTick == IntPtr.Zero && !h.Admission.Unknown
                && h.Measurements.Single().Outcome == RuntimeEntryOutcome.AbnormalReturn
                && h.Measurements.Single().HostTickSequence == 0 && Marshal.ReadIntPtr(exception) == new IntPtr(0x1234));
            Marshal.WriteIntPtr(exception, IntPtr.Zero);
            abnormal.OsThreadId = TickAdmissionHarness.ThreadId;
            h.Admission.Observe(abnormal, TickAdmissionHarness.ThreadId);
            TickCheck(h.Dispatches == 1 && h.Sequences.Single() == 1);
            var foreign = TickAdmissionHarness.Frame(); foreign.OsThreadId++;
            h.Admission.Observe(foreign, foreign.OsThreadId);
            TickCheck(h.Dispatches == 1 && h.Admission.Unknown && h.Faults == 1);
            h.Admission.Observe(TickAdmissionHarness.Frame(), TickAdmissionHarness.ThreadId);
            TickCheck(h.Dispatches == 1, "A correct thread revived a permanently invalidated channel.");
        }
        finally { Marshal.FreeHGlobal(exception); }
    }
    private static void TickEntryTiming()
    {
        foreach (var audit in new[] { false, true })
        {
            var h = new TickAdmissionHarness(new(audit, audit));
            h.DuringFilter = () => h.Clock += 7;
            h.DuringOwnership = () => h.Clock += 3;
            h.DuringProof = () => h.Clock += 19;
            h.DuringDispatch = () => h.Clock += 11;
            h.Admission.Observe(TickAdmissionHarness.Frame(), TickAdmissionHarness.ThreadId, entryStarted: 95);
            var sample = h.Measurements.Single();
            var admissionTicks = 5 + 7 + 6 + (audit ? 19 : 0);
            var scale = 1000d / System.Diagnostics.Stopwatch.Frequency;
            bool EqualsTicks(double milliseconds, int ticks) => Math.Abs(milliseconds / scale - ticks) < 0.001d;
            TickCheck(sample.HostTickSequence == 1 && sample.Outcome == RuntimeEntryOutcome.Dispatched
                && sample.FullAuditOnTick == audit && EqualsTicks(sample.AdmissionMilliseconds, admissionTicks)
                && EqualsTicks(sample.HostMilliseconds, 11) && EqualsTicks(sample.TotalMilliseconds, admissionTicks + 11),
                "Entry costs before the host, including an explicit full audit, were omitted from total time.");
        }
    }
    private static void TickMeasurementFailure()
    {
        var h = new TickAdmissionHarness { ThrowInMeasurement = true };
        var frame = TickAdmissionHarness.Frame(); var original = frame;
        h.Admission.Observe(frame, TickAdmissionHarness.ThreadId);
        h.Admission.Observe(frame, TickAdmissionHarness.ThreadId);
        TickCheck(h.Dispatches == 2 && h.Proofs == 0 && h.Faults == 0 && !h.Admission.Unknown
            && frame.Equals(original) && h.Sequences.SequenceEqual(new long[] { 1, 2 }));
    }

    internal const string ModeArgument = "--first-install-mapped-adoption";
    private const string GameFingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private const string DobbyFingerprint = "8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded";
    private static readonly List<object> Owners = new();
    [StructLayout(LayoutKind.Sequential)] private struct VoidStatistics
    { internal uint Version, Size; internal ulong Entered, Exited, InFlight, EnterFaults, ExitFaults; }
    [StructLayout(LayoutKind.Sequential)] private struct MidStatistics
    { internal uint Version, Size; internal ulong Entered, Exited, InFlight, Faults; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint BeforeNative(IntPtr init, [MarshalAs(UnmanagedType.LPWStr)] string absoluteDobbyPath);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ListenerInfoNative(uint slot, ref NativeListenerInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint RuntimeInfoNative(ref NativeRuntimeInvokeInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint VoidInfoNative(uint slot, ref NativeVoidBootstrapInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint VoidStatsNative(uint slot, ref VoidStatistics statistics);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint MidStatsNative(uint slot, ref MidStatistics statistics);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GateStatsNative(uint slot, ref NativeVoidAdmissionGateStatistics statistics);

    internal static int RunChild(string[] args)
    {
        var checks = 0;
        void Check(bool condition, string message)
        { ++checks; if (!condition) throw new InvalidOperationException(message); }
        var mode = args.Length > 1 ? args[1] : "missing";
        try
        {
            if (args.Length != 4 || mode is not ("happy" or "reentry"))
                throw new ArgumentException("Usage: --first-install-mapped-adoption happy|reentry absolute-GameAssembly.dll absolute-dobby.dll");
            if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
                throw new PlatformNotSupportedException("The mapped CLR adoption regression requires Windows x64.");
            var gamePath = RequireFile(args[2]); var dobbyPath = RequireFile(args[3]);
            var helperPath = RequireFile(Path.Combine(Path.GetDirectoryName(typeof(NativeFirstInstall).Assembly.Location)!, "RizToolbox.JudgeContext.dll"));
            Check(string.Equals(Path.GetFileName(gamePath), "GameAssembly.dll", StringComparison.OrdinalIgnoreCase), "The mapped input must retain the actual GameAssembly.dll basename.");
            var filesBefore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                [gamePath] = Hash(gamePath), [dobbyPath] = Hash(dobbyPath), [helperPath] = Hash(helperPath)
            };
            Check(filesBefore[gamePath] == GameFingerprint && filesBefore[dobbyPath] == DobbyFingerprint,
                "The mapped producer regression requires the actual pinned game and inspected Dobby files.");
            Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero && GetModuleHandle(helperPath) == IntPtr.Zero,
                "This child must begin without any game/helper module or earlier producer attempt.");
            var noProducerRejected = Rejected<UnsupportedSignatureException>(() => NativeFirstInstall.RequireReady());
            Check(noProducerRejected, "Production RequireReady accepted an unloaded native producer.");
            Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero && GetModuleHandle(helperPath) == IntPtr.Zero,
                "The unloaded rejection unexpectedly loaded a native image.");

            var helper = NativeLibrary.Load(helperPath);
            // DONT_RESOLVE_DLL_REFERENCES is restricted to this isolated test.
            // The actual GameAssembly entrypoint/imports are never executed.
            var module = LoadLibraryEx(gamePath, IntPtr.Zero, 1);
            Check(module != IntPtr.Zero && GetModuleHandle("GameAssembly.dll") == module, "The fixed game PE could not be mapped without initialization.");
            var init = NativeLibrary.GetExport(module, "il2cpp_init");
            Check(init == IntPtr.Add(module, 0x2841c0), "The actual mapped init export differs from its pinned RVA.");
            var before = Export<BeforeNative>(helper, "RizFirstInstall_BeforeIl2CppInit");
            var firstInfo = Export<FirstInfoV7Native>(helper, "RizFirstInstall_GetInfoV7");
            NativeFirstInstallInformationV7 ReadPublished()
            {
                var value = new NativeFirstInstallInformationV7 { Listener = new() { Common = new() { Version = 7, Size = 2528 } } };
                Check(firstInfo(ref value) == 0, "The current complete V7 native publication could not be read.");
                return value;
            }
            var listenerInfo = Export<ListenerInfoNative>(helper, "RizListenerContext_GetInfo");
            var runtimeInfo = Export<RuntimeInfoNative>(helper, "RizRuntimeInvokeContext_GetInfo");
            var voidInfo = Export<VoidInfoNative>(helper, "RizVoidBootstrap_GetInfo");
            var voidStats = Export<VoidStatsNative>(helper, "RizVoidContext_GetStats");
            var midStats = Export<MidStatsNative>(helper, "RizEventMidContext_GetStats");
            var gateStats = Export<GateStatsNative>(helper, "RizVoidContext_GetGateStats");
            var initial = ReadPublished().Listener.Common;
            Check(initial.Phase == 0 && initial.Unknown == 0 && initial.WritesStarted == 0,
                "The real producer was not in its initial unwritten phase.");
            var coldBytes = CodeSnapshot(module);
            Check(before(init, dobbyPath) == 1, "The real fixed-image native producer did not publish successfully.");
            var readyPublication = ReadPublished(); var readyV2 = readyPublication.Listener; var ready = readyV2.Common;
            Check(NativeOperationPublication.HasShape(readyPublication) && NativeFirstInstall.ReadyCommonShape(ready, 7, 2528, 64) && ready.WritesStarted == 64
                && ready.ModuleBase == unchecked((ulong)module.ToInt64()) && ready.ImageSize == 0x243c000 && ready.VoidSlot == 31,
                "The current V7 native publication did not report all twenty-six operations and actual sixty-four writes.");
            var listenerBefore = ReadListener(listenerInfo, readyV2.ListenerSlot);
            Check(NativeListenerPublication.MatchesLane(listenerBefore, readyV2) && listenerBefore.Attached == 0,
                "The actual listener lane did not match its initially unattached producer identity.");
            var publishedListenerPage = Bytes(readyV2.PostStub0, checked((int)readyV2.AllocationSize));
            var publishedBytes = CodeSnapshot(module);
            Check(!coldBytes.SequenceEqual(publishedBytes), "The actual mapped target bytes did not change during publication.");
            var producer = NativeFirstInstall.RequireReady(); Owners.Add(producer);
            Check(producer.Module == module && producer.HelperModule == helper && string.Equals(producer.HelperPath, helperPath, StringComparison.OrdinalIgnoreCase)
                && producer.RuntimeTarget == ready.RuntimeTarget && producer.RuntimeOriginal == ready.RuntimeOriginal
                && producer.RuntimeEntry == ready.RuntimeEntry && producer.RuntimeEpoch == ready.RuntimeEpoch
                && producer.UnityTarget == ready.UnityTarget && producer.UnityOriginal == ready.UnityOriginal
                && producer.UnityEntry == ready.UnityEntry && producer.UnityEpoch == ready.UnityEpoch && producer.MidReturnPc == ready.MidReturnPc,
                "Production CLR RequireReady did not preserve the real loaded producer identity.");
            Check(ListenerViewAgrees(producer.ListenerPublication, readyV2), "Production RequireReady discarded or substituted the embedded current listener identity.");
            Check(ReferenceEquals(NativeFirstInstall.RequireReady(), producer), "Repeated Ready queries replaced the private production owner.");
            var runtimeBefore = ReadRuntime(runtimeInfo); var parentBefore = ReadVoid(voidInfo, ready.VoidSlot);
            Check(RuntimeIdentity(runtimeBefore, ready, 0) && ParentIdentity(parentBefore, ready, 0),
                "The real permanent native channels were not initially unattached.");

            var logger = new Logger(); var counters = new Counters(); var engine = new NeverInstallEngine();
            var operationAdoption = ValidateOperationPublication(producer, helper, module, mode, logger, counters, engine, Check);
            var singleEventPublication = ValidateSingleEventPublication(producer, helper, module, Check);
            var singleEventBridgeAdoption = mode == "happy" ? ValidateSingleEventBridgeAdoption(producer, helper, Check) : null;
            var originalReentryRejected = false; var duplicateRuntimeRejected = false; var wrongTargetRejected = false;
            var conflictingMetadataRejected = false; var doubleSinkSharedEntry = false; var logicalDetachPreservedEntry = false;
            if (mode == "happy")
            {
                var consumer = new RuntimeBridge("MappedFixture.dll", "mapped.fixture", "NeverInvoked", "Tick", (_, _) => counters.RuntimeAfter++, logger);
                Owners.Add(consumer);
                var runtime = NativeRuntimeInvokeBridge.Adopt(producer, consumer, logger); Owners.Add(runtime);
                Check(RuntimeIdentity(ReadRuntime(runtimeInfo), ready, 1), "Production CLR runtime-invoke adoption did not publish its actual observer.");
                duplicateRuntimeRejected = Rejected<InvalidOperationException>(() => NativeRuntimeInvokeBridge.Adopt(producer, consumer, logger));
                Check(duplicateRuntimeRejected, "A second production runtime-invoke observer was accepted.");
                var main = new MainDispatcher(); var broker = new ExactHookBroker(engine, main, logger); var peer = new ExactHookBroker(engine, main, logger);
                Owners.Add(main); Owners.Add(broker); Owners.Add(peer);
                // These opaque metadata identifiers are never dereferenced or
                // described as IL2CPP metadata. The entry/slot/epoch come only
                // from the real pinned game image and native producer receipt.
                var target = new VerifiedHookTarget("mapped.producer.unityevent", producer.UnityTarget, new(0x123450), new(0x234560), ExactHookAbi.NativeVoid0);
                (ExactHookBroker.Binding Binding, ExactHookBroker.BoundaryProbeBinding Probe) Bind(ExactHookBroker owner, VerifiedHookTarget requested)
                    => owner.BindNativeVoidObservationWithMid<ObservationContext>(requested,
                        _ => { ++counters.Captures; return new(counters); }, (_, _) => ++counters.Afters,
                        (_, _) => ++counters.Faults, (_, _, _) => ++counters.Mids,
                        (_, _, _) => ++counters.ProbeEnters, (_, _, _, _) => ++counters.ProbeExits);
                wrongTargetRejected = Rejected<UnsupportedSignatureException>(() => Bind(broker, target with { Address = IntPtr.Add(target.Address, 16) }));
                Check(wrongTargetRejected && ParentIdentity(ReadVoid(voidInfo, ready.VoidSlot), ready, 0),
                    "A wrong target was accepted or consumed the real Unity observer attachment.");
                var first = Bind(broker, target); Owners.Add(first.Binding); Owners.Add(first.Probe);
                Check(ParentIdentity(ReadVoid(voidInfo, ready.VoidSlot), ready, 1) && first.Binding.ReadActivity() is { Known: true, InFlight: 0 }
                    && first.Probe.ReadActivity() is { Known: true, InFlight: 0 }, "The first production broker adoption has no known attached idle entry.");
                var second = Bind(peer, target with { Id = "mapped.producer.peer" }); Owners.Add(second.Binding); Owners.Add(second.Probe);
                var parentAfterSecond = ReadVoid(voidInfo, ready.VoidSlot);
                doubleSinkSharedEntry = ParentIdentity(parentAfterSecond, ready, 1) && SameParentIdentity(parentBefore, parentAfterSecond)
                    && second.Binding.ReadActivity() is { Known: true, InFlight: 0 } && second.Probe.ReadActivity() is { Known: true, InFlight: 0 }
                    && engine.InstallCalls == 0 && engine.PrepareCalls == 0 && engine.CommitCalls == 0;
                Check(doubleSinkSharedEntry, "Two broker instances failed to share the real existing immutable native entry without another patch.");
                conflictingMetadataRejected = Rejected<UnsupportedSignatureException>(() => Bind(peer, target with { MethodInfo = new(0x123458) }));
                Check(conflictingMetadataRejected, "Foreign controlled method identity was allowed to share the exact production entry.");
                first.Binding.Dispose(); first.Probe.Dispose();
                Check(second.Binding.ReadActivity() is { Known: true, InFlight: 0 }, "Logical detach of one sink damaged its peer's physical entry.");
                second.Binding.Dispose(); second.Probe.Dispose();
                var rebound = Bind(broker, target with { Id = "mapped.producer.rebound" }); Owners.Add(rebound.Binding); Owners.Add(rebound.Probe);
                logicalDetachPreservedEntry = ParentIdentity(ReadVoid(voidInfo, ready.VoidSlot), ready, 1)
                    && rebound.Binding.ReadActivity() is { Known: true, InFlight: 0 }
                    && engine.InstallCalls == 0 && engine.PrepareCalls == 0 && engine.CommitCalls == 0;
                Check(logicalDetachPreservedEntry, "Logical rebind replaced or reconfigured the permanent native entry.");
                rebound.Binding.Dispose(); rebound.Probe.Dispose();
                Check(first.Binding.Describe().NativeCalls == 0 && second.Binding.Describe().OriginalCalls == 0,
                    "The broker reported invocations despite the test never calling game code.");
                producer.VerifyReady();
                var afterPublication = ReadPublished(); var afterV2 = afterPublication.Listener;
                Check(afterPublication.Equals(readyPublication), "Legal CLR observer attachment changed or invalidated the immutable producer receipt.");
                Check(afterV2.Equals(readyV2)
                    && NativeListenerPublication.MatchesLane(ReadListener(listenerInfo, readyV2.ListenerSlot), readyV2),
                    "Parent/runtime observer attachment changed the embedded full listener identity.");
            }
            else
            {
                Check(before(init, dobbyPath) == 2, "Repeated original-init producer entry was not rejected as unknown.");
                var invalidatedPublication = ReadPublished(); var invalidated = invalidatedPublication.Listener.Common;
                Check(invalidated.Phase == 4 && invalidated.Unknown == 1
                    && invalidated.WritesStarted == 64 && SamePublishedIdentity(invalidated, ready),
                    "Reentry replaced the actual identity, repeated publication or failed to retain unknown responsibility.");
                Check(ready.Reserved1 == 0 && (invalidated.Reserved1 & uint.MaxValue) == 8
                    && ReadPublished().Listener.Common.Reserved1 == invalidated.Reserved1,
                    "Repeated V7 reads must preserve PreinitReentry as the first native failure.");
                var invalidatedV2 = invalidatedPublication.Listener;
                Check(invalidatedV2.Common.Phase == 4 && invalidatedV2.Common.Unknown == 1,
                    "The current reader lost the real reentry Unknown snapshot.");
                invalidatedV2.Common.Phase = readyV2.Common.Phase; invalidatedV2.Common.Unknown = readyV2.Common.Unknown;
                invalidatedV2.Common.Reserved1 = readyV2.Common.Reserved1;
                Check(invalidatedV2.Equals(readyV2), "Native reentry replaced a listener site, relay, registration or epoch identity.");
                invalidatedPublication.Listener = invalidatedV2;
                Check(invalidatedPublication.SingleEvent.Phase == 4 && invalidatedPublication.SingleEvent.Unknown == 1,
                    "Reentry lost the Single section's retained Unknown responsibility.");
                invalidatedPublication.SingleEvent.Phase = readyPublication.SingleEvent.Phase;
                invalidatedPublication.SingleEvent.Unknown = readyPublication.SingleEvent.Unknown;
                Check(invalidatedPublication.Equals(readyPublication), "Native reentry replaced one of the twenty-six retained operation claims or publication masks.");
                originalReentryRejected = Rejected<UnsupportedSignatureException>(() => NativeFirstInstall.RequireReady());
                Check(originalReentryRejected && Rejected<UnsupportedSignatureException>(() => producer.VerifyReady()),
                    "A real reentered native producer continued to authorize production CLR adoption.");
                try
                {
                    producer.VerifyRuntimeChannelOwnership();
                    throw new InvalidOperationException("The runtime channel lost the preceding publication failure.");
                }
                catch (UnsupportedSignatureException error)
                {
                    Check(error.Message.Contains("First failure: VerifyReady", StringComparison.Ordinal)
                        && error.Message.Contains("phase=4, unknown=1", StringComparison.Ordinal)
                        && error.Message.Contains("First native failure: PreinitReentry (reason=8,", StringComparison.Ordinal),
                        "A later runtime observation obscured the original native publication failure: " + error.Message);
                }
                Check(RuntimeIdentity(ReadRuntime(runtimeInfo), ready, 0) && ParentIdentity(ReadVoid(voidInfo, ready.VoidSlot), ready, 0),
                    "The isolated unknown-mode unexpectedly attached managed observers.");
            }

            var parentStatistics = new VoidStatistics { Version = 1, Size = 48 };
            var midStatistics = new MidStatistics { Version = 1, Size = 40 };
            var gate = new NativeVoidAdmissionGateStatistics { Version = 1, Size = 40 };
            Check(voidStats(ready.VoidSlot, ref parentStatistics) == 0 && parentStatistics.Entered == 0 && parentStatistics.Exited == 0
                && parentStatistics.InFlight == 0 && parentStatistics.EnterFaults == 0 && parentStatistics.ExitFaults == 0,
                "A Unity parent transport original or callback unexpectedly executed.");
            Check(midStats(ready.VoidSlot, ref midStatistics) == 0 && midStatistics.Entered == 0 && midStatistics.Exited == 0
                && midStatistics.InFlight == 0 && midStatistics.Faults == 0, "The native mid transport unexpectedly executed.");
            Check(gateStats(ready.VoidSlot, ref gate) == 0 && gate.IsValid && gate.Unknown == 0 && gate.Active == 0 && gate.Parked == 0
                && gate.Phase == NativeVoidAdmissionPhase.Open, "The adopted idle native channel changed admission state.");
            var finalRuntime = ReadRuntime(runtimeInfo);
            Check(finalRuntime.Entered == 0 && finalRuntime.Exited == 0 && finalRuntime.InFlight == 0 && finalRuntime.ObserverFaults == 0,
                "A runtime-invoke transport original or callback unexpectedly executed.");
            var finalListener = ReadListener(listenerInfo, readyV2.ListenerSlot);
            Check(finalListener.Attached == 0 && finalListener.Epoch == readyV2.ListenerEpoch
                && finalListener.Entered == 0 && finalListener.Exited == 0 && finalListener.InFlight == 0
                && finalListener.Faults == 0 && finalListener.Abnormal == 0 && finalListener.Overflow == 0 && finalListener.MissingPost == 0,
                "The listener-only readonly adoption unexpectedly attached observers, called a game leaf or changed native counters.");
            Check(counters.Total == 0 && logger.Errors == 0 && engine.InstallCalls == 0 && engine.PrepareCalls == 0 && engine.CommitCalls == 0,
                "CLR adoption invoked a callback, installed another detour or emitted an observer fault.");
            Check(publishedBytes.SequenceEqual(CodeSnapshot(module)), "CLR adoption or producer reentry modified the already published game instructions.");
            Check(publishedListenerPage.SequenceEqual(Bytes(readyV2.PostStub0, checked((int)readyV2.AllocationSize))),
                "Readonly adoption or producer reentry changed the sealed listener code/unwind page.");
            var fileProofs = filesBefore.Select(pair => new { path = pair.Key, beforeSha256 = pair.Value, afterSha256 = Hash(pair.Key) }).ToArray();
            Check(fileProofs.All(value => value.beforeSha256 == value.afterSha256), "A mapped producer regression changed a file on disk.");
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(CodeSnapshot(module).SequenceEqual(publishedBytes), "Process roots failed to retain the mapped publication across CLR collection.");
            if (mode == "happy") Check(NativeFirstInstall.RequireReady().RuntimeEpoch == ready.RuntimeEpoch && RuntimeIdentity(ReadRuntime(runtimeInfo), ready, 1)
                && ParentIdentity(ReadVoid(voidInfo, ready.VoidSlot), ready, 1)
                && ListenerViewAgrees(NativeFirstInstall.RequireReady().ListenerPublication, readyV2)
                && NativeListenerPublication.MatchesLane(ReadListener(listenerInfo, readyV2.ListenerSlot), readyV2),
                "CLR collection changed permanent adopted native identities.");
            Console.WriteLine(JsonSerializer.Serialize(new {
                schemaVersion = 1, kind = "mapped-image-managed-preinit-adoption", mode, passed = true, checks, failures = 0,
                noProducerRejected, realNativeProducerReady = true, productionRequireReadyVerified = true,
                productionListenerReceiptV7Verified = true, listenerReadonlyAdoptionVerified = true,
                listenerPublicationVersion = readyV2.Common.Version, listenerCapabilityVersion = readyV2.CapabilityVersion,
                listenerSlot = readyV2.ListenerSlot, listenerEpoch = readyV2.ListenerEpoch, listenerRegions = 6,
                listenerRegisteredFunctions = readyV2.RegisteredFunctionCount, listenerPrewriteLookupChecks = readyV2.PrewriteLookupChecks,
                listenerPrewriteBoundaryChecks = readyV2.PrewriteBoundaryChecks, listenerObserverAttached = false,
                listenerCallbacksVerified = false, listenerNativeInvocations = finalListener.Entered,
                productionRuntimeAdoptVerified = mode == "happy", productionBrokerAdoptVerified = mode == "happy",
                duplicateRuntimeRejected, wrongTargetRejected, conflictingMetadataRejected, doubleSinkSharedEntry,
                logicalDetachPreservedEntry, originalReentryRejected, operationAdoption, singleEventPublication, singleEventBridgeAdoption,
                voidSlot = ready.VoidSlot, writesStarted = ready.WritesStarted,
                runtimeEpoch = ready.RuntimeEpoch, unityEpoch = ready.UnityEpoch, metadataIdentityControlled = true,
                actualIl2CppMetadataResolved = false, mappedImageOnly = true, gameCodeExecuted = false,
                gameInitExecuted = false, transportInvocations = 0, managedObserverCallbacks = counters.Total,
                nativeToClrCallbackVerified = false, actualLedgerCaptureVerified = false, firstInstallSafetyVerified = false,
                gameRuntimeVerified = false, diskHashesUnchanged = true, files = fileProofs
            }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, kind = "mapped-image-managed-preinit-adoption", mode,
                passed = false, checks, failures = 1, failure = error.GetType().Name + ": " + error.Message,
                firstInstallSafetyVerified = false, gameRuntimeVerified = false, metadataIdentityControlled = true }));
            return 1;
        }
    }
    private static T Export<T>(IntPtr helper, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(helper, name));
    private static string RequireFile(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new ArgumentException("An existing absolute native file is required: " + path);
        return Path.GetFullPath(path);
    }
    private static string Hash(string path)
    { using var stream = File.OpenRead(path); using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant(); }
    private static bool Rejected<T>(Action action) where T : Exception
    { try { action(); return false; } catch (T) { return true; } }
    private static NativeRuntimeInvokeInformation ReadRuntime(RuntimeInfoNative read)
    {
        var info = new NativeRuntimeInvokeInformation { Version = 1, Size = 80 };
        if (read(ref info) != 0) throw new InvalidOperationException("The actual native runtime information could not be read.");
        return info;
    }
    private static NativeVoidBootstrapInformation ReadVoid(VoidInfoNative read, uint slot)
    {
        var info = new NativeVoidBootstrapInformation { Version = 1, Size = 56 };
        if (read(slot, ref info) != 0) throw new InvalidOperationException("The actual native Unity parent information could not be read.");
        return info;
    }
    private static NativeListenerInformation ReadListener(ListenerInfoNative read, uint slot)
    {
        var info = new NativeListenerInformation { Version = 1, Size = 176 };
        if (read(slot, ref info) != 0) throw new InvalidOperationException("The actual native listener information could not be read.");
        return info;
    }
    private static bool RuntimeIdentity(NativeRuntimeInvokeInformation value, NativeFirstInstallInformation ready, uint attached)
        => value.Version == 1 && value.Size == 80 && value.Attached == attached && value.Unknown == 0 && value.ObserverFaults == 0
        && value.Target == ready.RuntimeTarget && value.Original == ready.RuntimeOriginal && value.Entry == ready.RuntimeEntry && value.Epoch == ready.RuntimeEpoch;
    private static bool ParentIdentity(NativeVoidBootstrapInformation value, NativeFirstInstallInformation ready, uint attached)
        => value.Version == 1 && value.Size == 56 && value.Slot == ready.VoidSlot && value.Attached == attached && value.Unknown == 0 && value.Reserved == 0
        && value.Target == ready.UnityTarget && value.Original == ready.UnityOriginal && value.Entry == ready.UnityEntry && value.Epoch == ready.UnityEpoch;
    private static bool SameParentIdentity(NativeVoidBootstrapInformation a, NativeVoidBootstrapInformation b)
        => a.Slot == b.Slot && a.Target == b.Target && a.Original == b.Original && a.Entry == b.Entry && a.Epoch == b.Epoch;
    private static bool SamePublishedIdentity(NativeFirstInstallInformation a, NativeFirstInstallInformation b)
        => a.ModuleBase == b.ModuleBase && a.ImageSize == b.ImageSize && a.Init == b.Init
        && a.RuntimeTarget == b.RuntimeTarget && a.RuntimeOriginal == b.RuntimeOriginal && a.RuntimeEntry == b.RuntimeEntry && a.RuntimeEpoch == b.RuntimeEpoch
        && a.UnityTarget == b.UnityTarget && a.UnityOriginal == b.UnityOriginal && a.UnityEntry == b.UnityEntry && a.UnityEpoch == b.UnityEpoch
        && a.MidReturnPc == b.MidReturnPc && a.Relay == b.Relay;
    private static byte[] CodeSnapshot(IntPtr module)
    {
        var result = new List<byte>();
        foreach (var rva in RizOperationRvas)
            result.AddRange(Bytes(IntPtr.Add(module, rva), rva switch {
                0x154e890 or 0x154e930 => 147,
                0x3956f0 => 3576, 0x396be0 => 268, 0x396e40 => 5908, 0x398d60 => 5172, 0x396cf0 => 322,
                0xbc0ff0 => 647,
                _ => 32 }));
        foreach (var (rva, size) in new[] { (0x2af0e0, 64), (0x15642e0, 96), (0x1564410, 48), (0x1564470, 0xc0) })
        { var bytes = new byte[size]; Marshal.Copy(IntPtr.Add(module, rva), bytes, 0, size); result.AddRange(bytes); }
        return result.ToArray();
    }
    private static byte[] Bytes(IntPtr address, int count)
    { var result = new byte[count]; Marshal.Copy(address, result, 0, count); return result; }
    private sealed class Counters
    {
        internal int RuntimeAfter, Captures, Afters, Faults, Mids, ProbeEnters, ProbeExits, ContextDisposals;
        internal int Total => RuntimeAfter + Captures + Afters + Faults + Mids + ProbeEnters + ProbeExits + ContextDisposals;
    }
    private sealed class ObservationContext : IDisposable
    { private readonly Counters _counts; internal ObservationContext(Counters counts) => _counts = counts; public void Dispose() => ++_counts.ContextDisposals; }
    private sealed class NeverInstallEngine : INativeDetourEngine, IPreparedNativeDetourEngine
    {
        internal int InstallCalls, PrepareCalls, CommitCalls;
        public Delegate Install(VerifiedHookTarget target, Delegate replacement)
        { ++InstallCalls; throw new InvalidOperationException("CLR adoption attempted a new native detour."); }
        public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement)
        { ++PrepareCalls; throw new InvalidOperationException("CLR adoption attempted to prepare a new native detour."); }
        public void Commit(VerifiedHookTarget target)
        { ++CommitCalls; throw new InvalidOperationException("CLR adoption attempted to commit a new native detour."); }
    }
    private sealed class MainDispatcher : IMainThreadDispatcher
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        public bool IsMainThread => Environment.CurrentManagedThreadId == _thread;
        public void VerifyAccess() { if (!IsMainThread) throw new InvalidOperationException("The mapped regression changed its controlled main thread."); }
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The mapped regression must not enqueue game work.");
    }
    private sealed class Logger : ILogger
    { internal int Errors; public void Log(LogLevel level, string message, Exception? exception = null) { if (level == LogLevel.Error) ++Errors; } }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW", SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
}
