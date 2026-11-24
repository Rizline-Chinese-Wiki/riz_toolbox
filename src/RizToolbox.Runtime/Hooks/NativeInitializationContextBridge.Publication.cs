using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeInitializationContextBridge
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Publication
    {
        internal uint Version, Size, Configured;
        internal NativeInitializationKind Kind;
        internal IntPtr Entry, Slot;
        internal Configuration Configuration;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ReadPublicationNative(uint kind, ref Publication publication);

    private readonly string _path;
    private readonly GetEntryNative _getEntry;
    private readonly ReadPublicationNative _readPublication;
    private Configuration _configuredIdentity;
    private int _configureAttempted;
    private IntPtr _slotIdentity;
    private NativePlaybackExecutableSeal.RangeSeal? _slot, _enterCode, _exitCode;

    internal bool SupportsPublicationReadback { get; }

    internal NativePlaybackExecutableSeal RetainCodeAuthority()
    {
        if (!SupportsPublicationReadback || Volatile.Read(ref _unknown) != 0)
            throw new UnsupportedSignatureException("The native initialization bridge has no known configuration readback.");
        VerifyNativeEntries();
        using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(input)).ToLowerInvariant();
        return NativePlaybackExecutableSeal.Capture(_library, hash);
    }

    internal void VerifyPublishedConfiguration(IntPtr original)
    {
        try
        {
            if (!_configured || Volatile.Read(ref _configureAttempted) != 1 || Volatile.Read(ref _unknown) != 0
                || original == IntPtr.Zero || original != _configuredIdentity.Original || _slot is null
                || _configuredIdentity.Enter != Marshal.GetFunctionPointerForDelegate(_enterNative)
                || _configuredIdentity.Exit != Marshal.GetFunctionPointerForDelegate(_exitNative)
                || _configuredIdentity.User == IntPtr.Zero
                || !ReferenceEquals(GCHandle.FromIntPtr(_configuredIdentity.User).Target, this))
                throw new UnsupportedSignatureException("The native initialization bridge lost its original or exact callback owner.");
            VerifyNativeEntries();
            _slot.VerifyMemoryIdentity();
            _enterCode!.VerifyMemoryIdentity();
            _exitCode!.VerifyMemoryIdentity();
            var actual = ReadPublication();
            var configured = actual.Configuration;
            if (actual.Slot != _slotIdentity || configured.Version != _configuredIdentity.Version
                || configured.Size != _configuredIdentity.Size || configured.Original != original
                || configured.Enter != _configuredIdentity.Enter || configured.Exit != _configuredIdentity.Exit
                || configured.User != _configuredIdentity.User)
                throw new UnsupportedSignatureException("The actual native initialization slot, original or callbacks changed.");
        }
        catch
        {
            MarkCodeIdentityUnknown();
            throw;
        }
    }

    internal void MarkCodeIdentityUnknown()
        => Unknown("The native initialization image or published configuration is unknown.");

    private void VerifyNativeEntries()
    {
        if (Marshal.SizeOf<Publication>() != 0x48
            || !NativePlaybackExecutableSeal.ModulePath(_library).Equals(_path, StringComparison.OrdinalIgnoreCase)
            || NativeLibrary.GetExport(_library, "RizInitializationContext_Configure") != Marshal.GetFunctionPointerForDelegate(_configure)
            || NativeLibrary.GetExport(_library, "RizInitializationContext_GetStats") != Marshal.GetFunctionPointerForDelegate(_readPublication)
            || NativeLibrary.GetExport(_library, "RizInitializationContext_GetEntry") != Marshal.GetFunctionPointerForDelegate(_getEntry)
            || _getEntry((uint)Kind) != EntryAddress)
            throw new UnsupportedSignatureException("The native initialization helper, entry or exports changed.");
        HookTargetValidation.VerifyExecutable(EntryAddress, _library);
    }

    private Publication ReadPublication()
    {
        var actual = NewPublication();
        if (!SupportsPublicationReadback || _readPublication((uint)Kind, ref actual) != 0 || !NamesPublication(actual))
            throw new UnsupportedSignatureException("The requested native initialization kind has no ready publication.");
        return actual;
    }

    private bool NamesPublication(Publication row)
        => row.Version == 2 && row.Size == 0x48 && row.Configured == 1 && row.Kind == Kind
            && row.Entry == EntryAddress && row.Slot != IntPtr.Zero;

    private static Publication NewPublication() => new() { Version = 2, Size = 0x48 };

    private static NativePlaybackExecutableSeal.RangeSeal CaptureCallback(IntPtr address)
        => NativePlaybackExecutableSeal.RangeSeal.Capture(address, 1, IntPtr.Zero, false, true);
}
