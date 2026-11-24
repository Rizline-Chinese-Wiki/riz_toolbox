namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeEngineTransportFirstInstall
{
    // Playback can compose only the actual retained V5 target owner. No
    // publication DTO, caller-supplied patch or successful-read flag can issue
    // this authority. Owned fixtures use their existing profile-one owner;
    // production still enters through the ordinary fixed admission gate.
    internal static NativeEngineTransportFirstInstall? RetainPlaybackImage(IntPtr module, string imageSha256)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || module == IntPtr.Zero) return null;
        NativeEngineTransportFirstInstall? owner;
        lock (OwnersGate)
        {
            var matches = Owners.Values.Where(value => value.TargetModule == Address(module)).ToArray();
            if (matches.Length > 1)
                throw new UnsupportedSignatureException("Playback cannot compose competing retained V5 target owners.");
            owner = matches.SingleOrDefault();
        }
        if (owner is null)
        {
            if (GetModuleHandle("UnityPlayer.dll") != module
                || GetModuleHandle("RizToolbox.EngineBootstrap.dll") == IntPtr.Zero || !ProductionStartupAdmissionKnown)
                return null;
            owner = RequireReady();
        }
        lock (owner._gate)
        {
            owner.RequirePlaybackImage(module, imageSha256);
            owner.VerifyReady();
        }
        return owner;
    }

    private void RequirePlaybackImage(IntPtr module, string imageSha256)
    {
        if (module == IntPtr.Zero || Address(module) != TargetModule || module != _modules[2].Handle
            || !string.Equals(imageSha256, _modules[2].Hash, StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("Playback transport composition requires the exact independently pinned V5 target image.");
    }

    // An installation read must own its own full native entry/exit. An outer
    // operation may perform real writes, so its verification depth cannot
    // sponsor this independent read interval or a later installation phase.
    internal void VerifyPlaybackInstallationRead(IntPtr module, string imageSha256, Action read)
    {
        ArgumentNullException.ThrowIfNull(read);
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? playbackFailure = null;
        lock (_gate)
        {
            RequirePlaybackImage(module, imageSha256);
            if (_verificationDepth != 0 || _activityOwnerThread != 0 || _activityCoordinator is not null)
                throw new UnsupportedSignatureException("Playback installation verification must begin outside another V5 operation or activity read.");

            VerifyOperation(() =>
            {
                try { read(); }
                catch (Exception error) { playbackFailure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error); }
                return 0;
            });
        }
        // Finish the full V5 exit before propagating an independent playback
        // failure. A V5 verifier failure still makes its own owner Unknown.
        playbackFailure?.Throw();
    }

    internal void VerifyPlaybackMembersDisjoint(IntPtr module, string imageSha256,
        IReadOnlyList<(IntPtr Address, int Length)> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        lock (_gate)
        {
            RequirePlaybackImage(module, imageSha256); VerifyReady();
            foreach (var member in members)
            {
                var relative = member.Address.ToInt64() - module.ToInt64();
                if (relative < 0 || member.Length <= 0 || (ulong)relative + (ulong)member.Length > _modules[2].ImageSize)
                    throw new UnsupportedSignatureException("A playback member leaves the exact retained V5 target image.");
                foreach (var window in _wire.Windows)
                    if ((ulong)relative < (ulong)window.PhysicalRva + window.PhysicalBytes
                        && (ulong)window.PhysicalRva < (ulong)relative + (ulong)member.Length)
                        throw new UnsupportedSignatureException("A playback member overlaps a retained V5 transport window.");
            }
        }
    }

    // Playback supplies complete sections independently reconstructed from its
    // held file plus DIR64. Both boundaries use the existing full native
    // and independent managed verifier. Every contribution must still see its
    // exact original bytes; an overlapping/foreign preimage is never skipped.
    // Only managed expected bytes change. Actual image/code is read-only here.
    internal ulong ComposeVerifiedPlaybackWindows(IntPtr module, string imageSha256, uint sectionRva, byte[] expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        lock (_gate)
        {
            RequirePlaybackImage(module, imageSha256); VerifyReady();
            if (expected.Length == 0 || sectionRva >= _modules[2].ImageSize
                || (ulong)sectionRva + (ulong)expected.Length > _modules[2].ImageSize)
                throw new UnsupportedSignatureException("Playback transport composition requires a bounded target image range.");
            var sectionEnd = (ulong)sectionRva + (ulong)expected.Length;
            ulong covered = 0;
            for (var index = 0; index < NativeEngineTransportPublicationLayout.WindowCount; ++index)
            {
                var window = _wire.Windows[index];
                var end = (ulong)window.PhysicalRva + window.PhysicalBytes;
                if (end <= sectionRva || window.PhysicalRva >= sectionEnd) continue;
                if (window.PhysicalRva < sectionRva || end > sectionEnd)
                    throw new UnsupportedSignatureException("A retained V5 window crosses a playback executable section.");
                var relative = checked((int)(window.PhysicalRva - sectionRva));
                var bytes = checked((int)window.PhysicalBytes);
                if (!expected.AsSpan(relative, bytes).SequenceEqual(_modules[2].Disk(window.PhysicalRva, bytes)))
                    throw new UnsupportedSignatureException("A retained V5 window's playback preimage differs from its independent pinned file.");
                covered |= 1UL << index;
            }
            // Validate all preimages before changing any expected bytes.
            for (var index = 0; index < NativeEngineTransportPublicationLayout.WindowCount; ++index)
                if ((covered & (1UL << index)) != 0)
                {
                    var window = _wire.Windows[index];
                    window.PatchBytes.AsSpan(0, checked((int)window.PhysicalBytes))
                        .CopyTo(expected.AsSpan(checked((int)(window.PhysicalRva - sectionRva))));
                }
            VerifyReady();
            return covered;
        }
    }
}
