using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

// Immutable copied publication evidence. Only NativeFirstInstall's private
// loaded owner can adopt the native producer; this value cannot configure it.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenSingleEventPublication
{
    internal uint CapabilityVersion, ByteSize, Phase, Unknown;
    internal uint OperationId, Kind, Slot, ExpectedWrites;
    internal ulong Epoch, ParentEpoch;
    internal IntPtr MidEntry, PreEntry, PostEntry, Allocation;
    internal ulong AllocationSize;
    internal IntPtr MidRelay, PreRelay, PostRelay, DirectTarget;
    internal IntPtr SpanPc0, SpanPc1, SpanPc2, SpanPc3, SpanPc4, SpanPc5, SpanPc6;
    internal IntPtr SourceReturnPc0, SourceReturnPc1, SourceReturnPc2, MidReturnPc;
    internal IntPtr PostStub0, PostStub1, PostStub2;
    internal IntPtr PostTransportReturnPc0, PostTransportReturnPc1, PostTransportReturnPc2;
    internal IntPtr FunctionTable;
    internal ulong FunctionTableBase;
    internal IntPtr UnwindInfo0, UnwindInfo1, UnwindInfo2;
    internal uint SpanWidth0, SpanWidth1, SpanWidth2, SpanWidth3, SpanWidth4, SpanWidth5, SpanWidth6;
    internal uint Protection0, Protection1, Protection2, Protection3, Protection4, Protection5, Protection6;
    internal uint StubSize0, StubSize1, StubSize2, UnwindSize0, UnwindSize1, UnwindSize2;
    internal uint RegisteredFunctionCount, LookupChecks, BoundaryChecks, Reserved;
    internal uint ConfiguredMask, PublishedMask;
    internal ulong AttemptedWrites, CompletedWrites;
}

internal readonly record struct NativeSingleEventSpan(IntPtr Pc, uint Width, uint Protection);
internal readonly record struct NativeSingleEventSite(IntPtr SourceReturnPc, IntPtr PostStub,
    IntPtr PostTransportReturnPc, IntPtr UnwindInfo, uint StubSize, uint UnwindSize);

internal static class NativeSingleEventPublication
{
    internal static NativeSingleEventSpan Span(NativeTweenSingleEventPublication value, int index) => index switch
    {
        0 => new(value.SpanPc0, value.SpanWidth0, value.Protection0),
        1 => new(value.SpanPc1, value.SpanWidth1, value.Protection1),
        2 => new(value.SpanPc2, value.SpanWidth2, value.Protection2),
        3 => new(value.SpanPc3, value.SpanWidth3, value.Protection3),
        4 => new(value.SpanPc4, value.SpanWidth4, value.Protection4),
        5 => new(value.SpanPc5, value.SpanWidth5, value.Protection5),
        6 => new(value.SpanPc6, value.SpanWidth6, value.Protection6),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    internal static NativeSingleEventSite Site(NativeTweenSingleEventPublication value, int index) => index switch
    {
        0 => new(value.SourceReturnPc0, value.PostStub0, value.PostTransportReturnPc0, value.UnwindInfo0, value.StubSize0, value.UnwindSize0),
        1 => new(value.SourceReturnPc1, value.PostStub1, value.PostTransportReturnPc1, value.UnwindInfo1, value.StubSize1, value.UnwindSize1),
        2 => new(value.SourceReturnPc2, value.PostStub2, value.PostTransportReturnPc2, value.UnwindInfo2, value.StubSize2, value.UnwindSize2),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    internal static bool Inside(NativeTweenSingleEventPublication value, IntPtr pointer, ulong bytes)
    {
        var start = unchecked((ulong)value.Allocation.ToInt64());
        var address = unchecked((ulong)pointer.ToInt64());
        return start != 0 && bytes != 0 && address >= start && value.AllocationSize >= bytes
            && address - start <= value.AllocationSize - bytes;
    }

    internal static bool HasShape(NativeTweenSingleEventPublication value, NativeFirstInstallInformation common,
        NativeOperationPublicationRow parent)
    {
        if (value.CapabilityVersion != 1 || value.ByteSize != 416 || value.Phase != 3 || value.Unknown != 0
            || value.OperationId != 25 || value.Kind != 3 || value.Slot != 31 || value.ExpectedWrites != 7
            || value.Epoch == 0 || value.ParentEpoch != parent.Epoch || parent.Id != 25 || parent.Kind != 3 || parent.Slot != 31
            || value.MidEntry == IntPtr.Zero || value.PreEntry == IntPtr.Zero || value.PostEntry == IntPtr.Zero
            || value.MidEntry == value.PreEntry || value.PreEntry == value.PostEntry || value.MidEntry == value.PostEntry
            || value.FunctionTableBase != common.ModuleBase || value.Reserved != 0
            || value.ConfiguredMask != 0x7f || value.PublishedMask != 0x7f
            || value.AttemptedWrites != 7 || value.CompletedWrites != 7 || value.RegisteredFunctionCount != 3
            || value.LookupChecks < 15 || value.BoundaryChecks < 15 || value.AllocationSize != 4096
            || unchecked((ulong)value.Allocation.ToInt64()) % 4096 != 0
            || !Inside(value, value.FunctionTable, 36) || !Inside(value, value.MidRelay, 14)
            || !Inside(value, value.PreRelay, 14) || !Inside(value, value.PostRelay, 14)) return false;
        var module = new IntPtr(unchecked((long)common.ModuleBase));
        var block = unchecked((ulong)value.Allocation.ToInt64());
        if (block <= common.ModuleBase || block - common.ModuleBase > uint.MaxValue - 4096UL
            || value.FunctionTable != IntPtr.Add(value.Allocation, 0x380)
            || value.MidRelay != IntPtr.Add(value.Allocation, 0x500)
            || value.PreRelay != IntPtr.Add(value.Allocation, 0x520)
            || value.PostRelay != IntPtr.Add(value.Allocation, 0x540)
            || value.DirectTarget != IntPtr.Add(module, 0x154e5b0) || value.MidReturnPc != IntPtr.Add(module, 0xbc108c)) return false;
        int[] rvas = { 0xbc1087, 0xbc1107, 0xbc110d, 0xbc115d, 0xbc1162, 0xbc121b, 0xbc1221 };
        uint[] widths = { 9, 6, 9, 5, 9, 6, 9 };
        for (var index = 0; index < 7; ++index)
        {
            var span = Span(value, index);
            if (span.Pc != IntPtr.Add(module, rvas[index]) || span.Width != widths[index]
                || !NativeListenerPublication.ExecutableProtection(span.Protection)) return false;
        }
        for (var index = 0; index < 3; ++index)
        {
            var site = Site(value, index); var source = Span(value, 1 + index * 2);
            if (site.SourceReturnPc != IntPtr.Add(source.Pc, checked((int)source.Width))
                || site.PostTransportReturnPc != IntPtr.Add(site.PostStub, 5) || site.StubSize != 21 || site.UnwindSize != 36
                || site.PostStub != IntPtr.Add(value.Allocation, index * 0x100)
                || site.UnwindInfo != IntPtr.Add(site.PostStub, 0x40)
                || !Inside(value, site.PostStub, 21) || !Inside(value, site.UnwindInfo, 36)) return false;
            for (var other = 0; other < index; ++other)
            {
                var previous = Site(value, other);
                if (previous.PostStub == site.PostStub || previous.UnwindInfo == site.UnwindInfo) return false;
            }
        }
        return true;
    }
}
