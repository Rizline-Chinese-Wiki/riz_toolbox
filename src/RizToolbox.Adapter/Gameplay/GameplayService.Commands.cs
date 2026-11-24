using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class GameplayService
{
    private ControlCommand? _controlCommand;
    private CommandSource? _withdrawnSource;

    private sealed record CommandSource(long Information, long Controller, string? SessionId, long SceneGeneration,
        string? RunId, GameplaySourceIdentity? Source);

    private void ObserveCommandSource(long information, long controller, GameLifecycleSnapshot? lifecycle,
        string? runId, GameplaySourceIdentity? source)
    {
        if (_withdrawnSource is not { } previous) return;
        // Missing observations can end a lease without proving that its old
        // source has gone. Reset an unknown-write refusal only on a replacement.
        var changed = previous.Information != information || previous.Controller != controller
            || lifecycle is not null && previous.SessionId is not null
                && (previous.SessionId != lifecycle.SessionId || previous.SceneGeneration != lifecycle.SceneGeneration)
            || runId is not null && previous.RunId is not null && previous.RunId != runId
            || previous.Source is not null && source is not null && !SameSourceGraph(previous.Source, source)
            || previous.Source?.NativeDescriptor is { } oldDescriptor && source?.NativeDescriptor is { } newDescriptor
                && oldDescriptor != newDescriptor;
        if (!changed) return;
        _withdrawn.Clear();
        _withdrawnSource = null;
    }

    private OperationError? ValidateControlOwner(ControlLease lease, GameplayControlRequest request)
    {
        if (!lease.Active || !ReferenceEquals(_owner, lease) || request.ControllerGeneration != _generation
            || request.ControllerGeneration != lease.ControllerGeneration || request.ObservedRunId != _observedRunId
            || request.ObservedRunId != lease.ObservedRunId)
            return ControlError(new(ErrorCode.TargetExpired, "The control lease, observed run or controller generation has ended."), request.Action);
        return null;
    }

    private Result<ControlCommand> ReserveControlCommand(ControlLease lease, GameplayControlRequest request, CancellationToken cancellation)
    {
        var error = Guard(lease.Context, cancellation);
        if (error is not null) return Result<ControlCommand>.Fail(ControlError(error));
        error = ValidateRequest(request) ?? ValidateControlOwner(lease, request);
        if (error is not null) return Result<ControlCommand>.Fail(error);
        if (_controlCommand is not null)
            return Result<ControlCommand>.Fail(ControlError(new(ErrorCode.Conflict, "A gameplay control command already owns this session."), request.Action));
        if (_withdrawn.TryGetValue(request.Action, out var refusal))
            return Result<ControlCommand>.Fail(ControlError(refusal, request.Action));

        var command = new ControlCommand(this, lease, request, cancellation);
        _controlCommand = command;
        return Result<ControlCommand>.Ok(command);
    }

    private Result<IGameplayControlCommand> Submit(ControlLease lease, GameplayControlRequest request, CancellationToken cancellation)
    {
        var reserved = ReserveControlCommand(lease, request, cancellation);
        return reserved.Success ? Result<IGameplayControlCommand>.Ok(reserved.Value!) : Result<IGameplayControlCommand>.Fail(reserved.Error!);
    }

    private Result<GameplayControlReceipt> Execute(ControlLease lease, GameplayControlRequest request, CancellationToken cancellation)
    {
        var reserved = ReserveControlCommand(lease, request, cancellation);
        return reserved.Success ? RunControlCommand(reserved.Value!) : Result<GameplayControlReceipt>.Fail(reserved.Error!);
    }

    internal void PumpControlCommand()
    {
        _main.VerifyAccess();
        if (!_disposed && !_executing && _controlCommand is { } command) RunControlCommand(command);
    }

    private Result<GameplayControlReceipt> RunControlCommand(ControlCommand command)
    {
        var lease = command.Lease!;
        var request = command.Snapshot.Request;
        var cancellation = command.Cancellation;
        _executing = true;
        try
        {
            command.Update(GameplayControlCommandState.Executing);
            // This gate covers availability, graph reads, preparation, the
            // write, readback and disposal. A native getter can reenter here.
            var error = Guard(lease.Context, cancellation, allowExecuting: true);
            if (error is not null) return FinishFailedCommand(command, ControlError(error, request.Action));
            error = ValidateControlOwner(lease, request);
            if (error is not null) return FinishFailedCommand(command, error);

            var result = ExecuteReserved(command, lease, request, cancellation);
            if (!result.Success) return FinishFailedCommand(command, result.Error!);
            CompleteCommand(command, GameplayControlCommandState.Succeeded, result.Value);
            return result;
        }
        catch (Exception error)
        {
            return FinishFailedCommand(command, ControlError(Error(error), request.Action));
        }
        finally { EndExecution(); }
    }

    private Result<GameplayControlReceipt> FinishFailedCommand(ControlCommand command, OperationError error)
    {
        var state = command.WriteStarted && !command.Restored ? GameplayControlCommandState.Unknown
            : !command.WriteStarted && (error.KnownCode is ErrorCode.Cancelled or ErrorCode.ScopeExpired)
                ? GameplayControlCommandState.Cancelled : GameplayControlCommandState.Rejected;
        if (state == GameplayControlCommandState.Unknown)
        {
            error = error with { Retryable = false };
            _withdrawnSource ??= new(_identity, _controllerIdentity, _observedSessionId, _observedSceneGeneration,
                _observedRunId, _sourceIdentity);
            _withdrawn[command.Snapshot.Request.Action] = error with { Code = ErrorCode.OperationUnsupported, Retryable = false };
            _controlObservationDirty = true;
            command.Lease?.Dispose();
        }
        CompleteCommand(command, state, error: error);
        return Result<GameplayControlReceipt>.Fail(error);
    }

    private void CompleteCommand(ControlCommand command, GameplayControlCommandState state,
        GameplayControlReceipt? receipt = null, OperationError? error = null)
    {
        if (ReferenceEquals(_controlCommand, command)) _controlCommand = null;
        command.Update(state, receipt, error);
        command.Detach();
    }

    private Result CancelControlCommand(ControlCommand command)
    {
        if (!_main.IsMainThread) return Result.Fail(ControlError(new(ErrorCode.WrongThread, "Command cancellation requires the host thread.")));
        if (command.Snapshot.State == GameplayControlCommandState.Cancelled) return Result.Ok();
        if (!ReferenceEquals(_controlCommand, command) || command.Snapshot.State != GameplayControlCommandState.Queued)
            return Result.Fail(ControlError(new(ErrorCode.Conflict, "This gameplay command has already started or completed.")));
        CompleteCommand(command, GameplayControlCommandState.Cancelled, error: ControlError(new(ErrorCode.Cancelled, "The command was cancelled before execution.")));
        return Result.Ok();
    }

    private void CancelQueuedControlCommand(ControlLease lease)
    {
        if (_controlCommand is not { } command || !ReferenceEquals(command.Lease, lease)
            || command.Snapshot.State != GameplayControlCommandState.Queued) return;
        CompleteCommand(command, GameplayControlCommandState.Cancelled,
            error: ControlError(new(ErrorCode.Cancelled, "The owning control lease ended before dispatch.")));
    }

    private sealed class ControlCommand : IGameplayControlCommand
    {
        private GameplayService? _service;
        private GameplayControlCommandSnapshot _snapshot;
        internal ControlLease? Lease { get; private set; }
        internal CancellationToken Cancellation { get; private set; }
        internal bool WriteStarted, Restored;

        internal ControlCommand(GameplayService service, ControlLease lease, GameplayControlRequest request, CancellationToken cancellation)
        {
            _service = service;
            Lease = lease;
            Cancellation = cancellation;
            _snapshot = new(Guid.NewGuid(), request, GameplayControlCommandState.Queued);
        }

        public GameplayControlCommandSnapshot Snapshot => Volatile.Read(ref _snapshot);
        public Result Cancel() => Volatile.Read(ref _service)?.CancelControlCommand(this)
            ?? (Snapshot.State == GameplayControlCommandState.Cancelled ? Result.Ok()
                : Result.Fail(new(ErrorCode.Conflict, "This gameplay command already completed.", GameplayCapabilityIds.Controls)));

        internal void Update(GameplayControlCommandState state, GameplayControlReceipt? receipt = null, OperationError? error = null)
            => Volatile.Write(ref _snapshot, _snapshot with { State = state, Receipt = receipt, Error = error });

        internal void Detach()
        {
            Lease = null;
            Cancellation = default;
            Volatile.Write(ref _service, null);
        }
    }
}
