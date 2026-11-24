namespace RizToolbox.Runtime.Hooks;

// Lifetime checks for an owner that has already been established. No bridge
// factory accepts this interface as a publication or a Ready credential.
internal interface INativeEnginePublicationLifetime
{
    bool Unknown { get; }
    IntPtr KernelModule { get; }
    string KernelPath { get; }
    ulong TargetModule { get; }
    void VerifyReady();
    void MarkUnknown();
}

// Preserve the existing V4 owned path without changing its owner or permission.
internal sealed class NativeEngineV4PublicationLifetime : INativeEnginePublicationLifetime
{
    private readonly NativeEngineFirstInstall _owner;
    internal NativeEngineV4PublicationLifetime(NativeEngineFirstInstall owner) => _owner = owner;
    public bool Unknown => _owner.Unknown;
    public IntPtr KernelModule => _owner.KernelModule;
    public string KernelPath => _owner.KernelPath;
    public ulong TargetModule => _owner.Publication.TargetModule;
    public void VerifyReady() => _owner.VerifyReady();
    public void MarkUnknown() => _owner.MarkUnknown();
}
