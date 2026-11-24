using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

// Value-only copies. A tuple describes a configured native channel and cannot
// construct the private process Ready owner or authorize a first installation.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeOperationPublicationRow
{
    internal uint Id, Kind, Slot, Reserved;
    internal IntPtr Target, Original, Entry;
    internal ulong Epoch;
    internal uint Rva, BodySize, UnwindRva, Protection;
    internal NativeOperationChannelIdentity Identity => new((NativeOperationKind)Kind, Slot, Target, Original, Entry, Epoch);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeFirstInstallInformationV7
{
    internal NativeFirstInstallInformationV2 Listener;
    internal uint OperationCapabilityVersion, OperationCount, ExpectedWrites, Reserved;
    internal ulong ConfiguredMask, PublishedMask;
    internal NativeOperationPublicationRow Row0, Row1, Row2, Row3, Row4, Row5, Row6, Row7, Row8;
    internal NativeOperationPublicationRow Row9, Row10, Row11, Row12, Row13, Row14, Row15, Row16, Row17, Row18, Row19;
    internal NativeOperationPublicationRow Row20, Row21, Row22, Row23, Row24, Row25;
    internal NativeTweenSingleEventPublication SingleEvent;
}

internal readonly record struct NativeOperationChannelIdentity(NativeOperationKind Kind, uint Slot,
    IntPtr Target, IntPtr Original, IntPtr Entry, ulong Epoch)
{
    internal bool IsComplete => Kind is NativeOperationKind.Void0 or NativeOperationKind.Reference1 or NativeOperationKind.Reference2 or NativeOperationKind.Single1
        && Slot < 32 && Target != IntPtr.Zero && Original != IntPtr.Zero && Entry != IntPtr.Zero && Epoch != 0
        && Target != Entry && Original != Entry;
}

internal static class NativeOperationPublication
{
    internal const int Count = 26;
    internal const ulong CompleteMask = (1UL << Count) - 1;
    internal static NativeOperationPublicationRow Row(NativeFirstInstallInformationV7 info, uint index) => index switch {
        0 => info.Row0, 1 => info.Row1, 2 => info.Row2, 3 => info.Row3, 4 => info.Row4, 5 => info.Row5,
        6 => info.Row6, 7 => info.Row7, 8 => info.Row8, 9 => info.Row9, 10 => info.Row10, 11 => info.Row11,
        12 => info.Row12, 13 => info.Row13, 14 => info.Row14, 15 => info.Row15, 16 => info.Row16, 17 => info.Row17,
        18 => info.Row18, 19 => info.Row19, 20 => info.Row20, 21 => info.Row21, 22 => info.Row22,
        23 => info.Row23, 24 => info.Row24, 25 => info.Row25,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    internal static (NativeOperationKind Kind, uint Slot, uint Rva) Declaration(uint index)
    {
        if (index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        // IDs follow the same native declaration order: fourteen UnityEvent
        // operations, six coroutine Stop operations, five Tween operations and
        // the typed UnityEvent<Single>.Invoke call target.
        // Slots belong to an ABI kind, so each kind allocates independently
        // downward from 31.
        // The global operation ID cannot be used to calculate its slot.
        NativeOperationKind kind; uint rva;
        if (index < 14)
        {
            var declaration = UnityEventOperationProfile.Get((UnityEventOperation)index);
            kind = declaration.Kind; rva = checked((uint)declaration.Rva);
        }
        else if (index < 20)
        {
            var declaration = CoroutineStopProfile.Get((CoroutineStopOperation)(index - 14));
            kind = declaration.Kind; rva = checked((uint)declaration.Rva);
        }
        else if (index < 25)
        {
            var declaration = TweenOperationProfile.Get((NativeTweenOperation)(index - 20));
            kind = declaration.Kind; rva = checked((uint)declaration.Rva);
        }
        else { kind = NativeOperationKind.Single1; rva = 0xbc0ff0; }
        var slot = index switch { 0 => 31u, 1 => 30u, 2 => 31u, 3 => 31u, 4 => 30u, 5 => 29u,
            6 => 29u, 7 => 28u, 8 => 30u, 9 => 28u, 10 => 27u, 11 => 29u,
            12 => 27u, 13 => 26u, 14 => 25u, 15 => 24u, 16 => 26u, 17 => 23u, 18 => 22u, 19 => 21u,
            20 => 25u, 21 => 20u, 22 => 19u, 23 => 24u, 24 => 23u, 25 => 31u,
            _ => throw new ArgumentOutOfRangeException(nameof(index)) };
        return (kind, slot, rva);
    }
    internal static bool HasShape(NativeFirstInstallInformationV7 info)
    {
        if (!NativeListenerPublication.HasShapeV7Prefix(info.Listener)
            || info.OperationCapabilityVersion != 4 || info.OperationCount != Count || info.ExpectedWrites != 64
            || info.Reserved != 0 || info.ConfiguredMask != CompleteMask || info.PublishedMask != CompleteMask) return false;
        var common = info.Listener.Common; var module = new IntPtr(unchecked((long)common.ModuleBase));
        for (uint index = 0; index < Count; ++index)
        {
            var row = Row(info, index); var expected = Declaration(index);
            if (row.Id != index || row.Kind != (uint)expected.Kind || row.Slot != expected.Slot || row.Reserved != 0
                || row.Rva != expected.Rva || row.Target != IntPtr.Add(module, checked((int)row.Rva)) || !row.Identity.IsComplete || row.Target == row.Original
                || row.BodySize == 0 || row.BodySize > 0x10000 || row.Rva >= common.ImageSize
                || (ulong)row.Rva + row.BodySize > common.ImageSize || row.UnwindRva == 0 || row.UnwindRva >= common.ImageSize
                || !NativeListenerPublication.ExecutableProtection(row.Protection)) return false;
            for (uint previous = 0; previous < index; ++previous)
            {
                var other = Row(info, previous);
                if (other.Target == row.Target || other.Entry == row.Entry || other.Original == row.Original
                    || (other.Kind == row.Kind && other.Slot == row.Slot)) return false;
            }
        }
        return NativeSingleEventPublication.HasShape(info.SingleEvent, info.Listener.Common, info.Row25);
    }
}
