namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    // Installation and collision decisions remain under Gate. Exact identity
    // reads also serve native worker callbacks, which must not wait on an
    // owner-thread graph getter holding Gate and waiting for that callback.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, PlaybackOwner> PlaybackInstallations = new();
    internal NativePlaybackGetTimeHookLease BindPlaybackGetTime(VerifiedHookTarget target, IntPtr module,
        string expectedImageSha256, int originalBodyLength, string originalBodySha256,
        string helperPath, string helperSha256, Func<NativePlaybackSingleReturnSample> readClock,
        Action<NativePlaybackSingleReturnFault> faultOwner, Action<NativePlaybackSingleReturnFrame>? before = null,
        Action<NativePlaybackSingleReturnFrame, NativePlaybackSingleReturnCompletion>? after = null)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(readClock); ArgumentNullException.ThrowIfNull(faultOwner);
        if (_engine is not DobbyDetourEngine dobby || target.Abi != ExactHookAbi.InstanceSingleReturn0)
            throw new UnsupportedSignatureException("GetTime installation requires the concrete inspected Dobby engine and the instance Single() ABI.");
        lock (Gate)
        {
            if (Entries.ContainsKey(target.Address) || PlaybackInstallations.ContainsKey(target.Address))
                throw new UnsupportedSignatureException("This native address already has a broker installation or a retained failed attempt.");
            var owner = PlaybackOwner.Preflight(target, module, expectedImageSha256, originalBodyLength, originalBodySha256, dobby,
                helperPath, helperSha256, readClock, faultOwner, before, after);
            // Failed and withdrawn attempts stay in the existing collision domain.
            Entries.Add(target.Address, new Entry { Target = target, Engine = dobby, Replacement = (Action)(() => { }),
                NativeInstallFailure = "GetTime uses its private Single-return installation owner." });
            if (!PlaybackInstallations.TryAdd(target.Address, owner))
                throw new UnsupportedSignatureException("The GetTime address already retains another installation owner.");
            owner.Install();
            return NativePlaybackGetTimeHookLease.Issue(owner);
        }
    }
    internal static bool OwnsPlaybackInstallation(PlaybackOwner candidate)
    {
        return PlaybackInstallations.TryGetValue(candidate.TargetAddress, out var owner) && ReferenceEquals(owner, candidate);
    }
}
