using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Sdk;

internal static partial class StartChecks
{
    [DllImport("kernel32", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SeekProgressLoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32", EntryPoint = "FreeLibrary")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SeekProgressFreeLibrary(IntPtr module);

    private static int RunSeekProgress(string[] arguments, string mode)
    {
        SeekSourceContext? native = null;
        INativePlaybackSeekSource? source = null;
        NativeSeekProgressParticipant? progress = null;
        GameplayPreparedValueControl? transaction = null;
        IDisposable? plan = null;
        IGameplayValueParticipant? audio = null, clock = null;
        var module = IntPtr.Zero;
        var holdRestartGuardChecked = false;
        try
        {
            Check(arguments.Length == 5 && mode is "seek-progress-normal" or "seek-progress-chart" or "seek-progress-backward"
                or "seek-progress-partial" or "seek-progress-clock-withdrawn" or "seek-progress-route-change",
                "Unknown actual Seek progress participant case.");
            var path = Path.GetFullPath(Environment.GetEnvironmentVariable("RIZ_SEEK_PROGRESS_IMAGE")
                ?? throw new ArgumentException("An explicit isolated clock evidence image is required."));
            Check(path.StartsWith("D:\\riz-build-cache-next\\", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(path).Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase)
                && Hash(path) == GameplayClockNativeProfile.Fingerprint,
                "The progress case requires the unchanged isolated pinned code image.");
            module = SeekProgressLoadLibraryEx(path, IntPtr.Zero, 1); // no imports or DllMain
            Check(module != IntPtr.Zero, "The isolated fixed code evidence could not be mapped.");
            native = new(arguments, module); source = native.CaptureImplementation();
            var profile = new GameplayClockNativeProfile(native.Api.SeekProgressProfileApi,
                native.Api.GameplayApi.Class("Assembly-CSharp.dll", "InLevel", "LevelInformation"));
            profile.VerifyCurrent();
            Check(profile.ProgressClass == native.Api.GameplayApi.Class("Assembly-CSharp.dll", "InLevel", "ProgressControl")
                && profile.ChartClass == native.Api.GameplayApi.Class("Assembly-CSharp.dll", "InLevel", "Chart")
                && profile.GetTimeMethodInfo.ToInt64() != native.Binding.GetTimeMethod,
                "The mapped converter profile lost the actual owned classes or falsely borrowed their fixed GetTime consumer.");
            var baseline = source.Read(); var originalCaches = native.Api.ReadSeekProgressBuffers();
            var originalClock = source.Clock.Capture();
            var request = new GameplayControlRequest(GameplayControlAction.Seek, native.Binding.ControllerGeneration,
                native.Binding.RunId, mode == "seek-progress-chart" ? 9.8 : mode == "seek-progress-backward" ? .8 : 5.8,
                mode == "seek-progress-chart" ? GameplayControlValueUnit.ChartCoordinates : GameplayControlValueUnit.GameSeconds);
            progress = NativeSeekProgressParticipant.Capture(native.Api.GameplayApi, profile, native.Binding,
                native.Api.GameplayPause.ToInt64(), request, source.VerifyAuthority, source.VerifyAuthority);
            Check(native.Api.ActiveRoots == 22 && native.Api.SeekProgressStores.Count == 0,
                "Read-only progress preparation did not retain its independent eight-root graph alongside the seven source/preparation roots.");
            var targetCaches = (Progress: progress.Coordinates.ProgressSeconds, Chart: progress.Coordinates.ChartCoordinate,
                Real: progress.Coordinates.SyncedSeconds, Synced: progress.Coordinates.SyncedSeconds);
            var media = progress.MediaSeconds;
            Check(media == (mode == "seek-progress-backward" ? 1f : 6f)
                && native.Api.SeekProgressConversions.Last().Method == "RealTimeToChartTime"
                && native.Api.SeekProgressConversions.Last().Input == progress.Coordinates.ProgressSeconds
                && (mode != "seek-progress-chart" || native.Api.SeekProgressConversions.First().Method == "ChartTimeToRealTime")
                && native.Api.SeekProgressForbiddenImageCalls == 0,
                "Canonical Single media/progress/shifted chart conversion differed or applied the unrelated chart delay.");

            // Execute the existing production Plan/Audio/Clock implementations
            // and, after recovery, their original-Hold time guard. Actual native
            // game-state/Hold preparation remains absent and full Seek unsigned.
            var type = typeof(NativePlaybackSeekParticipants);
            var planType = type.GetNestedType("Plan", BindingFlags.NonPublic)!;
            plan = (IDisposable)planType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { request, source, progress })!;
            IGameplayValueParticipant Construct(string name) => (IGameplayValueParticipant)Activator.CreateInstance(
                type.GetNestedType(name, BindingFlags.NonPublic)!, BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { plan }, null)!;
            audio = Construct("Audio"); clock = Construct("Clock");
            transaction = GameplayPreparedValueControl.Prepare(request,
                new Func<IGameplayValueParticipant>[] { () => audio, () => progress, () => clock },
                source.VerifyAuthority, progress.ValueTolerance);
            var stages = new List<object>();
            var stageClocks = new List<float>(); var inStoreObservation = false;
            native.Api.AfterSeekProgressStore = field =>
            {
                inStoreObservation = true;
                try
                {
                    var nativeClock = native.Time(); var caches = native.Api.ReadSeekProgressBuffers(); stageClocks.Add(nativeClock);
                    stages.Add(new { field, media = native.Media, nativeClock,
                        caches = new { caches.Progress, caches.Chart, caches.Real, caches.Synced } });
                }
                finally { inStoreObservation = false; }
            };

            var revoked = false;
            if (mode is "seek-progress-clock-withdrawn" or "seek-progress-route-change")
                native.Api.DuringSeekProgressWriteMetadata = field =>
                {
                    if (field != "_nowTime" || revoked) return;
                    revoked = true;
                    if (mode == "seek-progress-clock-withdrawn") native.Clock.Withdraw("The actual final field metadata callback withdrew this Clock.");
                    else typeof(NativePlaybackStockClockRouter).GetField("_sourceRoute", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(native.Router, new object());
                };
            var readFailure = false;
            if (mode == "seek-progress-partial")
                native.Api.SeekTimeReadException = name =>
                {
                    if (name != "get_clip" || inStoreObservation || native.Api.SeekProgressStores.Count != 3 || readFailure) return false;
                    readFailure = true; return true;
                };

            if (mode is "seek-progress-clock-withdrawn" or "seek-progress-route-change")
            {
                Check(SeekTimeRefused(transaction.Commit) && revoked && native.TimeCalls == 1
                    && native.Api.SeekProgressStores.Count == 0 && native.Api.ReadSeekProgressBuffers() == originalCaches,
                    "A source revoked by final field metadata still wrote an actual progress cache after the media stage.");
                native.Api.DuringSeekProgressWriteMetadata = null;
                transaction.Restore();
                Check(!transaction.ConfirmRestored().OriginalStateRestored && native.TimeCalls == 1,
                    "A revoked operation invented an inverse media store or a full restoration.");
            }
            else
            {
                if (mode == "seek-progress-partial")
                {
                    Check(SeekTimeRefused(transaction.Commit) && readFailure && native.Api.SeekProgressStores.Count == 3
                        && native.Media == media && native.Time() == baseline.AudioPositionSeconds,
                        "A real post-third-cache getter failure did not stop before the fourth cache/Clock stage.");
                    native.Api.SeekTimeReadException = null;
                }
                else
                {
                    transaction.Commit(); var confirmed = transaction.Confirm();
                    Check(confirmed.Observed == true && confirmed.Unit == request.Unit
                        && confirmed.Value is double observed && Math.Abs(observed - request.Value!.Value) <= transaction.ValueTolerance
                        && native.Api.ReadSeekProgressBuffers() == targetCaches && native.Media == media && native.Time() == media,
                        "The actual media/four-cache/Clock combination did not independently confirm its typed target.");
                    Check(native.Api.SeekProgressStores.Select(row => row.Field)
                            .SequenceEqual(new[] { "_nowTime", "nowTime", "nowRealTime", "nowSyncedTime" })
                        && stageClocks.All(value => value == baseline.AudioPositionSeconds),
                        "Progress caches were not committed after audio and before native Clock publication.");
                }
                transaction.Restore(); var restored = transaction.ConfirmRestored();
                Check(restored.OriginalStateRestored && native.Api.ReadSeekProgressBuffers() == originalCaches
                    && native.Media == baseline.AudioPositionSeconds && native.Time() == baseline.AudioPositionSeconds
                    && native.TimeCalls == 2 && native.TimeStores == 2 && native.Provider.Probe().Supported,
                    "The actual inverse Clock/caches/media stages did not restore complete original time values on the same source.");
                var applyCount = mode == "seek-progress-partial" ? 3 : 4;
                var reverse = new[] { "_nowTime", "nowTime", "nowRealTime", "nowSyncedTime" }.Take(applyCount).Reverse();
                Check(native.Api.SeekProgressStores.Skip(applyCount).Select(row => row.Field).SequenceEqual(reverse)
                    && native.Clock.Capture().Rate == originalClock.Rate
                    && native.Clock.Capture().DspAnchorSeconds == originalClock.DspAnchorSeconds,
                    "Inverse cache order changed the selected rate or rewound DSP.");
                if (mode == "seek-progress-normal")
                {
                    var predecessor = new SeekProgressHoldRestoreProbe();
                    using var holdEpoch = (IGameplayValueParticipant)Activator.CreateInstance(
                        type.GetNestedType("HoldEpoch", BindingFlags.NonPublic)!, BindingFlags.NonPublic | BindingFlags.Instance,
                        null, new object[] { plan, predecessor }, null)!;
                    var storesBefore = native.Api.SeekProgressStores.Count; var mediaCallsBefore = native.TimeCalls;
                    foreach (var cache in new[] { (Name: "nowRealTime", Original: originalCaches.Real),
                        (Name: "nowSyncedTime", Original: originalCaches.Synced) })
                    {
                        native.Api.InjectSeekProgressAuxiliaryCache(cache.Name, cache.Original + 37f);
                        try
                        {
                            var changed = native.Api.ReadSeekProgressBuffers(); var refused = false;
                            try { holdEpoch.Restore(); }
                            catch (GameplayStateException error) when (error.Code == ErrorCode.Conflict
                                && error.Message == "Original Hold iterators require all four original progress caches.")
                            { refused = true; }
                            Check(refused && predecessor.Restores == 0 && native.Api.ReadSeekProgressBuffers() == changed
                                && changed != originalCaches && native.Api.SeekProgressStores.Count == storesBefore
                                && native.TimeCalls == mediaCallsBefore && native.Media == baseline.AudioPositionSeconds,
                                $"Changed {cache.Name}: refused={refused}, restarts={predecessor.Restores}, "
                                    + $"injected={changed}, current={native.Api.ReadSeekProgressBuffers()}, baseline={originalCaches}, "
                                    + $"stores={native.Api.SeekProgressStores.Count}/{storesBefore}, "
                                    + $"media calls={native.TimeCalls}/{mediaCallsBefore}, media={native.Media}/{baseline.AudioPositionSeconds}.");
                        }
                        finally { native.Api.InjectSeekProgressAuxiliaryCache(cache.Name, cache.Original); }
                    }
                    holdEpoch.Restore();
                    Check(predecessor.Restores == 1 && native.Api.ReadSeekProgressBuffers() == originalCaches
                        && native.Api.SeekProgressStores.Count == storesBefore && native.TimeCalls == mediaCallsBefore,
                        "Complete original four-cache readback did not permit exactly one predecessor handoff.");
                    holdRestartGuardChecked = true;
                }
            }
            Check(native.Api.SeekProgressCallbackFailure is null && native.Api.SeekTimeInjectionFailure is null
                && native.Api.SeekProgressForbiddenImageCalls == 0 && native.Api.AllFieldOwnersRooted,
                "A fixture callback error, unrooted store or forbidden mapped image execution was hidden.");
            native.Api.AfterSeekProgressStore = null;
            transaction.Dispose(); transaction = null; progress = null; audio = null; clock = null; plan = null; source = null;
            Check(native.Api.ActiveRoots == 7 && native.Group.ReadSourceHealth() is { Known: true, InFlight: 0 },
                "The combined participant disposal lost source responsibility or leaked independent preparation roots.");
            native.Finish();
            profile.VerifyCurrent();
            Check(SeekProgressFreeLibrary(module), "The isolated clock evidence mapping did not unload."); module = IntPtr.Zero;
            Check(SeekTimeRefused(profile.VerifyCurrent), "An unloaded fixed code profile remained usable.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "owned-seek-progress-participant", mode, passed = true, checks = _checks,
                nativeEvents = native.NativeEvents, progressStores = native.Api.SeekProgressStores.Select(row => new { row.Field, row.Bits }),
                conversions = native.Api.SeekProgressConversions.Select(row => new { row.Method, row.Input, row.Output }),
                stages, timeOriginalCalls = native.TimeCalls, activeRoots = native.Api.ActiveRoots,
                privateSourceImplementationExecuted = true, actualClockExecuted = true, progressParticipantExecuted = true,
                actualAudioProgressClockParticipantsExecuted = true, holdEpochRestartGuardExecuted = holdRestartGuardChecked,
                metadataSynthetic = true, conversionApiShim = true,
                mappedImageCalls = native.Api.SeekProgressForbiddenImageCalls, dllMainCalls = 0,
                actualGameStateParticipantExecuted = false, actualHoldEpochParticipantExecuted = false,
                formalFreshSourceScopeCovered = false, formalSeekProviderIssued = false, fullSeekCompleted = false,
                productionAdmission = false, gameExecuted = false, installed = false
            }));
            native.Dispose(); return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "owned-seek-progress-participant", mode, passed = false, checks = _checks,
                failure = error.ToString(), progressStores = native?.Api.SeekProgressStores.Select(row => new { row.Field, row.Bits }),
                timeOriginalCalls = native?.TimeCalls, activeRoots = native?.Api.ActiveRoots,
                callbackFailure = native?.Api.SeekProgressCallbackFailure?.ToString()
            }));
            if (native is not null)
            {
                native.Api.DuringSeekProgressWriteMetadata = null; native.Api.AfterSeekProgressStore = null;
                native.Api.SeekTimeReadException = null;
            }
            foreach (var owner in new IDisposable?[] { transaction, clock, progress, audio, plan, source, native })
                try { owner?.Dispose(); } catch (Exception cleanup) { Console.Error.WriteLine(cleanup); }
            if (module != IntPtr.Zero) SeekProgressFreeLibrary(module);
            return 1;
        }
    }

    // The production outer Hold participant owns the checks under test. This
    // probe only records whether it reached the otherwise native predecessor.
    private sealed class SeekProgressHoldRestoreProbe : IGameplayValueParticipant
    {
        internal int Restores;
        public string Id => "controlled.seek.predecessor-handoff";
        public void VerifyPrepared() => throw new NotSupportedException();
        public void Commit() => throw new NotSupportedException();
        public GameplayControlConfirmation Confirm() => throw new NotSupportedException();
        public void Restore() => ++Restores;
        public GameplayControlRestoration ConfirmRestored() => throw new NotSupportedException();
        public void Dispose() { }
    }

}
