namespace RizToolbox.Runtime.Hooks;

internal sealed partial class PlaybackSourceGroupLease : IDisposable
{
    private readonly ExactHookBroker.PlaybackSourceInstallGroup _owner;
    private readonly PlaybackSourceGroupLease _registered;
    private readonly Dictionary<NativePlaybackSourceRole, NativePlaybackSourceHookLease> _sources;
    private PlaybackSourceGroupLease(ExactHookBroker.PlaybackSourceInstallGroup owner)
    {
        _owner = owner;
        _registered = this;
        _sources = owner.SourceRoles.ToDictionary(role => role,
            role => NativePlaybackSourceHookLease.Issue(owner.SourceOwner(role)));
    }
    internal static PlaybackSourceGroupLease Issue(ExactHookBroker.PlaybackSourceInstallGroup owner)
    {
        if (!ExactHookBroker.OwnsPlaybackGroup(owner)) throw new UnsupportedSignatureException("A group lease requires its actual private registry owner.");
        owner.VerifyCurrent(); return new(owner);
    }
    internal NativePlaybackGetTimeHookLease GetTimeLease => _owner.GetTimeLease;
    internal NativePlaybackSourceHookLease SetPitchLease => SourceLease(NativePlaybackSourceRole.SetPitch);
    internal NativePlaybackSourceHookLease SourceLease(NativePlaybackSourceRole role) => _sources.TryGetValue(role, out var lease)
        ? lease : throw new UnsupportedSignatureException("The group has no such private physical source member.");
    internal void VerifyInitializationCapture(NativePlaybackSingleReturnBridge.LogicalHandoff handoff, long player, long source, long clip)
        => _owner.VerifyInitializationCapture(handoff, player, source, clip);
    internal void BindLogicalHandoff() => _owner.BindLogicalHandoff();
    internal void BindInitialCaptureSource() => _owner.BindInitialCaptureSource();
    internal NativePlaybackSingleReturnBridge.InitialCapture BeginInitialCapture(IntPtr initializer) => _owner.BeginInitialCapture(initializer);
    internal void VerifyInitialCapture(NativePlaybackSingleReturnBridge.InitialCapture receipt, long player, long source, long clip)
        => _owner.VerifyInitialCapture(receipt, player, source, clip);
    internal ulong Epoch => _owner.Epoch;
    internal IntPtr Module => _owner.Module;
    internal bool OwnedFixture => _owner.OwnedFixture;
    internal bool HasPlayerConstructor => _sources.ContainsKey(NativePlaybackSourceRole.PlayerConstructor);
    internal bool HasDirectAudioControls => _sources.ContainsKey(NativePlaybackSourceRole.AudioPlayHelper);
    internal bool HasProgressResource => _sources.ContainsKey(NativePlaybackSourceRole.ProgressClip);
    internal bool HasLowerPitch => _sources.ContainsKey(NativePlaybackSourceRole.LowerSetPitch);
    internal bool HasEnginePitch => _sources.ContainsKey(NativePlaybackSourceRole.EngineSetPitch);
    internal bool HasScheduledEnginePitch => _owner.HasScheduledEnginePitch;
    internal bool HasLowerTime => _sources.ContainsKey(NativePlaybackSourceRole.LowerSetTime);
    internal ulong FoundationEpoch => _owner.FoundationEpoch;
    internal void VerifyReceivers(NativeRuntime api, IntPtr player, IntPtr source, IntPtr progress, IntPtr getTimeMethod)
        => _owner.VerifyReceivers(api, player, source, progress, getTimeMethod);
    internal NativePlaybackStockSourceHub.WriterReservation ReservePitchWriter(NativePlaybackSourceObservation root,
        IntPtr source, uint bits, ulong writer, ulong sourceFence, ulong scheduling,
        NativePlaybackStockSourceHub.WriterReservation? predecessor)
        => _owner.ReservePitchWriter(root, source, bits, writer, sourceFence, scheduling, predecessor);
    internal NativePlaybackStockSourceHub.WriterReservation ReserveTimeWriter(NativePlaybackSourceObservation root,
        IntPtr source, uint bits, ulong writer, ulong sourceFence, ulong scheduling,
        NativePlaybackStockSourceHub.WriterReservation? predecessor)
        => _owner.ReserveTimeWriter(root, source, bits, writer, sourceFence, scheduling, predecessor);
    internal NativeRuntime.RootHandle RetainInitializationEnumerator(NativeRuntime api, NativePlaybackSourceObservation observation)
        => _owner.RetainInitializationEnumerator(api, observation);
    internal NativeRuntime.RootHandle RetainInitializationProgress(NativeRuntime api, IntPtr progress)
        => _owner.RetainInitializationProgress(api, progress);
    internal bool MatchesInitializationCall(NativePlaybackSourceObservation actual, ulong sequence, ulong token)
        => _owner.MatchesInitializationCall(actual, sequence, token);
    internal void RequireFreshAdmission() => _owner.RequireFreshAdmission();
    internal void RequireRateOperationAdmission()
    {
        VerifyOperationLease();
        _owner.RequireRateOperationAdmission();
    }
    internal void RequireSeekOperationAdmission()
    {
        VerifyOperationLease();
        _owner.RequireSeekOperationAdmission();
    }
    internal (NativePlaybackSingleReturnAuthority? Authority, bool Active) ReadOperationClockPolicy(NativePlaybackGetTimeHookLease hook)
    {
        VerifyOperationLease();
        return _owner.ReadOperationClockPolicy(hook);
    }
    internal void PublishOperationClockPolicy(NativePlaybackGetTimeHookLease hook, long expectedRevision,
        NativePlaybackSingleReturnAuthority next)
    {
        VerifyOperationLease();
        _owner.PublishOperationClockPolicy(hook, expectedRevision, next);
    }
    internal (long FullAudits, long ClockChecks, long RateAdmissions, long SourceChecks, long FullImageAudits) ReadOperationDiagnostics()
    {
        VerifyOperationLease();
        return _owner.ReadOperationDiagnostics();
    }
    private void VerifyOperationLease()
    {
        if (!ReferenceEquals(_registered, this))
            throw new UnsupportedSignatureException("A copied group lease owns no running playback channel.");
    }
    internal NativePlaybackStockSourceHub.InitializationInformation ReadInitialization() => _owner.ReadInitialization();
    // The retained root and actual yield stay on their registered owner path;
    // the returned diagnostic value cannot mint or replace a retry receipt.
    internal NativePlaybackStockSourceHub.AudioInterruptionInformation ReadAudioInterruption(
        NativeRuntime.RootHandle enumeratorRoot, NativePlaybackSourceObservation actualYield)
        => _owner.ReadAudioInterruption(enumeratorRoot, actualYield);
    internal bool IsActive => _owner.IsActive;
    internal bool ProductionAdmission => false;
    internal void VerifyCurrent(NativeRuntime.NativePlaybackStockTargets targets) => _owner.VerifyStockIdentity(targets);
    internal void VerifyCurrent() => _owner.VerifyCurrent();
    internal bool MatchesOriginalBody(IntPtr address, string hash, int length) => _owner.MatchesOriginalBody(address, hash, length);
    internal bool MatchesOriginalBodies(ReadOnlySpan<(IntPtr Address, string Hash, int Length)> bodies) => _owner.MatchesOriginalBodies(bodies);
    internal NativePlaybackSourceHealth ReadSourceHealth() { VerifyCurrent(); return _owner.ReadSourceHealth(); }
    internal void PumpCompleted() => _owner.PumpCompleted();
    internal void PollNativeFaults() => _owner.PollNativeFaults();
    internal void VerifyIssuedBefore(NativePlaybackSourceObservation observation) => _owner.VerifyIssuedBefore(observation);
    internal void VerifyIssuedReturn(NativePlaybackSourceCompletion completion) => _owner.VerifyIssuedReturn(completion);
    internal IntPtr StockLeafCaller(NativePlaybackSourceRole parent, NativePlaybackSourceRole leaf) => _owner.StockLeafCaller(parent, leaf);
    internal ExactHookBroker.PlaybackSourceInstallGroup.StockOccurrenceCallProfile CaptureStockOccurrenceCallProfile()
        => _owner.CaptureStockOccurrenceCallProfile();
    internal ExactHookBroker.ManagedBoundaryActivity ReadActivity() => _owner.ReadActivity();
    internal bool TryVerifyIdleForRootRelease()
    {
        VerifyOperationLease();
        return _owner.TryVerifyIdleForRootRelease();
    }
    public void Dispose() => _owner.Withdraw();
}

