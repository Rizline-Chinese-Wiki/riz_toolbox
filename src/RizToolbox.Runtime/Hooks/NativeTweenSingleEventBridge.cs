using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

/// <summary>One process-lifetime managed observer for the already published Single lane.
/// It has no Configure, entry installer, caller receipt or caller-created Ready path.</summary>
internal sealed partial class NativeTweenSingleEventBridge
{
    [StructLayout(LayoutKind.Sequential)] private struct Observer
    { internal uint Version, Size; internal IntPtr Notify, User; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(ref NativeTweenSingleEventInformation info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AttachNative(ulong epoch, ref Observer observer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint CurrentNative(IntPtr actual, ref NativeTweenSingleEventFrame copy, ref NativeEventMidFrame machine);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NotifyNative(IntPtr actual, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EntryNative();
    private static readonly object Gate = new(), Issuer = new();
    private static NativeTweenSingleEventBridge? _owner;
    private static readonly List<NativeTweenSingleEventBridge> RetainedAttempts = new();
    private static bool _attempted;
    [ThreadStatic] private static Delivery? _current;
    private readonly NativeFirstInstall? _producer;
    private readonly Action _verifyPublication;
    private readonly ReadNative _read;
    private readonly CurrentNative _readCurrent;
    private readonly NotifyNative _notify;
    private readonly NativeTweenSingleEventSubscriptions<Delivery> _subscriptions = new();
    private int _unknown;
    internal NativeTweenSingleEventIdentity Identity { get; }
    internal bool Production => _producer is not null;

    internal void RequireProduction()
    {
        if (_producer is null) throw new InvalidOperationException("A controlled Single transport cannot supply production publication authority.");
        _producer.VerifyReady();
    }

    internal static NativeTweenSingleEventBridge AdoptReady()
    {
        var producer = NativeFirstInstall.RequireReady(); producer.VerifyReady();
        lock (Gate)
        {
            if (_owner is null)
            {
                if (_attempted) throw new UnsupportedSignatureException("An earlier Single observer adoption is unconfirmed and remains permanently retained.");
                _attempted = true; _owner = new(producer);
            }
            if (!ReferenceEquals(_owner._producer, producer) || _owner.Identity != NativeTweenSingleEventIdentity.From(producer.SingleEventPublication))
                throw new UnsupportedSignatureException("The Single observer cannot adopt a different private publication owner.");
            return _owner;
        }
    }
    private NativeTweenSingleEventBridge(NativeFirstInstall producer)
        : this(producer, producer.HelperModule, NativeTweenSingleEventIdentity.From(producer.SingleEventPublication), producer.VerifyReady) { }

    private NativeTweenSingleEventBridge(NativeFirstInstall? producer, IntPtr helper,
        NativeTweenSingleEventIdentity identity, Action verifyPublication)
    {
        _producer = producer; Identity = identity; _verifyPublication = verifyPublication;
        _verifyPublication();
        if (!Identity.IsComplete || Marshal.SizeOf<NativeTweenSingleEventFrame>() != 200 || Marshal.SizeOf<Observer>() != 24
            || Marshal.SizeOf<NativeTweenSingleEventInformation>() != 144 || Marshal.SizeOf<NativeEventMidFrame>() != 432)
            throw new UnsupportedSignatureException("Single event transport layout differs from the native V1 contract.");
        T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(helper, "RizTweenSingleEventContext_" + name));
        _read = Export<ReadNative>("GetInfo"); _readCurrent = Export<CurrentNative>("ReadCurrent"); _notify = Notify;
        if (Export<EntryNative>("GetMidEntry")() != Identity.MidEntry || Export<EntryNative>("GetPreEntry")() != Identity.PreEntry
            || Export<EntryNative>("GetPostEntry")() != Identity.PostEntry || Information().Attached != 0)
            throw new UnsupportedSignatureException("Single producer entry identities differ or another observer already owns it.");
        var observer = new Observer { Version = 1, Size = 24, Notify = Marshal.GetFunctionPointerForDelegate(_notify) };
        RetainedAttempts.Add(this); // Own the unmanaged callback before the first possibly-published native Attach.
        if (Export<AttachNative>("Attach")(Identity.Epoch, ref observer) != 0 || Information().Attached != 1)
        { _unknown = 1; throw new UnsupportedSignatureException("Single observer Attach remains unconfirmed and process-owned."); }
        _verifyPublication();
    }
    internal NativeTweenSingleEventSubscriptions<Delivery>.Subscription Subscribe(Action<Delivery> observe, Action<string> fault)
    {
        ArgumentNullException.ThrowIfNull(observe); ArgumentNullException.ThrowIfNull(fault);
        _verifyPublication();
        if (!ReadActivity().Known) throw new UnsupportedSignatureException("The Single producer retains unknown responsibility.");
        return _subscriptions.Subscribe(observe, fault);
    }
    internal NativeTweenSingleEventSubscriptions<Delivery>.ParentLoan BorrowParent(
        NativeTweenSingleEventSubscriptions<Delivery>.Subscription subscription, ExactHookBroker.SingleCaptureAuthority physical)
    {
        physical.VerifyCurrent();
        if (physical.Key.Epoch != Identity.Epoch || physical.Key.ParentEpoch != Identity.ParentEpoch)
            throw new UnsupportedSignatureException("Single subscription capture belongs to another native producer generation.");
        var loan = _subscriptions.Begin(subscription, physical.Key);
        // Reserve first: even reentry during publication verification or the consumer's Capture
        // must see pending responsibility before it can withdraw/free this run's roots.
        try { _verifyPublication(); physical.VerifyCurrent(); return loan; }
        catch { loan.CaptureFault("Single subscriber capture lost its actual private publication window."); throw; }
    }
    private NativeTweenSingleEventInformation Information()
    {
        var value = new NativeTweenSingleEventInformation { Version = 1, Size = 144 };
        if (_read(ref value) != 0 || !InformationShape(value, Identity))
            throw new UnsupportedSignatureException("The native Single producer information does not match its actual publication.");
        return value;
    }
    internal static bool InformationShape(NativeTweenSingleEventInformation value, NativeTweenSingleEventIdentity identity)
        => identity.IsComplete && value.Version == 1 && value.Size == 144 && value.Configured == 1 && value.Attached <= 1 && value.Unknown <= 1
            && value.Epoch == identity.Epoch && value.ParentEpoch == identity.ParentEpoch && value.ParentCapacity == 128
            && value.ParentsEntered >= value.ParentsExited && value.ParentsEntered - value.ParentsExited == value.ParentsInFlight
            && value.ChildrenEntered >= value.ChildrenExited && value.ChildrenEntered - value.ChildrenExited == value.ChildrenInFlight
            && value.Mids <= value.ParentsEntered && value.RetainedParents <= value.ParentsExited && value.RetainedChildren <= value.ChildrenExited;
    internal ExactHookBroker.ManagedBoundaryActivity ReadActivity() => ReadActivity(_verifyPublication);

    private ExactHookBroker.ManagedBoundaryActivity ReadActivity(Action verify)
    {
        var stage = "producer verification";
        try
        {
            verify();
            stage = "native information read";
            var info = Information();
            stage = "activity validation";
            if (info.Unknown != 0 || info.CallbackFaults != 0 || info.MissingPosts != 0 || info.RetainedParents != 0
                || info.RetainedChildren != 0 || info.Overflow != 0 || info.ParentsInFlight > long.MaxValue || info.ChildrenInFlight > long.MaxValue)
                throw new UnsupportedSignatureException("The Single native producer retains unknown or missing-post responsibility.");
            return new(checked((long)Math.Max(info.ParentsInFlight, info.ChildrenInFlight)), Volatile.Read(ref _unknown) == 0);
        }
        catch (Exception error)
        {
            Fault($"Single producer activity is unknown during {stage}.", error);
            return new(0, false);
        }
    }
    private NativeTweenSingleEventObservation CopyCurrent(IntPtr actual)
    {
        var copy = new NativeTweenSingleEventFrame { Version = 1, Size = 200 };
        var machine = new NativeEventMidFrame { Version = 1, Size = 432 };
        if (_readCurrent(actual, ref copy, ref machine) != 0)
            throw new UnsupportedSignatureException("Single ReadCurrent rejected a noncurrent/stale native delivery address.");
        var hasMachine = copy.MachineFrame != IntPtr.Zero; copy.MachineFrame = IntPtr.Zero;
        var observation = new NativeTweenSingleEventObservation(copy, hasMachine ? machine : null);
        if (!NativeTweenSingleEventValidation.Valid(observation, Identity, GetCurrentThreadId()))
            throw new UnsupportedSignatureException("The native Single frame/machine copy differs from the exact parent/site transport.");
        return observation;
    }
    private uint Notify(IntPtr actual, IntPtr user)
    {
        Delivery? value = null;
        try
        {
            if (user != IntPtr.Zero) throw new UnsupportedSignatureException("A Single callback has a foreign user pointer.");
            var observation = CopyCurrent(actual);
            if (observation.Frame.Stage is NativeTweenSingleEventStage.Mid or NativeTweenSingleEventStage.Before) _verifyPublication();
            value = new(Issuer, this, actual, observation, _current); _current = value;
            return _subscriptions.Dispatch(NativeTweenSingleParentKey.From(observation.Frame), value);
        }
        catch (Exception error) { Fault("Single synchronous observation: " + error.Message); return 1; }
        finally { if (value is not null) { value.Closed = true; _current = value.Previous; } }
    }
    private void Fault(string detail, Exception? error = null)
    {
        if (Interlocked.Exchange(ref _unknown, 1) != 0) return;
        if (error is not null)
        {
            try { detail += Environment.NewLine + error; }
            catch { detail += " " + error.GetType().FullName; }
        }
        _subscriptions.FaultAll(detail);
    }
    internal sealed class Delivery
    {
        private readonly NativeTweenSingleEventBridge _bridge;
        private readonly IntPtr _actual;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        internal readonly Delivery? Previous;
        internal readonly NativeTweenSingleEventObservation Observation;
        internal bool Closed;
        internal Delivery(object issuer, NativeTweenSingleEventBridge bridge, IntPtr actual,
            NativeTweenSingleEventObservation observation, Delivery? previous)
        {
            if (!ReferenceEquals(issuer, Issuer)) throw new InvalidOperationException("A copied Single DTO cannot issue native source authority.");
            (_bridge, _actual, Observation, Previous) = (bridge, actual, observation, previous);
        }
        internal void VerifyCurrent()
        {
            if (Closed || _thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_current, this))
                throw new InvalidOperationException("The Single delivery loan has left its actual synchronous native callback.");
            var reread = _bridge.CopyCurrent(_actual);
            if (Observation.Frame.Stage is NativeTweenSingleEventStage.Mid or NativeTweenSingleEventStage.Before) _bridge._verifyPublication();
            if (!reread.Frame.Equals(Observation.Frame)) throw new UnsupportedSignatureException("The actual Single delivery changed during capture.");
        }
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
