using System.Text;
using System.Text.Json;
using System.Globalization;
using RizToolbox.Sdk;

namespace RizToolbox.Examples;

/// <summary>Opt-in SDK-only acceptance controls. Ordinary example installations leave this disabled.</summary>
internal static class ModuleRuntimeProbe
{
    internal static IPluginScope? Start(IPluginContext context)
    {
        var config = context.Configuration.Load("runtime-module-proof", () => new ProbeSettings());
        if (!config.Success) throw new InvalidOperationException(config.Error?.Message);
        if (config.Value?.Enabled != true) return null;
        var scope = ((IScopedPluginContext)context).CreateScope("runtime-module-proof");
        context = scope;
        RingColorRuntimeProbe.Start(context);
        var materials = context.Own(new MaterialRuntimeProbe(context));
        IUiWindowHandle? window = null;
        IUiControlHandle? rateStatus = null;
        (IGameplayControlLease Lease, IGameplayControlCommand Command)? pendingRate = null;
        var seekSeconds = "0";
        IAudioAssetLease? audioAsset = null;
        IAudioPlaybackLease? audioPlayback = null;
        double next = 0;
        long? attemptedScene = null;
        long sceneGeneration = 0;
        bool inputRegistered = false;
        IGameplayJudgementService? judgementService = null;
        IDisposable? judgementSubscription = null;
        IGameplayControlSubscription? controlSubscription = null;
        GameplayControlObservationSnapshot? controlObservation = null;
        OperationError? controlSubscriptionError = null;
        bool controlObservationStopped = false;
        var judgementSamples = new Dictionary<string,int>();
        var probeStorage = context.GetService<IPluginStorageService>(StorageCapabilityIds.Storage);
        var mailbox = probeStorage.Service is { } storage
            ? context.Own(new ModuleControlMailbox(storage,
                () => context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service, scope.Lifetime, Log))
            : null;
        if (mailbox is null) Log("MAILBOX UNAVAILABLE", probeStorage.Error);
        context.Events.Subscribe<GameSceneChanged>(change => { sceneGeneration = change.SceneGeneration; materials.SceneChanged(sceneGeneration); });
        context.Events.Subscribe<FrameUpdate>(frame =>
        {
            ReadRateCommand();
            if (frame.ElapsedSeconds < next) return;
            next = frame.ElapsedSeconds + 1;
            ObserveControls();
            mailbox?.Tick(controlObservation);
            materials.Tick(sceneGeneration);
            if (judgementService is not null && !judgementService.GetStatus().Success)
            { judgementSubscription?.Dispose(); judgementSubscription = null; judgementService = null; }
            if (judgementService is null && context.GetService<IGameplayJudgementService>(JudgementCapabilityIds.Observe).Service is { } judgements)
            {
                var subscribed = judgements.Subscribe(value =>
                {
                    judgementSamples.TryGetValue(value.SourceId,out var count); judgementSamples[value.SourceId] = count + 1;
                    if (count < 2) Log("JUDGEMENT",value);
                });
                Log("JUDGEMENT SUBSCRIPTION",new { subscribed.Success, subscribed.Error });
                if (subscribed.Success) { judgementService = judgements; judgementSubscription = subscribed.Value; }
            }
            if (!inputRegistered && context.GetService<IInputActionService>(UiInputCapabilityIds.Actions).Service is { } actions)
            {
                var registered = actions.Register(new("proof-f8", new(InputDeviceKind.Keyboard, "F8")),
                    _ => { Log("INPUT F8", new { sceneGeneration }); Snapshot(); });
                Log("INPUT REGISTRATION", new { registered.Success, registered.Error });
                Log("INPUT DEVICES", actions.GetDevices());
                inputRegistered = registered.Success;
            }
            if (window?.IsAlive == true || attemptedScene == sceneGeneration) return;
            var ui = context.GetService<IOwnedUiService>(UiInputCapabilityIds.Windows);
            if (!ui.Available || ui.Service is null) return;
            attemptedScene = sceneGeneration;
            var created = ui.Service.CreateWindow(new("module-proof", UiMountIds.Overlay, "SDK runtime proof",
                new(new(1370, 80, 510, 850)), Theme: new(FontSize: 20)));
            if (!created.Success) { Log("WINDOW REFUSED", created.Error); return; }
            window = created.Value!;
            Require(window.AddLabel(new("scope", "Explicit test controls; results are logged", new(15, 40, 475, 40))));
            Button("snapshot", "Snapshot / audit", 90, Snapshot);
            Button("pause", "Pause", 145, () => Control(GameplayControlAction.Pause));
            Button("resume", "Resume", 200, () => Control(GameplayControlAction.Resume));
            Button("retry", "Retry", 255, () => Control(GameplayControlAction.Retry));
            Button("skip", "Skip", 310, () => Control(GameplayControlAction.Skip));
            Button("audio", "Play quiet test tone", 365, PlayAudio);
            Button("stop", "Stop test tone", 420, StopAudio);
            Button("navigation", "Enter selected main-menu item", 475, Navigate);
            Require(window.AddTextField(new("text", "edit this SDK text field", new(15, 540, 475, 45)),
                value => Log("TEXT CHANGED", new { value }), 64));
            Button("rate-075", "0.75x", 600, () => SetSpeed(.75), 15, 150);
            Button("rate-100", "1.0x", 600, () => SetSpeed(1), 177, 150);
            Button("rate-125", "1.25x", 600, () => SetSpeed(1.25), 340, 150);
            Require(window.AddTextField(new("seek-seconds", seekSeconds, new(15, 660, 300, 45)),
                value => seekSeconds = value, 32));
            Button("seek", "Seek seconds", 660, Seek, 327, 163);
            rateStatus = Require(window.AddLabel(new("rate-status",
                "Experimental playback controls: pause the song before changing speed or seeking.", new(15, 720, 475, 110))));
            Log("WINDOW CREATED", new { sceneGeneration, viewport = Require(window.GetViewport()) });
            Snapshot();
            void Button(string id, string label, float y, Action action, float x = 15, float width = 475)
                => Require(window.AddButton(new(id, label, new(x, y, width, 45)), () =>
                {
                    Log("BUTTON", new { id });
                    try { action(); }
                    catch (Exception error) { Log("ACTION FAILED", new { id, error = error.Message }); }
                }));
        });
        ObserveControls();
        return scope;

        void ObserveControls()
        {
            if (controlSubscription is not null || controlObservationStopped) return;
            var lookup = context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls);
            if (!lookup.Available || lookup.Service is null)
            {
                Refused(lookup.Error ?? new(ErrorCode.CapabilityUnavailable, "Gameplay controls are unavailable."));
                return;
            }
            var subscribed = lookup.Service.Subscribe(snapshot => LogControlObservation("CONTROL CHANGE", snapshot), scope.Lifetime);
            if (!subscribed.Success)
            {
                Refused(subscribed.Error!);
                return;
            }
            controlSubscription = subscribed.Value ?? throw new InvalidOperationException("Control subscription returned no owned handle.");
            controlSubscriptionError = null;
            LogControlObservation("CONTROL INITIAL", controlSubscription.InitialSnapshot);

            void Refused(OperationError error)
            {
                if (controlSubscriptionError != error)
                    Log("CONTROL OBSERVATION UNAVAILABLE", new { message = error.Message, code = error.KnownCode.ToString() });
                controlSubscriptionError = error;
                if (error.KnownCode is ErrorCode.Cancelled or ErrorCode.ScopeExpired) controlObservationStopped = true;
            }
        }

