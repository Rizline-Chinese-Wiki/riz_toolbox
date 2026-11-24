using System.Runtime.InteropServices;
using System.Text.Json;
using RizToolbox.Adapter.Modules;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Keeps the process protection gates available and starts the LOCAL
/// play composition on demand. Unretired sessions retain their gate ownership.</summary>
internal sealed class LocalRunProtectionModule : IAdapterModule, ILocalRunPlayDispatcher
{
    internal const string Capability = "rizline.internal.local-run-protection";
    private readonly NativeRuntime _api; private readonly IMainThreadDispatcher _main; private readonly ILogger _log;
    private readonly AdapterBuildProfile _profile; private readonly string _dobby;
    private readonly Func<IOwnedUiService>? _hostUi;
    private readonly Func<LocalRunLaunchCommitState, LocalRunModification>? _trackModification;
    private LocalRunProtectionHost? _host;
    private NativeLocalAudioContentRuntime? _runtime;
    private string? _last, _lastComposition;
    internal bool RetainsResultUi => _runtime?.RetainsResultUi == true;
    internal void ObserveRunModification() => _runtime?.ObserveRunModification();
    internal LocalRunProtectionModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log, AdapterBuildProfile profile, string dobby,
        Func<IOwnedUiService>? hostUi = null, Func<LocalRunLaunchCommitState, LocalRunModification>? trackModification = null)
        => (_api, _main, _log, _profile, _dobby, _hostUi, _trackModification) = (api, main, log, profile, dobby, hostUi, trackModification);
    internal LocalRunProtectionHost Host { get { _main.VerifyAccess(); return _host ?? throw new InvalidOperationException("LOCAL protection is not installed."); } }
    // The production judgement provider installs the physical native activity
    // accounting required by LOCAL admission. Wait before creating retained gates.
    public AdapterModuleManifest Manifest { get; } = new("rizline.local-run-protection", new(0, 1), new[] { Capability },
        new[] { JudgementCapabilityIds.Observe }, Array.Empty<string>(), new[] {
            Method("local.start", "LevelOverControl", "Start"), Method("local.controller-retry", "NormalModeInLevelController", "Retry"),
            Method("local.result-retry", "LevelOverControl", "Retry"), Method("local.result-back", "LevelOverControl", "Back"),
            Method("local.controller-level-over", "NormalModeInLevelController", "LevelOver") },
        "Host-only pre-original LOCAL gates and copied counters. Exact bound receivers only; no unbound result-page classification, launch transaction, or playable LOCAL capability.");
    private static AdapterSymbolRequirement Method(string id, string type, string method)
        => new(id, "Assembly-CSharp.dll", "", type, AdapterSymbolKind.Method, method, false, Array.Empty<string>()) { ExpectedType = "System.Void" };
    public double IntervalSeconds => 1;
    public ModuleProbe Probe()
    {
        _main.VerifyAccess();
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint
            || _profile.UnityVersion != "2022.3.62f2")
            return ModuleProbe.Unsupported("LOCAL run protection requires the pinned Windows x64 Unity build.");
        if (_host is not null)
        {
            var previous = _host.Read();
            if (previous.CleanupUnknown) { WriteFailureSnapshot(); return Unconfirmed(previous); }
            if (previous.Stopped) { _host = null; _runtime = null; }
            else if (previous.StopRequested) return ModuleProbe.Waiting("Previous LOCAL consumers still require their retained protection host.");
            else return Tick();
        }
        try
        {
            var transport = NativeLocalRunBoundaryTransport.Resolve(_api, _main, _log, _profile, GetModuleHandle("GameAssembly.dll"), _dobby);
            _host = LocalRunProtectionHost.Create(_main, _log, transport);
            WriteSnapshot(); return ModuleProbe.Available;
        }
        catch (LocalRunProtectionCreationException error)
        {
            _host = error.Responsibility;
            WriteFailureSnapshot(); return Unconfirmed(_host.Read());
        }
        catch (UnsupportedSignatureException error) { return ModuleProbe.Unsupported(error.Message); }
    }
    private static ModuleProbe Unconfirmed(LocalRunProtectionSnapshot snapshot)
        => ModuleProbe.Unsupported("Previous LOCAL gate responsibility is unknown; process restart is required. "
            + (snapshot.Failure ?? "The retained owner supplied no confirmed failure detail."));
    private void WriteFailureSnapshot()
    {
        // Diagnostic failure must not replace the original unavailable reason.
        try { WriteSnapshot(); } catch { }
    }
    public void Register(ModuleRegistration registration)
        => registration.Register<ILocalRunProtectionDiagnostics>(Capability, new Diagnostics(Host));
    public ModuleProbe Tick()
    {
        _main.VerifyAccess();
        if (_runtime is { InstallationPending: true } runtime)
        {
            try { _ = runtime.TryInstall(); }
            catch (Exception) when (runtime.Unknown)
            {
                WriteFailureSnapshot(); return Unconfirmed(Host.Read());
            }
        }
        WriteSnapshot(); return ModuleProbe.Available;
    }
    public Result RequestPlay(LocalPlayRequest request, CancellationToken cancellationToken)
    {
        _main.VerifyAccess();
        if (cancellationToken.IsCancellationRequested) return Result.Fail(new(ErrorCode.Cancelled, "Play cancelled."));
        ArgumentNullException.ThrowIfNull(request);
        const string notReady = "The formal LOCAL protection composition is not ready.";
        if (_host is not { } host) return PlayUnavailable(notReady);
        if (_runtime is null)
        {
            var state = host.Read();
            if (!state.AllGatesInstalled || state.StopRequested || state.Stopped || state.CleanupUnknown)
                return PlayUnavailable(state.Failure ?? notReady);
            if (_hostUi is null) return PlayUnavailable("The formal adapter supplied no shared host UI owner.");
            if (cancellationToken.IsCancellationRequested) return Result.Fail(new(ErrorCode.Cancelled, "Play cancelled."));
            _runtime = new(_api, _main, _log, _profile, GetModuleHandle("GameAssembly.dll"), _dobby, host, _hostUi, _trackModification);
        }
        return _runtime.RequestPlay(request, cancellationToken);
    }
    private static Result PlayUnavailable(string reason)
        => Result.Fail(new(ErrorCode.CapabilityUnavailable, reason, AudioContentCapabilityIds.LocalCatalogSelection));
    private void WriteSnapshot()
    {
        var value = JsonSerializer.Serialize(Host.Read());
        if (value.Length > 1800) throw new InvalidDataException("LOCAL protection status exceeded its bounded row.");
        if (value != _last) { _last = value; _log.Log(LogLevel.Info, "LocalRunProtection " + value); }
        var composition = JsonSerializer.Serialize(new { installed = _runtime?.Installed ?? false,
            unknown = _runtime?.Unknown ?? false, reason = _runtime?.Failure });
        if (composition.Length > 1800) throw new InvalidDataException("LOCAL composition status exceeded its bounded row.");
        if (composition != _lastComposition) { _lastComposition = composition; _log.Log(LogLevel.Info, "LocalRunComposition " + composition); }
    }
    public void Release()
    {
        _main.VerifyAccess(); if (_host is null) return;
        if (_runtime is not null) _runtime.RequestStop(); else _host.RequestStop();
        // Keep the reference, including unknown slots, for explicit recovery checks.
        // The independent process pump continues after this module withdraws.
    }
    private sealed class Diagnostics : ILocalRunProtectionDiagnostics
    {
        private readonly LocalRunProtectionHost _host;
        internal Diagnostics(LocalRunProtectionHost host) => _host = host;
        public LocalRunProtectionSnapshot Read() => _host.Read();
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
