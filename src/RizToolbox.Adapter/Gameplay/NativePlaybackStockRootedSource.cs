using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Seven independently rooted objects plus actual stock execution and
/// native writer receipts with a host-issued run association. This layer proves
/// API readback on that exact graph. It does not issue a Clock, complete writer coverage,
/// or production first-install admission.</summary>
internal sealed partial class NativePlaybackStockRootedSource : IDisposable
{
    private readonly NativeRuntime _api;
    private readonly NativePlaybackBinding _binding;
    private readonly NativePlaybackRateOrigin _origin;
    private readonly GameEventState.PlaybackRunAssociation _runAssociation;
    private readonly IDisposable _runWithdrawal;
    private readonly PlaybackSourceGroupLease _group;
    private readonly NativePlaybackActualSource _actual;
    private readonly NativePlaybackStockOccurrenceProducer _producer;
    private readonly Action<ReadbackReceipt>? _returned;
    private readonly Action<string>? _withdrawn;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly object _issuer = new();
    private readonly PlaybackFreshReadScopes _operations = new();
    private readonly Dictionary<ulong, double> _beforeDsp = new();
    private ReadbackReceipt? _current;
    private string? _fault;
    private bool _disposed, _rootsReleased, _rootReleaseFailed;
    private bool _watchStaged, _acceptedReadback;
    private double _lastDsp;
    private float _selectedRate;
    private NativePlaybackInitializationTracker.CapturePermit? _initializationCapture;
    internal string? LastFailure => _fault;
    internal bool ProductionAdmission => false;
    internal bool RootsReleased => _freshAdoption?.RootsReleased ?? _rootsReleased;

