using System.Diagnostics;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Core;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Core.Localization;
using RizToolbox.Core.Services;
using RizToolbox.Core.Storage;
using RizToolbox.Hosting;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Loader;

/// <summary>Composition and diagnostics only. Game-specific logic belongs to Adapter; examples are external packages.</summary>
internal sealed class ToolboxHost
{
    private readonly StructuredDiagnosticLogger _log;
    private readonly MainThreadDispatcher _main = new();
    private readonly CapabilityRegistry _capabilities;
    private readonly PluginHost _plugins;
    private readonly EventHub _events;
    private readonly IGameAdapter _adapter;
    private readonly PluginLoadResult _packages;
    private readonly IReadOnlySet<string> _loadedPackageIds;
    private readonly NativeRuntime _api;
    private readonly Settings _settings;
    private readonly RuntimeConfiguration _configuration;
    private readonly PluginDiscoveryResult _discovery;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private LatestDiagnosticWriter<HostStatusCopy>? _statusWriter;
    private readonly Queue<ValidationCommand> _validation;
    private readonly IDisposable? _activationBinding;
    private readonly PluginLocalizationManager _localization;
    private readonly InterPluginServiceRegistry _pluginServices;
    private readonly CallbackPerformanceMonitor _performance;
    private readonly Command? _startupCommand;
    private TaskCompletionSource<StatusPublication>? _commandStatusCapture;
    private OperationError? _activationSaveFailure;
    private long _previousTick;
    private string? _lastCommand;
    private double _nextStatus;
    private bool _statusRequested;
    private double? _validationStartedAt;
    private bool _stopped;
    private string? _stopReason;
    private IReadOnlyList<string> _shutdownFailures = Array.Empty<string>();
    private IReadOnlyList<PackageUpdateView> _packageUpdates = Array.Empty<PackageUpdateView>();
    private long _statusRevision, _statusCaptureFailures, _reportedStatusWriteFailures;
    private string? _statusCaptureError;
    private long _hostTickSequence;
    private IReadOnlyCollection<string>? _lastCapabilityDemand;
    private bool _capabilityDemandApplied;

    private sealed record PackageUpdateView(string PluginId, bool? RestartRequired,
        IReadOnlyList<string> ChangedFiles, string Message);

    private sealed record StatusPublication(LatestDiagnosticWriter<HostStatusCopy>? Writer = null,
        long Revision = 0, OperationError? Error = null);

    private sealed record CommandExecution(string Outcome, OperationError? Error = null,
        PluginStatus? Plugin = null, bool? IntentRecorded = null, Task<StatusPublication>? Status = null,
        long? StatusRevision = null);

    private static readonly TimeSpan CommandStatusTimeout = TimeSpan.FromSeconds(5);