        void LogControlObservation(string label, GameplayControlObservationSnapshot snapshot, bool repeatOperations = false)
        {
            var previous = repeatOperations ? null : controlObservation;
            if (!repeatOperations) controlObservation = snapshot;
            var state = snapshot.State;
            // Keep failure text before the flags and split operation errors into their own
            // records so the bounded logger cannot discard them behind a large snapshot.
            Log(label, new { message = snapshot.Error?.Message, code = snapshot.Error?.KnownCode.ToString(), snapshot.Revision,
                state?.ControllerGeneration, state?.ObservedRunId, state?.ControllerAvailable,
                state?.LoadOver, state?.TimeReady, state?.Playing, state?.Paused, state?.LevelOver, state?.Crashed,
                state?.CanDoOperation, state?.CurrentOwner });
            if (snapshot.ProbeError is { } probeError && probeError != previous?.ProbeError)
                Log("CONTROL PROBE ERROR", new { message = probeError.Message, code = probeError.KnownCode.ToString(), snapshot.Revision });
            else if (snapshot.ProbeError is null && previous?.ProbeError is not null)
                Log("CONTROL PROBE RECOVERED", new { snapshot.Revision });
            foreach (var operation in snapshot.Operations)
            {
                var prior = previous?.Operations.FirstOrDefault(value => value.Action == operation.Action);
                if (prior is not null && prior.Supported == operation.Supported && prior.Error == operation.Error
                    && prior.StateReadiness?.Success == operation.StateReadiness?.Success
                    && prior.StateReadiness?.Error == operation.StateReadiness?.Error
                    && prior.Evidence == operation.Evidence && prior.ValueUnits.SequenceEqual(operation.ValueUnits)) continue;
                Log("CONTROL OPERATION " + operation.Action, new { stateReason = operation.StateReadiness?.Error?.Message,
                    ready = operation.StateReadiness?.Success, message = operation.Error?.Message,
                    code = operation.Error?.KnownCode.ToString(), operation.Supported,
                    units = operation.ValueUnits.Select(unit => unit.ToString()),
                    evidence = operation.Error is null ? operation.Evidence : null, snapshot.Revision });
            }
        }

