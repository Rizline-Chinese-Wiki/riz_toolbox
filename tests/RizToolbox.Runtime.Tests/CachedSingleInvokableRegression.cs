using System.Runtime.InteropServices;
using RizToolbox.Runtime;

internal static class CachedSingleInvokableRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Cached Single projection preserves every four-byte float payload without reading adjacent padding", Projection),
        ("Cached Single projection rejects open foreign and inconsistent closed-class metadata", Classes),
        ("Cached Single projection requires the official 4-byte argument and delegate field layout", Fields),
        ("Cached Single projection validates UnityAction<float>.Invoke without resolving a shared entry", InvokeSignature),
        ("Cached Single projection bounds official field and method enumeration", Bounds),
        ("Cached Single field publication adopts the official delegate result before post-read inspection", Publication),
        ("Cached Single projection detects argument delegate or class mutation during its exact read", Mutation),
        ("Cached Single observation rejects worker access before field or method reads", Admission)
    };

    private static void Projection()
    {
        using var f = new CachedBooleanInvokableRegression.Fixture(single: true); using var root = f.Api.Root(f.Object);
        Marshal.WriteInt64(f.Object, 0x18, -1);
        Marshal.WriteInt32(f.Object, 0x18, BitConverter.SingleToInt32Bits(-0f));
        var negativeZero = f.ProjectSingle(root);
        Check(negativeZero.Object == f.Object && negativeZero.ActualClass == f.Cached.Class
            && negativeZero.InvokableParentClass == f.Parent.Class && negativeZero.ActionClass == f.Action.Class
            && negativeZero.ArgumentClass == f.Boolean.Class && negativeZero.ArgumentBits == unchecked((int)0x80000000)
            && negativeZero.Delegate == f.Delegate && negativeZero.ActionInvokeMethod == f.Invoke.Pointer);
        foreach (uint rawBits in new uint[] {
            0x00000000u, 0x80000000u, 0x7f800000u, 0xff800000u,
            0x7fc12345u, 0xffc05678u, 0x7f800001u, 0xffa00001u,
            0x00000001u, 0x3f800000u
        })
        {
            var payload = unchecked((int)rawBits);
            Marshal.WriteInt32(f.Object, 0x18, payload);
            var observed = f.ProjectSingle(root);
            Check(observed.ArgumentBits == payload && BitConverter.SingleToInt32Bits(observed.Argument) == payload);
        }
        Check(f.Base.ActiveRoots == 1 && f.Base.NewRoots == 1 && f.Base.FreedRoots == 0 && root.Target == f.Object);
        f.NoGameCalls();
    }

    private static void Classes()
    {
        foreach (var mode in new[] { "cached-name", "parent-name", "namespace", "image", "generic", "not-inflated",
            "code", "byref", "value", "roundtrip", "parent", "base-parent", "action-parent", "multicast-parent",
            "single-size", "single-code" })
        {
            using var f = new CachedBooleanInvokableRegression.Fixture(single: true); using var root = f.Api.Root(f.Object);
            if (mode == "cached-name") f.Cached.Name = "UnityEngine.Events.CachedInvokableCall<System.Int32>";
            if (mode == "parent-name") f.Parent.ShortName = "InvokableCall";
            if (mode == "namespace") f.Cached.Namespace = "Fixture";
            if (mode == "image") f.Cached.Image = f.SystemImage;
            if (mode == "generic") f.Cached.Generic = true;
            if (mode == "not-inflated") f.Cached.Inflated = false;
            if (mode == "code") f.Cached.Code = 18;
            if (mode == "byref") f.Cached.ByRef = true;
            if (mode == "value") f.Cached.Value = true;
            if (mode == "roundtrip") f.Cached.ReturnedClass = f.Parent.Class;
            if (mode == "parent") f.Cached.Parent = f.BaseClass.Class;
            if (mode == "base-parent") f.Parent.Parent = f.Multicast.Class;
            if (mode == "action-parent") f.Action.Parent = f.DelegateClass.Class;
            if (mode == "multicast-parent") f.Multicast.Parent = f.BaseClass.Class;
            if (mode == "single-size") f.Boolean.Size = 8;
            if (mode == "single-code") f.Boolean.Code = 2;
            Throws<UnsupportedSignatureException>(() => f.ProjectSingle(root));
            Check(f.FieldReads == 0 && root.Target == f.Object && f.Base.ActiveRoots == 1); f.NoGameCalls();
        }
    }

    private static void Fields()
    {
        foreach (var mode in new[] { "argument-owner", "delegate-owner", "argument-static", "argument-literal",
            "argument-type", "argument-alias", "argument-byref", "delegate-type", "delegate-byref", "shadow",
            "duplicate", "missing", "argument-offset", "delegate-offset", "overlap", "object-bound", "object-size" })
        {
            using var f = new CachedBooleanInvokableRegression.Fixture(single: true); using var root = f.Api.Root(f.Object);
            if (mode == "argument-owner") f.Argument.OwnerOverride = f.Parent.Class;
            if (mode == "delegate-owner") f.ActionField.OwnerOverride = f.Cached.Class;
            if (mode == "argument-static") f.Argument.Flags = 0x10;
            if (mode == "argument-literal") f.Argument.Flags = 0x40;
            if (mode == "argument-type") f.Argument.Type = f.Action.Type;
            if (mode == "argument-alias") f.Argument.Type = f.AddType("System.Single", "System", "Single", 12, true, 4, f.SystemImage).Type;
            if (mode == "argument-byref") f.Boolean.ByRef = true;
            if (mode == "delegate-type") f.ActionField.Type = f.Boolean.Type;
            if (mode == "delegate-byref") f.Action.ByRef = true;
            if (mode == "shadow") f.AddField(f.Cached.Class, "Delegate", f.Action.Type, 0x10);
            if (mode == "duplicate") f.AddField(f.Cached.Class, "m_Arg1", f.Boolean.Type, 0x18);
            if (mode == "missing") f.Argument.Name = "OtherArgument";
            if (mode == "argument-offset") f.Argument.Offset = 0;
            if (mode == "delegate-offset") f.ActionField.Offset = 0;
            if (mode == "overlap") f.Argument.Offset = 0x10;
            if (mode == "object-bound") f.Argument.Offset = 0x20;
            if (mode == "object-size") f.ObjectBytes = 8192;
            Throws<UnsupportedSignatureException>(() => f.ProjectSingle(root));
            Check(f.FieldReads == 0 && f.Base.ActiveRoots == 1); f.NoGameCalls();
        }
        using var valid = new CachedBooleanInvokableRegression.Fixture(single: true); using var owner = valid.Api.Root(valid.Object);
        valid.Argument.Flags = 0x20; Check(valid.ProjectSingle(owner).ArgumentBits == 0); valid.NoGameCalls();
    }

    private static void InvokeSignature()
    {
        foreach (var mode in new[] { "owner", "name", "static", "abstract", "generic", "count", "return",
            "parameter", "parameter-alias", "duplicate" })
        {
            using var f = new CachedBooleanInvokableRegression.Fixture(single: true); using var root = f.Api.Root(f.Object);
            if (mode == "owner") f.Invoke.OwnerOverride = f.Parent.Class;
            if (mode == "name") f.Invoke.Name = "OtherInvoke";
            if (mode == "static") f.Invoke.Flags = 0x10;
            if (mode == "abstract") f.Invoke.Flags = 0x400;
            if (mode == "generic") f.Invoke.Generic = true;
            if (mode == "count") f.Invoke.Parameters = Array.Empty<IntPtr>();
            if (mode == "return") f.Invoke.Return = f.Boolean.Type;
            if (mode == "parameter") f.Invoke.Parameters = new[] { f.Action.Type };
            if (mode == "parameter-alias") f.Invoke.Parameters = new[] { f.AddType("System.Single", "System", "Single", 12, true, 4, f.SystemImage).Type };
            if (mode == "duplicate") f.AddMethod("Invoke");
            Throws<UnsupportedSignatureException>(() => f.ProjectSingle(root));
            Check(f.FieldReads == 0 && f.Base.ActiveRoots == 1); f.NoGameCalls();
        }
    }

    private static void Publication()
    {
        using var f = new CachedBooleanInvokableRegression.Fixture(single: true); using var root = f.Api.Root(f.Object);
        var expected = f.ProjectSingle(root); NativeRuntime.RootHandle? adopted = null; var reads = 0; var sawRead = false;
        f.AfterRead = field => { if (field == f.ActionField && ++reads == 3) sawRead = true; };
        f.OnMetadata = () => Check(!sawRead);
        var result = f.Api.ReadCachedSingleDelegateWithPublication(root, expected, pointer => {
            Check(pointer == f.Delegate); adopted = f.Api.Root(pointer);
        });
        Check(result == f.Delegate && adopted is not null && adopted.Target == f.Delegate
            && f.Base.ActiveRoots == 2);
        adopted!.Dispose();
        Marshal.WriteIntPtr(f.Object, 0x10, IntPtr.Zero); f.AfterRead = null; f.OnMetadata = null;
        expected = f.ProjectSingle(root); var notifications = 0;
        Check(f.Api.ReadCachedSingleDelegateWithPublication(root, expected, value => {
            Check(value == IntPtr.Zero); ++notifications;
        }) == IntPtr.Zero && notifications == 1 && f.Base.ActiveRoots == 1); f.NoGameCalls();
    }

    private static void Mutation()
    {
        foreach (var mode in new[] { "argument", "delegate-late", "class", "field", "invoke" })
        {
            using var f = new CachedBooleanInvokableRegression.Fixture(single: true); using var root = f.Api.Root(f.Object); var fired = false;
            f.AfterRead = field => {
                if (fired || field != (mode == "delegate-late" ? f.ActionField : f.Argument)) return; fired = true;
                if (mode == "argument") Marshal.WriteInt32(f.Object, 0x18, 1);
                if (mode == "delegate-late") Marshal.WriteIntPtr(f.Object, 0x10, f.OtherDelegate);
                if (mode == "class") Marshal.WriteIntPtr(f.Object, f.Parent.Class);
                if (mode == "field") f.Argument.Offset = 0x19;
                if (mode == "invoke") f.Invoke.Parameters = Array.Empty<IntPtr>();
            };
            Throws<UnsupportedSignatureException>(() => f.ProjectSingle(root));
            Check(f.Base.ActiveRoots == 1 && root.Target == f.Object); f.NoGameCalls();
        }
    }

    private static void Bounds()
    {
        using var fields = new CachedBooleanInvokableRegression.Fixture(single: true); using var fieldRoot = fields.Api.Root(fields.Object);
        fields.EndlessFields = true; Throws<UnsupportedSignatureException>(() => fields.ProjectSingle(fieldRoot));
        Check(fields.FieldEnumerations == 256 && fields.FieldReads == 0);
        using var methods = new CachedBooleanInvokableRegression.Fixture(single: true); using var methodRoot = methods.Api.Root(methods.Object);
        methods.EndlessMethods = true; Throws<UnsupportedSignatureException>(() => methods.ProjectSingle(methodRoot));
        Check(methods.MethodEnumerations == 256 && methods.FieldReads == 0); fields.NoGameCalls(); methods.NoGameCalls();
    }

    private static void Admission()
    {
        foreach (var name in new[] { "il2cpp_class_get_image", "il2cpp_field_get_parent", "il2cpp_class_is_generic",
            "il2cpp_class_is_inflated", "il2cpp_object_get_size" })
        {
            using var f = new CachedBooleanInvokableRegression.Fixture(single: true); using var root = f.Api.Root(f.Object);
            f.MissingExport = name; Throws<UnsupportedSignatureException>(() => f.ProjectSingle(root));
            Check(f.FieldReads == 0 && f.Base.ActiveRoots == 1);
        }
        using var valid = new CachedBooleanInvokableRegression.Fixture(single: true); var expired = valid.Api.Root(valid.Object);
        expired.Dispose(); Throws<TargetExpiredException>(() => valid.ProjectSingle(expired));
        using var owner = valid.Api.Root(valid.Object); var reads = valid.MetadataReads;
        Task.Run(() => Throws<InvalidOperationException>(() => valid.ProjectSingle(owner))).GetAwaiter().GetResult();
        Check(valid.MetadataReads == reads && valid.FieldReads == 0 && owner.Target == valid.Object); valid.NoGameCalls();
    }

    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Cached Single observation invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
