using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalCutoutEventSourceBackend : INativeCutoutEventSourceBackend
{
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly ExactHookBroker _broker;
    private readonly NativeRuntime.RootHandle _borrowedController;
    private readonly LocalRunLaunchCommitState _launch;
    private readonly NativePublicationArgumentRecords _arguments;
    // controller, EventTrigger, GO, Transform, list, then 32 trigger/event pairs.
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[69];
    private IntPtr _eventClass, _triggerClass, _unityEvent, _listClass, _alive, _getGo, _getTransform, _getItem;
    private IntPtr _rectTransformClass;
    private VerifiedHookTarget? _target;
    private ExactHookBroker.Binding? _binding;
    private ExactHookBroker.BoundaryProbeBinding? _probe;
    private NativeCutoutEventObservation? _baseline;
    private NativeLocalCutoutEventSource? _owner;
    private bool _captureAttempted, _bindAttempted;
    public bool CleanupUnknown { get; private set; }
    public int RootRecords { get { _main.VerifyAccess(); return _roots.Count(root => root is not null); } }
    internal int RetainedCallArgumentRecords => _arguments.Count;
    internal NativeLocalCutoutEventSourceBackend(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, ExactHookBroker broker, NativeRuntime.RootHandle actualController,
        LocalRunLaunchCommitState launch)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(main); ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(broker); ArgumentNullException.ThrowIfNull(actualController);
        ArgumentNullException.ThrowIfNull(launch); _launch = launch;
        (_api, _main, _profile, _module, _broker, _borrowedController) = (api, main, profile, module, broker, actualController);
        _arguments = new(main);
    }
    public NativeCutoutEventObservation Capture()
    {
        _main.VerifyAccess();
        if (_captureAttempted) throw new InvalidOperationException("Cutout source native capture is once-only.");
        _captureAttempted = true;
        try
        {
            RequireBuild();
            var normal = Native(() => _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController"));
            _eventClass = Native(() => _api.Class("Assembly-CSharp.dll", "", "EventTrigger"));
            _triggerClass = Native(() => _api.NestedClass(_eventClass, "Trigger"));
            _unityEvent = Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "UnityEvent"));
            var unityObject = Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object"));
            var component = Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Component"));
            var go = Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "GameObject"));
            var transform = Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Transform"));
            _rectTransformClass = Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "RectTransform"));
            var triggerName = Native(() => _api.InspectMetadataClass(_triggerClass)).TypeName;
            var listName = "System.Collections.Generic.List<" + triggerName + ">";
            Native(() => _api.RequireInstanceFieldLayout(normal, "cutoutMaskEventTrigger", "EventTrigger", 0x130));
            Native(() => _api.RequireInstanceFieldLayout(_eventClass, "triggers", listName, 0x28));
            _listClass = Native(() => _api.InspectMetadataFieldClass(_eventClass, "triggers", listName)).FieldClass.Class;
            Native(() => _api.RequireInstanceFieldLayout(_listClass, "_size", "System.Int32", 0x18));
            Native(() => _api.RequireInstanceFieldLayout(_listClass, "_version", "System.Int32", 0x1C));
            Native(() => _api.RequireInstanceFieldLayout(_triggerClass, "events", "UnityEngine.Events.UnityEvent", 0x10));
            Native(() => _api.RequireInstanceFieldLayout(_triggerClass, "offset", "System.Single", 0x18));
            _alive = Method(unityObject, "op_Implicit", new[] { "UnityEngine.Object" }, "System.Boolean", true, 0x15409B0);
            _getGo = Method(component, "get_gameObject", Array.Empty<string>(), "UnityEngine.GameObject", false, 0x1536400);
            _getTransform = Method(go, "get_transform", Array.Empty<string>(), "UnityEngine.Transform", false);
            _getItem = Method(_listClass, "get_Item", new[] { "System.Int32" }, triggerName, false);
            _target = Native(() => _api.ResolveExactHook("local.cutout.EventTrigger.StartEvents", _module, _eventClass,
                "StartEvents", Array.Empty<string>(), 0x38A460, ExactHookAbi.InstanceVoid));
            Root(0, Native(() => _borrowedController.Target), normal);
            if (!Alive(Pointer(0))) throw new TargetExpiredException("The actual preactivation controller is destroyed.");
            RootField(1, Pointer(0), "cutoutMaskEventTrigger", _eventClass);
            Native(() => _api.InvokeReferenceWithPublication(_getGo, Pointer(1), pointer => Root(2, pointer, go, published: true)));
            Native(() => _api.InvokeReferenceWithPublication(_getTransform, Pointer(2), pointer => Root(3, pointer, transform, published: true)));
            if (!Alive(Pointer(1)) || !Alive(Pointer(2)) || !Alive(Pointer(3)))
                throw new TargetExpiredException("The actual cutout EventTrigger graph is destroyed before activation.");
            RootField(4, Pointer(1), "triggers", _listClass);
            var version = Version(); var count = Count(); var rows = new NativeCutoutTrigger[count];
            for (var index = 0; index < count; ++index)
            {
                var slot = 5 + index * 2;
                ReadItem(index, pointer => Root(slot, pointer, _triggerClass, published: true));
                RootField(slot + 1, Pointer(slot), "events", _unityEvent);
                rows[index] = Row(slot);
            }
            if (Version() != version || Count() != count || Read<IntPtr>(Pointer(1), "triggers") != Pointer(4)
                || Read<IntPtr>(Pointer(0), "cutoutMaskEventTrigger") != Pointer(1))
                throw new InvalidOperationException("The actual cutout source changed during bounded capture.");
            return _baseline = new(Pointer(0).ToInt64(), Pointer(1).ToInt64(), Pointer(2).ToInt64(), Pointer(3).ToInt64(),
                Pointer(4).ToInt64(), version, true, Array.AsReadOnly(rows));
        }
        catch { CleanupUnknown = true; throw; }
    }
    public void Bind(NativeLocalCutoutEventSource owner)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(owner);
        if (_baseline is null || _bindAttempted || CleanupUnknown) throw new InvalidOperationException("Cutout probes bind once after a complete real capture.");
        _bindAttempted = true;
        _owner = owner;
        try
        {
            // Ordinary observer is deliberately inert. The scalar probe lane sees
            // worker/reentry calls which ordinary delivery suppresses. This remains
            // the existing managed-original InstanceVoid transport: native SEH/C++
            // original unwind is not newly proven by installing these observers.
            RequireKnown(); _binding = _broker.Bind(_target!, _ => false, (_, _, _, _) => { }); RequireKnown();
            _probe = _broker.BindBoundaryProbe(_target!,
                (self, _, method) => owner.ProbeEnter(self.ToInt64(), method == IntPtr.Zero || method == _target!.MethodInfo),
                (self, _, _, returned) => owner.ProbeExit(self.ToInt64(), returned));
            RequireKnown();
        }
        catch { CleanupUnknown = true; throw; }
    }
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        _main.VerifyAccess();
        if (_binding is null || _probe is null || !ObservationKnown) return new(0, false);
        var normal = _binding.ReadActivity();
        if (!ObservationKnown) return new(normal.InFlight, false);
        var probe = _probe.ReadActivity();
        return new(Math.Max(normal.InFlight, probe.InFlight), ObservationKnown && normal.Known && probe.Known);
    }
    public void BorrowUnityEvents(Action<int, NativeRuntime.RootHandle> adopt)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(adopt);
        if (_baseline is null || !_bindAttempted || _binding is null || _probe is null || !ObservationKnown)
            throw new InvalidOperationException("Only the complete retained production event closure can lend its roots.");
        for (var index = 0; index < _baseline.Triggers.Count; ++index)
        {
            RequireKnown();
            var root = _roots[6 + index * 2] ?? throw new InvalidOperationException("Actual event root slot is missing.");
            // The owner checks known state before and after the single native
            // Target read, then verifies its exact Trigger association.
            adopt(index, root); RequireKnown();
        }
    }
    public NativeCutoutEventObservation Refresh()
    {
        _main.VerifyAccess();
        if (_baseline is null) throw new InvalidOperationException("Cutout capture has no confirmed source.");
        RequireKnown();
        try
        {
            // Once Unity-dead, keep the already-rooted managed closure and copied
            // identities. Do not dereference old controller/trigger Unity getters.
            if (!Alive(Pointer(1)) || !Alive(Pointer(2)) || !Alive(Pointer(3))) return _baseline with { Alive = false };
            if (Read<IntPtr>(Pointer(1), "triggers") != Pointer(4))
                throw new InvalidOperationException("Cutout trigger list was replaced.");
            var version = Version(); var count = Count();
            if (count != _baseline.Triggers.Count || version != _baseline.Version)
                throw new InvalidOperationException("Cutout trigger list size/version changed.");
            var rows = new NativeCutoutTrigger[count];
            for (var index = 0; index < count; ++index)
            {
                var slot = 5 + index * 2;
                ReadItem(index, pointer => {
                    if (pointer != Pointer(slot)) throw new InvalidOperationException("Cutout trigger identity changed.");
                });
                if (Read<IntPtr>(Pointer(slot), "events") != Pointer(slot + 1))
                    throw new InvalidOperationException("Cutout UnityEvent identity changed.");
                rows[index] = Row(slot);
            }
            if (Version() != version || Count() != count) throw new InvalidOperationException("Cutout list changed while observed.");
            return _baseline with { Alive = true, Triggers = Array.AsReadOnly(rows) };
        }
        catch { CleanupUnknown = true; throw; }
    }
    private NativeCutoutTrigger Row(int slot)
    {
        var bits = BitConverter.SingleToInt32Bits(Read<float>(Pointer(slot), "offset"));
        if (!float.IsFinite(BitConverter.Int32BitsToSingle(bits))) throw new UnsupportedSignatureException("Non-finite cutout event offset is unsupported.");
        return new(Pointer(slot).ToInt64(), Pointer(slot + 1).ToInt64(), bits);
    }
    private int Count()
    {
        var count = Read<int>(Pointer(4), "_size");
        if (count < 0 || count > 32) throw new UnsupportedSignatureException("Cutout source exceeds its complete 32-trigger snapshot bound.");
        return count;
    }
    private int Version() => Read<int>(Pointer(4), "_version");
    private void ReadItem(int index, Action<IntPtr> publish)
    {
        RequireKnown();
        // Publication capacity rejection happens before runtime argument adoption.
        // Keep the owned Int32 buffer in this process-held backend first, including
        // an entry failure which cannot confirm whether native inputs were used.
        var arguments = new[] { A.Value(index) };
        _arguments.Begin(arguments);
        try { _api.InvokeReferenceWithPublication(_getItem, Pointer(4), publish, arguments); }
        catch { _arguments.Fail(); CleanupUnknown = true; throw; }
        _arguments.Complete(); RequireKnown();
    }
    private IntPtr Pointer(int index) => Native(() => _roots[index]!.Target);
    private void RootField(int slot, IntPtr owner, string field, IntPtr expected)
    {
        RequireKnown();
        var pointer = _api.ReadWithPublication<IntPtr>(owner, field);
        Root(slot, pointer, expected, published: true);
    }
    private void Root(int slot, IntPtr pointer, IntPtr expected, bool published = false)
    {
        if (!published) RequireKnown();
        if (pointer == IntPtr.Zero || _roots[slot] is not null) throw new InvalidOperationException("Cutout root publication is absent or duplicate.");
        // The synchronous publication callback must first retain its returned
        // reference. After that adoption, an external fault forbids even Target.
        _roots[slot] = _api.Root(pointer);
        RequireKnown();
        var actual = Native(() => _api.ObjectClass(Pointer(slot)));
        if (!MatchesRootClass(slot, actual, expected, _rectTransformClass))
        {
            var actualName = Native(() => _api.InspectMetadataClass(actual)).TypeName;
            var expectedName = Native(() => _api.InspectMetadataClass(expected)).TypeName;
            throw new UnsupportedSignatureException($"Cutout source root {slot} has class {actualName}; expected {expectedName}.");
        }
    }
    // GameObject.get_transform on the stock CutOut UI object returns its RectTransform component.
    internal static bool MatchesRootClass(int slot, IntPtr actual, IntPtr expected, IntPtr rectTransform)
        => actual != IntPtr.Zero && (actual == expected || (slot == 3 && actual == rectTransform));
    private T Read<T>(IntPtr owner, string field) where T : struct
        => Native(() => _api.ReadWithPublication<T>(owner, field));
    private bool Alive(IntPtr pointer) => Native(() => _api.InvokeValueWithPublication<bool>(_alive, IntPtr.Zero, A.Ref(pointer)));
    private IntPtr Method(IntPtr type, string name, string[] parameters, string returns, bool isStatic, long? rva = null)
    {
        var method = Native(() => isStatic ? _api.InspectDeclaredStaticMethod(type, name, parameters, returns, _module)
            : _api.InspectDeclaredMethod(type, name, parameters, returns, _module));
        // Closed List<T> methods are inflated by design; their class and signature
        // were obtained from this exact field. Other operations must be non-generic.
        if (method.Entry is null || rva.HasValue && method.Entry.Rva != rva || method.GenericMethod
            || type != _listClass && method.InflatedMethod || (method.Flags & 0x400) != 0
            || ((method.Flags & 0x10) != 0) != isStatic)
            throw new UnsupportedSignatureException("Cutout source method signature/RVA changed: " + name);
        return method.MethodInfo;
    }
    private bool ObservationKnown => !CleanupUnknown && !_arguments.CleanupUnknown
        && _launch.Read().Phase != LocalRunLaunchCommitPhase.Unknown && _owner?.ObservationAuthorityUnknown != true;
    private void RequireKnown()
    {
        if (!ObservationKnown) throw new InvalidOperationException("Cutout source shared observation authority is unknown.");
    }
    private T Native<T>(Func<T> operation)
    { RequireKnown(); var value = operation(); RequireKnown(); return value; }
    private void Native(Action operation)
    { RequireKnown(); operation(); RequireKnown(); }
    private void RequireBuild()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Cutout event source requires the pinned Windows x64 Unity image.");
        using var stream = File.OpenRead(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll"));
        using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
            throw new UnsupportedSignatureException("Cutout event source image differs from its pinned profile.");
    }
}
