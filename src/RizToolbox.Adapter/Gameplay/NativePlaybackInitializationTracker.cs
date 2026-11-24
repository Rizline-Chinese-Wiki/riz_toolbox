using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Retains the actual initializer identity independently of the old
/// source session. Only the registered completion can derive a conditional
/// seven-root capture permit from its actual native initialization transfer.
/// A later real Pause is still required to admit the successor Clock.</summary>
internal sealed class NativePlaybackInitializationTracker : IDisposable
{
    private readonly NativeRuntime _api;
    private readonly PlaybackSourceGroupLease _group;
    private readonly GameEventState.PlaybackRunAssociation _run;
    private readonly NativePlaybackBinding _binding;
    private readonly NativePlaybackInitializationHost? _initialHost;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<NativePlaybackSourceObservation> _owned = new();
    private readonly NativePlaybackInitializationTracker _registered;
    private NativeRuntime.RootHandle? _enumerator, _progress;
    private NativePlaybackSourceObservation? _yield, _final, _child;
    private IDisposable? _runWithdrawal;
    private string? _failure;
    private bool _disposed;
    private ExceptionDispatchInfo? _rootReleaseFailure;
    private Completion? _completion;
    private readonly object? _resourceWatchOwner;
    private NativePlaybackStockSourceHub.ProgressResourceTicket? _resource;
    private bool _resourceInvalidated;
    private bool _backingInvalidated;
    private NativePlaybackStockSourceHub.BackingInitializationInformation? _backingInitialization;
    private bool _lowerPitchInvalidated;
    private NativePlaybackStockSourceHub.LowerPitchInitializationInformation? _lowerPitchInitialization;
    private bool _audioInterruptionInvalidated;
    private NativePlaybackStockSourceHub.AudioInterruptionInformation? _audioInterruption;

