namespace RizToolbox.Runtime.Hooks;

/// <summary>A capability backed by the broker's retained, privately registered
/// installation owner. Scalar copies and caller verifiers cannot issue one.</summary>
internal sealed partial class NativePlaybackGetTimeHookLease : IDisposable
{
    private readonly ExactHookBroker.PlaybackOwner _owner;
    private readonly NativePlaybackGetTimeHookLease _registered;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private NativePlaybackGetTimeHookLease(ExactHookBroker.PlaybackOwner owner) => (_owner, _registered) = (owner, this);
    internal static NativePlaybackGetTimeHookLease Issue(ExactHookBroker.PlaybackOwner owner)
    {
        if (!ExactHookBroker.OwnsPlaybackInstallation(owner))
            throw new InvalidOperationException("A GetTime receipt requires the broker's exact retained installation owner.");
        return new(owner);
    }
    internal string OriginalBodySha256 => _owner.OriginalBodySha256;
    internal int OriginalBodyLength => _owner.OriginalBodyLength;
    internal int PatchedByteCount => _owner.PatchedByteCount;
    internal IntPtr OriginalAddress => _owner.OriginalAddress;
    internal IntPtr DobbyOriginalAddress => _owner.DobbyOriginalAddress;
    internal IntPtr TrampolineUnwindAddress => _owner.TrampolineUnwindAddress;
    internal IntPtr EntryAddress => _owner.EntryAddress;
    internal bool IsActive => _owner.IsActive;
    internal bool ProductionAdmission => false;
    internal ExactHookBroker.PlaybackOwner OwnerForGroupAdoption()
    { _owner.VerifyCurrent(_owner.Module, _owner.TargetAddress, _owner.MethodInfo); return _owner; }
    internal void VerifyCurrent(IntPtr module, IntPtr address, IntPtr methodInfo) => _owner.VerifyCurrent(module, address, methodInfo);
    // A fixed consumer profile can verify its actual grouped GetTime channel
    // without re-running installation proof. Unrelated standalone hooks retain
    // their existing complete verification until adopted by a real group.
    internal void VerifyOperationCurrent(IntPtr module, IntPtr address, IntPtr methodInfo)
    {
        if (!ReferenceEquals(_registered, this) || _thread != Environment.CurrentManagedThreadId)
            throw new UnsupportedSignatureException("The running GetTime lease belongs to its actual owner thread and private registration.");
        if (_owner.Module != module || _owner.TargetAddress != address || _owner.MethodInfo != methodInfo)
            throw new UnsupportedSignatureException("The running GetTime consumer identifies another module, target or method.");
        if (_owner.Group is { } group)
        {
            if (!ReferenceEquals(group.GetTimeLease, this))
                throw new UnsupportedSignatureException("The running consumer does not own the group's exact GetTime lease.");
            _ = group.ReadOperationClockPolicy(this);
        }
        else _owner.VerifyCurrent(module, address, methodInfo);
    }
    internal bool MatchesOriginalBody(string sha256, int length = 640) => _owner.MatchesOriginalBody(sha256, length);
    internal bool MatchesOriginalBody(IntPtr module, IntPtr address, IntPtr methodInfo, string sha256, int length)
        => _owner.MatchesOriginalBody(module, address, methodInfo, sha256, length);
    internal ExactHookBroker.ManagedBoundaryActivity ReadActivity() => _owner.ReadActivity();
    internal NativePlaybackSingleReturnHealth ReadHealth() => _owner.ReadHealth();
    internal (NativePlaybackSingleReturnAuthority? Authority, bool Active) ReadCurrent() => _owner.ReadCurrent();
    internal bool OwnsPublication(NativePlaybackSingleReturnAuthority authority) => _owner.OwnsPublication(authority);
    internal void Publish(long expectedRevision, NativePlaybackSingleReturnAuthority next) => _owner.Publish(expectedRevision, next);
    internal bool Withdraw(long expectedRevision) => _owner.WithdrawPolicy(expectedRevision);
    internal NativePlaybackSingleReturnBridge.DormantBootstrap PrepareDormant(NativePlaybackSingleReturnAuthority initial)
        => _owner.PrepareDormant(initial);
    public void Dispose() => _owner.Withdraw();
}
