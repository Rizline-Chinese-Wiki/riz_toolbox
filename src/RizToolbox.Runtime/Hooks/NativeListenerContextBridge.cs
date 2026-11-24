using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal enum NativeListenerStage : uint { Before = 1, Returned = 2, Abnormal = 3 }
[Flags] internal enum NativeListenerFlags : uint
{ None = 0, BeforeFaulted = 1, ParentAbnormal = 2, MissingPost = 4, TargetReadFaulted = 8 }

[StructLayout(LayoutKind.Sequential)]
internal struct NativeListenerFrame
{
    internal uint Version, Size, Slot, Site;
    internal NativeListenerStage Stage;
    internal uint OsThreadId;
    internal NativeListenerFlags Flags;
    internal uint Reserved0;
    internal ulong Epoch, ParentSequence, ChildSequence, ParentManagedToken;
    internal IntPtr Event, List, Invokable, ArgRcx, ArgRdx, ArgR8, ArgR9, ActualTarget, SourceReturnPc, TransportReturnPc;
    internal ulong SourceRsp, Index;
    internal IntPtr MachineFrame;
    internal ulong Reserved1;
}

// MachineFrame is copied while native memory is live. The delivered frame has
// its borrowed MachineFrame pointer cleared, including on abnormal completion.
internal readonly record struct NativeListenerObservation(NativeListenerFrame Frame, NativeEventMidFrame? Machine);
internal readonly record struct NativeListenerAdoptionIdentity(uint Slot, ulong Epoch, ulong ParentEpoch,
    IntPtr PreEntry, IntPtr PostEntry, IntPtr Source0, IntPtr Source1, IntPtr Source2,
    IntPtr Transport0, IntPtr Transport1, IntPtr Transport2, IntPtr Direct0, IntPtr Direct1)
{
    internal static NativeListenerAdoptionIdentity From(NativeListenerInformation info) => new(info.Slot, info.Epoch, info.ParentEpoch,
        info.PreEntry, info.PostEntry, info.SourceReturnPc0, info.SourceReturnPc1, info.SourceReturnPc2,
        info.PostTransportReturnPc0, info.PostTransportReturnPc1, info.PostTransportReturnPc2, info.DirectTarget0, info.DirectTarget1);
    internal IntPtr Source(uint site) => site switch { 0 => Source0, 1 => Source1, 2 => Source2, _ => IntPtr.Zero };
    internal IntPtr Transport(uint site) => site switch { 0 => Transport0, 1 => Transport1, 2 => Transport2, _ => IntPtr.Zero };
    internal bool IsComplete => Slot < 32 && Epoch != 0 && ParentEpoch != 0 && PreEntry != IntPtr.Zero && PostEntry != IntPtr.Zero
        && PreEntry != PostEntry && Source0 != IntPtr.Zero && Source1 != IntPtr.Zero && Source2 != IntPtr.Zero
        && Source0 != Source1 && Source0 != Source2 && Source1 != Source2 && Transport0 != IntPtr.Zero
        && Transport1 != IntPtr.Zero && Transport2 != IntPtr.Zero && Transport0 != Transport1 && Transport0 != Transport2
        && Transport1 != Transport2 && Source0 != Transport0 && Source0 != Transport1 && Source0 != Transport2
        && Source1 != Transport0 && Source1 != Transport1 && Source1 != Transport2
        && Source2 != Transport0 && Source2 != Transport1 && Source2 != Transport2 && Direct0 != IntPtr.Zero && Direct1 != IntPtr.Zero;
}