    internal ToolboxHost(NativeRuntime api, AdapterBuildProfile profile, StructuredDiagnosticLogger log,
        RuntimeConfiguration configuration)
    {
        _api = api; _log = log; _configuration = configuration;
        // The mailbox is one-shot input for a running host, not saved activation intent.
        // Capture before starting background work so a leftover request cannot replay,
        // while a new ID written after construction is accepted even before the first poll.
        _startupCommand = ReadJson<Command?>(Path.Combine(Bootstrap.DataRoot, "command.json"), null);
        _settings = ReadJson(Path.Combine(Bootstrap.DataRoot, "settings.json"), new Settings());
        try { _performance = new CallbackPerformanceMonitor(callbackBudgetMilliseconds: _settings.CallbackBudgetMilliseconds); }
        catch (ArgumentOutOfRangeException ex)
        {
            log.Log(LogLevel.Warning, "Invalid callback budget; keeping measurement enabled without a shutdown threshold.", ex);
            _performance = new CallbackPerformanceMonitor();
        }
        _events = new EventHub(log) { Performance = _performance };
        _capabilities = new(new(profile.GameVersion, profile.UnityVersion, "win-x64", profile.GameAssemblySha256, true), log);
        var extensions = new ExtensionPointRegistry(log);
        _plugins = new(_capabilities, _main, _events, log, dataRoot: Bootstrap.DataRoot, extensions: extensions);
        var storage = new PluginStorageManager(Path.Combine(Bootstrap.DataRoot, "plugin-storage"));
        var mods = Path.Combine(Bootstrap.DataRoot, "mods"); Directory.CreateDirectory(mods);
        // Inspect metadata before creating game services or executing plugin code.
        _discovery = PluginPackageLoader.DiscoverDirectory(mods, log);
        var packageDirectories = new Dictionary<string, string>(StringComparer.Ordinal);
        _capabilities.Register<IPerformanceService>(PerformanceCapabilityIds.Read, _performance);
        DeclareHostCapability(PerformanceCapabilityIds.Read, "snapshot", "Bounded host-thread callback windows, inclusive duration and thread allocations; no unmeasured frame or GPU guarantee.");
        if (configuration.AllowOptionalWrites && OperatingSystem.IsWindows())
        {
            var backgroundPlay = new BackgroundPlayService();
            _capabilities.RegisterScoped<IBackgroundPlayService>(BackgroundPlayCapability.Id, backgroundPlay.ForPlugin);
            _capabilities.DeclareMetadata(BackgroundPlayCapability.Id, new Version(1, 0), CapabilityStability.Experimental,
                new[] { new CapabilityOperationInfo("enable-for-scope", OperationThread.MainThread, ObservationMode.NotApplicable,
                    CleanupResponsibility.EnableScope, true, new[] { "*" }, "Keep gameplay running when the Unity window loses activation.") });
        }
        _localization = new PluginLocalizationManager();
        var language = _localization.SetLanguage(_settings.Language ?? "en");
        if (!language.Success) log.Log(LogLevel.Warning, "Plugin language selection rejected: " + language.Error!.Message);
        _pluginServices = new InterPluginServiceRegistry(_main, log);
        _pluginServices.ProviderFaulted += OnServiceProviderFault;
        _capabilities.RegisterScoped<IPluginStorageService>(StorageCapabilityIds.Storage, storage.ForPlugin);
        _capabilities.RegisterScoped<IPluginLocalizationService>(StorageCapabilityIds.Localization, _localization.ForPlugin);
        _capabilities.RegisterScoped<IPluginServices>(InterPluginCapabilityIds.Services, context => _pluginServices.ForPlugin(context));
        DeclareHostCapability(StorageCapabilityIds.Storage, "read-write", "Plugin-owned schema configuration, data and cache with bounded files and quotas.");
        DeclareHostCapability(StorageCapabilityIds.Localization, "resolve-register-observe", "Plugin-owned language tables, fallback and host-selected live language changes.");
        DeclareHostCapability(InterPluginCapabilityIds.Services, "publish-lookup-watch-message", "Versioned shared-contract services and read-only messages; providers withdraw with their activation scope.");
        // Bundled validation examples contain opt-in runtime probes. Their old
        // per-plugin files must not activate those probes in a Standard session.
        _packages = PluginPackageLoader.LoadDiscovered(_discovery, log, package =>
            configuration.AllowOptionalWrites && (configuration.EnableProofPlugins || !IsBundledValidationPackage(package.Metadata)));
        _loadedPackageIds = new HashSet<string>(_packages.Packages.Select(package => package.Metadata.Manifest.Id), StringComparer.Ordinal);
        _packageUpdates = Array.AsReadOnly(_packages.Packages.Select(package =>
            new PackageUpdateView(package.Metadata.Manifest.Id, null, Array.Empty<string>(),
                configuration.Mode == RuntimeMode.Developer ? "Pending first disk check." : "Periodic package checks are off in Standard mode.")).ToArray());
        foreach (var package in _packages.Packages)
        {
            packageDirectories.Add(package.Metadata.Manifest.Id, package.DirectoryPath);
            var result = _plugins.Register(package.Plugin);
            log.Log(result.Success ? LogLevel.Info : LogLevel.Warning,
                $"Package registration {package.Metadata.Manifest.Id}: {result.Success} {result.Error}");
        }
        var disabled = new HashSet<string>(_settings.DisabledPlugins ?? Array.Empty<string>(), StringComparer.Ordinal);
        var defaults = _plugins.Statuses.Select(status =>
        {
            var enabled = !disabled.Contains(status.Id) && (_settings.EnableExamples || !status.Id.StartsWith("example.", StringComparison.Ordinal));
            return new PluginActivationIntent(status.Id, enabled, !enabled);
        }).ToArray();
        bool runValidation = configuration.EnableProofPlugins && _settings.RunSelfValidation;
        if (runValidation) _plugins.RestoreActivationIntents(defaults);
        else if (configuration.AllowOptionalWrites)
        {
            var intentPath = Path.Combine(Bootstrap.DataRoot, "activation.json");
            var store = new PluginActivationStore(intentPath);
            var initialized = File.Exists(intentPath) ? Result.Ok() : store.Save(defaults);
            if (initialized.Success)
            {
                var binding = store.Bind(_plugins, enableUnspecified: false,
                    onSaveFailure: error =>
                    {
                        _activationSaveFailure = error;
                        log.Log(LogLevel.Error, "Cannot persist activation intent: " + error.Message);
                    });
                if (binding.Success) _activationBinding = binding.Value;
                else log.Log(LogLevel.Error, "Activation choices were not restored; plugins remain disabled: " + binding.Error!.Message);
            }
            else log.Log(LogLevel.Error, "Cannot initialize activation choices: " + initialized.Error!.Message);
        }
        var commands = runValidation
            ? ReadJson(Path.Combine(Bootstrap.DataRoot, "validation-plan.json"), Array.Empty<ValidationCommand>())
            : Array.Empty<ValidationCommand>();
        _validation = new(commands.OrderBy(x => x.AtSeconds));

        _adapter = configuration.AllowOptionalWrites ? AdapterFactory.Create(api, profile, _main, _capabilities, _events, extensions, log,
            (pluginId, error) => _plugins.ReportPluginFault(pluginId, error),
            pluginId => packageDirectories.TryGetValue(pluginId, out var directory) ? directory : null,
            pluginId => packageDirectories.ContainsKey(pluginId) ? storage.GetDataDirectory(pluginId) : null,
            developerDiagnostics: configuration.Mode == RuntimeMode.Developer && configuration.IsValid)
            : new SafeObservationAdapter(_capabilities.GameBuild);
        ApplyCapabilityDemand();
        WriteStatus();
        _ = Task.Run(WatchCommands);
    }