    internal NativePlaybackInitializationTracker(NativeRuntime api, PlaybackSourceGroupLease group,
        GameEventState.PlaybackRunAssociation run, NativePlaybackBinding binding, object? resourceWatchOwner = null)
    {
        run.VerifyCurrent(binding); group.VerifyCurrent();
        (_api, _group, _run, _binding, _registered) = (api, group, run, binding, this);
        _resourceWatchOwner = resourceWatchOwner;
        _runWithdrawal = run.OnWithdrawn(binding, reason => _failure ??= reason);
    }
    internal NativePlaybackInitializationTracker(NativeRuntime api, PlaybackSourceGroupLease group,
        GameEventState.PlaybackRunAssociation run, NativePlaybackInitializationHost host)
    {
        host.VerifyCurrent(api, run); group.VerifyCurrent();
        (_api, _group, _run, _initialHost, _registered) = (api, group, run, host, this);
        _resourceWatchOwner = host;
        _binding = null!;
        _runWithdrawal = run.OnHostWithdrawn(reason => _failure ??= reason);
    }
    private long HostProgress => _initialHost?.Progress ?? _binding.Progress;
    private void VerifyRun()
    {
        if (_initialHost is { } host) host.VerifyCurrent(_api, _run);
        else _run.VerifyCurrent(_binding);
    }
    private void VerifyOwner()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new GameplayStateException(ErrorCode.WrongThread, "Initialization roots belong to their actual owner thread.");
        if (!ReferenceEquals(_registered, this))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The initializer tracker is not its registered root owner.");
    }
    internal string? LastFailure => _failure;
    internal bool RootsRetained => _enumerator is not null || _progress is not null || (_resource?.RootsRetained ?? false);
    internal bool ResourceCaptureInvalidated => _resourceInvalidated;
    internal bool AudioInterruptionCaptureInvalidated
    {
        get
        {
            VerifyOwner();
            if (!_group.HasLowerTime || _audioInterruptionInvalidated || _disposed
                || _enumerator is null || _progress is null || _yield is null) return _audioInterruptionInvalidated;
            var enumerator = _enumerator.Target;
            var progress = _progress.Target;
            // This distinct 144-byte projection only selects a deferred route.
            // Native retry must still authenticate normal physical exit, a
            // fresh actual initializer and both physical owners' drained gates.
            var info = _group.ReadOperationAudioInterruption(_enumerator, _yield);
            if (info.Version != 1 || info.Size != 144 || info.Reserved != 0 || info.Invalidated > 1
                || info.Owner == 0 || info.OwnerEpoch != _group.Epoch || info.OwnerEpoch != _yield.OwnerEpoch
                || info.Initializer == 0 || info.InitializerOrdinal == 0
                || info.Enumerator != unchecked((ulong)enumerator.ToInt64())
                || info.Progress != unchecked((ulong)progress.ToInt64()) || progress.ToInt64() != HostProgress
                || Marshal.ReadIntPtr(enumerator, 0x20) != progress || _yield.Self != enumerator
                || info.YieldSequence != _yield.Sequence
                || _audioInterruption is { } prior && (info.Owner != prior.Owner || info.OwnerEpoch != prior.OwnerEpoch
                    || info.Initializer != prior.Initializer || info.InitializerOrdinal != prior.InitializerOrdinal
                    || info.Enumerator != prior.Enumerator || info.Progress != prior.Progress || info.YieldSequence != prior.YieldSequence)
                || info.Invalidated == 1 && (info.FenceKind != 2
                    || !((info.Reason == 1 && info.AttemptRole == (uint)NativePlaybackSourceRole.SetTime)
                        || (info.Reason == 2 && info.AttemptRole == (uint)NativePlaybackSourceRole.LowerSetTime))
                    || info.AttemptSequence == 0 || info.AttemptToken == 0 || info.Writer == 0 || info.Source == 0
                    || info.Scheduling == 0 || info.ActualStamp != info.Scheduling
                    || (info.ActualFlags & (uint)(NativePlaybackSourceFlags.LowerPitchChild
                        | NativePlaybackSourceFlags.EnginePitchChild | NativePlaybackSourceFlags.LowerTimeChild)) != 0)
                || _enumerator.Target != enumerator || _progress.Target != progress)
                throw new UnsupportedSignatureException("The typed audio interruption belongs to another exact initializer, host or Scheduling attempt.");
            _audioInterruption = info;
            // Zero is valid before phase six has staged any PlayerWatch. This
            // badge never substitutes for the registered native retry object.
            if (info.Invalidated == 1)
            {
                _audioInterruptionInvalidated = true;
                _failure ??= "An actual SetTime or direct lower-time attempt interrupted this staged initializer.";
            }
            return _audioInterruptionInvalidated;
        }
    }
    internal bool LowerPitchCaptureInvalidated
    {
        get
        {
            VerifyOwner();
            if (!_group.HasLowerPitch || _lowerPitchInvalidated || _disposed
                || _enumerator is null || _progress is null || _yield is null) return _lowerPitchInvalidated;
            var info = _group.ReadOperationLowerPitchInitializationInvalidation(_enumerator, _yield);
            if (info.Progress != unchecked((ulong)_progress.Target.ToInt64()) || _progress.Target.ToInt64() != HostProgress
                || _lowerPitchInitialization is { } prior && (info.Initializer != prior.Initializer
                    || info.InitializerOrdinal != prior.InitializerOrdinal || info.Enumerator != prior.Enumerator
                    || info.Progress != prior.Progress || info.YieldSequence != prior.YieldSequence))
                throw new UnsupportedSignatureException("The lower-pitch interruption belongs to another exact initializer or retained host.");
            _lowerPitchInitialization ??= info;
            if (info.Invalidated == 1)
            {
                _lowerPitchInvalidated = true;
                _failure ??= "An actual direct lower-pitch attempt interrupted this staged initializer.";
            }
            return _lowerPitchInvalidated;
        }
    }
    internal bool BackingCaptureInvalidated
    {
        get
        {
            VerifyOwner();
            if (_backingInvalidated || _disposed || _enumerator is null || _progress is null || _yield is null)
                return _backingInvalidated;
            var info = _group.ReadOperationBackingInitializationInvalidation(_enumerator, _yield);
            if (info.Progress != unchecked((ulong)_progress.Target.ToInt64()) || _progress.Target.ToInt64() != HostProgress
                || _backingInitialization is { } prior && (info.Initializer != prior.Initializer
                    || info.InitializerOrdinal != prior.InitializerOrdinal || info.Enumerator != prior.Enumerator
                    || info.Progress != prior.Progress || info.YieldSequence != prior.YieldSequence))
                throw new UnsupportedSignatureException("The retained initializer backing status belongs to another exact host or actual yield.");
            _backingInitialization ??= info;
            if (info.Invalidated == 1)
            {
                _backingInvalidated = true;
                _failure ??= "The staged source/clip Unity backing changed before this initializer source-open.";
            }
            return _backingInvalidated;
        }
    }
    internal void ObserveResourceBefore(NativePlaybackSourceObservation observation)
    {
        VerifyOwner(); _group.VerifyIssuedOperationBefore(observation);
        if (observation.Role != NativePlaybackSourceRole.ProgressClip || observation.Self.ToInt64() != HostProgress
            || _resource is null || _disposed) return;
        // Native invalidates the future credential before this callback. This
        // does not withdraw a Clock bound only to the current AudioClip graph.
        if (_resource.ReadOperation().Phase != 4)
            throw new UnsupportedSignatureException("A matching resource write did not expire the retained future capture before its store.");
        _resourceInvalidated = true;
        _failure ??= "The actual Progress resource writer superseded this future initializer capture.";
    }
    private void VerifyResource(bool allowConsumed = false)
    {
        if (_group.HasProgressResource)
        {
            if (_resource is null) throw new GameplayStateException(ErrorCode.NotReady, "The actual initializer has no independently rooted resource credential.");
            _resource.VerifyCurrent(allowConsumed);
        }
    }
    private void VerifyOperationResource()
    {
        if (_group.HasProgressResource)
        {
            if (_resource is null) throw new GameplayStateException(ErrorCode.NotReady, "The actual initializer has no independently rooted resource credential.");
            _resource.VerifyOperationCurrent();
        }
    }
    internal bool Owns(NativePlaybackSourceObservation observation)
    { VerifyOwner(); return _owned.Any(value => ReferenceEquals(value, observation)); }

    // One observation has one responsibility owner. The route must not also
    // deliver these accepted observations to its retired stock producer.
    internal bool ObserveBefore(NativePlaybackSourceObservation observation)
    {
        VerifyOwner();
        var start = observation.Role == NativePlaybackSourceRole.ProgressStartMoveNext;
        var child = _final is not null && observation.ParentSequence == _final.Sequence;
        if (!start && !child) return false;
        _group.VerifyIssuedOperationBefore(observation);
        if (Owns(observation)) throw new UnsupportedSignatureException("An initializer Before cannot have two responsibility deliveries.");
        _owned.Add(observation);
        if (_disposed) { observation.DisposeOperation(); return true; }
        try
        {
            VerifyRun();
            if (start)
            {
                if (observation.ParentSequence != 0 || observation.Depth != 1)
                    throw new GameplayStateException(ErrorCode.TargetExpired, "Initializer entry is not an independent actual parent.");
                if (_enumerator is null)
                {
                    _enumerator = _group.RetainOperationInitializationEnumerator(_api, observation);
                    if (Marshal.ReadInt32(_enumerator.Target, 0x10) != 0)
                        throw new GameplayStateException(ErrorCode.NotReady, "The initial yield was not observed.");
                    var progress = Marshal.ReadIntPtr(_enumerator.Target, 0x20);
                    if (progress.ToInt64() != HostProgress)
                        throw new GameplayStateException(ErrorCode.TargetExpired, "Initializer belongs to another host Progress.");
                    _progress = _group.RetainOperationInitializationProgress(_api, progress);
                    if (_group.HasProgressResource)
                    {
                        _resource = _group.CaptureOperationProgressResource(_resourceWatchOwner
                            ?? throw new UnsupportedSignatureException("This resource initializer has no retained Progress-watch owner."), _api);
                        _resource.BindOperationInitialization();
                    }
                    _yield = observation;
                }
                else
                {
                    if (_final is not null || observation.Self != _enumerator.Target
                        || Marshal.ReadIntPtr(_enumerator.Target, 0x20) != _progress!.Target
                        || Marshal.ReadInt32(_enumerator.Target, 0x10) != 1
                        || !_yield!.TryReadOperationCompletion(out var exit) || exit is not
                            { PhysicalExited: true, Returned: true, Abnormal: false, HasRawReturnByte: true, RawReturnByte: 1 })
                        throw new GameplayStateException(ErrorCode.TargetExpired, "The same retained initializer has no exact completed yield predecessor.");
                    _final = observation;
                    VerifyOperationResource();
                }
            }
            else
            {
                if (_child is not null || observation.Role != NativePlaybackSourceRole.SetClip
                    || observation.IncomingMethod != IntPtr.Zero
                    || observation.Caller != _group.StockOperationLeafCaller(NativePlaybackSourceRole.ProgressStartMoveNext, NativePlaybackSourceRole.SetClip))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "Initializer child is not its unique actual pinned clip CALL.");
                _child = observation;
            }
        }
        catch (Exception error) { _failure ??= error.Message; }
        return true;
    }
    internal bool ObserveReturned(NativePlaybackSourceCompletion completion)
    {
        VerifyOwner();
        if (!Owns(completion.Observation)) return false;
        _group.VerifyIssuedOperationReturn(completion);
        if (!completion.Returned || completion.Abnormal) _failure ??= "Initializer original exited abnormally.";
        return true;
    }
    internal Completion CaptureCompleted()
    {
        VerifyOwner();
        _ = AudioInterruptionCaptureInvalidated;
        _ = BackingCaptureInvalidated;
        _ = LowerPitchCaptureInvalidated;
        if (_disposed || _failure is not null || _enumerator is null || _progress is null || _yield is null || _final is null || _child is null)
            throw new GameplayStateException(ErrorCode.NotReady, _failure ?? "The actual initializer chain is incomplete.");
        VerifyRun();
        _group.VerifyCurrent();
        VerifyResource();
        if (_enumerator.Target != _final.Self || _progress.Target.ToInt64() != HostProgress
            || Marshal.ReadIntPtr(_enumerator.Target, 0x20) != _progress.Target
            || !_final.TryReadCompletion(out var parent) || parent is not
                { PhysicalExited: true, Returned: true, Abnormal: false, HasRawReturnByte: true, RawReturnByte: 0 }
            || !_child.TryReadCompletion(out var child) || child is not
                { PhysicalExited: true, Returned: true, Abnormal: false })
            throw new GameplayStateException(ErrorCode.NotReady, "Initializer roots or exact physical-exit receipts are unavailable.");
        var info = _group.ReadInitialization();
        if (info.Phase != 4 || info.Blocked != 1 || info.Receipt == 0
            || _audioInterruption is { } audio && (info.Owner != audio.Owner || info.OwnerEpoch != audio.OwnerEpoch
                || info.Receipt != audio.Initializer || info.Ordinal != audio.InitializerOrdinal)
            || info.Enumerator != unchecked((ulong)_enumerator.Target.ToInt64())
            || info.Progress != unchecked((ulong)_progress.Target.ToInt64())
            || !_group.MatchesInitializationCall(_yield, info.YieldSequence, info.YieldToken)
            || !_group.MatchesInitializationCall(_final, info.FinalSequence, info.FinalToken)
            || !_group.MatchesInitializationCall(_child, info.ChildSequence, info.ChildToken)
            || info.Clip != unchecked((ulong)_child.ReferenceArgument.ToInt64()))
            throw new GameplayStateException(ErrorCode.NotReady, "Native initialization candidate no longer matches the retained identity and actual calls.");
        VerifyRun();
        return _completion ??= new Completion(this, info);
    }
    internal sealed class Completion
    {
        private readonly NativePlaybackInitializationTracker _owner;
        private readonly NativePlaybackStockSourceHub.InitializationInformation _native;
        private NativePlaybackSingleReturnBridge.LogicalHandoff? _transfer;
        private NativePlaybackSingleReturnBridge.InitialCapture? _firstCapture;
        private CapturePermit? _permit;
        internal Completion(NativePlaybackInitializationTracker owner, NativePlaybackStockSourceHub.InitializationInformation native)
            => (_owner, _native) = (owner, native);
        internal void VerifyCurrent()
        {
            if (!ReferenceEquals(_owner.CaptureCompleted(), this))
                throw new GameplayStateException(ErrorCode.TargetExpired, "This initializer completion is not the exact registered object.");
            var current = _owner._group.ReadInitialization();
            if (current.Receipt != _native.Receipt || current.Ordinal != _native.Ordinal)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The initializer completion was superseded.");
        }
        internal IntPtr NativeIdentityFor(PlaybackSourceGroupLease group)
        {
            VerifyCurrent();
            if (!ReferenceEquals(_owner._group, group))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Initializer completion belongs to another registered physical group.");
            return new IntPtr(checked((long)_native.Receipt));
        }
        internal long Player { get { VerifyCurrent(); return checked((long)_native.Player); } }
        internal long Source { get { VerifyCurrent(); return checked((long)_native.Source); } }
        internal long Clip { get { VerifyCurrent(); return checked((long)_native.Clip); } }
        internal NativePlaybackBinding DescribeSuccessor(NativePlaybackBinding predecessor)
        {
            VerifyCurrent();
            if (predecessor != _owner._binding)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The initializer was retained for another exact predecessor graph.");
            return predecessor with { Player = checked((long)_native.Player), AudioSource = checked((long)_native.Source), Clip = checked((long)_native.Clip) };
        }
        internal void RetainTransfer(PlaybackSourceGroupLease group, NativePlaybackSingleReturnBridge.LogicalHandoff transfer)
        {
            VerifyRetained(group);
            if (_transfer is not null || transfer.Read(1).Phase != 1)
                throw new GameplayStateException(ErrorCode.TargetExpired, "This initializer already transferred its exact native completion.");
            _transfer = transfer;
        }
        private void VerifyRetained(PlaybackSourceGroupLease group)
        {
            _owner.VerifyOwner();
            if (_owner._disposed || _owner._failure is not null || !ReferenceEquals(_owner._completion, this)
                || !ReferenceEquals(_owner._group, group) || _owner._enumerator is null || _owner._progress is null
                || _owner._enumerator.Target.ToInt64() != checked((long)_native.Enumerator)
                || _owner._progress.Target.ToInt64() != checked((long)_native.Progress)
                || Marshal.ReadIntPtr(_owner._enumerator.Target, 0x20) != _owner._progress.Target)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The consumed initializer lost its exact registered roots or host authority.");
            _owner.VerifyRun(); group.VerifyCurrent();
            _owner.VerifyResource();
            var current = group.ReadInitialization();
            if (current.Phase != 6 || current.Blocked != 1 || current.Ordinal != _native.Ordinal
                || current.Enumerator != _native.Enumerator || current.Progress != _native.Progress
                || current.Player != _native.Player || current.Source != _native.Source || current.Clip != _native.Clip)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The private initializer transfer was replaced or lost its native hold.");
        }
        internal void VerifyTransferred(PlaybackSourceGroupLease group, NativePlaybackSingleReturnBridge.LogicalHandoff transfer)
        {
            VerifyRetained(group);
            if (!ReferenceEquals(_transfer, transfer))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Another logical transfer cannot consume this initializer completion.");
            var phase = transfer.Read().Phase;
            if (phase is not (1 or 2))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Conditional initializer capture requires the pending actual joint transfer.");
        }
        internal CapturePermit GrantCapture(PlaybackSourceGroupLease group, NativePlaybackSingleReturnBridge.LogicalHandoff transfer,
            NativePlaybackBinding binding)
        {
            VerifyTransferred(group, transfer);
            if (binding.SourceGeneration != checked((long)transfer.Read(2).NewEpoch)
                || binding != (_owner._binding with { SourceGeneration = binding.SourceGeneration,
                    Player = checked((long)_native.Player), AudioSource = checked((long)_native.Source), Clip = checked((long)_native.Clip) }))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Conditional capture changed the retained host or initialized graph.");
            _owner._run.VerifyCurrent(binding);
            group.VerifyInitializationCapture(transfer, binding.Player, binding.AudioSource, binding.Clip);
            return _permit ??= CapturePermit.Issue(this, group, transfer, binding);
        }
        internal void VerifyPermit(CapturePermit permit, PlaybackSourceGroupLease group,
            NativePlaybackSingleReturnBridge.LogicalHandoff transfer, NativePlaybackBinding binding)
        {
            VerifyTransferred(group, transfer);
            if (!ReferenceEquals(_permit, permit))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only the registered initializer capture permit owns this graph.");
            _owner._run.VerifyCurrent(binding);
            group.VerifyInitializationCapture(transfer, binding.Player, binding.AudioSource, binding.Clip);
        }
        internal void VerifyOpened(CapturePermit permit, PlaybackSourceGroupLease group,
            NativePlaybackSingleReturnBridge.LogicalHandoff transfer, NativePlaybackBinding binding)
        {
            _owner.VerifyOwner();
            if (_owner._disposed || _owner._failure is not null || !ReferenceEquals(_owner._completion, this)
                || !ReferenceEquals(_permit, permit) || !ReferenceEquals(_owner._group, group) || !ReferenceEquals(_transfer, transfer))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The source-open completion lost its registered initializer consumer.");
            _owner._run.VerifyCurrent(binding); transfer.Read(3);
            _owner.VerifyResource(allowConsumed: true);
            var current = group.ReadInitialization();
            if (current.Phase != 7 || current.Blocked != 0 || current.Ordinal != _native.Ordinal)
                throw new GameplayStateException(ErrorCode.TargetExpired, "Actual initialized source-open did not clear its own native fresh hold.");
            group.RequireFreshAdmission();
        }
        internal void RetainFirstCapture(PlaybackSourceGroupLease group, NativePlaybackSingleReturnBridge.InitialCapture capture)
        {
            VerifyRetained(group);
            if (_owner._initialHost is null || _transfer is not null || _firstCapture is not null || capture.Read(1).Phase != 1)
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only the exact host-only completion can retain first capture once.");
            _firstCapture = capture;
        }
        private void VerifyFirstCapture(PlaybackSourceGroupLease group, NativePlaybackSingleReturnBridge.InitialCapture capture)
        {
            VerifyRetained(group);
            if (!ReferenceEquals(_firstCapture, capture) || _owner._initialHost is null || capture.Read().Phase is not (1 or 2))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The first-capture completion lost its native registration.");
        }
        internal NativePlaybackBinding DescribeInitial(PlaybackSourceGroupLease group, NativePlaybackSingleReturnBridge.InitialCapture capture)
        {
            VerifyFirstCapture(group, capture);
            var info = capture.Read(2);
            if (info.Initializer != _native.Receipt || info.Player != _native.Player || info.Source != _native.Source || info.Clip != _native.Clip)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The native first-capture receipt no longer matches this exact retained completion.");
            return _owner._initialHost!.BindCompleted(capture);
        }
        internal CapturePermit GrantFirstCapture(PlaybackSourceGroupLease group, NativePlaybackSingleReturnBridge.InitialCapture capture)
        {
            var binding = DescribeInitial(group, capture);
            group.VerifyInitialCapture(capture, binding.Player, binding.AudioSource, binding.Clip);
            return _permit ??= CapturePermit.IssueFirst(this, group, capture, binding);
        }
        internal void VerifyFirstPermit(CapturePermit permit, PlaybackSourceGroupLease group,
            NativePlaybackSingleReturnBridge.InitialCapture capture, NativePlaybackBinding binding)
        {
            if (!ReferenceEquals(_permit, permit) || binding != DescribeInitial(group, capture))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only the registered first-capture permit owns its exact initialized graph.");
            group.VerifyInitialCapture(capture, binding.Player, binding.AudioSource, binding.Clip);
        }
        internal void VerifyFirstOpened(CapturePermit permit, PlaybackSourceGroupLease group,
            NativePlaybackSingleReturnBridge.InitialCapture capture, NativePlaybackBinding binding)
        {
            _owner.VerifyOwner();
            if (_owner._disposed || _owner._failure is not null || !ReferenceEquals(_owner._completion, this)
                || !ReferenceEquals(_permit, permit) || !ReferenceEquals(_owner._group, group) || !ReferenceEquals(_firstCapture, capture))
                throw new GameplayStateException(ErrorCode.TargetExpired, "First source-open lost its registered initializer consumer.");
            _owner.VerifyRun(); _owner._run.VerifyCurrent(binding); capture.Read(3);
            _owner.VerifyResource(allowConsumed: true);
            var current = group.ReadInitialization();
            if (current.Phase != 7 || current.Blocked != 0 || current.Ordinal != _native.Ordinal)
                throw new GameplayStateException(ErrorCode.TargetExpired, "Actual first source-open did not clear its own native hold.");
            group.RequireFreshAdmission();
        }
    }
    // This type is the only alternate path through rooted-source construction.
    // It is registered to one completion, native phase-2 transfer, immutable graph
    // and source object. It never grants ordinary Fresh or Clock admission.
    internal sealed class CapturePermit
    {
        private readonly Completion _completion;
        private readonly NativePlaybackStockClockRouter.ObservedInitialization? _observed;
        private readonly PlaybackSourceGroupLease _group;
        private readonly NativePlaybackSingleReturnBridge.LogicalHandoff? _transfer;
        private readonly NativePlaybackSingleReturnBridge.InitialCapture? _firstCapture;
        private readonly NativePlaybackBinding _binding;
        private readonly CapturePermit _registered;
        private NativePlaybackStockRootedSource? _source;
        private bool _opened;
        private CapturePermit(Completion completion, PlaybackSourceGroupLease group,
            NativePlaybackSingleReturnBridge.LogicalHandoff transfer, NativePlaybackBinding binding)
            => (_completion, _group, _transfer, _binding, _registered) = (completion, group, transfer, binding, this);
        internal static CapturePermit Issue(Completion completion, PlaybackSourceGroupLease group,
            NativePlaybackSingleReturnBridge.LogicalHandoff transfer, NativePlaybackBinding binding)
            => new(completion, group, transfer, binding);
        private CapturePermit(Completion completion, PlaybackSourceGroupLease group,
            NativePlaybackSingleReturnBridge.InitialCapture capture, NativePlaybackBinding binding)
            => (_completion, _group, _firstCapture, _binding, _registered) = (completion, group, capture, binding, this);
        internal static CapturePermit IssueFirst(Completion completion, PlaybackSourceGroupLease group,
            NativePlaybackSingleReturnBridge.InitialCapture capture, NativePlaybackBinding binding)
            => new(completion, group, capture, binding);
        private CapturePermit(NativePlaybackStockClockRouter.ObservedInitialization observed, PlaybackSourceGroupLease group,
            NativePlaybackSingleReturnBridge.LogicalHandoff transfer, NativePlaybackBinding binding)
            => (_completion, _observed, _group, _transfer, _binding, _registered) = (null!, observed, group, transfer, binding, this);
        internal static CapturePermit IssueObserved(NativePlaybackStockClockRouter.ObservedInitialization observed,
            PlaybackSourceGroupLease group, NativePlaybackSingleReturnBridge.LogicalHandoff transfer, NativePlaybackBinding binding)
            => new(observed, group, transfer, binding);
        internal void Claim(NativePlaybackStockRootedSource source, PlaybackSourceGroupLease group, NativePlaybackBinding binding)
        {
            if (_source is not null) throw new GameplayStateException(ErrorCode.TargetExpired, "Initializer capture already belongs to its one source object.");
            VerifyIdentity(group, binding); _source = source;
            _observed?.RegisterSource(this, source, group, binding);
            Verify(source, group, binding);
        }
        private void VerifyIdentity(PlaybackSourceGroupLease group, NativePlaybackBinding binding)
        {
            if (!ReferenceEquals(_registered, this) || _opened || !ReferenceEquals(_group, group) || _binding != binding)
                throw new GameplayStateException(ErrorCode.TargetExpired, "Copied, opened or foreign initializer capture has no conditional authority.");
        }
        internal void Verify(NativePlaybackStockRootedSource source, PlaybackSourceGroupLease group, NativePlaybackBinding binding)
        {
            VerifyIdentity(group, binding);
            if (!ReferenceEquals(_source, source))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Initializer permit belongs to another rooted source.");
            if (_observed is { } observed) observed.VerifyPermit(this, source, group, binding);
            else if (_firstCapture is { } capture) _completion.VerifyFirstPermit(this, group, capture, binding);
            else _completion.VerifyPermit(this, group, _transfer!, binding);
        }
        internal void CompleteSourceOpen(NativePlaybackStockRootedSource source, PlaybackSourceGroupLease group, NativePlaybackBinding binding)
        {
            VerifyIdentity(group, binding);
            if (!ReferenceEquals(_source, source))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only the captured source can finish initialization admission.");
            if (_observed is { } observed) observed.VerifyOpened(this, source, group, binding);
            else if (_firstCapture is { } capture) _completion.VerifyFirstOpened(this, group, capture, binding);
            else _completion.VerifyOpened(this, group, _transfer!, binding);
            _opened = true;
        }
    }
    public void Dispose()
    {
        VerifyOwner(); _rootReleaseFailure?.Throw(); _disposed = true; _completion = null;
        _runWithdrawal?.Dispose(); _runWithdrawal = null;
        // Unknown or in-flight native responsibility retains both roots.
        var activity = _group.ReadActivity();
        if (!activity.Known || activity.InFlight != 0) return;
        foreach (var observation in _owned) observation.Dispose();
        _group.PumpCompleted();
        if (_owned.Any(value => !value.IsReleased)) return;
        Exception? first = null;
        try { _resource?.Dispose(); if (_resource is { RootsRetained: false }) _resource = null; }
        catch (Exception error) { first ??= error; }
        try { _progress?.Dispose(); _progress = null; } catch (Exception error) { first ??= error; }
        try { _enumerator?.Dispose(); _enumerator = null; } catch (Exception error) { first ??= error; }
        // Preserve the exact failed handle and exception. RootHandle consumes
        // its id before native free, so repeated Dispose cannot prove success.
        if (first is not null)
        {
            _failure ??= "Initializer root release has an unknown native free outcome: " + first.Message;
            _rootReleaseFailure = ExceptionDispatchInfo.Capture(first); _rootReleaseFailure.Throw();
        }
    }
}
