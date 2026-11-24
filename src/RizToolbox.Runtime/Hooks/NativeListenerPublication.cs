using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

// Value-only listener prefix embedded in the current V7 receipt. The V2 type
// name describes its 416-byte layout, not an independently adopted protocol.
// These records are identities, not caller-created Ready authorities or SDK handles.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeFirstInstallInformationV2
{
    internal NativeFirstInstallInformation Common;
    internal uint CapabilityVersion, ListenerSlot;
    internal ulong ListenerEpoch;
    internal IntPtr PreEntry, PostEntry, PreRelay, PostRelay;
    internal IntPtr SourcePc0, SourcePc1, SourcePc2;
    internal IntPtr SourceReturnPc0, SourceReturnPc1, SourceReturnPc2;
    internal IntPtr PostStub0, PostStub1, PostStub2;
    internal IntPtr PostTransportReturnPc0, PostTransportReturnPc1, PostTransportReturnPc2;
    internal IntPtr DirectTarget0, DirectTarget1, FunctionTable;
    internal ulong FunctionTableBase;
    internal IntPtr UnwindInfo0, UnwindInfo1, UnwindInfo2;
    internal uint SourceWidth0, SourceWidth1, SourceWidth2, PostWidth0, PostWidth1, PostWidth2;
    internal uint SourceProtection0, SourceProtection1, SourceProtection2, PostProtection0, PostProtection1, PostProtection2;
    internal uint RegisteredFunctionCount, PrewriteLookupChecks, PrewriteBoundaryChecks, Reserved2;
    internal ulong AllocationSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeListenerInformation
{
    internal uint Version, Size, Slot, Attached;
    internal ulong Epoch, ParentEpoch;
    internal IntPtr PreEntry, PostEntry;
    internal IntPtr SourceReturnPc0, SourceReturnPc1, SourceReturnPc2;
    internal IntPtr PostTransportReturnPc0, PostTransportReturnPc1, PostTransportReturnPc2;
    internal IntPtr DirectTarget0, DirectTarget1;
    internal ulong Entered, Exited, InFlight, Faults, Abnormal, Overflow, MissingPost;
    internal uint Unknown, Reserved;
}

internal readonly record struct NativeListenerSiteIdentity(uint Site, IntPtr SourcePc, IntPtr SourceReturnPc,
    IntPtr PostStub, IntPtr PostTransportReturnPc, IntPtr UnwindInfo, uint SourceWidth, uint PostWidth,
    uint SourceProtection, uint PostProtection);

internal static class NativeListenerPublication
{
    internal static NativeListenerSiteIdentity Site(NativeFirstInstallInformationV2 info, uint site) => site switch {
        0 => new(site, info.SourcePc0, info.SourceReturnPc0, info.PostStub0, info.PostTransportReturnPc0,
            info.UnwindInfo0, info.SourceWidth0, info.PostWidth0, info.SourceProtection0, info.PostProtection0),
        1 => new(site, info.SourcePc1, info.SourceReturnPc1, info.PostStub1, info.PostTransportReturnPc1,
            info.UnwindInfo1, info.SourceWidth1, info.PostWidth1, info.SourceProtection1, info.PostProtection1),
        2 => new(site, info.SourcePc2, info.SourceReturnPc2, info.PostStub2, info.PostTransportReturnPc2,
            info.UnwindInfo2, info.SourceWidth2, info.PostWidth2, info.SourceProtection2, info.PostProtection2),
        _ => throw new ArgumentOutOfRangeException(nameof(site))
    };

    internal static bool HasShapeV7Prefix(NativeFirstInstallInformationV2 info)
    {
        var common = info.Common;
        if (!NativeFirstInstall.ReadyCommonShape(common, 7, 2528, 64) || info.CapabilityVersion != 1
            || info.ListenerSlot != common.VoidSlot || info.ListenerEpoch == 0 || info.PreEntry == IntPtr.Zero
            || info.PostEntry == IntPtr.Zero || info.PreEntry == info.PostEntry || info.PreRelay == IntPtr.Zero
            || info.PostRelay == IntPtr.Zero || info.FunctionTable == IntPtr.Zero || info.FunctionTableBase != common.ModuleBase
            || info.RegisteredFunctionCount != 3 || info.PrewriteLookupChecks != 12 || info.PrewriteBoundaryChecks != 12
            || info.Reserved2 != 0 || info.AllocationSize != 4096 || info.DirectTarget0 == IntPtr.Zero
            || info.DirectTarget0 != info.DirectTarget1) return false;
        var module = new IntPtr(unchecked((long)common.ModuleBase));
        if (info.DirectTarget0 != IntPtr.Add(module, 0x154e5b0)) return false;
        var block = unchecked((ulong)info.PostStub0.ToInt64());
        if (block <= common.ModuleBase || (block & 0xfff) != 0 || block - common.ModuleBase > uint.MaxValue - 4096UL
            || info.FunctionTable != IntPtr.Add(info.PostStub0, 0x380) || info.PreRelay != IntPtr.Add(info.PostStub0, 0x500)
            || common.Relay != IntPtr.Add(info.PostStub0, 0x520) || info.PostRelay != IntPtr.Add(info.PostStub0, 0x540)) return false;
        var sourceRvas = new[] { 0x1564470, 0x15644c2, 0x156451f };
        for (uint index = 0; index < 3; ++index)
        {
            var site = Site(info, index); var width = index == 2 ? 6u : 5u;
            if (site.SourcePc != IntPtr.Add(module, sourceRvas[index]) || site.SourceWidth != width
                || site.SourceReturnPc != IntPtr.Add(site.SourcePc, checked((int)width))
                || site.PostWidth != (index == 0 ? 6u : 9u) || site.PostStub == IntPtr.Zero
                || site.PostTransportReturnPc == IntPtr.Zero || site.UnwindInfo == IntPtr.Zero
                || site.PostStub != IntPtr.Add(info.PostStub0, checked((int)index * 0x100))
                || site.PostTransportReturnPc != IntPtr.Add(site.PostStub, 5) || site.UnwindInfo != IntPtr.Add(site.PostStub, 0x40)
                || !ExecutableProtection(site.SourceProtection) || !ExecutableProtection(site.PostProtection)) return false;
            for (uint previous = 0; previous < index; ++previous)
            {
                var other = Site(info, previous);
                if (other.PostStub == site.PostStub || other.PostTransportReturnPc == site.PostTransportReturnPc
                    || other.UnwindInfo == site.UnwindInfo) return false;
            }
        }
        return true;
    }

    internal static bool MatchesLane(NativeListenerInformation lane, NativeFirstInstallInformationV2 publication)
        => lane.Version == 1 && lane.Size == 176 && lane.Slot == publication.ListenerSlot && lane.Attached <= 1
        && lane.Epoch == publication.ListenerEpoch && lane.ParentEpoch == publication.Common.UnityEpoch
        && lane.PreEntry == publication.PreEntry && lane.PostEntry == publication.PostEntry
        && lane.SourceReturnPc0 == publication.SourceReturnPc0 && lane.SourceReturnPc1 == publication.SourceReturnPc1
        && lane.SourceReturnPc2 == publication.SourceReturnPc2
        && lane.PostTransportReturnPc0 == publication.PostTransportReturnPc0 && lane.PostTransportReturnPc1 == publication.PostTransportReturnPc1
        && lane.PostTransportReturnPc2 == publication.PostTransportReturnPc2
        && lane.DirectTarget0 == publication.DirectTarget0 && lane.DirectTarget1 == publication.DirectTarget1
        && lane.Unknown == 0 && lane.Reserved == 0 && lane.Faults == 0 && lane.Overflow == 0 && lane.MissingPost == 0
        && lane.InFlight <= long.MaxValue;

    internal static bool ExecutableProtection(uint protection)
        => (protection & (0x100u | 0x01u)) == 0 && (protection & 0xffu) is 0x10u or 0x20u or 0x40u or 0x80u;
}
