using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeJudgeContextBridge
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ReadConfigurationNative(ref CurrentConfiguration value);
    [StructLayout(LayoutKind.Sequential)]
    private struct CurrentConfiguration
    {
        internal uint Version, Size, Configured, Enabled;
        internal IntPtr Entry;
        internal Configuration Configuration;
    }
    private readonly string _path;
    private readonly ReadConfigurationNative _readConfiguration;
    private readonly GetEntryNative _getEntry;
    private Configuration _configuredIdentity;
    private Func<NativeJudgeFrame, nuint>? _configuredEnter;
    private Action<NativeJudgeFrame, nuint, NativeJudgeExitFlags>? _configuredExit;
    private NativePlaybackExecutableSeal.RangeSeal? _enterMemory, _exitMemory;
    private NativePlaybackExecutableSeal? _imageAuthority;
    private bool _configureAttempted;

    // Issued only by this concrete process-lifetime bridge, never by an interface
    // implementation or a caller-supplied copy of a native configuration row.
    internal NativePlaybackExecutableSeal RetainCodeAuthority()
    {
        try
        {
            if (Volatile.Read(ref _unknown) != 0)
                throw new UnsupportedSignatureException("The native judge bridge retains sticky unknown responsibility.");
            if (_imageAuthority is null)
            {
                using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var sha = SHA256.Create();
                var hash = Convert.ToHexString(sha.ComputeHash(input)).ToLowerInvariant();
                _imageAuthority = NativePlaybackExecutableSeal.Capture(_library, hash);
            }
            _imageAuthority.Verify();
            if (!NativePlaybackExecutableSeal.ModulePath(_library).Equals(_path, StringComparison.OrdinalIgnoreCase)
                || NativeLibrary.GetExport(_library, "RizJudgeContext_Configure") != Marshal.GetFunctionPointerForDelegate(_configure)
                || NativeLibrary.GetExport(_library, "RizJudgeContext_ReadConfigurationV1") != Marshal.GetFunctionPointerForDelegate(_readConfiguration)
                || NativeLibrary.GetExport(_library, "RizJudgeContext_GetEntry") != Marshal.GetFunctionPointerForDelegate(_getEntry)
                || _getEntry() != EntryAddress)
                throw new UnsupportedSignatureException("The actual native judge helper image, exports or entry changed.");
            return _imageAuthority;
        }
        catch { Interlocked.Exchange(ref _unknown, 1); throw; }
    }
    internal void VerifyConfiguredOriginal(IntPtr preparedOriginal)
    {
        try
        {
            var image = RetainCodeAuthority();
            VerifyConfiguration(preparedOriginal);
            image.Verify();
            VerifyConfiguration(preparedOriginal);
        }
        catch { Interlocked.Exchange(ref _unknown, 1); throw; }
    }
    private void VerifyConfiguration(IntPtr preparedOriginal)
    {
        try
        {
            if (!_configured || !_configureAttempted || Volatile.Read(ref _unknown) != 0
                || preparedOriginal == IntPtr.Zero || _configuredIdentity.Original != preparedOriginal
                || _configuredEnter is null || _configuredExit is null
                || !ReferenceEquals(_enter, _configuredEnter) || !ReferenceEquals(_exit, _configuredExit)
                || _configuredIdentity.Enter != Marshal.GetFunctionPointerForDelegate(_enterNative)
                || _configuredIdentity.Exit != Marshal.GetFunctionPointerForDelegate(_exitNative))
                throw new UnsupportedSignatureException("The native judge owner lost its exact once-only Configure arguments.");
            _enterMemory!.VerifyMemoryIdentity(); _exitMemory!.VerifyMemoryIdentity();
            var current = new CurrentConfiguration { Version = 1, Size = 0x48 };
            if (_readConfiguration(ref current) != 0 || current.Version != 1 || current.Size != 0x48
                || current.Configured != 1 || current.Enabled != 1 || current.Entry != EntryAddress
                || current.Configuration.Version != _configuredIdentity.Version
                || current.Configuration.Size != _configuredIdentity.Size
                || current.Configuration.Original != preparedOriginal
                || current.Configuration.Enter != _configuredIdentity.Enter
                || current.Configuration.Exit != _configuredIdentity.Exit
                || current.Configuration.User != _configuredIdentity.User
                || current.Configuration.Enabled != _configuredIdentity.Enabled
                || current.Configuration.Reserved != _configuredIdentity.Reserved)
                throw new UnsupportedSignatureException("The actual native judge configured state, original, entry or callbacks changed.");
        }
        catch { Interlocked.Exchange(ref _unknown, 1); throw; }
    }
    private static NativePlaybackExecutableSeal.RangeSeal CaptureCallback(IntPtr address)
    {
        if (address == IntPtr.Zero || VirtualQuery(address, out var memory, (nuint)Marshal.SizeOf<CallbackMemoryInformation>()) == 0
            || memory.AllocationBase == IntPtr.Zero)
            throw new UnsupportedSignatureException("A native judge callback has no retained executable allocation.");
        return NativePlaybackExecutableSeal.RangeSeal.Capture(address, 1, memory.AllocationBase, false, true);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CallbackMemoryInformation
    {
        internal IntPtr BaseAddress, AllocationBase;
        internal uint AllocationProtect;
        internal ushort PartitionId;
        internal nuint RegionSize;
        internal uint State, Protect, Type;
    }
    [DllImport("kernel32")]
    private static extern nuint VirtualQuery(IntPtr address, out CallbackMemoryInformation memory, nuint length);
}
