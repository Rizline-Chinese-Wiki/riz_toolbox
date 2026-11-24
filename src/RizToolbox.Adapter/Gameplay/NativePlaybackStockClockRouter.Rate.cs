using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockClockRouter
{
    internal sealed partial class Session
    {
        internal INativePlaybackRateSource CaptureRateSource(NativePlaybackRateOrigin origin)
        {
            VerifyRateOperationIdentity();
            if (origin != _owner._origin)
                throw new GameplayStateException(ErrorCode.TargetExpired, "Rate capture belongs to another exact backend/run or an unarmed Clock session.");
            return new RateSource(this);
        }

        private void VerifyRateOperationIdentity()
        {
            Registered();
            if (!_armed || _finalClock is null || _publication is null
                || !ReferenceEquals(_owner._binding, _initial.Binding)
                || _owner._handoff is not null || _owner.InitializationPending)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The exact armed playback session no longer owns its run/source binding.");
            _owner._run.VerifyCurrent(_initial.Binding);
            if (_finalClock.Capture().Binding != _initial.Binding)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The final playback Clock belongs to another source.");
        }

        private void VerifyRateOperationPolicy()
        {
            VerifyRateOperationIdentity();
            if (_formalProvider) VerifyFormalClockOperationIdentity();
            if (!_publication!.IsCurrentOperationPolicy(_finalClock!.Capture()))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The final Clock no longer owns its exact native publication.");
            VerifyRateOperationIdentity();
        }

        internal void VerifyFormalRateAuthority()
        {
            Registered(); VerifyFormalClockAuthority();
            VerifyFormalRateSourceAuthority();
            _owner.Current().VerifyCurrent();
            VerifyFormalClockAuthority();
        }

        /// <summary>A fresh value transaction holds a read responsibility over
        /// the same original seven roots. Transferred sources and historical
        /// fixtures retain independent accessors; none releases physical hooks.</summary>
        private sealed class RateSource : INativePlaybackRateSource, INativePlaybackReleaseGuard
        {
            private readonly Session _session;
            private readonly RateSource _registered;
            private readonly NativePlaybackStockRootedSource _rooted;
            private readonly GameEventState.PlaybackRunAssociation _run;
            private readonly PlaybackSourceGroupLease _group;
            private readonly NativePlaybackBinding _binding;
            private readonly NativePlaybackRateOrigin _origin;
            private readonly NativePlaybackClock _clock;
            private readonly NativePlaybackFreshSourceCapture.Adoption? _adoption;
            private readonly object? _sourceRoute;
            private readonly NativePlaybackActualSource? _actual;
            private readonly IDisposable? _freshRead;
            private NativePlaybackStockRootedSource.ReadbackReceipt _receipt;
            private NativePlaybackStockRootedSource.PitchWrite? _pitchWriter;
            private NativePlaybackStockRootedSource.TimeWrite? _timeWriter;
            private readonly bool _seek;
            private bool _disposed;
            internal RateSource(Session session, bool seek = false)
            {
                _session = session; _registered = this; _seek = seek;
                session.VerifyRateOperationIdentity();
                _group = session._owner._group ?? throw new GameplayStateException(ErrorCode.TargetExpired, "The current source group is unavailable.");
                // One admission for this captured operation. Nested wrappers
                // retain identity and current receipts; native reads/writes
                // independently verify their actual graphs and fences.
                if (seek) _group.RequireSeekOperationAdmission(); else _group.RequireRateOperationAdmission();
                _rooted = session._owner._source ?? throw new GameplayStateException(ErrorCode.TargetExpired, "The registered rooted source is unavailable.");
                _run = session._owner._run; _binding = session._initial.Binding;
                _origin = session._owner._origin; _clock = session.Clock;
                _adoption = session._owner._freshAdoption; _sourceRoute = session._owner._sourceRoute;
                _receipt = session._owner.ObserveCurrent();
                try
                {
                    if (_adoption is { } fresh) _freshRead = fresh.RetainRateSource(session);
                    else _actual = new(session._owner._api, _binding, _origin.Pause);
                    VerifyAuthority();
                }
                catch { try { _actual?.Dispose(); } finally { _freshRead?.Dispose(); } throw; }
            }
            public NativePlaybackBinding Binding => _binding;
            public NativePlaybackClock Clock => _clock;
            internal GameEventState.PlaybackRunAssociation Run => _run;
            public void VerifyAuthority()
            {
                VerifyOwner();
                if (_disposed) throw new ObjectDisposedException(nameof(RateSource));
                VerifyIdentity();
                VerifyReceiptAuthority();
                _session.VerifyRateOperationPolicy();
                // Policy/native checks do not lend their success to the next
                // getter. Finish on the same registered source and receipt.
                VerifyIdentity(); VerifyReceiptAuthority();
            }
            public NativePlaybackRateSample Read()
            {
                VerifyAuthority();
                var current = _receipt;
                NativePlaybackRateSample observed;
                try
                {
                    observed = _pitchWriter is { } writer ? writer.Read()
                        : _timeWriter is { } time ? time.Read() : current.Read().Sample;
                }
                finally { ObserveWriterReadbackIdentity(); }
                current = _receipt;
                var result = _actual is { } actual
                    ? actual.Read(observed.AuthorityRevision, observed.SchedulingRevision) : observed;
                VerifyAuthority();
                if (!ReferenceEquals(_receipt, current) || result.Binding != Binding
                    || (result with { DspSeconds = observed.DspSeconds }) != observed || result.DspSeconds < observed.DspSeconds)
                    throw new GameplayStateException(ErrorCode.Conflict, "Independent preparation roots/API no longer match the exact stock readback baseline.");
                return result;
            }
            public INativePlaybackPitchWrite PreparePitchWrite()
            {
                VerifyAuthority();
                if (_seek || _pitchWriter is not null || _timeWriter is not null)
                    throw new GameplayStateException(ErrorCode.Conflict, "This captured rate source already has a prepared pitch writer.");
                var receipt = _receipt;
                if (!receipt.ReadOperationPhysicalExit() || receipt.Role is not (NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay))
                    throw new GameplayStateException(ErrorCode.NotReady, "A continuous writer requires the actual completed Pause receipt.");
                _pitchWriter = _rooted.PreparePausedPitchWrite(receipt, VerifyPitchOperationSourceIdentity);
                _receipt = _pitchWriter.ObserveReadbackIdentity();
                try { VerifyAuthority(); }
                catch (Exception error)
                {
                    var preparing = _pitchWriter; _pitchWriter = null;
                    try { preparing.Dispose(); }
                    catch (Exception cleanup)
                    {
                        error.Data["RatePreparedWriterCleanupOwner"] = preparing;
                        error.Data["RatePreparedWriterCleanupFailure"] = cleanup;
                    }
                    throw;
                }
                return new PitchWrite(this, _pitchWriter);
            }
            private void VerifyPitchOperationSourceIdentity()
            {
                VerifyOwner();
                if (_disposed || _seek)
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The registered rate source ended its private pitch operation.");
                if (_pitchWriter is { } pitch) _receipt = pitch.ObserveReadbackIdentity();
                VerifyIdentity(); _session.VerifyRateOperationPolicy(); VerifyIdentity();
            }
            internal NativePlaybackStockRootedSource.TimeWrite PrepareTimeWrite()
            {
                VerifyAuthority();
                if (!_seek || _timeWriter is not null || _pitchWriter is not null)
                    throw new GameplayStateException(ErrorCode.Conflict, "This captured source has no free private time writer.");
                _timeWriter = _rooted.PreparePausedTimeWrite(VerifyTimeOperationSourceIdentity);
                _receipt = _timeWriter.ObserveReadbackIdentity();
                try { VerifyTimeWriter(_timeWriter); }
                catch (Exception error)
                {
                    var preparing = _timeWriter; _timeWriter = null;
                    try { preparing.Dispose(); }
                    catch (Exception cleanup)
                    {
                        error.Data["SeekPreparedWriterCleanupOwner"] = preparing;
                        error.Data["SeekPreparedWriterCleanupFailure"] = cleanup;
                    }
                    throw;
                }
                return _timeWriter;
            }
            private void VerifyTimeOperationSourceIdentity()
            {
                VerifyOwner();
                if (_disposed || !_seek)
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The registered Seek source ended its private time operation.");
                // A successor comes only from this same actual writer. During
                // pending API readback its original receipt stays unreadable;
                // the writer independently checks its native continuation.
                if (_timeWriter is { } time) _receipt = time.ObserveReadbackIdentity();
                VerifyIdentity(); _session.VerifyRateOperationPolicy(); VerifyIdentity();
            }
            internal void VerifyTimeWriter(NativePlaybackStockRootedSource.TimeWrite writer)
            {
                VerifyAuthority();
                if (!_seek || !ReferenceEquals(_timeWriter, writer))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The time writer is not owned by this captured Seek source.");
                writer.VerifyOperationIdentity();
            }
            internal void ObserveWriterReadbackIdentity()
            {
                VerifyOwner();
                if (_pitchWriter is { } pitch) _receipt = pitch.ObserveReadbackIdentity();
                else if (_timeWriter is { } time) _receipt = time.ObserveReadbackIdentity();
            }
            internal void ReleaseTimeWriter(NativePlaybackStockRootedSource.TimeWrite writer)
            {
                VerifyOwner();
                writer.Dispose();
                if (ReferenceEquals(_timeWriter, writer)) _timeWriter = null;
            }
            public void VerifyReleaseAllowed()
            {
                VerifyOwner();
                if (_disposed) return;
                _pitchWriter?.VerifyReleaseAllowed();
            }
            public void Dispose()
            {
                // Rejected worker/copy cleanup cannot consume another owner's
                // retained read responsibility, even after the route has closed.
                VerifyReleaseAllowed();
                if (_disposed) return;
                // Refused busy time cleanup leaves its retained preparation
                // roots available until the actual writer has unwound.
                _timeWriter?.Dispose(); _timeWriter = null;
                _disposed = true;
                try { _pitchWriter?.Dispose(); }
                finally
                {
                    _pitchWriter = null;
                    try { _actual?.Dispose(); }
                    finally { _freshRead?.Dispose(); }
                }
            }
            private void VerifyReceiptAuthority()
            {
                if (_pitchWriter is { } writer)
                {
                    writer.VerifyOperationIdentity();
                    if (!ReferenceEquals(writer.ObserveReadbackIdentity(), _receipt))
                        throw new GameplayStateException(ErrorCode.TargetExpired, "The current pitch writer owns another source receipt.");
                }
                else if (_timeWriter is { } time)
                {
                    time.VerifyOperationIdentity();
                    if (!ReferenceEquals(time.ObserveReadbackIdentity(), _receipt))
                        throw new GameplayStateException(ErrorCode.TargetExpired, "The current time writer owns another source receipt.");
                }
                else _receipt.VerifyOperationIdentity();
            }
            private void VerifyOwner()
            {
                if (_session._owner._thread != Environment.CurrentManagedThreadId)
                    throw new GameplayStateException(ErrorCode.WrongThread, "The prepared playback source belongs to its owner thread.");
                if (!ReferenceEquals(_registered, this))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "A copied rate source owns no roots or read responsibility.");
            }
            private void VerifyIdentity()
            {
                _session.VerifyRateOperationIdentity();
                _run.VerifyCurrent(_binding);
                var owner = _session._owner;
                if (!ReferenceEquals(owner._source, _rooted) || !ReferenceEquals(owner._run, _run)
                    || !ReferenceEquals(owner._group, _group) || !ReferenceEquals(owner._binding, _binding)
                    || !ReferenceEquals(owner._origin, _origin) || !ReferenceEquals(owner._sourceRoute, _sourceRoute)
                    || !ReferenceEquals(owner._freshAdoption, _adoption) || !ReferenceEquals(_session.Clock, _clock)
                    || !ReferenceEquals(owner.ObserveCurrent(), _receipt))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The captured operation's exact run, source, receipt or Clock registration changed.");
                _adoption?.VerifyOperationIdentity(owner, _rooted, owner._api, _run, _group, _binding, _origin);
            }
            private sealed class PitchWrite : INativePlaybackPitchWrite, INativePlaybackPitchRecovery, INativePlaybackReleaseGuard
            {
                private readonly RateSource _source;
                private readonly PitchWrite _registered;
                private readonly NativePlaybackStockRootedSource.PitchWrite _writer;
                private bool _disposed;
                internal PitchWrite(RateSource source, NativePlaybackStockRootedSource.PitchWrite writer)
                    => (_source, _writer, _registered) = (source, writer, this);
                public void VerifyCurrent()
                {
                    VerifyOwner();
                    if (_disposed) throw new ObjectDisposedException(nameof(PitchWrite));
                    _source.VerifyAuthority();
                    if (!ReferenceEquals(_source._pitchWriter, _writer))
                        throw new GameplayStateException(ErrorCode.TargetExpired, "This writer no longer owns its captured source receipt.");
                    _writer.VerifyOperationIdentity();
                }
                public void Write(float pitch)
                {
                    VerifyCurrent();
                    if (!NativePlaybackClock.ValidRate(pitch))
                        throw new GameplayStateException(ErrorCode.InvalidArgument, "The native pitch writer requires a positive finite Single playback rate.");
                    try { _writer.Write(pitch); }
                    finally { _source._receipt = _writer.ObserveReadbackIdentity(); }
                    VerifyCurrent();
                }
                public void RestoreOriginal()
                {
                    VerifyCurrent();
                    try { _writer.RestoreOriginal(); }
                    finally { _source._receipt = _writer.ObserveReadbackIdentity(); }
                    VerifyCurrent();
                }
                public void VerifyReleaseAllowed()
                {
                    VerifyOwner();
                    if (_disposed) return;
                    _writer.VerifyReleaseAllowed();
                }
                public void Dispose()
                {
                    VerifyReleaseAllowed();
                    if (_disposed) return;
                    // Source.Dispose may already have released this same real
                    // writer. Its own once-only owner check preserves that case
                    // without letting an old wrapper clear a newer registration.
                    _writer.Dispose();
                    if (ReferenceEquals(_source._pitchWriter, _writer)) _source._pitchWriter = null;
                    _disposed = true;
                }
                private void VerifyOwner()
                {
                    _source.VerifyOwner();
                    if (!ReferenceEquals(_registered, this))
                        throw new GameplayStateException(ErrorCode.TargetExpired, "A copied writer owns no prepared source receipt.");
                }
            }
        }
    }
}
