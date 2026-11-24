using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativeCoroutineContextBridge
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ReadConfigurationNative(uint kind, uint slot, ref CurrentConfiguration value);
    [StructLayout(LayoutKind.Sequential)]
    private struct CurrentConfiguration
    {
        internal uint Version, Size, Configured;
        internal NativeCoroutineKind Kind;
        internal uint Slot, Reserved;
        internal IntPtr Entry;
        internal Configuration Configuration;
    }
    private readonly string _path;
    private readonly ReadConfigurationNative _readConfiguration;
    private readonly EntryNative _getEntry;
    private Configuration _configuredIdentity;
    private NativeCoroutineKind _configuredKind;
    private uint _configuredSlot;
    private Func<NativeCoroutineFrame, nuint>? _configuredEnter;
    private Action<NativeCoroutineFrame, nuint, nuint, NativeCoroutineExitFlags>? _configuredExit;
    private NativePlaybackExecutableSeal.RangeSeal? _enterMemory, _exitMemory;
    private NativePlaybackExecutableSeal? _imageAuthority;
    private bool _configureAttempted;
    internal uint Slot => _slot;

    // Only this concrete process-lifetime bridge retains its native helper and
    // reads its actual immutable slot. Interface implementations and copied CLR
    // configuration records never issue an executable-image owner.
    internal NativePlaybackExecutableSeal RetainCodeAuthority()
    {
        try
        {
            if (Volatile.Read(ref _unknown) != 0)
                throw new UnsupportedSignatureException("The native coroutine bridge retains sticky unknown responsibility.");
            if (_imageAuthority is null)
            {
                using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var sha = SHA256.Create();
                var hash = Convert.ToHexString(sha.ComputeHash(input)).ToLowerInvariant();
                _imageAuthority = NativePlaybackExecutableSeal.Capture(_library, hash);
            }
            _imageAuthority.Verify();
            if (!NativePlaybackExecutableSeal.ModulePath(_library).Equals(_path, StringComparison.OrdinalIgnoreCase)
                || NativeLibrary.GetExport(_library, "RizCoroutineContext_Configure") != Marshal.GetFunctionPointerForDelegate(_configure)
                || NativeLibrary.GetExport(_library, "RizCoroutineContext_ReadConfigurationV1") != Marshal.GetFunctionPointerForDelegate(_readConfiguration)
                || NativeLibrary.GetExport(_library, "RizCoroutineContext_GetEntry") != Marshal.GetFunctionPointerForDelegate(_getEntry)
                || _getEntry((uint)Kind, _slot) != EntryAddress)
                throw new UnsupportedSignatureException("The actual native coroutine helper image, exports, kind, slot or entry changed.");
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
                || _configuredKind != Kind || _configuredSlot != _slot
                || _configuredEnter is null || _configuredExit is null
                || !ReferenceEquals(_enter, _configuredEnter) || !ReferenceEquals(_exit, _configuredExit)
                || _configuredIdentity.Version != 1 || _configuredIdentity.Size != 0x28
                || _configuredIdentity.Enter != Marshal.GetFunctionPointerForDelegate(_enterNative)
                || _configuredIdentity.Exit != Marshal.GetFunctionPointerForDelegate(_exitNative))
                throw new UnsupportedSignatureException("The native coroutine owner lost its exact once-only Configure arguments.");
            _enterMemory!.VerifyMemoryIdentity(); _exitMemory!.VerifyMemoryIdentity();
            var current = new CurrentConfiguration { Version = 1, Size = 0x48 };
            if (_readConfiguration((uint)Kind, _slot, ref current) != 0 || current.Version != 1 || current.Size != 0x48
                || current.Configured != 1 || current.Kind != _configuredKind || current.Slot != _configuredSlot
                || current.Reserved != 0 || current.Entry != EntryAddress
                || current.Configuration.Version != _configuredIdentity.Version
                || current.Configuration.Size != _configuredIdentity.Size
                || current.Configuration.Original != preparedOriginal
                || current.Configuration.Enter != _configuredIdentity.Enter
                || current.Configuration.Exit != _configuredIdentity.Exit
                || current.Configuration.User != _configuredIdentity.User)
                throw new UnsupportedSignatureException("The actual native coroutine configured kind, slot, entry, original or callbacks changed.");
        }
        catch { Interlocked.Exchange(ref _unknown, 1); throw; }
    }
    private static NativePlaybackExecutableSeal.RangeSeal CaptureCallback(IntPtr address)
    {
        if (address == IntPtr.Zero || VirtualQuery(address, out var memory, (nuint)Marshal.SizeOf<CallbackMemoryInformation>()) == 0
            || memory.AllocationBase == IntPtr.Zero)
            throw new UnsupportedSignatureException("A native coroutine callback has no retained executable allocation.");
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
