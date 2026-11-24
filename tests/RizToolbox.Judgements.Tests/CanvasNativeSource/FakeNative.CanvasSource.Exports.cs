using System.Runtime.InteropServices;
using System.Linq.Expressions;
using System.Reflection;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal IntPtr CanvasSourceFaultArray;
    internal string? CanvasSourceArrayFault;
    internal bool CanvasSourceAuditExports;
    internal readonly List<string> CanvasSourceExportLog = new();
    private void CanvasSourceObserveExport(string name)
    { if (CanvasSourceAuditExports) CanvasSourceExportLog.Add(name); }
    private void CanvasSourceInstrumentExports()
    {
        foreach (var row in _exports.ToArray())
        {
            var type = row.Value.GetType();
            var args = type.GetMethod("Invoke")!.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            var observe = Expression.Call(Expression.Constant(this), GetType().GetMethod(nameof(CanvasSourceObserveExport), BindingFlags.NonPublic | BindingFlags.Instance)!, Expression.Constant(row.Key));
            _exports[row.Key] = Expression.Lambda(type, Expression.Block(observe, Expression.Invoke(Expression.Constant(row.Value, type), args)), args).Compile();
        }
    }
    private bool CanvasFaultClass(IntPtr cls) => CanvasSourceFaultArray != IntPtr.Zero && cls == ClassOf(_boxedTypes[CanvasSourceFaultArray]);
    private void ConfigureCanvasSourceExports()
    {
        Add("il2cpp_field_get_value", "FieldGet", nameof(CanvasReadField));
        Add("il2cpp_field_static_get_value", "StaticGet", nameof(CanvasReadStatic));
        Add("il2cpp_object_get_class", "Ptr1", nameof(CanvasObjectClass));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(CanvasInvoke));
        Add("il2cpp_gchandle_new", "NewHandle", nameof(CanvasRoot));
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(CanvasRejectValue));
        Add("il2cpp_field_set_value_object", "FieldSetObject", nameof(CanvasRejectObject));
        Add("il2cpp_class_get_name", "Ptr1", nameof(CanvasClassName));
        Add("il2cpp_class_get_namespace", "Ptr1", nameof(CanvasNamespace));
        Add("il2cpp_class_get_parent", "Ptr1", nameof(CanvasClassParent));
        Add("il2cpp_class_is_inflated", "Predicate", nameof(CanvasInflated));
        Add("il2cpp_class_is_generic", "Predicate", nameof(CanvasGeneric));
        Add("il2cpp_class_get_method_from_name", "MethodLookup", nameof(CanvasNamedMethod));
        Add("il2cpp_class_get_rank", "Flags", nameof(CanvasArrayRank));
        Add("il2cpp_class_get_element_class", "Ptr1", nameof(CanvasArrayElement));
        Add("il2cpp_array_element_size", "ArrayElementSize", nameof(CanvasArrayWidth));
        Add("il2cpp_array_length", "ArraySize", nameof(CanvasArrayLength));
        Add("il2cpp_array_get_byte_length", "ArraySize", nameof(CanvasArrayBytes));
        Add("il2cpp_object_get_size", "ArraySize", nameof(CanvasArraySize));
        Add("il2cpp_array_object_header_size", "ReferenceArrayHeaderSize", nameof(CanvasArrayHeader));
        CanvasSourceInstrumentExports();
    }
    private void CanvasReadField(IntPtr owner, IntPtr field, IntPtr output)
    {
        var name = _fieldMetadata[field].Name;
        if (CanvasSourceNoHold && (owner == TargetHold || name is "holdControl" or "holdNotes" or "runningHoldNotes"))
        { ++CanvasSourceForbidden; throw new InvalidOperationException("Canvas source may not depend on Hold containers."); }
        CanvasSourceReads.Add((name, owner)); TargetReadField(owner, field, output); AfterCanvasSourceRead?.Invoke(name, owner);
    }
    private void CanvasReadStatic(IntPtr field, IntPtr output)
    {
        var name = _fieldMetadata[field].Name;
        if (name is "downVector" or "rightVector")
        {
            if (_fieldMetadata[field].Type != _geometryVectorType || _types[_geometryVectorType].Size != 12)
            { ++CanvasSourceForbidden; throw new InvalidOperationException("Bad static Vector3 metadata reached a native output."); }
        }
        CanvasSourceReads.Add((name, IntPtr.Zero)); GeometryStaticRead(field, output); AfterCanvasSourceRead?.Invoke(name, IntPtr.Zero);
    }
    private IntPtr CanvasObjectClass(IntPtr value)
    {
        if (CanvasSourcePointers.Contains(value)) CanvasSourcePublished &= HasRootFor(value);
        return GeometryObjectClass(value);
    }
    private uint CanvasRoot(IntPtr pointer, bool pinned)
    {
        var handle = GeometryRoot(pointer, pinned);
        AfterCanvasSourceRoot?.Invoke(handle, pointer, pinned); return handle;
    }
    private IntPtr CanvasClassName(IntPtr cls) => cls == ClassOf(_seekKeyListType) ? GameplayText("List`1") : JudgeClassName(cls);
    private IntPtr CanvasNamespace(IntPtr cls) => cls == ClassOf(_seekKeyListType) ? GameplayText("System.Collections.Generic") : JudgeNamespace(cls);
    private IntPtr CanvasClassParent(IntPtr cls) => cls == ClassOf(_seekKeyListType) ? ClassOf(Object) : GeometryClassParent(cls);
    private bool CanvasInflated(IntPtr cls) => cls == ClassOf(_seekKeyListType) || JudgeClassInflated(cls);
    private bool CanvasGeneric(IntPtr cls) => cls == ClassOf(_seekKeyListType) ? false : JudgeClassGeneric(cls);
    private IntPtr CanvasNamedMethod(IntPtr cls, string name, int count)
    {
        if (name.StartsWith("GetEase", StringComparison.Ordinal))
        { ++CanvasSourceForbidden; throw new InvalidOperationException("Capture must not resolve an easing initializer or consumer."); }
        return GeometryNamedMethod(cls, name, count);
    }
    private IntPtr CanvasInvoke(IntPtr method, IntPtr receiver, IntPtr args, IntPtr exception)
    {
        if (method == CanvasSourceProfile.Range)
        {
            ++CanvasSourceRangeAttempts;
            var row = _gameplayMethods[method];
            if (row.Static || row.Name != "ChartTimeRageToRealTime" || row.Class != CanvasSourceProfile.ProgressClass || row.Return != Single
                || !row.Parameters.SequenceEqual(new[] { _gameplayDouble, _gameplayDouble }))
            { ++CanvasSourceForbidden; throw new InvalidOperationException("Wrong range metadata entered runtime_invoke."); }
            ++Invocations; AllGameplayReceiversRooted &= receiver == GameplayProgress && HasRootFor(receiver);
            Marshal.WriteIntPtr(exception, IntPtr.Zero);
            var from = Marshal.PtrToStructure<double>(Marshal.ReadIntPtr(args));
            var to = Marshal.PtrToStructure<double>(Marshal.ReadIntPtr(args, IntPtr.Size));
            var result = CanvasSourceRangeResult ?? (float)((to - from) * .5d + .25d);
            CanvasSourceRanges.Add((from, to, result)); var boxed = Box(Single, BitConverter.GetBytes(result));
            AfterCanvasSourceRange?.Invoke();
            if (CanvasSourceRangeException != IntPtr.Zero)
            { Marshal.WriteIntPtr(exception, CanvasSourceRangeException); ++CanvasSourceRangeExceptionWrites; }
            return boxed;
        }
        if (IsArrayMethod(method) && receiver == CanvasSourceFaultArray)
        {
            if (method == _arrayLength && CanvasSourceArrayFault is "length" or "short")
            { ++Invocations; return Box(Int32, BitConverter.GetBytes(CanvasSourceArrayFault == "length" ? 65537 : _referenceArrays[receiver].Length - 1)); }
            if (method == _arrayRank && CanvasSourceArrayFault == "rank")
            { ++Invocations; return Box(Int32, BitConverter.GetBytes(2)); }
        }
        if (_gameplayMethods.TryGetValue(method, out var listMethod) && listMethod.Class == ClassOf(_seekKeyListType))
        {
            ++Invocations; AllGameplayReceiversRooted &= HasRootFor(receiver); Marshal.WriteIntPtr(exception, IntPtr.Zero);
            var size = TargetInt(receiver, "_size");
            if (listMethod.Name == "get_Count")
            { CanvasSourceCountReads.Add(receiver); return Box(Int32, BitConverter.GetBytes(size + CanvasSourceCountBias)); }
            if (listMethod.Name == "get_Item")
            {
                var index = Marshal.ReadInt32(Marshal.ReadIntPtr(args)); var backing = CanvasSourceBacking(receiver);
                if ((uint)index >= (uint)size || index >= _referenceArrays[backing].Length)
                    throw new InvalidOperationException("Canvas KeyPoint list getter left the actual active prefix.");
                var value = _canvasSourceItemOverrides.TryGetValue((receiver, index), out var supplied) ? supplied : _referenceArrays[backing][index];
                CanvasSourceItemReads.Add((receiver, index, value)); return value;
            }
        }
        if (_geometryMethods.Values.Contains(method) || _gameplayMethods.TryGetValue(method, out var m)
            && (m.Name.StartsWith("GetEase", StringComparison.Ordinal) || m.Name is "Update" or "GetInformations" or "SetPosition"))
        { ++CanvasSourceForbidden; throw new InvalidOperationException("Canvas capture entered a Transform/initialization/consumer method."); }
        return GeometryInvoke(method, receiver, args, exception);
    }
    private uint CanvasArrayRank(IntPtr cls) => CanvasFaultClass(cls) && CanvasSourceArrayFault == "rank" ? 2u : GeometryArrayRank(cls);
    private IntPtr CanvasArrayElement(IntPtr cls) => CanvasFaultClass(cls) && CanvasSourceArrayFault == "element" ? ClassOf(Object) : GeometryArrayElement(cls);
    private int CanvasArrayWidth(IntPtr cls) => CanvasFaultClass(cls) && CanvasSourceArrayFault == "width" ? GeometryArrayWidth(cls) + 4 : GeometryArrayWidth(cls);
    private uint CanvasArrayLength(IntPtr array) => array == CanvasSourceFaultArray && CanvasSourceArrayFault == "length" ? 65537u : GeometryArrayLength(array);
    private uint CanvasArrayBytes(IntPtr array) => GeometryArrayBytes(array) + (array == CanvasSourceFaultArray && CanvasSourceArrayFault == "bytes" ? 1u : 0u);
    private uint CanvasArrayHeader() => CanvasSourceArrayFault == "header-size" ? 17u : GeometryArrayHeader();
    private uint CanvasArraySize(IntPtr array) => GeometryArraySize(array) + (array == CanvasSourceFaultArray && CanvasSourceArrayFault == "size" ? 1u : 0u);
    private void CanvasRejectValue(IntPtr owner, IntPtr field, IntPtr value)
    { ++CanvasSourceForbidden; throw new InvalidOperationException("Read-only Canvas source entered scalar setter."); }
    private void CanvasRejectObject(IntPtr owner, IntPtr field, IntPtr value)
    { ++CanvasSourceForbidden; throw new InvalidOperationException("Read-only Canvas source entered reference setter."); }
    internal void CanvasSourceMetadata(IntPtr owner, string name, string mode)
    {
        var f = owner == IntPtr.Zero ? _gameplayFields[name == "EaseInfos" ? "geometry.ease.static" : "canvas.source.vector." + name].Field : TargetField(owner, name);
        var row = _fieldMetadata[f];
        switch (mode)
        {
            case "offset": _fieldMetadata[f] = (row.Name, row.Type, row.Offset + 4); break;
            case "static": _judgeFlags[f] = owner == IntPtr.Zero ? 0u : 0x10u; break;
            case "readonly": _judgeFlags[f] = 0x10u; break;
            case "literal": _judgeFlags[f] = (owner == IntPtr.Zero ? 0x10u : 0u) | 0x40u; break;
            case "type": _fieldMetadata[f] = (row.Name, row.Type == Single ? Int32 : Single, row.Offset); break;
            case "byref": _fieldMetadata[f] = (row.Name, AddType(_types[row.Type].Name, _types[row.Type].Code, _types[row.Type].ValueType, _types[row.Type].Size, byRef: true), row.Offset); break;
            case "width": _types[row.Type] = _types[row.Type] with { Size = _types[row.Type].Size + 4 }; break;
            case "parent": _judgeFieldParents[f] = ClassOf(Object); break;
            case "flags": _judgeFlags[f] = (owner == IntPtr.Zero ? 0x30u : 0u) | 1u; break;
            default: throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
    internal void CanvasSourceRangeMetadata(string mode)
    {
        var p = CanvasSourceProfile.Range; var row = _gameplayMethods[p];
        _gameplayMethods[p] = mode switch
        {
            "static" => row with { Static = true }, "owner" => row with { Class = ClassOf(Object) },
            "return" => row with { Return = Int32 }, "parameters" => row with { Parameters = new[] { Single, _gameplayDouble } },
            "arity" => row with { Parameters = new[] { _gameplayDouble } },
            "name" => row with { Name = "ChartTimeToRealTime" },
            "byref-parameter" => row with { Parameters = new[] { AddType("System.Double", 13, true, 8, byRef: true), _gameplayDouble } },
            "byref-return" => row with { Return = AddType("System.Single", 12, true, 4, byRef: true) },
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }
    internal void CanvasSourceListMethodMetadata(string name, string mode)
    {
        var p = _gameplayMethods.Single(row => row.Value.Class == ClassOf(_seekKeyListType) && row.Value.Name == name).Key;
        var row = _gameplayMethods[p];
        _gameplayMethods[p] = mode switch
        {
            "static" => row with { Static = true }, "owner" => row with { Class = ClassOf(Object) },
            "return" => row with { Return = name == "get_Count" ? Single : Object },
            "parameters" => row with { Parameters = name == "get_Count" ? new[] { Int32 } : new[] { Single } },
            "byref-return" => row with { Return = AddType(_types[row.Return].Name, _types[row.Return].Code, _types[row.Return].ValueType, _types[row.Return].Size, byRef: true) },
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }
    internal void CanvasSourceFault(IntPtr array, string mode)
    {
        CanvasSourceFaultArray = array; CanvasSourceArrayFault = mode;
        if (mode == "header") Marshal.WriteIntPtr(array, ClassOf(Object));
        CanvasSourceRemember();
    }
    internal void CanvasSourceReplaceList(int move, bool speed)
    {
        var old = CanvasSourceList(move, speed); var points = _referenceArrays[CanvasSourceBacking(old)].ToArray();
        CanvasSourceSet(CanvasSourceMoves[move], speed ? "speedKeyPoints" : "xPositionKeyPoints", CanvasList(points));
    }
}
