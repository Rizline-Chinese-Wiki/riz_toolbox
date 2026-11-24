using System.Runtime.InteropServices;
using System.Reflection;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime;

/// <summary>All IL2CPP details stay inside the adapter. Calls use runtime_invoke, not dump-generated ABI guesses.</summary>
internal sealed partial class NativeRuntime
{
    private readonly Func<string,IntPtr> _exports;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr RuntimeInvoke(IntPtr method,IntPtr instance,IntPtr arguments,IntPtr exception);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Ptr0();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Ptr1(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Ptr2(IntPtr a,IntPtr b);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ClassLookup(IntPtr image,[MarshalAs(UnmanagedType.LPUTF8Str)] string ns,[MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr MethodLookup(IntPtr cls,[MarshalAs(UnmanagedType.LPUTF8Str)] string name,int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr FieldLookup(IntPtr cls,[MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FieldGet(IntPtr obj,IntPtr field,IntPtr output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void StaticGet(IntPtr field,IntPtr output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Flags(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint MethodFlags(IntPtr value,out uint implementation);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ParameterType(IntPtr method,uint index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FreePointer(IntPtr pointer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NewHandle(IntPtr obj,[MarshalAs(UnmanagedType.I1)] bool pinned);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void FreeHandle(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr StringNew([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StringLength(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] private delegate bool Predicate(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] private delegate bool Assignable(IntPtr target,IntPtr source);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ValueSize(IntPtr cls,out uint alignment);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TypeCode(IntPtr type);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FieldOffset(IntPtr field);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr HandleTarget(uint id);
    private readonly Ptr0 _domain;
    private readonly Ptr2 _assemblies;
    private readonly Ptr1 _image,_imageName,_objectClass,_classParent,_className,_classNamespace,_methodName,_methodClass,_unbox,_newObject,_classType,_typeObject,_stringChars;
    private readonly ClassLookup _findClass;
    private readonly MethodLookup _findMethod;
    private readonly FieldLookup _findField;
    private readonly FieldGet _fieldGet;
    private readonly StaticGet _staticGet;
    private readonly Flags _fieldFlags;
    private readonly Ptr2 _methods;
    private readonly Flags _parameterCount;
    private readonly MethodFlags _methodFlags;
    private readonly ParameterType _parameterType;
    private readonly Ptr1 _typeName;
    private readonly FreePointer _free;
    private readonly Dictionary<(IntPtr,string,int,bool,string),IntPtr> _methodCache=new();
    private readonly NewHandle _newHandle;
    private readonly FreeHandle _freeHandle;
    private readonly StringNew _stringNew;
    private readonly StringLength _stringLength;
    private readonly Ptr1 _fieldType,_returnType,_typeClass,_enumBase;
    private readonly Predicate _byRef,_isValueType,_isEnum,_isGenericMethod,_isInflatedMethod;
    private readonly Assignable _assignable;
    private readonly ValueSize _valueSize;
    private readonly TypeCode _typeCode;
    private readonly Ptr2 _fields;
    private readonly Ptr1 _fieldName;
    private readonly FieldOffset _fieldOffset;
    private readonly HandleTarget _handleTarget;
    private readonly NativeThreadAccess _thread=new();
    private readonly Dictionary<IntPtr,NativeTypeLayout> _typeLayouts=new();
    internal readonly RuntimeInvoke InvokeRaw;
    internal ILogger? Diagnostics { get; }
    private readonly Dictionary<(string,string,string),IntPtr> _classes = new();

    internal NativeRuntime(IntPtr module, ILogger? diagnostics = null):this(name=>NativeLibrary.GetExport(module,name))
    { Diagnostics = diagnostics; }
    // Explicit export seam lets ABI rejection be tested without loading a game or native patching library.
    internal NativeRuntime(Func<string,IntPtr> exports)
    {
        _exports=exports??throw new ArgumentNullException(nameof(exports));
        _domain=Api<Ptr0>("il2cpp_domain_get"); _assemblies=Api<Ptr2>("il2cpp_domain_get_assemblies");
        _image=Api<Ptr1>("il2cpp_assembly_get_image"); _imageName=Api<Ptr1>("il2cpp_image_get_name");
        _objectClass=Api<Ptr1>("il2cpp_object_get_class"); _classParent=Api<Ptr1>("il2cpp_class_get_parent");
        _className=Api<Ptr1>("il2cpp_class_get_name"); _classNamespace=Api<Ptr1>("il2cpp_class_get_namespace");
        _methodName=Api<Ptr1>("il2cpp_method_get_name"); _methodClass=Api<Ptr1>("il2cpp_method_get_class");
        _unbox=Api<Ptr1>("il2cpp_object_unbox"); _newObject=Api<Ptr1>("il2cpp_object_new");
        _classType=Api<Ptr1>("il2cpp_class_get_type"); _typeObject=Api<Ptr1>("il2cpp_type_get_object");
        _findClass=Api<ClassLookup>("il2cpp_class_from_name"); _findMethod=Api<MethodLookup>("il2cpp_class_get_method_from_name");
        _findField=Api<FieldLookup>("il2cpp_class_get_field_from_name"); _fieldGet=Api<FieldGet>("il2cpp_field_get_value");
        _staticGet=Api<StaticGet>("il2cpp_field_static_get_value"); _fieldFlags=Api<Flags>("il2cpp_field_get_flags");
        _methods=Api<Ptr2>("il2cpp_class_get_methods");_parameterCount=Api<Flags>("il2cpp_method_get_param_count");
        _methodFlags=Api<MethodFlags>("il2cpp_method_get_flags");_parameterType=Api<ParameterType>("il2cpp_method_get_param");_typeName=Api<Ptr1>("il2cpp_type_get_name");
        _free=Api<FreePointer>("il2cpp_free");
        _newHandle=Api<NewHandle>("il2cpp_gchandle_new"); _freeHandle=Api<FreeHandle>("il2cpp_gchandle_free");
        _stringNew=Api<StringNew>("il2cpp_string_new"); _stringLength=Api<StringLength>("il2cpp_string_length");
        _stringChars=Api<Ptr1>("il2cpp_string_chars"); InvokeRaw=Api<RuntimeInvoke>("il2cpp_runtime_invoke");
        _fieldType=Api<Ptr1>("il2cpp_field_get_type");_returnType=Api<Ptr1>("il2cpp_method_get_return_type");
        _typeClass=Api<Ptr1>("il2cpp_class_from_type");_enumBase=Api<Ptr1>("il2cpp_class_enum_basetype");
        _byRef=Api<Predicate>("il2cpp_type_is_byref");_isValueType=Api<Predicate>("il2cpp_class_is_valuetype");
        _isEnum=Api<Predicate>("il2cpp_class_is_enum");_assignable=Api<Assignable>("il2cpp_class_is_assignable_from");
        _valueSize=Api<ValueSize>("il2cpp_class_value_size");_typeCode=Api<TypeCode>("il2cpp_type_get_type");
        _fields=Api<Ptr2>("il2cpp_class_get_fields");_fieldName=Api<Ptr1>("il2cpp_field_get_name");_fieldOffset=Api<FieldOffset>("il2cpp_field_get_offset");
        _handleTarget=Api<HandleTarget>("il2cpp_gchandle_get_target");
        _isGenericMethod=Api<Predicate>("il2cpp_method_is_generic");_isInflatedMethod=Api<Predicate>("il2cpp_method_is_inflated");
    }
    internal void BindCurrentThread()=>_thread.BindCurrentThread();
    private void VerifyAccess()=>_thread.Verify();
    private T Api<T>(string name) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(_exports(name));
    internal bool IsMethod(IntPtr method,string ns,string type,string name)
    {
        if(method==IntPtr.Zero || Utf8(_methodName(method))!=name) return false;
        var cls=_methodClass(method);
        return Utf8(_className(cls))==type && Utf8(_classNamespace(cls))==ns;
    }
    // The startup callback runs before thread binding. Read metadata directly without using the bound caches.
    internal bool MatchesStartupTick(IntPtr method, string assembly, string ns, string type, string name)
    {
        if (method == IntPtr.Zero || Utf8(_methodName(method)) != name)
            return false;

        var cls = _methodClass(method);
        if (cls == IntPtr.Zero || Utf8(_className(cls)) != type)
            return false;

        var classNamespace = _classNamespace(cls);
        if (classNamespace == IntPtr.Zero || Utf8(classNamespace) != ns || _parameterCount(method) != 0
            || (_methodFlags(method, out _) & 0x10) != 0 || _isGenericMethod(method) || _isInflatedMethod(method))
            return false;

        var returnType = _returnType(method);
        const int il2CppTypeVoid = 0x01;
        if (returnType == IntPtr.Zero || _byRef(returnType) || _typeCode(returnType) != il2CppTypeVoid)
            return false;

        var image = Api<Ptr1>("il2cpp_class_get_image")(cls);
        return image != IntPtr.Zero && Utf8(_imageName(image)) == assembly
            && _findClass(image, ns, type) == cls && _findMethod(cls, name, 0) == method;
    }
    internal IntPtr Class(string imageName,string ns,string name)
    {
        VerifyAccess();
        if(_classes.TryGetValue((imageName,ns,name),out var cached)) return cached;
        using var size=Arg.Value(IntPtr.Zero);
        var asms=_assemblies(_domain(),size.Pointer);
        var count=Marshal.ReadIntPtr(size.Pointer).ToInt64();
        if(count is <1 or >4096) throw new InvalidOperationException("IL2CPP assembly list is not ready.");
        for(var i=0;i<count;i++) {
            var image=_image(Marshal.ReadIntPtr(asms,i*IntPtr.Size));
            if(Utf8(_imageName(image))!=imageName) continue;
            var cls=_findClass(image,ns,name);
            if(cls!=IntPtr.Zero) { _classes[(imageName,ns,name)]=cls; return cls; }
        }
        throw new MissingMemberException($"IL2CPP type missing: {imageName}:{ns}.{name}");
    }
    internal IntPtr Method(IntPtr cls,string name,int count,bool isStatic=false,string[]? parameterTypes=null)
    {
        VerifyAccess();
        if(cls==IntPtr.Zero)throw new TargetExpiredException("Method declaring class is null.");
        if(count<0 || count>128 || (parameterTypes is not null&&parameterTypes.Length!=count))throw new ArgumentException("Invalid parameter signature.");
        var key=(cls,name,count,isStatic,string.Join("|",parameterTypes??Array.Empty<string>()));
        if(_methodCache.TryGetValue(key,out var cached))return cached;
        for(var current=cls;current!=IntPtr.Zero;current=_classParent(current)) {
            using var iterator=Arg.Value(IntPtr.Zero);var matches=new List<IntPtr>();
            while(true){
                var method=_methods(current,iterator.Pointer);if(method==IntPtr.Zero)break;
                if(Utf8(_methodName(method))!=name||_parameterCount(method)!=(uint)count||((_methodFlags(method,out _)&0x10)!=0)!=isStatic)continue;
                if(parameterTypes is not null&&Enumerable.Range(0,count).Any(i=>ParameterName(method,(uint)i)!=parameterTypes[i]))continue;
                matches.Add(method);
            }
            if(matches.Count>1)throw new AmbiguousMatchException($"Ambiguous IL2CPP signature: {Utf8(_className(cls))}.{name}/{count}; adapter must specify parameter types.");
            if(matches.Count==1){_methodCache[key]=matches[0];return matches[0];}
        }
        throw new MissingMethodException($"{Utf8(_className(cls))}.{name}/{count}");
    }
    private string ParameterName(IntPtr method,uint index){var name=_typeName(_parameterType(method,index));try{return Utf8(name);}finally{if(name!=IntPtr.Zero)_free(name);}}
    /// <summary>Metadata-only probe: never reads instance storage or invokes a game method.</summary>
    internal string DescribeFieldType(IntPtr cls,string name)
    {
        VerifyAccess(); return MetadataTypeName(_fieldType(Field(cls,name)));
    }
    /// <summary>Metadata-only probe used to reject changed adapter signatures before publication.</summary>
    internal string DescribeReturnType(IntPtr method)
    {
        VerifyAccess(); if(method==IntPtr.Zero)throw new MissingMethodException("Method metadata is null.");
        return MetadataTypeName(_returnType(method));
    }
    private string MetadataTypeName(IntPtr type)
    {
        if(type==IntPtr.Zero)throw new UnsupportedSignatureException("Native type metadata is absent.");
        var name=_typeName(type);
        try { var result=Utf8(name); return string.IsNullOrWhiteSpace(result)?throw new UnsupportedSignatureException("Native type name is absent."):result; }
        finally { if(name!=IntPtr.Zero)_free(name); }
    }
    internal IntPtr Field(IntPtr cls,string name)
    {
        VerifyAccess();
        if(cls==IntPtr.Zero)throw new TargetExpiredException("Field declaring class is null.");
        for(var current=cls;current!=IntPtr.Zero;current=_classParent(current)) {
            var field=_findField(current,name);
            if(field!=IntPtr.Zero) return field;
        }
        throw new MissingFieldException($"{Utf8(_className(cls))}.{name}");
    }
    internal T Read<T>(IntPtr obj,string name) where T:struct { VerifyAccess();RequireObject(obj);using var root=Root(obj);return ReadField<T>(obj,Field(_objectClass(obj),name),false); }
    internal T ReadStatic<T>(IntPtr cls,string name) where T:struct => ReadField<T>(IntPtr.Zero,Field(cls,name),true);
    private T ReadField<T>(IntPtr obj,IntPtr field,bool isStatic) where T:struct {
        VerifyAccess();
        if(((_fieldFlags(field)&0x10)!=0)!=isStatic)throw new UnsupportedSignatureException("Field static/instance access differs from the requested operation.");
        var layout=Describe(_fieldType(field));
        NativeAbi.ValidateValue(typeof(T),layout); // Crucially before native code can write into a managed-sized output buffer.
        using var output=new Arg(NativeAbi.SizeOf(typeof(T)));
        if(isStatic) _staticGet(field,output.Pointer);
        else { RequireObject(obj);_fieldGet(obj,field,output.Pointer); }
        return ReadValue<T>(output.Pointer);
    }
    internal string ReadString(IntPtr obj,string name) => Text(Read<IntPtr>(obj,name));
    internal IntPtr Call(IntPtr obj,string name,params Arg[] args) {
        try{VerifyAccess();RequireObject(obj);using var root=Root(obj);return Invoke(Method(_objectClass(obj),name,args.Length,parameterTypes:name=="AddComponent"?new[]{"System.Type"}:null),obj,args);}
        finally{foreach(var arg in args)arg.Dispose();}
    }
    internal IntPtr CallTyped(IntPtr obj,string name,string[] parameterTypes,params Arg[] args) {
        try{VerifyAccess();RequireObject(obj);using var root=Root(obj);return Invoke(Method(_objectClass(obj),name,args.Length,parameterTypes:parameterTypes),obj,args);}
        finally{foreach(var arg in args)arg.Dispose();}
    }
    internal IEnumerable<IntPtr> ArrayItems(IntPtr array) {
        if(array==IntPtr.Zero)yield break;
        VerifyAccess();using var root=Root(array);
        var count=ValidatedArrayLength(root.Target);
        for(var i=0;i<count;i++)yield return CallTyped(root.Target,"GetValue",new[]{"System.Int32"},Arg.Value(i));
    }
    internal IntPtr CallStatic(IntPtr cls,string name,params Arg[] args) {
        try{return Invoke(Method(cls,name,args.Length,true,name=="GetBuiltinResource"?new[]{"System.Type","System.String"}:null),IntPtr.Zero,args);}
        finally{foreach(var arg in args)arg.Dispose();}
    }
    internal IntPtr Invoke(IntPtr method,IntPtr obj,params Arg[] args) => InvokeCore(method,obj,null,null,false,args);
    /// <summary>Publishes a reference result before any temporary cleanup. The internal publisher
    /// must synchronously record responsibility before doing work that can throw. A publisher
    /// exception never makes the runtime destroy or reclaim an already-notified result.</summary>
    internal IntPtr InvokeReferenceWithPublication(IntPtr method,IntPtr obj,Action<IntPtr> publish,params Arg[] args) {
        VerifyAccess();ArgumentNullException.ThrowIfNull(publish);
        var call=new NativePublicationCall(VerifyAccess,args);
        try{return InvokeCore(method,obj,publish,call,false,args);}
        catch(Exception error){call.OperationFailed(error);throw;}
        finally{call.Dispose();}
    }
    private IntPtr InvokeCore(IntPtr method,IntPtr obj,Action<IntPtr>? publish,NativePublicationCall? publication,bool argumentsRooted,Arg[] args,Action? beforeDispatch=null,Action? afterInvoke=null,Action? preparationGuard=null,bool publishBeforePostGuard=false) {
        T Observe<T>(Func<T> read) { preparationGuard?.Invoke();var result=read();preparationGuard?.Invoke();return result; }
        try {
            VerifyAccess();
            if(method==IntPtr.Zero)throw new MissingMethodException("IL2CPP method pointer is null.");
            if(Observe(()=>_parameterCount(method))!=(uint)args.Length)throw new UnsupportedSignatureException("Argument count differs from method metadata.");
            var isStatic=(Observe(()=>_methodFlags(method,out _))&0x10)!=0;
            if(isStatic!=(obj==IntPtr.Zero))throw new UnsupportedSignatureException("Method static/instance target does not match metadata.");
            if(!isStatic){RequireObject(obj);var declaring=Observe(()=>_methodClass(method));var actual=Observe(()=>_objectClass(obj));if(!Observe(()=>_assignable(declaring,actual)))throw new UnsupportedSignatureException("Method receiver is not assignable to the declaring class.");}
            if(Observe(()=>_isGenericMethod(method))&&!Observe(()=>_isInflatedMethod(method)))throw new UnsupportedSignatureException("Open generic method cannot be invoked.");
            var returns=Describe(Observe(()=>_returnType(method)),preparationGuard);NativeAbi.ValidateReturn(returns);
            if(publish is not null&&(returns.Kind!=NativeValueKind.Reference||returns.ByRef))
                throw new UnsupportedSignatureException("Reference publication requires a non-byref native reference return.");
            for(var i=0;i<args.Length;i++){
                var argumentType=Observe(()=>_parameterType(method,(uint)i));var layout=Describe(argumentType,preparationGuard);NativeAbi.ValidateArgument(layout,args[i]);
                if(args[i].IsReference&&args[i].Pointer!=IntPtr.Zero){
                    RequireObject(args[i].Pointer);
                    var actual=Observe(()=>_objectClass(args[i].Pointer));if(layout.Class==IntPtr.Zero||!Observe(()=>_assignable(layout.Class,actual)))throw new UnsupportedSignatureException($"Reference argument {i} is not assignable to '{layout.Name}'.");
                }
            }
            // No buffers are exposed to native invocation until all arguments and the return layout pass.
            using var parameters=new Arg(checked(IntPtr.Size*args.Length));
            using var exception=Arg.Value(IntPtr.Zero);
            using var receiver=publication is null&&obj!=IntPtr.Zero?Root(obj):null;
            var roots=publication is null?new List<RootHandle>():null;
            try{
                if(publication is not null&&obj!=IntPtr.Zero){publication.Root(()=>Root(obj));preparationGuard?.Invoke();}
                for(var i=0;i<args.Length;i++){
                    if(args[i].IsReference&&args[i].Pointer!=IntPtr.Zero){
                        if(publication is null)roots!.Add(Root(args[i].Pointer));
                        else if(!argumentsRooted){var pointer=args[i].Pointer;publication.Root(()=>Root(pointer));preparationGuard?.Invoke();}
                    }
                    Marshal.WriteIntPtr(parameters.Pointer,i*IntPtr.Size,args[i].Pointer);
                }
            // All metadata, temporary roots and argument marshalling precede
            // the caller's final managed lifetime/attempt guard. There is no
            // native getter between this guard and the actual invocation.
            beforeDispatch?.Invoke();
            var result=InvokeRaw(method,obj,parameters.Pointer,exception.Pointer);
            void PublishReferenceResult() {
                publication!.BeginPublication(result);
                try{publish!(result);publication.PublicationAccepted();}
                catch{publication.PublicationFailed();throw;}
            }
            // Selected reference producers must root/publish a successful
            // already-returned token before a callback-withdrawal check can
            // throw. Existing callers retain their original post-invoke order.
            if(publishBeforePostGuard&&publish is not null&&Marshal.ReadIntPtr(exception.Pointer)==IntPtr.Zero){
                PublishReferenceResult();afterInvoke?.Invoke();return result;
            }
            // Admission can be withdrawn inside the native call, including an
            // exceptional return. Check before reading result or method metadata.
            afterInvoke?.Invoke();
            if(Marshal.ReadIntPtr(exception.Pointer)!=IntPtr.Zero) throw new InvalidOperationException($"IL2CPP invocation raised an exception in '{Utf8(Observe(()=>_methodName(method)))}'.");
            if(publish is not null)PublishReferenceResult();
            return result;
            }finally{if(roots is not null)for(var i=roots.Count-1;i>=0;i--)roots[i].Dispose();}
        } finally { if(publication is null)foreach(var arg in args)arg.Dispose(); }
    }
    internal T Unbox<T>(IntPtr value) where T:struct {
        VerifyAccess();RequireObject(value);
        using var root=Root(value);var cls=_objectClass(value);
        if(!_isValueType(cls))throw new UnsupportedSignatureException("Expected boxed value type; got an object reference.");
        NativeAbi.ValidateValue(typeof(T),Describe(_classType(cls)),allowSceneProjection:true);
        return ReadValue<T>(_unbox(value));
    }
    // IL2CPP System.Boolean is one byte. The default marshaler reads a four-byte
    // Win32 BOOL and can turn a false boxed value into true from adjacent padding.
    internal static T ReadValue<T>(IntPtr pointer) where T:struct => NativeAbi.Read<T>(pointer);
    internal IntPtr New(IntPtr cls,params Arg[] args) {
        try{VerifyAccess();var constructor=Method(cls,".ctor",args.Length);
            var obj=_newObject(cls);
            if(obj==IntPtr.Zero) throw new OutOfMemoryException("IL2CPP object allocation failed.");
            using var root=Root(obj); Invoke(constructor,obj,args); return obj;
        }finally{foreach(var arg in args)arg.Dispose();}
    }
    /// <summary>Allocates through an exact declared constructor and transfers its existing root to the caller.</summary>
    internal RootHandle NewExact(IntPtr cls,string[] parameterTypes,params Arg[] args) => NewExactCore(cls,parameterTypes,null,null,args);
    /// <summary>Notifies an internal publisher immediately after allocation/rooting and before
    /// constructor entry. Ownership transfers before invoking publish: it must synchronously
    /// record the root, and the runtime never frees that output even if publish/ctor/cleanup throws.</summary>
    internal RootHandle NewExactWithPublication(IntPtr cls,string[] parameterTypes,Action<RootHandle> publish,params Arg[] args) {
        VerifyAccess();ArgumentNullException.ThrowIfNull(publish);
        var call=new NativePublicationCall(VerifyAccess,args);
        try{return NewExactCore(cls,parameterTypes,publish,call,args);}
        catch(Exception error){call.OperationFailed(error);throw;}
        finally{call.Dispose();}
    }
    private RootHandle NewExactCore(IntPtr cls,string[] parameterTypes,Action<RootHandle>? publish,NativePublicationCall? publication,Arg[] args) {
        RootHandle? result=null;
        var argumentRoots=publication is null?new List<RootHandle>():null;
        try {
          try {
            VerifyAccess();
            ArgumentNullException.ThrowIfNull(parameterTypes);
            ArgumentNullException.ThrowIfNull(args);
            if(parameterTypes.Length!=args.Length)throw new UnsupportedSignatureException("Exact constructor parameter metadata does not match the supplied arguments.");
            var constructor=Method(cls,".ctor",args.Length,false,parameterTypes);
            var returns=_returnType(constructor);
            if(_methodClass(constructor)!=cls || (_methodFlags(constructor,out _)&(0x10|0x400))!=0
                || _byRef(returns) || MetadataTypeName(returns)!="System.Void"
                || (_isGenericMethod(constructor)&&!_isInflatedMethod(constructor)))
                throw new UnsupportedSignatureException("Exact construction requires a declared nonabstract instance constructor with a void return and no open method parameters.");
            // Reject incompatible buffers before object_new. Reference arguments remain rooted
            // across allocation as well as invocation; caller-owned roots are never consumed.
            for(var i=0;i<args.Length;i++) {
                var layout=Describe(_parameterType(constructor,(uint)i));NativeAbi.ValidateArgument(layout,args[i]);
                if(args[i].IsReference&&args[i].Pointer!=IntPtr.Zero) {
                    RootHandle retained;
                    if(publication is not null){var pointer=args[i].Pointer;retained=publication.Root(()=>Root(pointer));}
                    else{
                        retained=Root(args[i].Pointer);
                        try { argumentRoots!.Add(retained); }
                        catch { retained.Dispose();throw; }
                    }
                    if(layout.Class==IntPtr.Zero||!_assignable(layout.Class,_objectClass(retained.Target)))
                        throw new UnsupportedSignatureException($"Constructor reference argument {i} is not assignable to '{layout.Name}'.");
                }
            }
            var obj=_newObject(cls);
            if(obj==IntPtr.Zero)throw new OutOfMemoryException("IL2CPP object allocation failed.");
            result=Root(obj);
            if(publish is not null){
                // The caller owns this root from notification onward. In particular, no
                // finally below may free it after a publisher or constructor exception.
                var published=result;result=null;publication!.BeginPublication(published);
                try{publish(published);publication.PublicationAccepted();}
                catch{publication.PublicationFailed();throw;}
                InvokeCore(constructor,published.Target,null,publication,true,args);
                return published;
            }
            Invoke(constructor,result.Target,args);
          } finally {
                if(publication is null){
                    try { for(var i=argumentRoots!.Count-1;i>=0;i--)argumentRoots[i].Dispose(); }
                    finally { if(args is not null)foreach(var arg in args)arg.Dispose(); }
                }
          }
          // Transfer only after consumed-argument cleanup has completed. A cleanup failure
          // must not strand an allocated object root that the caller never receives.
          var transferred=result!;result=null;return transferred;
        } finally { result?.Dispose(); }
    }
    internal IntPtr Type(IntPtr cls) {VerifyAccess();if(cls==IntPtr.Zero)throw new TargetExpiredException("Type class is null.");return _typeObject(_classType(cls));}
    internal IntPtr String(string value) {VerifyAccess();ArgumentNullException.ThrowIfNull(value);if(value.Length>4*1024*1024)throw new ArgumentOutOfRangeException(nameof(value),"String exceeds the runtime allocation limit.");return _stringNew(value);}
    internal Arg Str(string value) {var obj=String(value);return Arg.Reference(obj,Root(obj));}
    internal string Text(IntPtr value) {
        VerifyAccess();if(value==IntPtr.Zero)return "";
        RequireObject(value);
        using var root=Root(value);
        if(Describe(_classType(_objectClass(value))).Name!="System.String")throw new UnsupportedSignatureException("Text requires a System.String object.");
        var length=_stringLength(value);
        if(length<0||length>4*1024*1024)throw new UnsupportedSignatureException("String length exceeds the runtime read limit.");
        return Marshal.PtrToStringUni(_stringChars(value),length)??"";
    }
    internal RootHandle Root(IntPtr obj) {VerifyAccess();RequireObject(obj);return new(_newHandle(obj,false),_freeHandle,_handleTarget,VerifyAccess);}
    internal IntPtr ObjectClass(IntPtr obj) { VerifyAccess(); RequireObject(obj); return _objectClass(obj); }
    private void RequireObject(IntPtr obj){if(obj==IntPtr.Zero)throw new TargetExpiredException("Object reference is null.");}
    private string TypeName(IntPtr type){var name=_typeName(type);try{return Utf8(name);}finally{if(name!=IntPtr.Zero)_free(name);}}
    private NativeTypeLayout Describe(IntPtr type,Action? guard=null){
        T Observe<T>(Func<T> read){guard?.Invoke();var value=read();guard?.Invoke();return value;}
        guard?.Invoke();
        if(type==IntPtr.Zero)throw new UnsupportedSignatureException("Native type metadata is absent.");
        if(_typeLayouts.TryGetValue(type,out var cached))return cached;
        var name=guard is null?TypeName(type):LineRingTypeName(type,guard);var byRef=Observe(()=>_byRef(type));var cls=Observe(()=>_typeClass(type));var code=Observe(()=>_typeCode(type));
        NativeTypeLayout result;
        var scalar=NativeAbi.Scalar(name);
        if(byRef)result=new(name,NativeValueKind.Unsupported,0,true,cls);
        else if(code==1)result=new(name,NativeValueKind.Void,0);
        else if(scalar is not null){
            if(cls==IntPtr.Zero||!Observe(()=>_isValueType(cls))||Observe(()=>_valueSize(cls,out _))!=scalar.Size)throw new UnsupportedSignatureException($"Scalar '{name}' has unexpected native storage.");
            result=scalar with{Class=cls};
        }
        else if(cls!=IntPtr.Zero&&Observe(()=>_isEnum(cls))){
            var underlying=Describe(Observe(()=>_enumBase(cls)),guard);result=new(name,NativeValueKind.Enum,Observe(()=>_valueSize(cls,out _)),Class:cls,Underlying:underlying);
        }
        else if(cls!=IntPtr.Zero&&Observe(()=>_isValueType(cls))){
            if(!NativeAbi.IsKnownStruct(name))result=new(name,NativeValueKind.Unsupported,0,Class:cls);
            else{
                var fields=new List<NativeFieldLayout>();using var iterator=Arg.Value(IntPtr.Zero);
                while(true){var field=Observe(()=>_fields(cls,iterator.Pointer));if(field==IntPtr.Zero)break;if((Observe(()=>_fieldFlags(field))&0x10)!=0)continue;
                    var fieldName=Utf8(Observe(()=>_fieldName(field)));var fieldType=Observe(()=>_fieldType(field));var typeName=guard is null?TypeName(fieldType):LineRingTypeName(fieldType,guard);
                    fields.Add(new(fieldName,typeName,Observe(()=>_fieldOffset(field))-2*IntPtr.Size));}
                // IL2CPP field offsets include the boxed object's two-pointer header; value_size excludes it.
                result=new(name,NativeValueKind.Struct,Observe(()=>_valueSize(cls,out _)),Class:cls,Fields:fields);NativeAbi.ValidateNativeStruct(result);
            }
        }
        else if(cls!=IntPtr.Zero&&code is 14 or 18 or 20 or 21 or 28 or 29)result=new(name,NativeValueKind.Reference,IntPtr.Size,Class:cls);
        else result=new(name,NativeValueKind.Unsupported,0,Class:cls);
        _typeLayouts[type]=result;return result;
    }
    internal static string Utf8(IntPtr value) => Marshal.PtrToStringUTF8(value)??"";

    internal sealed class RootHandle:IDisposable {
        private uint _id; private readonly FreeHandle _free;private readonly HandleTarget? _target;private readonly Action? _verify;
        internal RootHandle(uint id,FreeHandle free,HandleTarget? target=null,Action? verify=null){if(id==0)throw new TargetExpiredException("IL2CPP did not create a GC root.");_id=id;_free=free;_target=target;_verify=verify;}
        internal IntPtr Target{get{_verify?.Invoke();var id=Volatile.Read(ref _id);if(id==0)throw new TargetExpiredException("GC handle is disposed.");if(_target is null)throw new UnsupportedSignatureException("GC handle target lookup is unavailable.");var obj=_target(id);if(obj==IntPtr.Zero)throw new TargetExpiredException("GC handle no longer has a target.");return obj;}}
        internal void VerifyOwnerThread()=>_verify?.Invoke();
        public void Dispose(){VerifyOwnerThread();var id=Interlocked.Exchange(ref _id,0);if(id!=0)_free(id);}
    }
    internal sealed class Arg:IDisposable {
        private IntPtr _pointer;
        private int _disposed;
        internal IntPtr Pointer{get{VerifyActive();return _pointer;}}
        internal bool IsReference{get;}
        internal Type? ManagedType{get;private init;}
        internal int Size{get;}
        private readonly bool _owned;
        private IDisposable? _root;
        internal Arg(int size){if(size<0||size>16*1024*1024)throw new ArgumentOutOfRangeException(nameof(size));Size=size;_pointer=Marshal.AllocHGlobal(Math.Max(1,size));_owned=true;for(var i=0;i<Math.Max(1,size);i++)Marshal.WriteByte(_pointer,i,0);}
        private Arg(IntPtr value){_pointer=value;IsReference=true;Size=IntPtr.Size;}
        internal static Arg Ref(IntPtr value)=>new(value);
        internal static Arg Reference(IntPtr value,IDisposable root)=>new(value){_root=root??throw new ArgumentNullException(nameof(root))};
        internal static Arg Value<T>(T value) where T:struct {
            var arg=new Arg(NativeAbi.SizeOf(typeof(T))){ManagedType=typeof(T)};
            try{NativeAbi.Write(arg.Pointer,value);return arg;}catch{arg.Dispose();throw;}
        }
        internal void VerifyActive(){if(Volatile.Read(ref _disposed)!=0)throw new ObjectDisposedException(nameof(Arg));}
        public void Dispose(){
            if(Volatile.Read(ref _disposed)!=0)return;
            // Refusing a foreign-thread native free must happen before consuming the argument
            // or detaching its root. The main-thread owner can then release it exactly once.
            if(_root is RootHandle nativeRoot)nativeRoot.VerifyOwnerThread();
            if(Interlocked.Exchange(ref _disposed,1)!=0)return;
            var pointer=Interlocked.Exchange(ref _pointer,IntPtr.Zero);
            try{if(_owned&&pointer!=IntPtr.Zero)Marshal.FreeHGlobal(pointer);}
            finally{Interlocked.Exchange(ref _root,null)?.Dispose();}
        }
    }
}
