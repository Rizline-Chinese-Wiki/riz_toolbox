using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using RizToolbox.Adapter.UiInput;
using GraphRead = RizToolbox.Runtime.Hooks.NativeTweenSingleEventBridge.GraphRead;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Production read-only native operation consumer. Every copied object
/// reference has an independent recorded root before class/metadata inspection.
/// Containers retain full backing capacities, including nulls and duplicate items.</summary>
internal sealed partial class NativeLocalTweenBackend : INativeLocalTweenBackend
{
    private const int MaximumRoots = 4096, MaximumItems = 4096;
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly ExactHookBroker _broker;
    private readonly LocalRunLaunchCommitState _launch;
    private readonly NativeLocalUnityEventLedger _listeners;
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[MaximumRoots];
    private readonly Dictionary<long, int> _slots = new();
    private readonly HashSet<long> _tweens = new();
    private readonly Dictionary<long, long> _managerGenerations = new();
    private readonly HashSet<(long, ulong)> _awakes = new();
    private readonly Dictionary<NativeTweenOperation, VerifiedHookTarget> _targets = new();
    private readonly List<ExactHookBroker.Binding> _bindings = new();
    private readonly Dictionary<string, NativeExactReadField> _tweenFields = new(), _managerFields = new();
    private readonly Dictionary<(IntPtr, NativeTweenQueueKind), NativeTweenQueueLayout> _queues = new();
    private NativeExactReadField _addTween = null!, _delegateTarget = null!, _delegates = null!;
    private IntPtr _tweenClass, _managerClass, _actionClass, _delegateArrayClass;
    private NativeLocalTweenWorkOwner? _owner;
    private int _rootAttempts;
    private bool _attempted, _ready;
    public bool CleanupUnknown { get; private set; }
    public int RootRecords { get { _main.VerifyAccess(); return _roots.Count(root => root is not null) + (_admissionAuthority?.IndependentRootRecords ?? 0) + (_sourceJournal?.RootRecords ?? 0); } }
    public IntPtr PlayMethodInfo => _targets.TryGetValue(NativeTweenOperation.Play, out var target) ? target.MethodInfo : IntPtr.Zero;
    internal NativeLocalTweenBackend(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile,
        IntPtr module, ExactHookBroker broker, LocalRunLaunchCommitState launch, NativeLocalUnityEventLedger listeners)
        => (_api, _main, _profile, _module, _broker, _launch, _listeners) = (api, main, profile, module, broker, launch, listeners);
    public void Initialize(NativeLocalTweenWorkOwner owner)
    {
        _main.VerifyAccess();
        if (_attempted) throw new InvalidOperationException("Actual Tween source capture and binding install only once.");
        _attempted = true; _owner = owner;
        try
        {
            RequireBuild(); ResolveMetadata();
            ConfigureAdmission(owner);
            _listeners.BorrowPersistentTargets(_launch, _tweenClass, (_, _, borrowed) => {
                var identity = Hold(Native(() => borrowed.Target), _tweenClass, published: false);
                _tweens.Add(identity); owner.RegisterSource(identity);
                ClaimAdmission(owner, identity);
            });
            // Exact targets are completely resolved before attaching the first
            // observer to the native pre-init publication. No patch is installed here.
            foreach (var row in TweenOperationProfile.All)
                _targets.Add(row.Operation, Native(() => _api.ResolveTweenOperation(_module, row.Operation)));
            ReadStableGraph();
            owner.CaptureActualTweenDeathSources();
            owner.AdoptActualTweenChildEvents(_listeners);
            owner.AdoptTypedEvents(this);
            InitializeSourceJournal(owner);
            foreach (var row in TweenOperationProfile.All)
            {
                var operation = row.Operation; RequireKnown();
                _bindings.Add(_broker.BindNativeOperationObservation(_targets[operation], row.Kind,
                    frame => owner.CaptureOperation(operation, frame), owner.CompleteOperation,
                    (frame, reason) => owner.ObserveFault(operation, frame, reason)));
                RequireKnown();
            }
            _ready = true;
        }
        catch { CleanupUnknown = true; throw; }
    }
    private void ResolveMetadata()
    {
        _tweenClass = Native(() => _api.Class("Assembly-CSharp.dll", "", "TweenInfo"));
        _managerClass = Native(() => _api.Class("Assembly-CSharp.dll", "", "TweenManager"));
        void Tween(string name, string type, int offset)
            => _tweenFields.Add(name, Native(() => _api.InspectExactDeclaredReadField(_tweenClass, name, type, false, offset)));
        Tween("targetComponent", "UnityEngine.Component", 0x20); Tween("duration", "System.Single", 0x28);
        Tween("delay", "System.Single", 0x2c); Tween("easeType", "TweenInfo.EaseType", 0x30);
        Tween("curve", "UnityEngine.AnimationCurve", 0x38); Tween("tweenType", "TweenInfo.TweenType", 0x40);
        Tween("useFixedStartValue", "System.Boolean", 0x44);
        Tween("useOnStart", "System.Boolean", 0x45); Tween("useOnComplete", "System.Boolean", 0x46);
        Tween("startValue", "System.Single", 0x48); Tween("target", "System.Single", 0x4c);
        Tween("target2", "UnityEngine.Vector2", 0x50); Tween("target3", "UnityEngine.Vector3", 0x58);
        Tween("targetColor", "UnityEngine.Color", 0x64); Tween("fixedStart", "UnityEngine.Vector4", 0x74);
        Tween("onStart", "UnityEngine.Events.UnityEvent", 0x88); Tween("onComplete", "UnityEngine.Events.UnityEvent", 0x90);
        Tween("onTween", "UnityEngine.Events.UnityEvent<System.Single>", 0x98);
        Tween("autoPlay", "System.Boolean", 0xa0); Tween("caMessage", "System.String", 0xa8);
        Tween("nowTime", "System.Single", 0xb0); Tween("isOver", "System.Boolean", 0xd8);
        Tween("startColor", "UnityEngine.Color", 0xb4); Tween("startValue2", "UnityEngine.Vector2", 0xc4);
        Tween("startValue3", "UnityEngine.Vector3", 0xcc);
        void Manager(string name, string type, int offset)
            => _managerFields.Add(name, Native(() => _api.InspectExactDeclaredReadField(_managerClass, name, type, false, offset)));
        Manager("playingTweens", "TweenInfo[]", 0x20);
        Manager("scheduledTweens", "System.Collections.Generic.List<TweenInfo>", 0x28);
        Manager("readyTweens", "System.Collections.Generic.Stack<TweenInfo>", 0x30);
        Manager("tweenTrackSize", "System.Int32", 0x38); Manager("_workingTweenTrackSize", "System.Int32", 0x3c);
        _addTween = Native(() => _api.InspectExactDeclaredReadField(_managerClass, "AddTween", "System.Action<TweenInfo>", true));
        _actionClass = _addTween.FieldClass;
        var delegateClass = Native(() => _api.Class("mscorlib.dll", "System", "Delegate"));
        var multicast = Native(() => _api.Class("mscorlib.dll", "System", "MulticastDelegate"));
        _delegateTarget = Native(() => _api.InspectExactDeclaredReadField(delegateClass, "m_target", "System.Object", false, 0x20));
        _delegates = Native(() => _api.InspectExactDeclaredReadField(multicast, "delegates", "System.Delegate[]", false, 0x78));
        _delegateArrayClass = _delegates.FieldClass;
    }
    public NativeTweenWorkGraph Capture(NativeTweenOperation operation, NativeOperationFrame frame, bool after)
    {
        Check();
        try
        {
            var row = TweenOperationProfile.Get(operation);
            if (!frame.IsValid || frame.Kind != row.Kind || !_targets.TryGetValue(operation, out var target)
                || frame.Method != IntPtr.Zero && frame.Method != target.MethodInfo)
                throw new InvalidOperationException("Tween arguments have no exact verified native declaration.");
            // Store all actual input roots before inspecting either receiver.
            var self = AdoptPublishedInput(frame.Self);
            var argument = frame.Arg0 == IntPtr.Zero ? 0 : AdoptPublishedInput(frame.Arg0);
            RequireKnown();
            if (ObjectClass(Pointer(self)) != target.DeclaringClass)
                throw new UnsupportedSignatureException("Tween receiver differs from its exact pinned class.");
            if (operation == NativeTweenOperation.Play) _tweens.Add(self);
            else
            {
                if (!_managerGenerations.ContainsKey(self)) _managerGenerations.Add(self, 1);
                if (argument != 0)
                {
                    if (ObjectClass(Pointer(argument)) != _tweenClass)
                        throw new UnsupportedSignatureException("The actual manager argument is not the exact TweenInfo.");
                    _tweens.Add(argument);
                }
                if (after && operation == NativeTweenOperation.Awake && _awakes.Add((self, frame.Sequence)))
                    _managerGenerations[self] = checked(_managerGenerations[self] + 1);
            }
            return ReadCurrentStableGraph();
        }
        catch { CleanupUnknown = true; throw; }
    }
    public NativeTweenWorkGraph Refresh()
    {
        _main.VerifyAccess();
        try { return ReadCurrentStableGraph(); } catch { CleanupUnknown = true; throw; }
    }
    private NativeTweenWorkGraph ReadCurrentStableGraph()
    {
        var reader = _ready && !_retirementBindingsWithdrawn && _typedEventBinding is not null
            && _singleEventBridge is { Production: true } bridge ? bridge : null;
        if (reader is null) RequireKnown(); else RequireLocalKnown();
        return ReadStableGraph(reader);
    }
    public NativeTweenManagerObservation CaptureSharedManager(NativeTweenOperation operation, NativeOperationFrame frame, bool after)
    {
        try
        {
            RequireGraphObservationSinks();
            RequireSharedManagerFrame(operation, frame, _targets[operation], _managerClass,
                _managerGenerations.ContainsKey(frame.Self.ToInt64()));
            var manager = AdoptPublishedInput(frame.Self);
            // Each physical boundary validates its own pair. Neither the verification
            // scope nor a previous Tick's queue snapshot may span the game original.
            var result = _singleEventBridge!.ReadVerifiedGraph(scope =>
            {
                var first = ReadManager(manager, scope);
                var second = ReadManager(manager, scope);
                if (!NativeLocalTweenWorkOwner.SameManager(first, second))
                    throw new InvalidOperationException("Shared TweenManager changed during paired queue reads.");
                RequireKnown(scope);
                return second;
            });
            RequireGraphObservationSinks();
            return result;
        }
        catch { CleanupUnknown = true; throw; }
    }
    internal static void RequireSharedManagerFrame(NativeTweenOperation operation, NativeOperationFrame frame,
        VerifiedHookTarget target, IntPtr managerClass, bool retainedManager)
    {
        if (operation is not (NativeTweenOperation.Update or NativeTweenOperation.AddTweenToWaitingList)
            || !frame.IsValid || frame.Kind != TweenOperationProfile.Get(operation).Kind
            || target.Abi != ExactHookBroker.OperationAbi(frame.Kind) || frame.Method != IntPtr.Zero && frame.Method != target.MethodInfo
            || operation == NativeTweenOperation.AddTweenToWaitingList && frame.Arg0 == IntPtr.Zero
            || !retainedManager || target.DeclaringClass != managerClass)
            throw new InvalidOperationException($"Shared TweenManager {operation} sequence {frame.Sequence} requires its exact previously rooted receiver and native frame.");
    }
    private NativeTweenWorkGraph ReadStableGraph(NativeTweenSingleEventBridge? reader = null)
    {
        var before = Read(expected: null);
        var after = Read(before);
        if (reader is not null) RequireGraphObservationSinks();
        return after;

        NativeTweenWorkGraph Read(NativeTweenWorkGraph? expected)
        {
            // An unclaimed admission can retry its native claim here. Each
            // read-only graph traversal starts after that operation returns.
            RefreshAdmissions();
            return reader is null ? ReadChecked(null) : reader.ReadVerifiedGraph(ReadChecked);

            NativeTweenWorkGraph ReadChecked(GraphRead? verification)
            {
                var snapshot = ReadGraphOnce(verification);
                if (expected is not null && !SameGraph(expected, snapshot))
                    throw new InvalidOperationException("Actual Tween graph changed during its paired complete rooted reads.");
                if (expected is not null || verification is not null) RequireKnown(verification);
                return snapshot;
            }
        }
    }
    private NativeTweenWorkGraph ReadGraphOnce(GraphRead? verification)
    {
        long add = 0;
        RequireKnown(verification); _api.PublishExactStaticReferenceField(_addTween,
            pointer => add = Hold(pointer, _actionClass, nullable: true, published: true, verification)); RequireKnown(verification);
        var leaves = ReadAddTween(add, verification);
        var managers = new List<NativeTweenManagerObservation>();
        foreach (var manager in _managerGenerations.Keys.ToArray()) managers.Add(ReadManager(manager, verification));
        var infos = _tweens.OrderBy(value => value).Select(tween => ReadTween(tween, verification)).ToArray();
        return new(add, Array.AsReadOnly(leaves), Array.AsReadOnly(infos), Array.AsReadOnly(managers.ToArray()));
    }
    private NativeTweenDelegateObservation[] ReadAddTween(long action, GraphRead? verification)
    {
        if (action == 0) return Array.Empty<NativeTweenDelegateObservation>();
        var list = Reference(action, _delegates, nullable: true, verification);
        var entries = list == 0 ? new[] { action } : ReadArray(list, _delegateArrayClass,
            Native(() => _api.Class("mscorlib.dll", "System", "Delegate"), verification), verification);
        var observations = new List<NativeTweenDelegateObservation>();
        foreach (var entry in entries)
        {
            if (entry == 0) throw new UnsupportedSignatureException("The actual AddTween multicast contains a null delegate.");
            var target = Reference(entry, _delegateTarget, nullable: true, verification);
            var actual = ObjectClass(Pointer(entry, verification), verification); var supported = false; long method = 0;
            try
            {
                var projection = Native(() => _api.InspectRootedDelegateMethod(Root(entry), actual, _module), verification);
                method = projection.Method.MethodInfo.ToInt64();
                supported = actual == _actionClass && target != 0 && projection.Target.ToInt64() == target
                    && !projection.Virtual && !projection.Method.GenericMethod && !projection.Method.InflatedMethod
                    && (projection.Method.Flags & (0x10 | 0x40 | 0x400)) == 0 && projection.Method.ReturnType == "System.Void"
                    && projection.Method.ParameterTypes.SequenceEqual(new[] { "TweenInfo" })
                    && _targets.TryGetValue(NativeTweenOperation.AddTweenToWaitingList, out var expected)
                    && projection.Method.MethodInfo == expected.MethodInfo && projection.Method.DeclaringClass.Class == _managerClass
                    && ObjectClass(Pointer(target, verification), verification) == _managerClass
                    && Native(() => _api.InspectMethodParameterClass(projection.Method.MethodInfo, 0, "TweenInfo"), verification).Class == _tweenClass;
            }
            catch (UnsupportedSignatureException) { RequireKnown(verification); }
            if (supported && !_managerGenerations.ContainsKey(target)) _managerGenerations.Add(target, 1);
            observations.Add(new(entry, list, actual.ToInt64(), target, method, supported));
        }
        return observations.ToArray();
    }
    private NativeTweenManagerObservation ReadManager(long manager, GraphRead? verification)
    {
        if (ObjectClass(Pointer(manager, verification), verification) != _managerClass) throw new UnsupportedSignatureException("Retained manager changed its exact class.");
        var track = Value<int>(manager, _managerFields["tweenTrackSize"], verification);
        var working = Value<int>(manager, _managerFields["_workingTweenTrackSize"], verification);
        if (track < 0 || track > MaximumItems || working < 0 || working > track)
            throw new UnsupportedSignatureException("Actual TweenManager track/count exceeds the complete capture bound.");
        var playing = Reference(manager, _managerFields["playingTweens"], nullable: true, verification);
        NativeTweenContainerObservation? playingGraph = null;
        if (playing != 0)
        {
            var items = ReadArray(playing, _managerFields["playingTweens"].FieldClass, _tweenClass, verification);
            if (working > items.Length || track > items.Length) throw new UnsupportedSignatureException("Playing array is shorter than its actual track state.");
            playingGraph = new(playing, playing, ObjectClass(Pointer(playing, verification), verification).ToInt64(), working, 0, System.Array.AsReadOnly(items));
            foreach (var item in items.Where(item => item != 0)) _tweens.Add(item);
        }
        var scheduled = ReadQueue(manager, _managerFields["scheduledTweens"], NativeTweenQueueKind.List, verification);
        var ready = ReadQueue(manager, _managerFields["readyTweens"], NativeTweenQueueKind.Stack, verification);
        return new(manager, _managerClass.ToInt64(), _managerGenerations[manager], track, working, playingGraph, scheduled, ready);
    }
    private NativeTweenContainerObservation? ReadQueue(long manager, NativeExactReadField field, NativeTweenQueueKind kind, GraphRead? verification)
    {
        var queue = Reference(manager, field, nullable: true, verification); if (queue == 0) return null;
        var actual = ObjectClass(Pointer(queue, verification), verification);
        if (!_queues.TryGetValue((actual, kind), out var layout))
        { layout = Native(() => _api.InspectTweenQueueClass(actual, kind, _tweenClass), verification); _queues.Add((actual, kind), layout); }
        var count = Value<int>(queue, layout.SizeField, verification); var version = Value<int>(queue, layout.VersionField, verification);
        if (count < 0 || version < 0 || count > MaximumItems) throw new UnsupportedSignatureException("Actual Tween queue exceeds its complete logical bound.");
        var array = Reference(queue, layout.BackingField, nullable: false, verification);
        var items = ReadArray(array, layout.ArrayClass, _tweenClass, verification);
        if (count > items.Length) throw new InvalidOperationException("Actual Tween queue count exceeds its complete backing array.");
        if (Value<int>(queue, layout.SizeField, verification) != count || Value<int>(queue, layout.VersionField, verification) != version
            || Reference(queue, layout.BackingField, false, verification) != array)
            throw new InvalidOperationException("Tween queue size/version/backing changed during read-only capture.");
        foreach (var item in items.Where(item => item != 0)) _tweens.Add(item);
        return new(queue, array, actual.ToInt64(), count, version, System.Array.AsReadOnly(items));
    }
    private NativeTweenInfoObservation ReadTween(long tween, GraphRead? verification)
    {
        if (ObjectClass(Pointer(tween, verification), verification) != _tweenClass) throw new UnsupportedSignatureException("An actual queue item changed its exact TweenInfo class.");
        var message = Reference(tween, _tweenFields["caMessage"], true, verification);
        var target2 = Value<Vec2>(tween, _tweenFields["target2"], verification);
        var target3 = Value<NativeVector3>(tween, _tweenFields["target3"], verification);
        var color = Value<Rgba>(tween, _tweenFields["targetColor"], verification);
        var fixedStart = Value<NativeTweenVector4>(tween, _tweenFields["fixedStart"], verification);
        var startColor = Value<Rgba>(tween, _tweenFields["startColor"], verification);
        var start2 = Value<Vec2>(tween, _tweenFields["startValue2"], verification);
        var start3 = Value<NativeVector3>(tween, _tweenFields["startValue3"], verification);
        var effects = new NativeTweenEffectValues(Value<bool>(tween, _tweenFields["useFixedStartValue"], verification),
            Value<bool>(tween, _tweenFields["autoPlay"], verification), message, message == 0 ? "" : Native(() => _api.Text(Pointer(message, verification)), verification),
            BitConverter.SingleToInt32Bits(Value<float>(tween, _tweenFields["startValue"], verification)),
            BitConverter.SingleToInt32Bits(Value<float>(tween, _tweenFields["target"], verification)),
            new(Bits(target2.X), Bits(target2.Y)), new(Bits(target3.X), Bits(target3.Y), Bits(target3.Z)),
            new(Bits(color.R), Bits(color.G), Bits(color.B), Bits(color.Alpha)),
            new(Bits(fixedStart.X), Bits(fixedStart.Y), Bits(fixedStart.Z), Bits(fixedStart.W)),
            new(Bits(startColor.R), Bits(startColor.G), Bits(startColor.B), Bits(startColor.Alpha)),
            new(Bits(start2.X), Bits(start2.Y)), new(Bits(start3.X), Bits(start3.Y), Bits(start3.Z)));
        return new(tween, _tweenClass.ToInt64(), Reference(tween, _tweenFields["targetComponent"], true, verification),
            Reference(tween, _tweenFields["curve"], true, verification), Reference(tween, _tweenFields["onStart"], true, verification),
            Reference(tween, _tweenFields["onComplete"], true, verification), Reference(tween, _tweenFields["onTween"], true, verification),
            BitConverter.SingleToInt32Bits(Value<float>(tween, _tweenFields["duration"], verification)),
            BitConverter.SingleToInt32Bits(Value<float>(tween, _tweenFields["delay"], verification)),
            BitConverter.SingleToInt32Bits(Value<float>(tween, _tweenFields["nowTime"], verification)),
            Native(() => _api.ReadTweenInt32EnumField(Root(tween), _tweenFields["easeType"]), verification),
            Native(() => _api.ReadTweenInt32EnumField(Root(tween), _tweenFields["tweenType"]), verification),
            Value<bool>(tween, _tweenFields["useOnStart"], verification), Value<bool>(tween, _tweenFields["useOnComplete"], verification),
            Value<bool>(tween, _tweenFields["isOver"], verification), effects);
    }
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private long[] ReadArray(long array, IntPtr arrayClass, IntPtr elementClass, GraphRead? verification)
    {
        RequireKnown(verification);
        var result = _api.ReadRootedReferenceArrayWithPublication(Root(array), arrayClass, elementClass, MaximumItems,
            (_, pointer) => Hold(pointer, IntPtr.Zero, nullable: true, published: true, verification));
        RequireKnown(verification); return result.Items.Select(pointer => pointer.ToInt64()).ToArray();
    }
    private long Reference(long owner, NativeExactReadField field, bool nullable) => Reference(owner, field, nullable, null);
    private long Reference(long owner, NativeExactReadField field, bool nullable, GraphRead? verification)
    {
        long result = 0; RequireKnown(verification);
        _api.PublishExactReferenceField(Root(owner), field,
            pointer => result = Hold(pointer, IntPtr.Zero, nullable, published: true, verification));
        RequireKnown(verification); return result;
    }
    private T Value<T>(long owner, NativeExactReadField field, GraphRead? verification) where T : struct
        => Native(() => _api.ReadExactField<T>(Root(owner), field), verification);
    private long Hold(IntPtr pointer, IntPtr expected, bool nullable = false, bool published = true, GraphRead? verification = null)
    {
        _main.VerifyAccess(); RequireRetirementRootsReadable(); if (!published) RequireKnown(verification);
        if (pointer == IntPtr.Zero) { RequireKnown(verification); if (nullable) return 0; throw new InvalidOperationException("A required Tween reference is null."); }
        var identity = pointer.ToInt64();
        if (!_slots.ContainsKey(identity))
        {
            if (_retirementBindingsWithdrawn) throw new InvalidOperationException("Tween root enrollment permanently closed with its observation sinks.");
            if (_rootAttempts == MaximumRoots) throw new InvalidOperationException("Tween process root responsibility bound exhausted.");
            var slot = _rootAttempts++;
            _roots[slot] = _api.Root(pointer); _slots.Add(identity, slot);
        }
        RequireKnown(verification);
        var retained = Native(() => Root(identity).Target, verification);
        if (retained != pointer || expected != IntPtr.Zero && ObjectClass(retained, verification) != expected)
            throw new UnsupportedSignatureException("The independently retained Tween reference differs from its exact identity/class.");
        return identity;
    }
    private long AdoptPublishedInput(IntPtr pointer)
    {
        // The native frame already published self and the reference argument.
        // Record both roots before the first Target/class/metadata read or the
        // post-operation authority barrier, preserving any partial root batch.
        _main.VerifyAccess(); RequireRetirementRootsReadable();
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("A required native Tween input is null.");
        var identity = pointer.ToInt64();
        if (_slots.TryGetValue(identity, out var existingSlot))
        {
            if (_roots[existingSlot] is not { } existing || existing.Target != pointer)
                throw new TargetExpiredException("Tween retained input root identity changed.");
            return identity;
        }
        if (_retirementBindingsWithdrawn) throw new InvalidOperationException("Tween input root enrollment is permanently closed.");
        if (_rootAttempts == MaximumRoots) throw new InvalidOperationException("Tween native input root capacity exhausted.");
        var slot = _rootAttempts++;
        _roots[slot] = _api.Root(pointer); _slots.Add(identity, slot); return identity;
    }
    private NativeRuntime.RootHandle Root(long identity)
    {
        RequireRetirementRootsReadable();
        if (_slots.TryGetValue(identity, out var slot) && _roots[slot] is { } root)
        {
            if (root.Target.ToInt64() != identity)
                throw new TargetExpiredException("Tween root identity changed.");
            return root;
        }
        throw new InvalidOperationException("The actual Tween object has no independent retained root.");
    }
    private void RequireRetirementRootsReadable()
    { if (_retirementRootReleaseStarted || _owner?.RetiredRootsUnavailable == true) throw new InvalidOperationException("Tween backend roots have entered permanent retirement."); }
    private IntPtr Pointer(long identity, GraphRead? verification = null)
    { var pointer = Native(() => Root(identity).Target, verification); if (pointer.ToInt64() != identity) throw new TargetExpiredException("Tween root identity changed."); return pointer; }
    private IntPtr ObjectClass(IntPtr pointer, GraphRead? verification = null) => Native(() => _api.ObjectClass(pointer), verification);
    private T Native<T>(Func<T> read, GraphRead? verification = null)
    { RequireKnown(verification); var value = read(); RequireKnown(verification); return value; }
    private bool Known => !CleanupUnknown && _admissionAuthority?.Unknown != true && _owner is not null && !_owner.ObservationAuthorityUnknown && !_listeners.Unknown
        && _launch.Read().Phase != LocalRunLaunchCommitPhase.Unknown;
    private void RequireLocalKnown()
    {
        _main.VerifyAccess(); RequireRetirementRootsReadable();
        if (!Known) throw new InvalidOperationException("Tween read/source authority is Unknown.");
    }
    private void RequireKnown() => RequireKnown(null);
    private void RequireKnown(GraphRead? verification)
    {
        if (verification is null) RequireLocalKnown(); else RequireGraphObservationSinks();
        if (_ready)
        {
            foreach (var binding in _bindings)
                if (binding.ReadActivity() is not { Known: true, InFlight: >= 0 })
                    throw new InvalidOperationException("A shared Tween native operation transport is Unknown.");
            if (_typedEventBinding is not null && ReadTypedEventActivity(verification) is { } typed && (!typed.Known || typed.InFlight < 0))
                throw new InvalidOperationException("The typed Tween event transport is Unknown.");
        }
    }
    private void RequireGraphObservationSinks()
    {
        RequireLocalKnown();
        if (!_ready || _retirementBindingsWithdrawn || _typedEventBinding is null || _singleEventBridge is null)
            throw new InvalidOperationException("The current Tween graph scan lost its retained observation sinks.");
    }
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        _main.VerifyAccess(); RequireRetirementRootsReadable(); if (!Known || _bindings.Count != TweenOperationProfile.All.Count) return new(0, false);
        long total = 0;
        foreach (var binding in _bindings)
        {
            var activity = binding.ReadActivity();
            if (!Known || !activity.Known || activity.InFlight < 0 || total > long.MaxValue - activity.InFlight)
            { CleanupUnknown = true; return new(total, false); }
            total += activity.InFlight;
        }
        if (_typedEventBinding is { } typedEvent)
        {
            var typed = ReadTypedEventActivity();
            if (!Known || !typed.Known || typed.InFlight < 0 || total > long.MaxValue - typed.InFlight)
            { CleanupUnknown = true; return new(total, false); }
            total += typed.InFlight;
        }
        return new(total, Known);
    }
    private void Check() { if (!_ready) throw new InvalidOperationException("Tween production backend is not initialized."); RequireKnown(); }
    private void RequireBuild()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Tween graph requires the pinned Windows x64 Unity build.");
        using var stream = File.OpenRead(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll"));
        using var hash = SHA256.Create();
        if (Convert.ToHexString(hash.ComputeHash(stream)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
            throw new UnsupportedSignatureException("Tween image differs from its pinned operation profile.");
    }
    private static bool SameContainer(NativeTweenContainerObservation? a, NativeTweenContainerObservation? b)
        => a is null && b is null || a is not null && b is not null && a.Object == b.Object && a.Array == b.Array
            && a.Class == b.Class && a.Count == b.Count && a.Version == b.Version && a.CapacityItems.SequenceEqual(b.CapacityItems);
    internal static bool SameGraph(NativeTweenWorkGraph a, NativeTweenWorkGraph b)
        => a.AddTween == b.AddTween && a.AddTweenLeaves.SequenceEqual(b.AddTweenLeaves) && a.Tweens.SequenceEqual(b.Tweens)
            && a.Managers.Count == b.Managers.Count && a.Managers.Zip(b.Managers).All(pair => pair.First.Object == pair.Second.Object
                && pair.First.Class == pair.Second.Class && pair.First.Generation == pair.Second.Generation
                && pair.First.TrackSize == pair.Second.TrackSize && pair.First.WorkingCount == pair.Second.WorkingCount
                && SameContainer(pair.First.Playing, pair.Second.Playing) && SameContainer(pair.First.Scheduled, pair.Second.Scheduled)
                && SameContainer(pair.First.Ready, pair.Second.Ready));
}
