namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    // The original Play body may already have a LOCAL physical owner when the
    // optional stock clock service is first requested.
    internal PlaybackSourceGroupTargets? ReadDormantPlaybackTargets(IntPtr module)
    {
        _main.VerifyAccess();
        if (_engine is not DobbyDetourEngine dobby) return null;
        lock (Gate)
        {
            var group = PlaybackGroups.Values.SingleOrDefault(candidate => candidate.Module == module && candidate.HasDormantPlay);
            return group?.ReadDormantTargets(dobby);
        }
    }

    internal static bool TryVerifyDormantProgressPlayBody(IntPtr module, IntPtr address, int length, string sha256)
    {
        if (module == IntPtr.Zero || address != IntPtr.Add(module, 0x400700) || length != 272
            || !string.Equals(sha256, "b3514fac283f1f83e31092b96e98c904fc1fe2821bbe37de731fbf530b65b3b3", StringComparison.OrdinalIgnoreCase))
            return false;
        lock (Gate)
        {
            if (!PlaybackSourceInstallations.TryGetValue(address, out var owner) || !owner.Group.HasDormantPlay) return false;
            owner.Group.VerifyDormantPlay();
            return owner.MemberModule == module && owner.OriginalBodyLength == length
                && owner.OriginalBodySha256.Equals(sha256, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void InstallDormantLocalPlay(VerifiedHookTarget localPlay, PlaybackSourceGroupTargets targets,
        string sourceHelper, string sourceHelperHash, NativeFirstInstall.PlaybackMembership? foundation)
    {
        if (!Monitor.IsEntered(Gate) || _engine is not DobbyDetourEngine dobby)
            throw new UnsupportedSignatureException("A dormant LOCAL Play requires its original locked Dobby broker.");
        targets.VerifyCurrent();
        var stockPlay = targets.ProgressPlay;
        if (localPlay.Address != stockPlay.Address || localPlay.MethodInfo != stockPlay.MethodInfo
            || localPlay.DeclaringClass != stockPlay.DeclaringClass || localPlay.Abi != ExactHookAbi.LocalProgressPlayBooleanVoid
            || localPlay.ReturnClass != IntPtr.Zero || Entries.ContainsKey(localPlay.Address)
            || PlaybackSourceInstallations.ContainsKey(localPlay.Address)
            || PlaybackGroups.Values.Any(group => group.Module == targets.Module))
            throw new UnsupportedSignatureException("The dormant LOCAL Play does not identify an unclaimed fixed stock entry.");
        if (!targets.OwnedFixture && foundation is null)
            throw new UnsupportedSignatureException("Stock dormant Play needs its actual first-install membership.");
        if (foundation is not null)
        {
            foundation.VerifyCurrent();
            if (foundation.Module != targets.Module ||
                !foundation.ImageSha256.Equals(targets.ImageSha256, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("Dormant LOCAL Play requires its exact foundation image.");
        }
        var sequence = Interlocked.Increment(ref _playbackGroupEpoch);
        if (sequence <= 0) throw new UnsupportedSignatureException("Playback group epochs are exhausted.");
        var group = new PlaybackSourceInstallGroup((ulong)sequence, targets, foundation);
        if (!PlaybackGroups.TryAdd(group.Epoch, group))
            throw new UnsupportedSignatureException("The dormant LOCAL Play lost its process installation slot.");
        try
        {
            lock (group.InstallationGate)
            {
                Entries.Add(localPlay.Address, SourceGroupEntry(stockPlay, dobby, ownsProgressPlay: true));
                group.InstallDormantPlay(dobby, localPlay, sourceHelper, sourceHelperHash);
            }
        }
        catch (Exception error) { group.Fault("Dormant LOCAL Play installation failed and remains retained: " + error.Message); throw; }
    }

    internal sealed partial class PlaybackSourceInstallGroup
    {
        private bool _publishedDormantPlay, _publishingDormantPlay;
        private Action<NativePlaybackSourceObservation>? _dormantStockBefore;
        private Action<NativePlaybackSourceCompletion>? _dormantStockAfter;
        private Action<NativePlaybackSourceFault>? _dormantStockFault;
        internal bool PublishingDormantPlay => _publishingDormantPlay;
        internal bool HasDormantPlay => _publishedDormantPlay && Volatile.Read(ref _active) == 0
            && Volatile.Read(ref _withdrawn) == 0 && Volatile.Read(ref _fault) is null;

        internal void InstallDormantPlay(DobbyDetourEngine engine, VerifiedHookTarget localPlay, string sourceHelper, string sourceHelperHash)
        {
            if (!Monitor.IsEntered(Gate) || !Monitor.IsEntered(InstallationGate) || _engine is not null
                || _sources.Count != 0 || _hub is not null || _getTime is not null)
                throw new UnsupportedSignatureException("Dormant LOCAL Play publishes once under the actual group gates.");
            VerifySealIssuer(Module);
            _publishingDormantPlay = true;
            try
            {
                _engine = engine;
                _hub = new(sourceHelper, sourceHelperHash, fault => _dormantStockFault?.Invoke(fault), fault => Fault(fault.Reason));
                if (!_hub.SupportsDormantPlay)
                    throw new UnsupportedSignatureException("The installed playback source helper cannot observe dormant LOCAL Play.");
                _targets.VerifyCurrent();
                var owner = new PlaybackSourceOwner(this, _targets, NativePlaybackSourceRole.ProgressPlay, engine, _hub,
                    observation =>
                    {
                        if (_dormantStockBefore is null) observation.DisposeOperation(); else _dormantStockBefore(observation);
                    }, completion => _dormantStockAfter?.Invoke(completion));
                _sources.Add(owner.Role, owner);
                if (!PlaybackSourceInstallations.TryAdd(owner.TargetAddress, owner))
                    throw new UnsupportedSignatureException("Another physical owner already retains the dormant LOCAL Play target.");
                owner.PrepareInstall();
                owner.VerifyDormantPlayBody(Module, localPlay);
                // Publish the phase-only observer before Dobby first exposes this
                // native entry. Even calls without a managed claimant will count.
                owner.PrepareLocalPlayObserver(dormant: true);
                owner.CommitInstall();
                owner.VerifyDormantPlayBody(Module, localPlay);
                _hub.VerifyDormantPlayPublication();
                _publishedDormantPlay = true;
            }
            finally { _publishingDormantPlay = false; }
        }

        internal void VerifyDormantPlayInstallation()
        {
            if (!_publishingDormantPlay || !Monitor.IsEntered(Gate) || !Monitor.IsEntered(InstallationGate)
                || _operationThread != Environment.CurrentManagedThreadId || _sources.Count != 1 || _getTime is not null)
                throw new UnsupportedSignatureException("The initial LOCAL Play write requires its sole exact dormant owner.");
            VerifySealIssuer(Module);
            _targets.VerifyCurrent();
            _foundation?.VerifyCurrent();
            SourceOwner(NativePlaybackSourceRole.ProgressPlay).VerifyMemberEvidence();
        }

        internal void VerifyDormantPlay()
        {
            if (!Monitor.IsEntered(Gate) || !HasDormantPlay)
                throw new UnsupportedSignatureException("A dormant LOCAL Play proof cannot be borrowed from another installation phase.");
            VerifySealIssuer(Module);
            SourceOwner(NativePlaybackSourceRole.ProgressPlay).VerifyOperationChannel(this, _hub!, requireArmed: false);
            _hub!.VerifyDormantPlayPublication();
        }

        internal PlaybackSourceGroupTargets ReadDormantTargets(DobbyDetourEngine requested)
        {
            if (_engine is not { } original || !original.SharesPlaybackTransport(requested)
                || _operationThread != Environment.CurrentManagedThreadId || _preparationRetired
                || _installationResponsibilities.Count != 0 || _getTime is not null)
                throw new UnsupportedSignatureException("The dormant LOCAL Play belongs to another transport or stock installation attempt.");
            VerifyDormantPlay();
            return _targets;
        }

        internal DobbyDetourEngine RequireDormantAdoption(PlaybackSourceGroupTargets targets, DobbyDetourEngine requested,
            string sourceHelper, string sourceHelperHash, NativeFirstInstall.PlaybackMembership? foundation)
        {
            if (!ReferenceEquals(ReadDormantTargets(requested), targets) || !ReferenceEquals(_foundation, foundation)
                || LocalProgressPlayReservations.ContainsKey(targets.ProgressPlay.Address))
                throw new UnsupportedSignatureException("The stock group cannot adopt a different or still preparing LOCAL Play member.");
            _hub!.VerifyDormantPlayHelper(sourceHelper, sourceHelperHash);
            return _engine!;
        }

        private bool TryCommitWithDormantPlay(Action finish)
        {
            _hub!.VerifyDormantPlayPublication();
            if (!_hub.TryBeginDormantPlayPromotion()) return false;
            Exception? failure = null;
            try
            {
                _armAttempted = true;
                CommitGetTime();
                finish();
            }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                try { _hub.EndDormantPlayPromotion(failure is null); }
                catch (Exception error) when (failure is not null)
                {
                    Fault("Closing the failed dormant Play promotion lost its native evidence: " + error.Message);
                }
            }
            return true;
        }
    }
}

