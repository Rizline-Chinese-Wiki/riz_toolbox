using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalTweenBackend
{
    private NativeTweenAdmission? _admissionAuthority;
    private readonly Dictionary<long, NativeTweenAdmissionLease> _sourceClaims = new();
    private readonly Dictionary<long, NativeTweenSourceAdmissionSnapshot> _sourceAdmissionSnapshots = new();

    private void ConfigureAdmission(NativeLocalTweenWorkOwner owner)
    {
        RequireKnown();
        if (!ReferenceEquals(_owner, owner) || _admissionAuthority is not null)
            throw new InvalidOperationException("Actual Tween admission configures once for its exact retained source backend.");
        // Bind verifies the actual loaded producer and official class/cachedptr
        // metadata. Runtime keeps permanent policy responsibility if it fails.
        _admissionAuthority = _api.BindTweenAdmission();
        if (!_admissionAuthority.Production || _admissionAuthority.Unknown)
            throw new InvalidOperationException("The Tween source has no actual known native admission authority.");
        RequireKnown();
    }
    private void ClaimAdmission(NativeLocalTweenWorkOwner owner, long source)
    {
        RequireKnown();
        if (!ReferenceEquals(owner, _owner) || _admissionAuthority is null || !_admissionAuthority.Production)
            throw new InvalidOperationException("Source claim requires this actual configured native policy.");
        if (_sourceClaims.ContainsKey(source)) return; // Multiple persistent candidates can retain the same actual source.
        // Runtime records its own independent strong root before native Claim.
        // A rejected/unknown Claim stays in that authority's process registry.
        var lease = _admissionAuthority.Claim(Root(source), owner);
        _sourceClaims.Add(source, lease);
        CacheAdmission(source, lease.Read()); RequireKnown();
    }
    private void RefreshAdmissions()
    {
        if (_admissionAuthority is null) return;
        RequireKnown();
        foreach (var (source, lease) in _sourceClaims)
        { CacheAdmission(source, lease.Read()); RequireKnown(); }
    }
    private void CacheAdmission(long source, RizToolbox.Runtime.Hooks.NativeTweenAdmissionSnapshot observed)
    {
        if (!observed.Known || observed.Object != source)
            throw new InvalidOperationException("The actual native claim observation is unknown or belongs to another source.");
        _sourceAdmissionSnapshots[source] = new(source, observed.Known, observed.Phase, observed.Slot, observed.Epoch,
            observed.CachedNative, observed.IssuersInFlight, observed.ActiveEntries, observed.AdmittedIssuers,
            observed.DeniedIssuers, observed.DeniedDead);
    }
    internal bool TrySealAdmissions(NativeLocalTweenWorkOwner owner)
    {
        Check();
        try
        {
            if (!ReferenceEquals(_owner, owner) || _admissionAuthority is null || !_admissionAuthority.Production)
                throw new InvalidOperationException("Only this retained production source backend can seal its native claims.");
            owner.VerifyTweenAdmissionOwner();
            owner.RequireAdmissionSourceSet(_sourceClaims.Keys);
            foreach (var (source, lease) in _sourceClaims)
            { CacheAdmission(source, lease.Seal()); RequireKnown(); }
            // Re-read every actual private lease after issuing all seals. No
            // diagnostic DTO or a previously cached Sealed scalar is an issuer.
            var sealedSources = true;
            foreach (var (source, lease) in _sourceClaims)
            {
                var observed = lease.Read(); CacheAdmission(source, observed); RequireKnown();
                if (!observed.IsSealed || observed.InFlight != 0) sealedSources = false;
            }
            var activity = ReadActivity(); RequireKnown();
            return sealedSources && activity.Known && activity.InFlight == 0;
        }
        catch { CleanupUnknown = true; throw; }
    }
    internal IReadOnlyList<NativeTweenSourceAdmissionSnapshot> ReadCachedSourceAdmissions()
    {
        _main.VerifyAccess();
        // Copied diagnostics need no further native reads after a source fault.
        return Array.AsReadOnly(_sourceAdmissionSnapshots.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray());
    }
}
