using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

// ABI-only DTOs for EngineTransportPublication.h V5. Numeric addresses/handles
// are ulong machine values. These mutable wire records are never a production
// owner, Ready credential, Configure request, or permission to load a module.
internal static class NativeEngineTransportPublicationLayout
{
    internal const uint Version = 5;
    internal const ulong Magic = 0x3556425550545a52UL;
    internal const int ByteSize = 91376, ModuleCount = 5, TypedCount = 13, SiteCount = 23;
    internal const int WindowCount = 36, FamilyCount = 12, SourceRuntimeFunctionCount = 27;
    internal const int RuntimeFunctionCapacity = 128, ResourceCapacity = 23, EventCapacity = 1024;
    internal const int ModuleOffset = 320, TypedOffset = 800, SiteOffset = 3296, FamilyOffset = 6240;
    internal const int WindowOffset = 8352, RuntimeFunctionOffset = 24480, ResourceOffset = 38816, EventOffset = 42128;
    internal const int AdmissionOffset = 91280, AdmissionByteSize = 96;
    internal const uint NoIndex = uint.MaxValue;
    internal const ulong AllTyped = (1UL << TypedCount) - 1, AllSites = (1UL << SiteCount) - 1;
    internal const ulong AllWindows = (1UL << WindowCount) - 1, AllFamilies = (1UL << FamilyCount) - 1;
    internal const ulong AllResources = (1UL << ResourceCapacity) - 1;

    // Structural rejection only: success says the DTO can represent ABI 5.
    // It does not validate Ready, image identities, actual bytes, main-thread
    // provenance, native Verify, RF registration or any production permission.
    internal static bool ValidateHeader(in NativeEngineTransportPublicationV5 value)
    {
        if (value.Version != Version || value.ByteSize != ByteSize || value.SchemaMagic != Magic
            || value.ModuleCount != ModuleCount || value.TypedCount != TypedCount || value.SiteCount != SiteCount
            || value.WindowCount != WindowCount || value.FamilyCount != FamilyCount
            || value.RuntimeFunctionCount > RuntimeFunctionCapacity || value.ResourceCount > ResourceCapacity
            || value.EventCapacity != EventCapacity || value.EventCount > EventCapacity || value.Reserved0 != 0
            || !HasLength(value.Modules, ModuleCount) || !HasLength(value.Typed, TypedCount)
            || !HasLength(value.Sites, SiteCount) || !HasLength(value.Families, FamilyCount)
            || !HasLength(value.Windows, WindowCount) || !HasLength(value.RuntimeFunctions, RuntimeFunctionCapacity)
            || !HasLength(value.Resources, ResourceCapacity) || !HasLength(value.Events, EventCapacity)
            || !HasLength(value.FixedContractSha256, 32) || !HasLength(value.DependencyGraphSha256, 32)
            || !HasLength(value.Admission.AdmissionAuthoritySha256, 32)
            || !HasLength(value.Admission.ExclusivitySha256, 32)
            || !MaskBounds(value)) return false;
        foreach (var record in value.Modules)
            if (!HasLength(record.Sha256, 32)) return false;
        foreach (var record in value.Typed)
            if (!HasLength(record.BodySha256, 32) || !HasLength(record.OriginalCodeSha256, 32)) return false;
        foreach (var record in value.Families)
            if (record.Reserved0 != 0 || !HasLength(record.ConfigSha256, 32)) return false;
        foreach (var record in value.Windows)
            if (record.Reserved0 != 0 || record.Reserved1 != 0 || record.Reserved2 != 0 || record.Reserved3 != 0
                || !HasLength(record.ExpectedBytes, 32) || !HasLength(record.PatchBytes, 32)
                || !HasLength(record.ObservedBytes, 32) || !HasLength(record.RelayCodeSha256, 32)
                || !HasLength(record.SourceWindowSha256, 32)) return false;
        foreach (var record in value.RuntimeFunctions)
            if ((record.FrameRegisterAndOffset & 0xffff0000u) != 0 || !HasLength(record.UnwindSha256, 32)) return false;
        foreach (var record in value.Resources)
            if (!HasLength(record.TableSha256, 32)) return false;
        return true;
    }