/// <summary>A diagnostic projection of one registered physical member. Its
/// possession cannot create, replace or expand the private installation.</summary>
internal sealed class NativePlaybackSourceHookLease : IDisposable
{
    private readonly ExactHookBroker.PlaybackSourceOwner _owner;
    private NativePlaybackSourceHookLease(ExactHookBroker.PlaybackSourceOwner owner) => _owner = owner;
    internal static NativePlaybackSourceHookLease Issue(ExactHookBroker.PlaybackSourceOwner owner)
    {
        if (!ExactHookBroker.OwnsPlaybackSourceInstallation(owner)) throw new UnsupportedSignatureException("A source lease requires its actual registered installation owner.");
        return new(owner);
    }
    internal NativePlaybackSourceRole Role => _owner.Role;
    internal NativePlaybackSourceKind Kind => _owner.Kind;
    internal IntPtr EntryAddress => _owner.EntryAddress;
    internal IntPtr OriginalAddress => _owner.OriginalAddress;
    internal IntPtr DobbyOriginalAddress => _owner.DobbyOriginalAddress;
    internal IntPtr TrampolineUnwindAddress => _owner.TrampolineUnwindAddress;
    internal int PatchedByteCount => _owner.PatchedByteCount;
    internal int RelocatedByteCount => _owner.RelocatedByteCount;
    internal int OriginalBodyLength => _owner.OriginalBodyLength;
    internal string OriginalBodySha256 => _owner.OriginalBodySha256;
    internal ulong GroupEpoch => _owner.Group.Epoch;
    internal bool IsActive => _owner.Group.IsActive;
    internal void VerifyCurrent() => _owner.Group.VerifyCurrent();
    internal NativePlaybackSourceHealth ReadHealth() { VerifyCurrent(); return _owner.Group.ReadSourceHealth(); }
    internal void PumpCompleted() => _owner.Group.PumpCompleted();
    internal ExactHookBroker.ManagedBoundaryActivity ReadActivity() => _owner.Group.ReadActivity();
    public void Dispose() => _owner.Group.Withdraw();
}