/// <summary>Permanent observer adoption for a configured lane. This component
/// never installs instructions, configures a lane, or grants first-install Ready.</summary>
internal sealed class NativeListenerContextBridge
{
    [StructLayout(LayoutKind.Sequential)] private struct Observer
    { internal uint Version, Size; internal IntPtr Before, After, User; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(uint slot, ref NativeListenerInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AttachNative(uint slot, ulong epoch, ref Observer observer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nuint BeforeNative(IntPtr frame, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AfterNative(IntPtr frame, nuint token, IntPtr user);
    private sealed record Callbacks(Func<NativeListenerObservation, nuint> Before, Action<NativeListenerObservation, nuint> After,
        Action<string> Fault);
    private static readonly object Gate = new();
    private static readonly HashSet<(IntPtr Module, uint Slot)> Claimed = new();
    private static readonly List<NativeListenerContextBridge> Owners = new();
    private readonly ReadNative _read;
    private readonly AttachNative _attach;
    private readonly BeforeNative _before;
    private readonly AfterNative _after;
    private readonly ILogger _log;
    private Callbacks? _callbacks;
    private int _unknown, _attempted;
    internal NativeListenerAdoptionIdentity Identity { get; }

    internal static NativeListenerContextBridge AdoptConfigured(string absolutePath, NativeListenerAdoptionIdentity identity, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Native listener adoption requires Windows x64.");
        if (!Path.IsPathFullyQualified(absolutePath) || !identity.IsComplete)
            throw new ArgumentException("Listener adoption requires an existing absolute helper path and full native identity.");
        var path = Path.GetFullPath(absolutePath);
        var module = GetModuleHandle(path);
        if (module == IntPtr.Zero) throw new UnsupportedSignatureException("The listener helper must already be loaded; adoption does not load or install it.");
        lock (Gate)
        {
            if (Claimed.Contains((module, identity.Slot))) throw new InvalidOperationException("The listener lane already has a permanent managed owner.");
            var bridge = new NativeListenerContextBridge(module, identity, log);
            Owners.Add(bridge); Claimed.Add((module, identity.Slot)); return bridge;
        }
    }
    private NativeListenerContextBridge(IntPtr module, NativeListenerAdoptionIdentity identity, ILogger log)
    {
        if (Marshal.SizeOf<NativeListenerFrame>() != 176 || Marshal.SizeOf<Observer>() != 32
            || Marshal.SizeOf<NativeListenerInformation>() != 176 || Marshal.SizeOf<NativeEventMidFrame>() != 0x1b0)
            throw new UnsupportedSignatureException("Native listener version 1 layout changed.");
        Identity = identity; _log = log;
        _read = Marshal.GetDelegateForFunctionPointer<ReadNative>(NativeLibrary.GetExport(module, "RizListenerContext_GetInfo"));
        _attach = Marshal.GetDelegateForFunctionPointer<AttachNative>(NativeLibrary.GetExport(module, "RizListenerContext_Attach"));
        _before = Before; _after = After;
        var info = Information();
        if (!Matches(info, identity) || info.Attached != 0) throw new UnsupportedSignatureException("The listener lane does not match an unattached configured identity.");
    }
    internal void Attach(Func<NativeListenerObservation, nuint> before, Action<NativeListenerObservation, nuint> after, Action<string> fault)
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(after); ArgumentNullException.ThrowIfNull(fault);
        if (Interlocked.Exchange(ref _attempted, 1) != 0) throw new InvalidOperationException("Listener observer attachment is attempted once.");
        Volatile.Write(ref _callbacks, new(before, after, fault));
        try
        {
            var initial = Information();
            if (!Matches(initial, Identity) || initial.Attached != 0) throw new UnsupportedSignatureException("The listener lane changed before attachment.");
            var observer = new Observer { Version = 1, Size = 32, Before = Marshal.GetFunctionPointerForDelegate(_before), After = Marshal.GetFunctionPointerForDelegate(_after) };
            if (_attach(Identity.Slot, Identity.Epoch, ref observer) != 0) throw new UnsupportedSignatureException("Native listener observer attachment was rejected.");
            var attached = Information();
            if (!Matches(attached, Identity) || attached.Attached != 1) throw new UnsupportedSignatureException("Native listener attachment could not be confirmed.");
        }
        catch { Fault("Listener attachment is unconfirmed and remains process-owned."); throw; }
    }
    internal ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        try
        {
            var info = Information();
            if (!Matches(info, Identity) || info.InFlight > long.MaxValue) throw new UnsupportedSignatureException("Native listener responsibility became unknown.");
            return new((long)info.InFlight, Volatile.Read(ref _unknown) == 0);
        }
        catch { Fault("Listener activity could not be verified."); return new(0, false); }
    }
    private NativeListenerInformation Information()
    {
        var info = new NativeListenerInformation { Version = 1, Size = 176 };
        if (_read(Identity.Slot, ref info) != 0) throw new UnsupportedSignatureException("The native listener lane cannot be read.");
        return info;
    }
    internal static bool Matches(NativeListenerInformation info, NativeListenerAdoptionIdentity identity)
        => info.Version == 1 && info.Size == 176 && info.Attached <= 1 && info.Unknown == 0 && info.Reserved == 0
        && info.Faults == 0 && info.Overflow == 0 && info.MissingPost == 0 && NativeListenerAdoptionIdentity.From(info) == identity;
    internal static bool ValidFrame(NativeListenerFrame frame, NativeListenerAdoptionIdentity identity, bool before)
    {
        if (!identity.IsComplete || frame.Version != 1 || frame.Size != 176 || frame.Slot != identity.Slot || frame.Site > 2
            || frame.Epoch != identity.Epoch || frame.ParentSequence == 0 || frame.ChildSequence == 0 || frame.OsThreadId == 0
            || frame.OsThreadId != GetCurrentThreadId() || frame.Reserved0 != 0 || frame.Reserved1 != 0
            || frame.Event == IntPtr.Zero || frame.List == IntPtr.Zero || frame.Invokable == IntPtr.Zero || frame.SourceRsp == 0
            || frame.ArgRcx != frame.Invokable || (frame.SourceRsp & 15) != 0
            || frame.Site == 0 && frame.ActualTarget != identity.Direct0 || frame.Site == 1 && frame.ActualTarget != identity.Direct1
            || (frame.Flags & NativeListenerFlags.TargetReadFaulted) != 0 && frame.Site != 2
            || frame.SourceReturnPc != identity.Source(frame.Site) || frame.Index > int.MaxValue
            || ((uint)frame.Flags & ~15u) != 0) return false;
        if (before) return frame.Stage == NativeListenerStage.Before && frame.Flags == NativeListenerFlags.None
            && frame.ActualTarget != IntPtr.Zero && frame.MachineFrame != IntPtr.Zero
            && frame.TransportReturnPc == frame.SourceReturnPc;
        if (frame.Stage == NativeListenerStage.Returned) return (frame.Flags & ~NativeListenerFlags.BeforeFaulted) == 0
            && frame.ActualTarget != IntPtr.Zero && frame.MachineFrame != IntPtr.Zero
            && frame.TransportReturnPc == identity.Transport(frame.Site);
        return frame.Stage == NativeListenerStage.Abnormal && (frame.Flags & (NativeListenerFlags.ParentAbnormal | NativeListenerFlags.MissingPost)) != 0
            && (frame.MachineFrame == IntPtr.Zero && frame.TransportReturnPc == IntPtr.Zero
                || (frame.Flags & NativeListenerFlags.MissingPost) != 0 && frame.MachineFrame != IntPtr.Zero
                && frame.TransportReturnPc == identity.Transport(frame.Site));
    }
    private NativeListenerObservation Copy(IntPtr pointer, bool before)
    {
        HookTargetValidation.VerifyReadable(pointer, 176);
        var frame = Marshal.PtrToStructure<NativeListenerFrame>(pointer);
        if (!ValidFrame(frame, Identity, before)) throw new UnsupportedSignatureException("A listener callback has no valid configured native frame.");
        NativeEventMidFrame? machine = null;
        if (frame.MachineFrame != IntPtr.Zero)
        {
            HookTargetValidation.VerifyReadable(frame.MachineFrame, 0x1b0);
            machine = Marshal.PtrToStructure<NativeEventMidFrame>(frame.MachineFrame);
            if (!machine.Value.IsValid || machine.Value.ReturnPc != frame.TransportReturnPc || machine.Value.Rsp != frame.SourceRsp
                || machine.Value.Event != frame.Event || machine.Value.Executing != frame.List || machine.Value.Rbx != frame.Index
                || before && (machine.Value.Rcx != unchecked((ulong)frame.Invokable.ToInt64())
                    || machine.Value.Rdx != unchecked((ulong)frame.ArgRdx.ToInt64())
                    || machine.Value.R8 != unchecked((ulong)frame.ArgR8.ToInt64())
                    || machine.Value.R9 != unchecked((ulong)frame.ArgR9.ToInt64())))
                throw new UnsupportedSignatureException("A listener machine copy differs from its actual transport return.");
        }
        frame.MachineFrame = IntPtr.Zero; return new(frame, machine);
    }
    private nuint Before(IntPtr pointer, IntPtr user)
    {
        try
        {
            if (user != IntPtr.Zero || Volatile.Read(ref _unknown) != 0) return 0;
            return Volatile.Read(ref _callbacks)?.Before(Copy(pointer, true)) ?? 0;
        }
        catch (Exception error) { Fault("Listener Before failed.", error); return 0; }
    }
    private void After(IntPtr pointer, nuint token, IntPtr user)
    {
        try
        {
            if (user != IntPtr.Zero) throw new UnsupportedSignatureException("A listener callback has a foreign user identity.");
            // Cleanup must continue after a native/managed sticky Unknown.
            Volatile.Read(ref _callbacks)?.After(Copy(pointer, false), token);
        }
        catch (Exception error) { Fault("Listener After failed; responsibility remains unknown.", error); }
    }
    private void Fault(string reason, Exception? error = null)
    {
        if (Interlocked.Exchange(ref _unknown, 1) != 0) return;
        try { Volatile.Read(ref _callbacks)?.Fault(reason); } catch { }
        try { _log.Log(LogLevel.Error, reason, error); } catch { }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string path);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
