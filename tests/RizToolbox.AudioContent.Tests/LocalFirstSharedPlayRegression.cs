
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using static Assert;

internal static partial class LocalCatalogRegression
{
    internal static int LocalFirstSharedPlay(string[] args)
    {
        try
        {
            Check(args.Length is 6 or 7 && OperatingSystem.IsWindows() && IntPtr.Size == 8);
            var mode = args.Length == 6 ? "retry" : args[6];
            Check(mode is "retry" or "withdraw" or "final-fault" or "partial-final-fault");
            using var native = new NativeLocalRunTerminationBoundaryStateRegression.DormantPlayFixture(args);
            using var catalog = new CatalogFixture(play: native);
            using var context = catalog.Context("joint.local.first");
            var song = catalog.Resources(context);
            var chart = song.Charts.Single() with { Playback = AuthoredPlayback() };
            using var registration = Need(catalog.Catalog.ForPlugin(context).RegisterSong(song with { Charts = new[] { chart } }));
            using var selected = Need(catalog.Catalog.SelectionForPlugin(context).Select(registration.Snapshot.Song, chart.Id));
            native.VerifyUnmodified();
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                Equal(ErrorCode.Cancelled, selected.RequestPlay(cancelled.Token).Error!.Code);
            }
            native.VerifyUnmodified();
            Need(selected.RequestPlay());
            Equal(0, catalog.Catalog.PreparedRequestCount);
            native.RunOfficialAfterLocal(mode);
            Console.WriteLine(JsonSerializer.Serialize(new { passed = true, mode, native.StockCommitted, native.LocalTypedCommits,
                native.DeferredPumps, native.LocalPlayClaims, native.OriginalCalls, native.KnownRetirement, native.PumpPublishedGroup, native.GetTimeStayedOriginal, native.RetainedGetTimeFallback,
                gameExecuted = false, mappedGameImage = false, productionAdmission = false }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

internal static partial class NativeLocalRunTerminationBoundaryStateRegression
{
    private sealed partial class Backend
    {
        private readonly IntPtr _ownedTargets;
        internal (long Prepared, long Committed) ActualOwnedCounts => _broker.ReadActualPlaybackDobbyCallCounts();
        private VerifiedHookTarget OwnedTarget(string export, ExactHookAbi abi, bool returnsReference = false)
        {
            var address = NativeLibrary.GetExport(_ownedTargets, export);
            var method = Marshal.AllocHGlobal(Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
            Marshal.StructureToPtr(new HookTargetValidation.MethodInfo2022 { MethodPointer = address, Class = _ownedTargets }, method, false);
            // The physical originals keep their exact metadata for this process.
            return new("owned.local.fixture." + export, address, method, _ownedTargets, abi,
                returnsReference ? _ownedTargets : IntPtr.Zero);
        }
    }

    internal sealed class DormantPlayFixture : ILocalRunPlayDispatcher, IDisposable
    {
        private readonly MainThreadDispatcher _main = new();
        private readonly ManagementLogger _managementLog = new();
        private readonly ExactHookBroker _stock;
        private readonly Backend _backend;
        private readonly LocalRunProtectionHost _host;
        private readonly PlaybackSourceGroupTargets _targets;
        private readonly IntPtr _progress, _ownedProbes;
        private readonly VerifiedHookTarget _play;
        private readonly Invoke _invoke;
        private readonly string[] _paths;
        private readonly byte[] _originalPlay, _originalGetTime;
        private byte[]? _installedPlay;
        private PlaybackSourceGroupLease? _group;
        internal int DeferredPumps;
        internal bool KnownRetirement, PumpPublishedGroup, GetTimeStayedOriginal, RetainedGetTimeFallback;
        internal long LocalTypedCommits { get; private set; }
        internal long StockCommitted => _backend.ActualOwnedCounts.Committed - LocalTypedCommits;
        internal int LocalPlayClaims => _backend.SharedPlayClaims;
        internal int OriginalCalls => Marshal.ReadInt32(_progress, 0x44);

        internal DormantPlayFixture(string[] paths)
        {
            VerifyNoGame();
            _paths = paths;
            Equal(OwnedPlaybackFixtureProfile.WrapperSha256, Hash(paths[0]));
            Equal(OwnedPlaybackFixtureProfile.LowerSha256, Hash(paths[1]));
            var wrapper = NativeLibrary.Load(paths[0]); var lower = NativeLibrary.Load(paths[1]);
            Equal(1, Export<BindLower>(wrapper, "StockFixtureBindLowerPitch")(NativeLibrary.GetExport(lower, "StockLowerSetPitch")));
            Equal(1, Export<BindLower>(wrapper, "StockFixtureBindLowerTime")(NativeLibrary.GetExport(lower, "StockLowerSetTime")));
            _targets = PlaybackSourceGroupTargets.ResolveOwnedLowerTimeFixture(wrapper, lower);
            _play = _targets.ProgressPlay with { Abi = ExactHookAbi.LocalProgressPlayBooleanVoid };
            _originalPlay = PlayBytes();
            _originalGetTime = GetTimeBytes();
            var source = Export<Create>(wrapper, "StockFixtureCreateSource")();
            var player = Export<CreateWith>(wrapper, "StockFixtureCreatePlayer")(source);
            _progress = Export<CreateWith>(wrapper, "StockFixtureCreateProgress")(player);
            Check(source != IntPtr.Zero && player != IntPtr.Zero && _progress != IntPtr.Zero);
            _invoke = Export<Invoke>(wrapper, "StockFixtureInvoke");
            _stock = new(paths[4], _main, _managementLog);
            _ownedProbes = NativeLibrary.Load(paths[5]);
            _backend = new(_main, paths[4], _ownedProbes) { SharedPlayTarget = _play, InitializationEnabled = true };
            _host = LocalRunProtectionHost.Create(_main, new Logger(), new Transport());
        }

        internal void VerifyUnmodified()
        {
            Equal((0L, 0L), _backend.ActualOwnedCounts);
            Check(_originalPlay.AsSpan().SequenceEqual(PlayBytes()) && _originalGetTime.AsSpan().SequenceEqual(GetTimeBytes()));
            Check(_stock.ReadDormantPlaybackTargets(_targets.Module) is null);
        }

        public Result RequestPlay(LocalPlayRequest request, CancellationToken cancellationToken)
        {
            Check(!cancellationToken.IsCancellationRequested);
            _ = LocalChartPlaybackMetadataValidation.Require(request);
            using var play = _backend.ReserveSharedPlay();
            Check(!play.SharesOfficialPlay);
            play.PublishDormantSharedPlay(_targets, _paths[3], Hash(_paths[3]), foundation: null);
            Check(play.SharesOfficialPlay);
            _installedPlay = PlayBytes();
            Check(!_originalPlay.AsSpan().SequenceEqual(_installedPlay) && _originalGetTime.AsSpan().SequenceEqual(GetTimeBytes()));
            Equal((1L, 1L), _backend.ActualOwnedCounts);
            NativeLocalRunTerminationBoundaryState bridge;
            try { bridge = NativeLocalRunTerminationBoundaryState.CreateBackend(_main, _host, _backend); }
            catch (Exception error) { Console.Error.WriteLine(error); throw; }
            _host.InstallHandler(new LocalRunTerminationHandler(_main, _host.Sessions, bridge, new PendingSink(), new PendingCommands()));
            Check(_host.Read() is { AllGatesInstalled: true, RetainedBindings: 5, CleanupUnknown: false });
            var controller = _progress.ToInt64() - 2; // Controlled host graph.
            using var borrowed = new NativeRuntime.RootHandle(99, _ => { }, _ => new(controller), _main.VerifyAccess);
            var selection = request.Selection;
            var session = _host.RegisterPrepared(new(selection.Song.Song, selection.Song.Revision, selection.Chart.Id), controller + 3, request.Retain());
            var commit = _host.Sessions.BeginCommit(session);
            _host.Sessions.BindPendingController(commit, controller, new Lease());
            _host.Sessions.Commit(commit);
            bridge.RegisterCommitted(session, borrowed);
            Check(bridge.IsReadyForHost(_host.Sessions));
            Play(_progress, 0x80);
            Check(_backend.InitializationBinding!.Describe() is { PlayCalls: 1, InFlight: 0, UnknownObserved: 0 });
            Equal(1, LocalPlayClaims);
            _host.Sessions.BeginReturn(session);
            var proof = _host.Sessions.VerifyConsumersEnded(session, new Verifier())!;
            Check(_host.Sessions.Close(session, proof) && bridge.TryRetire(session));
            Check(bridge.TryStop());
            _host.RequestStop();
            return Result.Ok();
        }

        internal void RunOfficialAfterLocal(string mode)
        {
            LocalTypedCommits = _backend.ActualOwnedCounts.Committed - 1;
            Equal(2L, LocalTypedCommits);
            var getTime = Marshal.GetDelegateForFunctionPointer<GetTime>(_targets.GetTime.Address);
            var player = Marshal.ReadIntPtr(_progress, 0x38);
            var originalClock = getTime(player, _targets.GetTime.MethodInfo);
            Check(ReferenceEquals(_targets, _stock.ReadDormantPlaybackTargets(_targets.Module)));
            var signals = new List<string>();
            var faults = new List<string>();
            using var pending = _stock.BeginPlaybackSourceGroupPreparation(_targets, _paths[2], Hash(_paths[2]),
                _paths[3], Hash(_paths[3]), () => throw new InvalidOperationException("The owned fixture cannot grant Clock policy."),
                fault => faults.Add(fault.Reason), fault => faults.Add(fault.Reason),
                observation => { signals.Add(observation.Role.ToString()); observation.DisposeOperation(); },
                completion => signals.Add(completion.Observation.Role.ToString()));
            var logger = _managementLog;
            using var host = new NativePlaybackStockRateHost(null!, _main, logger, new GameEventState(), null!, _paths[4]);
            var files = NativePlaybackFilePreparation.PreparedFiles.Read(_paths[0], Hash(_paths[0]), _paths[2], _paths[3], default);
            Field("_nativePreparation").SetValue(host, pending);
            Field("_installationFiles").SetValue(host, files);
            logger.OnPhysicalGroupReady = () =>
            {
                _group = (PlaybackSourceGroupLease?)Field("_group").GetValue(host);
                Check(_group is { IsActive: true } && !pending.WaitingForDormantPlay);
                Equal((25L, 25L), _backend.ActualOwnedCounts);
                Check(!_originalGetTime.AsSpan().SequenceEqual(GetTimeBytes()) && _installedPlay!.AsSpan().SequenceEqual(PlayBytes()));
                _group!.VerifyCurrent();
                Play(_progress, 0xff);
                Equal(2, OriginalCalls);
                Equal(1, LocalPlayClaims);
                Check(faults.Count == 0);
                PumpPublishedGroup = true;
                // Without an actual pinned game module this fixture stops at
                // the manager's verified physical group before IL2CPP attach.
                host.Dispose();
            };
            for (var frame = 0; StockCommitted < 20 && frame < 56; ++frame)
            {
                host.PumpCompleted();
                Check(host.Stage == NativePlaybackStockHostStage.WaitingForInstallation && !pending.WaitingForDormantPlay);
            }
            Equal((25L, 22L), _backend.ActualOwnedCounts);
            Check(_originalGetTime.AsSpan().SequenceEqual(GetTimeBytes()) && signals.Count == 0);

            // The source factory object is an owned fixture. A one-shot guard on
            // its private clone pauses the worker's real original before its first
            // receiver dereference while no managed LOCAL subscriber is attached.
            var beforeBorrow = OriginalCalls;
            using (var borrowing = new OwnedPlayBorrow(_ownedProbes, _progress, address =>
            {
                Equal(1, _invoke(5, _play.Address, address, 0x81, _play.MethodInfo, out var exception));
                Equal(0u, exception);
                Equal(beforeBorrow + 1, Marshal.ReadInt32(address, 0x44));
                Equal(0x81L, Marshal.ReadInt64(address, 0x50));
            }))
            {
                Check(borrowing.EnteredOriginal);
                for (int poll = 0; poll < 2; ++poll)
                {
                    host.PumpCompleted();
                    Check(pending.WaitingForDormantPlay && host.Stage == NativePlaybackStockHostStage.WaitingForInstallation
                        && host.Detail.Contains("current Play call", StringComparison.Ordinal) && !host.Probe().Supported
                        && Field("_group").GetValue(host) is null);
                    Equal((25L, 22L), _backend.ActualOwnedCounts);
                    Check(_originalGetTime.AsSpan().SequenceEqual(GetTimeBytes()) && _installedPlay!.AsSpan().SequenceEqual(PlayBytes()));
                    Check(signals.Count == 0 && faults.Count == 0);
                    ++DeferredPumps;
                }
                if (mode == "withdraw") host.Dispose();
            }

            if (mode is "final-fault" or "partial-final-fault")
            {
                logger.FailBefore = mode == "final-fault" ? "source preparation complete; precommit validation entering"
                    : "source commit entering: SetPitch";
                host.PumpCompleted();
            }

            if (mode != "retry")
            {
                Check(!pending.IsOpen && !PumpPublishedGroup && host.Stage == NativePlaybackStockHostStage.Withdrawn
                    && Field("_group").GetValue(host) is null && !host.Probe().Supported);
                var partialPhysicalWrite = mode == "partial-final-fault";
                Equal((25L, partialPhysicalWrite ? 23L : 22L), _backend.ActualOwnedCounts);
                GetTimeStayedOriginal = _originalGetTime.AsSpan().SequenceEqual(GetTimeBytes());
                Check(GetTimeStayedOriginal == !partialPhysicalWrite && _installedPlay!.AsSpan().SequenceEqual(PlayBytes()));
                RetainedGetTimeFallback = getTime(player, _targets.GetTime.MethodInfo).Equals(originalClock);
                Check(RetainedGetTimeFallback);
                Play(_progress, 0xff);
                Equal(2, OriginalCalls);
                Equal(1, LocalPlayClaims);
                VerifyNoGame();
                return;
            }

            host.PumpCompleted();
            Check(PumpPublishedGroup && host.Stage == NativePlaybackStockHostStage.Withdrawn && _group is { IsActive: false });
            KnownRetirement = _group!.ReadActivity() is { Known: true, InFlight: 0 };
            Check(KnownRetirement);
            VerifyNoGame();

            static FieldInfo Field(string name) => typeof(NativePlaybackStockRateHost).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        }

        private void Play(IntPtr progress, byte raw)
        {
            var previous = Marshal.ReadInt32(progress, 0x44);
            Equal(1, _invoke(5, _play.Address, progress, raw, _play.MethodInfo, out var exception));
            Equal(0u, exception);
            Equal(previous + 1, Marshal.ReadInt32(progress, 0x44));
            Equal((long)raw, Marshal.ReadInt64(progress, 0x50));
        }
        private byte[] PlayBytes() => NativePlaybackExecutableSeal.RangeSeal.Read(_play.Address, _targets.SourceBodyLength(NativePlaybackSourceRole.ProgressPlay));
        private byte[] GetTimeBytes() => NativePlaybackExecutableSeal.RangeSeal.Read(_targets.GetTime.Address, _targets.GetTimeBodyLength);
        private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
        private static void VerifyNoGame() => Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero && GetModuleHandle("UnityPlayer.dll") == IntPtr.Zero);
        public void Dispose()
        {
            _group?.Dispose();
            _host.RequestStop();
            _main.Dispose();
        }

        private sealed class ManagementLogger : ILogger
        {
            internal Action? OnPhysicalGroupReady;
            internal string? FailBefore;
            public void Log(LogLevel level, string message, Exception? error = null)
            {
                if (FailBefore is { } boundary && message.Contains(boundary, StringComparison.Ordinal))
                    throw new InvalidOperationException("Owned final-audit refusal.");
                if (message.Contains("physical group complete; attaching source profiles", StringComparison.Ordinal))
                    OnPhysicalGroupReady?.Invoke();
            }
        }

        private static T Export<T>(IntPtr module, string name) where T : Delegate
            => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Create();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateWith(IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BindLower(IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float GetTime(IntPtr receiver, IntPtr method);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(uint kind, IntPtr target, IntPtr self, ulong arg, IntPtr method, out uint exception);
        [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    }


    // The ordinary host and Play path stay production code. Synthetic
    // Start/Getter methods still have real typed originals and activity.
    private sealed class OwnedInitializationBridge : INativeInitializationContextBridge
    {
        private readonly NativeInitializationContextBridge _native;
        internal OwnedInitializationBridge(NativeInitializationKind kind)
            => _native = new(Path.Combine(AppContext.BaseDirectory, "RizToolbox.JudgeContext.dll"), kind, new Logger());
        public NativeInitializationKind Kind => _native.Kind;
        public IntPtr EntryAddress => _native.EntryAddress;
        public void Configure(IntPtr original, NativeInitializationEnter enter, NativeInitializationExit exit, Action<string> fault)
            => _native.Configure(original, enter, exit, fault);
        public NativeInitializationActivity ReadActivity() => _native.ReadActivity();
    }

    private sealed class OwnedPlayBorrow : IDisposable
    {
        private const int ProgressFixtureSize = 0x60, StartField = 0x42;
        private readonly IntPtr _page;
        private readonly IntPtr _registration;
        private readonly ManualResetEvent _entered = new(false), _release = new(false);
        private readonly ReadInstruction _instruction;
        private readonly Task _original;
        internal bool EnteredOriginal => _entered.WaitOne(0) && _instruction() != IntPtr.Zero && !_original.IsCompleted;

        internal OwnedPlayBorrow(IntPtr probes, IntPtr progress, Action<IntPtr> call)
        {
            _page = VirtualAlloc(IntPtr.Zero, 4096, 0x3000, 4);
            Check(_page != IntPtr.Zero);
            var bytes = NativePlaybackExecutableSeal.RangeSeal.Read(progress, ProgressFixtureSize);
            Marshal.Copy(bytes, 0, _page, bytes.Length);
            Marshal.WriteByte(_page, StartField, 0x81);
            var configure = Marshal.GetDelegateForFunctionPointer<ConfigureGuard>(NativeLibrary.GetExport(probes, "OwnedGuardConfigure"));
            _instruction = Marshal.GetDelegateForFunctionPointer<ReadInstruction>(NativeLibrary.GetExport(probes, "OwnedGuardInstruction"));
            configure(IntPtr.Add(_page, StartField), _entered.SafeWaitHandle.DangerousGetHandle(),
                _release.SafeWaitHandle.DangerousGetHandle());
            _registration = AddVectoredExceptionHandler(1, NativeLibrary.GetExport(probes, "OwnedGuardHandler"));
            Check(_registration != IntPtr.Zero && VirtualProtect(_page, 4096, 0x104, out _));
            _original = Task.Factory.StartNew(() => call(_page), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            if (!_entered.WaitOne(TimeSpan.FromSeconds(5))) { Dispose(); throw new InvalidOperationException("Owned Play did not reach its guarded receiver read."); }
        }

        public void Dispose()
        {
            _release.Set();
            try { _original.GetAwaiter().GetResult(); }
            finally
            {
                RemoveVectoredExceptionHandler(_registration);
                VirtualFree(_page, 0, 0x8000);
                _entered.Dispose();
                _release.Dispose();
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ConfigureGuard(IntPtr address, IntPtr entered, IntPtr release);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ReadInstruction();
        [DllImport("kernel32")] private static extern IntPtr AddVectoredExceptionHandler(uint first, IntPtr callback);
        [DllImport("kernel32")] private static extern uint RemoveVectoredExceptionHandler(IntPtr registration);
        [DllImport("kernel32")] private static extern IntPtr VirtualAlloc(IntPtr address, nuint size, uint allocation, uint protect);
        [DllImport("kernel32")] private static extern bool VirtualProtect(IntPtr address, nuint size, uint protect, out uint old);
        [DllImport("kernel32")] private static extern bool VirtualFree(IntPtr address, nuint size, uint free);
    }
}