        void Snapshot()
        {
            if (judgementService is not null) Log("JUDGEMENT STATUS",judgementService.GetStatus());
            if (context.GetService<IGameplayClockService>(GameplayCapabilityIds.Clock).Service is { } gameClock)
                Log("GAME CLOCK", gameClock.Read());
            if (controlObservation is { } latest) LogControlObservation("CONTROL LATEST", latest, repeatOperations: true);
            if (context.GetService<IAudioClockService>(AudioContentCapabilityIds.Clock).Service is { } audioClock)
                Log("AUDIO CLOCK", audioClock.Read());
            if (context.GetService<IAudioReplacementService>(AudioContentCapabilityIds.Replacement).Service is { } sources)
            {
                var targets = sources.GetTargets();
                Log("AUDIO TARGETS", new { targets.Success, targets.Error, count = targets.Value?.Count,
                    targets = targets.Value?.Take(3).Select(x => new { x.Target, x.Metadata.Scene, x.Metadata.Description }),
                    truncated = targets.Value?.Count > 3 });
            }
            if (context.GetService<IGameSettingsReadService>(GameSettingsCapabilityIds.Read).Service is { } settings)
            {
                var read = settings.GetSettings();
                Log("SETTINGS", new { read.Success, read.Error,
                    values = read.Value?.Select(x => new { x.Descriptor.Id, x.Value, x.Descriptor.ReadAvailable,
                        x.Descriptor.OverrideAvailable, x.Descriptor.Minimum, x.Descriptor.Maximum }) });
            }
            if (context.GetService<IRunModificationService>(RunModificationCapabilityIds.Read).Service is { } audit)
            {
                var read = audit.Read(new(MaximumEntries: 4)); var value = read.Value;
                Log("RUN MODIFICATIONS", new { read.Success, read.Error, value?.Revision,
                    lifecycle = value is null ? null : new { value.Lifecycle.SessionId, value.Lifecycle.Revision, value.Lifecycle.SceneGeneration,
                        value.Lifecycle.Scene, value.Lifecycle.RunId, value.Lifecycle.Phase },
                    records = value?.Records.Select(x => new { x.Sequence, x.RunId, x.Source, x.Operation, x.Outcome, x.Association, x.EffectId }),
                    activeEffects = value?.ActiveEffects.Count, value?.Counters, value?.Truncated });
            }
            if (audioPlayback is not null) Log("AUDIO ACTIVE", new { audioPlayback.IsActive });
        }
        void Control(GameplayControlAction action)
        {
            var service = context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service;
            if (service is null) { Log("CONTROL UNAVAILABLE", new { action }); return; }
            var acquired = service.Acquire(allowExperimental: true);
            if (!acquired.Success) { Log("CONTROL REFUSED", new { action, acquired.Error }); return; }
            using var lease = acquired.Value!;
            Log("CONTROL RECEIPT", new { action, result = lease.Execute(action) });
            Snapshot();
        }
        void SetSpeed(double rate)
        {
            if (pendingRate is not null)
            {
                RateRefused(rate, new(ErrorCode.Conflict, "A rate command is already pending."));
                return;
            }
            try
            {
                var lookup = context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls);
                if (lookup.Service is not { } service)
                { RateRefused(rate, lookup.Error ?? new(ErrorCode.CapabilityUnavailable, "Gameplay controls are unavailable.")); return; }
                var acquired = service.Acquire(allowExperimental: true);
                if (!acquired.Success) { RateRefused(rate, acquired.Error!); return; }
                var lease = acquired.Value!;
                var request = new GameplayControlRequest(GameplayControlAction.SetSpeed,
                    lease.ControllerGeneration, lease.ObservedRunId, rate, GameplayControlValueUnit.PlaybackRateMultiplier);
                Result<IGameplayControlCommand> submitted;
                try { submitted = lease.Submit(request, scope.Lifetime); }
                catch { lease.Dispose(); throw; }
                if (!submitted.Success)
                {
                    lease.Dispose();
                    RateRefused(rate, submitted.Error!);
                    return;
                }
                pendingRate = (lease, submitted.Value!);
                SetRateStatus($"{rate:0.00}x queued.");
            }
            catch (Exception error)
            {
                Log("SPEED FAILED", new { rate, error = error.Message });
                SetRateStatus($"{rate:0.00}x failed: {error.Message}");
            }
        }
        void ReadRateCommand()
        {
            if (pendingRate is not { } active) return;
            var record = active.Command.Snapshot;
            if (record.State is GameplayControlCommandState.Queued or GameplayControlCommandState.Executing) return;
            pendingRate = null;
            active.Lease.Dispose();
            Log("SPEED RECEIPT", record);
            var rate = record.Request.Value.GetValueOrDefault();
            if (record.Receipt?.Modification.ConfirmedValue is { } confirmed)
            {
                SetRateStatus($"Confirmed {confirmed:0.00}x. Experimental; use Resume when ready.");
                Snapshot();
            }
            else if (record.State == GameplayControlCommandState.Unknown)
                SetRateStatus($"{rate:0.00}x outcome is unknown. Rate control is unavailable for this source.");
            else SetRateStatus($"{rate:0.00}x {record.State}: {record.Error?.Message}");
        }
        void RateRefused(double rate, OperationError error)
        {
            Log("SPEED REFUSED", new { rate, error });
            SetRateStatus($"{rate:0.00}x refused [{error.Code}]: {error.Message}");
        }
        void Seek()
        {
            try
            {
                if (!double.TryParse(seekSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                    || !double.IsFinite(seconds))
                {
                    Refused(new(ErrorCode.InvalidArgument, "Enter a finite target in game seconds, using a decimal point.")); return;
                }
                var lookup = context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls);
                if (lookup.Service is not { } service)
                { Refused(lookup.Error ?? new(ErrorCode.CapabilityUnavailable, "Gameplay controls are unavailable.")); return; }
                var acquired = service.Acquire(allowExperimental: true);
                if (!acquired.Success) { Refused(acquired.Error!); return; }
                using var lease = acquired.Value!;
                var request = new GameplayControlRequest(GameplayControlAction.Seek,
                    lease.ControllerGeneration, lease.ObservedRunId, seconds, GameplayControlValueUnit.GameSeconds);
                var result = lease.Execute(request);
                Log("SEEK RECEIPT", new { request, result });
                if (!result.Success) { Refused(result.Error!); return; }
                var modification = result.Value!.Modification;
                SetRateStatus($"Seek: {modification.TransactionState}. " +
                    (modification.ConfirmedValue is { } confirmed ? $"Confirmed {confirmed:0.###} s. " : "") +
                    "Experimental; use Resume when ready.");
                Snapshot();
            }
            catch (Exception error)
            {
                Log("SEEK FAILED", new { target = seekSeconds, error = error.Message });
                SetRateStatus($"Seek failed: {error.Message}");
            }
            void Refused(OperationError error)
            {
                Log("SEEK REFUSED", new { target = seekSeconds, error });
                SetRateStatus($"Seek refused [{error.Code}]: {error.Message}");
            }
        }
        void SetRateStatus(string text)
        {
            if (rateStatus is null) return;
            var changed = rateStatus.SetText(text);
            if (!changed.Success) Log("SPEED STATUS REFUSED", changed.Error);
        }
        void PlayAudio()
        {
            StopAudio();
            if (audioPlayback is not null) throw new InvalidOperationException("Prior audio stop failed; keeping its clip rooted.");
            var storage = RequireService<IPluginStorageService>(StorageCapabilityIds.Storage);
            const string path = "runtime-module-proof/tone.wav";
            var expected = Tone();
            var existing = storage.List(PluginStorageArea.Data);
            Require(existing);
            if (existing.Value!.Any(entry => entry.Name == path))
            {
                var prior = Require(storage.Read(PluginStorageArea.Data, path));
                if (!prior.SequenceEqual(expected)) throw new InvalidOperationException("Existing proof asset differs; preserving it.");
            }
            else Require(storage.Write(PluginStorageArea.Data, path, expected));
            var playback = RequireService<IAudioPlaybackService>(AudioContentCapabilityIds.Playback);
            var loaded = playback.LoadWav(new(ContentFileArea.PluginData, path));
            if (!loaded.Success) { Log("AUDIO LOAD REFUSED", loaded.Error); return; }
            audioAsset = loaded.Value!;
            Log("AUDIO LOADED", new { audioAsset.SampleFrames, audioAsset.Channels, audioAsset.Frequency, audioAsset.IsValid });
            var played = playback.Play(audioAsset.Asset, volume: .05f, loop: true);
            Log("AUDIO PLAY", new { played.Success, played.Error });
            if (played.Success) audioPlayback = played.Value;
            if (audioPlayback is not null) Log("AUDIO ACTIVE", new { audioPlayback.IsActive });
            Snapshot();
        }
        void StopAudio()
        {
            if (audioPlayback is not null)
            {
                var stopped = audioPlayback.Stop(); Log("AUDIO STOP", stopped);
                if (!stopped.Success) return;
                audioPlayback.Dispose(); audioPlayback = null;
            }
            audioAsset?.Dispose(); audioAsset = null;
        }
        void Navigate()
        {
            var navigation = RequireService<IGameNavigationService>(UiInputCapabilityIds.Navigation);
            var targets = navigation.GetTargets(); Log("NAVIGATION TARGETS", targets);
            var available = targets.Where(target => target.Available).ToArray();
            if (available.Length == 1) Log("NAVIGATION RECEIPT", navigation.Navigate(available[0].Id));
        }
        T RequireService<T>(string id) where T : class
            => context.GetService<T>(id).Service ?? throw new InvalidOperationException("Service unavailable: " + id);
        void Log(string label, object? value)
        {
            var text = "ModuleProbe " + label + " " + JsonSerializer.Serialize(value);
            context.Log.Log(LogLevel.Info, text.Length < 2048 ? text : text[..2000] + " [truncated]");
        }
    }
    private static T Require<T>(Result<T> result) => result.Success ? result.Value! : throw new InvalidOperationException(result.Error!.Message);
    private static void Require(Result result) { if (!result.Success) throw new InvalidOperationException(result.Error!.Message); }
    private static byte[] Tone()
    {
        const int frames = 8000, frequency = 8000;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + frames * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(frequency); writer.Write(frequency * 2);
        writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(frames * 2);
        for (int i = 0; i < frames; i++) writer.Write((short)(Math.Sin(i * 2d * Math.PI * 220 / frequency) * 1200));
        writer.Flush(); return stream.ToArray();
    }
    private sealed record ProbeSettings(bool Enabled = false);
}
