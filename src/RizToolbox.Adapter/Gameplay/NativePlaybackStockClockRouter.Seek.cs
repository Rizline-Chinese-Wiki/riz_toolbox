using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockClockRouter
{
    internal sealed partial class Session
    {
        internal INativePlaybackSeekSource CaptureSeekSource(NativePlaybackRateOrigin origin)
        {
            VerifyFormalClockOperationIdentity();
            if (!_formalProvider || origin != _owner._origin)
                throw new GameplayStateException(ErrorCode.TargetExpired, "Seek capture belongs to another formal provider, run or source.");
            return new SeekSource(this);
        }

        // Rate and Seek retain the same source/run/group/binding/adoption/route
        // identities and native Clock policy checks. Only the actual writer
        // and its independently read successor differ.
        private sealed class SeekSource : INativePlaybackSeekSource
        {
            private readonly Session _session;
            private readonly SeekSource _registered;
            private readonly RateSource _roots;
            private NativePlaybackStockRootedSource.TimeWrite? _time;
            private bool _disposed;
            internal SeekSource(Session session)
            {
                _session = session; _registered = this;
                _roots = new RateSource(session, seek: true);
            }
            public NativePlaybackBinding Binding => _roots.Binding;
            public NativePlaybackClock Clock => _roots.Clock;
            public GameEventState.PlaybackRunAssociation Run => _roots.Run;
            public void VerifyAuthority()
            {
                VerifyOwner();
                if (_disposed) throw new ObjectDisposedException(nameof(SeekSource));
                _roots.VerifyAuthority();
            }
            public NativePlaybackRateSample Read()
            {
                VerifyAuthority();
                var value = _roots.Read();
                VerifyAuthority(); return value;
            }
            public INativePlaybackTimeWrite PrepareTimeWrite()
            {
                VerifyOwner();
                if (_time is not null) throw new GameplayStateException(ErrorCode.Conflict, "This source already prepared its time writer.");
                VerifyAuthority();
                _time = _roots.PrepareTimeWrite();
                return new TimeWrite(this, _time);
            }
            public void Dispose()
            {
                VerifyOwner();
                if (_disposed) return;
                // A busy refusal leaves the actual operation and preparation
                // roots registered for cleanup after the writer unwinds.
                _roots.Dispose(); _time = null; _disposed = true;
            }
            private void VerifyOwner()
            {
                _session._owner.VerifyRegisteredOwner();
                if (_session._owner._thread != Environment.CurrentManagedThreadId)
                    throw new GameplayStateException(ErrorCode.WrongThread, "The Seek source belongs to its owner thread.");
                if (!ReferenceEquals(_registered, this))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "A copied Seek source owns no retained roots or writer.");
            }
            private sealed class TimeWrite : INativePlaybackTimeWrite
            {
                private readonly SeekSource _source;
                private readonly TimeWrite _registered;
                private readonly NativePlaybackStockRootedSource.TimeWrite _writer;
                private bool _disposed;
                internal TimeWrite(SeekSource source, NativePlaybackStockRootedSource.TimeWrite writer)
                    => (_source, _registered, _writer) = (source, this, writer);
                public void VerifyCurrent()
                {
                    VerifyOwner();
                    if (_disposed) throw new ObjectDisposedException(nameof(TimeWrite));
                    _source.VerifyAuthority();
                    if (!ReferenceEquals(_source._time, _writer))
                        throw new GameplayStateException(ErrorCode.TargetExpired, "The private time writer lost its exact source registration.");
                    _source._roots.VerifyTimeWriter(_writer);
                }
                public void Write(float mediaSeconds)
                {
                    VerifyCurrent();
                    try { _writer.Write(mediaSeconds); }
                    finally { _source._roots.ObserveWriterReadbackIdentity(); }
                    VerifyCurrent();
                }
                public void RestoreOriginal()
                {
                    VerifyCurrent();
                    try { _writer.RestoreOriginal(); }
                    finally { _source._roots.ObserveWriterReadbackIdentity(); }
                    VerifyCurrent();
                }
                public void Dispose()
                {
                    VerifyOwner();
                    if (_disposed) return;
                    _source._roots.ReleaseTimeWriter(_writer); _disposed = true;
                }
                private void VerifyOwner()
                {
                    _source.VerifyOwner();
                    if (!ReferenceEquals(_registered, this))
                        throw new GameplayStateException(ErrorCode.TargetExpired, "A copied time wrapper owns no Seek source or writer.");
                }
            }
        }
    }
}
