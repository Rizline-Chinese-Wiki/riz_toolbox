namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal enum NativeVoidPhysicalStage { Capturing, OriginalWindow, Exiting, Exited }
    [ThreadStatic] private static NativeVoidInvocation? _nativeVoidPhysicalTop;
    [ThreadStatic] private static int _nativeVoidOverflowDepth;
    [ThreadStatic] private static bool _nativeVoidPhysicalCorrupt;

    /// <summary>Read-only evidence minted only after matching the current physical
    /// top, exact entry and this sink's own retained scope. Never an SDK capability.</summary>
    internal sealed class NativeVoidPhysicalIdentity
    {
        private readonly NativeVoidInvocation _invocation;
        private readonly Sink _sink;
        private readonly IDisposable _context;
        internal VerifiedHookTarget Entry { get; }
        internal nuint Token { get; }
        internal int ThreadId { get; }
        internal NativeVoidFrame Frame { get; }
        internal IntPtr ConfiguredMidReturnPc => NativeVoidAdoptedIdentities.TryGetValue(_invocation.Entry, out var identity)
            ? identity.MidReturnPc : IntPtr.Zero;
        internal IntPtr ConfiguredListenerReturnPc(uint site) => _invocation.Entry.NativeListenerIdentity is { } identity
            ? identity.Source(site) : IntPtr.Zero;
        private NativeVoidPhysicalIdentity(NativeVoidInvocation invocation, Sink sink, IDisposable context)
        {
            (_invocation, _sink, _context) = (invocation, sink, context);
            (Entry, Token, ThreadId, Frame) = (invocation.Entry.Target, invocation.Token, invocation.ThreadId, invocation.Frame);
        }
        internal bool IsCurrentFor(IDisposable context)
        {
            if (!ReferenceEquals(_context, context) || !MatchesCurrent(_invocation.Entry, _sink, Frame.Self, out var current)
                || !ReferenceEquals(_invocation, current)) return false;
            return current!.Contexts.Any(item => ReferenceEquals(item.Sink, _sink) && ReferenceEquals(item.Context, context));
        }
        internal static bool TryGet<T>(Entry entry, Sink sink, IntPtr self, out T? context,
            out NativeVoidPhysicalIdentity? identity) where T : class, IDisposable
        {
            context = null; identity = null;
            if (!MatchesCurrent(entry, sink, self, out var current)) return false;
            foreach (var item in current!.Contexts)
                if (ReferenceEquals(item.Sink, sink) && item.Context is T typed)
                { context = typed; identity = new(current, sink, typed); return true; }
            return false;
        }
        private static bool MatchesCurrent(ExactHookBroker.Entry entry, Sink sink, IntPtr self, out NativeVoidInvocation? current)
        {
            current = _nativeVoidPhysicalTop;
            if (_nativeVoidPhysicalCorrupt || _nativeVoidOverflowDepth != 0 || self == IntPtr.Zero || !sink.Active
                || entry.Target.Abi != ExactHookAbi.NativeVoid0 || entry.NativeInstallFailure is not null
                || entry.NativeVoidBridge is null || entry.NativeMain?.IsMainThread != true
                || current is null || !ReferenceEquals(current.Entry, entry) || current.Stage != NativeVoidPhysicalStage.OriginalWindow
                || current.ThreadId != Environment.CurrentManagedThreadId || current.Frame.Self != self || !current.Frame.IsValid
                || current.Frame.Method != IntPtr.Zero && current.Frame.Method != entry.Target.MethodInfo
                || !entry.NativeVoidInvocations.TryGetValue(current.Token, out var retained) || !ReferenceEquals(current, retained))
                return false;
            var activity = ReadNativeVoidActivity(entry, entry.NativeVoidBridge);
            return activity.Known && activity.InFlight > 0;
        }
    }

    internal sealed partial class Binding
    {
        /// <summary>The caller must first validate its sealed native mid frame.
        /// This never searches a parent receiver or invokes a native object API.</summary>
        internal bool TryGetCurrentNativeVoidContext<T>(IntPtr observedSelf, out T? context,
            out NativeVoidPhysicalIdentity? identity) where T : class, IDisposable
            => NativeVoidPhysicalIdentity.TryGet(_entry, _sink, observedSelf, out context, out identity);
    }

    private static void MaskUntrackedNativeVoid(Entry entry)
    {
        if (_nativeVoidOverflowDepth == int.MaxValue)
        { _nativeVoidPhysicalCorrupt = true; Interlocked.Exchange(ref entry.NativeVoidUnknown, 1); return; }
        ++_nativeVoidOverflowDepth;
    }
    private static void ExitUntrackedNativeVoid(Entry entry, NativeVoidFrame frame)
    {
        // A normal child entered inside an overflow window must exit before its
        // token-zero parent. Never uncover a parent top on a damaged exit order.
        if (_nativeVoidOverflowDepth <= 0 || _nativeVoidPhysicalTop is { } child
            && child.OverflowDepthAtEnter >= _nativeVoidOverflowDepth)
        {
            _nativeVoidPhysicalCorrupt = true; Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
            NativeVoidFaults(entry, Volatile.Read(ref entry.NativeVoidObservers), frame, "Untracked native void0 physical exit is out of order."); return;
        }
        --_nativeVoidOverflowDepth;
    }
    private static bool IsCurrentNativeVoidPhysical(Entry entry, NativeVoidInvocation invocation)
    {
        if (ReferenceEquals(_nativeVoidPhysicalTop, invocation) && invocation.OverflowDepthAtEnter == _nativeVoidOverflowDepth)
            return true;
        _nativeVoidPhysicalCorrupt = true; Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
        NativeVoidFaults(entry, invocation.Observers, invocation.Frame, "Native void0 physical exit is out of order; responsibility retained."); return false;
    }
    private static void FinishNativeVoidPhysical(Entry entry, NativeVoidInvocation invocation)
    {
        if (!IsCurrentNativeVoidPhysical(entry, invocation)) return;
        invocation.Stage = NativeVoidPhysicalStage.Exited;
        _nativeVoidPhysicalTop = invocation.ParentPhysical;
    }
}
