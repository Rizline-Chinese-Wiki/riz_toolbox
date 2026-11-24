namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackSourceInstallGroup
    {
        private long _fullOperationAudits, _clockOperationChecks, _rateOperationAdmissions;
        internal (long FullAudits, long ClockChecks, long RateAdmissions, long SourceChecks, long FullImageAudits) ReadOperationDiagnostics()
        {
            VerifyOperationThread();
            // Faulted and withdrawn groups retain their record and native
            // responsibility. Reading counters grants no running authority.
            if (!OwnsPlaybackGroup(this))
                throw new UnsupportedSignatureException("Operation diagnostics require the actual retained group record.");
            return (Interlocked.Read(ref _fullOperationAudits), Interlocked.Read(ref _clockOperationChecks),
                Interlocked.Read(ref _rateOperationAdmissions), Interlocked.Read(ref _sourceOperationChecks),
                NativePlaybackExecutableSeal.ReadThreadFullImageAudits());
        }
        // The running operation uses the registered members it actually needs.
        // Install, adoption and explicit inspection still use VerifyCurrent.
        internal (NativePlaybackSingleReturnAuthority? Authority, bool Active) ReadOperationClockPolicy(
            NativePlaybackGetTimeHookLease hook)
        {
            VerifyOperationThread();
            if (!ReferenceEquals(_getTimeLease, hook))
                throw new UnsupportedSignatureException("The operation does not own this group's exact GetTime lease.");
            Interlocked.Increment(ref _clockOperationChecks);
            lock (_verifyGate)
            lock (Gate)
            {
                VerifyOperationGroupOwnership();
                try
                {
                    var policy = _getTime!.ReadOperationPolicy(this);
                    VerifyOperationGroupOwnership();
                    return policy;
                }
                catch (Exception error) { Fault("The running GetTime channel lost its authority: " + error.Message); throw; }
            }
        }

        internal void RequireRateOperationAdmission()
            => RequireValueOperationAdmission(NativePlaybackSourceRole.SetPitch);

        internal void RequireSeekOperationAdmission()
            => RequireValueOperationAdmission(NativePlaybackSourceRole.SetTime);

        private void RequireValueOperationAdmission(NativePlaybackSourceRole writerRole)
        {
            VerifyOperationThread();
            Interlocked.Increment(ref _rateOperationAdmissions);
            lock (_verifyGate)
            lock (Gate)
            {
                VerifyOperationGroupOwnership();
                if (writerRole == NativePlaybackSourceRole.SetTime && !HasLowerTime)
                    throw new UnsupportedSignatureException("Seek admission requires the actual SetTime and lower-time pair.");
                ulong clockInFlight;
                try
                {
                    SourceOwner(NativePlaybackSourceRole.SetPitch).VerifyRateWriterChannel(this, _hub!);
                    if (writerRole == NativePlaybackSourceRole.SetTime)
                    {
                        SourceOwner(NativePlaybackSourceRole.SetTime).VerifyOperationChannel(this, _hub!);
                        SourceOwner(NativePlaybackSourceRole.LowerSetTime).VerifyOperationChannel(this, _hub!);
                    }
                    Interlocked.Increment(ref _clockOperationChecks);
                    clockInFlight = _getTime!.ReadRateOperationClockInFlight(this);
                }
                catch (Exception error) { Fault("The running value channels lost their authority: " + error.Message); throw; }
                // Refuse a Clock borrower already present before capture. This
                // is a conservative preflight, not a lock across the operation:
                // later native publication still owns its actual in-flight fence.
                if (clockInFlight != 0)
                    throw new UnsupportedSignatureException("An actual GetTime call is still returning; a new value operation is unavailable.");
                // Pending initialization and backing transitions may also
                // decline this command without faulting the installation.
                _hub!.RequireOperationFreshAdmission();
                VerifyOperationGroupOwnership();
            }
        }

        internal void PublishOperationClockPolicy(NativePlaybackGetTimeHookLease hook, long expectedRevision,
            NativePlaybackSingleReturnAuthority next)
        {
            VerifyOperationThread();
            if (!ReferenceEquals(_getTimeLease, hook))
                throw new UnsupportedSignatureException("The running Clock publisher owns another exact GetTime lease.");
            ArgumentNullException.ThrowIfNull(next);
            lock (_verifyGate)
            lock (Gate)
            {
                VerifyOperationGroupOwnership();
                try
                {
                    _getTime!.PublishOperationPolicy(this, expectedRevision, next);
                    VerifyOperationGroupOwnership();
                }
                catch (Exception error) { Fault("The running Clock publication lost its authority: " + error.Message); throw; }
            }
        }

        private void VerifyOperationThread()
        {
            if (_operationThread != Environment.CurrentManagedThreadId)
                throw new UnsupportedSignatureException("Playback operations belong to the actual group owner thread.");
        }

        private void VerifyOperationGroupOwnership()
        {
            if (!IsActive) throw new UnsupportedSignatureException("The private playback group is dormant, withdrawn or sticky faulted.");
            VerifySealIssuer(Module);
            _image!.VerifyOperationModule(); _lowerImage?.VerifyOperationModule();
            _foundation?.VerifyRuntimeOwnership();
            if (_hub!.ReadOperationHealth() is not { Known: true, Armed: true, InFlight: <= long.MaxValue })
                throw new UnsupportedSignatureException("The running source hub is unarmed or its native activity is unknown.");
        }
    }
}
