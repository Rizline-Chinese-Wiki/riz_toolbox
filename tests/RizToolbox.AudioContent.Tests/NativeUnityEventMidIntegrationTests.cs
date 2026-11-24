using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

/// <summary>Actual fixture DLL -> Dobby -> native void helper -> native original
/// mid CALL -> CLR bridge -> broker physical identity -> Adapter ledger. Object
/// graph/root adoption is deliberately a controlled backend, not IL2CPP acceptance.</summary>
internal static class NativeUnityEventMidIntegrationTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Native UnityEvent joint parent and mid preserve actual caller token empty lists and foreign recursion", NormalAndRecursion),
        ("Native UnityEvent joint worker tail window and logical detach retain parent physical responsibility", WorkerAndDetach),
        ("Native UnityEvent joint adoption faults and native SEH Cpp originals preserve exact continuation and cleanup", AdoptionAndNativeFaults),
        ("Native UnityEvent joint next-generation reused context refuses a stale physical identity before adoption", StaleIdentity),
        ("Native UnityEvent joint clean adopted inline scope survives original SEH with paired cleanup and retained roots", () => ColdOriginalFault(1)),
        ("Native UnityEvent joint clean adopted inline scope survives original Cpp with paired cleanup and retained roots", () => ColdOriginalFault(2))
    };
    private static readonly List<Fixture> Owners = new();
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static Fixture Own() { var value = new Fixture(); Owners.Add(value); return value; }
    private static void NormalAndRecursion()
    {
        var f = Own(); f.List(2); var initial = f.Owner.Read().EventSnapshots.Single().Initial;
        f.Call(); f.NoCallbackFailure();
        var first = f.Owner.Read().EventSnapshots.Single();
        Check(first.Returned && first.Inline is not null && first.Inline.Executing.Rows.Count == 2, "Actual native mid failed to publish its nonempty same-generation list.");
        Check(first.InvokeCaller == f.ExpectedCaller.ToInt64() && first.InvokeMethod == f.Target.MethodInfo.ToInt64()
            && first.Inline!.MidReturnPc == f.ExpectedMid.ToInt64() && first.Inline.ParentCaller == f.ExpectedCaller.ToInt64(), "Native caller/MethodInfo/mid return did not survive the real joint path.");
        var firstToken = first.Inline!.NativeToken; f.List(0); f.Call(); f.NoCallbackFailure();
        var empty = f.Owner.Read().EventSnapshots.Single();
        Check(empty.Inline is not null && empty.Inline.Executing.Rows.Count == 0 && empty.Inline.NativeToken != firstToken
            && first.Inline.Executing.Rows.Count == 2 && initial.ExecutingCalls.Rows.Count == 2, "Empty native list or immutable previous generation was lost.");
        f.List(1); f.Control(nested: true); f.Call(); f.NoCallbackFailure();
        f.Control(nested: true, foreignMethod: true); f.Call(); f.NoCallbackFailure(); f.Control();
        var state = f.Owner.Read(); var native = f.Read();
        Check(native.Entered == 6 && native.Exited == 6 && native.Returned == 6 && f.MidCallbacks == 6 && f.SkippedForeign == 2
            && f.Backend.InlineAdoptions == 4 && f.Backend.Captures == 5 && f.Backend.SuccessfulAfter == 5
            && f.ProbeEntered == 6 && f.ProbeExited == 6 && f.ScopesEnded == 5,
            "Joint recursion duplicated an original or lost exact per-frame probes/scopes.");
        Check(f.LastIdentity is not null && !f.LastIdentity.IsCurrentFor(f.LastContext!) && f.Parent.ReadActivity() is { Known: true, InFlight: 0 }
            && f.Mid.ReadActivity() is { Known: true, InFlight: 0 } && f.Owner.TryConfirmNativeIdle(), "Joint normal physical scopes did not drain.");
        Check(!state.Unknown && state.Pending && state.InlineExecutionUnobserved && !state.DispatchPathVerified && !state.MutationClosed,
            "A fixture inline snapshot claimed full game dispatch or retirement.");
    }
    private static void WorkerAndDetach()
    {
        var f = Own(); var reads = f.Backend.GraphReads; f.Control(block: true);
        var worker = Task.Run(() => f.Call(foreign: true, worker: true));
        try
        {
            Check(SpinWait.SpinUntil(() => f.Read().Entered > f.Read().Exited, 5000), "Native worker did not reach its post-mid blocked original.");
            Check(f.Mid.ReadActivity() is { Known: true, InFlight: 0 } && f.Parent.ReadActivity() is { Known: true, InFlight: 1 }
                && f.Binding.ReadActivity() is { Known: true, InFlight: 1 } && f.Probe.ReadActivity() is { Known: true, InFlight: 1 }
                && !f.Owner.TryConfirmNativeIdle(), "Mid drain concealed the still-running native parent tail.");
            Check(f.WorkerCallbacks == 1 && f.WorkerCurrentRejected == 1 && f.Backend.GraphReads == reads && f.Backend.InlineAdoptions == 0,
                "Worker mid callback read a native object or acquired a main-thread parent context.");
        }
        finally { f.Resume(); Check(worker.Wait(5000), "Native worker did not join."); }
        f.AssertResult(worker.Result, 0); f.NoCallbackFailure(); f.Control();
        Check(!f.Owner.Read().Unknown && f.ProbeEntered == 1 && f.ProbeExited == 1, "Foreign worker contaminated the owned event or lost probe pairing.");
        f.AfterAdoption = () => {
            Check(f.Parent.ReadActivity().InFlight == 1 && f.Mid.ReadActivity().InFlight == 1, "Main native mid escaped the parent physical window.");
            f.Binding.Dispose(); f.Probe.Dispose();
            Check(f.Parent.ReadActivity().InFlight == 1 && f.Binding.ReadActivity().InFlight == 1 && f.Probe.ReadActivity().InFlight == 1,
                "Logical detach discarded native parent responsibility during actual callback.");
        };
        f.Call(); f.NoCallbackFailure();
        Check(f.Backend.InlineAdoptions == 1 && f.ScopesEnded == 1 && f.ProbeEntered == 2 && f.ProbeExited == 2
            && f.Owner.Read().Unknown && f.Owner.Read().Pending && f.Parent.ReadActivity().InFlight == 0,
            "Detached captured scope was reused, leaked its physical exit, or falsely completed its owner.");
        f.AfterAdoption = null; f.Call(); f.NoCallbackFailure();
        Check(f.Backend.InlineAdoptions == 1 && f.ProbeEntered == 2 && f.ProbeExited == 2 && f.Read().Entered == 3 && f.Read().Returned == 3,
            "Detached physical forwarding stopped or stale observations were delivered again.");
    }
    private static void AdoptionAndNativeFaults()
    {
        var f = Own(); f.Backend.FailAdoption = true;
        f.Call(); f.NoCallbackFailure();
        Check(f.Backend.InlineAdoptions == 1 && f.Backend.RetainedLists == 1 && f.Owner.Read().Unknown
            && f.Owner.Read().EventSnapshots.Single().Inline is null && f.Owner.Read().Pending && f.ScopesEnded == 1,
            "Actual callback adoption failure dropped controlled root responsibility or published an inline snapshot.");
        foreach (var mode in new uint[] { 1, 2 })
        {
            f.Control(mode: mode); var before = f.Read(); var result = f.Call(); f.AssertResult(result, mode);
            var after = f.Read();
            Check(after.Entered == before.Entered + 1 && after.Exited == after.Entered && after.Returned == after.Entered,
                "Native original SEH/C++ failure changed continuation count or escaped native catch.");
        }
        f.NoCallbackFailure(); f.Control();
        Check(f.Read().Entered == 3 && f.Read().Exited == 3 && f.Read().Returned == 3 && f.MidCallbacks == 3
            && f.Backend.InlineAdoptions == 1 && f.ProbeEntered == 3 && f.ProbeExited == 3 && f.AbnormalProbeExits == 2
            && f.Parent.ReadActivity() is { Known: true, InFlight: 0 } && f.Binding.ReadActivity() is { Known: false, InFlight: 0 },
            "Joint fault cleanup lost original-once, abnormal exit, native drain or sticky unknown authority.");
    }
    private static void StaleIdentity()
    {
        var f = Own(); f.SuppressAdoption = true;
        f.Call(); f.NoCallbackFailure(); var old = f.LastIdentity; var oldContext = f.LastContext;
        Check(old is not null && oldContext is not null && !old.IsCurrentFor(oldContext), "Exited native parent left a valid identity.");
        f.BeforeAdoption = (context, frame, actual) => {
            Check(ReferenceEquals(context, oldContext) && actual.Token != old!.Token && actual.Frame.Equals(old.Frame)
                && actual.IsCurrentFor(context) && !old.IsCurrentFor(context), "Joint native next generation did not reuse scope safely.");
            try { f.Owner.ObserveInlineList(context, frame, old!); throw new InvalidOperationException("Stale joint certificate was accepted."); }
            catch (InvalidOperationException error) when (error.Message.Contains("exact current", StringComparison.Ordinal)) { f.StaleRejected++; }
        };
        f.Call(); f.NoCallbackFailure();
        Check(f.StaleRejected == 1 && f.Backend.InlineAdoptions == 0 && f.Owner.Read().Unknown && f.Owner.Read().ReturnedInvocations == 1
            && f.Read().Entered == 2 && f.Read().Exited == 2 && f.Read().Returned == 2 && f.ProbeEntered == 2 && f.ProbeExited == 2 && f.ScopesEnded == 2,
            "Stale joint capability adopted a new native list or interrupted original forwarding/cleanup.");
    }
    private static void ColdOriginalFault(uint mode)
    {
        var f = Own(); Check(!f.Owner.Read().Unknown && f.Backend.InlineAdoptions == 0, "Original-fault case did not begin with a clean owner.");
        f.Control(mode: mode); var result = f.Call(); f.AssertResult(result, mode); f.NoCallbackFailure();
        var state = f.Owner.Read(); var row = state.EventSnapshots.Single(); var native = f.Read();
        Check(row.Inline is not null && row.Inline.Executing.Rows.Count == 2 && row.Inline.NativeToken > 0
            && row.Inline.MidReturnPc == f.ExpectedMid.ToInt64() && row.Inline.ParentCaller == f.ExpectedCaller.ToInt64()
            && f.Backend.InlineAdoptions == 1 && f.Backend.RetainedLists == 1 && f.Backend.RootRecords == 2 && !f.Backend.CleanupUnknown,
            "Clean original exception lost the inline snapshot or its adopted root responsibility.");
        Check(native.Entered == 1 && native.Exited == 1 && native.Returned == 1 && f.MidCallbacks == 1 && f.Backend.Captures == 1
            && f.Backend.SuccessfulAfter == 0 && f.ScopesEnded == 1 && f.PhysicalContexts.Count == 0
            && f.ProbeEntered == 1 && f.ProbeExited == 1 && f.AbnormalProbeExits == 1,
            "Clean native original exception duplicated continuation, manufactured After, or lost scope/probe cleanup.");
        Check(state.Unknown && state.Pending && state.ReturnedInvocations == 0 && !row.Returned && row.Post is null
            && state.InlineExecutionUnobserved && !state.DispatchPathVerified && !state.MutationClosed
            && f.LastContext is { _active: false } && f.LastIdentity is not null && !f.LastIdentity.IsCurrentFor(f.LastContext),
            "Abnormal clean parent claimed success, completed dispatch, or retained a live physical capability.");
        Check(f.Parent.ReadActivity() is { Known: true, InFlight: 0 } && f.Mid.ReadActivity() is { Known: true, InFlight: 0 }
            && f.Binding.ReadActivity() is { Known: false, InFlight: 0 } && f.Probe.ReadActivity() is { Known: false, InFlight: 0 },
            "Clean abnormal native parent failed to drain or cleared sticky observation uncertainty.");
    }

    [StructLayout(LayoutKind.Sequential)] private struct Result
    { internal uint Size, Status, ExceptionKind, ExceptionCode; internal ulong Payload; internal IntPtr Reference; internal uint Thread, OriginalKind; }
    [StructLayout(LayoutKind.Sequential)] private struct Observation
    { internal uint Size, Kind; internal ulong Entered, Exited; internal IntPtr Self, Argument, Method; internal ulong Returned; internal uint Scalar, Thread; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AddressNative(uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint MidConfigureNative(uint kind, IntPtr helperEntry, IntPtr list);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ControlNative(uint kind, uint mode, uint block, IntPtr nested, IntPtr nestedSelf, IntPtr nestedMethod);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ReadNative(uint kind, ref Observation result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint InvokeNative(uint kind, IntPtr entry, IntPtr self, IntPtr method, ref Result result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ResumeNative(uint kind);

    private sealed class Fixture
    {
        private const uint Kind = 4;
        private readonly IntPtr _library, _items;
        private readonly int _imageSize;
        private readonly (int Begin, int End)[] _executableSections;
        private readonly ControlNative _control;
        private readonly ReadNative _read;
        private readonly InvokeNative _invoke, _worker;
        private readonly ResumeNative _resume;
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Authority Authority = new();
        internal readonly IntPtr Self = Marshal.AllocHGlobal(0x40), Foreign = Marshal.AllocHGlobal(0x40), ActualList = Marshal.AllocHGlobal(0x40);
        internal readonly IntPtr ForeignMethod = new(unchecked((long)0x3456789ACDEF0140UL));
        internal readonly IntPtr ExpectedCaller, ExpectedMid;
        internal readonly VerifiedHookTarget Target;
        internal readonly NativeVoidContextBridge Parent;
        internal readonly NativeEventMidContextBridge Mid;
        internal readonly ExactHookBroker Broker;
        internal readonly GraphBackend Backend;
        internal readonly NativeLocalUnityEventLedger Owner;
        internal ExactHookBroker.Binding Binding = null!;
        internal ExactHookBroker.BoundaryProbeBinding Probe = null!;
        internal readonly Stack<NativeLocalUnityEventLedger.InvokeContext?> PhysicalContexts = new();
        internal NativeLocalUnityEventLedger.InvokeContext? LastContext;
        internal ExactHookBroker.NativeVoidPhysicalIdentity? LastIdentity;
        internal Action? AfterAdoption;
        internal Action<NativeLocalUnityEventLedger.InvokeContext, NativeEventMidFrame, ExactHookBroker.NativeVoidPhysicalIdentity>? BeforeAdoption;
        internal bool SuppressAdoption;
        internal int MidCallbacks, WorkerCallbacks, WorkerCurrentRejected, SkippedForeign, ProbeEntered, ProbeExited, AbnormalProbeExits, ScopesEnded, StaleRejected;
        private Exception? _callbackFailure;
        private int _listVersion;
        private uint _mode;
        internal Fixture()
        {
            string Required(string key) { var path = Environment.GetEnvironmentVariable(key); if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
                throw new InvalidOperationException("Required actual native integration fixture is missing: " + key); return path; }
            var fixturePath = Required("RIZ_INITIALIZATION_ABI_FIXTURE"); var helperPath = Required("RIZ_INITIALIZATION_CONTEXT_HELPER"); var dobby = Required("RIZ_INITIALIZATION_ABI_DOBBY");
            _library = NativeLibrary.Load(fixturePath);
            using (var stream = File.OpenRead(fixturePath)) using (var pe = new PEReader(stream))
            {
                _imageSize = pe.PEHeaders.PEHeader!.SizeOfImage;
                _executableSections = pe.PEHeaders.SectionHeaders.Where(section => (section.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0)
                    .Select(section => (section.VirtualAddress, checked(section.VirtualAddress + Math.Max(section.VirtualSize, section.SizeOfRawData)))).ToArray();
            }
            T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
            Target = new("native.unityevent.joint.fixture", Export<AddressNative>("RtVoidTarget")(Kind), new(unchecked((long)0x3456789ACDEF0120UL)), new(0x4321), ExactHookAbi.NativeVoid0);
            ExpectedCaller = Export<AddressNative>("RtVoidExpectedCaller")(Kind); ExpectedMid = Export<AddressNative>("RtEventMidExpectedReturn")(Kind);
            Check(InImage(Target.Address) && InImage(ExpectedCaller) && InImage(ExpectedMid) && ExpectedMid.ToInt64() - Target.Address.ToInt64() == 0x140,
                "Joint fixture did not supply real executable parent/caller/mid addresses at the required fixed delta.");
            _control = Export<ControlNative>("RtVoidControl"); _read = Export<ReadNative>("RtVoidRead"); _invoke = Export<InvokeNative>("RtVoidInvoke");
            _worker = Export<InvokeNative>("RtVoidInvokeWorker"); _resume = Export<ResumeNative>("RtVoidResume");
            _items = Marshal.AllocHGlobal(8 * 4); for (var index = 0; index < 4; ++index) Marshal.WriteIntPtr(_items, index * 8, Marshal.AllocHGlobal(0x20));
            for (var offset = 0; offset < 0x40; offset += 8) { Marshal.WriteInt64(Self, offset, 0); Marshal.WriteInt64(Foreign, offset, 0); Marshal.WriteInt64(ActualList, offset, 0); }
            Marshal.WriteIntPtr(ActualList, 0x10, _items); List(2);
            var log = new IntegrationLogger(); Parent = new(helperPath, log); Mid = new(helperPath, log); Broker = new(dobby, Main, log);
            var prefix = Bytes(Target.Address, 32); var site = IntPtr.Add(Target.Address, 0x13B);
            Check(prefix.All(value => value == 0x90) && Bytes(site, 5).SequenceEqual(new byte[] { 0x0F, 0x1F, 0x44, 0, 0 }), "Joint fixture prefix or actual original NOP site differs.");
            Mid.Configure(ExpectedMid, ObserveMid);
            Check(Export<MidConfigureNative>("RtEventMidConfigure")(Kind, Mid.EntryAddress, ActualList) == 1, "Joint fixture-only mid configuration failed.");
            var configuredCall = Bytes(site, 5);
            var relay = IntPtr.Add(ExpectedMid, BitConverter.ToInt32(configuredCall, 1));
            Check(configuredCall[0] == 0xE8 && Bytes(Target.Address, 32).SequenceEqual(prefix)
                && Bytes(relay, 6).SequenceEqual(new byte[] { 0xFF, 0x25, 0, 0, 0, 0 }) && Marshal.ReadIntPtr(relay, 6) == Mid.EntryAddress,
                "Fixture mid publication failed, changed the parent prefix, or lost its actual full helper relay target.");
            Backend = new(this);
            var module = Target.Address.ToInt64() - 0x15642E0;
            Check(module > 0 && module + 0x1564420 == ExpectedMid.ToInt64(), "Controlled ledger anchor changed the fixed native entry-to-mid relation.");
            Owner = NativeLocalUnityEventLedger.CaptureBackend(Main, Backend, Authority, module); Authority.Before = false;
            Check(!Bytes(Target.Address, 32).SequenceEqual(prefix) && Bytes(site, 5).SequenceEqual(configuredCall), "Dobby parent patch did not publish or overwrote the actual mid CALL.");
            Control();
        }
        private bool InImage(IntPtr value)
        {
            var rva = value.ToInt64() - _library.ToInt64();
            return rva >= 0 && rva < _imageSize && _executableSections.Any(section => rva >= section.Begin && rva < section.End);
        }
        private static byte[] Bytes(IntPtr pointer, int count) { var value = new byte[count]; Marshal.Copy(pointer, value, 0, count); return value; }
        internal void List(int count) { Main.VerifyAccess(); Marshal.WriteInt32(ActualList, 0x18, count); Marshal.WriteInt32(ActualList, 0x1C, ++_listVersion); }
        internal void Control(uint mode = 0, bool block = false, bool nested = false, bool foreignMethod = false)
        {
            Check(_control(Kind, mode, block ? 1u : 0u, nested ? Target.Address : IntPtr.Zero, nested ? Foreign : IntPtr.Zero,
                nested ? foreignMethod ? ForeignMethod : Target.MethodInfo : IntPtr.Zero) == 1, "Native joint control failed.");
            _mode = mode;
        }
        internal Observation Read() { var value = new Observation { Size = 64 }; Check(_read(Kind, ref value) == 1, "Native joint observation read failed."); return value; }
        internal Result Call(bool foreign = false, bool worker = false)
        {
            var value = new Result { Size = 40 }; var status = (worker ? _worker : _invoke)(Kind, Target.Address, foreign ? Foreign : Self, Target.MethodInfo, ref value);
            Check(status == value.Status && status is 1 or 2, "Actual native parent caller/catch failed."); AssertResult(value, _mode); return value;
        }
        internal void Resume() => _resume(Kind);
        internal void AssertResult(Result value, uint mode)
        {
            Check(value.Status == (mode == 0 ? 1u : 2u), "Native original status changed across joint observers.");
            if (mode == 0) Check(value.ExceptionKind == 0 && value.ExceptionCode == 0 && value.Reference == IntPtr.Zero, "Normal void original manufactured a result or exception.");
            else Check(value.ExceptionKind == mode && value.ExceptionCode == (mode == 1 ? 0xE04F494Eu : 0xE06D7363u)
                && value.Payload == (0xBADC0FFEE0DDF00DUL ^ Kind) && value.OriginalKind == 0x5600 + Kind, "Native SEH/C++ code/kind/payload changed.");
        }
        internal void NoCallbackFailure() { if (_callbackFailure is { } error) throw new InvalidOperationException("Actual native mid observation assertion failed.", error); }
        private void ObserveMid(NativeEventMidFrame frame)
        {
            try
            {
                Interlocked.Increment(ref MidCallbacks);
                Check(frame.IsValid && frame.ReturnPc == ExpectedMid && frame.Executing == ActualList && (frame.EntryRsp & 15) == 8
                    && Parent.ReadActivity() is { Known: true, InFlight: > 0 } && Mid.ReadActivity() is { Known: true, InFlight: > 0 },
                    "Actual native mid frame/caller or joint physical window differs.");
                if (!Main.IsMainThread)
                {
                    Interlocked.Increment(ref WorkerCallbacks);
                    if (!Binding.TryGetCurrentNativeVoidContext<NativeLocalUnityEventLedger.InvokeContext>(frame.Event, out _, out _)) Interlocked.Increment(ref WorkerCurrentRejected);
                    else throw new InvalidOperationException("Native worker acquired a main-thread current context.");
                    return;
                }
                if (!Binding.TryGetCurrentNativeVoidContext<NativeLocalUnityEventLedger.InvokeContext>(frame.Event, out var context, out var identity)
                    || context!._record is null) { SkippedForeign++; return; }
                Check(identity is not null && identity.IsCurrentFor(context) && identity.Entry == Target && identity.Frame.Self == frame.Event
                    && identity.Frame.Caller == ExpectedCaller && identity.Frame.Method == Target.MethodInfo, "Actual mid did not acquire this exact native parent's private identity.");
                LastIdentity = identity; LastContext = context;
                BeforeAdoption?.Invoke(context, frame, identity!);
                if (!SuppressAdoption)
                {
                    try { Owner.ObserveInlineList(context, frame, identity!); }
                    catch (InvalidOperationException) when (Backend.FailAdoption) { }
                    AfterAdoption?.Invoke();
                }
            }
            catch (Exception error) { Interlocked.CompareExchange(ref _callbackFailure, error, null); throw; }
        }
    }
    private sealed class Authority : ICutoutListenerAuthority
    {
        internal bool Before = true;
        public bool Unknown => false;
        public void VerifyBeforeCapture() { if (!Before) throw new InvalidOperationException("Controlled joint source capture window ended."); }
        public void VerifyObservation() { if (Before) throw new InvalidOperationException("Controlled joint source has not activated."); }
        public bool TryConfirmSourceIdle() => true;
        public void ObserveFaultFromAnyThread() { }
    }
    private sealed class GraphBackend : ICutoutListenerBackend
    {
        private readonly Fixture _f;
        internal int GraphReads, InlineAdoptions, RetainedLists, Captures, SuccessfulAfter;
        internal bool FailAdoption;
        internal GraphBackend(Fixture fixture) => _f = fixture;
        public bool CleanupUnknown { get; private set; }
        public int RootRecords => 1 + RetainedLists;
        public void Initialize(NativeLocalUnityEventLedger owner)
        {
            owner.RegisterEvent(_f.Self.ToInt64());
            _f.Binding = _f.Broker.BindNativeVoidObservation(_f.Target, () => _f.Parent,
                frame => {
                    if (!owner.ShouldObserveInvoke(frame.Self.ToInt64())) return null;
                    var context = owner.CaptureNativeInvoke(frame); Captures++;
                    Check(_f.PhysicalContexts.Count > 0, "Captured native joint scope has no current physical probe.");
                    _f.PhysicalContexts.Pop(); _f.PhysicalContexts.Push(context); return context;
                }, (context, frame) => { owner.CompleteNativeInvoke(context, frame); SuccessfulAfter++; }, owner.ObserveInvokeFault);
            _f.Probe = _f.Broker.BindBoundaryProbe(_f.Target,
                (self, _, method) => {
                    Interlocked.Increment(ref _f.ProbeEntered); if (_f.Main.IsMainThread) _f.PhysicalContexts.Push(null);
                    owner.ProbeEnter(self.ToInt64(), method == IntPtr.Zero || method == _f.Target.MethodInfo);
                },
                (self, _, _, returned) => {
                    Interlocked.Increment(ref _f.ProbeExited); if (!returned) Interlocked.Increment(ref _f.AbnormalProbeExits);
                    if (_f.Main.IsMainThread)
                    {
                        Check(_f.PhysicalContexts.Count > 0, "Native joint probe lost its own entry snapshot.");
                        if (_f.PhysicalContexts.Pop() is { } context)
                        { Check(!context._active, "Native joint scope did not Dispose before its own physical probe exit."); _f.ScopesEnded++; }
                    }
                    owner.ProbeExit(self.ToInt64(), returned);
                });
        }
        public NativeUnityEventGraph ReadGraph(int eventSlot)
        {
            _f.Main.VerifyAccess(); Check(eventSlot == 0, "Joint graph adopted an unknown source slot."); GraphReads++;
            var list = ReadList();
            return new(_f.Self.ToInt64(), 201, 202, false, false,
                new(301, 1, Array.Empty<NativeUnityEventInvokable>()), new(302, 1, Array.Empty<NativeUnityEventInvokable>()), list,
                304, 1, Array.Empty<NativeUnityEventPersistent>(), 0);
        }
        public NativeUnityEventPrepared CapturePrepared(int eventSlot, long returnedList)
            => throw new UnsupportedSignatureException("Joint native fixture does not synthesize independent Prepare.");
        public NativeUnityEventPrepared CaptureInline(int eventSlot, long actualList)
        {
            _f.Main.VerifyAccess(); Check(actualList == _f.ActualList.ToInt64(), "Joint mid adopted a foreign list.");
            // Controlled responsibility record is published before any list read.
            // This is not an IL2CPP GC handle or a production root acceptance test.
            InlineAdoptions++; RetainedLists++;
            if (FailAdoption) { CleanupUnknown = true; throw new InvalidOperationException("Controlled adopted-list fixture fault."); }
            var list = ReadList(); return new(ReadGraph(eventSlot), list);
        }
        private NativeUnityEventList ReadList()
        {
            _f.Main.VerifyAccess(); var count = Marshal.ReadInt32(_f.ActualList, 0x18); var version = Marshal.ReadInt32(_f.ActualList, 0x1C);
            Check(count is >= 0 and <= 4 && version > 0, "Controlled native fixture list shape changed."); var items = Marshal.ReadIntPtr(_f.ActualList, 0x10);
            var rows = Enumerable.Range(0, count).Select(index => new NativeUnityEventInvokable(Marshal.ReadIntPtr(items, index * 8).ToInt64(), 502, true,
                Array.Empty<NativeUnityEventLeaf>())).ToArray();
            return new(_f.ActualList.ToInt64(), version, Array.AsReadOnly(rows));
        }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
        {
            _f.Main.VerifyAccess(); var parent = _f.Binding?.ReadActivity() ?? new(0, false); var probe = _f.Probe?.ReadActivity() ?? new(0, false); var mid = _f.Mid.ReadActivity();
            return new(Math.Max(parent.InFlight, Math.Max(probe.InFlight, mid.InFlight)), parent.Known && probe.Known && mid.Known && !CleanupUnknown);
        }
    }
    private sealed class IntegrationLogger : ILogger
    { public void Log(LogLevel level, string message, Exception? exception = null) { } }
}
