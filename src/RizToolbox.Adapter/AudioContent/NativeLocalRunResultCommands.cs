using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

// A request interface only: neither successful queuing nor an action result
// certifies cancellation, scene completion, Close or retirement.
internal interface ILocalRunOwnedResultCommandTarget
{
    Result QueueOwnedResult(LocalRunSessionRegistry.SessionHandle session,
        LocalRunCapturedResult exactResult, LocalRunHostResultAction action);
}

internal sealed class NativeLocalRunResultCommands
{
    private readonly IMainThreadDispatcher _main;
    private readonly LocalRunSessionRegistry _registry;
    private readonly ILocalRunOwnedResultCommandTarget _target;
    private bool _busy;
    internal NativeLocalRunResultCommands(IMainThreadDispatcher main, LocalRunSessionRegistry registry,
        ILocalRunOwnedResultCommandTarget target)
        => (_main, _registry, _target) = (main, registry, target);

    internal Result Execute(LocalRunSessionRegistry.SessionHandle session, LocalRunCapturedResult result,
        LocalRunHostResultAction action)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(result);
        if (!Enum.IsDefined(typeof(LocalRunHostResultAction), action)) return Result.Fail(new(ErrorCode.InvalidArgument, "Unknown LOCAL result action."));
        if (_busy) return Result.Fail(new(ErrorCode.Conflict, "A LOCAL result command is already being dispatched."));
        var current = _registry.Read(session);
        if (current.Phase != LocalRunSessionPhase.ResultReady || current.ReturnRequested
            || current.ProtectionRetired || current.CleanupAttempted || current.ControllerIdentity == 0
            || current.Identity != result.Session || !ReferenceEquals(current.Result, result) || current.Failure is not null)
            return Result.Fail(new(ErrorCode.TargetExpired, "The exact cached LOCAL result no longer admits commands."));
        _busy = true;
        try { return _target.QueueOwnedResult(session, result, action)
            ?? throw new InvalidOperationException("The exact LOCAL command owner returned no request result."); }
        finally { _busy = false; }
    }
}
