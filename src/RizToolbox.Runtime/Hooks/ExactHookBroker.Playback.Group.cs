namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, PlaybackSourceInstallGroup> PlaybackGroups = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, PlaybackSourceOwner> PlaybackSourceInstallations = new();
    private static long _playbackGroupEpoch;
    internal (long Prepared, long Committed) ReadActualPlaybackDobbyCallCounts()
        => _engine is DobbyDetourEngine dobby ? dobby.ActualPreparedCallCounts
            : throw new UnsupportedSignatureException("A synthetic engine has no actual Dobby call counters.");

    internal PlaybackSourceGroupLease BindPlaybackSourceGroup(PlaybackSourceGroupTargets targets,
        string getTimeHelperPath, string getTimeHelperSha256, string sourceHelperPath, string sourceHelperSha256,
        Func<NativePlaybackSingleReturnSample> readClock, Action<NativePlaybackSingleReturnFault> getTimeFault,
        Action<NativePlaybackSourceFault> sourceFault, Action<NativePlaybackSourceObservation>? sourceBefore = null,
        Action<NativePlaybackSourceCompletion>? sourceAfter = null, Action<NativePlaybackSingleReturnFrame>? getTimeBefore = null,
        Action<NativePlaybackSingleReturnFrame, NativePlaybackSingleReturnCompletion>? getTimeAfter = null,
        NativePlaybackGetTimeHookLease? existingGetTime = null, NativeFirstInstall.PlaybackMembership? foundation = null)
        => OpenPlaybackSourceGroup(targets, getTimeHelperPath, getTimeHelperSha256, sourceHelperPath, sourceHelperSha256,
            readClock, getTimeFault, sourceFault, sourceBefore, sourceAfter, getTimeBefore, getTimeAfter,
            existingGetTime, foundation, prepareAcrossFrames: false).Lease!;

    internal PlaybackSourceInstallGroup.Preparation BeginPlaybackSourceGroupPreparation(PlaybackSourceGroupTargets targets,
        string getTimeHelperPath, string getTimeHelperSha256, string sourceHelperPath, string sourceHelperSha256,
        Func<NativePlaybackSingleReturnSample> readClock, Action<NativePlaybackSingleReturnFault> getTimeFault,
        Action<NativePlaybackSourceFault> sourceFault, Action<NativePlaybackSourceObservation>? sourceBefore = null,
        Action<NativePlaybackSourceCompletion>? sourceAfter = null, NativeFirstInstall.PlaybackMembership? foundation = null)
        => OpenPlaybackSourceGroup(targets, getTimeHelperPath, getTimeHelperSha256, sourceHelperPath, sourceHelperSha256,
            readClock, getTimeFault, sourceFault, sourceBefore, sourceAfter, null, null, null, foundation,
            prepareAcrossFrames: true).Pending!;

    private (PlaybackSourceGroupLease? Lease, PlaybackSourceInstallGroup.Preparation? Pending) OpenPlaybackSourceGroup(
        PlaybackSourceGroupTargets targets, string getTimeHelperPath, string getTimeHelperSha256,
        string sourceHelperPath, string sourceHelperSha256, Func<NativePlaybackSingleReturnSample> readClock,
        Action<NativePlaybackSingleReturnFault> getTimeFault, Action<NativePlaybackSourceFault> sourceFault,
        Action<NativePlaybackSourceObservation>? sourceBefore, Action<NativePlaybackSourceCompletion>? sourceAfter,
        Action<NativePlaybackSingleReturnFrame>? getTimeBefore,
        Action<NativePlaybackSingleReturnFrame, NativePlaybackSingleReturnCompletion>? getTimeAfter,
        NativePlaybackGetTimeHookLease? existingGetTime, NativeFirstInstall.PlaybackMembership? foundation, bool prepareAcrossFrames)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(targets); ArgumentNullException.ThrowIfNull(readClock);
        ArgumentNullException.ThrowIfNull(getTimeFault); ArgumentNullException.ThrowIfNull(sourceFault);
        _log.Log(RizToolbox.Sdk.LogLevel.Debug, "Playback installation: group target validation entering.");
        targets.VerifyCurrent();
        if (foundation is not null)
        {
            foundation.VerifyCurrent();
            if (foundation.Module != targets.Module || !foundation.ImageSha256.Equals(targets.ImageSha256, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The private foundation member belongs to another pinned playback image.");
            var bodies = targets.Roles.Select(role =>
                (Address: targets.SourceTarget(role).Address, Length: targets.SourceBodyLength(role)))
                .Append((Address: targets.GetTime.Address, Length: targets.GetTimeBodyLength)).ToArray();
            foreach (var range in foundation.ImageRanges())
                if (bodies.Any(body => body.Address.ToInt64() < range.Address.ToInt64() + range.Length
                    && range.Address.ToInt64() < body.Address.ToInt64() + body.Length))
                    throw new UnsupportedSignatureException("A private foundation source write overlaps a closed stock source body.");
        }
        if (_engine is not DobbyDetourEngine dobby)
            throw new UnsupportedSignatureException("Playback source groups require the actual retained Dobby Prepare/Commit owner.");
        lock (Gate)
        {
            if (prepareAcrossFrames && existingGetTime is not null)
                throw new UnsupportedSignatureException("An existing GetTime owner must be adopted in one synchronous installation.");
            var adopted = existingGetTime?.OwnerForGroupAdoption();
            var retained = PlaybackGroups.Values.SingleOrDefault(value => value.Module == targets.Module);
            var adoptingDormantPlay = retained is not null && prepareAcrossFrames && retained.HasDormantPlay;
            if (retained is not null && !adoptingDormantPlay
                || targets.Roles.Any(role => (!adoptingDormantPlay || role != NativePlaybackSourceRole.ProgressPlay)
                    && (Entries.ContainsKey(targets.SourceTarget(role).Address)
                        || LocalProgressPlayReservations.ContainsKey(targets.SourceTarget(role).Address)
                        || PlaybackSourceInstallations.ContainsKey(targets.SourceTarget(role).Address)))
                || adopted is null && (Entries.ContainsKey(targets.GetTime.Address) || PlaybackInstallations.ContainsKey(targets.GetTime.Address))
                || adopted is not null && (adopted.TargetAddress != targets.GetTime.Address || adopted.MethodInfo != targets.GetTime.MethodInfo
                    || adopted.Module != targets.Module || adopted.Group is not null || !adopted.HasEngine(dobby)))
                throw new UnsupportedSignatureException("A playback group address is already retained or its adopted GetTime owner differs.");
            PlaybackSourceInstallGroup group;
            if (adoptingDormantPlay)
            {
                group = retained!;
                dobby = group.RequireDormantAdoption(targets, dobby, sourceHelperPath, sourceHelperSha256, foundation);
            }
            else
            {
                var sequence = Interlocked.Increment(ref _playbackGroupEpoch);
                if (sequence <= 0) throw new UnsupportedSignatureException("Playback group epochs are exhausted.");
                group = new PlaybackSourceInstallGroup((ulong)sequence, targets, foundation);
                if (!PlaybackGroups.TryAdd(group.Epoch, group))
                    throw new UnsupportedSignatureException("The playback group epoch already retains another installation owner.");
            }
            group._installationTrace = text => _log.Log(RizToolbox.Sdk.LogLevel.Debug, "Playback installation: " + text);
            var pendingOwnsTrace = false;
            try
            {
                // Take the new private verifier gate before Initialize can
                // publish this group through an adopted GetTime owner. Other
                // readers then cannot hold it while waiting for broker Gate.
                lock (adopted?.GroupInstallationGate ?? group.InstallationGate)
                lock (group.InstallationGate)
                {
                    var initializing = prepareAcrossFrames
                        ? group.RecordInstallationResponsibility(PlaybackSourceInstallGroup.InstallationStepKind.Initialize)
                        : null;
                    if (adopted is null) Entries.Add(targets.GetTime.Address, SourceGroupEntry(targets.GetTime, dobby));
                    foreach (var role in targets.Roles)
                    {
                        if (adoptingDormantPlay && role == NativePlaybackSourceRole.ProgressPlay) continue;
                        var target = targets.SourceTarget(role);
                        Entries.Add(target.Address, SourceGroupEntry(target, dobby, role == NativePlaybackSourceRole.ProgressPlay));
                    }
                    group.Initialize(dobby, getTimeHelperPath, getTimeHelperSha256, sourceHelperPath, sourceHelperSha256,
                        readClock, getTimeFault, sourceFault, sourceBefore, sourceAfter, getTimeBefore, getTimeAfter, adopted, existingGetTime);
                    if (initializing is not null)
                    {
                        initializing.Complete();
                        var pending = new PlaybackSourceInstallGroup.Preparation(group, _main);
                        pendingOwnsTrace = true;
                        return (null, pending);
                    }
                    group.Install();
                    return (PlaybackSourceGroupLease.Issue(group), null);
                }
            }
            catch (Exception error)
            {
                if (prepareAcrossFrames)
                {
                    lock (group.InstallationGate) group.AbandonPreparation(error.Message);
                }
                else group.Fault("Playback common installation failed and remains retained: " + error.Message);
                throw;
            }
            finally { if (!pendingOwnsTrace) group._installationTrace = null; }
        }
    }
    private static Entry SourceGroupEntry(VerifiedHookTarget target, DobbyDetourEngine engine, bool ownsProgressPlay = false) => new()
    { Target = target, Engine = engine, Replacement = (Action)(() => { }), RetainedStockProgressPlay = ownsProgressPlay,
        NativeInstallFailure = "Private retained playback source group owns this address." };
    internal static bool OwnsPlaybackGroup(PlaybackSourceInstallGroup group)
    { return PlaybackGroups.TryGetValue(group.Epoch, out var actual) && ReferenceEquals(actual, group); }
    internal static bool OwnsPlaybackSourceInstallation(PlaybackSourceOwner owner)
    { return PlaybackSourceInstallations.TryGetValue(owner.TargetAddress, out var actual) && ReferenceEquals(actual, owner); }

    internal sealed partial class PlaybackSourceInstallGroup
    {
        // Cleared after installation; recurring verification emits no markers.
        internal Action<string>? _installationTrace;
        private readonly object _verifyGate = new();
        private readonly int _operationThread = Environment.CurrentManagedThreadId;
        private readonly PlaybackSourceGroupTargets _targets;
        private readonly NativeFirstInstall.PlaybackMembership? _foundation;
        private ReferenceImageMembers? _referenceMembers;
        private readonly Dictionary<NativePlaybackSourceRole, PlaybackSourceOwner> _sources = new();
        private PlaybackOwner? _getTime;
        private NativePlaybackStockSourceHub? _hub;
        private NativePlaybackExecutableSeal? _image;
        private NativePlaybackExecutableSeal? _lowerImage;
        private DobbyDetourEngine? _engine;
        private NativePlaybackEnginePitchPatchPair? _nativePitchPair;
        private NativePlaybackGetTimeHookLease? _getTimeLease;
        private int _active, _withdrawn, _adopted;
        private bool _withdrawalComplete;
        private string? _fault;
        internal ulong Epoch { get; }
        internal ulong FoundationEpoch => _foundation?.Epoch ?? 0;
        internal void VerifyReceivers(NativeRuntime api, IntPtr player, IntPtr source, IntPtr progress, IntPtr getTimeMethod)
        {
            lock (_verifyGate)
            lock (Gate)
            {
                if (!IsActive) throw new UnsupportedSignatureException("The private playback group is dormant, withdrawn or sticky faulted.");
                System.Runtime.ExceptionServices.ExceptionDispatchInfo? projectionFailure = null;
                try
                {
                    VerifySealIssuer(Module);
                    using var traversal = VerificationTraversal.Create(this);
                    VerifyCurrentBody(traversal);
                    // The fixed ObjectClass/MethodInfo projection invokes no
                    // gameplay getter. Keep it inside this one actual traversal;
                    // no successful check or admission escapes to its caller.
                    traversal.VerifyIssuer(this);
                    try { VerifyReceiverProjection(api, player, source, progress, getTimeMethod); }
                    catch (Exception error) { projectionFailure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
                    traversal.VerifyIssuer(this);
                }
                catch (Exception error) { Fault("The common playback group lost its physical authority: " + error.Message); throw; }
                // A wrong receiver was not a physical-group fault before this
                // projection was combined. Finish the full traversal exit first,
                // then preserve that original failure outside the Fault catch.
                projectionFailure?.Throw();
            }
        }
        private void VerifyReceiverProjection(NativeRuntime api, IntPtr player, IntPtr source, IntPtr progress, IntPtr getTimeMethod)
        {
            if (api.ObjectClass(player) != _targets.GetTime.DeclaringClass || getTimeMethod != _targets.GetTime.MethodInfo
                || new[] { NativePlaybackSourceRole.Pause, NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.Resume }
                    .Any(role => _targets.SourceTarget(role).DeclaringClass != api.ObjectClass(player))
                || new[] { NativePlaybackSourceRole.SetPitch, NativePlaybackSourceRole.PlayScheduled,
                    NativePlaybackSourceRole.PauseLeaf, NativePlaybackSourceRole.SetClip, NativePlaybackSourceRole.SetTime }
                    .Any(role => _targets.SourceTarget(role).DeclaringClass != api.ObjectClass(source))
                || _targets.HasDirectAudioControls && new[] { NativePlaybackSourceRole.AudioPlayHelper, NativePlaybackSourceRole.AudioPlayDelayed, NativePlaybackSourceRole.AudioPlay, NativePlaybackSourceRole.AudioStop, NativePlaybackSourceRole.AudioStopBoolean, NativePlaybackSourceRole.AudioSetLoop }
                    .Any(role => _targets.SourceTarget(role).DeclaringClass != api.ObjectClass(source))
                || _targets.SourceTarget(NativePlaybackSourceRole.ProgressPlay).DeclaringClass != api.ObjectClass(progress)
                || _targets.SourceTarget(NativePlaybackSourceRole.ProgressMusicPlayer).DeclaringClass != api.ObjectClass(progress)
                || _targets.HasProgressResource && _targets.ProgressClip.DeclaringClass != api.ObjectClass(progress)
                || _targets.HasPlayerConstructor && _targets.SourceTarget(NativePlaybackSourceRole.PlayerConstructor).DeclaringClass != api.ObjectClass(player))
                throw new UnsupportedSignatureException("The independently rooted object classes/GetTime method differ from the actual source group's metadata.");
        }
        internal NativePlaybackStockSourceHub.WriterReservation ReservePitchWriter(NativePlaybackSourceObservation root,
            IntPtr source, uint bits, ulong writer, ulong sourceFence, ulong scheduling,
            NativePlaybackStockSourceHub.WriterReservation? predecessor)
        {
            VerifyCurrent();
            if (root.Role is not (NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay
                or NativePlaybackSourceRole.Replay or NativePlaybackSourceRole.Resume))
                throw new UnsupportedSignatureException("Only a closed stock root can reserve this group's pitch writer.");
            var pitch = _targets.SourceTarget(NativePlaybackSourceRole.SetPitch);
            return _hub!.ReserveWriter(root, source, pitch.Address, pitch.MethodInfo, bits, writer, sourceFence, scheduling, predecessor);
        }
        internal NativePlaybackStockSourceHub.WriterReservation ReserveTimeWriter(NativePlaybackSourceObservation root,
            IntPtr source, uint bits, ulong writer, ulong sourceFence, ulong scheduling,
            NativePlaybackStockSourceHub.WriterReservation? predecessor)
        {
            VerifyCurrent();
            if (!_targets.HasLowerTime || root.Role is not (NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay))
                throw new UnsupportedSignatureException("Only an actual paused root with the registered lower-time pair can reserve time.");
            var time = _targets.SourceTarget(NativePlaybackSourceRole.SetTime);
            return _hub!.ReserveTimeWriter(root, source, time.Address, time.MethodInfo, bits, writer, sourceFence, scheduling, predecessor);
        }
        internal IntPtr Module => _targets.Module;
        internal bool OwnedFixture => _targets.OwnedFixture;
        internal bool HasLowerPitch => _targets.HasLowerPitch;
        internal bool HasEnginePitch => _targets.HasEnginePitch;
        internal bool HasScheduledEnginePitch => _targets.EnginePitch?.ScheduledPitch is not null;
        internal bool HasLowerTime => _targets.HasLowerTime;
        internal IntPtr LowerModule => _targets.LowerPitch?.Module ?? IntPtr.Zero;
        internal string LowerImageSha256 => _targets.LowerPitch?.ImageSha256
            ?? throw new UnsupportedSignatureException("No exact lower image issuer.");
        internal void VerifyImages()
        {
            if (_publishingDormantPlay) VerifyDormantPlayInstallation();
            else if (_installing) VerifyInstallationEvidence(members: false);
            else { Image.Verify(); _lowerImage?.Verify(); }
        }
        internal IEnumerable<NativePlaybackSourceRole> SourceRoles => _targets.Roles;
        internal string ImageSha256 => _targets.ImageSha256;
        internal NativePlaybackExecutableSeal Image => _image ?? throw new UnsupportedSignatureException("The playback group has no complete pinned image yet.");
        internal bool IsActive => Volatile.Read(ref _active) == 1 && Volatile.Read(ref _withdrawn) == 0 && Volatile.Read(ref _fault) is null;
        internal NativePlaybackGetTimeHookLease GetTimeLease => _getTimeLease!;
        internal PlaybackSourceOwner SourceOwner(NativePlaybackSourceRole role) => _sources.TryGetValue(role, out var owner)
            ? owner : throw new UnsupportedSignatureException("The group has no such private physical source member.");
        internal PlaybackSourceInstallGroup(ulong epoch, PlaybackSourceGroupTargets targets, NativeFirstInstall.PlaybackMembership? foundation)
            => (Epoch, _targets, _foundation) = (epoch, targets, foundation);
        internal void Initialize(DobbyDetourEngine engine, string getTimeHelper, string getTimeHelperHash,
            string sourceHelper, string sourceHelperHash, Func<NativePlaybackSingleReturnSample> readClock,
            Action<NativePlaybackSingleReturnFault> getTimeFault, Action<NativePlaybackSourceFault> sourceFault,
            Action<NativePlaybackSourceObservation>? sourceBefore, Action<NativePlaybackSourceCompletion>? sourceAfter,
            Action<NativePlaybackSingleReturnFrame>? getTimeBefore,
            Action<NativePlaybackSingleReturnFrame, NativePlaybackSingleReturnCompletion>? getTimeAfter,
            PlaybackOwner? adopted, NativePlaybackGetTimeHookLease? adoptedLease)
        {
            _installationTrace?.Invoke("group member preflight entering.");
            VerifySealIssuer(Module); _targets.VerifyCurrent();
            _referenceMembers = ReferenceImageMembers.Capture(Module);
            _engine = engine;
            _getTime = adopted ?? PlaybackOwner.PreflightGroup(this, _targets.GetTime, Module, ImageSha256,
                _targets.GetTimeBodyLength, _targets.GetTimeBodySha256, engine, getTimeHelper, getTimeHelperHash,
                readClock, getTimeFault, getTimeBefore, getTimeAfter);
            if (adopted is not null)
            {
                if (adopted.OriginalBodyLength != _targets.GetTimeBodyLength
                    || !adopted.OriginalBodySha256.Equals(_targets.GetTimeBodySha256, StringComparison.OrdinalIgnoreCase))
                    throw new UnsupportedSignatureException("The adopted GetTime private owner has a different complete original body pin.");
                adopted.AdoptIntoGroup(this); _adopted = 1; _getTimeLease = adoptedLease;
            }
            else if (!PlaybackInstallations.TryAdd(_getTime.TargetAddress, _getTime))
                throw new UnsupportedSignatureException("The group GetTime address already retains another installation owner.");
            if (_publishedDormantPlay)
            {
                _dormantStockBefore = sourceBefore;
                _dormantStockAfter = sourceAfter;
                _dormantStockFault = sourceFault;
                VerifyDormantPlay();
            }
            else _hub = new NativePlaybackStockSourceHub(sourceHelper, sourceHelperHash, sourceFault, fault => Fault(fault.Reason));
            foreach (var role in _targets.Roles)
            {
                if (_publishedDormantPlay && role == NativePlaybackSourceRole.ProgressPlay) continue;
                _installationTrace?.Invoke("source preflight entering: " + role);
                var owner = new PlaybackSourceOwner(this, _targets, role, engine, _hub!, sourceBefore, sourceAfter);
                _sources.Add(role, owner);
                if (!PlaybackSourceInstallations.TryAdd(owner.TargetAddress, owner))
                    throw new UnsupportedSignatureException("The source address already retains another installation owner.");
            }
            // Recheck the shared targets after capturing all source-local pins and before preparing any hook.
            _targets.VerifyCurrent();
            if (_targets.HasEnginePitch)
            {
                _installationTrace?.Invoke("native family preflight entering.");
                _nativePitchPair = _targets.HasLowerTime
                    ? NativePlaybackEnginePitchPatchPair.CreateForGroup(_targets.EnginePitch!, _targets.LowerTime!, engine, this)
                    : NativePlaybackEnginePitchPatchPair.CreateForGroup(_targets.EnginePitch!, engine, this);
                _installationTrace?.Invoke("native family preflight complete.");
            }
            _installationTrace?.Invoke("GameAssembly image capture entering.");
            _image = NativePlaybackExecutableSeal.CaptureGroup(this);
            _installationTrace?.Invoke("GameAssembly image capture complete; lower image capture entering.");
            if (_targets.HasLowerPitch) _lowerImage = NativePlaybackExecutableSeal.CaptureLowerGroup(this);
            _installationTrace?.Invoke("group image capture complete.");
        }
        internal void Install()
        {
            if (_installationResponsibilities.Count != 0)
                throw new UnsupportedSignatureException("The registered staged preparation owns this group's installation.");
            RunInstallation(() =>
            {
                PrepareGetTime();
                PrepareNativeFamily();
                foreach (var owner in _sources.Values.Where(value => !value.IsNativePitchPairMember))
                    PrepareSource(owner);
                ConfigurePreparedSources();
                CommitPreparedSources();
            });
        }
        internal void VerifyCurrent()
        {
            lock (_verifyGate)
            lock (Gate)
            {
                if (!IsActive) throw new UnsupportedSignatureException("The private playback group is dormant, withdrawn or sticky faulted.");
                try
                {
                    VerifySealIssuer(Module);
                    using var traversal = VerificationTraversal.Create(this);
                    VerifyCurrentBody(traversal);
                }
                catch (Exception error) { Fault("The common playback group lost its physical authority: " + error.Message); throw; }
            }
        }
        private void VerifyCurrentBody(VerificationTraversal traversal)
        {
            Interlocked.Increment(ref _fullOperationAudits);
            traversal.VerifyIssuer(this);
            Image.VerifyForGroupTraversal(traversal); _lowerImage?.VerifyForGroupTraversal(traversal);
            VerifyMembers(traversal);
            if (_sources.Count != _targets.SourceRoleCount || _sources.Values.Any(value => !value.IsCommitted) || !_hub!.ReadHealth().Armed)
                throw new UnsupportedSignatureException("The full source group did not retain its one armed native source hub.");
        }
        private void VerifyMembers(VerificationTraversal? traversal = null)
        {
            // Shared authority is checked once per aggregate traversal, not
            // once per child. Each source still rechecks its own registration,
            // code, metadata, channel, trampoline and actual native health.
            if (traversal is null) { _targets.VerifyCurrent(); _foundation?.VerifyCurrent(); }
            else { traversal.VerifyIssuer(this); traversal.VerifySupportingIssuer(_targets); }
            if (traversal is null) _referenceMembers?.Verify();
            else traversal.VerifyReferenceMembers(this);
            _getTime!.VerifyMemberEvidence(traversal);
            if (traversal is null)
            {
                foreach (var owner in _sources.Values) owner.VerifyMemberEvidenceForGroup(this, null);
                return;
            }
            if (traversal.IsInstallation)
            {
                // Dormant channels can still be unreserved or unconfigured.
                // Keep every ordinary local/entry/proof/health check, but no
                // completion pump may cross this read-only interval.
                foreach (var owner in _sources.Values) owner.VerifyMemberEvidenceForGroup(this, traversal);
                return;
            }
            // One actual locked traversal checks every local member first.
            // The shared native helper then brackets only its read-only entry
            // queries. Completion pumps run after that bracket closes; the
            // info/owner query batch opens a fresh helper-image bracket.
            var owners = _sources.Values.ToArray();
            var channels = owners.Select(owner => owner.BeginMemberEvidenceForGroupBatch(this, traversal)).ToArray();
            var hub = _hub ?? throw new UnsupportedSignatureException("The source group lost its actual native helper owner.");
            if (channels.Any(channel => channel is null || !ReferenceEquals(channel.Hub, hub)
                    || !channel.IsConfigured)
                || channels.Select(channel => channel!.Slot).Distinct().Count() != owners.Length)
                throw new UnsupportedSignatureException("The active source group lost a distinct configured native channel.");
            var exactChannels = channels.Select(channel => channel!).ToArray();
            traversal.VerifyIssuer(this);
            hub.VerifyMemberReservationsForGroup(this, traversal, exactChannels);
            for (var index = 0; index < owners.Length; ++index)
                if (!ReferenceEquals(owners[index].CompleteMemberEvidenceForGroupBatch(this, traversal,
                        exactChannels[index]), exactChannels[index]))
                    throw new UnsupportedSignatureException("A source member lost its actual reserved native channel.");
            traversal.VerifyIssuer(this);
            hub.VerifyMemberEvidenceForGroup(this, traversal, exactChannels);
            traversal.VerifyIssuer(this);
        }
        internal void VerifyNativeHelperBatchIssuer(NativePlaybackStockSourceHub hub, VerificationTraversal traversal)
        {
            ArgumentNullException.ThrowIfNull(traversal);
            traversal.VerifyIssuer(this);
            if (!ReferenceEquals(_hub, hub))
                throw new UnsupportedSignatureException("A native helper batch cannot borrow another group's hub.");
        }
        // One explicit invocation owns both shared validations. This ticket is
        // never ambient or retained by the group, including for owned groups
        // without a foundation. Every independent traversal validates anew.
        internal sealed class VerificationTraversal : IDisposable
        {
            private readonly PlaybackSourceInstallGroup _group;
            private readonly VerificationTraversal _registered;
            private readonly PlaybackSourceGroupTargets _targets;
            private readonly NativeRuntime.NativePlaybackStockTargets? _stock;
            private readonly NativePlaybackLowerPitchTarget? _lower;
            private readonly NativePlaybackLowerPitchTarget.EnginePitchPreflight? _enginePitch;
            private readonly NativePlaybackLowerPitchTarget.LowerTimePreflight? _lowerTime;
            private readonly NativeFirstInstall.PlaybackMembership.VerificationScope? _foundation;
            private readonly DobbyDetourEngine _dobbyEngine;
            private readonly NativePlaybackExecutableSeal _dobbySeal;
            private readonly NativePlaybackStockSourceHub _sourceHub;
            private readonly NativePlaybackExecutableSeal _sourceHelperSeal;
            private readonly ReferenceImageMembers? _referenceMemberOwner;
            private readonly long _generation;
            private readonly long? _installation;
            internal bool IsInstallation => _installation.HasValue;
            private readonly int _thread;
            private readonly ReferenceImageMembers.Snapshot? _referenceSnapshot;
            private ReferenceImageMembers.Snapshot.CompositionSection? _referenceSection;
            private bool _referenceExitAttempted;
            private int _unknown;
            private bool _closed;
            private VerificationTraversal(PlaybackSourceInstallGroup group, long generation,
                NativeFirstInstall.PlaybackMembership.VerificationScope? foundation, long? installation = null)
            {
                (_group, _registered, _foundation, _generation, _thread) =
                    (group, this, foundation, generation, Environment.CurrentManagedThreadId);
                _installation = installation;
                if (installation.HasValue) group._installationTraversal = this;
                _referenceMemberOwner = group._referenceMembers;
                // Capture actual issuer references only after Create's full
                // entry validation succeeded. These are a bounded parent
                // responsibility, not a cache of successful member evidence.
                _targets = group._targets;
                (_stock, _lower, _enginePitch, _lowerTime) =
                    (_targets.StockIssuer, _targets.LowerPitch, _targets.EnginePitch, _targets.LowerTime);
                _dobbyEngine = group._engine
                    ?? throw new UnsupportedSignatureException("The active playback group lost its retained Dobby owner.");
                // This real engine owns one exact seal. Retain verifies its full
                // image at this traversal's entry, inside both group locks.
                _dobbySeal = _dobbyEngine.RetainPlaybackAuthority();
                VerifyIssuer(group);
                _sourceHub = group._hub
                    ?? throw new UnsupportedSignatureException("The active playback group lost its retained source helper.");
                _sourceHelperSeal = _sourceHub.RetainMemberImageEvidenceForGroup(group, this);
                // Capture the full ordinary entry before any image check can
                // fail. Dispose then owns its exit even before the first section.
                _referenceSnapshot = _referenceMemberOwner?.CaptureSnapshot();
            }
            internal static VerificationTraversal Create(PlaybackSourceInstallGroup group)
            {
                var generation = group.VerifyTraversalVerificationIssuer();
                var foundation = group._foundation?.BeginVerification(group);
                try
                {
                    group._targets.VerifyCurrent();
                    return new(group, generation, foundation);
                }
                catch { foundation?.Dispose(); throw; }
            }
            internal static VerificationTraversal CreateInstallation(PlaybackSourceInstallGroup group)
            {
                var installation = group.BeginInstallationAudit();
                NativeFirstInstall.PlaybackMembership.VerificationScope? foundation = null;
                try
                {
                    foundation = group._foundation?.BeginVerification(group, installation);
                    group._targets.VerifyCurrent();
                    return new(group, _referenceCodeGeneration, foundation, installation);
                }
                catch
                {
                    try { foundation?.Dispose(); }
                    finally { group._installationTraversal = null; group._installationAudit = 0; }
                    throw;
                }
            }
            internal void RejectInstallationWrite()
            {
                Interlocked.Exchange(ref _unknown, 1);
            }
            internal void VerifyIssuer(PlaybackSourceInstallGroup group)
            {
                try
                {
                    VerifyContext(group);
                    if (Volatile.Read(ref _unknown) != 0)
                        throw new UnsupportedSignatureException("The shared target traversal is sticky unknown.");
                    if (_installation.HasValue) group.VerifyInstallationAudit(_installation.Value, _generation);
                    else group.VerifyTraversalVerificationIssuer(_generation);
                    _foundation?.VerifyIssuer(group);
                }
                catch { Interlocked.Exchange(ref _unknown, 1); throw; }
            }
            internal void VerifySupportingIssuer(PlaybackSourceGroupTargets targets)
            {
                VerifySupportingLineage();
                if (!ReferenceEquals(targets, _targets)) RejectSupportingIssuer();
            }
            internal void VerifySupportingIssuer(NativeRuntime.NativePlaybackStockTargets stock)
            {
                VerifySupportingLineage();
                if (stock is null || !ReferenceEquals(stock, _stock)) RejectSupportingIssuer();
            }
            internal void VerifySupportingIssuer(NativePlaybackLowerPitchTarget lower)
            {
                VerifySupportingLineage();
                if (lower is null || !ReferenceEquals(lower, _lower)) RejectSupportingIssuer();
            }
            internal void VerifySupportingIssuer(NativePlaybackLowerPitchTarget.EnginePitchPreflight enginePitch)
            {
                VerifySupportingLineage();
                if (enginePitch is null || !ReferenceEquals(enginePitch, _enginePitch)) RejectSupportingIssuer();
            }
            internal void VerifySupportingIssuer(NativePlaybackLowerPitchTarget.LowerTimePreflight lowerTime)
            {
                VerifySupportingLineage();
                if (lowerTime is null || !ReferenceEquals(lowerTime, _lowerTime)) RejectSupportingIssuer();
            }
            internal void VerifySharedDobbySeal(PlaybackSourceInstallGroup group,
                DobbyDetourEngine engine, NativePlaybackExecutableSeal seal)
            {
                try
                {
                    VerifyIssuer(group);
                    if (!ReferenceEquals(engine, _dobbyEngine) || !ReferenceEquals(group._engine, engine)
                        || !ReferenceEquals(seal, _dobbySeal))
                        throw new UnsupportedSignatureException("A member cannot borrow another Dobby engine or executable seal.");
                }
                catch { Interlocked.Exchange(ref _unknown, 1); throw; }
            }
            internal void VerifyInstallationSourceHelper(NativePlaybackStockSourceHub hub, NativePlaybackExecutableSeal image)
            {
                try
                {
                    VerifyIssuer(_group);
                    if (!_installation.HasValue || !ReferenceEquals(hub, _sourceHub)
                        || !ReferenceEquals(_group._hub, hub) || !ReferenceEquals(image, _sourceHelperSeal))
                        throw new UnsupportedSignatureException("An installation getter cannot borrow another source helper seal or active traversal.");
                }
                catch { Interlocked.Exchange(ref _unknown, 1); throw; }
            }
            private void VerifySupportingLineage()
            {
                // Check the original one-call ticket before looking at any
                // supplied issuer. A nested/independent call owns a fresh ticket;
                // neither thread-local state nor retained success grants this.
                VerifyIssuer(_group);
                if (!ReferenceEquals(_group._targets, _targets)
                    || !ReferenceEquals(_targets.StockIssuer, _stock)
                    || !ReferenceEquals(_targets.LowerPitch, _lower)
                    || !ReferenceEquals(_targets.EnginePitch, _enginePitch)
                    || !ReferenceEquals(_targets.LowerTime, _lowerTime)
                    || _stock is not null && !_targets.IdentifiesStock(_stock)
                    || _lower is not null && (!ReferenceEquals(_lower.StockIssuer, _stock)
                        || _lower.WrapperModule != _targets.Module || _lower.OwnedFixture != _targets.OwnedFixture)
                    || _enginePitch is not null && (_lower is null || !ReferenceEquals(_enginePitch.Parent, _lower)
                        || _enginePitch.Module != _lower.Module)
                    || _lowerTime is not null && (_lower is null || !ReferenceEquals(_lowerTime.Parent, _lower)
                        || _lowerTime.Module != _lower.Module))
                    RejectSupportingIssuer();
            }
            private void RejectSupportingIssuer()
            {
                Interlocked.Exchange(ref _unknown, 1);
                throw new UnsupportedSignatureException("Supporting verification requires this traversal's exact entry-verified issuer and parent lineage.");
            }
            private void VerifyContext(PlaybackSourceInstallGroup group)
            {
                if (_closed || !ReferenceEquals(_registered, this) || _thread != Environment.CurrentManagedThreadId
                    || !ReferenceEquals(group, _group) || !System.Threading.Monitor.IsEntered(Gate)
                    || !System.Threading.Monitor.IsEntered(group._verifyGate)
                    || _installation.HasValue && !ReferenceEquals(group._installationTraversal, this))
                    throw new UnsupportedSignatureException("A shared target verification cannot leave its one actual locked group traversal.");
            }
            internal (IntPtr Address, int Length)[] FoundationImageRanges(PlaybackSourceInstallGroup group)
            {
                VerifyIssuer(group);
                return _foundation?.ImageRanges(group) ?? Array.Empty<(IntPtr Address, int Length)>();
            }
            internal void ComposeFoundation(PlaybackSourceInstallGroup group, uint rva, Span<byte> expected)
            {
                VerifyIssuer(group); _foundation?.ComposeIntoPinnedFile(group, rva, expected);
            }
            internal void BeginReferenceComposition(PlaybackSourceInstallGroup group, IntPtr module,
                uint sectionRva, byte[] expected, (IntPtr Address, int Length)[] otherWrites)
            {
                VerifyIssuer(group);
                if (module != group.Module) return;
                VerifyReferenceOwner(group);
                var members = _referenceMemberOwner;
                if (members is null) return;
                if (_referenceSection is not null)
                    throw new UnsupportedSignatureException("The previous broker image section did not complete its composition.");
                // This scope is created only for one locked VerifyCurrentBody.
                // No membership or successful evidence is retained by the group.
                var snapshot = _referenceSnapshot
                    ?? throw new UnsupportedSignatureException("The traversal has no entry-verified broker image snapshot.");
                _referenceSection = snapshot.BeginCompositionSection(group, this);
                _referenceSection.VerifyCanonicalPreimagesForComposition(sectionRva, expected, otherWrites);
            }
            internal void ApplyReferenceComposition(PlaybackSourceInstallGroup group, IntPtr module,
                uint sectionRva, byte[] expected)
            {
                VerifyIssuer(group);
                if (module != group.Module) return;
                VerifyReferenceOwner(group);
                if (_referenceMemberOwner is null) return;
                var section = _referenceSection ?? throw new UnsupportedSignatureException("The broker image section has no validated canonical composition.");
                section.ApplyPatchesForComposition(sectionRva, expected);
                _referenceSection = null;
            }
            internal void VerifyReferenceMembers(PlaybackSourceInstallGroup group)
            {
                VerifyIssuer(group);
                VerifyReferenceOwner(group);
                if (_referenceSection is not null)
                    throw new UnsupportedSignatureException("A broker image section remained uncomposed at full member exit.");
                var members = _referenceMemberOwner;
                if (members is null) return;
                var snapshot = _referenceSnapshot
                    ?? throw new UnsupportedSignatureException("The traversal has no entry-verified broker image snapshot.");
                // The full ordinary exit remains after both complete image
                // comparisons and before the actual GetTime/source checks.
                _referenceExitAttempted = true;
                snapshot.Verify();
            }
            private void VerifyReferenceOwner(PlaybackSourceInstallGroup group)
            {
                if (!ReferenceEquals(group._referenceMembers, _referenceMemberOwner))
                    throw new UnsupportedSignatureException("The traversal lost its original broker image membership owner.");
            }
            public void Dispose()
            {
                if (_closed) return;
                VerifyContext(_group); // Reject a copied/off-thread exit before touching the actual foundation scope.
                try
                {
                    // Attempt every actual exit even when composition or one
                    // exit failed. A failed ordinary phase is sticky unknown.
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo? exitFailure = null;
                    if (_referenceSnapshot is not null && !_referenceExitAttempted)
                    {
                        _referenceExitAttempted = true;
                        try { _referenceSnapshot.Verify(); }
                        catch (Exception error) { exitFailure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
                    }
                    try { VerifyReferenceOwner(_group); }
                    catch (Exception error) { exitFailure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
                    try { _group._targets.VerifyCurrent(); VerifySupportingIssuer(_targets); }
                    catch (Exception error) { exitFailure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
                    try { _dobbySeal.Verify(); }
                    catch (Exception error) { exitFailure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
                    // A receiver projection remains within this traversal.
                    // Recheck the originally retained helper even after another
                    // exit failed; earlier member batches cannot sign its exit.
                    try
                    {
                        _sourceHelperSeal.Verify();
                        if (!ReferenceEquals(_group._hub, _sourceHub))
                            throw new UnsupportedSignatureException("The traversal lost its original native source helper owner.");
                    }
                    catch (Exception error) { exitFailure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
                    exitFailure?.Throw();
                }
                catch { Interlocked.Exchange(ref _unknown, 1); throw; }
                finally
                {
                    try { _foundation?.Dispose(); }
                    finally
                    {
                        if (_installation.HasValue && ReferenceEquals(_group._installationTraversal, this))
                        { _group._installationTraversal = null; _group._installationAudit = 0; }
                        _closed = true;
                    }
                }
            }
        }
        private long VerifyTraversalVerificationIssuer(long? generation = null)
        {
            if (!System.Threading.Monitor.IsEntered(Gate) || !System.Threading.Monitor.IsEntered(_verifyGate)
                || !IsActive || generation.HasValue && generation.Value != _referenceCodeGeneration)
                throw new UnsupportedSignatureException("Shared target verification requires this one locked active group and unchanged broker generation.");
            VerifySealIssuer(Module); return _referenceCodeGeneration;
        }
        internal long VerifyFoundationVerificationIssuer(NativeFirstInstall.PlaybackMembership foundation, long? generation = null,
            long? installation = null)
        {
            if (foundation is null || !ReferenceEquals(_foundation, foundation))
                throw new UnsupportedSignatureException("Foundation verification requires this one locked active group and unchanged broker generation.");
            if (!installation.HasValue) return VerifyTraversalVerificationIssuer(generation);
            var current = generation ?? _referenceCodeGeneration;
            VerifyInstallationAudit(installation.Value, current); return current;
        }
        internal void VerifySealIssuer(IntPtr module)
        {
            if ((module != Module && (!HasLowerPitch || module != LowerModule)) || !OwnsPlaybackGroup(this) || Volatile.Read(ref _fault) is not null)
                throw new UnsupportedSignatureException("The executable composition lacks its exact retained private group issuer.");
        }
        // This identity check must stay below Current/image verification: the
        // same private pair is itself a member of the group's image composition.
        internal void VerifyNativePitchPairIssuer(NativePlaybackLowerPitchTarget.EnginePitchPreflight target, DobbyDetourEngine engine,
            NativePlaybackLowerPitchTarget.LowerTimePreflight? lowerTime = null)
        {
            VerifySealIssuer(target.Module);
            if (!_targets.HasEnginePitch || !ReferenceEquals(_targets.EnginePitch, target)
                || !ReferenceEquals(_targets.LowerPitch, target.Parent) || !ReferenceEquals(_engine, engine)
                || !_sources.TryGetValue(NativePlaybackSourceRole.LowerSetPitch, out var lower)
                || !_sources.TryGetValue(NativePlaybackSourceRole.EngineSetPitch, out var native)
                || !OwnsPlaybackSourceInstallation(lower) || !OwnsPlaybackSourceInstallation(native)
                || !lower.HasEngine(engine) || !native.HasEngine(engine)
                || lower.TargetAddress != target.Parent.Address || native.TargetAddress != target.Address
                || !NativeMember(lower, NativePlaybackSourceRole.LowerSetPitch)
                || !NativeMember(native, NativePlaybackSourceRole.EngineSetPitch)
                || !ReferenceEquals(_targets.LowerTime, lowerTime)
                || lowerTime is not null && (!ReferenceEquals(lowerTime.Parent, target.Parent) || lowerTime.Module != target.Module
                    || !_sources.TryGetValue(NativePlaybackSourceRole.LowerSetTime, out var time)
                    || !NativeMember(time, NativePlaybackSourceRole.LowerSetTime) || time.TargetAddress != lowerTime.Address))
                throw new UnsupportedSignatureException("The fixed native family lost its exact registered source members, targets or engine.");
            bool NativeMember(PlaybackSourceOwner owner, NativePlaybackSourceRole role)
                => ReferenceEquals(owner.Group, this) && owner.Role == role && owner.Kind == NativePlaybackSourceKind.IcallSingle1
                    && owner.MemberModule == target.Module && OwnsPlaybackSourceInstallation(owner) && owner.HasEngine(engine)
                    && (owner.EntryAddress == IntPtr.Zero || ReferenceEquals(owner.ConfiguredChannel.Hub, _hub)
                        && owner.ConfiguredChannel.Role == role && owner.ConfiguredChannel.Kind == NativePlaybackSourceKind.IcallSingle1
                        && owner.ConfiguredChannel.TargetAddress == owner.TargetAddress);
        }
        internal (IntPtr Address, int Length)[] ImageBodies(IntPtr module)
        {
            lock (Gate)
            {
                VerifySealIssuer(module);
                var rows = _sources.Values.Where(value => value.MemberModule == module)
                    .Select(value => (value.TargetAddress, value.OriginalBodyLength));
                if (module == Module)
                    rows = rows.Append((_getTime!.TargetAddress, _getTime.OriginalBodyLength))
                        .Concat(_foundation?.ImageRanges() ?? Array.Empty<(IntPtr Address, int Length)>())
                        .Concat(_referenceMembers?.ImageRanges() ?? Array.Empty<(IntPtr Address, int Length)>());
                return rows.OrderBy(value => value.Item1.ToInt64()).ToArray();
            }
        }
        internal void ComposeVerifiedMembers(IntPtr module, uint sectionRva, byte[] expected)
            => ComposeVerifiedMembersCore(module, sectionRva, expected, null);
        internal void ComposeVerifiedRetiredMembers(IntPtr module, uint sectionRva, byte[] expected)
        {
            VerifyRetiredActivityContext();
            ComposeVerifiedMembersCore(module, sectionRva, expected, null, retired: true);
        }
        internal void ComposeVerifiedMembersForTraversal(IntPtr module, uint sectionRva, byte[] expected,
            VerificationTraversal traversal)
            => ComposeVerifiedMembersCore(module, sectionRva, expected, traversal);
        private void ComposeVerifiedMembersCore(IntPtr module, uint sectionRva, byte[] expected,
            VerificationTraversal? traversal, bool retired = false)
        {
            // Each contributor below authenticates its own current owner and
            // complete contribution before composing. Rechecking every member
            // here and then again in each Compose multiplied all image reads.
            // Keep common targets current; installation/active group boundaries
            // still verify both images and the complete member set.
            lock (Gate)
            {
                VerifySealIssuer(module);
                if (traversal is null) _targets.VerifyCurrent(); else traversal.VerifySupportingIssuer(_targets);
                var ordinary = module == Module && traversal is null ? _referenceMembers?.CaptureSnapshot() : null;
                var otherWrites = _sources.Values.Where(value => value.MemberModule == module && value.PatchedByteCount > 0)
                    .Select(value => (Address: value.TargetAddress, Length: value.PatchedByteCount));
                if (module == Module)
                    otherWrites = otherWrites.Concat(traversal is not null ? traversal.FoundationImageRanges(this)
                            : _foundation?.ImageRanges() ?? Array.Empty<(IntPtr Address, int Length)>())
                        .Concat(_getTime!.PatchedByteCount > 0
                            ? new[] { (Address: _getTime.TargetAddress, Length: _getTime.PatchedByteCount) }
                            : Array.Empty<(IntPtr Address, int Length)>());
                // Each registered broker preimage64 must see the independent
                // canonical+DIR64 bytes before any owner changes this section.
                var writes = otherWrites.ToArray();
                ordinary?.VerifyCanonicalPreimagesForComposition(sectionRva, expected, writes);
                traversal?.BeginReferenceComposition(this, module, sectionRva, expected, writes);
                if (module == Module)
                {
                    if (traversal is null) _foundation?.ComposeIntoPinnedFile(sectionRva, expected);
                    else traversal.ComposeFoundation(this, sectionRva, expected);
                    _getTime!.ComposeIntoPinnedFile(sectionRva, expected, traversal);
                }
                foreach (var owner in _sources.Values.Where(value => value.MemberModule == module
                    && (traversal is null || SectionContainsSource(value, module, sectionRva, expected.Length))))
                    owner.ComposeIntoPinnedFile(this, sectionRva, expected, traversal, retired);
                ordinary?.ApplyPatchesForComposition(sectionRva, expected);
                traversal?.ApplyReferenceComposition(this, module, sectionRva, expected);
                ordinary?.Verify();
            }
        }
        private static bool SectionContainsSource(PlaybackSourceOwner owner, IntPtr module, uint sectionRva, int sectionLength)
        {
            var relative = owner.TargetAddress.ToInt64() - module.ToInt64() - sectionRva;
            return relative >= 0 && relative < sectionLength;
        }
        internal void VerifyStockIdentity(NativeRuntime.NativePlaybackStockTargets targets)
        {
            if (!_targets.IdentifiesStock(targets))
                throw new UnsupportedSignatureException("The group lease has a different runtime-issued stock target receipt.");
            targets.VerifyCurrent(); VerifyCurrent();
        }
        internal bool MatchesOriginalBody(IntPtr address, string hash, int length)
        {
            VerifyCurrent();
            return MatchesOriginalBodyPin(address, hash, length);
        }
        internal bool MatchesOriginalBodies(ReadOnlySpan<(IntPtr Address, string Hash, int Length)> bodies)
        {
            lock (_verifyGate)
            {
                // Snapshot only one bounded query. No caller-owned array or
                // successful verification survives this call, and every match
                // is bracketed by this actual registered group's full checks.
                var requested = !bodies.IsEmpty && bodies.Length <= _sources.Count + 1 ? bodies.ToArray() : null;
                VerifyCurrent();
                var matches = requested is not null;
                if (requested is not null)
                {
                    var addresses = new HashSet<IntPtr>();
                    foreach (var body in requested)
                        matches &= addresses.Add(body.Address) && MatchesOriginalBodyPin(body.Address, body.Hash, body.Length);
                }
                VerifyCurrent();
                return matches;
            }
        }
        private bool MatchesOriginalBodyPin(IntPtr address, string hash, int length)
            => address == _getTime!.TargetAddress && length == _getTime.OriginalBodyLength && string.Equals(hash, _getTime.OriginalBodySha256, StringComparison.OrdinalIgnoreCase)
                || _sources.Values.Any(value => address == value.TargetAddress && length == value.OriginalBodyLength
                    && string.Equals(hash, value.OriginalBodySha256, StringComparison.OrdinalIgnoreCase));
        internal NativePlaybackSourceHealth ReadSourceHealth() => _hub!.ReadHealth();
        internal NativePlaybackStockSourceHub.AudioInterruptionInformation ReadAudioInterruption(
            NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
        {
            VerifyCurrent();
            // The hub authenticates this owner's retained initializer and actual
            // yield. No scalar projection crosses into the opaque retry gate.
            return _hub!.ReadAudioInterruption(enumeratorRoot, actualYield);
        }
        internal void VerifyIssuedBefore(NativePlaybackSourceObservation observation)
        { VerifyCurrent(); SourceOwner(observation.Role).VerifyIssuedBefore(observation); }
        internal void VerifyIssuedReturn(NativePlaybackSourceCompletion completion)
        { VerifyCurrent(); SourceOwner(completion.Observation.Role).VerifyIssuedReturn(completion); }
        // These are caller addresses, not execution receipts or a reusable group
        // authority. The producer uses them only to check its fixed constructor
        // profile; each later observation still verifies its own actual caller.
        internal sealed record StockOccurrenceCallProfile(
            IntPtr PauseLeaf, IntPtr ProgressPauseLeaf,
            IntPtr ReplayPitch, IntPtr ReplaySchedule, IntPtr ReplayStop,
            IntPtr ResumePitch, IntPtr ResumeSchedule, IntPtr ResumeStop, IntPtr ResumeTime,
            bool HasDirectAudioControls)
        {
            internal void RequireComplete(bool directAudio)
            {
                if (HasDirectAudioControls != directAudio
                    || PauseLeaf == IntPtr.Zero || ProgressPauseLeaf == IntPtr.Zero
                    || ReplayPitch == IntPtr.Zero || ReplaySchedule == IntPtr.Zero
                    || ResumePitch == IntPtr.Zero || ResumeSchedule == IntPtr.Zero || ResumeTime == IntPtr.Zero
                    || directAudio && (ReplayStop == IntPtr.Zero || ResumeStop == IntPtr.Zero)
                    || !directAudio && (ReplayStop != IntPtr.Zero || ResumeStop != IntPtr.Zero))
                    throw new UnsupportedSignatureException("The fixed stock occurrence profile lacks an exact required caller.");
            }
        }

        internal StockOccurrenceCallProfile CaptureStockOccurrenceCallProfile()
        {
            lock (_verifyGate)
            lock (Gate)
            {
                if (!IsActive) throw new UnsupportedSignatureException("The private playback group is dormant, withdrawn or sticky faulted.");
                StockOccurrenceCallProfile? profile = null;
                System.Runtime.ExceptionServices.ExceptionDispatchInfo? callerFailure = null;
                try
                {
                    VerifySealIssuer(Module);
                    using var traversal = VerificationTraversal.Create(this);
                    VerifyCurrentBody(traversal);
                    traversal.VerifyIssuer(this);
                    try
                    {
                        var directAudio = _targets.HasDirectAudioControls;
                        if (directAudio != _sources.ContainsKey(NativePlaybackSourceRole.AudioPlayHelper)
                            || _targets.StockIssuer is not null && !directAudio)
                            throw new UnsupportedSignatureException("The stock occurrence profile lost its complete direct-audio source group.");
                        IntPtr Caller(NativePlaybackSourceRole parent, NativePlaybackSourceRole leaf)
                        {
                            traversal.VerifyIssuer(this);
                            return StockLeafCallerCore(parent, leaf);
                        }
                        profile = new(
                            Caller(NativePlaybackSourceRole.Pause, NativePlaybackSourceRole.PauseLeaf),
                            Caller(NativePlaybackSourceRole.ProgressPlay, NativePlaybackSourceRole.PauseLeaf),
                            Caller(NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.SetPitch),
                            Caller(NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.PlayScheduled),
                            directAudio ? Caller(NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.AudioStop) : IntPtr.Zero,
                            Caller(NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetPitch),
                            Caller(NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.PlayScheduled),
                            directAudio ? Caller(NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.AudioStop) : IntPtr.Zero,
                            Caller(NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetTime), directAudio);
                        profile.RequireComplete(directAudio);
                    }
                    catch (Exception error) { callerFailure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
                    traversal.VerifyIssuer(this);
                }
                catch (Exception error) { Fault("The common playback group lost its physical authority: " + error.Message); throw; }
                // A bad caller profile is not itself a group traversal failure.
                // Complete the full target/Dobby/foundation exit first, then
                // retain the caller failure just as independent lookup did.
                callerFailure?.Throw();
                return profile!;
            }
        }
        internal IntPtr StockLeafCaller(NativePlaybackSourceRole parent, NativePlaybackSourceRole leaf)
        {
            VerifyCurrent();
            return StockLeafCallerCore(parent, leaf);
        }

        internal bool IsStockResumeCountdownCaller(IntPtr caller)
        {
            if (OwnedFixture)
            {
                // The fixed fixture invokes Play through the compiler's CFG dispatch slot.
                return PlaybackSourceGroupTargets.IsDirectAudioFixtureImage(ImageSha256)
                    && caller == IntPtr.Add(Module, PlaybackSourceGroupTargets.DirectAudioFixtureResumeCountdownCallerRva)
                    && NativePlaybackExecutableSeal.RangeSeal.Read(IntPtr.Add(caller, -6), 6)
                        .AsSpan().SequenceEqual(new byte[] { 0xff, 0x15, 0x45, 0x3a, 0, 0 });
            }

            const int pauseUiResumePlayReturnRva = 0x3c3ced;
            if (ImageSha256 != PlaybackSourceGroupTargets.StockImageSha256
                || caller != IntPtr.Add(Module, pauseUiResumePlayReturnRva)) return false;
            var call = NativePlaybackExecutableSeal.RangeSeal.Read(IntPtr.Add(caller, -5), 5);
            return call[0] == 0xe8 && IntPtr.Add(caller, BitConverter.ToInt32(call, 1)) == _targets.ProgressPlay.Address;
        }

        private IntPtr StockLeafCallerCore(NativePlaybackSourceRole parent, NativePlaybackSourceRole leaf)
        {
            // Each owned image has its separately fixed CALL profile. A recipe
            // rebuild or runtime-discovered CALL cannot grant this identity.
            // Separately frozen no-predecessor allocation fixture. Exact image and
            // eight direct CALLs are recorded in its closed profile; rebuilding
            // this recipe does not confer either image identity.
            var lowerTimeFixture = OwnedFixture && HasLowerTime
                && ImageSha256 == NativePlaybackLowerPitchTarget.OwnedWrapperImageSha256;
            var lowerPitchFixture = OwnedFixture && HasLowerPitch && !HasLowerTime
                && ImageSha256 == NativePlaybackLowerPitchTarget.LegacyOwnedWrapperImageSha256;
            var unityBackingFixture = lowerPitchFixture || OwnedFixture && _targets.HasUnityBacking
                && ImageSha256 == PlaybackSourceGroupTargets.UnityBackingFixtureSha256;
            var progressResourceFixture = OwnedFixture && _targets.HasProgressResource
                && ImageSha256 == PlaybackSourceGroupTargets.ProgressResourceFixtureSha256;
            var directAudioFixture = OwnedFixture && PlaybackSourceGroupTargets.IsDirectAudioFixtureImage(ImageSha256);
            var constructorFixture = OwnedFixture && ImageSha256 == PlaybackSourceGroupTargets.ConstructorFixtureSha256;
            var initialPlayerFixture = OwnedFixture && ImageSha256 == "7ef26e1c274cc1cf826fb8e7a9283e913224a6c9f9c6a35cb76103b943c19d7f";
            var startFixture = initialPlayerFixture || OwnedFixture && ImageSha256 == "9fe8cde32ae1ce1fe9426efafb46a758d4e37593007a64747a042fdcab51bd6e";
            var timeWriterFixture = OwnedFixture && ImageSha256 == "ecd548c5993e4f7e07409d7d70f5d307279f132f2d7f3d2fc540b06cf163ec93";
            var handoffFixture = OwnedFixture && (ImageSha256 is "ccc23b6f72b6b71c80e62d45187dd1c8d490b41d7828c6510dcc6b44fdfdd18a"
                or "68de93fbfec24e15bdb6cd02969896d431e21ab5d424b5653954ff5279dd9931");
            var fixture = handoffFixture || OwnedFixture && ImageSha256 == "01e27f33028b26cea9721367e70d587500370ae13dd06f580025aa25da287279";
            var stock = !OwnedFixture && ImageSha256 == PlaybackSourceGroupTargets.StockImageSha256;
            if (!lowerTimeFixture && !unityBackingFixture && !progressResourceFixture && !directAudioFixture && !constructorFixture && !fixture && !timeWriterFixture && !startFixture && !stock)
                throw new UnsupportedSignatureException("No closed stock occurrence caller profile identifies this group image.");
            var rva = lowerTimeFixture ? OwnedPlaybackFixtureProfile.CallerReturnRva(parent, leaf) : unityBackingFixture ? (parent, leaf) switch
            {
                (NativePlaybackSourceRole.Pause, NativePlaybackSourceRole.PauseLeaf) => 0x1c37,
                (NativePlaybackSourceRole.ProgressPlay, NativePlaybackSourceRole.PauseLeaf) => 0x1ee8,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.AudioStop) => 0x1f74,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.SetPitch) => 0x1fa2,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.PlayScheduled) => 0x1fb0,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.AudioStop) => 0x2036,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetTime) => 0x204e,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetPitch) => 0x2076,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.PlayScheduled) => 0x2084,
                (NativePlaybackSourceRole.ProgressStartMoveNext, NativePlaybackSourceRole.SetClip) => lowerPitchFixture ? 0x4257 : 0x41a7,
                _ => throw new UnsupportedSignatureException("The exact Unity-backed fixture has no such physical parent/leaf CALL.")
            } : progressResourceFixture ? (parent, leaf) switch
            {
                (NativePlaybackSourceRole.Pause, NativePlaybackSourceRole.PauseLeaf) => 0x1b77,
                (NativePlaybackSourceRole.ProgressPlay, NativePlaybackSourceRole.PauseLeaf) => 0x1e28,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.AudioStop) => 0x1eb4,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.SetPitch) => 0x1ee2,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.PlayScheduled) => 0x1ef0,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.AudioStop) => 0x1f76,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetTime) => 0x1f8e,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetPitch) => 0x1fb6,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.PlayScheduled) => 0x1fc4,
                (NativePlaybackSourceRole.ProgressStartMoveNext, NativePlaybackSourceRole.SetClip) => 0x3a17,
                _ => throw new UnsupportedSignatureException("The exact resource fixture has no such physical parent/leaf CALL.")
            } : directAudioFixture ? (parent, leaf) switch
            {
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.AudioStop) => 0x1e84,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.AudioStop) => 0x1f46,
                (NativePlaybackSourceRole.Pause, NativePlaybackSourceRole.PauseLeaf) => 0x1b47,
                (NativePlaybackSourceRole.ProgressPlay, NativePlaybackSourceRole.PauseLeaf) => 0x1df8,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.SetPitch) => 0x1eb2,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.PlayScheduled) => 0x1ec0,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetTime) => 0x1f5e,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetPitch) => 0x1f86,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.PlayScheduled) => 0x1f94,
                (NativePlaybackSourceRole.ProgressStartMoveNext, NativePlaybackSourceRole.SetClip) => 0x34ea,
                _ => throw new UnsupportedSignatureException("The fixed direct-audio fixture has no such parent/leaf CALL.")
            } : constructorFixture ? (parent, leaf) switch
            {
                (NativePlaybackSourceRole.Pause, NativePlaybackSourceRole.PauseLeaf) => 0x1817,
                (NativePlaybackSourceRole.ProgressPlay, NativePlaybackSourceRole.PauseLeaf) => 0x1ac8,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.SetPitch) => 0x1b78,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.PlayScheduled) => 0x1b86,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetTime) => 0x1c0b,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetPitch) => 0x1c30,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.PlayScheduled) => 0x1c3e,
                (NativePlaybackSourceRole.ProgressStartMoveNext, NativePlaybackSourceRole.SetClip) => 0x2fda,
                _ => throw new UnsupportedSignatureException("The exact constructor fixture has no such physical parent/leaf CALL.")
            } : startFixture ? (parent, leaf) switch
            {
                (NativePlaybackSourceRole.Pause, NativePlaybackSourceRole.PauseLeaf) => 0x16a7,
                (NativePlaybackSourceRole.ProgressPlay, NativePlaybackSourceRole.PauseLeaf) => 0x17d8,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.SetPitch) => 0x1888,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.PlayScheduled) => 0x1896,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetTime) => 0x191b,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetPitch) => 0x1940,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.PlayScheduled) => 0x194e,
                (NativePlaybackSourceRole.ProgressStartMoveNext, NativePlaybackSourceRole.SetClip) => initialPlayerFixture ? 0x2c71 : 0x2c41,
                _ => throw new UnsupportedSignatureException("This exact V4 owned image has no such physical parent/leaf CALL.")
            } : timeWriterFixture ? (parent, leaf) switch
            {
                (NativePlaybackSourceRole.Pause, NativePlaybackSourceRole.PauseLeaf) => 0x1697,
                (NativePlaybackSourceRole.ProgressPlay, NativePlaybackSourceRole.PauseLeaf) => 0x17c8,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.SetPitch) => 0x1878,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.PlayScheduled) => 0x1886,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetTime) => 0x190b,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetPitch) => 0x1930,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.PlayScheduled) => 0x193e,
                _ => throw new UnsupportedSignatureException("This exact V3 owned image has no such physical parent/leaf CALL.")
            } : (parent, leaf) switch
            {
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.AudioStop) when stock => 0x3fbf21,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.AudioStop) when stock => 0x3fc185,
                (NativePlaybackSourceRole.Pause, NativePlaybackSourceRole.PauseLeaf) => fixture ? 0x1557 : 0x3fbdd1,
                (NativePlaybackSourceRole.ProgressPlay, NativePlaybackSourceRole.PauseLeaf) => fixture ? 0x1688 : 0x40077e,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.SetPitch) => fixture ? 0x1738 : 0x3fbf83,
                (NativePlaybackSourceRole.Replay, NativePlaybackSourceRole.PlayScheduled) => fixture ? 0x1746 : 0x3fbfaa,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetTime) when !fixture => 0x3fc19d,
                (NativePlaybackSourceRole.ProgressStartMoveNext, NativePlaybackSourceRole.SetClip) when stock => 0x404b06,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetPitch) => fixture ? 0x17e4 : 0x3fc203,
                (NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.PlayScheduled) => fixture ? 0x17f2 : 0x3fc22a,
                _ => throw new UnsupportedSignatureException("This physical parent/leaf pair has no exact caller profile.")
            };
            if (handoffFixture) rva += 0x110;
            var caller = IntPtr.Add(Module, rva);
            var call = NativePlaybackExecutableSeal.RangeSeal.Read(IntPtr.Add(caller, -5), 5);
            if (call[0] != 0xe8 || IntPtr.Add(caller, BitConverter.ToInt32(call, 1)) != _targets.SourceTarget(leaf).Address)
                throw new UnsupportedSignatureException("The pinned stock caller is not the exact direct leaf CALL.");
            return caller;
        }
        internal void PumpCompleted()
        {
            foreach (var owner in _sources.Values) owner.PumpCompleted();
            _hub?.PumpUnresolvedPlayerRootOwners();
            _hub?.PumpProgressResourceTickets();
        }
        internal void PollNativeFaults()
        {
            // A relevant worker deliberately emits no managed source observer.
            // Poll the retained transport diagnostics on the owner before
            // consuming fault notifications; this creates no source authority.
            try { _ = _hub!.ReadHealth(); _ = _getTime!.ReadTransportHealth(); }
            catch (Exception error) { Fault("The retained native playback transport could not be polled: " + error.Message); }
        }
        internal bool TryVerifyIdleForRootRelease()
        {
            VerifyOperationThread();
            var source = _hub!;
            var getTime = _getTime!;
            // A native callback cannot retire its own active roots. Defer that
            // attempt using only the live counters. Releasing roots still
            // requires the complete retained activity and native watch proofs.
            if (source.ReadOperationHealth() is { Known: true, InFlight: > 0 }
                || getTime.ReadTransportHealth() is { Known: true, InFlight: > 0 }) return false;
            if (ReadActivity() is not { Known: true, InFlight: 0 }) return false;
            // A worker can enter during the full audit. Sample both channels
            // again before the caller attempts native watch retirement.
            return getTime.ReadTransportHealth() is { Known: true, InFlight: 0 }
                && source.ReadOperationHealth() is { Known: true, InFlight: 0 }
                && Volatile.Read(ref _fault) is null;
        }
        internal ManagedBoundaryActivity ReadActivity()
        {
            lock (_verifyGate)
            lock (Gate)
            {
                if (_withdrawn != 0 && !_withdrawalComplete) return new(long.MaxValue, false);
                var count = 0L; var known = Volatile.Read(ref _fault) is null;
                try
                {
                    var value = _getTime!.ReadTransportHealth();
                    count = checked((long)value.InFlight); known &= value.Known;
                    if (_withdrawalComplete && value.AuthorityActive)
                        throw new UnsupportedSignatureException("The withdrawn GetTime owner still has an active policy.");
                }
                catch (Exception error)
                {
                    count = long.MaxValue; known = false;
                    if (_withdrawalComplete) Fault("Playback retained clock activity failed: " + error.Message);
                }
                // Shared counters contribute once for all configured source members.
                try { var value = _hub!.ReadHealth(); count = count == long.MaxValue ? count : checked(count + (long)value.InFlight); known &= value.Known; }
                catch { count = long.MaxValue; known = false; }
                try
                {
                    VerifySealIssuer(Module);
                    if (_withdrawalComplete) VerifyRetiredActivity();
                    else { VerifyImages(); VerifyMembers(); }
                }
                catch (Exception error) { known = false; Fault("Playback retained activity authority failed: " + error.Message); }
                return new(count, known && Volatile.Read(ref _fault) is null);
            }
        }
        private void VerifyRetiredActivity()
        {
            VerifyRetiredActivityContext();
            Image.VerifyForRetiredGroup(this); _lowerImage?.VerifyForRetiredGroup(this);
            _targets.VerifyCurrent(); _foundation?.VerifyCurrent(); _referenceMembers?.Verify();
            _getTime!.VerifyMemberEvidence();
            foreach (var member in _sources.Values) member.VerifyRetiredMemberEvidence(this);
            VerifyRetiredActivityContext();
        }
        internal void VerifyRetiredActivityContext()
        {
            if (!Monitor.IsEntered(_verifyGate) || !Monitor.IsEntered(Gate) || !_withdrawalComplete
                || _withdrawn != 1 || Volatile.Read(ref _active) != 0 || _getTimeLease is null
                || _sources.Count != _targets.SourceRoleCount)
                throw new UnsupportedSignatureException("Retained activity requires the locked, explicitly withdrawn playback group.");
            VerifySealIssuer(Module);
        }
        internal void VerifyRetiredSourceHelper(NativePlaybackStockSourceHub hub)
        {
            VerifyRetiredActivityContext();
            if (!ReferenceEquals(_hub, hub))
                throw new UnsupportedSignatureException("Retired activity cannot borrow another group's source helper.");
        }
        internal void VerifyRetiredImage(NativePlaybackExecutableSeal image)
        {
            VerifyRetiredActivityContext();
            if (!ReferenceEquals(_image, image) && !ReferenceEquals(_lowerImage, image))
                throw new UnsupportedSignatureException("Retired activity cannot borrow another group's executable image.");
        }
        internal void Fault(string reason)
        {
            if (Interlocked.CompareExchange(ref _fault, reason, null) is not null) return;
            Volatile.Write(ref _active, 0);
            try { _getTime?.RevokeFromGroup(reason); } finally { _hub?.MarkBrokerFault(reason); }
        }
        internal void Withdraw()
        {
            lock (_verifyGate)
            lock (Gate)
            {
                if (Interlocked.Exchange(ref _withdrawn, 1) != 0) return;
                Volatile.Write(ref _active, 0);
                try
                {
                    try { _getTime?.WithdrawFromGroup(); } finally { _hub?.Withdraw(); }
                    _withdrawalComplete = true;
                }
                catch (Exception error)
                {
                    Fault("Playback whole-group withdrawal failed: " + error.Message);
                    throw;
                }
            }
        }
    }
}
