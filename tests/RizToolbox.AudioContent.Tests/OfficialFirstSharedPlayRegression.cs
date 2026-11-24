using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using static Assert;

internal static partial class LocalCatalogRegression
{
    internal static int OfficialFirstSharedPlay(string[] args)
    {
        try
        {
            Check(args.Length == 6 && OperatingSystem.IsWindows() && IntPtr.Size == 8);
            var shared = args[0] == "shared";
            Check(shared || args[0] == "legacy");
            using var native = new NativeLocalRunTerminationBoundaryStateRegression.SharedPlayFixture(args.Skip(1).ToArray());
            using var catalog = new CatalogFixture(play: native);
            using var context = catalog.Context("joint.local.plugin");
            var song = catalog.Resources(context);
            var chart = song.Charts.Single() with { Playback = AuthoredPlayback() };
            using var registration = Need(catalog.Catalog.ForPlugin(context).RegisterSong(song with { Charts = new[] { chart } }));
            using var selected = Need(catalog.Catalog.SelectionForPlugin(context).Select(registration.Snapshot.Song, chart.Id));
            Check(selected.IsCurrent && native.LocalBindings == 0 && native.LocalProgressPlayCalls == 0);
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                Equal(ErrorCode.Cancelled, selected.RequestPlay(cancelled.Token).Error!.Code);
            }
            Check(native.LocalBindings == 0 && native.CatalogRequests == 0);
            var result = selected.RequestPlay();
            if (shared) Need(result);
            else
            {
                Equal(ErrorCode.Conflict, result.Error!.Code);
                Check(result.Error.Message.Contains("Official playback retains ProgressControl.Play", StringComparison.Ordinal));
            }
            Check(native.CatalogRequests == 1 && catalog.Catalog.PreparedRequestCount == 0);
            native.VerifyThenPlayOfficialAgain(shared);
            Console.WriteLine(JsonSerializer.Serialize(new { passed = true, mode = args[0], plugin = context.PluginId,
                catalogRequests = native.CatalogRequests, hostCompositions = native.HostCompositions, localFixtureBindings = native.LocalBindings,
                localInvocations = native.LocalProgressPlayCalls, localClaims = native.LocalProgressClaims,
                secondStockCall = native.SecondStockCall, stockCommits = native.StockCommits, playEntryRetained = native.PlayEntryRetained,
                knownStockWithdrawal = native.KnownStockWithdrawal,
                productionAdmission = false, gameExecuted = false, scope = "public catalog entry and actual termination host with controlled IL2CPP and launch backend" }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

internal static partial class NativeLocalRunTerminationBoundaryStateRegression
{
    private sealed partial class Backend
    {
        internal VerifiedHookTarget? SharedPlayTarget;
        internal ExactHookBroker.LocalProgressPlayReservation? SharedPlayReservation;
        internal int SharedPlayClaims;
        internal int SharedLocalBinds => _engine.Installs;
        internal bool ReplacedStockPlay => SharedPlayTarget is not null && _engine.Replacements.ContainsKey(SharedPlayTarget.Address);
        internal ExactHookBroker.LocalProgressPlayReservation ReserveSharedPlay()
            => SharedPlayReservation = _broker.ReserveLocalProgressPlay(SharedPlayTarget!);
    }

    internal sealed class SharedPlayFixture : ILocalRunPlayDispatcher, IDisposable
    {
        private readonly MainThreadDispatcher _main = new();
        private readonly ExactHookBroker _stock;
        private readonly PlaybackSourceGroupLease _group;
        private readonly Backend _backend;
        private readonly LocalRunProtectionHost _host;
        private readonly IntPtr _progress;
        private readonly VerifiedHookTarget _play;
        private readonly Invoke _invoke;
        private readonly int _playBodyLength;
        private readonly byte[] _installedPlay;
        private NativeLocalRunTerminationBoundaryState? _bridge;
        internal int CatalogRequests, HostCompositions;
        internal bool SecondStockCall, KnownStockWithdrawal;
        internal int LocalBindings => _backend.SharedLocalBinds;
        internal long LocalProgressPlayCalls => _backend.InitializationBinding?.Describe().PlayCalls ?? 0;
        internal int LocalProgressClaims => _backend.SharedPlayClaims;
        internal long StockCommits => _stock.ReadActualPlaybackDobbyCallCounts().Committed;
        internal bool PlayEntryRetained => _installedPlay.AsSpan().SequenceEqual(
            NativePlaybackExecutableSeal.RangeSeal.Read(_play.Address, _playBodyLength));

        internal SharedPlayFixture(string[] paths)
        {
            Check(GetModuleHandle("GameAssembly.dll") == IntPtr.Zero && GetModuleHandle("UnityPlayer.dll") == IntPtr.Zero);
            string Hash(int index) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(paths[index]))).ToLowerInvariant();
            Equal(OwnedPlaybackFixtureProfile.WrapperSha256, Hash(0));
            Equal(OwnedPlaybackFixtureProfile.LowerSha256, Hash(1));
            var wrapper = NativeLibrary.Load(paths[0]); var lower = NativeLibrary.Load(paths[1]);
            Equal(1, Export<BindLower>(wrapper, "StockFixtureBindLowerPitch")(NativeLibrary.GetExport(lower, "StockLowerSetPitch")));
            Equal(1, Export<BindLower>(wrapper, "StockFixtureBindLowerTime")(NativeLibrary.GetExport(lower, "StockLowerSetTime")));
            var targets = PlaybackSourceGroupTargets.ResolveOwnedLowerTimeFixture(wrapper, lower);
            var source = Export<Create>(wrapper, "StockFixtureCreateSource")();
            var player = Export<CreateWith>(wrapper, "StockFixtureCreatePlayer")(source);
            _progress = Export<CreateWith>(wrapper, "StockFixtureCreateProgress")(player);
            Check(source != IntPtr.Zero && player != IntPtr.Zero && _progress != IntPtr.Zero);
            _play = targets.ProgressPlay with { Abi = ExactHookAbi.LocalProgressPlayBooleanVoid };
            _playBodyLength = targets.SourceBodyLength(NativePlaybackSourceRole.ProgressPlay);
            _invoke = Export<Invoke>(wrapper, "StockFixtureInvoke");
            _stock = new(paths[4], _main, new Logger());
            _group = _stock.BindPlaybackSourceGroup(targets, paths[2], Hash(2), paths[3], Hash(3),
                () => throw new InvalidOperationException("The owned Play fixture cannot grant Clock policy."), _ => { }, _ => { });
            Equal(23L, StockCommits);
            _installedPlay = NativePlaybackExecutableSeal.RangeSeal.Read(_play.Address, _playBodyLength);
            _host = LocalRunProtectionHost.Create(_main, new Logger(), new Transport());
            _backend = new(_main) { SharedPlayTarget = _play, InitializationEnabled = true };
        }

        public Result RequestPlay(LocalPlayRequest request, CancellationToken cancellationToken)
        {
            ++CatalogRequests;
            Check(!cancellationToken.IsCancellationRequested && request.Resources.Count == 3);
            _ = LocalChartPlaybackMetadataValidation.Require(request);
            try
            {
                using var play = _backend.ReserveSharedPlay();
                Check(play.SharesOfficialPlay && PlayEntryRetained);
                _bridge = NativeLocalRunTerminationBoundaryState.CreateBackend(_main, _host, _backend);
            }
            catch (ExactHookBroker.LocalProgressPlayConflictException error) { return Result.Fail(new(ErrorCode.Conflict, error.Message)); }
            ++HostCompositions;
            _host.InstallHandler(new LocalRunTerminationHandler(_main, _host.Sessions, _bridge, new PendingSink(), new PendingCommands()));
            Check(_host.Read() is { AllGatesInstalled: true, RetainedBindings: 5, CleanupUnknown: false }
                && _backend.InitializationBinding is { IsActive: true } && !_backend.ReplacedStockPlay);
            var controller = _progress.ToInt64() - 2; // Layout of this controlled host backend only.
            using var borrowed = new NativeRuntime.RootHandle(99, _ => { }, _ => new(controller), _main.VerifyAccess);
            var selection = request.Selection;
            var session = _host.RegisterPrepared(new(selection.Song.Song, selection.Song.Revision, selection.Chart.Id),
                controller + 3, request.Retain());
            var commit = _host.Sessions.BeginCommit(session);
            _host.Sessions.BindPendingController(commit, controller, new Lease());
            _host.Sessions.Commit(commit);
            _bridge.RegisterCommitted(session, borrowed);
            Check(_bridge.IsReadyForHost(_host.Sessions));
            CallRetainedOriginal(0x80);
            Check(_backend.InitializationBinding!.Describe() is { PlayCalls: 1, InFlight: 0, UnknownObserved: 0 });
            Equal(1, LocalProgressClaims);
            _host.Sessions.BeginReturn(session);
            var proof = _host.Sessions.VerifyConsumersEnded(session, new Verifier())!;
            Check(_host.Sessions.Close(session, proof) && _bridge.TryRetire(session));
            Check(_bridge.TryStop());
            _host.RequestStop();
            return Result.Ok();
        }

        internal void VerifyThenPlayOfficialAgain(bool shared)
        {
            Check(PlayEntryRetained && StockCommits == 23 && _group.IsActive);
            Equal(shared ? 1 : 0, HostCompositions);
            Equal(shared ? 1L : 0, LocalProgressPlayCalls);
            if (!shared) Equal(0, LocalBindings);
            CallRetainedOriginal(0xff);
            SecondStockCall = true;
            Equal(shared ? 1L : 0, LocalProgressPlayCalls);
            _group.VerifyCurrent();
            _group.Dispose();
            KnownStockWithdrawal = _group.ReadActivity() is { Known: true, InFlight: 0 };
            Check(KnownStockWithdrawal);
            CallRetainedOriginal(0x01);
            Check(PlayEntryRetained && GetModuleHandle("GameAssembly.dll") == IntPtr.Zero);
        }

        private void CallRetainedOriginal(byte raw)
        {
            Marshal.WriteByte(_progress, 0x42, raw);
            var previous = Marshal.ReadInt32(_progress, 0x44);
            Equal(1, _invoke(5, _play.Address, _progress, raw, _play.MethodInfo, out var exception));
            Equal(0u, exception);
            Equal(previous + 1, Marshal.ReadInt32(_progress, 0x44));
            Equal((long)raw, Marshal.ReadInt64(_progress, 0x50));
        }

        public void Dispose()
        {
            _host.RequestStop();
            _group.Dispose();
            _main.Dispose();
        }

        private static T Export<T>(IntPtr module, string name) where T : Delegate
            => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Create();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateWith(IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BindLower(IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Invoke(uint kind, IntPtr target, IntPtr self, ulong arg, IntPtr method, out uint exception);
        [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    }
}
