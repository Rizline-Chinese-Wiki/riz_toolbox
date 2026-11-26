using System.Diagnostics;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Core;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Hosting;
using RizToolbox.Sdk;

namespace RizToolbox.Loader;

// A status worker receives only these detached value records. It never keeps a
// host, provider, plugin instance, Type, Exception, or delayed query alive.
internal sealed record HostStatusCopy(
    string TargetPath, long Revision, bool Stopped, string? StopReason, long HostTickSequence, SceneStatus Scene,
    RuntimeConfigurationStatus Runtime, IReadOnlyList<string>? CapabilityDemand,
    IReadOnlyList<string> ShutdownFailures, GameBuildDescriptor? GameBuild,
    IReadOnlyList<PluginStatus> Plugins, IReadOnlyList<PluginResourceCounts> PluginResources,
    IReadOnlyList<CapabilityDescriptor> Capabilities, IReadOnlyList<StatusCapabilityInfo> CapabilityDetails,
    DiagnosticSnapshot Diagnostics, DispatcherSnapshot Dispatcher,
    IReadOnlyList<AdapterModuleStatus> AdapterModules, PerformanceWindowCopy? Performance,
    IReadOnlyList<StatusExtensionPoint> ExtensionPoints,
    IReadOnlyList<PluginPackageDiagnostic> PackageDiagnostics,
    IReadOnlyList<StatusDiscoveredPackage> DiscoveredPackages,
    IReadOnlyList<StatusPackageUpdate> PackageUpdates, StatusCaptureLimits CaptureLimits,
    StatusHealthCopy StatusHealth);

internal sealed record StatusExtensionPoint(string Id, string Version, string? Payload,
    ExtensionPointPhase Phase, string? Description);
internal sealed record StatusDiscoveredPackage(string Id, bool Loaded, bool BlockedByRuntimeMode);
internal sealed record StatusPackageUpdate(string PluginId, bool? RestartRequired,
    IReadOnlyList<string> ChangedFiles, string Message);
internal sealed record StatusCaptureLimits(int MaximumRows, int MaximumCharacters, int MaximumTotalRows,
    int MaximumTotalCharacters, long TruncatedValues);
internal sealed record StatusHealthCopy(long CaptureFailures, string? LastCaptureError,
    LatestDiagnosticWriterSnapshot Persistence);
internal sealed record StatusCapabilityInfo(string Id, Version Version, CapabilityStability Stability,
    bool Available, long Generation, string? UnavailableReason, IReadOnlyList<StatusCapabilityOperation> Operations);
internal sealed record StatusCapabilityOperation(string Id, OperationThread Thread, ObservationMode Observation,
    double? SamplingIntervalSeconds, CleanupResponsibility Cleanup, bool ModifiesGameplay,
    IReadOnlyList<string> SupportedScenes, string Description);

internal static class HostStatusPersistence
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    internal static async Task Write(HostStatusCopy copy)
    {
        // All transformations, percentile sorting and complete JSON encoding
        // happen here, after the owner-thread capture has finished.
        object document = copy.Stopped ? new
        {
            revision = copy.Revision, stopped = true, stopReason = copy.StopReason,
            lastCompletedHostTickSequence = copy.HostTickSequence,
            runtime = copy.Runtime, requiredCapabilitiesRequested = copy.CapabilityDemand,
            shutdownFailures = copy.ShutdownFailures, sdkVersion = SdkInfo.Version.ToString(),
            plugins = copy.Plugins, pluginResources = copy.PluginResources,
            capabilityDetails = copy.CapabilityDetails, diagnostics = copy.Diagnostics,
            dispatcher = copy.Dispatcher,
            packageUpdates = PackageUpdates(copy), statusCapture = copy.CaptureLimits, statusDiagnostics = copy.StatusHealth
        } : new
        {
            revision = copy.Revision, stopped = false, stopReason = copy.StopReason,
            commandReceiptVersion = 1,
            lastCompletedHostTickSequence = copy.HostTickSequence,
            runtime = copy.Runtime, requiredCapabilitiesRequested = copy.CapabilityDemand,
            shutdownFailures = copy.ShutdownFailures, sdkVersion = SdkInfo.Version.ToString(),
            scene = copy.Scene.Name, sceneGeneration = copy.Scene.SceneGeneration,
            sceneObservation = copy.Scene.Observation.ToString(),
            gameBuild = copy.GameBuild, plugins = copy.Plugins,
            capabilities = copy.Capabilities, capabilityDetails = copy.CapabilityDetails,
            diagnostics = copy.Diagnostics, dispatcher = copy.Dispatcher,
            adapterModules = copy.AdapterModules,
            performance = CallbackPerformanceMonitor.Summarize(copy.Performance!),
            pluginResources = copy.PluginResources, processResources = CaptureProcessResources(),
            extensionPoints = copy.ExtensionPoints.Select(x => new
                { x.Id, version = x.Version, payload = x.Payload, x.Phase, x.Description }).ToArray(),
            packageDiagnostics = copy.PackageDiagnostics,
            discoveredPackages = copy.DiscoveredPackages.Select(x => new
                { id = x.Id, loaded = x.Loaded, blockedByRuntimeMode = x.BlockedByRuntimeMode }).ToArray(),
            packageUpdates = PackageUpdates(copy), statusCapture = copy.CaptureLimits, statusDiagnostics = copy.StatusHealth
        };
        var json = JsonSerializer.Serialize(document, Json);
        var temporary = copy.TargetPath + ".tmp";
        await File.WriteAllTextAsync(temporary, json).ConfigureAwait(false);
        File.Move(temporary, copy.TargetPath, true);
    }

    private static object[] PackageUpdates(HostStatusCopy copy) => copy.PackageUpdates.Select(x => (object)new
        { pluginId = x.PluginId, restartRequired = x.RestartRequired, changedFiles = x.ChangedFiles, message = x.Message }).ToArray();

    private static object CaptureProcessResources()
    {
        try
        {
            using var process = Process.GetCurrentProcess(); process.Refresh();
            return new { processId = process.Id, managedBytes = GC.GetTotalMemory(false), totalAllocatedBytes = GC.GetTotalAllocatedBytes(false),
                workingSetBytes = process.WorkingSet64, privateBytes = process.PrivateMemorySize64, handleCount = process.HandleCount,
                gen0Collections = GC.CollectionCount(0), gen1Collections = GC.CollectionCount(1), gen2Collections = GC.CollectionCount(2) };
        }
        catch (Exception error) { return new { unavailableReason = StatusCopyBudget.SafeError(error) }; }
    }
}

