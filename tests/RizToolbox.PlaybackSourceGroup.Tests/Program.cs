using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

return SourceGroupChecks.Run(args);

internal static partial class SourceGroupChecks
{
    private static int _checks;
    private static void Check(bool value, string reason) { ++_checks; if (!value) throw new InvalidOperationException(reason); }
    private static void Reject(Action action, string reason)
    { ++_checks; try { action(); } catch (UnsupportedSignatureException) { return; } throw new InvalidOperationException(reason); }
    private static string HashFile(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
    private static byte[] Bytes(IntPtr address, int length) { var result = new byte[length]; Marshal.Copy(address, result, 0, length); return result; }
    private static T Export<T>(IntPtr module, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateSource();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateObject(IntPtr parent);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float GetTime(IntPtr self, IntPtr method);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Configure(IntPtr source, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Wait();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Release();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(uint kind, IntPtr target, IntPtr self, ulong bits, IntPtr method, out uint code);
    private sealed class Main : IMainThreadDispatcher
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        public bool IsMainThread => Environment.CurrentManagedThreadId == _thread;
        public void VerifyAccess() { if (!IsMainThread) throw new InvalidOperationException("Wrong fixture installation thread."); }
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) { VerifyAccess(); action(); return Task.CompletedTask; }
    }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint count, uint protection, out uint old);
    [DllImport("kernel32")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint count);
    private static void Write(IntPtr address, byte value)
    {
        Check(VirtualProtect(address, 1, 0x40, out var old), "Tamper page open failed."); Marshal.WriteByte(address, value);
        Check(VirtualProtect(address, 1, old, out _) && FlushInstructionCache((IntPtr)(-1), address, 1), "Tamper protection/cache restore failed.");
    }
    private static readonly Dictionary<string, NativePlaybackSourceRole> Roles = new()
    {
        ["pitch"] = NativePlaybackSourceRole.SetPitch, ["pause"] = NativePlaybackSourceRole.Pause,
        ["replay"] = NativePlaybackSourceRole.Replay, ["resume"] = NativePlaybackSourceRole.Resume,
        ["schedule"] = NativePlaybackSourceRole.PlayScheduled, ["clip"] = NativePlaybackSourceRole.SetClip,
        ["progress"] = NativePlaybackSourceRole.ProgressPlay, ["audiopause"] = NativePlaybackSourceRole.PauseLeaf
    };
    private sealed partial class Fixture
    {
        internal readonly IntPtr Module, Source, Player, Progress;
        internal readonly PlaybackSourceGroupTargets Targets;
        internal readonly ExactHookBroker Broker;
        internal readonly GetTime GetTime;
        internal readonly List<NativePlaybackSourceObservation> Observations = new();
        internal readonly List<NativePlaybackSourceCompletion> Returned = new();
        internal readonly List<object> Trace = new();
        internal readonly NativePlaybackBinding Binding;
        internal readonly NativePlaybackClockState Initial;
        internal readonly string Mode;
        internal PlaybackSourceGroupLease Group = null!;
        internal NativePlaybackGetTimeHookLease GetTimeLease => Group.GetTimeLease;
        internal readonly ExactHookBroker.PlaybackOwner GetTimeOwner;
        internal NativePlaybackClock Clock = null!;
        internal NativePlaybackClockPublication Publication = null!;
        internal NativePlaybackGetTimeHookLease? Adopted;
        internal int GetTimeBefore, GetTimeAfter, GetTimeFaults, SourceFaults, DeferredObserved;
        internal bool Deferred, ReaderFault;
        internal NativePlaybackSourceRole? WithdrawInBefore;
        internal long WithdrawInFlight;
        internal Fixture(string[] args)
        {
            Mode = args[0];
            Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero && Path.GetFileName(args[1]) == "RizToolbox.PlaybackStockFixture.dll", "Only the new owned stock fixture may execute.");
            Module = NativeLibrary.Load(Path.GetFullPath(args[1])); Targets = PlaybackSourceGroupTargets.ResolveOwnedFixture(Module);
            Source = Export<CreateSource>(Module, "StockFixtureCreateSource")();
            Player = Export<CreateObject>(Module, "StockFixtureCreatePlayer")(Source);
            Progress = Export<CreateObject>(Module, "StockFixtureCreateProgress")(Player);
            Check(Source != IntPtr.Zero && Player != IntPtr.Zero && Progress != IntPtr.Zero && Progress != Player, "Distinct native owned storage was not created.");
            Marshal.Copy(new[] { 3.0 }, 0, IntPtr.Add(Source, 24), 1);
            GetTime = Marshal.GetDelegateForFunctionPointer<GetTime>(Targets.GetTime.Address);
            Broker = new ExactHookBroker(Path.GetFullPath(args[4]), new Main(), new Logger());
            Binding = new NativePlaybackBinding("stock-nine-" + Mode, 1, 2, 3, 0x101, 0x102, 0x103,
                Player.ToInt64(), Source.ToInt64(), 0x106, Targets.GetTime.MethodInfo.ToInt64());
            Initial = new NativePlaybackClockState(Binding, 1, NativePlaybackPhase.Paused, 100, 6, 2, 200);
            NativePlaybackSingleReturnSample Sample()
            {
                if (ReaderFault) throw new InvalidOperationException("Intentional exact clock reader fault.");
                var state = Clock?.Capture() ?? Initial;
                return new(NativePlaybackClockPublication.Identity(Binding), state.Revision, NativePlaybackClockPublication.Phase(state), 101);
            }
            if (Mode == "adopt-nine")
                Adopted = Broker.BindPlaybackGetTime(Targets.GetTime, Module, Targets.ImageSha256,
                    Targets.GetTimeBodyLength, Targets.GetTimeBodySha256, Path.GetFullPath(args[2]), HashFile(args[2]),
                    Sample, _ => ++GetTimeFaults, _ => ++GetTimeBefore, (_, _) => ++GetTimeAfter);
            var priorOriginal = Adopted?.OriginalAddress ?? IntPtr.Zero; var priorEntry = Adopted?.EntryAddress ?? IntPtr.Zero;
            Group = Broker.BindPlaybackSourceGroup(Targets, Path.GetFullPath(args[2]), HashFile(args[2]),
                Path.GetFullPath(args[3]), HashFile(args[3]), Sample, _ => ++GetTimeFaults, _ => ++SourceFaults,
                Before, After, _ => ++GetTimeBefore, (_, _) => ++GetTimeAfter, Adopted);
            GetTimeOwner = GetTimeLease.OwnerForGroupAdoption();
            Check(Broker.ReadActualPlaybackDobbyCallCounts() == (11, 11), "Eleven physical entries did not perform exactly eleven actual Prepare/Commit calls.");
            Check(Group.IsActive && GetTimeLease.IsActive && !Group.ProductionAdmission, "The eleven-member installation did not arm as one private group.");
            var leases = PlaybackSourceGroupTargets.SourceRoles().Select(Group.SourceLease).ToArray();
            Check(leases.Length == 10 && leases.All(value => value.IsActive && value.GroupEpoch == Group.Epoch
                && value.PatchedByteCount is 5 or 6 && value.OriginalAddress != value.DobbyOriginalAddress
                && value.RelocatedByteCount == (value.Role == NativePlaybackSourceRole.ProgressMusicPlayer ? 8 : value.Role == NativePlaybackSourceRole.ProgressPlay ? 7 : 6))
                && leases.Select(value => value.TrampolineUnwindAddress).Append(GetTimeLease.TrampolineUnwindAddress).Distinct().Count() == 11,
                "Independent exact relocation, patch and unwind receipts are missing.");
            foreach (var role in PlaybackSourceGroupTargets.SourceRoles())
                Check(Group.MatchesOriginalBody(Targets.SourceTarget(role).Address, Targets.SourceBodySha256(role), Targets.SourceBodyLength(role)),
                    "A complete private source body pin was lost: " + role);
            Check(Group.MatchesOriginalBody(Targets.GetTime.Address, Targets.GetTimeBodySha256, Targets.GetTimeBodyLength), "The private GetTime body pin was lost.");
            if (Adopted is not null)
                Check(ReferenceEquals(Adopted, GetTimeLease) && GetTimeLease.OriginalAddress == priorOriginal && GetTimeLease.EntryAddress == priorEntry,
                    "The actual prior GetTime broker owner was replaced during adoption.");
            Check(GetTime(Player, Targets.GetTime.MethodInfo) == 3, "Dormant GetTime policy did not preserve the original return.");
            Publication = new NativePlaybackClockPublication(GetTimeLease, Initial, IntPtr.Zero, Group.VerifyCurrent);
            Clock = new NativePlaybackClock(Binding, 100, 6, 2, 200, Publication);
            Check(Publication.IsCurrent(Clock.Capture()) && GetTime(Player, Targets.GetTime.MethodInfo) == 6,
                "Actual GetTime did not consume the same final clock publication.");
        }
        private void Before(NativePlaybackSourceObservation observation)
        {
            observation.VerifyCurrent();
            Trace.Add(new { phase = "before", role = observation.Role.ToString(), observation.Sequence, observation.ParentSequence,
                observation.OwnerEpoch, self = observation.Self.ToInt64(), method = observation.IncomingMethod.ToInt64(),
                observation.ArgBits, observation.WriterSequence, observation.SourceSequence, observation.SchedulingSequence });
            if (Deferred) { ++DeferredObserved; observation.Dispose(); } else Observations.Add(observation);
            if (Mode == "callback-before-nine") throw new InvalidOperationException("Intentional Before failure.");
            if (WithdrawInBefore == observation.Role)
            {
                var activity = Group.ReadActivity(); WithdrawInFlight = activity.InFlight;
                Check(activity.Known && activity.InFlight == 1, "The shared source hub counted one entry more than once.");
                Group.Dispose();
                Check(Group.ReadActivity() is { Known: true, InFlight: 1 } && !GetTimeOwner.ReadTransportHealth().AuthorityActive,
                    "Clean callback withdrawal lost actual in-flight responsibility or kept native clock authority.");
            }
        }
        private void After(NativePlaybackSourceCompletion completion)
        {
            completion.VerifyCurrent(); Check(!completion.PhysicalExited, "Returned callback pretended physical exit."); Returned.Add(completion);
            if (Mode == "callback-after-nine") throw new InvalidOperationException("Intentional return observer failure.");
        }
        internal IntPtr Self(NativePlaybackSourceRole role) => role switch
        {
            NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume => Player,
            NativePlaybackSourceRole.ProgressPlay => Progress, _ => Source
        };
        internal int Invoke(NativePlaybackSourceRole role, ulong bits, IntPtr method, out uint code)
            => Export<Invoke>(Module, "StockFixtureInvoke")((uint)Group.SourceLease(role).Kind, Targets.SourceTarget(role).Address, Self(role), bits, method, out code);
        internal void Call(NativePlaybackSourceRole role, ulong bits = 0, IntPtr? method = null)
        { Check(Invoke(role, bits, method ?? Targets.SourceTarget(role).MethodInfo, out var code) == 1 && code == 0, "An actual original did not return normally: " + role); }
        internal void Drain(bool verifyKnown)
        {
            foreach (var observation in Observations)
            {
                Check(observation.TryReadCompletion(out var physical) && physical is { PhysicalExited: true }, "An actual entry lost its exact physical-exit journal receipt.");
                if (verifyKnown) physical!.VerifyCurrent(); observation.Dispose();
                Check(observation.IsReleased, "A completed native source responsibility was not released.");
            }
            Observations.Clear(); Returned.Clear(); Group.PumpCompleted();
        }
        internal void AssertRevoked()
        {
            Reject(Group.VerifyCurrent, "A member fault did not revoke the complete group.");
            Check(!Group.IsActive && !GetTimeLease.IsActive && !GetTimeOwner.ReadTransportHealth().AuthorityActive
                && PlaybackSourceGroupTargets.SourceRoles().All(role => !Group.SourceLease(role).IsActive)
                && Group.ReadActivity() is { Known: false, InFlight: 0 }, "Fault revocation kept authority or lost actual drain.");
        }
    }
    internal static int Run(string[] args)
    {
        var mode = args.Length == 0 ? "invalid" : args[0];
        try
        {
            if (mode.StartsWith("prepare-source-", StringComparison.Ordinal)) return RunSourcePreparation(args);
            Check(args.Length == 5 && OperatingSystem.IsWindows() && IntPtr.Size == 8, "Expected mode fixture GetTime-helper source-helper Dobby.");
            if (mode.StartsWith("precapture-", StringComparison.Ordinal)) return RunPreCapture(args);
            if (mode.StartsWith("commit-gettime-", StringComparison.Ordinal)) return RunGetTimeCommit(args);
            var fixture = new Fixture(args);
            if (mode is "normal-nine" or "adopt-nine" or "stream-nine") RunNormal(fixture); else RunFault(fixture);
            Check(fixture.Broker.ReadActualPlaybackDobbyCallCounts() == (11, 11), "Verification performed an extra physical installation.");
            Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero, "GameAssembly was unexpectedly loaded.");
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "actual-eleven-member-playback-source-group", mode, passed = true,
                checks = _checks, physicalMemberCount = 11, sourceMemberCount = 10, sharedSourceHubCount = 1, epoch = fixture.Group.Epoch,
                actualDobbyPrepareCount = 11, actualDobbyCommitCount = 11, adoptedActualBrokerOwner = fixture.Adopted is not null,
                fixture.WithdrawInFlight, fixture.DeferredObserved, gameCodeExecuted = false, productionAdmission = false, trace = fixture.Trace }));
            GC.KeepAlive(fixture); return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "actual-eleven-member-playback-source-group", mode, passed = false, checks = _checks, error = error.Message })); return 1;
        }
    }
}
