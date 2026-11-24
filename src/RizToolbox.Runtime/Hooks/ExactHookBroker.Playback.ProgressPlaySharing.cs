using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal interface ILocalProgressPlayObservation : IDisposable
{
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
}

internal sealed partial class ExactHookBroker
{
    internal bool CanObserveOfficialProgressPlay(VerifiedHookTarget localPlay)
    {
        _main.VerifyAccess();
        lock (Gate) return FindOfficialProgressPlay(localPlay) is not null;
    }

    internal void PrepareOfficialProgressPlayObserver(VerifiedHookTarget localPlay)
    {
        _main.VerifyAccess();
        lock (Gate)
        {
            var owner = FindOfficialProgressPlay(localPlay)
                ?? throw new LocalProgressPlayConflictException("The retained official Play entry cannot host this LOCAL observation.");
            owner.PrepareLocalPlayObserver();
        }
    }

    internal ILocalProgressPlayObservation BindOfficialProgressPlayObserver(VerifiedHookTarget localPlay,
        Func<IntPtr, byte, IntPtr, IntPtr, ulong, nuint> enter,
        Action<IntPtr, byte, IntPtr, IntPtr, ulong, nuint, bool> exit,
        Action<string>? fault = null)
    {
        _main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(enter);
        ArgumentNullException.ThrowIfNull(exit);
        lock (Gate)
        {
            var owner = FindOfficialProgressPlay(localPlay)
                ?? throw new LocalProgressPlayConflictException("The retained official Play entry cannot host this LOCAL observation.");
            return owner.BindLocalPlayObserver(enter, exit, fault);
        }
    }

    private static PlaybackSourceOwner? FindOfficialProgressPlay(VerifiedHookTarget localPlay, bool allowDormant = false)
    {
        if (localPlay.Abi != ExactHookAbi.LocalProgressPlayBooleanVoid
            || !Entries.TryGetValue(localPlay.Address, out var entry) || !entry.RetainedStockProgressPlay
            || !PlaybackSourceInstallations.TryGetValue(localPlay.Address, out var owner)
            || !owner.CanObserveLocalPlay(localPlay, allowDormant))
            return null;
        return owner;
    }

    internal sealed partial class PlaybackSourceOwner
    {
        internal bool CanObserveLocalPlay(VerifiedHookTarget localPlay, bool allowDormant)
        {
            if (!Monitor.IsEntered(Gate) || Role != NativePlaybackSourceRole.ProgressPlay
                || Kind != NativePlaybackSourceKind.Boolean1 || !IsCommitted
                || !(_group.IsActive || allowDormant && _group.HasDormantPlay)
                || localPlay.Address != _target.Address || localPlay.MethodInfo != _target.MethodInfo
                || localPlay.DeclaringClass != _target.DeclaringClass || localPlay.ReturnClass != IntPtr.Zero
                || !_hub.SupportsSharedProgressPlay)
                return false;
            VerifyOperationChannel(_group, _hub, requireArmed: _group.IsActive);
            VerifySharedPlayOriginal();
            return _group.IsActive || allowDormant && _group.HasDormantPlay;
        }

        internal ILocalProgressPlayObservation BindLocalPlayObserver(
            Func<IntPtr, byte, IntPtr, IntPtr, ulong, nuint> enter,
            Action<IntPtr, byte, IntPtr, IntPtr, ulong, nuint, bool> exit, Action<string>? fault)
            => _hub.BindSharedProgressPlay(ConfiguredChannel, VerifySharedPlayOriginal, enter, exit, fault,
                dormant: !_group.IsActive);

        internal void PrepareLocalPlayObserver(bool dormant = false)
            => _hub.PrepareSharedProgressPlay(ConfiguredChannel, VerifySharedPlayOriginal, dormant);

        internal void VerifyDormantPlayBody(IntPtr module, VerifiedHookTarget play)
        {
            if (MemberModule != module || play.Address != TargetAddress || play.MethodInfo != _target.MethodInfo
                || play.DeclaringClass != _target.DeclaringClass || play.ReturnClass != IntPtr.Zero
                || play.Abi != ExactHookAbi.LocalProgressPlayBooleanVoid)
                throw new UnsupportedSignatureException("The exact LOCAL and stock Play declarations differ.");
            VerifySharedPlayOriginal();
        }

