using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal enum NativeTweenSingleEventStage : uint { Mid, Before, Returned, Abnormal }
[Flags] internal enum NativeTweenSingleEventFlags : uint
{ None = 0, ObserverFaulted = 1, MissingPost = 2, ParentAbnormal = 4, TargetReadFaulted = 8 }

[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenSingleEventFrame
{
    internal uint Version, Size;
    internal NativeTweenSingleEventStage Stage;
    internal uint Site, ThreadId;
    internal NativeTweenSingleEventFlags Flags;
    internal uint Reserved0, Reserved1;
    internal ulong Epoch, ParentEpoch, ParentSequence, ChildSequence, ParentManagedToken;
    internal IntPtr Event, List, Invokable, ActualTarget, ArgRcx, ArgRdx, ArgR8, ArgR9;
    internal IntPtr ParentMethod, ParentCaller, SourceReturnPc, TransportReturnPc;
    internal ulong SourceRsp, Index;
    internal uint ValueBits, Reserved2;
    internal IntPtr MachineFrame;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeTweenSingleEventInformation
{
    internal uint Version, Size, Configured, Attached, Unknown, ParentCapacity;
    internal ulong Epoch, ParentEpoch, ParentsEntered, ParentsExited, ParentsInFlight, Mids;
    internal ulong ChildrenEntered, ChildrenExited, ChildrenInFlight, CallbackFaults, RejectedReads, MissingPosts;
    internal ulong RetainedParents, RetainedChildren, Overflow;
}

internal readonly record struct NativeTweenSingleEventIdentity(ulong Epoch, ulong ParentEpoch,
    IntPtr MidEntry, IntPtr PreEntry, IntPtr PostEntry, IntPtr MidReturnPc,
    IntPtr Source0, IntPtr Source1, IntPtr Source2, IntPtr Post0, IntPtr Post1, IntPtr Post2, IntPtr DirectTarget)
{
    internal static NativeTweenSingleEventIdentity From(NativeTweenSingleEventPublication p) => new(p.Epoch, p.ParentEpoch,
        p.MidEntry, p.PreEntry, p.PostEntry, p.MidReturnPc, p.SourceReturnPc0, p.SourceReturnPc1, p.SourceReturnPc2,
        p.PostTransportReturnPc0, p.PostTransportReturnPc1, p.PostTransportReturnPc2, p.DirectTarget);
    internal IntPtr Source(uint site) => site switch { 0 => Source0, 1 => Source1, 2 => Source2, _ => IntPtr.Zero };
    internal IntPtr Post(uint site) => site switch { 0 => Post0, 1 => Post1, 2 => Post2, _ => IntPtr.Zero };
    internal bool IsComplete => Epoch != 0 && ParentEpoch != 0 && MidEntry != IntPtr.Zero && PreEntry != IntPtr.Zero
        && PostEntry != IntPtr.Zero && MidEntry != PreEntry && MidEntry != PostEntry && PreEntry != PostEntry
        && DirectTarget != IntPtr.Zero && new[] { MidReturnPc, Source0, Source1, Source2, Post0, Post1, Post2 }.All(p => p != IntPtr.Zero)
        && new[] { MidReturnPc, Source0, Source1, Source2, Post0, Post1, Post2 }.Distinct().Count() == 7;
}

internal readonly record struct NativeTweenSingleEventObservation(NativeTweenSingleEventFrame Frame, NativeEventMidFrame? Machine);

internal static class NativeTweenSingleEventValidation
{
    // Value validation grants no source authority. Only Bridge.Delivery, backed by native ReadCurrent,
    // can enter the physical parent and root-taking paths.
    internal static bool Valid(NativeTweenSingleEventObservation observation, NativeTweenSingleEventIdentity identity, uint thread)
    {
        var f = observation.Frame;
        if (!identity.IsComplete || f.Version != 1 || f.Size != 200 || f.Epoch != identity.Epoch || f.ParentEpoch != identity.ParentEpoch
            || f.ThreadId == 0 || f.ThreadId != thread || f.ParentSequence is 0 or > long.MaxValue || f.Event == IntPtr.Zero || f.List == IntPtr.Zero
            || f.ParentMethod == IntPtr.Zero || f.ParentCaller == IntPtr.Zero || f.Reserved0 != 0 || f.Reserved1 != 0 || f.Reserved2 != 0
            || f.Index > int.MaxValue || (f.SourceRsp & 15) != 0 || f.SourceRsp == 0 || ((uint)f.Flags & ~15u) != 0) return false;
        if (f.Stage == NativeTweenSingleEventStage.Mid)
        {
            if (f.Site != 3 || f.ChildSequence != 0 || f.Invokable != IntPtr.Zero || f.ActualTarget != IntPtr.Zero
                || f.SourceReturnPc != identity.MidReturnPc || f.TransportReturnPc != identity.MidReturnPc || f.Index != 0 || f.Flags != 0) return false;
        }
        else
        {
            if (f.Site > 2 || f.ChildSequence is 0 or > long.MaxValue || f.Invokable == IntPtr.Zero || f.ArgRcx != f.Invokable
                || f.SourceReturnPc != identity.Source(f.Site) || f.Site == 1 && f.ActualTarget != identity.DirectTarget
                || f.Site == 1 && f.ArgRdx != IntPtr.Zero
                || f.Site == 2 && f.ArgRdx == IntPtr.Zero || f.Site != 1 && f.ArgR8 == IntPtr.Zero) return false;
            if (f.Stage == NativeTweenSingleEventStage.Before && (f.Flags != 0 || f.ActualTarget == IntPtr.Zero || f.TransportReturnPc != f.SourceReturnPc)) return false;
            if (f.Stage == NativeTweenSingleEventStage.Returned && (f.ActualTarget == IntPtr.Zero || (f.Flags & ~NativeTweenSingleEventFlags.ObserverFaulted) != 0
                || f.TransportReturnPc != identity.Post(f.Site))) return false;
            if (f.Stage == NativeTweenSingleEventStage.Abnormal && (f.Flags & (NativeTweenSingleEventFlags.MissingPost | NativeTweenSingleEventFlags.ParentAbnormal)) == 0) return false;
            if (f.Stage is not (NativeTweenSingleEventStage.Before or NativeTweenSingleEventStage.Returned or NativeTweenSingleEventStage.Abnormal)) return false;
        }
        if (observation.Machine is not { } m)
            return f.Stage == NativeTweenSingleEventStage.Abnormal && f.TransportReturnPc == f.SourceReturnPc;
        if (!m.IsValid || (m.EntryRsp & 15) != 8 || m.Rsp != f.SourceRsp || m.ReturnPc != f.TransportReturnPc
            || unchecked((long)m.Rbp) != f.Event.ToInt64() || unchecked((long)m.R15) != f.List.ToInt64()
            || unchecked((long)m.R13) != f.ParentMethod.ToInt64() || unchecked((uint)m.Rsi) != f.Index
            || unchecked((uint)m.Xmm6.Low) != f.ValueBits) return false;
        if (f.Stage == NativeTweenSingleEventStage.Mid) return m.Rcx == 0;
        if (f.Stage != NativeTweenSingleEventStage.Before) return true;
        return unchecked((long)m.Rcx) == f.ArgRcx.ToInt64() && unchecked((long)m.Rdx) == f.ArgRdx.ToInt64()
            && unchecked((long)m.R8) == f.ArgR8.ToInt64() && unchecked((long)m.R9) == f.ArgR9.ToInt64()
            && (f.Site != 0 || unchecked((uint)m.Xmm1.Low) == f.ValueBits)
            && (f.Site != 2 || unchecked((long)m.R14) == f.Invokable.ToInt64());
    }

    internal static bool SameChild(NativeTweenSingleEventFrame a, NativeTweenSingleEventFrame b)
        => a.Epoch == b.Epoch && a.ParentEpoch == b.ParentEpoch && a.ParentSequence == b.ParentSequence
            && a.ParentManagedToken == b.ParentManagedToken && a.ChildSequence == b.ChildSequence && a.Site == b.Site
            && a.Event == b.Event && a.List == b.List && a.Invokable == b.Invokable && a.Index == b.Index
            && a.ActualTarget == b.ActualTarget && a.ArgRcx == b.ArgRcx && a.ArgRdx == b.ArgRdx && a.ArgR8 == b.ArgR8 && a.ArgR9 == b.ArgR9
            && a.ParentMethod == b.ParentMethod && a.ParentCaller == b.ParentCaller && a.SourceReturnPc == b.SourceReturnPc
            && a.SourceRsp == b.SourceRsp && a.ThreadId == b.ThreadId && a.ValueBits == b.ValueBits;
}
