using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

return Checks.Run(args);

internal static partial class Checks
{
    private static int _checks;
    private static void Check(bool value, string reason) { ++_checks; if (!value) throw new InvalidOperationException(reason); }
    private static void Reject(Action action, string reason)
    { ++_checks; try { action(); } catch (UnsupportedSignatureException) { return; } throw new InvalidOperationException(reason); }
    private static T Clone<T>(T value) where T : class => (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null)!;
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
    private static T Export<T>(IntPtr module, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr NewSource();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr NewObject(IntPtr parent);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(uint kind, IntPtr target, IntPtr self, ulong bits, IntPtr method, out uint code);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float GetTime(IntPtr self, IntPtr method);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    private sealed class Main : IMainThreadDispatcher
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        public bool IsMainThread => Environment.CurrentManagedThreadId == _thread;
        public void VerifyAccess() { if (!IsMainThread) throw new InvalidOperationException("Wrong thread."); }
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default) { VerifyAccess(); action(); return Task.CompletedTask; }
    }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
    private sealed class Fixture
    {
        internal readonly IntPtr Module, Source, Player, Progress;
        internal readonly PlaybackSourceGroupTargets Targets;
        internal readonly ExactHookBroker Broker;
        internal readonly PlaybackSourceGroupLease Group;
        internal readonly NativePlaybackStockOccurrenceProducer Producer;
        internal readonly List<NativePlaybackSourceObservation> Before = new();
        internal readonly List<object> Trace = new();
        internal readonly string Mode;
        internal NativePlaybackStockOccurrenceProducer.ExecutionReceipt? Captured;
        internal int Returns, InflightReturns;
        internal NativePlaybackStockOccurrenceProducer.ContinuationReceipt? WriterResult;
        internal Action<NativePlaybackSourceObservation>? WriterBefore;
        private bool _injected;
        internal Fixture(string[] args)
        {
            Mode = args[0];
            Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero, "No GameAssembly execution is permitted.");
            Module = NativeLibrary.Load(args[1]); Targets = PlaybackSourceGroupTargets.ResolveOwnedFixture(Module);
            Source = Export<NewSource>(Module, "StockFixtureCreateSource")();
            Player = Export<NewObject>(Module, "StockFixtureCreatePlayer")(Source);
            Progress = Export<NewObject>(Module, "StockFixtureCreateProgress")(Player);
            Broker = new ExactHookBroker(args[4], new Main(), new Logger());
            NativePlaybackStockOccurrenceProducer? producer = null;
            Group = Broker.BindPlaybackSourceGroup(Targets, args[2], Hash(args[2]), args[3], Hash(args[3]),
                () => throw new InvalidOperationException("No clock policy is admitted by S2."), _ => { }, _ => { }, observation =>
                {
                    Before.Add(observation);
                    if (Mode == "copies")
                    {
                        Reject(() => producer!.ObserveBefore(Clone(observation)), "A copied actual observation was admitted.");
                    }
                    producer!.ObserveBefore(observation);
                    WriterBefore?.Invoke(observation);
                    if (Mode == "copies") Reject(() => producer!.ObserveBefore(observation), "A duplicate exact Before issued another occurrence.");
                    Trace.Add(new { phase = "before", role = observation.Role.ToString(), observation.Sequence, observation.ParentSequence,
                        observation.OwnerEpoch, self = observation.Self.ToInt64(), caller = observation.Caller.ToInt64(), observation.ArgBits });
                    if (Mode == "extra-leaf" && !_injected && observation.Role == NativePlaybackSourceRole.Replay)
                    { _injected = true; Call(NativePlaybackSourceRole.PlayScheduled, DoubleBits(555.125)); }
                }, completion =>
                {
                    if (Mode == "copies") Reject(() => producer!.ObserveReturned(Clone(completion)), "A copied actual completion was admitted.");
                    producer!.ObserveReturned(completion);
                    if (Mode == "copies") Reject(() => producer!.ObserveReturned(completion), "A duplicate exact return issued another receipt.");
                });
            Producer = producer = new(Group, Player, Source, Progress, receipt =>
            {
                ++Returns; Captured = receipt;
                receipt.VerifyCurrent(); Check(!receipt.PhysicalExited, "Stock Returned pretended physical exit.");
                Check(Group.ReadActivity().InFlight == 1, "Outer physical responsibility was not retained in Returned.");
                ++InflightReturns;

                if (receipt.Schedule is { } schedule)
                {
                    schedule.VerifyCurrent();
                    Check(schedule.RawDoubleBits == unchecked((ulong)Marshal.ReadInt64(Source, 112))
                        && schedule.ParentSequence == receipt.Sequence && schedule.Source == Source,
                        "The private schedule witness differs from its actual source/parent/raw64 original argument.");
                }
                if (Mode.StartsWith("writer-", StringComparison.Ordinal) && receipt.Role is NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume)
                    WriterResult = producer!.WritePitch(receipt, 2);
                if (Mode == "callback-error") throw new InvalidOperationException("Intentional stock receipt consumer failure.");
            });
            Check(Broker.ReadActualPlaybackDobbyCallCounts() == (12, 12) && Group.IsActive && !Producer.ProductionAdmission,
                "S2 needs one actual twelve-member group and no production admission.");
        }
        internal static ulong SingleBits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));
        internal static ulong DoubleBits(double value) => unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        internal IntPtr Self(NativePlaybackSourceRole role) => role is NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume
            ? Player : role == NativePlaybackSourceRole.ProgressPlay ? Progress : Source;
        internal (int Returned, uint Code) Call(NativePlaybackSourceRole role, ulong bits = 0)
        {
            var value = Export<Invoke>(Module, "StockFixtureInvoke")((uint)Group.SourceLease(role).Kind, Targets.SourceTarget(role).Address,
                Self(role), bits, Targets.SourceTarget(role).MethodInfo, out var code);
            return (value, code);
        }
        internal NativePlaybackStockOccurrenceProducer.ExecutionReceipt Success(NativePlaybackSourceRole role, ulong bits = 0)
        {
            var before = Returns;
            var objectAddress = role == NativePlaybackSourceRole.ProgressPlay ? Progress : Player;
            var offset = role switch { NativePlaybackSourceRole.Pause => 64, NativePlaybackSourceRole.Replay => 56, NativePlaybackSourceRole.Resume => 60, _ => 68 };
            var originalCalls = Marshal.ReadInt32(objectAddress, offset);
            Check(Call(role, bits) == (1, 0u), "The original did not return normally: " + role);
            var receipt = Producer.Current;
            Check(receipt is not null && Returns == before + 1 && receipt.Role == role && receipt.ArgBits == bits,
                "The actual stock entry did not issue one exact private execution receipt: " + role);
            receipt!.VerifyCurrent(); Check(receipt.PhysicalExited && Group.ReadActivity() is { Known: true, InFlight: 0 }, "The actual physical journal did not complete.");
            Check(Marshal.ReadInt32(objectAddress, offset) == originalCalls + 1, "The actual stock original body was not called exactly once.");
            Trace.Add(new { phase = "completed", role = role.ToString(), receipt.Sequence, receipt.WriterSequence, receipt.SchedulingSequence,
                rawSchedule = receipt.Schedule?.RawDoubleBits, actualSchedule = receipt.Schedule?.ActualStartDsp });
            Producer.PumpCompleted(); return receipt;
        }
        internal void NoReceipt(NativePlaybackSourceRole role, ulong bits = 0)
        {
            var prior = Returns; Check(Call(role, bits) == (1, 0u), "The rejected/no-op original did not return exactly normally.");
            Check(Producer.Current is null && Returns == prior && Producer.LastRejection is not null, "A rejected original signed stock completion.");
            Producer.PumpCompleted();
            AssertPhysicalHealthy();
        }
        internal void AssertPhysicalHealthy()
        {
            Group.VerifyCurrent(); Group.GetTimeLease.VerifyCurrent(Module, Targets.GetTime.Address, Targets.GetTime.MethodInfo);
            Check(Group.IsActive && Group.GetTimeLease.IsActive && Group.ReadSourceHealth() is { Known: true, InFlight: 0 }
                && Group.SourceLease(NativePlaybackSourceRole.ProgressPlay).RelocatedByteCount == 7,
                "An ordinary logical boundary withdrew the healthy physical eleven-member installation.");
            Check(float.IsFinite(Export<GetTime>(Module, "StockGetTime")(Player, Targets.GetTime.MethodInfo)), "The same actual GetTime detour stopped forwarding after a logical boundary.");
        }
    }
    internal static int Run(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "invalid";
        try
        {
            Check(args.Length == 5, "Expected mode fixture GetTime-helper source-helper Dobby.");
            var f = new Fixture(args);
            if (mode.StartsWith("writer-", StringComparison.Ordinal)) return WriterChecks(f, mode);
            if (mode is "normal" or "copies" or "no-op")
            {
                Marshal.WriteInt32(f.Source, 48, 1); Marshal.WriteByte(f.Player, 24, 1);
                var paused = f.Success(NativePlaybackSourceRole.Pause);
                Check(paused.Schedule is null && Marshal.ReadInt32(f.Source, 48) == 0 && Marshal.ReadByte(f.Player, 24) == 0,
                    "Actual Pause leaf/origin was not observed.");
                var replay = f.Success(NativePlaybackSourceRole.Replay, Fixture.SingleBits(.125f));
                Reject(paused.VerifyCurrent, "An old Pause receipt survived the next actual occurrence.");
                Check(replay.Schedule!.RawDoubleBits == Fixture.DoubleBits(100.125) && Marshal.ReadInt32(f.Source, 20) == 1,
                    "Replay lost its exact actual Double anchor/reset pitch.");
                Reject(Clone(replay).VerifyCurrent, "A copied execution receipt was admitted.");
                Reject(Clone(replay.Schedule!).VerifyCurrent, "A copied schedule witness was admitted.");
                f.NoReceipt(NativePlaybackSourceRole.Replay, Fixture.SingleBits(.125f));
                Reject(replay.VerifyCurrent, "A stock no-op revived its predecessor.");
                f.Success(NativePlaybackSourceRole.Pause);
                Marshal.Copy(new[] { 4.25 }, 0, IntPtr.Add(f.Source, 24), 1);
                var resume = f.Success(NativePlaybackSourceRole.Resume, Fixture.SingleBits(.25f));
                Check(resume.Schedule!.RawDoubleBits == Fixture.DoubleBits(100.25) && BitConverter.Int64BitsToDouble(Marshal.ReadInt64(f.Player, 32)) == 96,
                    "Resume was inferred from post-origin rather than the distinct actual schedule argument.");
                f.NoReceipt(NativePlaybackSourceRole.Resume, Fixture.SingleBits(.25f));
                Marshal.WriteByte(f.Progress, 0x42, 0);
                f.NoReceipt(NativePlaybackSourceRole.ProgressPlay, 0);
                Check(Marshal.ReadByte(f.Progress, 0x43) == 1, "The actual seven-byte CMP/JE no-op branch changed flags.");
                f.Success(NativePlaybackSourceRole.ProgressPlay, 1);
                Check(Marshal.ReadByte(f.Progress, 0x43) == 0 && Marshal.ReadByte(f.Player, 24) == 0, "The actual inline Pause branch did not run.");
                if (mode == "copies")
                    foreach (var observation in f.Before.Where(value => !value.IsReleased).ToArray())
                        Reject(() => f.Producer.ObserveBefore(observation), "A late observation issued another occurrence.");
            }
            else if (mode == "different-source")
            {
                var foreign = Export<NewSource>(f.Module, "StockFixtureCreateSource")();
                Marshal.WriteIntPtr(f.Player, 16, foreign); f.NoReceipt(NativePlaybackSourceRole.Replay, Fixture.SingleBits(.125f));
            }
            else if (mode == "foreign-owner")
            {
                var legitimate = f.Success(NativePlaybackSourceRole.Replay, Fixture.SingleBits(.125f));
                Marshal.WriteInt32(f.Source, 48, 0);
                var path = Path.Combine(Path.GetDirectoryName(args[3])!, "ForeignPlaybackSource.dll");
                Check(File.Exists(path) && Hash(path) == Hash(args[3]), "The isolated foreign helper copy is not pinned.");
                ulong foreignEpoch = 0;
                var foreign = new NativePlaybackStockSourceBridge(path, Hash(path), NativePlaybackSourceKind.Void0, NativePlaybackSourceRole.Pause,
                    f.Targets.Pause.Address, _ => f.Group.SourceLease(NativePlaybackSourceRole.Pause).OriginalAddress, observation =>
                    {
                        foreignEpoch = observation.OwnerEpoch;
                        Reject(() => f.Producer.ObserveBefore(observation), "A foreign hub with equal epoch issued stock evidence.");
                        observation.Dispose();
                    }, completion => Reject(() => f.Producer.ObserveReturned(completion), "A foreign return issued stock evidence."), _ => { });
                foreign.Arm();
                Check(Export<Invoke>(f.Module, "StockFixtureInvoke")((uint)NativePlaybackSourceKind.Void0, foreign.EntryAddress, f.Player, 0,
                    f.Targets.Pause.MethodInfo, out var code) == 1 && code == 0, "The actual foreign original did not return.");
                foreign.PumpCompleted();
                Check(foreignEpoch == f.Before[0].OwnerEpoch && foreign.ReadHealth() is { Known: true, InFlight: 0 }, "The negative did not use a separate healthy hub with the same native epoch.");
                legitimate.VerifyCurrent();
            }
            else if (mode == "extra-leaf") f.NoReceipt(NativePlaybackSourceRole.Replay, Fixture.SingleBits(.125f));
            else if (mode is "external-pause" or "external-schedule")
            {
                var prior = f.Success(NativePlaybackSourceRole.Replay, Fixture.SingleBits(.125f));
                Check(f.Call(mode == "external-pause" ? NativePlaybackSourceRole.PauseLeaf : NativePlaybackSourceRole.PlayScheduled, Fixture.DoubleBits(999)) == (1, 0u), "External original did not run.");
                Check(f.Producer.Current is null, "An external leaf kept stock execution authority.");
                Reject(prior.VerifyCurrent, "An external leaf left an old stock receipt valid.");
            }
            else if (mode == "callback-error")
            {
                Check(f.Call(NativePlaybackSourceRole.Replay, Fixture.SingleBits(.125f)) == (1, 0u), "Observer error escaped instead of preserving original return.");
                Check(f.Captured is not null && !f.Group.IsActive, "Callback error kept the group active.");
                Reject(f.Captured!.VerifyCurrent, "A callback-faulted receipt became a valid stock completion.");
            }
            else if (mode is "leaf-error-schedule" or "leaf-error-pause")
            {
                var pause = mode == "leaf-error-pause";
                Marshal.WriteInt32(f.Source, 68, pause ? 32 : 4);
                if (pause) Marshal.WriteInt32(f.Source, 48, 1);
                var result = f.Call(pause ? NativePlaybackSourceRole.Pause : NativePlaybackSourceRole.Replay, pause ? 0 : Fixture.SingleBits(.25f));
                Check(result.Returned == 2 && result.Code == (pause ? 0xe2400014u : 0xe2400012u), "The actual nested leaf SEH changed.");
                Check(f.Returns == 0 && f.Producer.Current is null && !f.Group.IsActive, "A leaf exception signed stock completion.");
            }
            else if (mode.StartsWith("original-error-", StringComparison.Ordinal))
            {
                var role = mode.EndsWith("pause", StringComparison.Ordinal) ? NativePlaybackSourceRole.Pause
                    : mode.EndsWith("resume", StringComparison.Ordinal) ? NativePlaybackSourceRole.Resume : NativePlaybackSourceRole.Replay;
                Marshal.WriteInt32(f.Player, 44, role == NativePlaybackSourceRole.Pause ? 1 : role == NativePlaybackSourceRole.Replay ? 2 : 4);
                if (role == NativePlaybackSourceRole.Pause) Marshal.WriteInt32(f.Source, 48, 1);
                var result = f.Call(role, role == NativePlaybackSourceRole.Pause ? 0 : Fixture.SingleBits(.25f));
                Check(result.Returned == 2 && result.Code == (role == NativePlaybackSourceRole.Pause ? 0xe2400015u : role == NativePlaybackSourceRole.Replay ? 0xe2400016u : 0xe2400017u),
                    "The exact original SEH did not survive observation.");
                Check(f.Returns == 0 && f.Producer.Current is null && !f.Group.IsActive, "An original exception signed a stock completion.");
            }
            else throw new InvalidOperationException("Unknown finite mode.");
            f.Producer.Dispose(); f.Group.PumpCompleted();
            if (mode != "callback-error" && !mode.StartsWith("original-error-", StringComparison.Ordinal) && !mode.StartsWith("leaf-error-", StringComparison.Ordinal))
                f.AssertPhysicalHealthy();
            Check(f.Group.ReadActivity().InFlight == 0 && f.Broker.ReadActualPlaybackDobbyCallCounts() == (12, 12), "Actual physical responsibilities did not drain exactly once.");
            Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero, "GameAssembly unexpectedly loaded.");
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "actual-stock-occurrence-execution", mode, passed = true, checks = _checks,
                receiptReturns = f.Returns, returnedBeforePhysicalExit = f.InflightReturns, actualDobbyPrepare = 12, actualDobbyCommit = 12,
                gameCodeExecuted = false, productionAdmission = false, providerIssued = false, writerReservationIssued = false, trace = f.Trace }));
            GC.KeepAlive(f); return 0;
        }
        catch (Exception error)
        { Console.Error.WriteLine(error); Console.WriteLine(JsonSerializer.Serialize(new { kind = "actual-stock-occurrence-execution", mode, passed = false, checks = _checks, error = error.Message })); return 1; }
    }
}
