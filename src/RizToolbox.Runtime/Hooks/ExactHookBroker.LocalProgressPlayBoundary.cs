namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    // The native owner retains and calls the original. This boundary attaches
    // only the LOCAL Timeline scope to its actual Before/finally interval.
    internal sealed class LocalProgressPlayBoundary
    {
        [ThreadStatic] private static Invocation? _current;
        private readonly LocalInitializationPolicy _policy;
        private int _faulted;

        internal LocalProgressPlayBoundary(LocalInitializationPolicy policy) => _policy = policy;
        internal bool IsFaulted => Volatile.Read(ref _faulted) != 0;

        private sealed record Invocation(LocalProgressPlayBoundary Owner, Invocation? Parent,
            IntPtr Receiver, byte RawFromBeginning, IntPtr Method, IntPtr Caller, ulong Sequence)
        {
            internal LocalInitializationFrame Frame = null!;
            internal bool CompletionUnknown;
        }

        internal nuint Enter(IntPtr receiver, byte rawFromBeginning, IntPtr method, IntPtr caller, ulong sequence)
        {
            // Allocate the retained pairing before publishing authorization.
            var invocation = new Invocation(this, _current, receiver, rawFromBeginning, method, caller, sequence);
            var valid = receiver != IntPtr.Zero && caller != IntPtr.Zero && sequence != 0;
            var frame = valid && !IsFaulted ? CaptureInitializationPlay(_policy, receiver, method, this) : null;

            // The original still runs if attribution fails. This ordinary
            // scope prevents it from borrowing older LOCAL authority.
            if (frame is null)
            {
                frame = new LocalInitializationFrame
                {
                    Policy = _policy, Parent = CurrentInitialization, Scope = InitializationScope.Play,
                    Receiver = receiver, Method = method, Sequence = NextLocalScopeSequence(), Unknown = true, SharedPlay = this
                };
                CurrentInitialization = frame;
                ++InitializationDepth;
                lock (_policy.Sync) ++_policy.UnknownInFlight;
            }
            invocation.Frame = frame;
            _current = invocation;
            if (!valid) Fault("The shared Play entry has an invalid actual native receiver, caller or sequence.");
            return checked((nuint)frame.Sequence);
        }

        internal void Exit(IntPtr receiver, byte rawFromBeginning, IntPtr method, IntPtr caller,
            ulong sequence, nuint token, bool returned)
        {
            var actual = _current;
            if (actual is null || !ReferenceEquals(actual.Owner, this) || actual.CompletionUnknown
                || !ReferenceEquals(CurrentInitialization, actual.Frame) || token != (nuint)actual.Frame.Sequence
                || receiver != actual.Receiver || rawFromBeginning != actual.RawFromBeginning
                || method != actual.Method || caller != actual.Caller || sequence != actual.Sequence)
            {
                if (actual is not null && ReferenceEquals(actual.Owner, this))
                {
                    actual.CompletionUnknown = true;
                    if (!actual.Frame.Unknown)
                    {
                        actual.Frame.Unknown = true;
                        lock (_policy.Sync) ++_policy.UnknownInFlight;
                    }
                }
                Fault("The actual shared Play completion lost its entered thread, frame or native original interval.");
                // Keep both the frame and the native pending record so that
                // retirement cannot mistake the failed pair for zero activity.
                throw new UnsupportedSignatureException("The shared Play completion does not pair with its actual entry.");
            }

            _current = actual.Parent;
            ExitInitializationFrame(actual.Frame);
            if (!returned && actual.Frame.Claimed)
                InitializationFault(_policy, null, "The claimed shared Play original exited abnormally.");
        }

        internal void Fault(string reason)
        {
            if (Interlocked.Exchange(ref _faulted, 1) == 0) InitializationFault(_policy, null, reason);
        }
    }
}