        private void VerifySharedPlayOriginal()
        {
            _group.VerifySealIssuer(MemberModule);
            var channel = _bridge;
            if (!OwnsPlaybackSourceInstallation(this) || !IsCommitted && !_group.PublishingDormantPlay
                || Role != NativePlaybackSourceRole.ProgressPlay
                || channel is not { IsConfigured: true } || _proof is not { } proof
                || channel.Role != Role || channel.Kind != Kind || channel.TargetAddress != TargetAddress
                || channel.OriginalAddress != proof.OriginalAddress)
                throw new UnsupportedSignatureException("The shared Play original lost its exact physical owner.");
            _dobby.Verify();
            VerifyLocalPhysicalEvidence(null);
            proof.Verify();
            _relay?.Verify();
        }
    }
}

internal sealed partial class NativePlaybackStockSourceHub
{
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct ProgressPlayObserverWire
    {
        internal uint Version, Size, Slot, Reserved;
        internal ulong OwnerEpoch;
        internal IntPtr Target, Original, Entry, Enter, Exit, Context;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ProgressPlayObserverNative(IntPtr owner, ref ProgressPlayObserverWire observer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nuint EnterProgressPlayNative(IntPtr context, IntPtr receiver, uint raw, IntPtr method,
        IntPtr caller, ulong sequence);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ExitProgressPlayNative(IntPtr context, IntPtr receiver, uint raw, IntPtr method,
        IntPtr caller, ulong sequence, nuint token, uint returned);

    private const string BindProgressPlayExport = "RtPlaybackSourceBindProgressPlayObserverV1";
    private const string VerifyProgressPlayExport = "RtPlaybackSourceVerifyProgressPlayObserverV1";
    private const string BindDormantPlayExport = "RtPlaybackSourceBindDormantProgressPlayObserverV1";
    private const string BeginPlayPromotionExport = "RtPlaybackSourceBeginDormantPlayPromotionV1";
    private const string EndPlayPromotionExport = "RtPlaybackSourceEndDormantPlayPromotionV1";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int BeginPlayPromotionNative(IntPtr owner, ulong epoch);
    internal bool SupportsDormantPlay => SupportsSharedProgressPlay
        && NativeLibrary.TryGetExport(_library, BindDormantPlayExport, out _)
        && NativeLibrary.TryGetExport(_library, BeginPlayPromotionExport, out _)
        && NativeLibrary.TryGetExport(_library, EndPlayPromotionExport, out _);

    internal void VerifyDormantPlayHelper(string path, string hash)
    {
        VerifyThread();
        if (!_path.Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) || !_image.FileSha256.Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("The stock group requested a different source helper from its retained LOCAL Play.");
        _image.Verify();
    }

    internal bool TryBeginDormantPlayPromotion()
    {
        VerifyThread(); _image.Verify();
        return Export<BeginPlayPromotionNative>(BeginPlayPromotionExport)(_owner, Epoch) switch
        {
            1 => true,
            2 => false,
            _ => throw new UnsupportedSignatureException("The retained LOCAL Play promotion lost its native owner or proof.")
        };
    }

    internal void EndDormantPlayPromotion(bool completed)
    {
        VerifyThread();
        // Release blocked borrowers even when a post-write audit has failed.
        if (Export<ArmedNative>(EndPlayPromotionExport)(_owner, Epoch, completed ? 1 : 0) != 1)
            throw new UnsupportedSignatureException("The retained LOCAL Play promotion ended with uncertain native evidence.");
    }
    private SharedProgressPlayObserver? _sharedProgressPlay;
    internal bool SupportsSharedProgressPlay
    {
        get
        {
            if (!NativeLibrary.TryGetExport(_library, BindProgressPlayExport, out _)
                || !NativeLibrary.TryGetExport(_library, VerifyProgressPlayExport, out _)) return false;
            _image.Verify();
            return true;
        }
    }

    internal ILocalProgressPlayObservation BindSharedProgressPlay(NativePlaybackStockSourceBridge channel,
        Action verifyOriginal,
        Func<IntPtr, byte, IntPtr, IntPtr, ulong, nuint> enter,
        Action<IntPtr, byte, IntPtr, IntPtr, ulong, nuint, bool> exit, Action<string>? fault, bool dormant)
    {
        VerifyThread();
        lock (_gate)
            return RequireSharedProgressPlay(channel, verifyOriginal, dormant).Attach(channel, enter, exit, fault);
    }

    internal void PrepareSharedProgressPlay(NativePlaybackStockSourceBridge channel, Action verifyOriginal, bool dormant = false)
    {
        VerifyThread();
        lock (_gate) RequireSharedProgressPlay(channel, verifyOriginal, dormant).Prepare(channel);
    }

    private SharedProgressPlayObserver RequireSharedProgressPlay(NativePlaybackStockSourceBridge channel, Action verifyOriginal, bool dormant = false)
    {
        if (channel.Role != NativePlaybackSourceRole.ProgressPlay || channel.Kind != NativePlaybackSourceKind.Boolean1
            || !SupportsSharedProgressPlay || dormant && !SupportsDormantPlay)
            throw new UnsupportedSignatureException("Shared Play requires its exact native Boolean1 member and observer exports.");
        VerifyOperationMemberEvidence(channel, requireArmed: !dormant);
        // Retain the callbacks before publishing their pointers. Closing a
        // subscriber does not release the physical entry or its callbacks.
        return _sharedProgressPlay ??= new(this, channel, verifyOriginal, dormant);
    }

    internal void VerifyDormantPlayPublication()
    {
        if (_sharedProgressPlay is not { } observer)
            throw new UnsupportedSignatureException("The dormant native Play has no retained observer.");
        observer.Verify();
    }

    private sealed class SharedProgressPlayObserver
    {
        [ThreadStatic] private static Invocation? _current;
        private readonly NativePlaybackStockSourceHub _hub;
        private readonly NativePlaybackStockSourceBridge _channel;
        private readonly EnterProgressPlayNative _enter;
        private readonly ExitProgressPlayNative _exit;
        private readonly ProgressPlayObserverNative _bind, _verify;
        private readonly ProgressPlayObserverWire _wire;
        private readonly Action _verifyOriginal;
        private readonly object _gate = new();
        private readonly Dictionary<ulong, Invocation> _pending = new();
        private Binding? _active;
        private bool _publicationAttempted;

        internal SharedProgressPlayObserver(NativePlaybackStockSourceHub hub, NativePlaybackStockSourceBridge channel, Action verifyOriginal, bool dormant)
        {
            _hub = hub; _channel = channel; _verifyOriginal = verifyOriginal;
            hub._image.Verify();
            if (Marshal.SizeOf<ProgressPlayObserverWire>() != 72)
                throw new UnsupportedSignatureException("The shared Play observer wire differs from V1.");
            _bind = hub.Export<ProgressPlayObserverNative>(dormant ? BindDormantPlayExport : BindProgressPlayExport);
            _verify = hub.Export<ProgressPlayObserverNative>(VerifyProgressPlayExport);
            _enter = Enter; _exit = Exit;
            _wire = new() { Version = 1, Size = 72, Slot = channel.Slot, OwnerEpoch = hub.Epoch,
                Target = channel.TargetAddress, Original = channel.OriginalAddress, Entry = channel.EntryAddress,
                Enter = Marshal.GetFunctionPointerForDelegate(_enter), Exit = Marshal.GetFunctionPointerForDelegate(_exit),
                Context = hub._context };
        }

        internal Binding Attach(NativePlaybackStockSourceBridge channel,
            Func<IntPtr, byte, IntPtr, IntPtr, ulong, nuint> enter,
            Action<IntPtr, byte, IntPtr, IntPtr, ulong, nuint, bool> exit, Action<string>? fault)
        {
            lock (_gate)
            {
                Prepare(channel);
                return _active = new(this, enter, exit, fault);
            }
        }

        internal void Prepare(NativePlaybackStockSourceBridge channel)
        {
            if (!ReferenceEquals(_channel, channel)) throw new UnsupportedSignatureException("The shared Play observer lost its original channel.");
            lock (_gate)
            {
                if (_active is not null || _pending.Count != 0)
                    throw new ExactHookBroker.LocalProgressPlayConflictException("The retained Play observation still has a subscriber or an in-flight original.");
                if (!_publicationAttempted)
                {
                    _hub._image.Verify();
                    _publicationAttempted = true;
                    var wire = _wire;
                    if (_bind(_hub._owner, ref wire) != 1)
                        throw new UnsupportedSignatureException("The native Play owner refused its exact observer registration.");
                }
                VerifyPublication();
            }
        }

        internal void Verify() { lock (_gate) VerifyPublication(); }

        private void VerifyPublication()
        {
            _verifyOriginal();
            _hub._image.Verify();
            var wire = _wire;
            if (_verify(_hub._owner, ref wire) != 1)
                throw new UnsupportedSignatureException("The native Play observer lost its exact owner, original or paired callbacks.");
            _hub._image.Verify();
            _verifyOriginal();
        }

        private nuint Enter(IntPtr context, IntPtr receiver, uint raw, IntPtr method, IntPtr caller, ulong sequence)
        {
            Binding? binding = null;
            Invocation? invocation = null;
            try
            {
                lock (_gate)
                {
                    var parent = _current;
                    // A detached original may still call Play recursively.
                    // Give only this physical thread its old masking scope.
                    binding = _active ?? (parent?.Binding.BelongsTo(this) == true ? parent.Binding : null);
                    if (binding is null || binding.Failed) return 0;
                    if (context != _wire.Context || sequence == 0 || caller == IntPtr.Zero || raw > byte.MaxValue)
                        throw new UnsupportedSignatureException("The physical Play Before has an invalid native frame.");
                    invocation = new(binding, parent, receiver, (byte)raw, method, caller);
                    _pending.Add(sequence, invocation); ++binding.InFlight;
                    _current = invocation;
                }
                invocation.Token = binding.Enter(receiver, (byte)raw, method, caller, sequence);
                if (invocation.Token != 0) return checked((nuint)sequence);
                Complete(sequence, invocation);
                return 0;
            }
            catch (Exception error)
            {
                // If Enter failed after registration, the retained frame
                // stays Unknown. Native still runs the actual original.
                if (invocation is not null && ReferenceEquals(_current, invocation)) _current = invocation.Parent;
                binding?.ReportFault("Shared Play Before failed: " + error.Message);
                return 0;
            }
        }

        private void Exit(IntPtr context, IntPtr receiver, uint raw, IntPtr method, IntPtr caller, ulong sequence,
            nuint token, uint returned)
        {
            Invocation? invocation = null;
            try
            {
                lock (_gate)
                {
                    if (!_pending.TryGetValue(sequence, out invocation) || context != _wire.Context || token != sequence
                        || !ReferenceEquals(_current, invocation)
                        || invocation.Receiver != receiver || invocation.Raw != raw || invocation.Method != method
                        || invocation.Caller != caller || invocation.Thread != Environment.CurrentManagedThreadId
                        || invocation.Token == 0 || returned > 1)
                        throw new UnsupportedSignatureException("The physical Play return does not match its exact Before.");
                }
                invocation.Binding.Exit(receiver, (byte)raw, method, caller, sequence, invocation.Token, returned == 1);
                Complete(sequence, invocation);
            }
            catch (Exception error)
            {
                (invocation?.Binding ?? Volatile.Read(ref _active))?.ReportFault("Shared Play completion failed: " + error.Message);
            }
            finally
            {
                if (invocation is not null && ReferenceEquals(_current, invocation)) _current = invocation.Parent;
            }
        }

        private void Complete(ulong sequence, Invocation invocation)
        {
            if (!ReferenceEquals(_current, invocation)) throw new UnsupportedSignatureException("The shared Play scope did not unwind on its original thread.");
            lock (_gate)
            {
                _pending.Remove(sequence);
                --invocation.Binding.InFlight;
                _current = invocation.Parent;
            }
        }

        private sealed record Invocation(Binding Binding, Invocation? Parent, IntPtr Receiver, byte Raw, IntPtr Method, IntPtr Caller)
        {
            internal int Thread { get; } = Environment.CurrentManagedThreadId;
            internal nuint Token { get; set; }
        }

        internal sealed class Binding : ILocalProgressPlayObservation
        {
            private readonly SharedProgressPlayObserver _owner;
            private readonly Action<string>? _fault;
            private int _failed;
            private bool _detached;
            internal readonly Func<IntPtr, byte, IntPtr, IntPtr, ulong, nuint> Enter;
            internal readonly Action<IntPtr, byte, IntPtr, IntPtr, ulong, nuint, bool> Exit;
            internal long InFlight;
            internal bool Failed => Volatile.Read(ref _failed) != 0;
            internal bool BelongsTo(SharedProgressPlayObserver owner) => ReferenceEquals(_owner, owner);

            internal Binding(SharedProgressPlayObserver owner, Func<IntPtr, byte, IntPtr, IntPtr, ulong, nuint> enter,
                Action<IntPtr, byte, IntPtr, IntPtr, ulong, nuint, bool> exit, Action<string>? fault)
                => (_owner, Enter, Exit, _fault) = (owner, enter, exit, fault);

            internal void ReportFault(string reason)
            {
                if (Interlocked.Exchange(ref _failed, 1) != 0) return;
                try { _fault?.Invoke(reason); } catch { }
            }

            public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
            {
                try { _owner.VerifyPublication(); }
                catch (Exception error) { ReportFault(error.Message); }
                lock (_owner._gate) return new(InFlight, !Failed);
            }

            public void Dispose()
            {
                _owner._hub.VerifyThread();
                lock (_owner._gate)
                {
                    if (_detached) return;
                    if (!ReferenceEquals(_owner._active, this))
                        throw new UnsupportedSignatureException("The shared Play observer lease no longer names this subscriber.");
                    _detached = true;
                    _owner._active = null;
                }
            }
        }
    }
}
