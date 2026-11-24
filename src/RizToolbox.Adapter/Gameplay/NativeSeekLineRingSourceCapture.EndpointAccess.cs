namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekLineRingSourceCapture
{
    private NativeSeekLineEndpointCommands? _endpointWriterOwner;
    private bool _endpointWriterReleasing;

    internal void AdoptByEndpointWriter(NativeSeekLineEndpointCommands owner)
    {
        VerifyThread(); ArgumentNullException.ThrowIfNull(owner);
        if (_endpointWriterOwner is not null || _disposed || _busy || _releasing || _notifyingDisposal
            || _cleanupFailure is not null || _sourceExpired)
            throw new InvalidOperationException("The retained original line capture cannot transfer to another endpoint writer.");
        _endpointWriterOwner = owner;
    }

    internal void DisposeFromEndpointWriter(NativeSeekLineEndpointCommands owner)
    {
        VerifyThread();
        if (!ReferenceEquals(_endpointWriterOwner, owner))
            throw new InvalidOperationException("Only the command owner may release its adopted original line capture.");
        _endpointWriterReleasing = true;
        try { Dispose(); }
        finally { _endpointWriterReleasing = false; }
    }

    // The adopted source owns the only line reader child. A writer callback runs
    // inside that same source/shared operation; public source methods would reenter.
    internal T WithVerifiedEndpointOperation<T>(NativeSeekLineEndpointCommands owner, Func<SeekLineRingSourceSnapshot,
        IRetainedSeekLineEndpointAccess, T> action)
    {
        VerifyThread(); ArgumentNullException.ThrowIfNull(action);
        if (!ReferenceEquals(_endpointWriterOwner, owner))
            throw new InvalidOperationException("The retained line reader belongs to another endpoint writer.");
        try
        {
            return Operation(() =>
            {
                var current = VerifyInOperation(baseline: false);
                if (_reader is not IRetainedSeekLineEndpointAccess access)
                    throw new InvalidOperationException("The original line reader has no endpoint write capability.");
                var result = action(current, access);
                VerifyInOperation(baseline: false);
                return result;
            });
        }
        catch
        {
            _sourceVerified = false; _baselineVerified = false;
            throw;
        }
    }

    // The endpoint command owner also owns the original producer scratch.
    // This callback stays inside the one retained reader/shared operation.
    internal T WithVerifiedLineBufferOperation<T>(NativeSeekLineEndpointCommands owner,
        Func<SeekLineRingSourceSnapshot, IRetainedSeekLineBufferAccess, T> action)
    {
        VerifyThread(); ArgumentNullException.ThrowIfNull(action);
        if (!ReferenceEquals(_endpointWriterOwner, owner))
            throw new InvalidOperationException("The original line buffer belongs to another endpoint writer.");
        try
        {
            return Operation(() =>
            {
                var current = VerifyInOperation(baseline: false);
                if (_reader is not IRetainedSeekLineBufferAccess access)
                    throw new InvalidOperationException("The original line reader has no Vector3 scratch capability.");
                var result = action(current, access);
                VerifyInOperation(baseline: false);
                return result;
            });
        }
        catch
        {
            _sourceVerified = false; _baselineVerified = false;
            throw;
        }
    }

    internal T WithVerifiedLineRendererOperation<T>(NativeSeekLineEndpointCommands owner,
        Func<SeekLineRingSourceSnapshot, IRetainedSeekLineRendererAccess, T> action)
    {
        VerifyThread(); ArgumentNullException.ThrowIfNull(action);
        if (!ReferenceEquals(_endpointWriterOwner, owner))
            throw new InvalidOperationException("The original renderer source belongs to another command owner.");
        try
        {
            return Operation(() =>
            {
                var current = VerifyInOperation(baseline: false);
                if (_reader is not IRetainedSeekLineRendererAccess access)
                    throw new InvalidOperationException("The original reader has no renderer capability.");
                var result = action(current, access);
                VerifyInOperation(baseline: false);
                return result;
            });
        }
        catch
        {
            _sourceVerified = false; _baselineVerified = false;
            throw;
        }
    }

    internal void RequireRendererCanvasAssociationInOperation(NativeSeekLineEndpointCommands owner,
        NativeSeekCanvasTargetCapture capture, NativeSeekCanvasTargetPlan plan)
    {
        VerifyThread(); Local();
        if (!_busy || !ReferenceEquals(_endpointWriterOwner, owner)
            || !ReferenceEquals(_canvasCapture, capture) || _snapshot is null
            || !ReferenceEquals(_snapshot.Identity.CanvasCapture, capture)
            || !ReferenceEquals(_snapshot.Identity.CanvasPlan, plan))
            throw new InvalidOperationException("The held original renderer operation has another Canvas capture/Plan or owner.");
        Local();
    }
}
