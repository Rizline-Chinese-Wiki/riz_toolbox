using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal enum ExactHookAbi { InstanceVoid, InstanceEnum32SingleVoid, InstanceReferenceReturn0, InstanceEnum32Void, InstanceInt32SingleVoid, InstanceBooleanReturn0,
    StaticTimelineModeVoid, StaticTimelineInstantaneousHandle, StaticTimelineEndRangeVoid,
    LocalInitializationReference1, LocalProgressPlayBooleanVoid, StaticTimelineTooltipVoid, InstanceBooleanVoid, CoroutineStartReference1, CoroutineFactoryReference0, NativeVoid0,
    OperationVoid0, OperationReference1Void, OperationReference2Void, OperationSingle1Void, InstanceSingleReturn0,
    PlaybackVoid0, PlaybackSingle1Void, PlaybackBoolean1Void, PlaybackDouble1Void, PlaybackPitchSingle1Void, PlaybackReference1Void, PlaybackBooleanReturn0, PlaybackUInt64_1Void,
    PlaybackIcallSingle1Void }
internal sealed record VerifiedHookTarget(string Id, IntPtr Address, IntPtr MethodInfo, IntPtr DeclaringClass, ExactHookAbi Abi, IntPtr ReturnClass = default);

/// <summary>Unity 2022 MethodInfo layout is explicit and independently cross-checked against exported metadata APIs.</summary>
internal static class HookTargetValidation
{
    internal static void ValidateBooleanArgumentSignature(NativeTypeLayout returns, IReadOnlyList<NativeTypeLayout> parameters)
    {
        if (returns.Kind != NativeValueKind.Void || returns.ByRef || parameters.Count != 1
            || parameters[0] is not { Name: "System.Boolean", Kind: NativeValueKind.Boolean, Size: 1, ByRef: false })
            throw new UnsupportedSignatureException("Boolean-argument observation requires exactly one non-byref System.Boolean byte and void return.");
    }
    internal static void ValidateTimelineHandleLayout(NativeTypeLayout layout, uint alignment)
    {
        if (IntPtr.Size != 8 || layout is not { Name: "Steamworks.Data.TimelineEventHandle", Kind: NativeValueKind.Struct, Size: 8, ByRef: false }
            || alignment != 8 || layout.Fields is null || layout.Fields.Count != 1
            || layout.Fields[0] != new NativeFieldLayout("Value", "System.UInt64", 0)
            || Marshal.SizeOf<ExactHookBroker.TimelineEventHandle>() != 8
            || Marshal.OffsetOf<ExactHookBroker.TimelineEventHandle>(nameof(ExactHookBroker.TimelineEventHandle.Value)) != IntPtr.Zero)
            throw new UnsupportedSignatureException("TimelineEventHandle differs from its exact single UInt64 value/return ABI.");
    }
    // Source: BepInEx/Il2CppInterop MethodInfo_29_1.cs, ApplicableToUnityVersionsSince("2022.1.0").
    // 2023.2+ has a distinct handler and is intentionally outside this build allowlist.
    [StructLayout(LayoutKind.Sequential)]
    internal struct MethodInfo2022
    {
        public IntPtr MethodPointer, VirtualMethodPointer, Invoker, Name, Class, ReturnType, Parameters, RuntimeData, GenericData;
        public uint Token;
        public ushort Flags, ImplementationFlags, Slot;
        public byte ParameterCount, Bitfield;
    }
    internal static void ValidateMetadata(MethodInfo2022 layout, IntPtr apiName, IntPtr apiClass, IntPtr apiReturn,
        uint apiFlags, uint parameterCount, IntPtr expectedAddress)
    {
        if (layout.Name != apiName || layout.Class != apiClass || layout.ReturnType != apiReturn || layout.Flags != (ushort)apiFlags
            || layout.ParameterCount != parameterCount || layout.MethodPointer != expectedAddress || expectedAddress == IntPtr.Zero)
            throw new UnsupportedSignatureException("MethodInfo2022 layout, methodPointer or exact build RVA differs from exported metadata.");
    }
    internal static void VerifyReadable(IntPtr address, nuint bytes)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Exact hooks require Windows x64.");
        if (address == IntPtr.Zero || VirtualQuery(address, out var info, (nuint)Marshal.SizeOf<MemoryInformation>()) == 0
            || info.State != 0x1000 || (info.Protect & (0x100u | 0x01u)) != 0 || bytes > info.RegionSize
            || (ulong)address.ToInt64() - (ulong)info.BaseAddress.ToInt64() > (ulong)(info.RegionSize - bytes))
            throw new UnsupportedSignatureException("Native metadata memory is unreadable or crosses an uncommitted region.");
    }
    internal static void VerifyExecutable(IntPtr address, IntPtr module)
    {
        VerifyReadable(address, 1);
        VirtualQuery(address, out var info, (nuint)Marshal.SizeOf<MemoryInformation>());
        if (info.AllocationBase != module || info.Type != 0x1000000 || (info.Protect & 0xF0) == 0)
            throw new UnsupportedSignatureException("Method entry is not executable memory in the validated GameAssembly image.");
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryInformation
    {
        public IntPtr BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State, Protect, Type;
    }
    [DllImport("kernel32")] private static extern nuint VirtualQuery(IntPtr address, out MemoryInformation info, nuint length);
}
