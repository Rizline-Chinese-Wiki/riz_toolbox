using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockClockRouter
{
    private readonly NativePlaybackFreshSourceCapture? _freshCapture;
    private NativePlaybackFreshSourceCapture.Adoption? _freshAdoption;
    internal static NativePlaybackStockClockRouter AdoptFresh(NativeRuntime api, NativePlaybackFreshSourceCapture capture,
        GameEventState.PlaybackRunAssociation run, PlaybackSourceGroupLease group)
        => new(api, capture, run, group);
    private NativePlaybackStockClockRouter(NativeRuntime api, NativePlaybackFreshSourceCapture capture,
        GameEventState.PlaybackRunAssociation run, PlaybackSourceGroupLease group)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(run); ArgumentNullException.ThrowIfNull(group);
        _api = api; _run = run; _group = group; _freshCapture = capture;
        _registeredOwner = this; _binding = null!; _origin = null!;
        try
        {
            var reservation = capture.ReserveAdoption(this, api, run, group);
            var adoption = reservation.Adoption;
            _binding = reservation.Binding; _origin = reservation.Origin;
            var route = new object(); _sourceRoute = route;
            _source = new(api, adoption, _binding, _origin, run, group,
                receipt => { if (ReferenceEquals(_sourceRoute, route)) _session?.Observe(receipt); },
                reason => { if (ReferenceEquals(_sourceRoute, route)) adoption.Close(this, reason); });
        }
        catch (Exception error)
        {
            _transferFailure ??= "The typed fresh source failed to adopt: " + error.Message;
            _freshAdoption?.Close(this, _transferFailure);
            _freshAdoption?.FinishConstruction();
            throw;
        }
    }
    internal void VerifyFreshReservation(NativePlaybackFreshSourceCapture capture, NativeRuntime api,
        GameEventState.PlaybackRunAssociation run, PlaybackSourceGroupLease group)
    {
        VerifyRegisteredOwner();
        if (_thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_freshCapture, capture) || _freshAdoption is not null
            || !ReferenceEquals(_api, api) || !ReferenceEquals(_run, run) || !ReferenceEquals(_group, group))
            throw new GameplayStateException(ErrorCode.TargetExpired, "Only this constructing source-only route may reserve fresh adoption.");
    }
    internal void RegisterFreshAdoption(NativePlaybackFreshSourceCapture capture, NativePlaybackFreshSourceCapture.Adoption adoption)
    {
        VerifyRegisteredOwner();
        if (_thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_freshCapture, capture) || _freshAdoption is not null)
            throw new GameplayStateException(ErrorCode.TargetExpired, "Only the constructing typed fresh route can register its adoption.");
        _freshAdoption = adoption;
    }
    internal void StopFreshAdoption(NativePlaybackFreshSourceCapture.Adoption adoption, string reason)
    {
        VerifyRegisteredOwner();
        if (_thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_freshAdoption, adoption) && !_retiredFreshAdoptions.Contains(adoption))
            throw new GameplayStateException(ErrorCode.TargetExpired, "Another adoption cannot stop this route.");
        if (_retiredFreshAdoptions.Contains(adoption)) return;
        if (!_freshHostEnded && _freshInitializer is null && _session is not { SupportsObservedSuccessors: true })
            _transferFailure ??= reason;
        _session?.Withdraw(reason);
    }
    private bool FreshBeforeClosed(NativePlaybackSourceObservation observation)
    {
        if (_freshAdoption is not { } adoption) return false;
        if (_freshInitializer is not null) return false;
        if (observation.Role == NativePlaybackSourceRole.ProgressStartMoveNext && !_freshHostEnded)
        {
            _group!.VerifyIssuedOperationBefore(observation);
            if (_session is not { SupportsObservedSuccessors: true } || !adoption.CanRetainInitialization)
            {
                try { adoption.Close(this, "This actual Start has no retained armed predecessor graph for managed same-run initialization."); }
                finally { observation.DisposeOperation(); }
                return true;
            }
            return false;
        }
        if (_freshHostEnded) { observation.DisposeOperation(); return true; }
        try
        {
            adoption.VerifyOperationIdentity(this, _source!, _api, _run, _group!, _binding, _origin);
            return false;
        }
        catch (Exception error)
        {
            adoption.Close(this, "The fresh source identity ended before this attempted source operation: " + error.Message);
            observation.DisposeOperation(); return true;
        }
    }
    internal NativePlaybackStockRootedSource.ReadbackReceipt FreshReadback()
    {
        Open();
        if (_freshAdoption is not { } adoption) throw new GameplayStateException(ErrorCode.NotReady, "This route has no typed fresh adoption.");
        adoption.VerifyRouter(this); return Current();
    }
}
