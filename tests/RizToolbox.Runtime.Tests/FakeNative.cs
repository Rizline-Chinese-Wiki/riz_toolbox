using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Runtime;

// Real unmanaged buffers, synthetic metadata/export delegates. No game, injection, or platform DLL is loaded.
sealed partial class FakeNative : IDisposable
{
    internal sealed record TypeInfo(string Name, int Code, bool ValueType, int Size, bool ByRef = false, IntPtr EnumBase = default);
    private readonly Dictionary<IntPtr, TypeInfo> _types = new();
    private readonly Dictionary<IntPtr, IntPtr> _classes = new();
    private readonly Dictionary<string, Delegate> _exports = new();
    private int _lowerIl2CppAuditEnabled, _lowerIl2CppOwnerThread;
    internal long LowerIl2CppCalls, LowerWorkerIl2CppCalls;
    internal void BeginLowerIl2CppAudit()
    {
        _lowerIl2CppOwnerThread = Environment.CurrentManagedThreadId;
        Interlocked.Exchange(ref LowerIl2CppCalls, 0); Interlocked.Exchange(ref LowerWorkerIl2CppCalls, 0);
        Volatile.Write(ref _lowerIl2CppAuditEnabled, 1);
    }
    internal void EndLowerIl2CppAudit() => Volatile.Write(ref _lowerIl2CppAuditEnabled, 0);
    private void ObserveLowerIl2CppExport()
    {
        if (Volatile.Read(ref _lowerIl2CppAuditEnabled) == 0) return;
        Interlocked.Increment(ref LowerIl2CppCalls);
        if (Environment.CurrentManagedThreadId != _lowerIl2CppOwnerThread) Interlocked.Increment(ref LowerWorkerIl2CppCalls);
    }
    private readonly Dictionary<uint, IntPtr> _handles = new();
    private readonly Dictionary<IntPtr,List<IntPtr>> _structFields = new();
    private readonly Dictionary<IntPtr,(string Name,IntPtr Type,int Offset)> _fieldMetadata = new();
    private readonly Dictionary<IntPtr,IntPtr> _boxedTypes = new();
    private readonly Dictionary<IntPtr,uint> _arrays = new();
    internal int ArrayHeaderBytes = 40, ArrayAllocations, PinnedRoots, ArrayElementBytes = 4;
    internal bool CorruptArrayByteLength, CorruptArrayObjectSize, ReturnWrongArrayClass;
    internal IntPtr LastArray;
    private IntPtr _singleArrayType;
    private readonly List<IntPtr> _allocations = new();
    private uint _nextHandle;
    internal readonly IntPtr Void, Boolean, Int32, Single, Object, String, Receiver, UnknownStruct;
    internal readonly IntPtr Method = new(20), Field = new(30), Instance = new(40), OtherObject = new(41);
    internal IntPtr FieldType, ReturnType;
    internal IntPtr[] Parameters = Array.Empty<IntPtr>();
    internal bool IsStatic, StaticField, ThrowOnInvoke, GenericMethod, InflatedMethod;
    internal bool NamedMethodAvailable;
    internal int FieldReads, Invocations, NewRoots, FreedRoots, Unboxes;
    internal uint FailFreeHandle = 0;
    internal int FreeHandleAttempts;
    internal Action<IntPtr>? DuringHandleTarget;
    internal byte[] FieldBytes = { 0 };
    internal IntPtr ExceptionOnInvoke, Result = IntPtr.Zero;
    internal NativeRuntime Api { get; }
    internal int ActiveRoots => _handles.Count;
    internal FakeNative()
    {
        Void = AddType("System.Void", 1, true, 0); Boolean = AddType("System.Boolean", 2, true, 1);
        Int32 = AddType("System.Int32", 8, true, 4); Single = AddType("System.Single", 12, true, 4);
        Object = AddType("System.Object", 28, false, IntPtr.Size); String = AddType("System.String", 14, false, IntPtr.Size);
        Receiver = AddType("Fake.Receiver", 18, false, IntPtr.Size); UnknownStruct = AddType("Fake.LargeStruct", 17, true, 128);
        FieldType = Boolean; ReturnType = Void;
        Add("il2cpp_domain_get", "Ptr0"); Add("il2cpp_domain_get_assemblies", "Ptr2");
        Add("il2cpp_assembly_get_image", "Ptr1"); Add("il2cpp_image_get_name", "Ptr1");
        Add("il2cpp_object_get_class", "Ptr1", nameof(ObjectClass)); Add("il2cpp_class_get_parent", "Ptr1", nameof(ClassParent));
        Add("il2cpp_class_get_name", "Ptr1"); Add("il2cpp_class_get_namespace", "Ptr1");
        Add("il2cpp_method_get_name", "Ptr1", nameof(MethodName)); Add("il2cpp_method_get_class", "Ptr1", nameof(MethodClass));
        Add("il2cpp_object_unbox", "Ptr1", nameof(Unbox)); Add("il2cpp_object_new", "Ptr1", nameof(NewObject));
        Add("il2cpp_class_get_type", "Ptr1", nameof(ClassType)); Add("il2cpp_type_get_object", "Ptr1");
        Add("il2cpp_class_from_name", "ClassLookup"); Add("il2cpp_class_get_method_from_name", "MethodLookup");
        Add("il2cpp_class_get_field_from_name", "FieldLookup", nameof(FindField));
        Add("il2cpp_field_get_value", "FieldGet", nameof(ReadField)); Add("il2cpp_field_static_get_value", "StaticGet", nameof(ReadStatic));
        Add("il2cpp_field_get_parent", "Ptr1", nameof(FieldParent));
        Add("il2cpp_field_set_value_object", "FieldSetObject", nameof(SetFieldObject));
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(SetFieldValue));
        Add("il2cpp_field_get_flags", "Flags", nameof(FieldFlags)); Add("il2cpp_class_get_methods", "Ptr2", nameof(ClassMethods));
        Add("il2cpp_method_get_param_count", "Flags", nameof(ParameterCount));
        Add("il2cpp_method_get_flags", "MethodFlags", nameof(MethodFlags));
        Add("il2cpp_method_get_param", "ParameterType", nameof(ParameterType)); Add("il2cpp_type_get_name", "Ptr1", nameof(TypeName));
        Add("il2cpp_free", "FreePointer", nameof(Free));
        Add("il2cpp_gchandle_new", "NewHandle", nameof(NewHandle)); Add("il2cpp_gchandle_free", "FreeHandle", nameof(FreeHandle));
        Add("il2cpp_string_new", "StringNew", nameof(MakeString)); Add("il2cpp_string_length", "StringLength", nameof(ReadStringLength)); Add("il2cpp_string_chars", "Ptr1", nameof(ReadStringCharacters));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(Invoke));
        Add("il2cpp_field_get_type", "Ptr1", nameof(GetFieldType)); Add("il2cpp_method_get_return_type", "Ptr1", nameof(GetReturnType));
        Add("il2cpp_class_from_type", "Ptr1", nameof(TypeClass)); Add("il2cpp_class_enum_basetype", "Ptr1", nameof(EnumBase));
        Add("il2cpp_type_is_byref", "Predicate", nameof(ByRef)); Add("il2cpp_class_is_valuetype", "Predicate", nameof(IsValueType));
        Add("il2cpp_class_is_enum", "Predicate", nameof(IsEnum)); Add("il2cpp_class_is_assignable_from", "Assignable", nameof(Assignable));
        Add("il2cpp_class_value_size", "ValueSize", nameof(ValueSize)); Add("il2cpp_type_get_type", "TypeCode", nameof(TypeCode));
        Add("il2cpp_class_get_fields", "Ptr2", nameof(ClassFields)); Add("il2cpp_field_get_name", "Ptr1", nameof(FieldName)); Add("il2cpp_field_get_offset", "FieldOffset", nameof(FieldOffset));
        Add("il2cpp_gchandle_get_target", "HandleTarget", nameof(HandleTarget));
        Add("il2cpp_method_is_generic", "Predicate", nameof(IsGenericMethod)); Add("il2cpp_method_is_inflated", "Predicate", nameof(IsInflatedMethod));
        Add("il2cpp_array_new", "ArrayNew", nameof(NewArray)); Add("il2cpp_array_class_get", "ArrayClassGet", nameof(ArrayClass));
        Add("il2cpp_array_element_size", "ArrayElementSize", nameof(ElementSize));
        Add("il2cpp_array_length", "ArraySize", nameof(ArrayLength)); Add("il2cpp_array_get_byte_length", "ArraySize", nameof(ArrayBytes));
        Add("il2cpp_object_get_size", "ArraySize", nameof(ObjectSize));
        Api = new NativeRuntime(ExportPointer); Api.BindCurrentThread();
    }
    internal IntPtr AddType(string name, int code, bool valueType, int size, bool byRef = false, IntPtr enumBase = default)
    {
        var type = new IntPtr(100 + _types.Count); _types[type] = new(name, code, valueType, size, byRef, enumBase);
        _classes[new IntPtr(type.ToInt64() + 1000)] = type; return type;
    }
    internal IntPtr AddStruct(string name,int size,params NativeFieldLayout[] fields)
    {
        var type=AddType(name,17,true,size);var list=new List<IntPtr>();_structFields[TypeClass(type)]=list;
        foreach(var field in fields){var id=new IntPtr(10000+_fieldMetadata.Count);list.Add(id);
            _fieldMetadata[id]=(field.Name,_types.First(t=>t.Value.Name==field.TypeName).Key,field.Offset+2*IntPtr.Size);}
        return type;
    }
    internal IntPtr Box(IntPtr type,byte[] value)
    {
        var memory=Marshal.AllocHGlobal(Math.Max(1,value.Length));_allocations.Add(memory);Marshal.Copy(value,0,memory,value.Length);_boxedTypes[memory]=type;return memory;
    }
    private readonly Dictionary<IntPtr, string> _strings = new();
    internal int? StringLengthOverride;
    internal int StringLengthReads, StringCharacterReads;
    internal IntPtr MakeString(string value)
    {
        var memory = Marshal.StringToHGlobalUni(value); _allocations.Add(memory);
        _strings[memory] = value; _boxedTypes[memory] = String; return memory;
    }
    private int ReadStringLength(IntPtr value) { StringLengthReads++; return StringLengthOverride ?? _strings[value].Length; }
    private IntPtr ReadStringCharacters(IntPtr value) { StringCharacterReads++; return value; }
    private IntPtr TypeClass(IntPtr type) => new(type.ToInt64() + 1000);
    private IntPtr ClassType(IntPtr cls) => _classes[cls];
    private IntPtr ObjectClass(IntPtr obj) => TypeClass(_boxedTypes.TryGetValue(obj,out var boxed)?boxed:obj == Instance ? Receiver : String);
    private IntPtr Unbox(IntPtr obj){Unboxes++;return obj;}
    private IntPtr MethodClass(IntPtr method) => method == _constructor ? ConstructorClass : IsArrayMethod(method) ? TypeClass(_referenceArrayType) : TypeClass(Receiver);
    private IntPtr FindField(IntPtr cls, string name) => Field;
    private uint FieldFlags(IntPtr field) => field==Field ? (StaticField ? 0x10u : 0) | ExtraFieldFlags : 0;
    private IntPtr GetFieldType(IntPtr field) => field==Field?FieldType:_fieldMetadata[field].Type;
    private IntPtr ClassFields(IntPtr cls,IntPtr iterator){
        if(!_structFields.TryGetValue(cls,out var fields))return IntPtr.Zero;
        var index=Marshal.ReadIntPtr(iterator).ToInt32();if(index>=fields.Count)return IntPtr.Zero;
        Marshal.WriteIntPtr(iterator,new IntPtr(index+1));return fields[index];
    }
    private IntPtr FieldName(IntPtr field){var name=Marshal.StringToHGlobalAnsi(_fieldMetadata[field].Name);_allocations.Add(name);return name;}
    private int FieldOffset(IntPtr field)=>_fieldMetadata[field].Offset;
    private void ReadField(IntPtr obj, IntPtr field, IntPtr output) { FieldReads++; Marshal.Copy(FieldBytes, 0, output, FieldBytes.Length); }
    private void ReadStatic(IntPtr field, IntPtr output) => ReadField(IntPtr.Zero, field, output);
    private uint ParameterCount(IntPtr method) => IsArrayMethod(method) ? method == _arrayGet ? 1u : 0u : (uint)Parameters.Length;
    private uint MethodFlags(IntPtr method, out uint implementation) { implementation = 0; return (IsStatic && !IsArrayMethod(method) ? 0x10u : 0) | (method == _constructor ? ConstructorExtraFlags : 0); }
    private IntPtr ParameterType(IntPtr method, uint index) => method == _arrayGet ? Int32 : Parameters[index];
    private IntPtr GetReturnType(IntPtr method) => IsArrayMethod(method) ? method == _arrayGet ? Object : Int32 : ReturnType;
    private IntPtr TypeName(IntPtr type) => Marshal.StringToCoTaskMemUTF8(_types[type].Name);
    private void Free(IntPtr pointer) => Marshal.FreeCoTaskMem(pointer);
    private bool ByRef(IntPtr type) => _types[type].ByRef;
    private bool IsValueType(IntPtr cls) => _types[_classes[cls]].ValueType;
    private bool IsEnum(IntPtr cls) => _types[_classes[cls]].EnumBase != IntPtr.Zero;
    private bool IsGenericMethod(IntPtr method)=>GenericMethod;
    private bool IsInflatedMethod(IntPtr method)=>InflatedMethod;
    private IntPtr EnumBase(IntPtr cls) => _types[_classes[cls]].EnumBase;
    private int ValueSize(IntPtr cls, out uint alignment) { alignment = 1; return _types[_classes[cls]].Size; }
    private int TypeCode(IntPtr type) => _types[type].Code;
    private bool Assignable(IntPtr target, IntPtr source) => target == source || target == TypeClass(Object);
    private uint NewHandle(IntPtr obj, bool pinned) { var id = ++_nextHandle; _handles[id] = obj; NewRoots++; if(pinned)PinnedRoots++; return id; }
    internal IntPtr ClassOf(IntPtr type) => TypeClass(type);
    private IntPtr ArrayClass(IntPtr element, uint rank)
    {
        if (_singleArrayType == IntPtr.Zero) _singleArrayType = AddType("System.Single[]", 29, false, IntPtr.Size);
        return TypeClass(_singleArrayType);
    }
    private int ElementSize(IntPtr cls) => ArrayElementBytes;
    private IntPtr NewArray(IntPtr cls, ulong count)
    {
        var length=checked((uint)count); var p=Marshal.AllocHGlobal(checked(ArrayHeaderBytes+(int)length*4+8));_allocations.Add(p);
        for(int i=0;i<ArrayHeaderBytes+(int)length*4+8;i++)Marshal.WriteByte(p,i,0x5a);
        _boxedTypes[p]=ReturnWrongArrayClass?Object:_singleArrayType;_arrays[p]=length;ArrayAllocations++;LastArray=p;return p;
    }
    private uint ArrayLength(IntPtr obj)=>_arrays[obj];
    private uint ArrayBytes(IntPtr obj)=>checked(_arrays[obj]*4+(CorruptArrayByteLength&&_arrays[obj]>0?1u:0u));
    private uint ObjectSize(IntPtr obj)=>checked((uint)ArrayHeaderBytes+_arrays[obj]*4+(CorruptArrayObjectSize&&_arrays[obj]>0?4u:0u));
    private void FreeHandle(uint id) {
        _handles.Remove(id); FreedRoots++; FreeHandleAttempts++;
        if(id==FailFreeHandle)throw new IOException("Synthetic unknown GC-root free outcome.");
    }
    private IntPtr HandleTarget(uint id)
    {
        var target = _handles.TryGetValue(id, out var value) ? value : IntPtr.Zero;
        DuringHandleTarget?.Invoke(target);
        return target;
    }
    private IntPtr Invoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        Invocations++;
        if(IsArrayMethod(method))return InvokeReferenceArray(method,instance,arguments,exception);
        if(method==_constructor)AllConstructorCallsRooted &= _handles.Values.Contains(instance);
        if (ThrowOnInvoke) throw new InvalidOperationException("Synthetic managed invocation failure.");
        Marshal.WriteIntPtr(exception, ExceptionOnInvoke); return Result;
    }
    private void Add(string name, string delegateName, string? handler = null)
    {
        var delegateType = typeof(NativeRuntime).GetNestedType(delegateName, BindingFlags.NonPublic)!;
        if (handler is not null) _exports[name] = Delegate.CreateDelegate(delegateType, this, GetType().GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)!);
        else
        {
            var invoke = delegateType.GetMethod("Invoke")!;
            var parameters = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            var statements = new List<Expression>();
            for (var i = 0; i < parameters.Length; i++)
                if (parameters[i].Type.IsByRef) statements.Add(Expression.Assign(parameters[i], Expression.Default(parameters[i].Type.GetElementType()!)));
            statements.Add(Expression.Default(invoke.ReturnType));
            _exports[name] = Expression.Lambda(delegateType, Expression.Block(statements), parameters).Compile();
        }
        // Preserve the exact unmanaged delegate type and every ref/out argument.
        // This test-only wrapper observes calls only inside an explicitly bounded window.
        var original = _exports[name];
        var signature = delegateType.GetMethod("Invoke")!;
        var arguments = signature.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
        var observe = Expression.Call(Expression.Constant(this), GetType().GetMethod(nameof(ObserveLowerIl2CppExport), BindingFlags.Instance | BindingFlags.NonPublic)!);
        _exports[name] = Expression.Lambda(delegateType, Expression.Block(observe,
            Expression.Invoke(Expression.Constant(original, delegateType), arguments)), arguments).Compile();
    }
    public void Dispose() { foreach(var allocation in _allocations)Marshal.FreeHGlobal(allocation);GC.KeepAlive(_exports); }
}
