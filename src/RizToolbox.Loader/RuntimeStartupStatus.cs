using System.Text.Json;
using RizToolbox.Sdk;

namespace RizToolbox.Loader;

internal sealed class RuntimeStartupStatus
{
    private enum HostState { AwaitingTransport, AwaitingUnity, AdmissionFailed, InitializingHost, HostInitializationFailed, RunningFirstTick, FirstTickFailed, FirstTickReturned }

    private readonly string _path;
    private readonly RuntimeConfiguration _configuration;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private string _installationState = "Pending";
    private HostState _hostState = HostState.AwaitingTransport;
    private long? _firstHostTickSequence;
    private OperationError? _installationError, _hostError;
    private string? _installationDetail, _hostDetail;

    internal RuntimeStartupStatus(string dataRoot, RuntimeConfiguration configuration, ILogger log)
    {
        _path = Path.Combine(dataRoot, "runtime-startup.json");
        _configuration = configuration;
        _log = log;
        Publish();
    }

    internal void TransportAttached()
    {
        lock (_gate)
        {
            _installationState = "Ready";
            if (_hostState == HostState.AwaitingTransport) _hostState = HostState.AwaitingUnity;
            Publish();
        }
    }

    internal void TransportUnavailable(string? detail)
    {
        lock (_gate)
        {
            _installationState = "Unavailable";
            _installationError = new(ErrorCode.CapabilityUnavailable, "当前游戏版本或安装状态不受支持。");
            _installationDetail = detail;
            Publish();
        }
    }

    internal void AdmissionFailed(Exception error)
    {
        lock (_gate)
        {
            if (_firstHostTickSequence.HasValue || _hostState == HostState.AdmissionFailed) return;
            RecordHostFailure(HostState.AdmissionFailed, ErrorCode.CapabilityUnavailable, "首个 Unity Tick 未通过运行时检查。", error);
        }
    }

    internal void RunFirstTick(long sequence, Action initialize, Action tick)
    {
        lock (_gate)
        {
            if (_firstHostTickSequence.HasValue || _installationError is not null || _hostState == HostState.AdmissionFailed) return;
            _firstHostTickSequence = sequence;
            _hostState = HostState.InitializingHost;
            Publish();
        }

        try { initialize(); }
        catch (Exception error)
        {
            lock (_gate) RecordHostFailure(HostState.HostInitializationFailed, ErrorCode.InternalError, "宿主初始化失败。", error);
            throw;
        }
        lock (_gate)
        {
            _hostState = HostState.RunningFirstTick;
            Publish();
        }

        try { tick(); }
        catch (Exception error)
        {
            lock (_gate) RecordHostFailure(HostState.FirstTickFailed, ErrorCode.InternalError, "宿主首个 Tick 未正常返回。", error);
            throw;
        }
        lock (_gate)
        {
            _hostState = HostState.FirstTickReturned;
            Publish();
        }
    }

    private void RecordHostFailure(HostState state, ErrorCode code, string message, Exception error)
    {
        _hostState = state;
        _hostError = new(code, message);
        _hostDetail = error.Message;
        Publish();
    }

    private void Publish()
    {
        string? temporary = null;
        try
        {
            temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var snapshot = new
            {
                schemaVersion = 1, processId = Environment.ProcessId, observedAtUtc = DateTimeOffset.UtcNow,
                runtime = _configuration.CaptureStatus(), installationState = _installationState,
                hostState = _hostState.ToString(), firstHostTickSequence = _firstHostTickSequence,
                error = _installationError ?? _hostError,
                detail = _installationError is null ? _hostDetail : _installationDetail,
                optionalWritesAllowedByStartup = _installationState == "Ready"
                    && _hostState == HostState.FirstTickReturned && _configuration.AllowOptionalWrites,
                grantsOperationPermission = false
            };
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
        }
        catch (Exception error)
        {
            try { _log.Log(LogLevel.Warning, "Cannot write runtime startup status.", error); } catch { }
        }
        finally
        {
            if (temporary is not null) { try { File.Delete(temporary); } catch { } }
        }
    }
}
