namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal sealed partial class PlaybackSourceInstallGroup
    {
        private bool _installing;
        private long _installationGeneration;
        private long _installationAudit;
        private VerificationTraversal? _installationTraversal;
        // Used only by the containing broker before publishing an adopted
        // owner. Taking a monitor grants no image or installation authority.
        internal object InstallationGate => _verifyGate;

        // Every invocation is a fresh read-only interval. Completion draining
        // stays outside it, as do reservation, configuration, Prepare, Commit
        // and Arm. No ticket or successful check reaches the next interval.
        private void VerifyInstallationEvidence(bool members = true, NativePlaybackExecutableSeal? onlyImage = null,
            PlaybackSourceOwner? committingSource = null)
        {
            lock (_verifyGate)
            lock (Gate)
            {
                VerifyInstallationContext();
                RequireNoInstallationAudit();
                foreach (var owner in _sources.Values) owner.PumpCompleted();
                void ReadOnlyAudit()
                {
                    using var traversal = VerificationTraversal.CreateInstallation(this);
                    if (onlyImage is not null) onlyImage.VerifyForGroupTraversal(traversal);
                    else
                    {
                        Image.VerifyForGroupTraversal(traversal);
                        _lowerImage?.VerifyForGroupTraversal(traversal);
                    }
                    if (committingSource is not null) committingSource.VerifyMemberEvidenceForGroup(this, traversal);
                    else if (members) VerifyMembers(traversal);
                }
                // The existing V5 verifier retains complete native/file/image
                // checks at both ends. Nested reads retain all live publication,
                // source, relay, RF, graph and Kernel checks. This interval has
                // no pump, observer callback or physical write.
                (_lowerImage ?? Image).VerifyInstallationRead(this, ReadOnlyAudit);
            }
        }
        internal void VerifySourceBeforeCommit(PlaybackSourceOwner owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            // This fresh interval brackets both images and the exact source.
            // Its exit must pass before the physical commit can begin.
            if (_publishingDormantPlay && ReferenceEquals(owner, SourceOwner(NativePlaybackSourceRole.ProgressPlay)))
                VerifyDormantPlayInstallation();
            else VerifyInstallationEvidence(members: false, committingSource: owner);
        }
        internal bool TryVerifyInstallationImage(NativePlaybackExecutableSeal image)
        {
            if (!_installing) return false;
            if (!ReferenceEquals(image, _image) && !ReferenceEquals(image, _lowerImage))
                throw new UnsupportedSignatureException("An installation image read cannot borrow another retained image.");
            VerifyInstallationEvidence(members: false, onlyImage: image);
            return true;
        }
        private void VerifyInstallationContext()
        {
            if (!_installing || _operationThread != Environment.CurrentManagedThreadId
                || !System.Threading.Monitor.IsEntered(Gate) || !System.Threading.Monitor.IsEntered(_verifyGate)
                || Volatile.Read(ref _active) != 0 || Volatile.Read(ref _withdrawn) != 0)
                throw new UnsupportedSignatureException("Installation verification requires this locked dormant installation on its original thread.");
            VerifySealIssuer(Module);
        }
        private long BeginInstallationAudit()
        {
            VerifyInstallationContext(); RequireNoInstallationAudit();
            if (_installationGeneration == long.MaxValue)
                throw new UnsupportedSignatureException("Installation verification generations are exhausted.");
            _installationAudit = ++_installationGeneration;
            return _installationAudit;
        }
        private void VerifyInstallationAudit(long audit, long generation)
        {
            VerifyInstallationContext();
            if (audit <= 0 || audit != _installationAudit || audit != _installationGeneration
                || generation != _referenceCodeGeneration)
                throw new UnsupportedSignatureException("Installation verification left its one read-only interval or broker generation.");
        }
        internal void RequireNoInstallationAudit()
        {
            if (_installationAudit != 0 || _installationTraversal is not null)
            {
                _installationTraversal?.RejectInstallationWrite();
                throw new UnsupportedSignatureException("A physical write or completion pump cannot run inside installation verification.");
            }
            if (_preparationRetired)
                throw new UnsupportedSignatureException("The dormant playback preparation was withdrawn or faulted before commit.");
            if (_currentPreparation is { IsOpen: true } && !IsActive
                && (_operationThread != Environment.CurrentManagedThreadId || !Monitor.IsEntered(Gate)
                    || !Monitor.IsEntered(_verifyGate) || !_installing))
                throw new UnsupportedSignatureException("The dormant playback group can write or pump only inside its locked preparation step.");
        }
    }
    internal sealed partial class PlaybackOwner
    {
        // Keep an already-issued standalone GetTime reader outside the entire
        // group adoption. Its normal lock order starts with this exact gate.
        internal object GroupInstallationGate => _gate;
    }
}
