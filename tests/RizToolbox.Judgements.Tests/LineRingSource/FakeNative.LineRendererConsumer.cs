using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Exact synthetic IL2CPP metadata and physical LineInformation field storage
// for the existing retained backend. No generated code or game is loaded.
sealed partial class FakeNative
{
    internal sealed record LineRendererNativeCall(int Ordinal, IntPtr Line, IntPtr Array,
        int Count, NativeVector3BufferBits[] CompleteScratch, bool Active);

    internal readonly List<LineRendererNativeCall> LineRendererNativeCalls = new();
    internal readonly List<(IntPtr Line, string Name, long Value)> LineRendererInverseStores = new();
    internal Action<LineRendererNativeCall>? AfterLineRendererFieldsSaved;
    internal bool LineRendererExceptionAfterFields;
    internal bool LineRendererArgumentsRooted = true;
    internal int LineRendererUnityCalls;
    internal IntPtr LineRendererFirstDynamic, LineRendererSecondDynamic;
    internal ISeekLineSetPositionsNativeProfile LineRendererNativeProfile = null!;

    private IntPtr _lineRendererPlanType, _lineRendererPoolType, _lineRendererChildType;
    private IntPtr _lineRendererComponentType, _lineRendererUnityType;
    private IntPtr _lineRendererMethod, _lineRendererMethodName, _lineRendererModule;
    private readonly Dictionary<IntPtr, uint> _lineRendererObjectSizes = new();
    private readonly Dictionary<IntPtr, IntPtr> _lineRendererPlans = new();