    // Capacity bounds, not a proof that the referenced members are configured,
    // committed, occupied, compatible or owned by this exact publication.
    internal static bool MaskBounds(in NativeEngineTransportPublicationV5 value)
    {
        if (((value.ConfiguredFamilies | value.BoundFamilies | value.RetainedFamilies) & ~AllFamilies) != 0
            || ((value.PreparedWindows | value.WriteAttemptedWindows | value.CommittedWindows
                | value.OutstandingProtectionWindows) & ~AllWindows) != 0
            || (value.RetainedResources & ~AllResources) != 0 || !HasLength(value.Families, FamilyCount)) return false;
        foreach (var record in value.Families)
            if ((record.TypedMask & ~AllTyped) != 0 || (record.SiteMask & ~AllSites) != 0
                || (record.WindowMask & ~AllWindows) != 0) return false;
        return true;
    }
    private static bool HasLength<T>(T[]? value, int length) => value is not null && value.Length == length;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeEngineTransportModuleV5
{
    internal uint Kind, Machine, ImageSize, TimeDateStamp;
    internal ulong Module, FileBytes, FileIndex;
    internal uint VolumeSerial, Flags;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)]
    internal byte[] Sha256;
    internal ulong HeldFile;
    internal uint EntryRva, ExceptionRva;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeEngineTransportTypedRowV5
{
    internal uint Version, ByteSize, Index, ChannelKind;
    internal uint FamilyIndex, ModuleIndex, WindowIndex, Flags;
    internal ulong Module, Target, Original, Continuation, Entry, HelperEpoch;
    internal uint Rva, EndRva, PrefixBytes, SourceRfFirst;
    internal uint SourceRfCount, OriginalRfFirst, OriginalRfCount, HelperSlot;
    internal uint OriginalBytes, OriginalModuleIndex, ScratchRegister, SourceBodyBytes;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)]
    internal byte[] BodySha256;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)]
    internal byte[] OriginalCodeSha256;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeEngineTransportSiteV5
{
    internal uint Version, ByteSize, Index, FamilyIndex;
    internal uint ChannelKind, PhaseKind, WindowIndex, FunctionIndex;
    internal uint ObservationRva, LogicalBytes, LogicalContinuationRva, PhysicalContinuationRva;
    internal uint BranchTargetRva, BypassReplayRva, BypassReplayBytes, BypassContinuationRva;
    internal ulong SitePc, RelayReturnPc, ProbeEntry, HelperEpoch;
    internal uint HelperSlot, Flags, SourceRfIndex, ResourceIndex;
    internal ulong Relay, AlternateContinuation;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeEngineTransportFamilyV5
{
    internal uint Version, ByteSize, Index, FamilyKind;
    internal uint ChannelKind, Slot, AbiVersion, FrameBytes;
    internal uint CompletionBytes, InfoBytes, ConfigBytes, ObserverBytes;
    internal ulong Epoch, Module, Entry, Target, Original;
    internal ulong TypedMask, SiteMask, WindowMask;
    internal uint ConfigureAttempts, ConfigureSucceeded, Flags, LastWin32Error;
    internal uint MainOsThread, HelperUnknown, RetainedClaim, Reserved0;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)]
    internal byte[] ConfigSha256;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeEngineTransportWindowV5
{
    internal uint Version, ByteSize, Index, Role;
    internal uint TypedIndex, SiteIndex, FamilyIndex, Flags;
    internal uint ModuleIndex, SourceRfIndex, PhysicalRva, PhysicalBytes;
    internal uint LogicalRva, LogicalBytes, ResourceIndex, RelayRfFirst;
    internal uint RelayRfCount, LiteralBytes, RelayBytes, Reserved0;
    internal ulong Source, PatchTarget, Relay, RelayReturnPc, Continuation;
    internal ulong AlternateContinuation, ProbeEntry, LiteralAddress, LiteralValue;
    internal uint OriginalProtection, ObservedProtection, ProtectAttempts, ProtectSucceeded;
    internal uint SourceWriteAttempts, SourceBytesWritten, ReadbackAttempts, ReadbackVerified;
    internal uint RestoreAttempts, RestoreSucceeded, FlushAttempts, FlushSucceeded;
    internal uint ProtectionOutstanding, LastWin32Error, ReadbackBytes, Reserved1;
    internal ulong ProtectionBase, ProtectionBytes, WriteSequence, CommitSequence;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)]
    internal byte[] ExpectedBytes;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)]
    internal byte[] PatchBytes;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)]
    internal byte[] ObservedBytes;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)]
    internal byte[] RelayCodeSha256;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)]
    internal byte[] SourceWindowSha256;
    internal ulong ObservedLiteralValue;
    internal uint RelayOriginalProtection, RelayObservedProtection, FailureStage, Reserved2;
    internal ulong ReadbackSequence, Reserved3;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeEngineTransportRuntimeFunctionV5
{
    internal uint Version, ByteSize, Index, Kind;
    internal uint ModuleIndex, ResourceIndex, TableEntryIndex, ParentIndex;
    internal ulong ImageBase, RuntimeFunctionAddress;
    internal uint BeginRva, EndRva, UnwindRva, UnwindBytes;
    internal uint UnwindFlags, PrologBytes, CodeSlots, FrameRegisterAndOffset;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)]
    internal byte[] UnwindSha256;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeEngineTransportResourceV5
{
    internal uint Version, ByteSize, Index, Flags;
    internal ulong AllocationBase, AllocationBytes, TableBase, TableAddress;
    internal uint TableCount, RfFirst, RfCount, AllocationProtection;
    internal uint ObservedProtection, AllocationAttempts, AllocationSucceeded, RxProtectAttempts;
    internal uint RxProtectSucceeded, TableRegisterAttempts, TableRegisterSucceeded, RootRetained;
    internal uint LastWin32Error, ProtectionOutstanding, TableRegistered, AllocationOwned;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)]
    internal byte[] TableSha256;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeEngineTransportEventV5
{
    internal ulong Sequence;
    internal uint Stage, ObjectKind, ObjectIndex, State;
    internal uint Failure, Win32Error, BytesWritten, Flags;
    internal ulong Detail;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeEngineTransportAdmissionReceiptV5
{
    internal uint Version, ByteSize, WitnessFlags, EarliestThreadId;
    internal uint PreWriteThreadCount, SuspendedThreadCount, NonMainInteriorRipCount, PostCommitThreadCount;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)]
    internal byte[] AdmissionAuthoritySha256;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)]
    internal byte[] ExclusivitySha256;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct NativeEngineTransportPublicationV5
{
    internal uint Version, ByteSize, State, Failure;
    internal ulong SchemaMagic, Epoch, SnapshotSequence;
    internal uint OwnerThread, ProcessId, AttachReason, StartupStatic;
    internal uint Profile, Claimed, ProducerThread, MainThread;
    internal uint MainThreadProof, VerifyFailures, LastWin32Error, EventCount;
    internal uint EventOverflow, SourceWriteAttempts, SourceWriteCompletions, ProtectionOutstanding;
    internal uint ModuleCount, TypedCount, SiteCount, WindowCount;
    internal uint FamilyCount, RuntimeFunctionCount, ResourceCount, EventCapacity;
    internal ulong ConfiguredFamilies, BoundFamilies, RetainedFamilies, PreparedWindows;
    internal ulong WriteAttemptedWindows, CommittedWindows, OutstandingProtectionWindows, RetainedResources;
    internal ulong BootstrapModule, KernelModule, TargetModule, ExecutableModule, DoorstopModule;
    internal uint FailureWindowIndex, FailureFamilyIndex, FailureResourceIndex, Reserved0;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)]
    internal byte[] FixedContractSha256;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)]
    internal byte[] DependencyGraphSha256;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
    internal NativeEngineTransportModuleV5[] Modules;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 13)]
    internal NativeEngineTransportTypedRowV5[] Typed;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 23)]
    internal NativeEngineTransportSiteV5[] Sites;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
    internal NativeEngineTransportFamilyV5[] Families;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
    internal NativeEngineTransportWindowV5[] Windows;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
    internal NativeEngineTransportRuntimeFunctionV5[] RuntimeFunctions;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 23)]
    internal NativeEngineTransportResourceV5[] Resources;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)]
    internal NativeEngineTransportEventV5[] Events;
    internal NativeEngineTransportAdmissionReceiptV5 Admission;
}
