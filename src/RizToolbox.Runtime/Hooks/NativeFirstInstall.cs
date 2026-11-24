using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RizToolbox.Runtime.Hooks;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeFirstInstallInformation
{
    internal uint Version, Size, Phase, Unknown, VoidSlot, Reserved0;
    internal ulong ModuleBase, ImageSize;
    internal IntPtr Init, RuntimeTarget, RuntimeOriginal, RuntimeEntry;
    internal ulong RuntimeEpoch;
    internal IntPtr UnityTarget, UnityOriginal, UnityEntry;
    internal ulong UnityEpoch;
    internal IntPtr MidReturnPc, Relay;
    internal ulong WritesStarted, Reserved1;
}

/// <summary>Read-only adoption of the real pre-il2cpp_init native producer.
/// There is no caller-created ready receipt, patch operation or fallback to a
/// late first installation. The loaded helper and exact GameAssembly image are
/// independently matched before this private process responsibility is issued.</summary>
internal sealed partial class NativeFirstInstall
{
    internal const uint ReservedVoidSlot = 31;
    private const string GameFingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private static readonly object Gate = new();
    private static NativeFirstInstall? _owner;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(ref NativeFirstInstallInformationV7 info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadListenerNative(uint slot, ref NativeListenerInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadRuntimeNative(ref NativeRuntimeInvokeInformation info);
    private readonly ReadNative _read;
    private readonly ReadListenerNative _readListener;
    private readonly ReadOperationNative _readOperation;
    private readonly ReadRuntimeNative _readRuntime;
    private readonly NativeFirstInstallInformation _identity;
    private readonly NativeFirstInstallInformationV2 _publication;
    private readonly NativeFirstInstallInformationV7 _operationPublication;
    private string? _firstFailure;
    internal string HelperPath { get; }
    internal IntPtr Module { get; }
    internal IntPtr HelperModule { get; }
    internal IntPtr RuntimeTarget => _identity.RuntimeTarget;
    internal IntPtr RuntimeOriginal => _identity.RuntimeOriginal;
    internal IntPtr RuntimeEntry => _identity.RuntimeEntry;
    internal ulong RuntimeEpoch => _identity.RuntimeEpoch;
    internal uint VoidSlot => _identity.VoidSlot;
    internal IntPtr UnityTarget => _identity.UnityTarget;
    internal IntPtr UnityOriginal => _identity.UnityOriginal;
    internal IntPtr UnityEntry => _identity.UnityEntry;
    internal ulong UnityEpoch => _identity.UnityEpoch;
    internal IntPtr MidReturnPc => _identity.MidReturnPc;
    // A value-only identity copy. It cannot authorize a native installation or
    // create another Ready owner. Listener callbacks/ledger are separate work;
    // a TargetReadFaulted Abnormal After(token=0) can occur without Before.
    internal NativeFirstInstallInformationV2 ListenerPublication => _publication;
    internal NativeFirstInstallInformationV7 OperationPublication => _operationPublication;
    internal NativeTweenSingleEventPublication SingleEventPublication => _operationPublication.SingleEvent;

    internal static NativeFirstInstall RequireReady()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new PlatformNotSupportedException("Native first installation requires Windows x64.");
        lock (Gate)
        {
            _owner ??= ReadLoadedProducer();
            _owner.VerifyReady(); return _owner;
        }
    }
    private NativeFirstInstall(string path, IntPtr helper, IntPtr module, ReadNative read,
        ReadListenerNative readListener, ReadOperationNative readOperation, ReadRuntimeNative readRuntime,
        NativeFirstInstallInformationV7 info)
    {
        HelperPath = path; HelperModule = helper; Module = module; _read = read; _readListener = readListener;
        _readOperation = readOperation; _readRuntime = readRuntime;
        _operationPublication = info; _publication = info.Listener; _identity = info.Listener.Common;
    }
    private static NativeFirstInstall ReadLoadedProducer()
    {
        if (Marshal.SizeOf<NativeFirstInstallInformation>() != 144 || Marshal.SizeOf<NativeFirstInstallInformationV2>() != 416
            || Marshal.SizeOf<NativeFirstInstallInformationV7>() != 2528 || Marshal.SizeOf<NativeOperationPublicationRow>() != 64
            || Marshal.SizeOf<NativeTweenSingleEventPublication>() != 416
            || Marshal.SizeOf<NativeListenerInformation>() != 176 || Marshal.SizeOf<NativeOperationBootstrapInformation>() != 96
            || Marshal.SizeOf<NativeRuntimeInvokeInformation>() != 80)
            throw new UnsupportedSignatureException("Native first installation information layout changed.");
        var helperPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(NativeFirstInstall).Assembly.Location)!, "RizToolbox.JudgeContext.dll"));
        var helper = GetModuleHandle(helperPath);
        var module = GetModuleHandle("GameAssembly.dll");
        if (helper == IntPtr.Zero || module == IntPtr.Zero || !string.Equals(ModulePath(helper), helperPath, StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("The native pre-init producer is not already loaded; no late installation is permitted.");
        if (!NativeLibrary.TryGetExport(helper, "RizFirstInstall_GetInfoV7", out var readAddress)
            || !NativeLibrary.TryGetExport(helper, "RizListenerContext_GetInfo", out var listenerAddress)
            || !NativeLibrary.TryGetExport(helper, "RizOperationBootstrap_GetInfo", out var operationAddress)
            || !NativeLibrary.TryGetExport(helper, "RizRuntimeInvokeContext_GetInfo", out var runtimeAddress))
            throw new UnsupportedSignatureException("The loaded producer has no complete version 7, 26-operation, 64-write receipt.");
        var read = Marshal.GetDelegateForFunctionPointer<ReadNative>(readAddress);
        var readListener = Marshal.GetDelegateForFunctionPointer<ReadListenerNative>(listenerAddress);
        var readOperation = Marshal.GetDelegateForFunctionPointer<ReadOperationNative>(operationAddress);
        var readRuntime = Marshal.GetDelegateForFunctionPointer<ReadRuntimeNative>(runtimeAddress);
        var operations = NewInformation();
        var readStatus = read(ref operations);
        if (readStatus != 0 || !NativeOperationPublication.HasShape(operations))
            throw new UnsupportedSignatureException("The native pre-init producer has no known complete version 7 Ready receipt. "
                + DescribePublicationFailure(readStatus, operations.Listener.Common));
        var publication = operations.Listener;
        var info = publication.Common;
        if (info.ModuleBase != unchecked((ulong)module.ToInt64()) || info.ImageSize != ImageSize(module))
            throw new UnsupportedSignatureException("The native producer receipt identifies a different loaded game image.");
        using (var stream = File.OpenRead(ModulePath(module)))
        using (var sha = SHA256.Create())
            if (!string.Equals(Convert.ToHexString(sha.ComputeHash(stream)), GameFingerprint, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The loaded game file differs from the native producer's pinned build.");
        if (info.ImageSize != 0x243c000 || info.Init != IntPtr.Add(module, 0x2841C0)
            || info.RuntimeTarget != IntPtr.Add(module, 0x2AF0E0)
            || info.UnityTarget != IntPtr.Add(module, 0x15642E0) || info.MidReturnPc != IntPtr.Add(module, 0x1564420))
            throw new UnsupportedSignatureException("The native producer's addresses differ from the pinned image.");
        HookTargetValidation.VerifyExecutable(info.Init, module);
        HookTargetValidation.VerifyExecutable(info.RuntimeTarget, module);
        HookTargetValidation.VerifyExecutable(info.UnityTarget, module);
        HookTargetValidation.VerifyExecutable(info.MidReturnPc, module);
        HookTargetValidation.VerifyExecutable(info.RuntimeEntry, helper);
        HookTargetValidation.VerifyExecutable(info.UnityEntry, helper);
        VerifyExecutable(info.RuntimeOriginal); VerifyExecutable(info.UnityOriginal); VerifyExecutable(info.Relay);
        var midSite = IntPtr.Subtract(info.MidReturnPc, 5);
        HookTargetValidation.VerifyReadable(midSite, 5);
        if (Marshal.ReadByte(midSite) != 0xe8 || IntPtr.Add(info.MidReturnPc, Marshal.ReadInt32(midSite, 1)) != info.Relay)
            throw new UnsupportedSignatureException("The producer's actual inline CALL does not lead to its retained relay.");
        var lane = NewListenerInformation();
        if (readListener(publication.ListenerSlot, ref lane) != 0 || !NativeListenerPublication.MatchesLane(lane, publication))
            throw new UnsupportedSignatureException("The native listener lane differs from the producer's actual publication identity.");
        VerifyListenerMemory(publication, helper, module);
        VerifyOperationMemory(operations, helper, module, readOperation);
        VerifySingleEventMemory(operations, helper, module);
        return new(helperPath, helper, module, read, readListener, readOperation, readRuntime, operations);
    }
    // A fresh, complete publication audit. Startup/adoption and explicit
    // Developer Tick audits retain this proof; Standard ticks do not borrow a
    // successful result from it as if arbitrary memory were still audited.
    internal void VerifyReady()
    {
        RequireKnown();
        try
        {
            var info = NewInformation();
            var lane = NewListenerInformation();
            if (GetModuleHandle(HelperPath) != HelperModule || GetModuleHandle("GameAssembly.dll") != Module
                || !string.Equals(ModulePath(HelperModule), HelperPath, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The published helper or game module changed.");
            var status = _read(ref info);
            if (status != 0 || !NativeOperationPublication.HasShape(info) || !info.Equals(_operationPublication))
                throw new UnsupportedSignatureException(DescribePublicationFailure(status, info.Listener.Common));
            var listenerStatus = _readListener(info.Listener.ListenerSlot, ref lane);
            if (listenerStatus != 0 || !NativeListenerPublication.MatchesLane(lane, info.Listener))
                throw new UnsupportedSignatureException($"The published listener lane differs (slot={info.Listener.ListenerSlot}, status={listenerStatus}).");
            VerifyListenerMemory(info.Listener, HelperModule, Module);
            VerifyOperationMemory(info, HelperModule, Module, _readOperation);
            VerifySingleEventMemory(info, HelperModule, Module);
            VerifyTweenSourceMemory();
        }
        catch (Exception error) { RememberFailure(nameof(VerifyReady), error.Message); throw; }
    }
    // An Unknown V7 snapshot may carry the first failure in its trailing word.
    // Read it only for diagnostics, after the caller has rejected the publication.
    internal static string DescribePublicationFailure(uint status, NativeFirstInstallInformation info)
    {
        var state = $"The native V7 publication differs (status={status}, phase={info.Phase}, unknown={info.Unknown}).";
        if (status != 0 || info.Phase != 4 || info.Unknown != 1 || info.Reserved1 == 0)
            return state;
        var reason = (uint)(info.Reserved1 & uint.MaxValue);
        var detail = (uint)(info.Reserved1 >> 32);
        var family = reason switch
        {
            1 => "PublishedCore", 2 => "PublishedOperation", 3 => "PublishedSingleEvent",
            4 => "PublishedTweenSource", 5 => "PublishedVerificationException", 6 => "PlaybackAttestation",
            7 => "PlaybackVerificationException", 8 => "PreinitReentry", 9 => "UnexpectedPrepareWrite",
            10 => "InstallCommit", 11 => "InstallPublication", 12 => "InstallException",
            13 => "InstallAbandoned", _ => "Unrecognized"
        };
        var message = $"{state} First native failure: {family} (reason={reason}, detail=0x{detail:x8}).";
        return reason == 4 ? message + DescribeTweenSourceFailure(detail) : message;
    }

    private static string DescribeTweenSourceFailure(uint detail)
    {
        // SourceFailureDetail in TweenSourcePublication.cpp. Physical site codes
        // use zero-based offsets from 8, unlike encoded callback faults below.
        if (detail < 8) return detail switch
        {
            1 => " Tween source preparation or publication is incomplete.",
            2 => " The Tween source publication is already unknown.",
            3 => " The immutable Tween source publication failed verification.",
            4 => " The Tween source context identity failed verification.",
            5 => " The Tween source context became unknown.",
            6 => " The Tween source publication packet failed verification.",
            7 => " The published Tween source site mask is inconsistent.",
            _ => string.Empty
        };
        if (detail - 8 <= (uint)NativeTweenSourceSite.ExecuteOnComplete)
            return $" The physical Tween source site {(NativeTweenSourceSite)(detail - 8)} failed identity verification.";
        if (detail >> 24 != 1) return string.Empty;
        // See RizTweenSourceFault. The stage and site bytes are one-based.
        var cause = ((detail >> 16) & 0xff) switch
        {
            1 => "an event or transaction sequence was exhausted",
            2 => "an unfinished source record would be overwritten",
            3 => "the journal record identity differs",
            4 => "the source transaction lost its active parent match during a reference callback",
            5 => "the reference scope reservation failed",
            6 => "the borrowed reference delivery failed",
            7 => "the committed source notification failed",
            8 => "the reference scope closure failed",
            9 => "the operation nesting limit was reached",
            10 => "the operation receiver failed its alignment, readability, or exact class check",
            11 => "the operation manager failed its identity check",
            12 => "an active operation sequence was reused",
            13 => "the operation capacity was exhausted",
            14 => "the exit ticket has no matching active operation",
            15 => "the operation exited without a confirmed normal return",
            16 => "the operation exited with an unfinished source transaction",
            17 => "the operation exited with an unfinished move",
            18 => "the operation exited with an unfinished pop",
            19 => "the active pending source transaction differs",
            20 => "the source entry frame is invalid",
            21 => "the return address has no registered source",
            22 => "the source has no active operation parent",
            23 => "the source operation parent has an unexpected kind",
            24 => "the source nesting limit was reached",
            25 => "the capacity for pending source transactions was exhausted",
            26 => "the source Before caller or data differs",
            27 => "source data failed verification after the Before callback",
            28 => "the source After has no pending Before",
            29 => "the source After differs from its pending Before",
            30 => "the source stack pointer changed between Before and After",
            31 => "the source After caller or data differs",
            32 => "source data failed verification after the After callback",
            _ => null
        };
        if (cause is null) return string.Empty;
        var site = detail & 0xff;
        var stage = (detail >> 8) & 0xff;
        return site is >= 1 and <= 22 && stage is >= 1 and <= 3
            ? $" Tween source {(NativeTweenSourceSite)(site - 1)} ({(NativeTweenSourceStage)(stage - 1)}) failed because {cause}."
            : $" Tween source verification failed because {cause}.";
    }
    private void RememberFailure(string operation, string message)
        => Interlocked.CompareExchange(ref _firstFailure, operation + ": " + (message.Length <= 512 ? message : message[..512]), null);
    private void RequireKnown()
    {
        if (Volatile.Read(ref _firstFailure) is { } cause)
            throw new UnsupportedSignatureException("Native first installation identity is permanently unknown. First failure: " + cause);
    }
    // The narrower Standard contract retains the real process owner/module
    // roots and their sticky managed Unknown. Actual runtime channel identity,
    // epoch, known/in-flight and faults are read separately for this callback.
    // In particular _read/GetInfoV7 is NOT a cheap receipt read: native code
    // audits the complete publication graph there. No success cache replaces it.
    internal void VerifyRuntimeChannelOwnership()
    {
        RequireKnown();
        if (GetModuleHandle(HelperPath) != HelperModule || GetModuleHandle("GameAssembly.dll") != Module)
        {
            const string reason = "The retained runtime channel modules are no longer current.";
            RememberFailure(nameof(VerifyRuntimeChannelOwnership), reason);
            throw new UnsupportedSignatureException(reason);
        }
    }
    // Playback operations may run outside the Runtime-invoke observer. They
    // therefore read its real channel here, rather than borrowing the observer's
    // earlier admission. The permanent channel can be unattached in an isolated
    // installation and later acquire its one observer without changing epoch.
    internal void VerifyPlaybackRuntimeChannelOwnership()
    {
        VerifyRuntimeChannelOwnership();
        try
        {
            var info = new NativeRuntimeInvokeInformation { Version = 1, Size = 80 };
            if (_readRuntime(ref info) != 0 || info.Attached > 1
                || !NativeRuntimeInvokeBridge.MatchesInformation(info, RuntimeTarget, RuntimeOriginal,
                    RuntimeEntry, RuntimeEpoch, info.Attached == 1))
                throw new UnsupportedSignatureException("The foundation playback operation lost its known native runtime channel or exact epoch.");
            VerifyRuntimeChannelOwnership();
        }
        catch (Exception error) { RememberFailure(nameof(VerifyPlaybackRuntimeChannelOwnership), error.Message); throw; }
    }
    private static NativeFirstInstallInformationV7 NewInformation() => new() { Listener = new() { Common = new() { Version = 7, Size = 2528 } } };
    private static NativeListenerInformation NewListenerInformation() => new() { Version = 1, Size = 176 };
    internal static bool ReadyCommonShape(NativeFirstInstallInformation info, uint version, uint size, ulong writes)
        => info.Version == version && info.Size == size && info.Phase == 3 && info.Unknown == 0
        && info.VoidSlot == ReservedVoidSlot && info.Reserved0 == 0 && info.Reserved1 == 0
        && info.ModuleBase != 0 && info.ImageSize > 0x1564420 && info.Init != IntPtr.Zero
        && info.RuntimeTarget != IntPtr.Zero && info.RuntimeOriginal != IntPtr.Zero && info.RuntimeEntry != IntPtr.Zero && info.RuntimeEpoch != 0
        && info.UnityTarget != IntPtr.Zero && info.UnityOriginal != IntPtr.Zero && info.UnityEntry != IntPtr.Zero && info.UnityEpoch != 0
        && info.MidReturnPc != IntPtr.Zero && info.Relay != IntPtr.Zero && info.WritesStarted == writes;

    private static void VerifyListenerMemory(NativeFirstInstallInformationV2 publication, IntPtr helper, IntPtr module)
    {
        HookTargetValidation.VerifyExecutable(publication.PreEntry, helper);
        HookTargetValidation.VerifyExecutable(publication.PostEntry, helper);
        HookTargetValidation.VerifyExecutable(publication.DirectTarget0, module);
        HookTargetValidation.VerifyExecutable(publication.DirectTarget1, module);
        VerifyRelay(publication.PreRelay, publication.PreEntry);
        VerifyRelay(publication.PostRelay, publication.PostEntry);
        HookTargetValidation.VerifyReadable(publication.FunctionTable, 36);
        for (uint index = 0; index < 3; ++index)
        {
            var site = NativeListenerPublication.Site(publication, index);
            HookTargetValidation.VerifyExecutable(site.SourcePc, module);
            HookTargetValidation.VerifyExecutable(site.SourceReturnPc, module);
            HookTargetValidation.VerifyReadable(site.SourcePc, site.SourceWidth);
            HookTargetValidation.VerifyReadable(site.SourceReturnPc, site.PostWidth);
            VerifyProtection(site.SourcePc, site.SourceProtection); VerifyProtection(site.SourceReturnPc, site.PostProtection);
            var call = index == 2 ? IntPtr.Add(site.SourcePc, 1) : site.SourcePc;
            if ((index == 2 && Marshal.ReadByte(site.SourcePc) != 0x90) || !RelativeBranch(call, 0xe8, publication.PreRelay)
                || !RelativeBranch(site.SourceReturnPc, 0xe9, site.PostStub))
                throw new UnsupportedSignatureException("The actual listener source CALL or post branch changed.");
            for (var offset = 5; offset < site.PostWidth; ++offset)
                if (Marshal.ReadByte(site.SourceReturnPc, offset) != 0x90)
                    throw new UnsupportedSignatureException("The actual listener post region lost its complete padded publication.");
            HookTargetValidation.VerifyReadable(site.PostStub, 14); VerifyProtection(site.PostStub, 0x20);
            if (site.PostTransportReturnPc != IntPtr.Add(site.PostStub, 5) || !RelativeBranch(site.PostStub, 0xe8, publication.PostRelay)
                || Marshal.ReadByte(site.PostStub, 5) != 0xff || Marshal.ReadByte(site.PostStub, 6) != 0xc3
                || Marshal.ReadByte(site.PostStub, 7) != 0x8b || Marshal.ReadByte(site.PostStub, 8) != 0xc3
                || !RelativeBranch(IntPtr.Add(site.PostStub, 9), 0xe9, publication.Common.MidReturnPc))
                throw new UnsupportedSignatureException("The actual listener post CALL/replay/loop continuation changed.");
            HookTargetValidation.VerifyReadable(site.UnwindInfo, 16);
            if (Marshal.ReadByte(site.UnwindInfo) != 0x21 || Marshal.ReadByte(site.UnwindInfo, 1) != 0
                || Marshal.ReadByte(site.UnwindInfo, 2) != 0 || Marshal.ReadByte(site.UnwindInfo, 3) != 0
                || unchecked((uint)Marshal.ReadInt32(site.UnwindInfo, 4)) != 0x15642e0
                || unchecked((uint)Marshal.ReadInt32(site.UnwindInfo, 8)) != 0x156454e
                || unchecked((uint)Marshal.ReadInt32(site.UnwindInfo, 12)) != 0x1db3fb4)
                throw new UnsupportedSignatureException("The listener post unwind chain differs from the fixed parent.");
            var row = IntPtr.Add(publication.FunctionTable, checked((int)index * 12));
            var begin = unchecked((uint)Marshal.ReadInt32(row)); var end = unchecked((uint)Marshal.ReadInt32(row, 4));
            var unwind = unchecked((uint)Marshal.ReadInt32(row, 8));
            if (publication.FunctionTableBase + begin != unchecked((ulong)site.PostStub.ToInt64()) || end <= begin || end - begin != 14
                || publication.FunctionTableBase + unwind != unchecked((ulong)site.UnwindInfo.ToInt64()))
                throw new UnsupportedSignatureException("The registered listener function table differs from its retained stub identity.");
            // Read the OS registration only after validating the bounded table
            // and chain bytes. Never ask the OS to unwind a damaged chain here.
            var actual = RtlLookupFunctionEntry(unchecked((ulong)site.PostStub.ToInt64()), out var actualBase, IntPtr.Zero);
            if (actual == IntPtr.Zero || actualBase != publication.FunctionTableBase)
                throw new UnsupportedSignatureException("The listener post stub has no matching registered native function.");
            HookTargetValidation.VerifyReadable(actual, 12);
            if (Marshal.ReadInt32(actual) != unchecked((int)begin) || Marshal.ReadInt32(actual, 4) != unchecked((int)end)
                || Marshal.ReadInt32(actual, 8) != unchecked((int)unwind))
                throw new UnsupportedSignatureException("The OS listener registration differs from the producer receipt.");
        }
    }
    private static bool RelativeBranch(IntPtr instruction, byte opcode, IntPtr target)
        => Marshal.ReadByte(instruction) == opcode && IntPtr.Add(instruction, 5 + Marshal.ReadInt32(instruction, 1)) == target;
    private static void VerifyRelay(IntPtr address, IntPtr target)
    {
        HookTargetValidation.VerifyReadable(address, 14); VerifyProtection(address, 0x20);
        if (Marshal.ReadByte(address) != 0xff || Marshal.ReadByte(address, 1) != 0x25 || Marshal.ReadInt32(address, 2) != 0
            || Marshal.ReadIntPtr(address, 6) != target)
            throw new UnsupportedSignatureException("A retained listener relay changed its exact tail target.");
    }
    private static void VerifyProtection(IntPtr address, uint expected)
    {
        if (VirtualQuery(address, out var info, (nuint)Marshal.SizeOf<MemoryInformation>()) == 0 || info.Protect != expected
            || !NativeListenerPublication.ExecutableProtection(info.Protect))
            throw new UnsupportedSignatureException("A listener publication's native page protection changed.");
    }
    private static uint ImageSize(IntPtr module)
    {
        HookTargetValidation.VerifyReadable(module, 64);
        var offset = Marshal.ReadInt32(module, 0x3c);
        if (Marshal.ReadInt16(module) != 0x5a4d || offset < 64 || offset > 0x100000)
            throw new UnsupportedSignatureException("The loaded game has no supported PE header.");
        var header = IntPtr.Add(module, offset); HookTargetValidation.VerifyReadable(header, 0x108);
        if (Marshal.ReadInt32(header) != 0x4550 || (ushort)Marshal.ReadInt16(header, 4) != 0x8664 || (ushort)Marshal.ReadInt16(header, 24) != 0x20b)
            throw new UnsupportedSignatureException("The loaded game is not a PE32+ AMD64 image.");
        return unchecked((uint)Marshal.ReadInt32(header, 0x50));
    }
    private static string ModulePath(IntPtr module)
    {
        var text = new StringBuilder(32768); var count = GetModuleFileName(module, text, text.Capacity);
        if (count == 0 || count >= text.Capacity) throw new UnsupportedSignatureException("A loaded native image path cannot be verified.");
        return Path.GetFullPath(text.ToString());
    }
    private static void VerifyExecutable(IntPtr address)
    {
        HookTargetValidation.VerifyReadable(address, 1);
        if (VirtualQuery(address, out var info, (nuint)Marshal.SizeOf<MemoryInformation>()) == 0 || (info.Protect & 0xf0) == 0)
            throw new UnsupportedSignatureException("The producer retained an original or relay outside executable memory.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInformation
    { internal IntPtr BaseAddress, AllocationBase; internal uint AllocationProtect; internal ushort PartitionId; internal nuint RegionSize; internal uint State, Protect, Type; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module, StringBuilder path, int capacity);
    [DllImport("kernel32.dll")] private static extern nuint VirtualQuery(IntPtr address, out MemoryInformation info, nuint length);
    [DllImport("kernel32.dll")] private static extern IntPtr RtlLookupFunctionEntry(ulong controlPc, out ulong imageBase, IntPtr historyTable);
}
