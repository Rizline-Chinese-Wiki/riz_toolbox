using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackFreshSourceCapture
{
    internal sealed partial class Adoption
    {
        private NativePlaybackSingleReturnBridge.FreshClockReceipt? _clock;

        internal NativePlaybackSingleReturnBridge.FreshClockReceipt PrepareClock(
            NativePlaybackStockClockRouter.Session session, NativePlaybackStockRootedSource.ReadbackReceipt baseline,
            NativePlaybackGetTimeHookLease hook, NativePlaybackSingleReturnAuthority initial)
        {
            Registered();
            _router.VerifyFreshClockCandidate(session, this, baseline, hook);
            if (_clock is not null)
                throw new GameplayStateException(ErrorCode.Conflict, "This exact adopted source already registered its Clock ticket.");
            using var read = _capture.EnterRead();
            VerifyRouter(_router);
            var execution = baseline.FreshClockExecution(this, _source!);
            // This is the original claimed native source ticket. No projection,
            // second claim, new roots, or new watch can authorize publication.
            _clock = hook.PrepareFreshClock(_capture._ticket!, execution, initial);
            try { _capture.Open(); return _clock; }
            catch { _clock.Cancel(); throw; }
        }

        internal void VerifyClock(NativePlaybackStockClockRouter router,
            NativePlaybackSingleReturnBridge.FreshClockReceipt clock)
        {
            Registered(); _capture.Open();
            if (!ReferenceEquals(_clock, clock))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The Clock ticket is not owned by this original fresh adoption.");
            VerifyOperationIdentity(router, _source!, _capture._api, _capture._run,
                _capture._group, _capture._binding!, _capture._origin!);
            // Keep the actual claimed Clock ticket and complete native policy
            // check. This nested owner check reads no second seven-root graph.
            clock.Read(2);
            VerifyOperationIdentity(router, _source!, _capture._api, _capture._run,
                _capture._group, _capture._binding!, _capture._origin!);
        }

        internal IDisposable RetainRateSource(NativePlaybackStockClockRouter.Session session)
        {
            Registered();
            var scope = _capture.EnterRead();
            try { VerifyRateSource(session); return scope; }
            catch { scope.Dispose(); throw; }
        }

        internal void VerifyRateSource(NativePlaybackStockClockRouter.Session session)
        {
            Registered();
            _router.VerifyFreshRateSession(session, this);
            VerifyOperationIdentity(_router, _source!, _capture._api, _capture._run,
                _capture._group, _capture._binding!, _capture._origin!);
            session.VerifyFreshWriterAuthority(this, _source!);
        }

        internal NativePlaybackStockOccurrenceProducer.ContinuationReceipt? RestorePitch(
            NativePlaybackStockRootedSource source, NativePlaybackStockOccurrenceProducer producer,
            NativePlaybackStockOccurrenceProducer.ExecutionReceipt execution, float pitch)
        {
            VerifySourceRegistration(source); source.VerifyFreshProducer(this, producer);
            using var scope = _capture.EnterRead();
            var session = _router.FreshWriterSession(this, source);
            if (session is null) return null;
            session.VerifyFreshWriterAuthority(this, source);
            var result = producer.WritePitch(execution, pitch);
            session.VerifyFreshWriterAuthority(this, source);
            return result;
        }

        internal NativePlaybackStockOccurrenceProducer.ContinuationReceipt WritePitch(
            NativePlaybackStockRootedSource source, NativePlaybackStockOccurrenceProducer producer,
            NativePlaybackStockOccurrenceProducer.ExecutionReceipt execution,
            NativePlaybackStockOccurrenceProducer.ContinuationReceipt? predecessor, float pitch)
        {
            VerifySourceRegistration(source); source.VerifyFreshProducer(this, producer);
            using var scope = _capture.EnterRead();
            var session = _router.FreshWriterSession(this, source)
                ?? throw new GameplayStateException(ErrorCode.NotReady, "A fresh source cannot write before its exact Clock is armed.");
            session.VerifyFreshWriterAuthority(this, source);
            if (predecessor is not null) _ = predecessor.ReservationFor(producer);
            var result = predecessor is null ? producer.WritePitch(execution, pitch) : predecessor.WritePitch(pitch);
            session.VerifyFreshWriterAuthority(this, source);
            return result;
        }
    }
}
