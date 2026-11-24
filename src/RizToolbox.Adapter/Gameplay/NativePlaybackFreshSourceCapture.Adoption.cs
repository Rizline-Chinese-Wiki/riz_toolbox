using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackFreshSourceCapture
{
    private readonly PlaybackFreshReadScopes _reads = new();
    private Adoption? _adoption;
    private bool _drainingRoots;

    private IDisposable EnterRead()
    {
        Registered();
        return new ReadScope(this, _reads.Enter());
    }
    private sealed class ReadScope : IDisposable
    {
        private readonly NativePlaybackFreshSourceCapture _owner;
        private readonly IDisposable _scope;
        private readonly ReadScope _registered;
        private bool _released;
        internal ReadScope(NativePlaybackFreshSourceCapture owner, IDisposable scope)
            => (_owner, _scope, _registered) = (owner, scope, this);
        public void Dispose()
        {
            _owner.Registered();
            if (!ReferenceEquals(_registered, this)) throw new GameplayStateException(ErrorCode.TargetExpired, "Copied fresh read responsibility.");
            if (_released) return;
            _scope.Dispose(); _released = true;
            if (_owner._closed) _owner.TryDrainClosedRoots();
        }
    }
    internal (Adoption Adoption, NativePlaybackBinding Binding, NativePlaybackRateOrigin Origin) ReserveAdoption(
        NativePlaybackStockClockRouter router, NativeRuntime api,
        GameEventState.PlaybackRunAssociation run, PlaybackSourceGroupLease group)
    {
        Open(); ArgumentNullException.ThrowIfNull(router);
        if (!ReferenceEquals(api, _api) || !ReferenceEquals(run, _run) || !ReferenceEquals(group, _group)
            || !_claimed || _adoption is not null)
            throw new GameplayStateException(ErrorCode.TargetExpired, "Fresh adoption requires its unique exact API, run and physical group.");
        router.VerifyFreshReservation(this, api, run, group);
        // Register before the first reentrant getter. Even construction failure
        // retains this candidate; no second route may borrow its roots.
        var adoption = new Adoption(this, router); _adoption = adoption;
        router.RegisterFreshAdoption(this, adoption);
        try
        {
            VerifyInstallationCurrent();
            // These immutable projections have no intervening native consumer.
            // Returning them grants no admission beyond this actual adoption;
            // construction and every accessor retain their own fresh checks.
            return (adoption, _binding!, _origin!);
        }
        catch { adoption.FinishConstruction(); throw; }
    }

    internal sealed partial class Adoption
    {
        private readonly NativePlaybackFreshSourceCapture _capture;
        private readonly NativePlaybackStockClockRouter _router;
        private readonly Adoption _registered;
        private NativePlaybackStockRootedSource? _source;
        private NativePlaybackStockOccurrenceProducer? _producer;
        private NativePlaybackActualSource? _accessor;
        private bool _constructing = true, _accessorReleased, _stopFailed;
        internal Adoption(NativePlaybackFreshSourceCapture capture, NativePlaybackStockClockRouter router)
            => (_capture, _router, _registered) = (capture, router, this);
        private void Registered()
        {
            _capture.Registered();
            if (!ReferenceEquals(_registered, this) || !ReferenceEquals(_capture._adoption, this))
                throw new GameplayStateException(ErrorCode.TargetExpired, "A copied or detached fresh adoption owns no roots.");
        }
        internal void VerifyRouter(NativePlaybackStockClockRouter router)
        {
            Registered();
            if (!ReferenceEquals(_router, router)) throw new GameplayStateException(ErrorCode.TargetExpired, "Fresh adoption belongs to another route.");
            _capture.VerifyCurrent();
        }
        // Nested wrappers may select the exact registered identity without a
        // new native graph read. This grants no source, sample, writer or Clock
        // authority: their actual operation still checks tickets and fences.
        internal void VerifyOperationIdentity(NativePlaybackStockClockRouter router,
            NativePlaybackStockRootedSource source, NativeRuntime api, GameEventState.PlaybackRunAssociation run,
            PlaybackSourceGroupLease group, NativePlaybackBinding binding, NativePlaybackRateOrigin origin)
        {
            Registered(); _capture.Open();
            if (!ReferenceEquals(_router, router) || !ReferenceEquals(_source, source)
                || !ReferenceEquals(_capture._api, api) || !ReferenceEquals(_capture._run, run)
                || !ReferenceEquals(_capture._group, group) || !ReferenceEquals(_capture._binding, binding)
                || !ReferenceEquals(_capture._origin, origin) || !_capture._claimed || _accessorReleased)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The running wrapper lost its exact fresh adoption, source and binding registration.");
            run.VerifyCurrent(binding); _capture.Open();
        }
        internal NativePlaybackBinding Binding(NativePlaybackStockClockRouter router)
        { VerifyRouter(router); return _capture._binding!; }
        internal NativePlaybackRateOrigin Origin(NativePlaybackStockClockRouter router)
        { VerifyRouter(router); return _capture._origin!; }
        internal void BindSource(NativePlaybackStockRootedSource source, NativeRuntime api,
            GameEventState.PlaybackRunAssociation run, PlaybackSourceGroupLease group)
        {
            Registered();
            if (!_constructing || _source is not null || source is null || !ReferenceEquals(api, _capture._api)
                || !ReferenceEquals(run, _capture._run) || !ReferenceEquals(group, _capture._group))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Fresh adoption permits one constructed rooted consumer.");
            _source = source;
        }
        internal void VerifySourceRegistration(NativePlaybackStockRootedSource source)
        {
            Registered();
            if (!ReferenceEquals(_source, source)) throw new GameplayStateException(ErrorCode.TargetExpired, "The rooted consumer is not this adoption's registered source.");
        }
        internal void VerifySource(NativePlaybackStockRootedSource source)
        { VerifySourceRegistration(source); _capture.VerifyCurrent(); }
        internal (NativePlaybackBinding Binding, NativePlaybackRateOrigin Origin) ReadConstructionIdentity(
            NativePlaybackStockRootedSource source, NativeRuntime api)
        {
            VerifySourceRegistration(source);
            if (!_constructing || !ReferenceEquals(api, _capture._api))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only the exact constructing source can read its fresh identity projection.");
            // These two immutable values have no intervening native consumer.
            // This local projection is data only: BorrowRoots and every actual
            // accessor/native operation still verify the retained adoption.
            _capture.VerifyInstallationCurrent();
            if (!_constructing)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The source construction ended while reading its identity projection.");
            return (_capture._binding!, _capture._origin!);
        }
        internal void RegisterProducer(NativePlaybackStockRootedSource source, NativePlaybackStockOccurrenceProducer producer)
        {
            VerifySourceRegistration(source);
            if (!_constructing || _producer is not null || producer is null)
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only the actual first consumer can register its producer.");
            source.VerifyFreshProducer(this, producer);
            _producer = producer;
        }
        internal NativeRuntime.RootHandle[] BorrowRoots(NativePlaybackStockRootedSource source, NativePlaybackActualSource accessor)
        {
            VerifySourceRegistration(source);
            if (!_constructing || _producer is null || _accessor is not null || accessor is null)
                throw new GameplayStateException(ErrorCode.TargetExpired, "Borrowing requires the registered construction and actual producer.");
            accessor.VerifyFreshBorrowCandidate(this, source, _capture._api);
            _accessor = accessor;
            _capture.VerifyInstallationCurrent();
            return _capture._roots.ToArray(); // The same seven handles; no new retains.
        }
        internal void VerifyAccessorRegistration(NativePlaybackActualSource accessor)
        {
            Registered();
            if (!ReferenceEquals(_accessor, accessor)) throw new GameplayStateException(ErrorCode.TargetExpired, "Copied or foreign accessor has no borrowed graph.");
        }
        // This scope holds only read responsibility, so reentrant closure cannot
        // free the seven roots before the whole sample unwinds. It grants no
        // source, native-operation or Clock authority and caches no validation.
        internal IDisposable PinAccessorLifetime(NativePlaybackActualSource accessor)
        {
            VerifyAccessorRegistration(accessor);
            if (_accessorReleased) throw new GameplayStateException(ErrorCode.TargetExpired, "The borrowed accessor has ended.");
            return _capture.EnterRead();
        }
        internal IDisposable EnterAccessor(NativePlaybackActualSource accessor)
        {
            var read = PinAccessorLifetime(accessor);
            try { _capture.VerifyCurrent(); return new AccessorOperation(this, accessor, read); }
            catch { read.Dispose(); throw; }
        }
        private sealed class AccessorOperation : IDisposable
        {
            private readonly Adoption _owner;
            private readonly NativePlaybackActualSource _accessor;
            private readonly IDisposable _read;
            private readonly AccessorOperation _registered;
            private bool _closed;
            internal AccessorOperation(Adoption owner, NativePlaybackActualSource accessor, IDisposable read)
                => (_owner, _accessor, _read, _registered) = (owner, accessor, read, this);
            public void Dispose()
            {
                // Reject a foreign exit before releasing the real root read
                // responsibility. Reentrant closure keeps roots until unwind.
                _owner.Registered();
                if (!ReferenceEquals(_registered, this))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "Copied fresh accessor operation owns no read responsibility.");
                if (_closed) return;
                try { _owner.VerifyAccessorOpen(_accessor); _owner._capture.VerifyCurrent(); }
                finally { _closed = true; _read.Dispose(); }
            }
        }
        internal void VerifyAccessorOpen(NativePlaybackActualSource accessor)
        {
            VerifyAccessorRegistration(accessor); _capture.Open();
            if (_accessorReleased) throw new GameplayStateException(ErrorCode.TargetExpired, "The borrowed accessor has ended.");
        }
        internal void VerifyAccessorProjection(NativePlaybackActualSource accessor, NativeRuntime api,
            NativePlaybackBinding binding, long pause, IReadOnlyList<NativeRuntime.RootHandle> roots)
        {
            VerifyAccessorOpen(accessor);
            if (!ReferenceEquals(api, _capture._api) || !ReferenceEquals(binding, _capture._binding)
                || pause != _capture._origin!.Pause || roots.Count != 7 || _capture._roots.Count != 7
                || roots.Where((root, index) => !ReferenceEquals(root, _capture._roots[index])).Any())
                throw new GameplayStateException(ErrorCode.TargetExpired, "The accessor does not project its adoption's exact binding and seven borrowed handles.");
        }
        internal void ReleaseAccessor(NativePlaybackActualSource accessor)
        { VerifyAccessorRegistration(accessor); _accessorReleased = true; }
        internal bool RootsReleased { get { Registered(); return _capture.RootsReleased; } }
        internal void FinishConstruction()
        {
            Registered(); _constructing = false;
            if (_capture._closed) _capture.TryDrainClosedRoots();
        }
        internal void Close(NativePlaybackStockClockRouter router, string reason)
        {
            Registered();
            if (!ReferenceEquals(_router, router)) throw new GameplayStateException(ErrorCode.TargetExpired, "Foreign route cannot close fresh adoption.");
            _capture.Invalidate(reason);
        }
        internal void Close(NativePlaybackStockRootedSource source, string reason)
        { VerifySourceRegistration(source); _capture.Invalidate(reason); }
        internal void StopNewUse(string reason)
        {
            Registered();
            try { _router.StopFreshAdoption(this, reason); _source?.StopFreshAdoption(this, reason); }
            catch { _stopFailed = true; throw; }
        }
        internal bool PrepareDrain()
        {
            Registered();
            if (_constructing || _stopFailed || _capture._reads.Active != 0 || _clock is { IsClosed: false }) return false;
            // These are the actual registered producer journals, not a boolean
            // or caller-provided drain callback. Disposal cannot erase duties.
            if (_producer is not null && !_producer.OwnResponsibilitiesDrained) return false;
            _accessor?.Dispose();
            return true;
        }
        internal void Pump(NativePlaybackStockClockRouter router)
        {
            Registered();
            if (!ReferenceEquals(_router, router)) throw new GameplayStateException(ErrorCode.TargetExpired, "Foreign route cannot pump fresh roots.");
            if (_capture._closed) _capture.TryDrainClosedRoots();
        }
    }
}