    internal void ConfigureLineRendererConsumer(IntPtr syntheticImage)
    {
        if (LineBufferArray == IntPtr.Zero || syntheticImage == IntPtr.Zero)
            throw new InvalidOperationException("Configure the original scratch and synthetic image first.");
        _lineRendererModule = syntheticImage;
        _lineRendererPlanType = AddType("InLevel.LineInformation.LineRenderer", 18, false, IntPtr.Size);
        _lineRendererPoolType = SeekType("InLevel.EasyPool");
        _lineRendererChildType = SeekType("InLevel.EasyPoolChild");
        _lineRendererComponentType = SeekType("UnityEngine.Component");
        _lineRendererUnityType = SeekType("UnityEngine.LineRenderer");

        LineRendererFirstDynamic = _canvasSourceArrays[LineRingDynamicArray][0];
        var secondStart = NoteViewPoints[1][0]; var secondEnd = NoteViewPoints[1][1];
        TargetSet(secondStart, "canvasIndex", 0); TargetSet(secondEnd, "canvasIndex", 1);
        TargetSet(secondStart, "xPosition", .005f); TargetSet(secondEnd, "xPosition", .005f);
        TargetSet(secondStart, "floorPosition", 4f); TargetSet(secondEnd, "floorPosition", 4f);
        var group = SeekValue<IntPtr>(LineRendererFirstDynamic, "linesGroup");
        LineRendererSecondDynamic = LineRingNewLine(group, secondStart, secondEnd, false);
        TargetSet(LineRendererSecondDynamic, "endLineInformation", IntPtr.Zero);
        var groupOnly = _canvasSourceArrays[LineRingGroupArray(group)].Single(line => TargetBoolean(line, "lineIsStatic"));
        LineRingSetGroupLines(group, LineRendererFirstDynamic, LineRendererSecondDynamic, groupOnly);
        LineRingSetDynamic(LineRendererFirstDynamic, LineRendererSecondDynamic, LineRendererFirstDynamic);

        foreach (var line in LineRingLines.Distinct())
        {
            _lineRendererObjectSizes.Add(line, 0x98);
            var active = line == LineRendererFirstDynamic;
            var pool = LineRendererObject(_lineRendererPoolType, 0x40);
            var child = LineRendererObject(_lineRendererChildType, 0x40);
            var target = LineRendererObject(_lineRendererUnityType, 0x40);
            var targetTransform = GeometryNewTransform(GeometryUiRoot, new(1, 2, 3),
                new(0, 0, 0, 1), new(1, 1, 1));
            GeometryComponentTransforms[target] = targetTransform;
            SeekField("line.renderer.child." + line + ".component", _lineRendererChildType,
                "targetComponent", _lineRendererComponentType, child, target, 0x20);
            SeekField("line.renderer.child." + line + ".pool", _lineRendererChildType,
                "parentPool", _lineRendererPoolType, child, pool, 0x28);
            var plan = LineRendererObject(_lineRendererPlanType, 0x40);
            _lineRendererPlans.Add(line, plan);
            SeekField("line.renderer.plan." + line + ".pool", _lineRendererPlanType,
                "pool", _lineRendererPoolType, plan, pool, 0x10);
            SeekField("line.renderer.plan." + line + ".child", _lineRendererPlanType,
                "poolChild", _lineRendererChildType, plan, child, 0x18);
            SeekField("line.renderer.plan." + line + ".transform", _lineRendererPlanType,
                "poolChildTransformPlan", _geometryTransformType, plan, targetTransform, 0x20);
            SeekField("line.renderer.plan." + line + ".active", _lineRendererPlanType,
                "active", Boolean, plan, active, 0x28);
            SeekField("line.renderer.line." + line + ".plan", _noteViewLineType,
                "lineRendererPlan", _lineRendererPlanType, line, plan, 0x48);
            // A non-dynamic/group-only Line also retains this physical array.
            // Prefix writes must be observed through that alias without issuing
            // a separate SetPositions for it or replacing its saved reference.
            var sharedAlias = line == LineRendererFirstDynamic || line == groupOnly;
            var saved = sharedAlias ? LineBufferArray : IntPtr.Zero;
            var count = line == LineRendererFirstDynamic ? 2 : line == groupOnly ? 3 : 0;
            SeekField("line.renderer.line." + line + ".array", _noteViewLineType,
                "_positionsPlan", _lineBufferArrayType, line, saved, 0x88);
            SeekField("line.renderer.line." + line + ".count", _noteViewLineType,
                "_positionCountPlan", Int32, line, count, 0x90);
            Marshal.WriteIntPtr(line, 0x88, saved);
            Marshal.WriteInt32(line, 0x90, count);
        }
        CanvasSourcePointers.UnionWith(_lineRendererObjectSizes.Keys);

        _lineRendererMethodName = GameplayText("SetPositions");
        _lineRendererMethod = GameplayAllocate(Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
        Marshal.StructureToPtr(new HookTargetValidation.MethodInfo2022
        {
            MethodPointer = IntPtr.Add(syntheticImage, NativeRuntime.ExactLineSetPositionsRva),
            VirtualMethodPointer = IntPtr.Add(syntheticImage, NativeRuntime.ExactLineSetPositionsRva),
            Name = _lineRendererMethodName, Class = ClassOf(_noteViewLineType),
            ReturnType = Void, ParameterCount = 2, Flags = 0, ImplementationFlags = 0
        }, _lineRendererMethod, false);

        Add("il2cpp_class_get_nested_types", "Ptr2", nameof(LineRendererNested));
        Add("il2cpp_class_get_parent", "Ptr1", nameof(LineRendererClassParent));
        Add("il2cpp_class_is_assignable_from", "Assignable", nameof(LineRendererAssignable));
        Add("il2cpp_class_get_methods", "Ptr2", nameof(LineRendererMethods));
        Add("il2cpp_method_get_name", "Ptr1", nameof(LineRendererMethodName));
        Add("il2cpp_method_get_class", "Ptr1", nameof(LineRendererMethodClass));
        Add("il2cpp_method_get_param_count", "Flags", nameof(LineRendererParameterCount));
        Add("il2cpp_method_get_param", "ParameterType", nameof(LineRendererParameter));
        Add("il2cpp_method_get_return_type", "Ptr1", nameof(LineRendererReturn));
        Add("il2cpp_method_get_flags", "MethodFlags", nameof(LineRendererMethodFlags));
        Add("il2cpp_field_get_value", "FieldGet", nameof(LineRendererReadField));
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(LineRendererSetValue));
        Add("il2cpp_field_set_value_object", "FieldSetObject", nameof(LineRendererSetObject));
        Add("il2cpp_object_get_size", "ArraySize", nameof(LineRendererObjectSize));
        Add("il2cpp_class_instance_size", "ArraySize", nameof(LineRendererInstanceSize));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(LineRendererInvoke));
        ConfigureLineProducerControl();
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
        LineRendererNativeProfile = new LineRendererProfileValue(GameplayApi,
            GameplayApi.InspectLineSetPositionsSignature(ClassOf(_noteViewLineType), syntheticImage, () => { }));
        CanvasSourceRemember();
    }

    private IntPtr LineRendererObject(IntPtr type, uint bytes)
    {
        var value = GameplayAllocate(checked((int)bytes));
        for (var i = 0; i < bytes; ++i) Marshal.WriteByte(value, i, 0);
        Marshal.WriteIntPtr(value, ClassOf(type));
        _boxedTypes.Add(value, type); _lineRendererObjectSizes.Add(value, bytes);
        return value;
    }

    internal IntPtr LineRendererPlan(IntPtr line) => _lineRendererPlans[line];
    internal IntPtr LineRendererSavedArray(IntPtr line) => Marshal.ReadIntPtr(line, 0x88);
    internal int LineRendererSavedCount(IntPtr line) => Marshal.ReadInt32(line, 0x90);
    internal void LineRendererSetSaved(IntPtr line, IntPtr array, int count)
    {
        Marshal.WriteIntPtr(line, 0x88, array); Marshal.WriteInt32(line, 0x90, count);
        TargetSet(line, "_positionsPlan", array); TargetSet(line, "_positionCountPlan", count);
    }
    internal void LineRendererSetActive(IntPtr line, bool active)
        => TargetSet(_lineRendererPlans[line], "active", active);
    internal void LineRendererSetWrongComponent(IntPtr line)
        => TargetSet(SeekValue<IntPtr>(_lineRendererPlans[line], "poolChild"),
            "targetComponent", GeometryCamera);

    private IntPtr LineRendererNested(IntPtr owner, IntPtr iterator)
    {
        if (owner != ClassOf(_noteViewLineType)) return SeekNested(owner, iterator);
        if (Marshal.ReadIntPtr(iterator) != IntPtr.Zero) return IntPtr.Zero;
        Marshal.WriteIntPtr(iterator, new IntPtr(1)); return ClassOf(_lineRendererPlanType);
    }
    private IntPtr LineRendererClassParent(IntPtr cls)
        => cls == ClassOf(_lineRendererUnityType) ? ClassOf(_lineRendererComponentType) : CanvasClassParent(cls);
    private bool LineRendererAssignable(IntPtr target, IntPtr source)
    {
        for (var cls = source; cls != IntPtr.Zero; cls = LineRendererClassParent(cls))
            if (cls == target) return true;
        return GeometryAssignable(target, source);
    }
    private IntPtr LineRendererMethods(IntPtr cls, IntPtr iterator)
    {
        if (cls != ClassOf(_noteViewLineType)) return GeometryMethods(cls, iterator);
        if (Marshal.ReadIntPtr(iterator) != IntPtr.Zero) return IntPtr.Zero;
        Marshal.WriteIntPtr(iterator, new IntPtr(1)); return _lineRendererMethod;
    }
    private IntPtr LineRendererMethodName(IntPtr method)
        => method == _lineRendererMethod ? _lineRendererMethodName : GameplayMethodName(method);
    private IntPtr LineRendererMethodClass(IntPtr method)
        => method == _lineRendererMethod ? ClassOf(_noteViewLineType) : GameplayMethodClass(method);
    private uint LineRendererParameterCount(IntPtr method)
        => method == _lineRendererMethod ? 2u : GameplayParamCount(method);
    private IntPtr LineRendererParameter(IntPtr method, uint index)
        => method == _lineRendererMethod ? index == 0 ? _lineBufferArrayType : Int32 : GameplayParam(method, index);
    private IntPtr LineRendererReturn(IntPtr method)
        => method == _lineRendererMethod ? Void : GameplayReturn(method);
    private uint LineRendererMethodFlags(IntPtr method, out uint implementation)
    {
        if (method != _lineRendererMethod) return GameplayFlags(method, out implementation);
        implementation = 0; return 0;
    }
    private void LineRendererReadField(IntPtr owner, IntPtr field, IntPtr output)
    {
        var name = _fieldMetadata[field].Name;
        if (name is "_positionsPlan" or "_positionCountPlan" && _lineRendererObjectSizes.ContainsKey(owner))
        {
            ++FieldReads; AllGameplayReceiversRooted &= HasRootFor(owner);
            if (name == "_positionsPlan") Marshal.WriteIntPtr(output, Marshal.ReadIntPtr(owner, 0x88));
            else Marshal.WriteInt32(output, Marshal.ReadInt32(owner, 0x90));
            CanvasSourceReads.Add((name, owner)); AfterCanvasSourceRead?.Invoke(name, owner);
            return;
        }
        CanvasReadField(owner, field, output);
    }
    private void LineRendererSetValue(IntPtr owner, IntPtr field, IntPtr input)
    {
        if (_lineRendererObjectSizes.ContainsKey(owner) && _fieldMetadata[field].Name == "_positionCountPlan")
        {
            if (!HasRootFor(owner)) throw new InvalidOperationException("Unrooted saved count receiver.");
            var count = Marshal.ReadInt32(input);
            Marshal.WriteInt32(owner, 0x90, count); TargetSet(owner, "_positionCountPlan", count);
            LineRendererInverseStores.Add((owner, "_positionCountPlan", count)); ++ValueFieldWrites;
            return;
        }
        CanvasCacheWriteScalar(owner, field, input);
    }
    private void LineRendererSetObject(IntPtr owner, IntPtr field, IntPtr value)
    {
        if (_lineRendererObjectSizes.ContainsKey(owner) && _fieldMetadata[field].Name == "_positionsPlan")
        {
            if (!HasRootFor(owner) || value != IntPtr.Zero && !HasRootFor(value))
                throw new InvalidOperationException("Unrooted saved array inverse.");
            Marshal.WriteIntPtr(owner, 0x88, value); TargetSet(owner, "_positionsPlan", value);
            LineRendererInverseStores.Add((owner, "_positionsPlan", value.ToInt64())); ++ObjectFieldWrites;
            return;
        }
        CanvasRejectObject(owner, field, value);
    }
    private uint LineRendererObjectSize(IntPtr value)
        => _lineRendererObjectSizes.TryGetValue(value, out var bytes) ? bytes : LineBufferObjectSize(value);
    private uint LineRendererInstanceSize(IntPtr cls)
        => cls == ClassOf(_noteViewLineType) ? 0x98u
            : cls == ClassOf(_lineRendererPlanType) || cls == ClassOf(_lineRendererChildType)
                || cls == ClassOf(_lineRendererPoolType) ? 0x40u : CursorInstanceSize(cls);
    private IntPtr LineRendererInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception)
    {
        if (method != _lineRendererMethod) return LineRingSourceInvoke(method, receiver, arguments, exception);
        if (!_lineRendererPlans.TryGetValue(receiver, out var plan))
            throw new InvalidOperationException("A foreign synthetic line reached SetPositions.");
        var array = Marshal.ReadIntPtr(arguments);
        var count = Marshal.ReadInt32(Marshal.ReadIntPtr(arguments, IntPtr.Size));
        LineRendererArgumentsRooted &= HasRootFor(receiver) && HasRootFor(array);
        if (array != LineBufferArray || count < 0 || count > _lineBufferLengths[array])
            throw new InvalidOperationException("SetPositions substituted the original shared scratch or count.");
        var active = TargetBoolean(plan, "active");
        ++Invocations;
        LineRendererSetSaved(receiver, array, count);
        var call = new LineRendererNativeCall(LineRendererNativeCalls.Count + 1, receiver, array,
            count, LineBufferReadBits(array), active);
        LineRendererNativeCalls.Add(call);
        if (active)
        {
            var child = SeekValue<IntPtr>(plan, "poolChild");
            var target = SeekValue<IntPtr>(child, "targetComponent");
            if (ObjectClass(target) != ClassOf(_lineRendererUnityType)
                || SeekValue<IntPtr>(child, "parentPool") != SeekValue<IntPtr>(plan, "pool")
                || GeometryComponentTransforms[target] != SeekValue<IntPtr>(plan, "poolChildTransformPlan"))
                throw new InvalidOperationException("The active target is not the exact UnityEngine.LineRenderer child.");
            ++LineRendererUnityCalls;
        }
        AfterLineRendererFieldsSaved?.Invoke(call);
        Marshal.WriteIntPtr(exception, LineRendererExceptionAfterFields ? new IntPtr(1) : IntPtr.Zero);
        return IntPtr.Zero;
    }

    private sealed class LineRendererProfileValue : ISeekLineSetPositionsNativeProfile
    {
        private readonly NativeRuntime _api;
        private readonly NativeLineSetPositionsSignature _signature;
        internal LineRendererProfileValue(NativeRuntime api, NativeLineSetPositionsSignature signature)
            => (_api, _signature) = (api, signature);
        public IntPtr LineClass => _signature.LineClass;
        public IntPtr Vector3ArrayClass => _signature.ArrayClass;
        public IntPtr SetPositionsMethod => _signature.Method.MethodInfo;
        public void VerifyCurrent(Action guard)
        {
            guard();
            var current = _api.InspectLineSetPositionsSignature(LineClass, _signature.Module, guard);
            if (current.LineClass != _signature.LineClass
                || current.ArrayClass != _signature.ArrayClass
                || current.VectorClass != _signature.VectorClass
                || current.CountClass != _signature.CountClass
                || current.Module != _signature.Module
                || current.Method.MethodInfo != _signature.Method.MethodInfo
                || current.Method.Entry != _signature.Method.Entry)
                throw new UnsupportedSignatureException("The synthetic exact SetPositions signature changed.");
            guard();
        }
        public void InvokeExact(NativeRuntime.RootHandle line, NativeRuntime.RootHandle scratch,
            int count, Action guard, Action verifySource, Action beforeEntry)
            => _api.InvokeLineSetPositionsWithPublicationAdmission(_signature, line, scratch, count,
                guard, () => VerifyCurrent(guard), verifySource, beforeEntry);
    }
}
