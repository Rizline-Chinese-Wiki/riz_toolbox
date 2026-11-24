namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    private static readonly object SingleCaptureIssuer = new();
    internal sealed class SingleCaptureAuthority
    {
        private readonly object _issuer;
        private readonly OperationInvocation _call;
        private readonly Sink _sink;
        internal NativeTweenSingleParentKey Key { get; }
        internal SingleCaptureAuthority(object issuer, OperationInvocation call, Sink sink, NativeTweenSingleParentKey key)
        {
            if (!ReferenceEquals(issuer, SingleCaptureIssuer)) throw new InvalidOperationException("Single capture requires its private actual broker issuer.");
            (_issuer, _call, _sink, Key) = (issuer, call, sink, key);
        }
        internal void VerifyCurrent()
        {
            // The actual broker already selected this Capture observer. Withdrawal may
            // happen reentrantly after Begin; it cannot revoke that borrowed parent's
            // exit duty. Subscription.Begin separately rejects every new closed loan.
            if (!ReferenceEquals(_issuer, SingleCaptureIssuer) || !ReferenceEquals(_operationTop, _call)
                || !ReferenceEquals(_call.CapturingObserver, _sink) || _call.Thread != Environment.CurrentManagedThreadId
                || _call.Overflow != _operationOverflow
                || !_call.Entry.OperationInvocations.TryGetValue(checked((nuint)Key.ManagedToken), out var actual) || !ReferenceEquals(actual, _call))
                throw new InvalidOperationException("Single subscriber snapshot is outside its actual operation Capture window.");
        }
    }
    internal sealed partial class Binding
    {
        internal SingleCaptureAuthority CaptureSingleSubscriberParent(NativeOperationFrame frame, ulong singleEpoch)
        {
            var call = _operationTop;
            if (call is null || !ReferenceEquals(call.Entry, _entry) || !ReferenceEquals(call.CapturingObserver, _sink)
                || !_sink.Active || !SameOperationFrame(call.Frame, frame) || frame.Kind != NativeOperationKind.Single1
                || _entry.OperationBridge is not NativeBootstrappedOperationBridge native
                || native.Identity.Kind != NativeOperationKind.Single1 || native.Identity.Slot != 31)
                throw new InvalidOperationException("Single subscriber enrollment requires its exact actual pre-original Capture observer.");
            var token = _entry.OperationInvocations.Single(pair => ReferenceEquals(pair.Value, call)).Key;
            var key = new NativeTweenSingleParentKey(singleEpoch, native.Identity.Epoch, frame.Sequence, unchecked((ulong)token),
                frame.Self, frame.Method, frame.Caller, frame.ScalarBits, SingleCaptureThreadId());
            var result = new SingleCaptureAuthority(SingleCaptureIssuer, call, _sink, key); result.VerifyCurrent(); return result;
        }
        // Reads only the actual direct Single broker parent. A null/foreign/nested physical parent
        // masks owned ancestors; no native object is inspected before this association succeeds.
        internal bool WithCurrentSingleContext(NativeTweenSingleEventBridge.Delivery delivery, Action<IDisposable> observe)
        {
            ArgumentNullException.ThrowIfNull(delivery); ArgumentNullException.ThrowIfNull(observe); delivery.VerifyCurrent();
            var context = CurrentSingleContext(delivery.Observation.Frame);
            if (context is null) return false;
            observe(context); delivery.VerifyCurrent(); return true;
        }
        // Scalar association core only. It stays private so object readers must enter
        // through the live Delivery guard above, even for a byte-identical copied frame.
        private IDisposable? CurrentSingleContext(NativeTweenSingleEventFrame frame)
        {
            var call = _operationTop;
            if (frame.ParentManagedToken == 0 || call is null || !ReferenceEquals(call.Entry, _entry)
                || call.Thread != Environment.CurrentManagedThreadId || call.Overflow != _operationOverflow
                || call.CapturingObserver is not null || _entry.NativeMain?.IsMainThread != true
                || !_entry.OperationInvocations.TryGetValue(checked((nuint)frame.ParentManagedToken), out var retained)
                || !ReferenceEquals(call, retained)) return null;
            if (call.Frame.Kind != NativeOperationKind.Single1 || call.Frame.Sequence != frame.ParentSequence
                || call.Frame.Self != frame.Event || call.Frame.Method != frame.ParentMethod || call.Frame.Caller != frame.ParentCaller
                || call.Frame.ScalarBits != frame.ValueBits || _entry.Target.Abi != ExactHookAbi.OperationSingle1Void
                || _entry.OperationBridge is not NativeBootstrappedOperationBridge native || native.Identity.Epoch != frame.ParentEpoch
                || native.Identity.Kind != NativeOperationKind.Single1 || native.Identity.Slot != 31)
                throw new UnsupportedSignatureException("The Single delivery crossed its exact managed token/native sequence/value parent.");
            var context = call.Contexts.SingleOrDefault(item => ReferenceEquals(item.Sink, _sink));
            return context.Context;
        }
        internal void SingleEventFault(string reason)
        {
            Interlocked.Exchange(ref _entry.OperationUnknown, 1);
            var call = _operationTop;
            var frame = call is not null && ReferenceEquals(call.Entry, _entry) ? call.Frame : default;
            try { _sink.OperationFault?.Invoke(frame, reason); } catch { }
        }
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    private static extern uint SingleCaptureThreadId();
}
