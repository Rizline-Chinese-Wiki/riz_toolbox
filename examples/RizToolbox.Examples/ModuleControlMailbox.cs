using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using RizToolbox.Sdk;

namespace RizToolbox.Examples;

/// <summary>Opt-in test transport over this plugin's storage, never a second gameplay provider.
/// Requests still need an actual SDK lease for the exact run and controller generation.</summary>
internal sealed class ModuleControlMailbox : IDisposable
{
    internal const string RequestName = "runtime-module-proof/control-request.json";
    internal const string StatusName = "runtime-module-proof/control-status.json";
    internal const string ResultName = "runtime-module-proof/control-result.json";
    internal const int MaximumCommands = 32;
    private const int MaximumRequestBytes = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 16 };
    private readonly IPluginStorageService _storage;
    private readonly Func<IGameplayControlService?> _controls;
    private readonly CancellationToken _lifetime;
    private readonly Action<string, object?> _log;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Dictionary<string, byte[]> _outcomes = new(StringComparer.Ordinal);
    private byte[]? _pendingResult;
    private string? _lastInputFingerprint;
    private OperationError? _inputError;
    private OperationError? _storageError;
    private bool _disposed;

    internal string SessionId { get; } = Guid.NewGuid().ToString("N");
    internal int CompletedCommandCount => _outcomes.Count;

    internal ModuleControlMailbox(IPluginStorageService storage, Func<IGameplayControlService?> controls,
        CancellationToken lifetime, Action<string, object?> log, Func<DateTimeOffset>? utcNow = null)
        => (_storage, _controls, _lifetime, _log, _utcNow) = (storage, controls, lifetime, log, utcNow ?? (() => DateTimeOffset.UtcNow));

    internal void Tick(GameplayControlObservationSnapshot? observation)
    {
        if (_disposed || _lifetime.IsCancellationRequested) return;
        // A failed acknowledgement must never cause a second execution. Hold it until
        // storage accepts it, including when another request arrives in the meantime.
        if (_pendingResult is not null && !FlushResult()) return;
        var now = _utcNow();
        var state = observation?.State;
        if (!Write(StatusName, JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, SessionId, updatedUtc = now, active = true,
            commandsConsumed = _outcomes.Count, maxCommands = MaximumCommands,
            observationRevision = observation?.Revision, error = observation?.Error, inputError = _inputError,
            state = state is null ? null : new { state.ControllerGeneration, state.ObservedRunId, state.ControllerAvailable,
                state.LoadOver, state.TimeReady, state.Playing, state.Paused, state.LevelOver, state.Crashed, state.CanDoOperation,
                operations = state.Operations.Select(op => new { action = op.Action.ToString(), op.Supported,
                    ready = op.StateReadiness?.Success, error = op.StateReadiness?.Error ?? op.Error }) }
        }, JsonOptions))) return;

        var entries = _storage.List(PluginStorageArea.Data);
        if (!entries.Success) { StorageFailed(entries.Error!); return; }
        var entry = entries.Value!.FirstOrDefault(item => item.Name == RequestName);
        if (entry is null) return;
        if (entry.Bytes is <= 0 or > MaximumRequestBytes)
        { RejectInput("size:" + entry.Bytes, "Control request must contain 1..4096 bytes."); return; }
        var read = _storage.Read(PluginStorageArea.Data, RequestName);
        if (!read.Success) { StorageFailed(read.Error!); return; }
        var bytes = read.Value!;
        if (bytes.Length is <= 0 or > MaximumRequestBytes)
        { RejectInput("read-size:" + bytes.Length, "Control request changed size or exceeds 4096 bytes."); return; }
        if (bytes.Length != entry.Bytes)
        { RejectInput("changed-size:" + bytes.Length, "Control request changed size after enumeration; wait for a stable request."); return; }
        var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        if (_lastInputFingerprint == fingerprint) return;
        _lastInputFingerprint = fingerprint;
        _inputError = null;

        string? nonce = null, action = null;
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Control request must be an object.");
            // Extract a correlation ID without permitting duplicate keys to choose an action.
            nonce = Text(root, "nonce");
            if (!Guid.TryParseExact(nonce, "N", out var nonceGuid)) throw new FormatException("nonce must be a GUID in N format.");
            nonce = nonceGuid.ToString("N");
            if (!Guid.TryParseExact(Text(root, "sessionId"), "N", out var sessionGuid))
                throw new FormatException("sessionId must be a GUID in N format.");
            action = Text(root, "action");
            if (sessionGuid.ToString("N") != SessionId)
            {
                // A new activation cannot certify what happened to an old request.
                // Preserve its actual acknowledgement (or its unknown outcome).
                _inputError = new(ErrorCode.ScopeExpired, "Request belongs to another probe activation.");
                _log("MAILBOX INPUT REFUSED", _inputError);
                return;
            }
            if (_outcomes.TryGetValue(nonce!, out var previous))
            { _pendingResult = previous; FlushResult(); return; }
            if (_outcomes.Count >= MaximumCommands)
            { Publish(nonce!, action, "validation", Failed(ErrorCode.QuotaExceeded, "Probe command budget exhausted; explicitly reactivate the probe."), remember: false); return; }
            GameplayControlRequest request;
            DateTimeOffset expires;
            try { request = ParseRequest(root, now, out expires); }
            catch (Exception error) when (error is FormatException or InvalidOperationException or JsonException)
            { Publish(nonce!, action, "validation", Failed(ErrorCode.InvalidArgument, error.Message)); return; }

            // Reserve the nonce before acquiring or invoking anything. Even a thrown SDK
            // call or a failed result write leaves this request consumed for this session.
            _outcomes.Add(nonce!, Array.Empty<byte>());
            var stage = "acquire";
            Result<GameplayControlReceipt> result;
            try
            {
                if (_lifetime.IsCancellationRequested) result = Failed(ErrorCode.Cancelled, "Probe scope has ended.");
                else if (_controls() is not { } service) result = Failed(ErrorCode.CapabilityUnavailable, "Gameplay controls are unavailable.");
                else
                {
                    var acquired = service.Acquire(allowExperimental: true, cancellationToken: _lifetime);
                    if (!acquired.Success) result = Result<GameplayControlReceipt>.Fail(acquired.Error!);
                    else
                    {
                        using var lease = acquired.Value ?? throw new InvalidOperationException("SDK returned no control lease.");
                        if (lease.ControllerGeneration != request.ControllerGeneration || lease.ObservedRunId != request.ObservedRunId)
                        { stage = "validation"; result = Failed(ErrorCode.TargetExpired, "Current lease does not match the requested run and controller generation."); }
                        else if (_lifetime.IsCancellationRequested)
                        { stage = "validation"; result = Failed(ErrorCode.Cancelled, "Probe scope ended during lease acquisition."); }
                        else if (_utcNow() >= expires)
                        { stage = "validation"; result = Failed(ErrorCode.TimedOut, "Control request expired during lease acquisition; it was not executed."); }
                        else
                        { stage = "execute"; result = lease.Execute(request, _lifetime); }
                    }
                }
            }
            catch (Exception error)
            { stage = "exception"; result = Failed(ErrorCode.InternalError, "SDK control call threw; effects may be unknown: " + Bounded(error.Message)); }
            Publish(nonce!, action, stage, result);
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException)
        {
            _inputError = new(ErrorCode.InvalidArgument, Bounded(error.Message));
            _log("MAILBOX INPUT REFUSED", _inputError);
        }
    }

    private static GameplayControlRequest ParseRequest(JsonElement root, DateTimeOffset now, out DateTimeOffset expires)
    {
        expires = default;
        var actionText = Text(root, "action");
        var valued = actionText is "set-speed" or "seek";
        var allowed = new HashSet<string>(StringComparer.Ordinal)
            { "schemaVersion", "sessionId", "nonce", "expectedRunId", "expectedGeneration", "action", "expiresUtc" };
        if (valued) { allowed.Add("value"); allowed.Add("unit"); }
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!allowed.Contains(property.Name) || !found.Add(property.Name))
                throw new FormatException("Unknown or duplicate request field: " + Bounded(property.Name));
        if (!found.SetEquals(allowed)) throw new FormatException("Control request is missing a required field.");
        if (!root.GetProperty("schemaVersion").TryGetInt32(out var version) || version != 1)
            throw new FormatException("Unsupported control request schema.");
        var run = Text(root, "expectedRunId");
        if (string.IsNullOrWhiteSpace(run) || run.Length > 128) throw new FormatException("expectedRunId must contain 1..128 nonblank characters.");
        if (!root.GetProperty("expectedGeneration").TryGetInt64(out var generation) || generation <= 0)
            throw new FormatException("expectedGeneration must be a positive integer.");
        var expiresText = Text(root, "expiresUtc");
        if (expiresText?.Length > 48 || !DateTimeOffset.TryParse(expiresText, CultureInfo.InvariantCulture, DateTimeStyles.None, out expires)
            || expires.Offset != TimeSpan.Zero || expires <= now || expires > now.AddSeconds(125))
            throw new FormatException("expiresUtc must be a UTC expiry within the next 125 seconds.");
        var action = actionText switch
        {
            "pause" => GameplayControlAction.Pause, "resume" => GameplayControlAction.Resume,
            "set-speed" => GameplayControlAction.SetSpeed, "seek" => GameplayControlAction.Seek,
            _ => throw new FormatException("Only pause, resume, set-speed and seek are accepted.")
        };
        double? value = null;
        var unit = GameplayControlValueUnit.None;
        if (valued)
        {
            if (!root.GetProperty("value").TryGetDouble(out var number) || !double.IsFinite(number)
                || action == GameplayControlAction.SetSpeed && number <= 0)
                throw new FormatException("Control value must be finite; speed must also be positive.");
            unit = (action, Text(root, "unit")) switch
            {
                (GameplayControlAction.SetSpeed, "rate") => GameplayControlValueUnit.PlaybackRateMultiplier,
                (GameplayControlAction.Seek, "game-seconds") => GameplayControlValueUnit.GameSeconds,
                (GameplayControlAction.Seek, "chart-coordinates") => GameplayControlValueUnit.ChartCoordinates,
                _ => throw new FormatException("Set-speed requires unit 'rate'. Seek requires 'game-seconds' or 'chart-coordinates'.")
            };
            value = number;
        }
        return new(action, generation, run, value, unit);
    }

    private void Publish(string nonce, string? action, string stage, Result<GameplayControlReceipt> result, bool remember = true)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            { schemaVersion = 1, SessionId, nonce, completedUtc = _utcNow(), action, stage, result }, JsonOptions);
        // SDK evidence is retained as supplied. Dispatched and Confirmed are never relabelled.
        if (remember) _outcomes[nonce] = bytes;
        _pendingResult = bytes;
        _log("MAILBOX RESULT", new { SessionId, nonce, action, stage, result });
        FlushResult();
    }

    private bool FlushResult()
    {
        if (_pendingResult is null) return true;
        if (!Write(ResultName, _pendingResult)) return false;
        _pendingResult = null;
        return true;
    }

    private bool Write(string name, byte[] bytes)
    {
        var result = _storage.Write(PluginStorageArea.Data, name, bytes);
        if (!result.Success) { StorageFailed(result.Error!); return false; }
        _storageError = null;
        return true;
    }

    private void StorageFailed(OperationError error)
    {
        if (_storageError != error) _log("MAILBOX STORAGE REFUSED", error);
        _storageError = error;
    }

    private void RejectInput(string fingerprint, string message)
    {
        if (_lastInputFingerprint == fingerprint) return;
        _lastInputFingerprint = fingerprint;
        _inputError = new(ErrorCode.InvalidArgument, message);
        _log("MAILBOX INPUT REFUSED", _inputError);
    }

    private static string? Text(JsonElement value, string name)
        => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    private static Result<GameplayControlReceipt> Failed(ErrorCode code, string message) => Result<GameplayControlReceipt>.Fail(new(code, message));
    private static string Bounded(string message) => message.Length <= 256 ? message : message[..256];
    public void Dispose() => _disposed = true;
}
