using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RizToolbox.Runtime.Hooks;

/// <summary>Read-only adoption of the native, startup-owned engine publication.
/// No method here loads a target DLL, configures a Kernel channel, patches a
/// source, or turns a caller-created receipt into game Ready authority.</summary>
internal sealed class NativeEngineFirstInstall
{
    internal const string ProductionKernelSha256 = "923486d2901ab6f66ea6d90d99cc65b1afafbc3275c338f0fd71ea5d22bc5662";
    internal const string ProductionTargetSha256 = "43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1";
    internal const string ProductionExecutableSha256 = "f9c37f1078619d02c62a7a5815ca48179666efb2dfe7e6e698bfff7858c26e5e";
    // EngineGameProfile.h records these as facts-only. Production admission
    // cannot be supplied by JSON, a receipt flag, a managed caller or profile 1.
    internal static bool ProductionStartupAdmissionKnown => false;
    private static readonly object OwnersGate = new();
    private static readonly Dictionary<string, NativeEngineFirstInstall> Owners = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RejectedOwners = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<object> PermanentRoots = new();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ValueNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(IntPtr output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InfoNative(uint kind, uint slot, ref NativeEngineNodeInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EntryNative(uint kind, uint slot);
    private readonly ReadNative _read;
    private readonly ValueNative _verify;
    private readonly InfoNative _kernelInfo;
    private readonly EntryNative _kernelEntry;
    private readonly ModuleFacts[] _modules;
    private readonly NativeEnginePublication _publication;
    private readonly NativeEngineProductionProfileData? _productionProfile;
    private readonly ModuleFacts? _doorstop;
    private readonly uint _expectedProfile;
    private readonly object _gate = new();
    private readonly bool[] _attached = new bool[4];
    private int _unknown;
    internal IntPtr KernelModule => _modules[1].Handle;
    internal string KernelPath => _modules[1].Path;
    internal NativeEnginePublication Publication => _publication;
    internal bool Unknown => Volatile.Read(ref _unknown) != 0;

    internal static NativeEngineFirstInstall RequireReady()
    {
        // Generated identity constants cannot authorize the execution window.
        // This independent gate stays false until that native contract is proven.
        if (!ProductionStartupAdmissionKnown)
            throw new UnsupportedSignatureException("CapabilityUnavailable: engine startup execution-window admission remains unproven.");
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new PlatformNotSupportedException("Engine publication adoption requires Windows x64.");
        var profile = NativeEngineProductionProfile.Read()
            ?? throw new UnsupportedSignatureException("CapabilityUnavailable: this runtime has no compiled production candidate fingerprints.");
        var executable = Environment.ProcessPath ?? throw new UnsupportedSignatureException("The actual process image cannot be identified.");
        if (!string.Equals(System.IO.Path.GetFileName(executable), "Rizline.exe", StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("Production publication adoption requires the fixed game executable.");
        var directory = System.IO.Path.GetDirectoryName(Canonical(executable))!;
        var paths = new[] { "RizToolbox.EngineBootstrap.dll", "RizToolbox.EngineNodeKernel.dll", "UnityPlayer.dll", "Rizline.exe" }
            .Select(name => Canonical(System.IO.Path.Combine(directory, name))).ToArray();
        var hashes = Enumerable.Range(0, 4).Select(profile.Hash).ToArray();
        lock (OwnersGate)
        {
            if (RejectedOwners.Contains(paths[0])) throw new UnsupportedSignatureException("This production engine owner is permanently Unknown.");
            try
            {
                if (!Owners.TryGetValue(paths[0], out var owner))
                { owner = new(paths, hashes, profile); Owners.Add(paths[0], owner); }
                else if (owner._expectedProfile != 2)
                    throw new UnsupportedSignatureException("An owned fixture cannot supply production game authority.");
                owner.VerifyReady(); return owner;
            }
            catch { RejectedOwners.Add(paths[0]); throw; }
        }
    }

    // Test-internal entry: all paths and hashes come from the exact parent-held
    // owned build. They never enter the public SDK or authorize game profile 2.
    internal static NativeEngineFirstInstall RequireOwnedPublished(
        string bootstrapPath, string bootstrapSha256, string kernelPath, string kernelSha256,
        string targetPath, string targetSha256, string executablePath, string executableSha256)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new PlatformNotSupportedException("Engine publication adoption requires Windows x64.");
        var paths = new[] { Canonical(bootstrapPath), Canonical(kernelPath), Canonical(targetPath), Canonical(executablePath) };
        var hashes = new[] { Digest(bootstrapSha256), Digest(kernelSha256), Digest(targetSha256), Digest(executableSha256) };
        if (GetModuleHandle("UnityPlayer.dll") != IntPtr.Zero || GetModuleHandle("GameAssembly.dll") != IntPtr.Zero
            || string.Equals(System.IO.Path.GetFileName(paths[3]), "Rizline.exe", StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("The owned publication entry cannot adopt a game process.");
        lock (OwnersGate)
        {
            if (RejectedOwners.Contains(paths[0])) throw new UnsupportedSignatureException("This engine publication owner is permanently unknown.");
            try
            {
                if (!Owners.TryGetValue(paths[0], out var owner))
                {
                    owner = new(paths, hashes); Owners.Add(paths[0], owner);
                }
                else if (!owner._modules.Select(x => x.Path).SequenceEqual(paths, StringComparer.OrdinalIgnoreCase)
                    || !owner._modules.Select(x => x.Hash).SequenceEqual(hashes, StringComparer.Ordinal))
                    throw new UnsupportedSignatureException("The existing publication owner has different independently supplied exact build facts.");
                owner.VerifyReady(); return owner;
            }
            catch { RejectedOwners.Add(paths[0]); throw; }
        }
    }

    private NativeEngineFirstInstall(string[] paths, string[] hashes, NativeEngineProductionProfileData? productionProfile = null)
    {
        _productionProfile = productionProfile; _expectedProfile = productionProfile is null ? 1u : 2u;
        _modules = paths.Select((path, index) => new ModuleFacts(path, hashes[index], index == 3)).ToArray();
        if (_modules.Select(x => x.Handle).Distinct().Count() != 4)
            throw new UnsupportedSignatureException("Four distinct already-loaded publication images are required.");
        if (productionProfile is not null)
        {
            _doorstop = new ModuleFacts(Canonical(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(paths[3])!, "winhttp.dll")), productionProfile.Hash(4), false);
            if (_modules.Any(module => module.Handle == _doorstop.Handle))
                throw new UnsupportedSignatureException("The production Doorstop must be a fifth distinct loaded module.");
        }
        _read = Export<ReadNative>(_modules[0], "RizEngineFirstInstall_GetInfoV4");
        _verify = Export<ValueNative>(_modules[0], "RizEngineFirstInstall_VerifyV4");
        if (Export<ValueNative>(_modules[0], "RizEngineFirstInstall_GetVersion")() != 4)
            throw new UnsupportedSignatureException("Only actual publication V4 can authorize adoption.");
        _kernelInfo = Export<InfoNative>(_modules[1], "RizEngineNodeContext_GetInfo");
        _kernelEntry = Export<EntryNative>(_modules[1], "RizEngineNodeContext_GetEntry");
        if (Export<ValueNative>(_modules[1], "RizEngineNodeContext_GetAbiVersion")() != 2
            || Export<ValueNative>(_modules[1], "RizEngineNodeContext_GetLimit")() != 32
            || Export<ValueNative>(_modules[1], "RizEngineNodeContext_GetFrameSize")() != 112
            || Export<ValueNative>(_modules[1], "RizEngineNodeContext_GetCompletionSize")() != 32
            || Marshal.SizeOf<NativeEngineNodeInformation>() != 112 || Marshal.SizeOf<NativeEngineNodeFrame>() != 112
            || Marshal.SizeOf<NativeEngineNodeCompletion>() != 32)
            throw new UnsupportedSignatureException("Kernel ABI2/scalar1/frame112/completion32/info112 differ from publication V4.");
        _publication = Read();
        lock (OwnersGate) PermanentRoots.Add(this); // Keep held files, modules and all native delegates even after refusal.
        VerifyReady();
    }

    internal NativeEngineNodeChannelIdentity Identity(NativeEngineNodeKind kind)
    {
        VerifyReady(); return _publication.RowAt(kind).Identity;
    }

    internal void MarkUnknown() => Interlocked.Exchange(ref _unknown, 1);

    internal void VerifyReady()
    {
        lock (_gate)
        {
            if (Unknown) throw new UnsupportedSignatureException("Engine publication ownership is permanently unknown.");
            try
            {
                foreach (var module in _modules) module.VerifyCurrent();
                _doorstop?.VerifyCurrent();
                var before = Read();
                if (!NativeEnginePublication.HasReadyShape(before, _expectedProfile) || !before.SameImmutableRecord(_publication)
                    || before.ProcessId != unchecked((uint)Environment.ProcessId) || RetryBusy(() => _verify()) != 0)
                    throw new UnsupportedSignatureException("Native V4 publication is not the same complete, verified Ready record.");
                var after = Read();
                if (!after.SameImmutableRecord(before) || !after.SameImmutableRecord(_publication))
                    throw new UnsupportedSignatureException("Native V4 changed across its read-only verification.");
                for (var index = 0; index < 4; ++index) VerifyModule(before.ModuleAt(index), _modules[index], index);
                if (_productionProfile is not null) VerifyProductionGraph();
                for (var index = 0; index < 4; ++index) VerifyRow(before.RowAt((NativeEngineNodeKind)index), index);
            }
            catch { MarkUnknown(); throw; }
        }
    }

    private NativeEnginePublication Read()
    {
        var buffer = Marshal.AllocHGlobal(NativeEnginePublication.ByteSize);
        try
        {
            var data = new byte[NativeEnginePublication.ByteSize];
            BinaryPrimitives.WriteUInt32LittleEndian(data, 4); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 4096);
            Marshal.Copy(data, 0, buffer, data.Length);
            if (RetryBusy(() => _read(buffer)) != 0) throw new UnsupportedSignatureException("Native V4 snapshot is invalid, busy or unavailable.");
            Marshal.Copy(buffer, data, 0, data.Length); return NativeEnginePublication.Decode(data);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static uint RetryBusy(Func<uint> call)
    {
        var started = Environment.TickCount64;
        while (true)
        {
            var result = call();
            if (result != 2 || Environment.TickCount64 - started >= 5000) return result;
            Thread.Sleep(1); // Only the explicit read-only ABI busy result is retryable.
        }
    }

    private void VerifyProductionGraph()
    {
        var bootstrap = _modules[0]; var kernel = _modules[1]; var unity = _modules[2]; var executable = _modules[3];
        var doorstop = _doorstop ?? throw new UnsupportedSignatureException("The independently held production Doorstop is missing.");
        executable.VerifyImportsTo(unity, new[] { "UnityMain2" });
        unity.VerifyImportsTo(doorstop, new[] { "WinHttpOpen", "WinHttpGetProxyForUrl", "WinHttpGetIEProxyConfigForCurrentUser", "WinHttpCloseHandle" });
        doorstop.VerifyImportsTo(bootstrap, new[] { "RizEngineFirstInstall_GetVersion", "RizEngineFirstInstall_GetInfoV4",
            "RizEngineFirstInstall_VerifyV4", "RizEngineFirstInstall_AbortAfterPublication" });
        bootstrap.VerifyImportsTo(kernel, new[] { "RizEngineNodeContext_GetAbiVersion", "RizEngineNodeContext_GetLimit",
            "RizEngineNodeContext_GetFrameSize", "RizEngineNodeContext_GetCompletionSize", "RizEngineNodeContext_GetEntry",
            "RizEngineNodeContext_Configure", "RizEngineNodeContext_Attach", "RizEngineNodeContext_GetInfo" });
    }

    private static T Export<T>(ModuleFacts module, string name) where T : Delegate
    {
        var expected = checked(Address(module.Handle) + module.ExportRva(name));
        if (!NativeLibrary.TryGetExport(module.Handle, name, out var address) || Address(address) != expected)
            throw new UnsupportedSignatureException("An already-loaded exact native module has a missing or changed fixed export: " + name + ".");
        Memory(address, 1, module.Handle, 0x20);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static void VerifyModule(NativeEnginePublication.Module record, ModuleFacts module, int kind)
    {
        if (record.Kind != kind || record.Address != Address(module.Handle) || record.Machine != module.Machine
            || record.ImageSize != module.ImageSize || record.TimeDateStamp != module.Timestamp || record.FileBytes != (ulong)module.Data.Length
            || record.FileIndex != module.FileIndex || record.VolumeSerial != module.VolumeSerial
            || !record.Sha256.SequenceEqual(Convert.FromHexString(module.Hash)) || record.EntryRva != module.EntryRva
            || record.ExceptionRva != module.ExceptionRva || record.Flags != (kind == 0 ? 7u : 15u) || record.HeldFile == 0)
            throw new UnsupportedSignatureException("Native V4 module identity differs from independently held loaded/disk facts.");
        if (!GetFileInformationByHandle(NativeEnginePublication.Pointer(record.HeldFile), out var identity)
            || identity.Volume != module.VolumeSerial || (((ulong)identity.IndexHigh << 32) | identity.IndexLow) != module.FileIndex
            || (((ulong)identity.SizeHigh << 32) | identity.SizeLow) != (ulong)module.Data.Length)
            throw new UnsupportedSignatureException("The native retained file handle has a different actual file identity.");
    }

    private void VerifyRow(NativeEnginePublication.Row row, int kind)
    {
        var target = _modules[2]; var bootstrap = _modules[0]; var kernel = _modules[1];
        var names = new[] { "RizFixedOwnedStart", "RizFixedOwnedExecute", "RizFixedOwnedRelease", "RizFixedOwnedDetach" };
        if (_productionProfile is null)
        {
            if (!NativeLibrary.TryGetExport(target.Handle, names[kind], out var exported) || row.Target != Address(exported))
                throw new UnsupportedSignatureException("Owned source does not match its fixed actual target export.");
        }
        else if (row.Rva != _productionProfile.FunctionRva((NativeEngineNodeKind)kind)
            || row.Target != Address(target.Handle) + _productionProfile.FunctionRva((NativeEngineNodeKind)kind))
            throw new UnsupportedSignatureException("Production source differs from the independently compiled fixed game RVA.");
        var original = target.Disk(row.Rva, 32);
        if (!row.OriginalBytes.SequenceEqual(original) || !row.ModuleSha256.SequenceEqual(Convert.FromHexString(target.Hash)))
            throw new UnsupportedSignatureException("V4 original source bytes differ from the independently held target image.");
        var family = target.Family(row.Rva);
        if (family.Count != row.RuntimeFunctionCount)
            throw new UnsupportedSignatureException("V4 omitted part of the actual chained source unwind family.");
        var end = family.Max(x => x.End);
        var body = target.Disk(row.Rva, checked((int)(end - row.Rva)));
        if (!row.BodySha256.SequenceEqual(SHA256.HashData(body)))
            throw new UnsupportedSignatureException("V4 full source body hash differs from independent disk/unwind facts.");
        row.PatchBytes[..checked((int)row.PrefixBytes)].CopyTo(body);
        if (!ReadMemory(NativeEnginePublication.Pointer(row.Target), body.Length, target.Handle, 0x20).AsSpan().SequenceEqual(body)
            || !ReadMemory(NativeEnginePublication.Pointer(row.Target), 32, target.Handle, 0x20).AsSpan().SequenceEqual(row.ObservedBytes))
            throw new UnsupportedSignatureException("Actual complete source body/patch/protection differs from the fixed publication.");
        var prefix = NativeEnginePublication.Prefix((NativeEngineNodeKind)kind);
        var thunk = ReadMemory(NativeEnginePublication.Pointer(row.Original), prefix.Length + 10, bootstrap.Handle, 0x20);
        if (!thunk.AsSpan(0, prefix.Length).SequenceEqual(prefix)
            || !thunk.AsSpan(prefix.Length, 3).SequenceEqual(new byte[] { 0x4c, 0x8b, 0x1d })
            || !thunk.AsSpan(prefix.Length + 7, 3).SequenceEqual(new byte[] { 0x41, 0xff, 0xe3 }))
            throw new UnsupportedSignatureException("Retained original is not its fixed prefix and typed continuation tail.");
        var continuationSlot = checked((long)row.Original + prefix.Length + 7 + BinaryPrimitives.ReadInt32LittleEndian(thunk.AsSpan(prefix.Length + 3, 4)));
        var continuation = ReadMemory(new IntPtr(continuationSlot), 8, bootstrap.Handle, 0x04);
        if (BinaryPrimitives.ReadUInt64LittleEndian(continuation) != row.Continuation)
            throw new UnsupportedSignatureException("Retained original continuation points to a different source instruction.");
        Memory(NativeEnginePublication.Pointer(row.Entry), 1, kernel.Handle, 0x20);
        if (_kernelEntry((uint)kind, row.HelperSlot) != NativeEnginePublication.Pointer(row.Entry))
            throw new UnsupportedSignatureException("V4 entry differs from its exact Kernel kind/slot table.");
        var info = new NativeEngineNodeInformation { Version = 1, Size = 112 };
        if (_kernelInfo((uint)kind, row.HelperSlot, ref info) != 0
            || !NativeEngineNodeContextBridge.MatchesInformation(info, row.Identity, null)
            || info.Entered > long.MaxValue || info.Exited > long.MaxValue || (_attached[kind] && info.Attached == 0))
            throw new UnsupportedSignatureException("Live Kernel channel lost its exact configured tuple or acquired a fault/Unknown.");
        if (info.Attached == 1) _attached[kind] = true;
        foreach (var function in family) target.VerifyUnwind(function);
        var originalRva = checked((uint)(row.Original - Address(bootstrap.Handle)));
        foreach (var function in bootstrap.Family(originalRva)) bootstrap.VerifyUnwind(function);
    }

    private static string Canonical(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!System.IO.Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute native module path is required.", nameof(path));
        return System.IO.Path.GetFullPath(path);
    }
    private static string Digest(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("An independent SHA256 build fingerprint is required.", nameof(value));
        return value.ToLowerInvariant();
    }
    private static ulong Address(IntPtr value) => unchecked((ulong)value.ToInt64());
    private static string ModulePath(IntPtr module)
    {
        var text = new StringBuilder(32768); var count = GetModuleFileName(module, text, text.Capacity);
        if (count is 0 or >= 32768) throw new UnsupportedSignatureException("Cannot identify the loaded native module path.");
        return Canonical(text.ToString());
    }
    private static void Memory(IntPtr address, int bytes, IntPtr module, uint protection)
    {
        if (bytes <= 0 || Address(address) > ulong.MaxValue - (ulong)bytes) throw new UnsupportedSignatureException("Invalid native memory range.");
        var current = Address(address); var end = current + (ulong)bytes;
        while (current < end)
        {
            if (VirtualQuery(NativeEnginePublication.Pointer(current), out var memory, (nuint)Marshal.SizeOf<MemoryInformation>()) == 0
                || memory.AllocationBase != module || memory.State != 0x1000 || memory.Type != 0x1000000
                || (memory.Protect & 0x101) != 0 || (protection != 0 && memory.Protect != protection)
                || current < Address(memory.BaseAddress) || current - Address(memory.BaseAddress) >= (ulong)memory.RegionSize)
                throw new UnsupportedSignatureException("Actual native range is not committed exact-module image memory with the required protection.");
            var next = Address(memory.BaseAddress) + (ulong)memory.RegionSize;
            if (next <= current) throw new UnsupportedSignatureException("Invalid native memory region bounds.");
            current = Math.Min(next, end);
        }
    }
    private static byte[] ReadMemory(IntPtr address, int bytes, IntPtr module, uint protection)
    {
        Memory(address, bytes, module, protection); var data = new byte[bytes];
        if (!ReadProcessMemory(GetCurrentProcess(), address, data, (nuint)bytes, out var read) || read != (nuint)bytes)
            throw new UnsupportedSignatureException("Cannot copy the exact native publication memory range.");
        return data;
    }

    private sealed class ModuleFacts
    {
        internal readonly string Path, Hash;
        internal readonly IntPtr Handle;
        internal readonly byte[] Data;
        internal readonly uint Machine, ImageSize, Timestamp, EntryRva, ExceptionRva, VolumeSerial;
        internal readonly ulong FileIndex;
        private readonly FileStream _held;
        private readonly bool _executable;
        private readonly uint _headerSize;
        private readonly uint _importRva, _importBytes, _exportRva, _exportBytes;
        private readonly List<(uint Rva, uint Raw, uint Bytes)> _sections = new();
        private readonly List<Function> _functions = new();
        internal ModuleFacts(string path, string hash, bool executable)
        {
            Path = path; Hash = hash; _executable = executable;
            Handle = executable ? GetModuleHandle(null) : GetModuleHandle(path);
            if (Handle == IntPtr.Zero || !string.Equals(ModulePath(Handle), path, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The exact native publication dependency is not already loaded from its canonical path.");
            _held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            lock (OwnersGate) PermanentRoots.Add(this); // Preserve actual handles even if later identity verification refuses adoption.
            if (_held.Length is <= 0 or > 268435456) throw new UnsupportedSignatureException("Native image file length is out of bounds.");
            Data = new byte[checked((int)_held.Length)]; var used = 0;
            while (used < Data.Length) { var count = _held.Read(Data, used, Data.Length - used); if (count == 0) throw new EndOfStreamException(); used += count; }
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(Data)), hash, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("An already-loaded native module differs from its independent exact build hash.");
            if (!GetFileInformationByHandle(_held.SafeFileHandle.DangerousGetHandle(), out var id))
                throw new UnsupportedSignatureException("Cannot identify the independently held native file.");
            VolumeSerial = id.Volume; FileIndex = ((ulong)id.IndexHigh << 32) | id.IndexLow;
            if (U16(0) != 0x5a4d) throw new UnsupportedSignatureException("Native image DOS header is invalid.");
            var nt = checked((int)U32(0x3c)); var optional = checked(nt + 24);
            if (U32(nt) != 0x4550 || U16(nt + 4) != 0x8664 || U16(optional) != 0x20b || U32(optional + 108) < 4)
                throw new UnsupportedSignatureException("Native image is not a complete AMD64 PE32+ image.");
            Machine = U16(nt + 4); Timestamp = U32(nt + 8); EntryRva = U32(optional + 16);
            ImageSize = U32(optional + 56); _headerSize = U32(optional + 60); ExceptionRva = U32(optional + 112 + 3 * 8);
            _exportRva = U32(optional + 112); _exportBytes = U32(optional + 116);
            _importRva = U32(optional + 120); _importBytes = U32(optional + 124);
            var exceptionBytes = U32(optional + 116 + 3 * 8); var sectionAt = checked(optional + U16(nt + 20));
            for (var index = 0; index < U16(nt + 6); ++index)
            {
                var at = checked(sectionAt + index * 40); var rva = U32(at + 12); var raw = U32(at + 20); var bytes = U32(at + 16);
                if ((ulong)raw + bytes > (ulong)Data.Length) throw new UnsupportedSignatureException("Native image section is truncated.");
                _sections.Add((rva, raw, bytes));
            }
            if (exceptionBytes == 0 || exceptionBytes % 12 != 0 || exceptionBytes > 16 * 1024 * 1024)
                throw new UnsupportedSignatureException("Native image lacks a bounded complete static unwind table.");
            var pdata = Disk(ExceptionRva, checked((int)exceptionBytes));
            for (var at = 0; at < pdata.Length; at += 12)
            {
                var function = new Function(BinaryPrimitives.ReadUInt32LittleEndian(pdata.AsSpan(at)),
                    BinaryPrimitives.ReadUInt32LittleEndian(pdata.AsSpan(at + 4)), BinaryPrimitives.ReadUInt32LittleEndian(pdata.AsSpan(at + 8)));
                if (function.Begin >= function.End || function.End > ImageSize || function.Unwind >= ImageSize)
                    throw new UnsupportedSignatureException("Native runtime function range is invalid.");
                _functions.Add(function);
            }
            VerifyCurrent();
        }
        private ushort U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(at, 2));
        private uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(at, 4));
        internal byte[] Disk(uint rva, int bytes)
        {
            if (bytes <= 0) throw new UnsupportedSignatureException("Invalid bounded disk RVA request.");
            if ((ulong)rva + (ulong)bytes <= _headerSize) return Data.AsSpan(checked((int)rva), bytes).ToArray();
            foreach (var section in _sections)
                if (rva >= section.Rva && (ulong)rva - section.Rva + (ulong)bytes <= section.Bytes)
                    return Data.AsSpan(checked((int)(section.Raw + rva - section.Rva)), bytes).ToArray();
            throw new UnsupportedSignatureException("Native RVA does not have a complete held file-backed range.");
        }
        internal void VerifyCurrent()
        {
            if ((_executable ? GetModuleHandle(null) : GetModuleHandle(Path)) != Handle
                || !string.Equals(ModulePath(Handle), Path, StringComparison.OrdinalIgnoreCase)
                || !GetFileInformationByHandle(_held.SafeFileHandle.DangerousGetHandle(), out var identity)
                || identity.Volume != VolumeSerial || (((ulong)identity.IndexHigh << 32) | identity.IndexLow) != FileIndex
                || (((ulong)identity.SizeHigh << 32) | identity.SizeLow) != (ulong)Data.Length)
                throw new UnsupportedSignatureException("An adopted loaded native dependency or held file changed.");
            var diskHeader = Data.AsSpan(0, checked((int)Math.Min(_headerSize, 4096)));
            var mappedHeader = ReadMemory(Handle, diskHeader.Length, Handle, 0x02);
            var imageBaseOffset = checked((int)U32(0x3c) + 48); // NT signature + file header + PE32+ ImageBase offset.
            if (imageBaseOffset < 0 || imageBaseOffset > diskHeader.Length - sizeof(ulong))
                throw new UnsupportedSignatureException("The complete PE32+ ImageBase field is outside the held header range.");
            var preferredBase = BinaryPrimitives.ReadUInt64LittleEndian(diskHeader.Slice(imageBaseOffset, sizeof(ulong)));
            var mappedBase = BinaryPrimitives.ReadUInt64LittleEndian(mappedHeader.AsSpan(imageBaseOffset, sizeof(ulong)));
            if (mappedBase != preferredBase && mappedBase != Address(Handle))
                throw new UnsupportedSignatureException("Mapped PE ImageBase is neither its held preferred base nor its actual loaded module base.");
            // The observed loader may update exactly this validated eight-byte
            // field for ASLR. Normalize only that known value, then retain the
            // full byte comparison of every other mapped DOS/NT/header byte.
            BinaryPrimitives.WriteUInt64LittleEndian(mappedHeader.AsSpan(imageBaseOffset, sizeof(ulong)), preferredBase);
            if (!mappedHeader.AsSpan().SequenceEqual(diskHeader))
                throw new UnsupportedSignatureException("Mapped PE headers differ from the independently held native image.");
        }
        private static uint Word(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
        private static ulong Quad(byte[] data, int at) => BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(at, 8));
        private string DiskName(uint rva)
        {
            var name = new StringBuilder();
            for (uint index = 0; index < 512; ++index)
            {
                var value = Disk(checked(rva + index), 1)[0];
                if (value == 0) return name.ToString();
                if (value < 0x20 || value > 0x7e) throw new UnsupportedSignatureException("Native import/export name is not bounded ASCII.");
                name.Append((char)value);
            }
            throw new UnsupportedSignatureException("Native import/export name exceeds its fixed bound.");
        }
        internal uint ExportRva(string name)
        {
            if (_exportRva == 0 || _exportBytes < 40 || (ulong)_exportRva + _exportBytes > ImageSize)
                throw new UnsupportedSignatureException("The fixed graph destination has no complete export table.");
            var header = Disk(_exportRva, 40); var functionCount = Word(header, 20); var nameCount = Word(header, 24);
            if (functionCount is 0 or > 65536 || nameCount is 0 or > 65536)
                throw new UnsupportedSignatureException("Native export table counts exceed the bounded fixed graph.");
            var functions = Word(header, 28); var names = Word(header, 32); var ordinals = Word(header, 36);
            if (!ReadMemory(NativeEnginePublication.Pointer(Address(Handle) + _exportRva), 40, Handle, 0x02).AsSpan().SequenceEqual(header)
                || !ReadMemory(NativeEnginePublication.Pointer(Address(Handle) + names), checked((int)nameCount * 4), Handle, 0x02).AsSpan().SequenceEqual(Disk(names, checked((int)nameCount * 4)))
                || !ReadMemory(NativeEnginePublication.Pointer(Address(Handle) + ordinals), checked((int)nameCount * 2), Handle, 0x02).AsSpan().SequenceEqual(Disk(ordinals, checked((int)nameCount * 2))))
                throw new UnsupportedSignatureException("The actual fixed export directory/name/ordinal table changed.");
            uint found = 0; var matches = 0;
            for (uint index = 0; index < nameCount; ++index)
            {
                var namePointer = Word(Disk(checked(names + index * 4), 4), 0);
                var heldName = DiskName(namePointer);
                var heldNameBytes = Encoding.ASCII.GetBytes(heldName + '\0');
                if (!ReadMemory(NativeEnginePublication.Pointer(Address(Handle) + namePointer), heldNameBytes.Length, Handle, 0x02).AsSpan().SequenceEqual(heldNameBytes))
                    throw new UnsupportedSignatureException("An actual fixed export name changed.");
                if (!string.Equals(heldName, name, StringComparison.Ordinal)) continue;
                ++matches;
                var ordinal = BinaryPrimitives.ReadUInt16LittleEndian(Disk(checked(ordinals + index * 2), 2));
                if (ordinal >= functionCount) throw new UnsupportedSignatureException("Native export ordinal exceeds its held table.");
                var functionSlot = checked(functions + ordinal * 4u); var held = Disk(functionSlot, 4);
                found = Word(held, 0);
                if (found == 0 || found >= ImageSize || found >= _exportRva && (ulong)found < (ulong)_exportRva + _exportBytes
                    || !ReadMemory(NativeEnginePublication.Pointer(Address(Handle) + functionSlot), 4, Handle, 0x02).AsSpan().SequenceEqual(held))
                    throw new UnsupportedSignatureException("The actual fixed export is forwarded, altered or outside its destination image.");
                Memory(NativeEnginePublication.Pointer(Address(Handle) + found), 1, Handle, 0x20);
            }
            if (matches != 1) throw new UnsupportedSignatureException("The fixed graph export is missing or duplicated: " + name + ".");
            return found;
        }
        internal void VerifyImportsTo(ModuleFacts destination, IReadOnlyList<string> expectedNames)
        {
            if (_importRva == 0 || _importBytes < 20 || _importBytes > 1024 * 1024 || (ulong)_importRva + _importBytes > ImageSize)
                throw new UnsupportedSignatureException("The fixed graph source has no bounded ordinary import table.");
            var remaining = new HashSet<string>(expectedNames, StringComparer.Ordinal);
            if (remaining.Count != expectedNames.Count || remaining.Count == 0) throw new UnsupportedSignatureException("Invalid fixed ordinary import names.");
            var destinationName = System.IO.Path.GetFileName(destination.Path); var descriptors = 0; var ended = false;
            for (uint offset = 0; offset + 20 <= _importBytes; offset += 20)
            {
                var descriptorRva = checked(_importRva + offset); var descriptor = Disk(descriptorRva, 20);
                if (NativeEnginePublication.IsZero(descriptor)) { ended = true; break; }
                if (!string.Equals(DiskName(Word(descriptor, 12)), destinationName, StringComparison.OrdinalIgnoreCase)) continue;
                ++descriptors;
                if (!ReadMemory(NativeEnginePublication.Pointer(Address(Handle) + descriptorRva), 20, Handle, 0x02).AsSpan().SequenceEqual(descriptor))
                    throw new UnsupportedSignatureException("An actual fixed ordinary import descriptor changed.");
                var dllName = Word(descriptor, 12); var dllNameBytes = Encoding.ASCII.GetBytes(DiskName(dllName) + '\0');
                if (!ReadMemory(NativeEnginePublication.Pointer(Address(Handle) + dllName), dllNameBytes.Length, Handle, 0x02).AsSpan().SequenceEqual(dllNameBytes))
                    throw new UnsupportedSignatureException("An actual fixed ordinary import DLL name changed.");
                var original = Word(descriptor, 0); var iat = Word(descriptor, 16);
                if (original == 0 || iat == 0) throw new UnsupportedSignatureException("The fixed ordinary import has no independent ILT/IAT.");
                var thunkEnded = false;
                for (uint index = 0; index <= 65536; ++index)
                {
                    var iltRva = checked(original + index * 8); var iatRva = checked(iat + index * 8);
                    var held = Disk(iltRva, 8); var nameRva = Quad(held, 0);
                    if (!ReadMemory(NativeEnginePublication.Pointer(Address(Handle) + iltRva), 8, Handle, 0x02).AsSpan().SequenceEqual(held))
                        throw new UnsupportedSignatureException("An actual fixed ordinary import name thunk changed.");
                    var slot = ReadMemory(NativeEnginePublication.Pointer(Address(Handle) + iatRva), 8, Handle, 0x02);
                    if (nameRva == 0)
                    {
                        if (Quad(slot, 0) != 0) throw new UnsupportedSignatureException("The actual fixed IAT terminator changed.");
                        thunkEnded = true; break;
                    }
                    if (nameRva > uint.MaxValue || (nameRva & 0x8000000000000000) != 0)
                        throw new UnsupportedSignatureException("An ordinal or unbounded import cannot prove the fixed named graph.");
                    var name = DiskName(checked((uint)nameRva + 2));
                    var importNameBytes = Disk((uint)nameRva, checked(name.Length + 3));
                    if (!ReadMemory(NativeEnginePublication.Pointer(Address(Handle) + nameRva), importNameBytes.Length, Handle, 0x02).AsSpan().SequenceEqual(importNameBytes))
                        throw new UnsupportedSignatureException("An actual fixed ordinary import name/hint changed.");
                    if (!remaining.Remove(name)) throw new UnsupportedSignatureException("The fixed graph import is unexpected or repeated: " + name + ".");
                    var expected = checked(Address(destination.Handle) + destination.ExportRva(name));
                    if (Quad(slot, 0) != expected) throw new UnsupportedSignatureException("An actual fixed IAT slot resolves to a different module/export: " + name + ".");
                }
                if (!thunkEnded) throw new UnsupportedSignatureException("The fixed ordinary import thunks are unterminated.");
            }
            if (!ended || descriptors != 1 || remaining.Count != 0)
                throw new UnsupportedSignatureException("The complete fixed ordinary import edge set is missing, duplicated or unterminated.");
        }
        private byte[] Unwind(Function function)
        {
            var header = Disk(function.Unwind, 4); var flags = header[0] >> 3;
            if ((header[0] & 7) != 1 || (flags & ~7) != 0 || ((flags & 4) != 0 && (flags & 3) != 0))
                throw new UnsupportedSignatureException("Unsupported or contradictory native unwind metadata.");
            var bytes = (4 + header[2] * 2 + 3) & ~3;
            return Disk(function.Unwind, checked(bytes + ((flags & 4) != 0 ? 12 : (flags & 3) != 0 ? 4 : 0)));
        }
        private Function Root(Function function)
        {
            var seen = new HashSet<Function>();
            for (var depth = 0; depth < 32; ++depth)
            {
                if (!seen.Add(function)) throw new UnsupportedSignatureException("Cyclic native unwind chain.");
                var bytes = Unwind(function); if ((bytes[0] >> 3 & 4) == 0) return function;
                var at = bytes.Length - 12;
                function = new(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4)),
                    BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 8)));
                if (!_functions.Contains(function)) throw new UnsupportedSignatureException("Native unwind chain leaves its held static function table.");
            }
            throw new UnsupportedSignatureException("Native unwind chain exceeds the fixed bound.");
        }
        internal List<Function> Family(uint begin)
        {
            var first = _functions.Where(x => x.Begin == begin).ToArray();
            if (first.Length != 1) throw new UnsupportedSignatureException("Fixed source has no unique actual static runtime function entry.");
            var root = Root(first[0]);
            var family = _functions.Where(x => Root(x) == root).OrderBy(x => x.Begin).ToList();
            if (family.Count is 0 or > 64 || family[0].Begin != begin)
                throw new UnsupportedSignatureException("Fixed source has an unexpected complete unwind family.");
            for (var index = 1; index < family.Count; ++index)
                if (family[index - 1].End != family[index].Begin) throw new UnsupportedSignatureException("Fixed source body contains a gap or overlapping unwind ranges.");
            return family;
        }
        internal void VerifyUnwind(Function function)
        {
            var address = NativeEnginePublication.Pointer(Address(Handle) + function.Begin);
            var entry = RtlLookupFunctionEntry(Address(address), out var imageBase, IntPtr.Zero);
            if (entry == IntPtr.Zero || imageBase != Address(Handle))
                throw new UnsupportedSignatureException("Actual source/original has no matching OS static unwind registration.");
            var bytes = ReadMemory(entry, 12, Handle, 0x02);
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != function.Begin
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != function.End
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) != function.Unwind)
                throw new UnsupportedSignatureException("Actual OS unwind tuple differs from independently held static metadata.");
            var unwind = Unwind(function);
            if (!ReadMemory(NativeEnginePublication.Pointer(Address(Handle) + function.Unwind), unwind.Length, Handle, 0x02).AsSpan().SequenceEqual(unwind))
                throw new UnsupportedSignatureException("Actual source/original unwind metadata differs from its full held chain bytes.");
        }
    }
    private readonly record struct Function(uint Begin, uint End, uint Unwind);
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInformation
    { internal IntPtr BaseAddress, AllocationBase; internal uint AllocationProtect; internal ushort PartitionId; internal nuint RegionSize; internal uint State, Protect, Type; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    { internal uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleFileNameW", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module, StringBuilder output, int capacity);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern nuint VirtualQuery(IntPtr address, out MemoryInformation output, nuint size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] output, nuint size, out nuint read);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(IntPtr file, out FileInformation info);
    [DllImport("ntdll.dll")] private static extern IntPtr RtlLookupFunctionEntry(ulong controlPc, out ulong imageBase, IntPtr history);
}
