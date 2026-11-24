using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

internal static class ExactFieldWriteRegression
{
    private static readonly string[] OptionalExports = {
        "il2cpp_field_get_parent", "il2cpp_field_set_value_object", "il2cpp_field_set_value"
    };
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Field-write exports remain lazy and unavailable write APIs do not break reads", LazyExports),
        ("Exact scalar field writes use the value setter and preserve caller-owned roots", ScalarWrite),
        ("Exact Boolean field writes pass a one-byte value buffer", BooleanWrite),
        ("Exact reference field writes pass Il2CppObject pointers and root them before lazy resolution", ReferenceWrite),
        ("Exact null reference field writes pass null directly without a temporary root", NullReference),
        ("Field writes reject a different exact owner and an inherited field before native writes", OwnerAndDeclaringClass),
        ("Disposed field owners reject writes and still consume supplied arguments", DisposedOwner),
        ("Static readonly and literal fields reject writes before either native setter", ImmutableFields),
        ("Exact field writes reject changed byref and unsupported field types", FieldTypeRejections),
        ("Reference and value arguments cannot cross native field setter paths", ArgumentKindRejections),
        ("Field reference assignability is checked before the native setter", ReferenceAssignability),
        ("Missing optional field-write exports fail closed and consume arguments", MissingExports),
        ("Reference and scalar field writes require only their selected native setter", IndependentSetters),
        ("A reference setter exception releases temporary roots and consumed argument ownership", ReferenceSetterFailure),
        ("A scalar setter exception consumes its buffer without freeing the owner", ValueSetterFailure),
        ("Field writes reject foreign threads before metadata and preserve caller arguments", ForeignThread),
        ("Wrong-thread field writes preserve an attached native root for owner-thread cleanup", ForeignThreadOwnedReference),
        ("Parameter class lookup returns the exact closed generic class from parameter metadata", ClosedParameterClass),
        ("Parameter class lookup rejects changed byref absent and unbounded metadata", ParameterClassRejections),
        ("Metadata assignability checks exact classes and rejects absent or foreign-thread use", AssignabilityChecks)
    };

    private static void LazyExports()
    {
        using var native = new FakeNative();
        foreach (var name in OptionalExports) native.MissingExports.Add(name);
        Check(!native.Api.Read<bool>(native.Instance, "field"));
        Check(!native.RequestedExports.Intersect(OptionalExports, StringComparer.Ordinal).Any());
        Check(native.FieldReads == 1 && native.FieldWrites == 0 && native.ActiveRoots == 0);
    }

    private static void ScalarWrite()
    {
        using var native = new FakeNative();
        native.FieldType = native.Int32;
        using var owner = native.Api.Root(native.Instance);
        var argument = A.Value(unchecked((int)0x87abcdef));
        native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Int32", argument);
        Check(native.ValueFieldWrites == 1 && native.ObjectFieldWrites == 0);
        Check(native.LastWrittenOwner == owner.Target && native.LastWrittenField == native.Field);
        Check(native.LastWrittenBytes.SequenceEqual(BitConverter.GetBytes(unchecked((int)0x87abcdef))));
        Check(native.AllFieldOwnersRooted && native.ActiveRoots == 1 && owner.Target == native.Instance);
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
        owner.Dispose(); Check(native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
    }

    private static void BooleanWrite()
    {
        using var native = new FakeNative();
        using var owner = native.Api.Root(native.Instance);
        native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Boolean", A.Value(false));
        Check(native.ValueFieldWrites == 1 && native.ObjectFieldWrites == 0);
        Check(native.LastWrittenBytes.Length == 1 && native.LastWrittenBytes[0] == 0);
    }

    private static void ReferenceWrite()
    {
        using var native = new FakeNative(); native.FieldType = native.Object;
        using var owner = native.Api.Root(native.Instance);
        var lazyChecks = 0;
        native.BeforeExport = name => {
            if (OptionalExports.Contains(name)) { lazyChecks++; Check(native.HasRootFor(native.OtherObject)); }
        };
        var argument = A.Ref(native.OtherObject);
        native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Object", argument);
        Check(lazyChecks == 2 && native.ObjectFieldWrites == 1 && native.ValueFieldWrites == 0);
        Check(native.LastWrittenObject == native.OtherObject && native.AllFieldReferencesRooted && native.AllFieldOwnersRooted);
        Check(native.ActiveRoots == 1 && !native.HasRootFor(native.OtherObject));
        Check(!native.RequestedExports.Contains("il2cpp_field_set_value"));
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
    }

    private static void NullReference()
    {
        using var native = new FakeNative(); native.FieldType = native.String;
        using var owner = native.Api.Root(native.Instance);
        var rootsBefore = native.NewRoots;
        native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.String", A.Ref(IntPtr.Zero));
        Check(native.ObjectFieldWrites == 1 && native.ValueFieldWrites == 0 && native.LastWrittenObject == IntPtr.Zero);
        Check(native.NewRoots == rootsBefore && native.ActiveRoots == 1);
    }

    private static void OwnerAndDeclaringClass()
    {
        using (var native = new FakeNative())
        {
            using var owner = native.Api.Root(native.Instance);
            var argument = A.Value(false);
            Throws<UnsupportedSignatureException>(() => native.Api.WriteExact(owner, native.ClassOf(native.Object), "field", "System.Boolean", argument));
            Check(native.FieldParentQueries == 0 && native.FieldWrites == 0 && native.ActiveRoots == 1);
            Throws<ObjectDisposedException>(() => _ = argument.Pointer);
        }
        using (var native = new FakeNative())
        {
            native.FieldParentClass = native.ClassOf(native.Object);
            using var owner = native.Api.Root(native.Instance);
            Throws<UnsupportedSignatureException>(() => native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Boolean", A.Value(false)));
            Check(native.FieldParentQueries == 1 && native.FieldWrites == 0 && native.ActiveRoots == 1);
        }
    }

    private static void DisposedOwner()
    {
        using var native = new FakeNative(); native.FieldType = native.Object;
        using var owner = native.Api.Root(native.Instance); owner.Dispose();
        var argument = A.Ref(native.OtherObject);
        Throws<TargetExpiredException>(() => native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Object", argument));
        Check(native.FieldWrites == 0 && native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
    }

    private static void ImmutableFields()
    {
        foreach (var flags in new[] { 0x10u, 0x20u, 0x40u, 0x70u })
        {
            using var native = new FakeNative { ExtraFieldFlags = flags };
            using var owner = native.Api.Root(native.Instance);
            var argument = A.Value(false);
            Throws<UnsupportedSignatureException>(() => native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Boolean", argument));
            Check(native.FieldWrites == 0 && native.ActiveRoots == 1);
            Check(!native.RequestedExports.Contains("il2cpp_field_set_value") && !native.RequestedExports.Contains("il2cpp_field_set_value_object"));
            Throws<ObjectDisposedException>(() => _ = argument.Pointer);
        }
    }

    private static void FieldTypeRejections()
    {
        using (var native = new FakeNative())
        {
            native.FieldType = native.Int32;
            using var owner = native.Api.Root(native.Instance);
            Throws<UnsupportedSignatureException>(() => native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Single", A.Value(1f)));
            Check(native.FieldWrites == 0);
        }
        using (var native = new FakeNative())
        {
            native.FieldType = native.AddType("System.Int32&", 8, true, 4, byRef: true);
            using var owner = native.Api.Root(native.Instance);
            Throws<UnsupportedSignatureException>(() => native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Int32&", A.Value(1)));
            Check(native.FieldWrites == 0);
        }
        using (var native = new FakeNative())
        {
            native.FieldType = native.UnknownStruct;
            using var owner = native.Api.Root(native.Instance);
            Throws<UnsupportedSignatureException>(() => native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "Fake.LargeStruct", A.Value(1)));
            Check(native.FieldWrites == 0);
        }
    }

    private static void ArgumentKindRejections()
    {
        using (var native = new FakeNative())
        {
            native.FieldType = native.Int32;
            using var owner = native.Api.Root(native.Instance);
            Throws<UnsupportedSignatureException>(() => native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Int32", A.Ref(native.OtherObject)));
            Check(native.FieldWrites == 0 && native.ActiveRoots == 1 && !native.HasRootFor(native.OtherObject));
        }
        using (var native = new FakeNative())
        {
            native.FieldType = native.Object;
            using var owner = native.Api.Root(native.Instance);
            Throws<UnsupportedSignatureException>(() => native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Object", A.Value(native.OtherObject)));
            Check(native.FieldWrites == 0 && native.ActiveRoots == 1);
        }
    }

    private static void ReferenceAssignability()
    {
        using var native = new FakeNative(); native.FieldType = native.Receiver;
        using var owner = native.Api.Root(native.Instance);
        var argument = A.Ref(native.OtherObject);
        Throws<UnsupportedSignatureException>(() => native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "Fake.Receiver", argument));
        Check(native.FieldWrites == 0 && native.ActiveRoots == 1 && !native.HasRootFor(native.OtherObject));
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
    }

    private static void MissingExports()
    {
        foreach (var missing in new[] { "il2cpp_field_get_parent", "il2cpp_field_set_value_object" })
        {
            using var native = new FakeNative(); native.FieldType = native.Object; native.MissingExports.Add(missing);
            using var owner = native.Api.Root(native.Instance);
            var argument = A.Ref(native.OtherObject);
            Throws<UnsupportedSignatureException>(() => native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Object", argument));
            Check(native.FieldWrites == 0 && native.ActiveRoots == 1 && !native.HasRootFor(native.OtherObject));
            Throws<ObjectDisposedException>(() => _ = argument.Pointer);
        }
        using (var native = new FakeNative())
        {
            native.MissingExports.Add("il2cpp_field_set_value");
            using var owner = native.Api.Root(native.Instance);
            Throws<UnsupportedSignatureException>(() => native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Boolean", A.Value(false)));
            Check(native.FieldWrites == 0 && native.ActiveRoots == 1);
        }
    }

    private static void IndependentSetters()
    {
        using (var native = new FakeNative())
        {
            native.MissingExports.Add("il2cpp_field_set_value_object");
            using var owner = native.Api.Root(native.Instance);
            native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Boolean", A.Value(true));
            Check(native.ValueFieldWrites == 1 && native.ObjectFieldWrites == 0);
        }
        using (var native = new FakeNative())
        {
            native.FieldType = native.Object; native.MissingExports.Add("il2cpp_field_set_value");
            using var owner = native.Api.Root(native.Instance);
            native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Object", A.Ref(native.OtherObject));
            Check(native.ObjectFieldWrites == 1 && native.ValueFieldWrites == 0);
        }
    }

    private static void ReferenceSetterFailure()
    {
        using var native = new FakeNative { ThrowOnFieldWrite = true }; native.FieldType = native.Object;
        using var owner = native.Api.Root(native.Instance);
        var ownership = new CountingLease(); var argument = A.Reference(native.OtherObject, ownership);
        Throws<InvalidOperationException>(() => native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Object", argument));
        Check(native.ObjectFieldWrites == 1 && native.ValueFieldWrites == 0 && native.AllFieldReferencesRooted && native.AllFieldOwnersRooted);
        Check(native.ActiveRoots == 1 && !native.HasRootFor(native.OtherObject) && ownership.Count == 1);
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
    }

    private static void ValueSetterFailure()
    {
        using var native = new FakeNative { ThrowOnFieldWrite = true };
        using var owner = native.Api.Root(native.Instance);
        var argument = A.Value(true);
        Throws<InvalidOperationException>(() => native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Boolean", argument));
        Check(native.ValueFieldWrites == 1 && native.ObjectFieldWrites == 0 && native.ActiveRoots == 1);
        Check(owner.Target == native.Instance);
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
    }

    private static void ForeignThread()
    {
        using var native = new FakeNative();
        using var owner = native.Api.Root(native.Instance);
        using var argument = A.Value(true); Exception? failure = null;
        var worker = new Thread(() => {
            try { native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Boolean", argument); }
            catch (Exception error) { failure = error; }
        });
        worker.Start(); worker.Join();
        Check(failure is InvalidOperationException && native.FieldWrites == 0 && native.FieldParentQueries == 0 && native.ActiveRoots == 1);
        Check(NativeRuntime.ReadValue<bool>(argument.Pointer));
        argument.Dispose();
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
        Check(owner.Target == native.Instance);
    }

    private static void ForeignThreadOwnedReference()
    {
        using var native = new FakeNative(); native.FieldType = native.Object;
        using var owner = native.Api.Root(native.Instance);
        // Keep an independent managed reference so even a failing regression can clean
        // up the native handle on its owner thread without hiding the original failure.
        using var attachedRoot = native.Api.Root(native.OtherObject);
        using var argument = A.Reference(native.OtherObject, attachedRoot);
        Exception? failure = null;
        var worker = new Thread(() => {
            try { native.Api.WriteExact(owner, native.ClassOf(native.Receiver), "field", "System.Object", argument); }
            catch (Exception error) { failure = error; }
        });
        worker.Start(); worker.Join();
        Check(failure is InvalidOperationException && native.FieldWrites == 0 && native.FieldParentQueries == 0);
        Check(argument.Pointer == native.OtherObject && attachedRoot.Target == native.OtherObject);
        Check(native.ActiveRoots == 2 && native.FreedRoots == 0);
        argument.Dispose(); argument.Dispose();
        Check(native.ActiveRoots == 1 && native.FreedRoots == 1 && owner.Target == native.Instance);
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
        owner.Dispose(); Check(native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
    }

    private static void ClosedParameterClass()
    {
        const string typeName = "System.Collections.Generic.Dictionary<System.String,UnityEngine.Color>";
        using var native = new FakeNative();
        _ = native.AddType(typeName, 21, false, IntPtr.Size);
        var parameter = native.AddType(typeName, 21, false, IntPtr.Size);
        native.Parameters = new[] { native.String, parameter };
        var actual = native.Api.InspectMethodParameterClass(native.Method, 1, typeName);
        Check(actual.Class == native.ClassOf(parameter) && actual.TypeName == typeName && native.ActiveRoots == 0 && native.FieldWrites == 0);
    }

    private static void ParameterClassRejections()
    {
        using var native = new FakeNative(); native.Parameters = new[] { native.String };
        Throws<MissingMethodException>(() => native.Api.InspectMethodParameterClass(IntPtr.Zero, 0, "System.String"));
        Throws<UnsupportedSignatureException>(() => native.Api.InspectMethodParameterClass(native.Method, 1, "System.String"));
        Throws<UnsupportedSignatureException>(() => native.Api.InspectMethodParameterClass(native.Method, 0, "System.Object"));
        native.Parameters = new[] { native.AddType("System.String&", 14, false, IntPtr.Size, byRef: true) };
        Throws<UnsupportedSignatureException>(() => native.Api.InspectMethodParameterClass(native.Method, 0, "System.String&"));
        native.Parameters = Enumerable.Repeat(native.String, 129).ToArray();
        Throws<UnsupportedSignatureException>(() => native.Api.InspectMethodParameterClass(native.Method, 0, "System.String"));
        Check(native.FieldWrites == 0 && native.ActiveRoots == 0);
    }

    private static void AssignabilityChecks()
    {
        using var native = new FakeNative();
        native.Api.RequireAssignable(native.ClassOf(native.Object), native.ClassOf(native.String));
        native.Api.RequireAssignable(native.ClassOf(native.Receiver), native.ClassOf(native.Receiver));
        Throws<UnsupportedSignatureException>(() => native.Api.RequireAssignable(native.ClassOf(native.Receiver), native.ClassOf(native.String)));
        Throws<UnsupportedSignatureException>(() => native.Api.RequireAssignable(IntPtr.Zero, native.ClassOf(native.String)));
        Throws<UnsupportedSignatureException>(() => native.Api.RequireAssignable(native.ClassOf(native.Object), IntPtr.Zero));
        native.Parameters = new[] { native.String }; Exception? assignFailure = null, parameterFailure = null;
        var worker = new Thread(() => {
            try { native.Api.RequireAssignable(native.ClassOf(native.Object), native.ClassOf(native.String)); } catch (Exception error) { assignFailure = error; }
            try { native.Api.InspectMethodParameterClass(native.Method, 0, "System.String"); } catch (Exception error) { parameterFailure = error; }
        });
        worker.Start(); worker.Join();
        Check(assignFailure is InvalidOperationException && parameterFailure is InvalidOperationException && native.FieldWrites == 0);
    }

    private static void Check(bool condition)
    { if (!condition) throw new InvalidOperationException("Exact-field-write invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private sealed class CountingLease : IDisposable
    {
        internal int Count;
        public void Dispose() => Count++;
    }
}