    internal NativePlaybackStockRootedSource(NativeRuntime api, NativePlaybackBinding binding,
        NativePlaybackRateOrigin origin, GameEventState.PlaybackRunAssociation runAssociation,
        PlaybackSourceGroupLease group, Action<ReadbackReceipt>? returned = null, Action<string>? withdrawn = null)
        : this(api, binding, origin, runAssociation, group, null, returned, withdrawn) { }
    private NativePlaybackStockRootedSource(NativeRuntime api, NativePlaybackBinding binding,
        NativePlaybackRateOrigin origin, GameEventState.PlaybackRunAssociation runAssociation,
        PlaybackSourceGroupLease group, NativePlaybackInitializationTracker.CapturePermit? initializationCapture,
        Action<ReadbackReceipt>? returned, Action<string>? withdrawn)
    {
        ArgumentNullException.ThrowIfNull(runAssociation);
        runAssociation.VerifyCurrent(binding);
        if (initializationCapture is null) group.RequireFreshAdmission();
        else initializationCapture.Claim(this, group, binding);
        _initializationCapture = initializationCapture;
        if (!binding.Valid || origin.RunId != binding.RunId || origin.ControllerGeneration != binding.ControllerGeneration
            || origin.Controller != binding.Controller || origin.Information != binding.Information || origin.Progress != binding.Progress || origin.Pause == 0)
            throw new GameplayStateException(ErrorCode.InvalidArgument, "The rooted source requires one immutable descriptor and its exact pause/controller graph.");
        (_api, _binding, _origin, _group, _returned) = (api, binding, origin, group, returned);
        _withdrawn = withdrawn;
        _runAssociation = runAssociation;
        _actual = new(api, binding, origin.Pause);
        NativePlaybackStockOccurrenceProducer? producer = null;
        IDisposable? withdrawal = null;
        try
        {
            VerifyGraph(); _lastDsp = _actual.ReadDsp();
            var initial = _actual.Read(checked((long)group.Epoch), 0);
            ValidateSample(initial); AcceptDsp(initial.DspSeconds); _selectedRate = initial.Pitch;
            _producer = producer = new(group, new(binding.Player), new(binding.AudioSource), new(binding.Progress), OnReturned);
            _runWithdrawal = withdrawal = runAssociation.OnWithdrawn(binding,
                reason => Fail(new GameplayStateException(ErrorCode.TargetExpired, reason)));
            VerifyGraph();
            // The player is already the fifth independent root in _actual.
            // Stage its exact watch while the native source-open gate is still
            // closed (or activate immediately for an initial fresh source).
            _watchStaged = group.StagePlayerWatch(this, api, _actual.PlayerRoot, _actual.AudioSourceRoot, _actual.ProgressRoot, _actual.ClipRoot);
            if (_watchStaged != group.HasPlayerConstructor)
                throw new UnsupportedSignatureException("The rooted source and its exact constructor group disagree on watch admission.");
        }
        catch (Exception error)
        {
            _disposed = true; _fault ??= "The rooted source construction failed: " + error.Message;
            try { withdrawal?.Dispose(); } catch { }
            try { producer?.Dispose(); } catch { }
            // Stage can fail after the native receipt was issued. Retain the
            // seven roots if retirement cannot be proved; the hub still holds
            // this source owner and therefore its player handle.
            if (!PumpUnresolvedPlayerRoots(producer)) RetainPlayerRoots(producer);
            throw;
        }
    }
    private void VerifyThread()
    { if (_thread != Environment.CurrentManagedThreadId) throw new GameplayStateException(ErrorCode.WrongThread, "Rooted stock access is owner-thread bound."); }
    private void Open()
    {
        VerifyThread(); _freshAdoption?.VerifySourceRegistration(this);
        if (_disposed) throw new ObjectDisposedException(nameof(NativePlaybackStockRootedSource));
        if (_fault is not null) throw new GameplayStateException(ErrorCode.TargetExpired, _fault);
    }
    private void VerifyGraph()
    {
        using var operation = EnterOperation();
        VerifyGraphBody();
        // The fresh accessor has just completed the same capture's complete
        // bracket. Only local registration/run checks follow it, so another
        // recursive capture check would introduce no new consumer boundary.
        if (_freshAdoption is { } current) current.VerifySourceRegistration(this); else VerifyCaptureAdmission();
        Open();
    }
    private void VerifyGraphBody()
    {
        // Actual.VerifyGraph immediately performs the complete fresh accessor
        // check before its first getter. This local identity check grants no use.
        if (_freshAdoption is { } fresh) fresh.VerifySourceRegistration(this); else VerifyCaptureAdmission();
        _runAssociation.VerifyCurrent(_binding);
        _actual.VerifyGraph();
        if (_freshAdoption is null)
            _group.VerifyOperationReceivers(_api, new(_binding.Player), new(_binding.AudioSource), new(_binding.Progress), new(_binding.GetTimeMethod));
        if (_watchStaged)
        {
            if (_initializationCapture is not null) _group.VerifyPlayerWatch(this);
            else _group.VerifyOperationActivePlayerWatch(this);
        }
        _runAssociation.VerifyCurrent(_binding);
    }
    private double VerifyGraphAndReadDsp(NativePlaybackFreshSourceCapture.Adoption adoption)
    {
        using var operation = EnterOperation();
        if (!ReferenceEquals(_freshAdoption, adoption))
            throw new GameplayStateException(ErrorCode.TargetExpired, "Only this source's retained fresh adoption can combine its before-DSP read.");
        VerifyGraphBody();
        // No native consumer separates the ordinary graph exit's fresh check
        // from ReadDsp's fresh entry. Let that existing operation entry serve this
        // single synchronous boundary; ReadDsp retains its post-getter check.
        var dsp = _actual.ReadDsp();
        Open();
        return dsp;
    }
    internal void ObserveBefore(NativePlaybackSourceObservation observation)
    {
        VerifyThread(); _producer.ObserveBefore(observation);
        if (!_disposed && _fault is null && _producer.LogicalFault is string sourceFault)
            Fail(new GameplayStateException(ErrorCode.TargetExpired, sourceFault));
        if (_disposed || _fault is not null) return;
        if (observation.ParentSequence == 0 && observation.Role is NativePlaybackSourceRole.Pause
            or NativePlaybackSourceRole.ProgressPlay or NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume)
        {
            _current = null;
            try
            {
                double dsp;
                if (_freshAdoption is { } fresh) dsp = VerifyGraphAndReadDsp(fresh);
                else { VerifyGraph(); dsp = _actual.ReadDsp(); }
                AcceptDsp(dsp);
                _beforeDsp[observation.Sequence] = dsp;
            }
            catch (Exception error) { Fail(error); }
        }
    }
    internal void ObserveReturned(NativePlaybackSourceCompletion completion)
    {
        VerifyThread(); _producer.ObserveReturned(completion);
        if (!_disposed && _fault is null && completion.Observation.ParentSequence == 0
            && completion.Observation.Role is NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay or NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume
            && _current is null)
        {
            if (_freshAdoption is { } fresh && !_acceptedReadback && _producer.IsRejectedNoEffectReturn(completion))
            {
                // A verified native no-op supplies no baseline. Before the
                // first accepted readback, keep this same fresh owner waiting
                // for a later real operation; never revive an older receipt,
                // replace its roots, or manufacture a Clock from paused flags.
                try { fresh.VerifySource(this); }
                catch (Exception error) { Fail(error); }
            }
            else Fail(new GameplayStateException(ErrorCode.ObservationUnavailable, _producer.LastRejection ?? "The actual stock root produced no current readback receipt."));
        }
        if (completion.Observation.ParentSequence == 0) _beforeDsp.Remove(completion.Sequence);
    }
    private void OnReturned(NativePlaybackStockOccurrenceProducer.ExecutionReceipt receipt)
    {
        if (_disposed || _fault is not null) return;
        try
        {
            if (!_beforeDsp.TryGetValue(receipt.Sequence, out var before))
                throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The exact native root has no independently observed before-DSP read.");
            var first = ReadVerified(receipt.VerifyCurrent, receipt.SchedulingSequence);
            NativePlaybackStockOccurrenceProducer.ContinuationReceipt? writer = null;
            if (receipt.Role is NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume)
            {
                if (first.Sample.Pitch != 1 || receipt.Schedule is null)
                    throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The actual stock reset-to-one or independent schedule witness is missing.");
                receipt.Schedule.VerifyCurrent();
                var startDsp = receipt.Schedule.ActualStartDsp;
                var mediaAnchor = first.Sample.AudioPositionSeconds;
                VerifyScheduledReadback(receipt.Role, startDsp, mediaAnchor, first.Sample, first.AfterDsp);
                writer = _freshAdoption is { } fresh
                    ? (_selectedRate == first.Sample.Pitch ? null : fresh.RestorePitch(this, _producer, receipt, _selectedRate))
                    : _producer.WritePitch(receipt, _selectedRate);
                if (writer is not null)
                {
                    first = ReadVerified(writer.VerifyCurrent, writer.SchedulingSequence);
                    if (first.Sample.Pitch != _selectedRate)
                        throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The reserved native writer returned without matching actual API pitch readback.");
                    VerifyScheduledReadback(receipt.Role, startDsp, mediaAnchor, first.Sample, first.AfterDsp);
                }
            }
            else if (first.Sample.AudioPlaying || first.Sample.HasStockOrigin)
                throw new GameplayStateException(ErrorCode.ObservationUnavailable, "Actual Pause did not leave stopped audio and a cleared stock origin.");
            _current = ReadbackReceipt.Issue(this, receipt, writer, before, first.Sample, first.AfterDsp, _issuer);
            _current.VerifyCurrent(); _acceptedReadback = true; _returned?.Invoke(_current);
        }
        catch (Exception error) { Fail(error); }
    }
    private static void VerifyScheduledReadback(NativePlaybackSourceRole role, double startDsp, float mediaAnchor,
        NativePlaybackRateSample sample, double afterDsp)
    {
        if (!double.IsFinite(startDsp) || startDsp < afterDsp)
            throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The native DSP start passed before the schedule readback completed.");

        // Replay resets the source. Resume stores the same leaf DSP start
        // minus the resumed media time in its own nullable origin field.
        // The origin corroborates the leaf argument and never supplies it.
        if (!sample.HasStockOrigin || sample.StockOriginSeconds != startDsp - mediaAnchor
            || (role == NativePlaybackSourceRole.Replay && mediaAnchor != 0)
            || sample.AudioPositionSeconds != mediaAnchor)
            throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The actual stock origin and media position disagree with the native schedule.");
    }
    private (NativePlaybackRateSample Sample, double AfterDsp) ReadVerified(Action verify, ulong scheduling)
    {
        using var operation = EnterOperation();
        Open(); verify(); VerifyGraph();
        var sample = _actual.Read(checked((long)_group.Epoch), checked((long)scheduling));
        ValidateSample(sample); AcceptDsp(sample.DspSeconds);
        var after = _actual.ReadDsp(); AcceptDsp(after);
        verify(); VerifyGraph();
        return (sample, after);
    }
    private (NativePlaybackRateSample Sample, double AfterDsp) ReadVerifiedReceipt(ReadbackReceipt receipt, ulong scheduling)
    {
        using var operation = EnterOperation();
        // A complete receipt verification already includes VerifyGraph. Keep
        // this first check outside the Fail region so a stale receipt cannot
        // fault the source that now owns the current readback.
        Open(); receipt.VerifyCurrent();
        try
        {
            var sample = _actual.Read(checked((long)_group.Epoch), checked((long)scheduling));
            ValidateSample(sample); AcceptDsp(sample.DspSeconds);
            var after = _actual.ReadDsp(); AcceptDsp(after);
            receipt.VerifyCurrent();
            return (sample, after);
        }
        catch (Exception error) { Fail(error); throw; }
    }
    private void AcceptDsp(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value < _lastDsp)
            throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The independently read native DSP clock regressed or became nonfinite.");
        _lastDsp = value;
    }
    private static void ValidateSample(NativePlaybackRateSample sample)
    {
        if (!double.IsFinite(sample.DspSeconds) || sample.DspSeconds < 0 || !float.IsFinite(sample.Pitch) || sample.Pitch <= 0
            || !float.IsFinite(sample.AudioPositionSeconds) || sample.AudioPositionSeconds < 0
            || !float.IsFinite(sample.ClipLengthSeconds) || sample.ClipLengthSeconds <= 0
            || sample.AudioPositionSeconds > sample.ClipLengthSeconds)
            throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The actual rooted DSP/media/pitch/clip readback is outside its finite domain.");
    }
    private void Fail(Exception error)
    {
        var notify = _fault is null;
        _fault ??= error.Message; _current = null;
        _producer.WithdrawLogical("Rooted API readback became unavailable: " + error.Message);
        if (_freshAdoption is { } fresh) fresh.Close(this, _fault);
        if (notify) _withdrawn?.Invoke(_fault);
    }
    internal ReadbackReceipt? Current
    {
        get { Open(); _current?.VerifyCurrent(); return _current; }
    }
    // Local identity data only. The caller must immediately perform Read or
    // VerifyCurrent on the selected receipt before consuming its authority.
    internal ReadbackReceipt? ObserveCurrentReadbackIdentity()
    {
        Open(); return _current;
    }
    // Scheduling hint only: Resume/Replay cannot supply the first paused Clock
    // baseline. Presence still requires Current's full fresh validation, actual
    // physical exit and API state checks; no sample flags are cached here.
    internal bool HasFirstPausedReadbackCandidate
    {
        get { Open(); return _current?.Role is NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay; }
    }
    internal ReadbackReceipt WritePausedPitch(ReadbackReceipt expected, float pitch)
    {
        using var writer = PreparePausedPitchWrite(expected);
        writer.Write(pitch);
        return writer.ObserveReadbackIdentity();
    }
    internal void PumpCompleted()
    {
        VerifyThread();
        if (_disposed || _fault is not null || !_group.IsActive) _producer.PumpCompleted();
        else _producer.PumpOperationCompleted();
        if (_disposed && !_rootsReleased)
        {
            try
            {
                if (!TryReleasePlayerRoots(_producer)) RetainPlayerRoots(_producer);
            }
            catch (Exception error)
            {
                _fault ??= "The retired player graph could not release every root: " + error.Message;
                RetainPlayerRoots(_producer);
                throw;
            }
        }
    }
    private void RetainPlayerRoots(NativePlaybackStockOccurrenceProducer? producer)
        => _group.RetainUnresolvedPlayerRootOwner(this, () => PumpUnresolvedPlayerRoots(producer));
    private bool PumpUnresolvedPlayerRoots(NativePlaybackStockOccurrenceProducer? producer)
    {
        VerifyThread();
        try { return TryReleasePlayerRoots(producer); }
        catch (Exception error)
        {
            _fault ??= "The retired player graph could not release every root: " + error.Message;
            return false;
        }
    }
    private bool TryReleasePlayerRoots(NativePlaybackStockOccurrenceProducer? producer)
    {
        if (_operations.Active != 0) return false;
        if (_freshAdoption is { } fresh)
        {
            fresh.VerifySourceRegistration(this);
            if (!_disposed) return false;
            fresh.Close(this, _fault ?? "The adopted fresh source was disposed.");
            return fresh.RootsReleased;
        }
        if (_rootsReleased) return true;
        if (!_disposed || _rootReleaseFailed || producer is not null && !producer.OwnResponsibilitiesDrained) return false;
        bool retired;
        try
        {
            var activity = _group.ReadActivity();
            var getTime = _group.GetTimeLease.ReadActivity();
            // A failed Stage may have issued its native receipt before setting
            // _watchStaged. Always retire by this exact owner, including that case.
            retired = activity.Known && activity.InFlight == 0 && getTime.Known && getTime.InFlight == 0
                && _group.TryRetirePlayerWatch(this);
        }
        catch (Exception error) { _fault ??= "The rooted player watch could not retire: " + error.Message; return false; }
        if (!retired) return false;
        try { _actual.Dispose(); _rootsReleased = true; }
        catch { _rootReleaseFailed = true; throw; } // An unknown native free outcome cannot become a successful retry.
        return true;
    }
    public void Dispose()
    {
        VerifyThread(); _freshAdoption?.VerifySourceRegistration(this); _operations.Close(); _disposed = true; _current = null;
        _runWithdrawal.Dispose();
        _producer.Dispose(); PumpCompleted();
    }

    internal sealed partial class ReadbackReceipt
    {
        private readonly NativePlaybackStockRootedSource _owner;
        private readonly NativePlaybackStockOccurrenceProducer.ExecutionReceipt _execution;
        private readonly NativePlaybackStockOccurrenceProducer.ContinuationReceipt? _writer;
        private ReadbackReceipt(NativePlaybackStockRootedSource owner, NativePlaybackStockOccurrenceProducer.ExecutionReceipt execution,
            NativePlaybackStockOccurrenceProducer.ContinuationReceipt? writer, double before, NativePlaybackRateSample sample, double after)
            => (_owner, _execution, _writer, BeforeDsp, Sample, AfterDsp) = (owner, execution, writer, before, sample, after);
        internal static ReadbackReceipt Issue(NativePlaybackStockRootedSource owner, NativePlaybackStockOccurrenceProducer.ExecutionReceipt execution,
            NativePlaybackStockOccurrenceProducer.ContinuationReceipt? writer, double before, NativePlaybackRateSample sample, double after, object issuer)
        {
            if (!ReferenceEquals(owner._issuer, issuer)) throw new UnsupportedSignatureException("Only this rooted consumer can issue its API readback receipt.");
            return new(owner, execution, writer, before, sample, after);
        }
        internal double BeforeDsp { get; }
        internal double AfterDsp { get; }
        internal NativePlaybackRateSample Sample { get; }
        internal NativePlaybackSourceRole Role => _execution.Role;
        internal ulong RootSequence => _execution.Sequence;
        internal ulong? ActualScheduleBits => _writer?.Schedule?.RawDoubleBits ?? _execution.Schedule?.RawDoubleBits;
        internal bool RunAuthorityProven { get { VerifyCurrent(); return true; } }
        internal bool ProductionAdmission => false;
        internal bool PhysicalExited { get { VerifyCurrent(); return _writer?.RootPhysicalExited ?? _execution.PhysicalExited; } }
        internal void VerifyCurrent()
        {
            using var operation = _owner.EnterOperation();
            _owner.Open();
            if (!ReferenceEquals(_owner._current, this)) throw new UnsupportedSignatureException("A copied or superseded readback receipt is not current.");
            _owner.RejectPendingPitchReadback(this);
            _owner.RejectPendingTimeReadback(this);
            try
            {
                if (_writer is null) _execution.VerifyCurrent(); else _writer.VerifyCurrent();
                _owner.VerifyGraph();
                _owner.Open();
                if (!ReferenceEquals(_owner._current, this))
                    throw new UnsupportedSignatureException("The readback receipt changed during a native graph getter.");
            }
            catch (Exception error) { _owner.Fail(error); throw; }
        }
        internal void VerifyOperationIdentity()
        {
            using var operation = _owner.EnterOperation();
            _owner.Open();
            if (!ReferenceEquals(_owner._current, this))
                throw new UnsupportedSignatureException("A copied or superseded readback receipt is not the running source's current receipt.");
            _owner.RejectPendingPitchReadback(this);
            _owner.RejectPendingTimeReadback(this);
            try
            {
                // This identity/fence check performs no root getter or audio
                // sample. Actual reads retain VerifyCurrent and real readback.
                if (_writer is null) _execution.VerifyCurrent(); else _writer.VerifyCurrent();
                _owner.Open();
                if (!ReferenceEquals(_owner._current, this))
                    throw new UnsupportedSignatureException("The running source changed its readback receipt during native fence verification.");
            }
            catch (Exception error) { _owner.Fail(error); throw; }
        }
        internal bool ReadOperationPhysicalExit()
        {
            VerifyOperationIdentity();
            var exited = _writer?.RootPhysicalExited ?? _execution.PhysicalExited;
            _owner.Open();
            if (!ReferenceEquals(_owner._current, this))
                throw new UnsupportedSignatureException("The running source changed its receipt while reading actual physical exit.");
            return exited;
        }
        internal (NativePlaybackRateSample Sample, double AfterDsp) Read()
        {
            using var operation = _owner.EnterOperation();
            return _owner.ReadVerifiedReceipt(this, _writer?.SchedulingSequence ?? _execution.SchedulingSequence);
        }
        internal (NativePlaybackRateSample Sample, double StartDsp) ReadScheduledBaseline()
        {
            if (ActualScheduleBits is not ulong bits)
                throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The actual completed stock schedule has no leaf parameter.");
            var startDsp = BitConverter.Int64BitsToDouble(unchecked((long)bits));
            var (sample, afterDsp) = Read();
            VerifyScheduledReadback(Role, startDsp, Sample.AudioPositionSeconds, sample, afterDsp);
            return (sample, startDsp);
        }
        internal NativePlaybackStockOccurrenceProducer.ContinuationReceipt WriteNative(NativePlaybackStockOccurrenceProducer producer, float pitch)
        {
            using var operation = _owner.EnterOperation();
            VerifyCurrent();
            if (!ReferenceEquals(_owner._producer, producer)) throw new UnsupportedSignatureException("Another producer cannot consume this readback receipt.");
            if (_owner._freshAdoption is { } fresh)
                return fresh.WritePitch(_owner, producer, _execution, _writer, pitch);
            return _writer is null ? producer.WritePitch(_execution, pitch) : _writer.WritePitch(pitch);
        }
        internal ReadbackReceipt Succeed(NativePlaybackStockRootedSource owner, NativePlaybackStockOccurrenceProducer.ContinuationReceipt writer,
            NativePlaybackRateSample sample, double after, object issuer)
        {
            if (!ReferenceEquals(owner, _owner) || !ReferenceEquals(owner._issuer, issuer))
                throw new UnsupportedSignatureException("Only the actual rooted owner can issue its writer readback successor.");
            return Issue(owner, _execution, writer, BeforeDsp, sample, after, issuer);
        }
    }
}
