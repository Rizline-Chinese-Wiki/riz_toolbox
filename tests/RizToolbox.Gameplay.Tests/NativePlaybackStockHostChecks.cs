using System.Reflection;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static class NativePlaybackStockHostChecks
{
    internal static IEnumerable<(string Name, Action Run)> All => new (string, Action)[]
    {
        ("formal playback capture waits for committed shared run observation", CommittedRun),
        ("formal playback probe never creates native authority before installation", DormantHost),
        ("formal playback worker and copied cleanup cannot close the actual host", OwnerCleanup),
        ("retirement-only playback maintenance never starts an unrequested physical bind", DormantMaintenance),
        ("an active clock-only module does not prepare advanced playback", ClockOnlyMaintenance),
        ("later control demand re-enters the unchanged physical preparation checks", LaterControlDemand),
        ("unrequested playback maintenance still consumes native faults and retires ownership", DormantFaultDrain),
        ("stock control availability preserves waiting transition and withdrawal reasons", PublishedRefusals)
    };

    internal static IEnumerable<(string Name, Action Run)> FilePreparationChecks => OperatingSystem.IsWindows()
        ? new (string, Action)[]
        {
            ("playback file preparation can be consumed once and demand withdrawal retires a completed request", FilePreparationHandoff),
            ("playback rejects file identity replacement at the same path between preparation and consumption", FilePreparationPathReplacement)
        }
        : Array.Empty<(string Name, Action Run)>();
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Refused(Action action, ErrorCode code)
    {
        try { action(); }
        catch (GameplayStateException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected refusal: " + code);
    }

    private static void CommittedRun()
    {
        var state = new GameEventState();
        state.SetAvailability(scenes: true, runs: true, pause: true); state.Scene("Game");
        var rejectedDuringPublish = false;
        using var subscription = state.Subscribe(change =>
        {
            if (change.Kind != GameLifecycleChangeKind.RunStarted) return;
            Refused(() => state.CaptureCurrentPlaybackRun(), ErrorCode.NotReady);
            rejectedDuringPublish = true;
        }).Value!;
        state.Controller(11, true, false, "observed-chart", 13);
        var actual = state.CaptureCurrentPlaybackRun();
        Check(rejectedDuringPublish && ReferenceEquals(actual, state.CapturePlaybackRun(11, 13)),
            "The post-Poll accessor replaced the observer's private run association.");
        state.ControllerUnavailable();
        Refused(actual.VerifyHostCurrent, ErrorCode.TargetExpired);
        Refused(() => state.CaptureCurrentPlaybackRun(), ErrorCode.NotReady);
        state.Controller(21, true, false, "retry-chart", 13);
        var sameController = state.CaptureCurrentPlaybackRun();
        Check(!ReferenceEquals(actual, sameController) && sameController.RunId != actual.RunId
            && sameController.Controller == actual.Controller && sameController.Information == 21,
            "A committed retry reused the old association or invented a different controller.");
        state.ControllerUnavailable();
        state.Controller(31, true, false, "next-chart", 33);
        var nextController = state.CaptureCurrentPlaybackRun();
        Check(!ReferenceEquals(sameController, nextController) && nextController.RunId != sameController.RunId
            && nextController.Controller == 33 && nextController.Information == 31,
            "A new observed song did not retain its own committed controller/information association.");
        Refused(sameController.VerifyHostCurrent, ErrorCode.TargetExpired);
    }

    private static void DormantHost()
    {
        using var main = new MainThreadDispatcher();
        // Probe/read requests must not touch native APIs or initiate installation.
        using var host = new NativePlaybackStockRateHost(null!, main, new Logger(), new GameEventState(), null!, "unused");
        var probe = host.Probe();
        Check(!probe.Supported && probe.Error?.KnownCode == ErrorCode.NotReady && !host.FrameworkReady
            && !host.ProductionAdmission && host.Stage == NativePlaybackStockHostStage.WaitingForInstallation,
            "A dormant formal host claimed a source, clock or engine permission.");
        Refused(() => host.CaptureGetTimeHook(), ErrorCode.NotReady);
        Refused(() => host.CaptureSourceDescriptor(), ErrorCode.NotReady);
        Check(host.Stage == NativePlaybackStockHostStage.WaitingForInstallation,
            "An ordinary capability read started or withdrew installation.");
    }

    private static void OwnerCleanup()
    {
        using var main = new MainThreadDispatcher();
        using var host = new NativePlaybackStockRateHost(null!, main, new Logger(), new GameEventState(), null!, "unused");
        var workerRejected = Task.Run(() =>
        {
            try { host.Dispose(); return false; }
            catch (InvalidOperationException) { return true; }
        }).GetAwaiter().GetResult();
        var clone = (NativePlaybackStockRateHost)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
        Refused(clone.Dispose, ErrorCode.TargetExpired);
        Check(workerRejected && host.Stage == NativePlaybackStockHostStage.WaitingForInstallation
            && host.Probe().Error?.KnownCode == ErrorCode.NotReady, "Rejected cleanup mutated the actual host.");
        host.Dispose(); NativePlaybackStockRateHost.PumpRetired();
        Check(host.Stage == NativePlaybackStockHostStage.Withdrawn && !host.FrameworkReady
            && host.Probe().Error?.KnownCode == ErrorCode.TargetExpired, "Owner cleanup failed to withdraw the dormant host.");
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void DormantMaintenance()
    {
        using var main = new MainThreadDispatcher();
        // Null native state makes an accidental PreparePhysical observable:
        // the old startup path would catch that failure and withdraw the host.
        using var host = new NativePlaybackStockRateHost(null!, main, new Logger(), new GameEventState(), null!, "unused");
        for (var i = 0; i < 8; ++i) host.PumpInstallationAndRetirement();
        Check(host.Stage == NativePlaybackStockHostStage.WaitingForInstallation && !host.FrameworkReady
            && !Field<bool>(host, "_installAttempted") && Field<long>(host, "_nextInstallProbe") == 0,
            "Dormant maintenance started preparation, consumed its retry window or withdrew an untouched host.");
    }

    private static void ClockOnlyMaintenance()
    {
        using var fixture = new Fixture();
        using var host = new NativePlaybackStockRateHost(null!, fixture.Main, new Logger(), new GameEventState(), null!, "unused");
        var demandReads = 0;
        var module = new GameplayModule(null!, fixture.Main, () => null, null, (_, _, _, _) => false,
            host, pumpAfterObservation: true, playbackRequested: () => { ++demandReads; return false; });
        // Exercise the actual active-service branch without loading IL2CPP.
        typeof(GameplayModule).GetField("_service", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(module, fixture.Service);
        try { for (var i = 0; i < 4; ++i) module.MaintainPlayback(); }
        finally { module.Release(); }
        Check(demandReads == 4 && host.Stage == NativePlaybackStockHostStage.WaitingForInstallation
            && Field<long>(host, "_nextInstallProbe") == 0,
            "Clock service publication was mistaken for playback-control demand.");
    }

    private static void LaterControlDemand()
    {
        using var fixture = new Fixture();
        var logger = new PreparationLogger();
        var profile = new AdapterBuildProfile("offline-demand", new string('0', 64), "offline", "unsupported", "manager", "Update",
            new Dictionary<string, TypeBinding>());
        using var host = new NativePlaybackStockRateHost(null!, fixture.Main, logger, new GameEventState(), profile, "unused");
        var requested = false;
        var module = new GameplayModule(null!, fixture.Main, () => null, null, (_, _, _, _) => false,
            host, pumpAfterObservation: true, playbackRequested: () => requested);
        typeof(GameplayModule).GetField("_service", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(module, fixture.Service);
        try
        {
            module.MaintainPlayback();
            Check(host.Stage == NativePlaybackStockHostStage.WaitingForInstallation && logger.Error is null,
                "No-demand maintenance entered native preparation.");
            requested = true; module.MaintainPlayback();
            Check(host.Stage == NativePlaybackStockHostStage.Withdrawn && logger.Error is UnsupportedSignatureException
                && logger.Error.Message.Contains("pinned game build", StringComparison.Ordinal)
                && Field<long>(host, "_nextInstallProbe") > 0 && !Field<bool>(host, "_installAttempted"),
                "Later demand was permanently disabled or bypassed the existing pre-bind profile verification.");
        }
        finally { module.Release(); }
    }

    private static void DormantFaultDrain()
    {
        using var main = new MainThreadDispatcher();
        using var host = new NativePlaybackStockRateHost(null!, main, new Logger(), new GameEventState(), null!, "unused");
        var faults = Field<ConcurrentQueue<object>>(host, "_faults");
        faults.Enqueue(new NativePlaybackSingleReturnFault("retained-native-fault"));
        host.PumpInstallationAndRetirement();
        Check(faults.IsEmpty && host.Stage == NativePlaybackStockHostStage.Withdrawn
            && Field<string>(host, "_failure") == "retained-native-fault" && Field<long>(host, "_nextInstallProbe") == 0,
            "No-demand dispatch skipped retained fault/retirement responsibility or started a new bind.");
        NativePlaybackStockRateHost.PumpRetired();
    }

    private static void PublishedRefusals()
    {
        using var main = new MainThreadDispatcher();
        using var host = new NativePlaybackStockRateHost(null!, main, new Logger(), new GameEventState(), null!, "unused");
        Expect(ErrorCode.NotReady, host.Detail);

        const string transition = "The completed source has no retained native predecessor.";
        typeof(NativePlaybackStockRateHost).GetField("_transitionError", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, new OperationError(ErrorCode.OperationUnsupported, transition));
        typeof(NativePlaybackStockRateHost).GetMethod("SetStage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, new object[] { NativePlaybackStockHostStage.TransitionUnavailable, transition });
        Expect(ErrorCode.OperationUnsupported, transition);

        const string fault = "The retained source reported a native fault.";
        Field<ConcurrentQueue<object>>(host, "_faults").Enqueue(new NativePlaybackSingleReturnFault(fault));
        Expect(ErrorCode.TargetExpired, "The retained playback host has a pending native fault.");
        host.PumpInstallationAndRetirement();
        Expect(ErrorCode.TargetExpired, fault);

        void Expect(ErrorCode code, string message)
        {
            var actual = host.Probe();
            var rate = NativePlaybackRateProviderAdoption.Availability(host, observationOnly: true);
            var seek = host.ObserveSeekAvailability();
            Check(!actual.Supported && actual.Error?.KnownCode == code && actual.Error.Message == message,
                $"The stock host resolved {actual.Error}, expected {code}: {message}");
            Check(!rate.Supported && rate.Error?.KnownCode == code && rate.Error.Message == message
                && !seek.Supported && seek.Error?.KnownCode == code && seek.Error.Message == message,
                $"Expected both SDK value controls to report {code}: {message} Rate: {rate.Error}; Seek: {seek.Error}");
        }
    }

    private static void FilePreparationHandoff()
    {
        using var fixture = new PreparationFiles();
        using (var request = fixture.Prepare(fixture.StockPath))
        {
            WaitForWorker(request);
            Check(request.TryTake(out var files), "The completed preparation did not yield its files.");
            using (files!)
            {
                files!.VerifyStockImage(fixture.StockPath, fixture.StockHash);
                files.VerifyHelpers();
                Check(files.GetTimeSha256 == PreparationFiles.Hash(fixture.GetTimePath)
                    && files.SourceSha256 == PreparationFiles.Hash(fixture.SourcePath),
                    "The completed file set did not retain both exact helper digests.");
                FilePreparationRefused<ObjectDisposedException>(() => request.TryTake(out _));
            }
        }

        using var main = new MainThreadDispatcher();
        using var host = new NativePlaybackStockRateHost(null!, main, new Logger(), new GameEventState(), null!, "unused");
        using var withdrawn = fixture.Prepare(fixture.StockPath);
        WaitForWorker(withdrawn);
        var pending = typeof(NativePlaybackStockRateHost).GetField("_filePreparation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        pending.SetValue(host, withdrawn);
        host.PumpInstallationAndRetirement();

        Check(pending.GetValue(host) is null && !Field<bool>(host, "_installAttempted")
            && host.Stage == NativePlaybackStockHostStage.WaitingForInstallation,
            "Demand withdrawal retained prepared files or began a physical installation.");
        FilePreparationRefused<ObjectDisposedException>(() => withdrawn.TryTake(out _));
        Check(SpinWait.SpinUntil(fixture.CanWriteAll, TimeSpan.FromSeconds(10)),
            "Withdrawal did not close the worker's held files.");
    }

    private static void FilePreparationPathReplacement()
    {
        using var fixture = new PreparationFiles();
        string stockPath;
        try { stockPath = fixture.CreateLinkedStockPath(); }
        catch (Exception error) when (error is UnauthorizedAccessException
            || error is IOException && (error.HResult & 0xffff) == 1314)
        {
            Console.WriteLine("SKIP unavailable Windows symbolic-link privilege: " + error.Message);
            return;
        }

        using var request = fixture.Prepare(stockPath);
        WaitForWorker(request);
        // The first frame leaves a successful request pending. The next frame
        // reopens the same spelling after it resolves to another exact file.
        fixture.SwitchLinkedStockPath();
        Check(PreparationFiles.Hash(stockPath) == fixture.StockHash, "The replacement must have the same digest.");
        Check(request.TryTake(out var files), "The original held preparation did not reach the consumer.");
        using (files!)
            FilePreparationRefused<UnsupportedSignatureException>(() => files!.VerifyStockImage(stockPath, fixture.StockHash));
    }

    private static void WaitForWorker(NativePlaybackFilePreparation request)
    {
        var worker = Field<Task>(request, "_worker");
        Check(SpinWait.SpinUntil(() => worker.IsCompleted, TimeSpan.FromSeconds(10))
            && worker.IsCompletedSuccessfully, "The owned file-preparation worker did not complete successfully.");
    }

    private static void FilePreparationRefused<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected file preparation refusal: " + typeof(T).Name);
    }

    private sealed class PreparationFiles : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("riztoolbox-playback-preparation-").FullName;
        private string First => Path.Combine(_directory, "first");
        private string Second => Path.Combine(_directory, "second");
        private string LiveLink => Path.Combine(_directory, "live");
        private string NextLink => Path.Combine(_directory, "next");
        private string RetiredLink => Path.Combine(_directory, "retired");
        internal string StockPath => Path.Combine(First, "stock.bin");
        internal string GetTimePath => Path.Combine(_directory, "get-time.bin");
        internal string SourcePath => Path.Combine(_directory, "source.bin");
        internal string StockHash { get; }

        internal PreparationFiles()
        {
            Directory.CreateDirectory(First);
            File.WriteAllBytes(StockPath, new byte[] { 12, 23, 34, 45 });
            File.WriteAllBytes(GetTimePath, new byte[] { 7, 8, 9 });
            File.WriteAllBytes(SourcePath, new byte[] { 14, 15, 16 });
            StockHash = Hash(StockPath);
        }

        internal NativePlaybackFilePreparation Prepare(string stockPath) =>
            new(stockPath, StockHash, GetTimePath, SourcePath);

        internal string CreateLinkedStockPath()
        {
            Directory.CreateDirectory(Second);
            File.Copy(StockPath, Path.Combine(Second, "stock.bin"));
            Directory.CreateSymbolicLink(LiveLink, First);
            Directory.CreateSymbolicLink(NextLink, Second);
            return Path.Combine(LiveLink, "stock.bin");
        }

        internal void SwitchLinkedStockPath()
        {
            Directory.Move(LiveLink, RetiredLink);
            Directory.Move(NextLink, LiveLink);
        }

        internal bool CanWriteAll()
        {
            try
            {
                foreach (var path in new[] { StockPath, GetTimePath, SourcePath })
                    using (File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None)) { }
                return true;
            }
            catch (IOException) { return false; }
        }

        internal static string Hash(string path) =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

        public void Dispose()
        {
            foreach (var link in new[] { LiveLink, NextLink, RetiredLink })
                if (Directory.Exists(link)) Directory.Delete(link);
            foreach (var file in new[] { StockPath, GetTimePath, SourcePath }) File.Delete(file);
            if (Directory.Exists(Second))
            {
                File.Delete(Path.Combine(Second, "stock.bin"));
                Directory.Delete(Second);
            }
            Directory.Delete(First);
            Directory.Delete(_directory);
        }
    }

    private sealed class PreparationLogger : ILogger
    {
        internal Exception? Error;
        public void Log(LogLevel level, string message, Exception? exception = null) { if (exception is not null) Error = exception; }
    }
}