internal sealed class StatusCopyBudget
{
    internal const int MaximumRows = 256, MaximumCharacters = 4096,
        MaximumTotalRows = 4096, MaximumTotalCharacters = 1_048_576;
    private long _truncated;
    private int _rows, _characters;
    internal StatusCaptureLimits Limits => new(MaximumRows, MaximumCharacters, MaximumTotalRows, MaximumTotalCharacters, _truncated);
    internal int NextSelectionLimit => Math.Min(MaximumRows, MaximumTotalRows - _rows);
    internal string? Text(string? text)
    {
        if (text is null) return null;
        var count = Math.Min(text.Length, Math.Min(MaximumCharacters, MaximumTotalCharacters - _characters));
        _characters += count;
        if (count == text.Length) return text;
        _truncated++; return text[..count];
    }
    internal IReadOnlyList<TOut> Selection<TIn, TOut>(DiagnosticRowCopy<TIn> selection, Func<TIn, TOut> copy)
    {
        _truncated += selection.TruncatedValues;
        return Rows(selection.Items, copy);
    }
    internal IReadOnlyList<TOut> Rows<TIn, TOut>(IEnumerable<TIn> items, Func<TIn, TOut> copy)
    {
        var result = new List<TOut>();
        foreach (var item in items)
        {
            if (result.Count == MaximumRows || _rows == MaximumTotalRows) { _truncated++; break; }
            _rows++;
            result.Add(copy(item));
        }
        return Array.AsReadOnly(result.ToArray());
    }
    internal IReadOnlyList<string> Strings(IEnumerable<string> items) => Rows(items, x => Text(x)!);
    internal OperationError? Error(OperationError? error) => error is null ? null
        : error with { Message = Text(error.Message)!, CapabilityId = Text(error.CapabilityId), TargetId = Text(error.TargetId) };
    internal StatusCapabilityInfo Capability(DiagnosticCapabilityRow item) => new(Text(item.Id)!, item.Version, item.Stability, item.Available,
        item.Generation, Text(item.UnavailableReason), Rows(item.Operations, operation => new StatusCapabilityOperation(Text(operation.Id)!,
            operation.Thread, operation.Observation, operation.SamplingIntervalSeconds, operation.Cleanup, operation.ModifiesGameplay,
            Strings(operation.SupportedScenes), Text(operation.Description)!)));
    internal AdapterModuleStatus Module(AdapterModuleStatus item)
    {
        var manifest = item.Manifest;
        var copied = manifest with
        {
            Id = Text(manifest.Id)!, Capabilities = Strings(manifest.Capabilities),
            RequiredCapabilities = Strings(manifest.RequiredCapabilities), SupportedScenes = Strings(manifest.SupportedScenes),
            Evidence = Text(manifest.Evidence)!,
            RequiredSymbols = Rows(manifest.RequiredSymbols, symbol => symbol with
            {
                Id = Text(symbol.Id)!, Assembly = Text(symbol.Assembly)!, Namespace = Text(symbol.Namespace)!,
                Type = Text(symbol.Type)!, Member = Text(symbol.Member), ExpectedType = Text(symbol.ExpectedType),
                ParameterTypes = symbol.ParameterTypes is null ? null : Strings(symbol.ParameterTypes)
            })
        };
        return item with { Manifest = copied, ProfileId = Text(item.ProfileId)!, BuildFingerprint = Text(item.BuildFingerprint)!,
            Failure = Text(item.Failure), Symbols = Rows(item.Symbols, symbol => symbol with
                { Id = Text(symbol.Id)!, Signature = Text(symbol.Signature)!, Failure = Text(symbol.Failure) }) };
    }
    internal static string SafeError(Exception error)
    {
        try { var text = error.GetType().Name + ": " + error.Message; return text.Length <= 512 ? text : text[..512]; }
        catch { return "Diagnostic capture is unavailable."; }
    }
}