    private static bool IsBundledValidationPackage(PluginPackageMetadata metadata)
        => string.Equals(Path.GetFileName(metadata.EntryAssembly), "RizToolbox.Examples.dll", StringComparison.OrdinalIgnoreCase) && metadata.Manifest.Id is
            "example.scores" or "example.events" or "example.panel" or "example.color-low" or "example.color-high";

    private void DeclareHostCapability(string id, string operation, string description)
        => _capabilities.DeclareMetadata(id, new Version(1, 0), CapabilityStability.Experimental,
            new[] { new CapabilityOperationInfo(operation, OperationThread.MainThread, ObservationMode.NotApplicable,
                CleanupResponsibility.EnableScope, false, new[] { "*" }, description) });

    private void OnServiceProviderFault(string pluginId, Exception error) => _plugins.ReportPluginFault(pluginId, error);

    private T ReadJson<T>(string file, T fallback)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? fallback : fallback; }
        catch (Exception ex) { _log.Log(LogLevel.Warning, $"Cannot read {Path.GetFileName(file)}; using defaults.", ex); return fallback; }
    }

    private async Task WatchCommands()
    {
        var file = Path.Combine(Bootstrap.DataRoot, "command.json");
        long nextPackageScan = Stopwatch.GetTimestamp() + 5 * Stopwatch.Frequency;
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(500, _shutdown.Token);
            }
            catch (OperationCanceledException) { break; }
            try
            {
                if (File.Exists(file))
                {
                    var text = await File.ReadAllTextAsync(file, _shutdown.Token);
                    var command = JsonSerializer.Deserialize<Command>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (command is not null && ShouldDispatch(command))
                        await DispatchCommand(command);
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.Log(LogLevel.Warning, "Command processing failed.", ex); }
            // A malformed command must not starve independent package checks.
            if (_configuration.Mode == RuntimeMode.Developer && !_shutdown.IsCancellationRequested && Stopwatch.GetTimestamp() >= nextPackageScan)
            {
                try
                {
                    var updates = ScanPackageUpdates();
                    if (updates is not null)
                        await _main.InvokeAsync(() => { if (!_stopped) _packageUpdates = updates; }, _shutdown.Token);
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
                catch (Exception ex) { _log.Log(LogLevel.Warning, "Package disk check failed.", ex); }
                finally { nextPackageScan = Stopwatch.GetTimestamp() + 5 * Stopwatch.Frequency; }
            }
        }
    }

    private bool ShouldDispatch(Command command)
        => !Volatile.Read(ref _stopped) && !string.IsNullOrWhiteSpace(command.Id) && command.Id != _lastCommand
            && (command.ExpectedSessionId is null || command.ExpectedSessionId == _log.SessionId)
            && (command.Id != _startupCommand?.Id
                || (command.ExpectedSessionId is not null && command.ExpectedSessionId != _startupCommand?.ExpectedSessionId));

    private async Task DispatchCommand(Command command)
    {
        var action = command.Action ?? string.Empty;
        CommandExecution? execution = null;
        await _main.InvokeAsync(() =>
        {
            if (_stopped) return;
            try
            {
                execution = ExecuteCommand(action, command.PluginId, command.Language, command.Id);
                if (action == "status" && execution.Outcome == "succeeded")
                {
                    _commandStatusCapture = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    execution = execution with { Status = _commandStatusCapture.Task };
                }
            }
            catch (Exception error)
            {
                execution = new("failed", new(ErrorCode.InternalError, StatusCopyBudget.SafeError(error)));
                _log.TryLog(LogLevel.Warning, "Command processing failed.", error);
            }
            finally
            {
                if (!_stopped) _statusRequested = true;
            }
        }, _shutdown.Token);
        if (execution is null) return;

        // Only the callback consumes an ID. Receipt I/O and status persistence
        // must never turn an already-dispatched command back into queued work.
        _lastCommand = command.Id;
        execution = await WaitForCommandStatus(execution);
        await WriteCommandReceipt(command, action, execution);
    }

    private static async Task<CommandExecution> WaitForCommandStatus(CommandExecution execution)
    {
        if (execution.Status is null) return execution;
        OperationError? failure;
        try
        {
            var status = await execution.Status.WaitAsync(CommandStatusTimeout);
            failure = status.Error;
            if (status.Writer is { } writer)
            {
                var saved = writer.WaitThrough(status.Revision, CommandStatusTimeout);
                if (saved.Persisted) return execution with { StatusRevision = saved.LastWritten };
                failure = saved.Completed
                    ? new(ErrorCode.InternalError, saved.LastError ?? "The requested host status was not written.")
                    : new(ErrorCode.TimedOut, "Timed out waiting for the requested host status to be written.");
            }
        }
        catch (TimeoutException)
        {
            failure = new(ErrorCode.TimedOut, "Timed out waiting for the requested host status capture.");
        }
        return execution with { Outcome = "failed", Error = execution.Error ?? failure };
    }

    private async Task WriteCommandReceipt(Command command, string action, CommandExecution execution)
    {
        var target = Path.Combine(Bootstrap.DataRoot, "command-result.json");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var document = new
            {
                schemaVersion = 1, sessionId = _log.SessionId, commandId = command.Id, action,
                pluginId = action is ("enable" or "disable") ? command.PluginId : null,
                outcome = execution.Outcome, errorCode = execution.Error?.Code.ToString(),
                errorMessage = execution.Error?.Message, errorRetryable = execution.Error?.Retryable,
                pluginState = execution.Plugin?.State.ToString(), desiredEnabled = execution.Plugin?.DesiredEnabled,
                intentRecorded = execution.IntentRecorded, statusRevision = execution.StatusRevision
            };
            // Shutdown cancels intake but must leave the running command's
            // outcome publishable after the final host status has been written.
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(document));
            File.Move(temporary, target, true);
        }
        catch (Exception error) { _log.TryLog(LogLevel.Warning, "Cannot publish host command receipt.", error); }
        finally { try { File.Delete(temporary); } catch { } }
    }

    private IReadOnlyList<PackageUpdateView>? ScanPackageUpdates()
    {
        // Only immutable paths/fingerprints are read on the command worker.
        // Do not hold up main-thread shutdown waiting for background disk IO;
        // cancellation and the publish guard discard any late result.
        if (Volatile.Read(ref _stopped) || _shutdown.IsCancellationRequested) return null;
        var updates = new List<PackageUpdateView>(_packages.Packages.Count);
        foreach (var package in _packages.Packages)
        {
            if (Volatile.Read(ref _stopped) || _shutdown.IsCancellationRequested) return null;
            var status = package.CheckForUpdates();
            updates.Add(new(package.Metadata.Manifest.Id, status.RestartRequired,
                Array.AsReadOnly(status.ChangedFiles.ToArray()), status.Message));
        }
        return Array.AsReadOnly(updates.ToArray());
    }

    private void Execute(string action, string? id, string? language = null)
        => ExecuteCommand(action, id, language, commandId: null);

    private CommandExecution ExecuteCommand(string action, string? id, string? language, string? commandId)
    {
        if (action is "enable" or "disable")
            return ExecuteLifecycleCommand(action, id, commandId);
        if (!_configuration.AllowOptionalWrites && action is not ("status" or "shutdown"))
        {
            _log.Log(LogLevel.Warning, RuntimeConfiguration.InvalidModeMessage);
            return new("rejected", _configuration.ConfigurationError);
        }

        var priorFailure = _plugins.LatestLifecycleFailure;
        Result result;
        switch (action)
        {
            case "shutdown":
                var publication = Shutdown(false, "command");
                var failure = NewPluginFailure(priorFailure);
                if (_shutdownFailures.Count != 0)
                    failure = new(ErrorCode.InternalError, _shutdownFailures[0]);
                failure ??= _adapter is RizlineAdapter { UnconfirmedCleanup: { } cleanup }
                    ? new(ErrorCode.InternalError, cleanup) : null;
                if (publication is null)
                    return new("failed", failure ?? new(ErrorCode.InternalError, "Final host status was not captured."));
                return new(failure is null ? "succeeded" : "failed", failure, Status: Task.FromResult(publication));
            case "set-language" when language is not null:
                result = _localization.SetLanguage(language);
                break;
            case "status": result = Result.Ok(); break;
            default: result = Result.Fail(new(ErrorCode.InvalidArgument,
                "Unknown command or missing pluginId.", TargetId: id)); break;
        }
        _log.Log(result.Success ? LogLevel.Info : LogLevel.Warning, $"Lifecycle command {action} {id}: {result.Success} {result.Error}");
        if (NewPluginFailure(priorFailure) is { } pluginFailure) return new("failed", pluginFailure);
        if (result.Success) return new("succeeded");
        return new(result.Error!.Code is ErrorCode.InvalidArgument or ErrorCode.Conflict ? "rejected" : "failed", result.Error);
    }

    private OperationError? NewPluginFailure(OperationError? previous)
        => ReferenceEquals(previous, _plugins.LatestLifecycleFailure) ? null : _plugins.LatestLifecycleFailure;

    private CommandExecution ExecuteLifecycleCommand(string action, string? id, string? commandId)
    {
        var priorFailure = _plugins.LatestLifecycleFailure;
        var priorSaveFailure = _activationSaveFailure;
        var enabled = action == "enable";
        var intentRecorded = false;
        void RecordIntent() => intentRecorded = true;

        var writesAllowed = _configuration.AllowOptionalWrites;
        OperationResult result;
        if (!writesAllowed)
            result = OperationResult.Fail(_configuration.ConfigurationError! with { TargetId = id });
        else if (id is null)
            result = OperationResult.Fail(ErrorCode.InvalidArgument, "Unknown command or missing pluginId.");
        else
        {
            if (commandId is not null) _plugins.ActivationIntentsChanged += RecordIntent;
            try { result = _plugins.SetDesiredEnabled(id, enabled); }
            finally { if (commandId is not null) _plugins.ActivationIntentsChanged -= RecordIntent; }
        }

        var message = writesAllowed ? $"Lifecycle command {action} {id}: {result.Success} {result.Error}"
            : RuntimeConfiguration.InvalidModeMessage;
        if (commandId is null)
        {
            _log.Log(result.Success ? LogLevel.Info : LogLevel.Warning, message);
            return new(result.Success ? "succeeded" : "rejected", result.Error);
        }

        var status = id is null ? null : _plugins.Statuses.FirstOrDefault(plugin => plugin.Id == id);
        var error = result.Error;
        var waiting = enabled && intentRecorded && error is { Retryable: true }
            && status is { State: PluginState.Waiting, DesiredEnabled: true };
        var outcome = result.Success ? "succeeded" : waiting ? "waiting" : "rejected";
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["action"] = action,
            ["outcome"] = outcome,
            ["intentRecorded"] = intentRecorded ? "true" : "false",
            ["coreSuccess"] = result.Success ? "true" : "false",
            ["pluginState"] = status?.State.ToString(),
            ["desiredEnabled"] = status is null ? null : status.DesiredEnabled ? "true" : "false",
            ["errorCode"] = error?.Code.ToString(),
            ["errorMessage"] = error?.Message,
            ["errorRetryable"] = error is null ? null : error.Retryable ? "true" : "false"
        };
        if (status?.Failure is { } stateFailure && stateFailure != error)
        {
            fields["stateErrorCode"] = stateFailure.Code.ToString();
            fields["stateErrorMessage"] = stateFailure.Message;
        }
        if (!writesAllowed) fields["configurationDetail"] = _configuration.ConfigurationDetail;
        _log.Write(result.Success || waiting ? LogLevel.Info : LogLevel.Warning, "log", message,
            new(PluginId: id, CapabilityId: error?.CapabilityId, TargetId: error?.TargetId, CorrelationId: commandId), fields: fields);

        var failure = NewPluginFailure(priorFailure);
        if (failure is null && intentRecorded && status is { State: PluginState.Faulted })
            failure = status.Failure ?? new(ErrorCode.InternalError, "Plugin is faulted.", TargetId: id);
        if (failure is null && !ReferenceEquals(priorSaveFailure, _activationSaveFailure))
            failure = _activationSaveFailure;
        return new(failure is null ? outcome : "failed", failure ?? error, status, intentRecorded);
    }

    internal void Tick(long hostTickSequence)
    {
        if (_stopped) return;
        long started = Stopwatch.GetTimestamp(), allocation = GC.GetAllocatedBytesForCurrentThread();
        if (_previousTick != 0) _performance.Record("host", "update-arrival-interval", (started - _previousTick) * 1000d / Stopwatch.Frequency, enforceBudget: false);
        _previousTick = started;
        try { TickCore(hostTickSequence); }
        finally { _performance.Record("host", "managed-update", (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency,
            Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocation), enforceBudget: false); }
    }

    internal void RecordRuntimeEntry(RuntimeEntryMeasurement sample)
    {
        // The runtime collects this on the verified callback thread after Host
        // returns. Include admission as well as host work; the game original has
        // already returned and is deliberately outside these measurements.
        if (_stopped || sample.Outcome != RuntimeEntryOutcome.Dispatched) return;
        _hostTickSequence = sample.HostTickSequence;
        _performance.Record("runtime", "after-admission", sample.AdmissionMilliseconds, enforceBudget: false);
        _performance.Record("runtime", "host-dispatch", sample.HostMilliseconds, enforceBudget: false);
        _performance.Record("runtime", "after-entry-total", sample.TotalMilliseconds, enforceBudget: false);
    }

    private void TickCore(long hostTickSequence)
    {
        TimedPhase("before-command-boundaries", _adapter, static adapter => adapter.FlushBoundaryObservations());
        TimedPhase("main-dispatch", _main, static main => main.Pump(32));
        if (_stopped) return;
        TimedPhase("service-faults", _pluginServices, static services => services.DrainProviderFaults());
        if (_stopped) return;
        TimedPhase("validation-commands", this, static host => host.PumpValidationCommands());
        if (_stopped) return;
        TimedPhase("adapter-commands", _adapter, static adapter => adapter.PumpCommands());
        if (_stopped) return;
        // A command such as Retry can create a new boundary while running.
        // Consume it before sampling services or publishing the frame event.
        TimedPhase("after-command-boundaries", _adapter, static adapter => adapter.FlushBoundaryObservations());
        TimedPhase("capability-demand", this, static host => host.ApplyCapabilityDemand());
        TimedPhase("adapter-tick", (Adapter: _adapter, Sequence: hostTickSequence),
            static phase => phase.Adapter.Tick(phase.Sequence));
        if (_stopped) return;
        TimedPhase("frame-events", (Events: _events, Seconds: _clock.Elapsed.TotalSeconds),
            static phase => phase.Events.Publish(new FrameUpdate(phase.Seconds)));
        if (_statusRequested || _clock.Elapsed.TotalSeconds >= _nextStatus)
        {
            _statusRequested = false;
            _nextStatus = _clock.Elapsed.TotalSeconds + 2;
            TimedPhase("status-capture", this, static host => host.PublishTickStatus());
        }
    }

    private void PublishTickStatus()
    {
        var command = _commandStatusCapture;
        _commandStatusCapture = null;
        var publication = WriteStatus();
        command?.TrySetResult(publication);
    }

    private void TimedPhase<T>(string phase, T state, Action<T> work)
    {
        var started = Stopwatch.GetTimestamp(); var allocated = GC.GetAllocatedBytesForCurrentThread();
        try { AdapterTickPhase.Run(_log, phase, state, work); }
        finally
        {
            // Measurement cannot fault a native runtime channel. Host phases
            // are observations; their budgets never interrupt native work.
            try { _performance.Record("host-phase", phase, (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency,
                Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocated), enforceBudget: false); }
            catch (Exception error) { _log.TryLog(LogLevel.Warning, "Host phase measurement is unavailable. " + StatusCopyBudget.SafeError(error)); }
        }
    }

    private void PumpValidationCommands()
    {
        if (_validation.Count > 0 && _validationStartedAt is null)
        {
            if ((_settings.ValidationRequiredCapabilities ?? Array.Empty<string>()).All(id => _capabilities.GetCapability(id).Available))
            { _validationStartedAt = _clock.Elapsed.TotalSeconds; _log.Log(LogLevel.Info, "Validation prerequisites ready; external lifecycle plan started."); }
            else if (_clock.Elapsed.TotalSeconds > 60)
            { _log.Log(LogLevel.Error, "Validation prerequisites unavailable after 60 seconds."); Shutdown(true, "validation-prerequisites-unavailable"); return; }
        }
        while (_validation.Count > 0 && _validationStartedAt is double validationStart && _clock.Elapsed.TotalSeconds - validationStart >= _validation.Peek().AtSeconds)
        {
            var step = _validation.Dequeue();
            if (step.Action == "shutdown") { Shutdown(true, "validation-plan"); return; }
            Execute(step.Action, step.PluginId);
            _statusRequested = true;
        }
    }

    private void ApplyCapabilityDemand()
    {
        // Proof packages may exercise optional providers not listed as hard
        // requirements. The all-modules plan belongs only to that opt-in mode.
        var demand = _configuration.EnableProofPlugins ? null : _plugins.GetRequiredCapabilityDemand();
        if (_capabilityDemandApplied && ReferenceEquals(_lastCapabilityDemand, demand)) return;
        _adapter.SetCapabilityDemand(demand);
        _lastCapabilityDemand = demand;
        _capabilityDemandApplied = true;
    }

    private StatusPublication? Shutdown(bool quit, string reason)
    {
        _main.VerifyAccess();
        if (_stopped) return null;
        _stopped = true; _stopReason = reason;
        _commandStatusCapture?.TrySetResult(new(Error: new(ErrorCode.ScopeExpired, "Host stopped before capturing the requested status.")));
        _commandStatusCapture = null;
        StatusPublication? publication = null;
        var failures = new List<string>();
        void Attempt(string step, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                failures.Add($"{step}: {ex.GetType().Name}: {ex.Message}");
                _shutdownFailures = Array.AsReadOnly(failures.ToArray());
                try { _log.Log(LogLevel.Error, $"Host shutdown step '{step}' failed.", ex); }
                catch { /* Continue releasing the remaining owners even if diagnostics fail. */ }
            }
        }
        try
        {
            Attempt("cancel command loop", _shutdown.Cancel);
            Attempt("activation binding", () => _activationBinding?.Dispose());
            Attempt("plugin host", _plugins.Dispose);
            _pluginServices.ProviderFaulted -= OnServiceProviderFault;
            Attempt("inter-plugin services", _pluginServices.Dispose);
            Attempt("adapter", _adapter.Dispose);
            foreach (var package in _packages.Packages.Reverse())
                Attempt($"package {package.Metadata.Manifest.Id}", package.Dispose);
            Attempt("main-thread dispatcher", _main.Dispose);
        }
        finally
        {
            Attempt("shutdown diagnostic", () => _log.Log(failures.Count == 0 ? LogLevel.Info : LogLevel.Warning,
                failures.Count == 0 ? $"Toolbox host stopped ({reason}); shutdown steps returned. Check provider diagnostics for retained or failed resources." :
                    $"Toolbox host stopped ({reason}); {failures.Count} cleanup step(s) failed."));
            Attempt("final status", () => publication = WriteStatus());
            Attempt("status writer completion", () => _statusWriter?.RequestComplete());
            Attempt("diagnostic flush request", _log.RequestFlush);
            if (quit) Attempt("application quit", () => _api.CallStatic(
                _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Application"), "Quit", NativeRuntime.Arg.Value(0)));
        }
        return publication;
    }

    private StatusPublication WriteStatus(bool synchronous = false)
    {
        _main.VerifyAccess();
        try
        {
            var writer = _statusWriter ??= new LatestDiagnosticWriter<HostStatusCopy>(HostStatusPersistence.Write);
            var health = writer.GetSnapshot();
            if (health.Failures > _reportedStatusWriteFailures)
            {
                _reportedStatusWriteFailures = health.Failures;
                _log.TryLog(LogLevel.Warning, "Cannot save diagnostic status; game phases continue. " + health.LastError);
            }
            var revision = ++_statusRevision;
            var copy = CaptureStatus(revision);
            if (!writer.Publish(revision, copy, terminal: _stopped))
                return new(Error: new(ErrorCode.ScopeExpired, "The host status writer is closed."));
            // Reserved for explicit checks; normal ticks and shutdown never
            // wait for storage. A timeout is not a successful persistence.
            if (synchronous) _ = writer.WaitThrough(revision, TimeSpan.FromSeconds(5));
            return new(writer, revision);
        }
        catch (Exception error)
        {
            _statusCaptureError = StatusCopyBudget.SafeError(error);
            if (++_statusCaptureFailures == 1 || _statusCaptureFailures % 64 == 0)
                _log.TryLog(LogLevel.Warning, "Cannot capture diagnostic status; game phases continue. " + _statusCaptureError);
            return new(Error: new(ErrorCode.InternalError, _statusCaptureError));
        }
    }

    private HostStatusCopy CaptureStatus(long revision)
    {
        var budget = new StatusCopyBudget();
        var scene = !_stopped && _adapter is ISceneStatusSource source ? source.CaptureSceneStatus() : default;
        scene = scene with { Name = budget.Text(scene.Name) };
        var runtime = _configuration.CaptureStatus();
        runtime = runtime with { SourcePath = budget.Text(runtime.SourcePath),
            ConfigurationError = budget.Error(runtime.ConfigurationError),
            ConfigurationDetail = budget.Text(runtime.ConfigurationDetail), AssuranceBoundary = budget.Text(runtime.AssuranceBoundary)! };
        var plugins = budget.Selection(_plugins.CaptureDiagnosticStatuses(budget.NextSelectionLimit), item => item with
            { Id = budget.Text(item.Id)!, LastError = budget.Text(item.LastError), Failure = budget.Error(item.Failure) });
        var resources = budget.Selection(_plugins.CaptureDiagnosticResourceCounts(budget.NextSelectionLimit, 1024),
            item => item with { PluginId = budget.Text(item.PluginId)! });
        var details = budget.Selection(_capabilities.CaptureDiagnosticMetadata(budget.NextSelectionLimit), budget.Capability);
        var diagnostics = _log.GetSnapshot();
        var levels = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var pair in diagnostics.Levels) levels.Add(pair.Key, pair.Value);
        diagnostics = diagnostics with { LastError = budget.Text(diagnostics.LastError),
            Levels = new System.Collections.ObjectModel.ReadOnlyDictionary<string, long>(levels) };
        var updates = budget.Rows(_packageUpdates, item => new StatusPackageUpdate(budget.Text(item.PluginId)!,
            item.RestartRequired, budget.Strings(item.ChangedFiles), budget.Text(item.Message)!));
        // Disposed providers never participate in a terminal capture. These
        // remaining sources are retained managed status records only.
        IReadOnlyList<CapabilityDescriptor> capabilities = Array.Empty<CapabilityDescriptor>();
        IReadOnlyList<AdapterModuleStatus> modules = Array.Empty<AdapterModuleStatus>();
        IReadOnlyList<StatusExtensionPoint> extensions = Array.Empty<StatusExtensionPoint>();
        IReadOnlyList<PluginPackageDiagnostic> packageDiagnostics = Array.Empty<PluginPackageDiagnostic>();
        IReadOnlyList<StatusDiscoveredPackage> discovered = Array.Empty<StatusDiscoveredPackage>();
        PerformanceWindowCopy? performance = null;
        GameBuildDescriptor? build = null;
        if (!_stopped)
        {
            capabilities = budget.Rows(_adapter.SupportedCapabilities, id =>
            {
                var item = _capabilities.GetCapability(id);
                return item with { Id = budget.Text(item.Id)!, UnavailableReason = budget.Text(item.UnavailableReason) };
            });
            modules = budget.Rows(_adapter.GetModules(), budget.Module);
            performance = _performance.CaptureWindow();
            build = _capabilities.GameBuild;
            build = build with { GameVersion = budget.Text(build.GameVersion)!, UnityVersion = budget.Text(build.UnityVersion)!,
                Platform = budget.Text(build.Platform)!, Fingerprint = budget.Text(build.Fingerprint)! };
            extensions = budget.Selection(_plugins.Extensions.CaptureDiagnosticDescriptors(budget.NextSelectionLimit), item => new StatusExtensionPoint(budget.Text(item.Id)!,
                item.Version.ToString(), budget.Text(item.PayloadType.FullName), item.Phase, budget.Text(item.Description)));
            packageDiagnostics = budget.Rows(_packages.Diagnostics, item => item with
                { PackagePath = budget.Text(item.PackagePath)!, Code = budget.Text(item.Code)!, Message = budget.Text(item.Message)! });
            discovered = budget.Rows(_discovery.Packages, item => new StatusDiscoveredPackage(budget.Text(item.Metadata.Manifest.Id)!,
                _loadedPackageIds.Contains(item.Metadata.Manifest.Id), !_configuration.AllowOptionalWrites ||
                    (!_configuration.EnableProofPlugins && IsBundledValidationPackage(item.Metadata))));
        }
        var demand = _lastCapabilityDemand is null ? null : budget.Strings(_lastCapabilityDemand);
        var failures = budget.Strings(_shutdownFailures);
        var stopReason = budget.Text(_stopReason);
        var writerHealth = _statusWriter!.GetSnapshot();
        var statusHealth = new StatusHealthCopy(_statusCaptureFailures, budget.Text(_statusCaptureError),
            writerHealth with { LastError = budget.Text(writerHealth.LastError) });
        return new(Path.Combine(Bootstrap.DataRoot, "status.json"), revision, _stopped, stopReason, _hostTickSequence,
            scene, runtime, demand, failures, build, plugins, resources, capabilities, details, diagnostics, _main.GetSnapshot(),
            modules, performance, extensions, packageDiagnostics, discovered, updates, budget.Limits,
            statusHealth);
    }
    private sealed record Settings(bool EnableExamples = false, bool RunSelfValidation = false, string[]? DisabledPlugins = null,
        string[]? ValidationRequiredCapabilities = null, string? Language = "en", double? CallbackBudgetMilliseconds = null);
    private sealed record Command(string Id, string? Action, string? PluginId, string? Language = null,
        string? ExpectedSessionId = null);
    private sealed record ValidationCommand(double AtSeconds, string Action, string? PluginId);
}
