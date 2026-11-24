using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackSourceInstallGroup
    {
        internal enum InstallationStepKind
        {
            Initialize, GetTime, NativeFamily, Source, Configure, CommitGetTime, CommitSource, CommitNativeFamily, CommitAndArm, Withdraw
        }

        private sealed record Step(InstallationStepKind Kind, Action Run, NativePlaybackSourceRole? SourceRole = null);

        internal sealed class InstallationResponsibility
        {
            internal InstallationStepKind Kind { get; }
            internal NativePlaybackSourceRole? SourceRole { get; }
            internal bool Completed { get; private set; }

            internal InstallationResponsibility(InstallationStepKind kind, NativePlaybackSourceRole? role)
                => (Kind, SourceRole) = (kind, role);

            internal void Complete() => Completed = true;
        }

        private readonly List<InstallationResponsibility> _installationResponsibilities = new();
        private volatile bool _preparationRetired;
        private volatile Preparation? _currentPreparation;
        private bool _armAttempted;

        internal InstallationResponsibility RecordInstallationResponsibility(InstallationStepKind kind,
            NativePlaybackSourceRole? sourceRole = null)
        {
            if (!Monitor.IsEntered(Gate) || !Monitor.IsEntered(_verifyGate) || !OwnsPlaybackGroup(this))
                throw new UnsupportedSignatureException("Playback installation responsibility belongs to the locked registered group.");
            var record = new InstallationResponsibility(kind, sourceRole);
            _installationResponsibilities.Add(record);
            return record;
        }

        internal void AbandonPreparation(string? failure)
        {
            if (_preparationRetired || _installing || Volatile.Read(ref _active) != 0 || _adopted != 0
                || _installationResponsibilities.Count == 0 || _armAttempted)
                throw new UnsupportedSignatureException("Only an installation that has never attempted to arm can abandon dormant preparation.");
            var record = RecordInstallationResponsibility(InstallationStepKind.Withdraw);
            // Committed entries still call their retained originals. No Arm or
            // policy publication has occurred, so withdrawal needs no native
            // call on a possibly changed image. All physical owners stay rooted.
            if (failure is not null) Interlocked.CompareExchange(ref _fault, failure, null);
            _preparationRetired = true;
            Volatile.Write(ref _withdrawn, 1);
            record.Complete();
        }

        private void RunInstallation(Action step)
        {
            if (_installing || _operationThread != Environment.CurrentManagedThreadId
                || !Monitor.IsEntered(Gate) || !Monitor.IsEntered(_verifyGate)
                || Volatile.Read(ref _active) != 0 || Volatile.Read(ref _withdrawn) != 0
                || Volatile.Read(ref _fault) is not null)
                throw new UnsupportedSignatureException("Playback installation requires the original locked dormant group.");
            _installing = true;
            try
            {
                RequireNoInstallationAudit();
                step();
            }
            finally { _installing = false; }
        }

        private void PrepareGetTime()
        {
            _installationTrace?.Invoke("initial group image validation entering.");
            VerifyInstallationEvidence();
            _installationTrace?.Invoke("initial validation complete; GetTime prepare entering.");
            if (_adopted == 0) _getTime!.PrepareInstall();
        }

        private void PrepareNativeFamily()
        {
            if (_nativePitchPair is not null)
            {
                _installationTrace?.Invoke("native family prepare entering.");
                var lower = SourceOwner(NativePlaybackSourceRole.LowerSetPitch);
                var native = SourceOwner(NativePlaybackSourceRole.EngineSetPitch);
                lower.ReserveNativePairChannel(); native.ReserveNativePairChannel();
                if (_targets.HasLowerTime)
                {
                    var time = SourceOwner(NativePlaybackSourceRole.LowerSetTime);
                    time.ReserveNativePairChannel();
                    _nativePitchPair.Prepare(lower.EntryAddress, native.EntryAddress, time.EntryAddress);
                    lower.AdoptNativePairMember(_nativePitchPair.IssueSourceMember(lower));
                    native.AdoptNativePairMember(_nativePitchPair.IssueSourceMember(native));
                    time.AdoptNativePairMember(_nativePitchPair.IssueSourceMember(time));
                }
                else
                {
                    _nativePitchPair.Prepare(lower.EntryAddress, native.EntryAddress);
                    lower.AdoptNativePairMember(_nativePitchPair.IssueSourceMember(lower));
                    native.AdoptNativePairMember(_nativePitchPair.IssueSourceMember(native));
                }
            }
            _installationTrace?.Invoke("native family prepare complete.");
        }

        private void PrepareSource(PlaybackSourceOwner owner)
        {
            _installationTrace?.Invoke("source prepare entering: " + owner.Role);
            owner.PrepareInstall();
        }

        private void ConfigurePreparedSources()
        {
            if (_targets.HasLowerPitch) _hub!.ConfigureLowerPitch(
                _sources[NativePlaybackSourceRole.SetPitch].ConfiguredChannel,
                _sources[NativePlaybackSourceRole.LowerSetPitch].ConfiguredChannel);
            if (_targets.HasEnginePitch) _hub!.ConfigureEnginePitch(
                _sources[NativePlaybackSourceRole.LowerSetPitch].ConfiguredChannel,
                _sources[NativePlaybackSourceRole.EngineSetPitch].ConfiguredChannel, _targets.EnginePitch!);
            if (_targets.EnginePitch?.ScheduledPitch is { } scheduledPitch) _hub!.ConfigureScheduledEnginePitch(
                _sources[NativePlaybackSourceRole.PlayScheduled].ConfiguredChannel,
                _sources[NativePlaybackSourceRole.EngineSetPitch].ConfiguredChannel, scheduledPitch);
            if (_targets.HasLowerTime) _hub!.ConfigureLowerTime(
                SourceOwner(NativePlaybackSourceRole.SetTime).ConfiguredChannel,
                SourceOwner(NativePlaybackSourceRole.LowerSetTime).ConfiguredChannel);
        }

        private void CommitPreparedSources()
        {
            foreach (var step in CommitSteps(acrossFrames: false)) step.Run();
        }

        private IEnumerable<Step> CommitSteps(bool acrossFrames)
        {
            if (!_publishedDormantPlay) yield return new(InstallationStepKind.CommitGetTime, CommitGetTime);
            foreach (var owner in _sources.Values.Where(value => !value.IsNativePitchPairMember && !value.IsCommitted))
                if (!acrossFrames || !RequiresFinalArmFrame(owner.Role))
                    yield return new(InstallationStepKind.CommitSource, () => CommitSource(owner), owner.Role);
            if (_nativePitchPair is not null)
                yield return new(InstallationStepKind.CommitNativeFamily, CommitNativeFamily);
            yield return new(InstallationStepKind.CommitAndArm, acrossFrames ? CommitWrappersAndArm : ArmPreparedSources);
        }

        private bool RequiresFinalArmFrame(NativePlaybackSourceRole role)
            => _nativePitchPair is not null && (role == NativePlaybackSourceRole.SetPitch
                || _targets.HasLowerTime && role == NativePlaybackSourceRole.SetTime);

        private void CommitGetTime()
        {
            _installationTrace?.Invoke("source preparation complete; precommit validation entering.");
            VerifyInstallationEvidence();
            if (_adopted == 0) { _getTime!.CommitInstall(); VerifyInstallationEvidence(); }
        }

        private void CommitSource(PlaybackSourceOwner owner)
        {
            _installationTrace?.Invoke("source commit entering: " + owner.Role);
            owner.CommitInstall();
            VerifyInstallationEvidence();
        }

        private void CommitNativeFamily()
        {
            CommitSource(SourceOwner(NativePlaybackSourceRole.LowerSetPitch));
            CommitSource(SourceOwner(NativePlaybackSourceRole.EngineSetPitch));
            if (_targets.HasLowerTime) CommitSource(SourceOwner(NativePlaybackSourceRole.LowerSetTime));
        }

        private void CommitWrappersAndArm()
        {
            // A configured wrapper expects a matching native child even
            // before Arm. Keep the public wrappers pristine until the frame
            // that installs them and enables the source's receiver watch.
            foreach (var owner in _sources.Values.Where(value => RequiresFinalArmFrame(value.Role)))
                CommitSource(owner);
            ArmPreparedSources();
        }

        private void ArmPreparedSources()
        {
            if (_sources.Count != _targets.SourceRoleCount || _sources.Values.Any(value => !value.IsCommitted || !value.ConfiguredChannel.IsConfigured))
                throw new UnsupportedSignatureException("The source members and GetTime did not finish all physical commits.");
            _installationTrace?.Invoke("all commits complete; final arm and validation entering.");
            // A final Arm on a later frame requires new full evidence after
            // every previously committed entry and its original have run.
            VerifyInstallationEvidence();
            RequireNoInstallationAudit();
            _armAttempted = true;
            _getTime!.ArmInstall();
            _hub!.Arm();
            VerifyInstallationEvidence(); _installing = false; Volatile.Write(ref _active, 1);
            VerifyCurrent(); _getTimeLease ??= NativePlaybackGetTimeHookLease.Issue(_getTime);
            _hub.ConfigureInitialization(StockLeafCaller(NativePlaybackSourceRole.ProgressStartMoveNext, NativePlaybackSourceRole.SetClip));
            _getTime.BindFreshSource(_hub);
            if (_targets.HasPlayerConstructor)
            {
                _hub.ConfigurePlayerWatch(_targets.GetTime.DeclaringClass, _targets.PitchInstance.DeclaringClass);
                _hub.ConfigureProgressWatch(_targets.ProgressPlay.DeclaringClass, _targets.ProgressStartMoveNext.DeclaringClass);
                if (_targets.HasProgressResource) _hub.ConfigureProgressResource();
                if (_targets.HasDirectAudioControls)
                    _hub.ConfigureStockStop(StockLeafCaller(NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.AudioStop),
                        StockLeafCaller(NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.AudioStop),
                        _targets.ReplayScheduled.MethodInfo, _targets.ResumeScheduled.MethodInfo);
            }
        }

        internal sealed class Preparation : IDisposable
        {
            private readonly Preparation _registered;
            private readonly PlaybackSourceInstallGroup _group;
            private readonly IMainThreadDispatcher _main;
            private readonly int _managedThread = Environment.CurrentManagedThreadId;
            private readonly uint _osThread = NativeTweenSourceReferences.CurrentThreadId();
            private readonly Step[] _steps;
            private readonly Step[] _commitSteps;
            private int _next;
            private int _nextCommit;
            private volatile bool _closed;
            private bool _insideStep;

            internal Preparation(PlaybackSourceInstallGroup group, IMainThreadDispatcher main)
            {
                if (!Monitor.IsEntered(group.InstallationGate) || !Monitor.IsEntered(Gate)
                    || group._currentPreparation is not null || group._preparationRetired || group.IsActive
                    || group._installationResponsibilities.Count != 1
                    || group._installationResponsibilities[0] is not { Kind: InstallationStepKind.Initialize, Completed: true })
                    throw new UnsupportedSignatureException("A playback group issues one exact preparation under its original broker locks.");
                (_registered, _group, _main) = (this, group, main);
                _steps = Steps().ToArray();
                _commitSteps = group.CommitSteps(acrossFrames: true).ToArray();
                group._currentPreparation = this;
            }

            private IEnumerable<Step> Steps()
            {
                yield return new(InstallationStepKind.GetTime, _group.PrepareGetTime);
                if (_group._nativePitchPair is not null)
                    yield return new(InstallationStepKind.NativeFamily, _group.PrepareNativeFamily);
                foreach (var owner in _group._sources.Values.Where(owner => !owner.IsNativePitchPairMember && !owner.IsCommitted))
                    yield return new(InstallationStepKind.Source, () => _group.PrepareSource(owner), owner.Role);
                yield return new(InstallationStepKind.Configure, _group.ConfigurePreparedSources);
            }

            internal bool IsOpen => !_closed;
            internal bool IsPrepared => IsOpen && _next == _steps.Length;
            internal bool WaitingForDormantPlay { get; private set; }

            internal void PrepareNext()
            {
                VerifyOpen();
                if (IsPrepared) throw new UnsupportedSignatureException("Playback preparation has already finished its dormant steps.");
                var step = _steps[_next];
                Run(() =>
                {
                    // A new frame cannot borrow the prior step's successful
                    // validation. GetTime includes the first complete audit.
                    if (_next != 0) _group.VerifyInstallationEvidence();
                    var responsibility = _group.RecordInstallationResponsibility(step.Kind, step.SourceRole);
                    step.Run();
                    responsibility.Complete();
                    ++_next;
                });
            }

            internal PlaybackSourceGroupLease? CommitNext()
            {
                VerifyOpen();
                if (!IsPrepared) throw new UnsupportedSignatureException("All playback preparation steps must finish before the physical commit.");
                var step = _commitSteps[_nextCommit];
                PlaybackSourceGroupLease? lease = null;
                Run(() =>
                {
                    void Commit()
                    {
                        var responsibility = _group.RecordInstallationResponsibility(step.Kind, step.SourceRole);
                        step.Run();
                        responsibility.Complete();
                    }
                    if (step.Kind == InstallationStepKind.CommitAndArm && _group._publishedDormantPlay)
                    {
                        WaitingForDormantPlay = !_group.TryCommitWithDormantPlay(Commit);
                        if (WaitingForDormantPlay) return;
                    }
                    else Commit();
                    if (step.Kind == InstallationStepKind.CommitAndArm)
                        lease = PlaybackSourceGroupLease.Issue(_group);
                    ++_nextCommit;
                    if (lease is not null) Close();
                });
                return lease;
            }

            private void Run(Action step)
            {
                lock (_group.InstallationGate)
                lock (Gate)
                {
                    _insideStep = true;
                    try { _group.RunInstallation(step); }
                    catch (Exception error)
                    {
                        try
                        {
                            if (_group._armAttempted)
                            {
                                Close();
                                _group.Fault("Playback common installation failed and remains retained: " + error.Message);
                            }
                            else _group.AbandonPreparation(error.Message);
                        }
                        finally { Close(); }
                        throw;
                    }
                    finally { _insideStep = false; }
                }
            }

            private void VerifyOwner()
            {
                _main.VerifyAccess();
                if (!ReferenceEquals(this, _registered) || !ReferenceEquals(this, _group._currentPreparation)
                    || _managedThread != Environment.CurrentManagedThreadId
                    || _osThread != NativeTweenSourceReferences.CurrentThreadId())
                    throw new UnsupportedSignatureException("Playback preparation belongs to its original managed and OS thread.");
                if (_insideStep) throw new UnsupportedSignatureException("A playback installation step cannot be interrupted or reentered.");
            }

            private void VerifyOpen()
            {
                VerifyOwner();
                if (_closed) throw new UnsupportedSignatureException("This playback preparation no longer owns another step.");
            }

            public void Dispose()
            {
                VerifyOwner();
                if (_closed) return;
                lock (_group.InstallationGate)
                lock (Gate)
                {
                    _group.AbandonPreparation(failure: null);
                    Close();
                }
            }

            private void Close()
            {
                _closed = true;
                _group._installationTrace = null;
            }
        }
    }
}
