namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    private static readonly object OperationRootIssuer = new();

    /// <summary>A private witness for one rooted reference while the actual
    /// native Before observer is borrowing its input. It grants neither a late
    /// Root request nor an original-call, queue-exit or release receipt.</summary>
    internal sealed class OperationRootReceipt
    {
        private readonly object _issuer;
        private readonly OperationInvocation _call;
        private readonly Sink _observer;
        private readonly IntPtr _identity;
        internal NativeRuntime.RootHandle Root { get; }
        internal NativeOperationFrame Frame { get; }
        internal INativeTweenSourceOwner Owner { get; }
        internal bool Production { get; }

        internal OperationRootReceipt(object issuer, OperationInvocation call, Sink observer,
            NativeOperationFrame frame, NativeRuntime.RootHandle root, INativeTweenSourceOwner owner, bool production)
        {
            if (!ReferenceEquals(issuer, OperationRootIssuer))
                throw new InvalidOperationException("Foreign native operation root receipt issuer.");
            (_issuer, _call, _observer, Frame, Root, Owner, Production) = (issuer, call, observer, frame, root, owner, production);
            _identity = root.Target;
        }

        internal void VerifyCurrent()
        {
            VerifyRetained();
            if (!ReferenceEquals(_operationTop, _call) || !ReferenceEquals(_call.CapturingObserver, _observer)
                || _operationOverflow != _call.Overflow || !_observer.Active)
                throw new InvalidOperationException("The operation root receipt is outside its exact native Before observer.");
        }

        internal void VerifyRetained()
        {
            if (!ReferenceEquals(_issuer, OperationRootIssuer) || _call.Thread != Environment.CurrentManagedThreadId
                || !SameOperationFrame(Frame, _call.Frame) || !Frame.IsValid || Root.Target != _identity
                || BorrowedReference(Frame, _identity) != _identity)
                throw new UnsupportedSignatureException("The retained operation root lost its exact original input identity.");
        }
    }

    internal static OperationRootReceipt CaptureCurrentOperationRoot(NativeOperationFrame frame,
        NativeRuntime.RootHandle root, INativeTweenSourceOwner owner, bool production)
    {
        ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(owner);
        var call = _operationTop;
        if (call is null || call.CapturingObserver is not { } observer || !observer.Active
            || call.Thread != Environment.CurrentManagedThreadId || _operationOverflow != call.Overflow
            || !frame.IsValid || !SameOperationFrame(call.Frame, frame)
            || Volatile.Read(ref call.Entry.OperationUnknown) != 0)
            throw new InvalidOperationException("A source root can be promoted only in its exact current native Before observer.");
        _ = BorrowedReference(frame, root.Target);
        if (production)
        {
            var ready = NativeFirstInstall.RequireReady(); ready.VerifyReady();
            if (call.Entry.OperationBridge is not NativeBootstrappedOperationBridge bridge
                || ready.OperationIdentity(call.Entry.Target, frame.Kind) != bridge.Identity)
                throw new UnsupportedSignatureException("The source root operation is outside the actual FirstInstall publication.");
        }
        var receipt = new OperationRootReceipt(OperationRootIssuer, call, observer, frame, root, owner, production);
        receipt.VerifyCurrent(); return receipt;
    }

    /// <summary>Compares the direct physical broker parent, including foreign
    /// calls whose observers returned no managed context. An owned ancestor
    /// beyond such a call cannot stand in for the actual immediate parent.</summary>
    internal static void VerifyCurrentOperationParent(NativeOperationFrame frame, NativeOperationFrame? expectedParent)
    {
        var call = _operationTop;
        if (call is null || call.CapturingObserver is not { Active: true }
            || call.Thread != Environment.CurrentManagedThreadId || _operationOverflow != call.Overflow
            || !frame.IsValid || !SameOperationFrame(call.Frame, frame)
            || Volatile.Read(ref call.Entry.OperationUnknown) != 0)
            throw new InvalidOperationException("Native operation parent verification requires the exact current Before observer.");
        if (expectedParent is { } expected
            ? call.Parent is null || !SameOperationFrame(call.Parent.Frame, expected)
                || call.Parent.Thread != Environment.CurrentManagedThreadId || call.Parent.Overflow != _operationOverflow
                || Volatile.Read(ref call.Parent.Entry.OperationUnknown) != 0
            : call.Parent is not null)
            throw new UnsupportedSignatureException("The retained owned operation is not this Before input's direct actual native parent.");
    }

    internal static IntPtr BorrowedReference(NativeOperationFrame frame, IntPtr identity)
    {
        if (!frame.IsValid || identity == IntPtr.Zero
            || identity != frame.Self && !(frame.Kind is NativeOperationKind.Reference1 or NativeOperationKind.Reference2 && identity == frame.Arg0))
            throw new InvalidOperationException("The retained root is not a reference borrowed by this native input frame.");
        return identity;
    }
}
