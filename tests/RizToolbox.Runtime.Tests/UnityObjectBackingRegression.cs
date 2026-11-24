using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Runtime;

// Real owned wrapper headers, static storage and separate native backing/ID
// buffers; synthetic official exports describe only those buffers. No game
// image is loaded, no game method runs, and no native backing is read by the
// runtime projection. The native SEH watch tests own actual-ID comparison.
internal static class UnityObjectBackingRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Unity backing projection reads independently rooted exact source and clip from real wrapper and static storage", Projection),
        ("Unity backing projection copies unusable cached pointers without native dereference or method invocation", NoNativeDereference),
        ("Unity backing projection admits only initialized aligned bounded native-ID offsets", OffsetBounds),
        ("Unity backing projection rejects source and clip zero cached pointers while retaining their actual roots", ZeroCached),
        ("Unity backing projection rejects wrong pointer declaration before any value read", CachedMetadata),
        ("Unity backing projection rejects wrong static ID-offset declaration before any value read", OffsetMetadata),
        ("Unity backing projection requires exact official source and clip classes rather than assignability", ExactClasses),
        ("Unity backing projection verifies official reference and primitive class metadata and ancestry", ClassMetadata),
        ("Unity backing projection rejects absent official metadata exports without reading storage", RequiredExports),
        ("Unity backing projection rechecks declarations around every actual scalar read", MetadataChanges),
        ("Unity backing projection does not trust cached scalar layouts after primitive metadata changes", WarmMetadataChanges),
        ("Unity backing projection rejects changing cached pointer and initialized static offset across reads", ValueChanges),
        ("Unity backing projection rejects root and exact-class changes during every value-read boundary", RootAndClassChanges),
        ("Unity backing projection rejects null disposed mismatched and foreign-thread roots before observation", RootsAndThreads),
        ("Unity backing observation remains copied data when the native ID changes behind the same pointer", NativeIdIsNotAuthority),
        ("Unity backing projection uses the bounded official static-data leaf and rejects null unreadable or moved storage", StaticDataStorage)
    };

    private static void Projection()
    {
        using var f = new Fixture(); using var source = f.Api.Root(f.Source); using var clip = f.Api.Root(f.Clip);
        var sourceObservation = f.Api.ReadUnityObjectBackingObservation(source, f.Source.ToInt64(), f.AudioSource.Class);
        var clipObservation = f.Api.ReadUnityObjectBackingObservation(clip, f.Clip.ToInt64(), f.AudioClip.Class);
        Check(sourceObservation.ManagedObject == f.Source.ToInt64() && sourceObservation.ExactClass == f.AudioSource.Class
            && sourceObservation.CachedNativePointer == f.NativeSource.ToInt64() && sourceObservation.NativeInstanceIdOffset == 8
            && sourceObservation.UnityObjectClass == f.UnityObject.Class && sourceObservation.InstanceIdOffsetFieldOffset == 0);
        Check(clipObservation.ManagedObject == f.Clip.ToInt64() && clipObservation.ExactClass == f.AudioClip.Class
            && clipObservation.CachedNativePointer == f.NativeClip.ToInt64() && clipObservation.NativeInstanceIdOffset == 8
            && clipObservation.UnityObjectClass == f.UnityObject.Class && clipObservation.InstanceIdOffsetFieldOffset == 0);
        Check(f.Source != f.Clip && f.NativeSource != f.NativeClip && f.Source != f.NativeSource && f.Clip != f.NativeClip
            && Marshal.ReadIntPtr(f.Source, 0x10) == f.NativeSource && Marshal.ReadIntPtr(f.Clip, 0x10) == f.NativeClip
            && Marshal.ReadInt32(f.NativeSource, 8) == -1234 && Marshal.ReadInt32(f.NativeClip, 8) == 5678
            && f.InstanceReads == 4 && f.StaticReads == 4 && f.NewRoots == 2 && f.ActiveRoots == 2 && f.FreedRoots == 0
            && source.Target == f.Source && clip.Target == f.Clip);
        f.NoEffects();
    }

    private static void NoNativeDereference()
    {
        using var f = new Fixture(); using var source = f.Api.Root(f.Source); using var clip = f.Api.Root(f.Clip);
        var unusable = new IntPtr(unchecked((long)0x8123456789abcdefUL));
        Marshal.WriteIntPtr(f.Source, 0x10, unusable); Marshal.WriteIntPtr(f.Clip, 0x10, unusable);
        Check(f.Api.ReadUnityObjectBackingObservation(source, f.Source.ToInt64(), f.AudioSource.Class).CachedNativePointer == unusable.ToInt64());
        Check(f.Api.ReadUnityObjectBackingObservation(clip, f.Clip.ToInt64(), f.AudioClip.Class).CachedNativePointer == unusable.ToInt64());
        Check(f.InstanceReads == 4 && f.StaticReads == 4 && f.NewRoots == 2 && f.FreedRoots == 0 && f.ActiveRoots == 2);
        f.NoEffects();
    }

    private static void OffsetBounds()
    {
        foreach (var offset in new[] { 0, 4, 8, NativeRuntime.MaximumUnityNativeInstanceIdOffset })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Source); Marshal.WriteInt32(f.StaticFields, offset);
            var observed = f.Api.ReadUnityObjectBackingObservation(root, f.Source.ToInt64(), f.AudioSource.Class);
            Check(observed.NativeInstanceIdOffset == offset && f.StaticReads == 2 && f.InstanceReads == 2);
            f.NoEffects();
        }
        foreach (var offset in new[] { -1, -4, int.MinValue, 1, 6, 0x401, 0x404, int.MaxValue })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Source); Marshal.WriteInt32(f.StaticFields, offset);
            Throws<UnsupportedSignatureException>(() => f.Api.ReadUnityObjectBackingObservation(root, f.Source.ToInt64(), f.AudioSource.Class));
            Check(f.StaticReads == 1 && f.InstanceReads == 0 && f.ActiveRoots == 1 && f.NewRoots == 1 && f.FreedRoots == 0);
            f.NoEffects();
        }
    }

    private static void ZeroCached()
    {
        foreach (var isClip in new[] { false, true })
        {
            using var f = new Fixture(); var obj = isClip ? f.Clip : f.Source; var cls = isClip ? f.AudioClip.Class : f.AudioSource.Class;
            using var root = f.Api.Root(obj); Marshal.WriteIntPtr(obj, 0x10, IntPtr.Zero);
            Throws<TargetExpiredException>(() => f.Api.ReadUnityObjectBackingObservation(root, obj.ToInt64(), cls));
            Check(f.StaticReads == 1 && f.InstanceReads == 1 && root.Target == obj && f.ActiveRoots == 1 && f.FreedRoots == 0);
            f.NoEffects();
        }
    }

    private static void CachedMetadata()
    {
        foreach (var mode in new[] { "owner", "offset", "static", "literal", "type", "name", "byref", "size", "reference",
            "duplicate", "shadow-source", "shadow-clip", "instance-size" })
        {
            using var f = new Fixture(); var obj = mode == "shadow-clip" ? f.Clip : f.Source;
            var cls = mode == "shadow-clip" ? f.AudioClip.Class : f.AudioSource.Class; using var root = f.Api.Root(obj);
            switch (mode)
            {
                case "owner": f.Cached.OwnerOverride = f.AudioSource.Class; break;
                case "offset": f.Cached.Offset = 0x18; break;
                case "static": f.Cached.Flags = 0x10; break;
                case "literal": f.Cached.Flags = 0x40; break;
                case "type": f.Cached.Type = f.Integer.Type; break;
                case "name": f.Cached.Name = "cachedAlias"; break;
                case "byref": f.Pointer.ByRef = true; break;
                case "size": f.Pointer.Size = 4; break;
                case "reference": f.Pointer.Value = false; break;
                case "duplicate": f.AddField(f.UnityObject.Class, "m_CachedPtr", f.Pointer.Type, 0x10); break;
                case "shadow-source": f.AddField(f.AudioSource.Class, "m_CachedPtr", f.Pointer.Type, 0x10); break;
                case "shadow-clip": f.AddField(f.AudioClip.Class, "m_CachedPtr", f.Pointer.Type, 0x10); break;
                case "instance-size": f.UnityObject.InstanceSize = 0x17; break;
            }
            Throws<UnsupportedSignatureException>(() => f.Api.ReadUnityObjectBackingObservation(root, obj.ToInt64(), cls));
            Check(f.InstanceReads == 0 && f.StaticReads == 0 && f.ActiveRoots == 1 && f.FreedRoots == 0, mode);
            f.NoEffects();
        }
    }

    private static void OffsetMetadata()
    {
        foreach (var mode in new[] { "owner", "offset", "negative-offset", "instance", "literal", "type", "name", "byref",
            "size", "reference", "duplicate", "shadow-source", "shadow-clip", "type-alias" })
        {
            using var f = new Fixture(); var obj = mode == "shadow-clip" ? f.Clip : f.Source;
            var cls = mode == "shadow-clip" ? f.AudioClip.Class : f.AudioSource.Class; using var root = f.Api.Root(obj);
            switch (mode)
            {
                case "owner": f.Offset.OwnerOverride = f.AudioSource.Class; break;
                case "offset": f.Offset.Offset = 4; break;
                case "negative-offset": f.Offset.Offset = -1; break;
                case "instance": f.Offset.Flags = 0; break;
                case "literal": f.Offset.Flags |= 0x40; break;
                case "type": f.Offset.Type = f.Pointer.Type; break;
                case "name": f.Offset.Name = "offsetAlias"; break;
                case "byref": f.Integer.ByRef = true; break;
                case "size": f.Integer.Size = 8; break;
                case "reference": f.Integer.Value = false; break;
                case "duplicate": f.AddField(f.UnityObject.Class, f.Offset.Name, f.Integer.Type, 0, 0x10); break;
                case "shadow-source": f.AddField(f.AudioSource.Class, f.Offset.Name, f.Integer.Type, 0, 0x10); break;
                case "shadow-clip": f.AddField(f.AudioClip.Class, f.Offset.Name, f.Integer.Type, 0, 0x10); break;
                case "type-alias": f.Offset.Type = f.AddType("System.Int32", "System", "Int32Alias", 8, true, 4, f.SystemImage).Type; break;
            }
            Throws<UnsupportedSignatureException>(() => f.Api.ReadUnityObjectBackingObservation(root, obj.ToInt64(), cls));
            Check(f.InstanceReads == 0 && f.StaticReads == 0 && f.ActiveRoots == 1 && f.FreedRoots == 0, mode);
            f.NoEffects();
        }
    }

    private static void ExactClasses()
    {
        foreach (var mode in new[] { "wrong-source", "wrong-clip", "derived", "alias", "foreign", "zero-class" })
        {
            using var f = new Fixture(); var obj = mode == "wrong-clip" ? f.Clip : f.Source; using var root = f.Api.Root(obj);
            var cls = mode == "wrong-source" ? f.AudioClip.Class : mode == "wrong-clip" ? f.AudioSource.Class : f.AudioSource.Class;
            if (mode is "derived" or "alias" or "foreign")
            {
                var actual = mode == "alias" ? f.AddType("UnityEngine.AudioSource", "UnityEngine", "AudioSource", 18, false, 8, f.AudioImage, f.UnityObject.Class)
                    : f.AddType("Fixture.DerivedSource", "Fixture", "DerivedSource", 18, false, 8, f.AudioImage,
                        mode == "derived" ? f.AudioSource.Class : f.SystemObject.Class);
                Marshal.WriteIntPtr(obj, actual.Class); cls = actual.Class;
            }
            if (mode == "zero-class") cls = IntPtr.Zero;
            Throws<UnsupportedSignatureException>(() => f.Api.ReadUnityObjectBackingObservation(root, obj.ToInt64(), cls));
            Check(f.InstanceReads == 0 && f.StaticReads == 0 && root.Target == obj && f.FreedRoots == 0, mode);
            f.NoEffects();
        }
    }

    private static void ClassMetadata()
    {
        foreach (var mode in new[] { "source-image", "source-name", "source-type", "source-byref", "source-value", "source-parent",
            "object-image", "object-parent", "object-kind", "pointer-code", "integer-code", "pointer-enum", "integer-enum",
            "pointer-image", "integer-class-name", "cycle" })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Source);
            switch (mode)
            {
                case "source-image": f.AudioSource.Image = f.CoreImage; break;
                case "source-name": f.AudioSource.ShortName = "SourceAlias"; break;
                case "source-type": f.AudioSource.Name = "UnityEngine.AudioClip"; break;
                case "source-byref": f.AudioSource.ByRef = true; break;
                case "source-value": f.AudioSource.Value = true; break;
                case "source-parent": f.AudioSource.Parent = f.SystemObject.Class; break;
                case "object-image": f.UnityObject.Image = f.AudioImage; break;
                case "object-parent": f.UnityObject.Parent = IntPtr.Zero; break;
                case "object-kind": f.UnityObject.Code = 21; break;
                case "pointer-code": f.Pointer.Code = 8; break;
                case "integer-code": f.Integer.Code = 24; break;
                case "pointer-enum": f.Pointer.Enum = true; break;
                case "integer-enum": f.Integer.Enum = true; break;
                case "pointer-image": f.Pointer.Image = f.AudioImage; break;
                case "integer-class-name": f.Integer.ShortName = "IntegerAlias"; break;
                case "cycle": f.AudioSource.Parent = f.AudioSource.Class; f.AssignableOverride = true; break;
            }
            Throws<UnsupportedSignatureException>(() => f.Api.ReadUnityObjectBackingObservation(root, f.Source.ToInt64(), f.AudioSource.Class));
            Check(f.InstanceReads == 0 && f.StaticReads == 0 && f.ActiveRoots == 1 && f.FreedRoots == 0, mode);
            f.NoEffects();
        }
    }

    private static void RequiredExports()
    {
        foreach (var export in new[] { "il2cpp_field_get_parent", "il2cpp_class_instance_size", "il2cpp_class_get_image", "il2cpp_class_get_static_field_data" })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Source); f.MissingExport = export;
            Throws<UnsupportedSignatureException>(() => f.Api.ReadUnityObjectBackingObservation(root, f.Source.ToInt64(), f.AudioSource.Class));
            Check(f.InstanceReads == 0 && f.StaticReads == 0 && f.ActiveRoots == 1 && f.FreedRoots == 0, export);
            f.NoEffects();
        }
    }

    private static void MetadataChanges()
    {
        foreach (var boundary in new[] { 1, 2, 3, 4 })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Source); var reads = 0;
            f.AfterRead = (_, _) => { if (++reads == boundary) f.Cached.Flags |= 0x80; };
            Throws<UnsupportedSignatureException>(() => f.Api.ReadUnityObjectBackingObservation(root, f.Source.ToInt64(), f.AudioSource.Class));
            Check(f.InstanceReads + f.StaticReads == boundary && f.ActiveRoots == 1 && f.FreedRoots == 0);
            f.NoEffects();
        }
        using var shadow = new Fixture(); using var shadowRoot = shadow.Api.Root(shadow.Clip);
        shadow.AfterRead = (isStatic, _) => { if (!isStatic) shadow.AddField(shadow.AudioClip.Class, shadow.Offset.Name, shadow.Integer.Type, 0, 0x10); };
        Throws<UnsupportedSignatureException>(() => shadow.Api.ReadUnityObjectBackingObservation(shadowRoot, shadow.Clip.ToInt64(), shadow.AudioClip.Class));
        Check(shadow.InstanceReads == 1 && shadow.StaticReads == 1 && shadow.FreedRoots == 0); shadow.NoEffects();
    }

    private static void WarmMetadataChanges()
    {
        foreach (var mode in new[] { "pointer-code", "integer-code", "pointer-size", "integer-size", "pointer-byref", "integer-enum", "integer-image" })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Source);
            _ = f.Api.ReadUnityObjectBackingObservation(root, f.Source.ToInt64(), f.AudioSource.Class);
            switch (mode)
            {
                case "pointer-code": f.Pointer.Code = 8; break;
                case "integer-code": f.Integer.Code = 24; break;
                case "pointer-size": f.Pointer.Size = 4; break;
                case "integer-size": f.Integer.Size = 8; break;
                case "pointer-byref": f.Pointer.ByRef = true; break;
                case "integer-enum": f.Integer.Enum = true; break;
                case "integer-image": f.Integer.Image = f.AudioImage; break;
            }
            Throws<UnsupportedSignatureException>(() => f.Api.ReadUnityObjectBackingObservation(root, f.Source.ToInt64(), f.AudioSource.Class));
            Check(f.InstanceReads == 2 && f.StaticReads == 2 && f.ActiveRoots == 1 && f.FreedRoots == 0, mode);
            f.NoEffects();
        }
    }

    private static void ValueChanges()
    {
        foreach (var mode in new[] { "source-pointer", "clip-pointer", "source-zero", "offset" })
        {
            using var f = new Fixture(); var obj = mode == "clip-pointer" ? f.Clip : f.Source;
            var cls = mode == "clip-pointer" ? f.AudioClip.Class : f.AudioSource.Class; using var root = f.Api.Root(obj);
            f.BeforeRead = (isStatic, count) => {
                if (count != 2) return;
                if (mode == "offset" && isStatic) Marshal.WriteInt32(f.StaticFields, 12);
                if (mode != "offset" && !isStatic) Marshal.WriteIntPtr(obj, 0x10, mode == "source-zero" ? IntPtr.Zero : f.OtherNative);
            };
            Throws<TargetExpiredException>(() => f.Api.ReadUnityObjectBackingObservation(root, obj.ToInt64(), cls));
            Check(f.StaticReads == 2 && f.InstanceReads == 2 && root.Target == obj && f.ActiveRoots == 1 && f.FreedRoots == 0, mode);
            f.NoEffects();
        }
    }

    private static void RootAndClassChanges()
    {
        foreach (var mode in new[] { "root", "class" })
            foreach (var boundary in new[] { 1, 2, 3, 4 })
            {
                using var f = new Fixture(); using var root = f.Api.Root(f.Source); var reads = 0;
                f.AfterRead = (_, _) => {
                    if (++reads != boundary) return;
                    if (mode == "root") f.ChangeRootTarget(f.Source, f.OtherSource);
                    else Marshal.WriteIntPtr(f.Source, f.AudioClip.Class);
                };
                Throws<TargetExpiredException>(() => f.Api.ReadUnityObjectBackingObservation(root, f.Source.ToInt64(), f.AudioSource.Class));
                Check(f.InstanceReads + f.StaticReads == boundary && f.ActiveRoots == 1 && f.NewRoots == 1 && f.FreedRoots == 0, mode);
                f.NoEffects();
            }
    }

    private static void RootsAndThreads()
    {
        using var f = new Fixture(); using var root = f.Api.Root(f.Source);
        var disposed = f.Api.Root(f.Source); disposed.Dispose(); var metadata = f.MetadataReads;
        Throws<ArgumentNullException>(() => f.Api.ReadUnityObjectBackingObservation(null!, f.Source.ToInt64(), f.AudioSource.Class));
        Throws<TargetExpiredException>(() => f.Api.ReadUnityObjectBackingObservation(disposed, f.Source.ToInt64(), f.AudioSource.Class));
        Throws<TargetExpiredException>(() => f.Api.ReadUnityObjectBackingObservation(root, f.Clip.ToInt64(), f.AudioSource.Class));
        Throws<UnsupportedSignatureException>(() => f.Api.ReadUnityObjectBackingObservation(root, 0, f.AudioSource.Class));
        Task.Run(() => Throws<InvalidOperationException>(() => f.Api.ReadUnityObjectBackingObservation(root, f.Source.ToInt64(), f.AudioSource.Class)))
            .GetAwaiter().GetResult();
        var access = new NativeThreadAccess(); Task.Run(access.BindCurrentThread).GetAwaiter().GetResult();
        var targets = 0; var frees = 0;
        var foreign = new NativeRuntime.RootHandle(123, _ => ++frees, _ => { ++targets; return f.Source; }, access.Verify);
        Throws<InvalidOperationException>(() => f.Api.ReadUnityObjectBackingObservation(foreign, f.Source.ToInt64(), f.AudioSource.Class));
        Check(f.MetadataReads == metadata && f.InstanceReads == 0 && f.StaticReads == 0 && targets == 0 && frees == 0
            && f.ActiveRoots == 1 && f.NewRoots == 2 && f.FreedRoots == 1);
        // Synthetic foreign root has no native resource; do not consume its
        // thread-owned responsibility by disposing it on this thread.
        GC.KeepAlive(foreign); f.NoEffects();
    }

    private static void NativeIdIsNotAuthority()
    {
        using var f = new Fixture(); using var root = f.Api.Root(f.Source);
        var first = f.Api.ReadUnityObjectBackingObservation(root, f.Source.ToInt64(), f.AudioSource.Class);
        Marshal.WriteInt32(f.NativeSource, 8, int.MinValue);
        var second = f.Api.ReadUnityObjectBackingObservation(root, f.Source.ToInt64(), f.AudioSource.Class);
        Check(first == second && Marshal.ReadInt32(f.NativeSource, 8) == int.MinValue && f.InstanceReads == 4 && f.StaticReads == 4
            && f.NewRoots == 1 && f.FreedRoots == 0);
        Marshal.WriteIntPtr(f.Source, 0x10, f.OtherNative);
        var third = f.Api.ReadUnityObjectBackingObservation(root, f.Source.ToInt64(), f.AudioSource.Class);
        Check(third != first && third.ManagedObject == first.ManagedObject && third.ExactClass == first.ExactClass
            && third.CachedNativePointer == f.OtherNative.ToInt64() && f.NewRoots == 1 && f.FreedRoots == 0);
        f.NoEffects();
    }

    private static void StaticDataStorage()
    {
        foreach (var address in new[] { IntPtr.Zero, new IntPtr(1) })
        {
            using var f = new Fixture(); using var root = f.Api.Root(f.Source);
            Marshal.WriteIntPtr(f.UnityObject.Class, 0xB8, address);
            Throws<UnsupportedSignatureException>(() => f.Api.ReadUnityObjectBackingObservation(root, f.Source.ToInt64(), f.AudioSource.Class));
            Check(f.StaticReads == 1 && f.GenericStaticReads == 0 && f.InstanceReads == 0 && f.ActiveRoots == 1 && f.FreedRoots == 0);
            f.NoEffects();
        }
        using var moved = new Fixture(); using var movedRoot = moved.Api.Root(moved.Source);
        moved.AfterRead = (isStatic, count) => {
            if (isStatic && count == 1) Marshal.WriteIntPtr(moved.UnityObject.Class, 0xB8, moved.OtherStaticFields);
        };
        Throws<TargetExpiredException>(() => moved.Api.ReadUnityObjectBackingObservation(movedRoot, moved.Source.ToInt64(), moved.AudioSource.Class));
        Check(moved.StaticReads == 2 && moved.InstanceReads == 1 && moved.GenericStaticReads == 0
            && moved.ActiveRoots == 1 && moved.FreedRoots == 0); moved.NoEffects();
    }

    private sealed class Fixture : IDisposable
    {
        internal sealed class TypeRow
        {
            internal string Name = "", Namespace = "", ShortName = "";
            internal IntPtr Type, Class, Parent, Image;
            internal int Code, Size;
            internal uint InstanceSize = 0x40;
            internal bool Value, ByRef, Enum;
        }
        internal sealed class FieldRow
        {
            internal string Name = "";
            internal IntPtr Pointer, Class, Type, OwnerOverride;
            internal int Offset;
            internal uint Flags;
        }
        private readonly List<IntPtr> _allocations = new();
        private readonly Dictionary<string, Delegate> _exports = new();
        private readonly Dictionary<IntPtr, TypeRow> _types = new(), _classes = new();
        private readonly Dictionary<(IntPtr, string, string), IntPtr> _definitions = new();
        private readonly List<FieldRow> _fields = new();
        private readonly HashSet<IntPtr> _instances = new();
        private readonly Dictionary<uint, IntPtr> _handles = new();
        private uint _nextHandle;
        private readonly IntPtr _assemblies;
        internal readonly IntPtr SystemImage, CoreImage, AudioImage, Source, Clip, OtherSource, NativeSource, NativeClip, OtherNative, StaticFields, OtherStaticFields;
        internal readonly TypeRow SystemObject, UnityObject, AudioSource, AudioClip, Pointer, Integer;
        internal readonly FieldRow Cached, Offset;
        internal readonly NativeRuntime Api;
        internal int InstanceReads, StaticReads, GenericStaticReads, MetadataReads, NewRoots, FreedRoots, Invocations;
        internal int ActiveRoots => _handles.Count;
        internal string? MissingExport, ExportFailure, UnexpectedExport;
        internal bool AssignableOverride;
        internal Action<bool, int>? BeforeRead, AfterRead;

        internal Fixture()
        {
            SystemImage = Allocate(16); CoreImage = Allocate(16); AudioImage = Allocate(16); _assemblies = Allocate(24);
            Marshal.WriteIntPtr(_assemblies, SystemImage); Marshal.WriteIntPtr(_assemblies, 8, CoreImage); Marshal.WriteIntPtr(_assemblies, 16, AudioImage);
            SystemObject = AddType("System.Object", "System", "Object", 28, false, 8, SystemImage);
            UnityObject = AddType("UnityEngine.Object", "UnityEngine", "Object", 18, false, 8, CoreImage, SystemObject.Class);
            UnityObject.InstanceSize = 0x18;
            AudioSource = AddType("UnityEngine.AudioSource", "UnityEngine", "AudioSource", 18, false, 8, AudioImage, UnityObject.Class);
            AudioClip = AddType("UnityEngine.AudioClip", "UnityEngine", "AudioClip", 18, false, 8, AudioImage, UnityObject.Class);
            Pointer = AddType("System.IntPtr", "System", "IntPtr", 24, true, 8, SystemImage);
            Integer = AddType("System.Int32", "System", "Int32", 8, true, 4, SystemImage);
            Cached = AddField(UnityObject.Class, "m_CachedPtr", Pointer.Type, 0x10);
            Offset = AddField(UnityObject.Class, "OffsetOfInstanceIDInCPlusPlusObject", Integer.Type, 0, 0x10);
            Source = Instance(AudioSource.Class); Clip = Instance(AudioClip.Class); OtherSource = Instance(AudioSource.Class);
            NativeSource = Allocate(0x404); NativeClip = Allocate(0x404); OtherNative = Allocate(0x404); StaticFields = Allocate(0x40); OtherStaticFields = Allocate(0x40);
            Marshal.WriteIntPtr(Source, 0x10, NativeSource); Marshal.WriteIntPtr(Clip, 0x10, NativeClip); Marshal.WriteIntPtr(OtherSource, 0x10, OtherNative);
            Marshal.WriteInt32(NativeSource, 8, -1234); Marshal.WriteInt32(NativeClip, 8, 5678); Marshal.WriteInt32(OtherNative, 8, 2345);
            Marshal.WriteInt32(StaticFields, 8);
            Marshal.WriteInt32(OtherStaticFields, 8);
            // Include real class static-fields storage for the companion native
            // fixture shape; this projection still uses only official getters.
            Marshal.WriteIntPtr(UnityObject.Class, 0xB8, StaticFields);

            Bind("il2cpp_domain_get", "Ptr0", nameof(Domain));
            Bind("il2cpp_domain_get_assemblies", "Ptr2", nameof(Assemblies));
            Bind("il2cpp_assembly_get_image", "Ptr1", nameof(Identity)); Bind("il2cpp_image_get_name", "Ptr1", nameof(ImageName));
            Bind("il2cpp_class_from_name", "ClassLookup", nameof(FindClass)); Bind("il2cpp_object_get_class", "Ptr1", nameof(GetObjectClass));
            Bind("il2cpp_class_get_parent", "Ptr1", nameof(Parent)); Bind("il2cpp_class_get_name", "Ptr1", nameof(ClassName));
            Bind("il2cpp_class_get_namespace", "Ptr1", nameof(ClassNamespace)); Bind("il2cpp_class_get_type", "Ptr1", nameof(ClassType));
            Bind("il2cpp_class_get_image", "Ptr1", nameof(ClassImage)); Bind("il2cpp_class_instance_size", "ArraySize", nameof(InstanceSize));
            Bind("il2cpp_class_get_static_field_data", "Ptr1", nameof(StaticData));
            Bind("il2cpp_class_from_type", "Ptr1", nameof(TypeClass)); Bind("il2cpp_type_get_name", "Ptr1", nameof(TypeName));
            Bind("il2cpp_type_get_type", "TypeCode", nameof(TypeCode)); Bind("il2cpp_type_is_byref", "Predicate", nameof(ByRef));
            Bind("il2cpp_class_is_valuetype", "Predicate", nameof(ValueType)); Bind("il2cpp_class_is_enum", "Predicate", nameof(IsEnum));
            Bind("il2cpp_class_value_size", "ValueSize", nameof(ValueSize)); Bind("il2cpp_class_is_assignable_from", "Assignable", nameof(Assignable));
            Bind("il2cpp_class_get_fields", "Ptr2", nameof(Fields)); Bind("il2cpp_class_get_field_from_name", "FieldLookup", nameof(FindField));
            Bind("il2cpp_field_get_parent", "Ptr1", nameof(FieldParent)); Bind("il2cpp_field_get_name", "Ptr1", nameof(FieldName));
            Bind("il2cpp_field_get_type", "Ptr1", nameof(FieldType)); Bind("il2cpp_field_get_flags", "Flags", nameof(FieldFlags));
            Bind("il2cpp_field_get_offset", "FieldOffset", nameof(FieldOffset)); Bind("il2cpp_field_get_value", "FieldGet", nameof(ReadField));
            Bind("il2cpp_field_static_get_value", "StaticGet", nameof(ReadStatic)); Bind("il2cpp_free", "FreePointer", nameof(Free));
            Bind("il2cpp_gchandle_new", "NewHandle", nameof(NewHandle)); Bind("il2cpp_gchandle_free", "FreeHandle", nameof(FreeHandle));
            Bind("il2cpp_gchandle_get_target", "HandleTarget", nameof(HandleTarget)); Bind("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(Invoke));
            Default("il2cpp_method_get_name", "Ptr1"); Default("il2cpp_method_get_class", "Ptr1");
            Default("il2cpp_object_unbox", "Ptr1"); Default("il2cpp_object_new", "Ptr1"); Default("il2cpp_type_get_object", "Ptr1");
            Default("il2cpp_class_get_method_from_name", "MethodLookup"); Default("il2cpp_class_get_methods", "Ptr2");
            Default("il2cpp_method_get_param_count", "Flags"); Default("il2cpp_method_get_flags", "MethodFlags");
            Default("il2cpp_method_get_param", "ParameterType"); Default("il2cpp_method_get_return_type", "Ptr1");
            Default("il2cpp_string_new", "StringNew"); Default("il2cpp_string_length", "StringLength"); Default("il2cpp_string_chars", "Ptr1");
            Default("il2cpp_class_enum_basetype", "Ptr1"); Default("il2cpp_method_is_generic", "Predicate"); Default("il2cpp_method_is_inflated", "Predicate");
            Api = new NativeRuntime(export => {
                if (export == MissingExport) throw new EntryPointNotFoundException("Synthetic absent " + export);
                if (!_exports.TryGetValue(export, out var value)) throw new EntryPointNotFoundException("Unexpected requested export " + export);
                return Marshal.GetFunctionPointerForDelegate(value);
            }); Api.BindCurrentThread();
        }

        internal TypeRow AddType(string name, string ns, string shortName, int code, bool value, int size, IntPtr image, IntPtr parent = default)
        {
            var row = new TypeRow { Name = name, Namespace = ns, ShortName = shortName, Code = code, Value = value, Size = size,
                Image = image, Parent = parent, Type = Allocate(16), Class = Allocate(0x200) };
            _types.Add(row.Type, row); _classes.Add(row.Class, row); _definitions.TryAdd((image, ns, shortName), row.Class); return row;
        }
        internal FieldRow AddField(IntPtr cls, string name, IntPtr type, int offset, uint flags = 0)
        {
            var row = new FieldRow { Pointer = Allocate(16), Class = cls, Name = name, Type = type, Offset = offset, Flags = flags };
            _fields.Add(row); return row;
        }
        internal void ChangeRootTarget(IntPtr before, IntPtr after)
        { foreach (var id in _handles.Where(pair => pair.Value == before).Select(pair => pair.Key).ToArray()) _handles[id] = after; }
        internal void NoEffects() => Check(Invocations == 0 && GenericStaticReads == 0 && ExportFailure is null && UnexpectedExport is null,
            "Unexpected effects/export: " + ExportFailure + " / " + UnexpectedExport);
        private IntPtr Allocate(int size)
        { var pointer = Marshal.AllocHGlobal(size); _allocations.Add(pointer); Marshal.Copy(new byte[size], 0, pointer, size); return pointer; }
        private IntPtr Instance(IntPtr cls)
        { var pointer = Allocate(0x40); Marshal.WriteIntPtr(pointer, cls); _instances.Add(pointer); return pointer; }
        private IntPtr Text(string value)
        { var pointer = Marshal.StringToHGlobalAnsi(value); _allocations.Add(pointer); return pointer; }
        private void Metadata() => ++MetadataReads;
        private void Bind(string export, string type, string method)
            => _exports[export] = Delegate.CreateDelegate(typeof(NativeRuntime).GetNestedType(type, BindingFlags.NonPublic)!, this,
                GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!);
        private void Unexpected(string export) => UnexpectedExport = export;
        private void Default(string export, string type)
        {
            var delegateType = typeof(NativeRuntime).GetNestedType(type, BindingFlags.NonPublic)!; var invoke = delegateType.GetMethod("Invoke")!;
            var parameters = invoke.GetParameters().Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name)).ToArray();
            var body = new List<Expression> { Expression.Call(Expression.Constant(this), GetType().GetMethod(nameof(Unexpected), BindingFlags.Instance | BindingFlags.NonPublic)!, Expression.Constant(export)) };
            foreach (var parameter in parameters)
                if (parameter.Type.IsByRef) body.Add(Expression.Assign(parameter, Expression.Default(parameter.Type.GetElementType()!)));
            body.Add(Expression.Default(invoke.ReturnType)); _exports[export] = Expression.Lambda(delegateType, Expression.Block(body), parameters).Compile();
        }

        // Exports remain total over fixture inputs and record invalid requests;
        // tests never throw an exception across a reverse-P/Invoke boundary.
        private TypeRow ClassRow(IntPtr cls)
        { if (_classes.TryGetValue(cls, out var row)) return row; ExportFailure = "Unknown class"; return SystemObject; }
        private TypeRow TypeRowFor(IntPtr type)
        { if (_types.TryGetValue(type, out var row)) return row; ExportFailure = "Unknown type"; return Integer; }
        private FieldRow FieldRowFor(IntPtr field)
        { var row = _fields.FirstOrDefault(candidate => candidate.Pointer == field); if (row is not null) return row; ExportFailure = "Unknown field"; return Cached; }
        private IntPtr Domain() => SystemImage;
        private IntPtr Assemblies(IntPtr domain, IntPtr size) { Metadata(); Marshal.WriteIntPtr(size, new IntPtr(3)); return _assemblies; }
        private IntPtr Identity(IntPtr value) { Metadata(); return value; }
        private IntPtr ImageName(IntPtr image)
        { Metadata(); return Text(image == SystemImage ? "mscorlib.dll" : image == CoreImage ? "UnityEngine.CoreModule.dll" : image == AudioImage ? "UnityEngine.AudioModule.dll" : "Unknown"); }
        private IntPtr FindClass(IntPtr image, string ns, string name) { Metadata(); return _definitions.GetValueOrDefault((image, ns, name)); }
        private IntPtr GetObjectClass(IntPtr value)
        { Metadata(); if (_instances.Contains(value)) return Marshal.ReadIntPtr(value); ExportFailure = "Unknown wrapper"; return IntPtr.Zero; }
        private IntPtr Parent(IntPtr cls) { Metadata(); return ClassRow(cls).Parent; }
        private IntPtr ClassName(IntPtr cls) { Metadata(); return Text(ClassRow(cls).ShortName); }
        private IntPtr ClassNamespace(IntPtr cls) { Metadata(); return Text(ClassRow(cls).Namespace); }
        private IntPtr ClassType(IntPtr cls) { Metadata(); return ClassRow(cls).Type; }
        private IntPtr ClassImage(IntPtr cls) { Metadata(); return ClassRow(cls).Image; }
        private uint InstanceSize(IntPtr cls) { Metadata(); return ClassRow(cls).InstanceSize; }
        private IntPtr TypeClass(IntPtr type) { Metadata(); return TypeRowFor(type).Class; }
        private IntPtr TypeName(IntPtr type) { Metadata(); return Marshal.StringToCoTaskMemUTF8(TypeRowFor(type).Name); }
        private int TypeCode(IntPtr type) { Metadata(); return TypeRowFor(type).Code; }
        private bool ByRef(IntPtr type) { Metadata(); return TypeRowFor(type).ByRef; }
        private bool ValueType(IntPtr cls) { Metadata(); return ClassRow(cls).Value; }
        private bool IsEnum(IntPtr cls) { Metadata(); return ClassRow(cls).Enum; }
        private int ValueSize(IntPtr cls, out uint alignment) { Metadata(); alignment = (uint)ClassRow(cls).Size; return ClassRow(cls).Size; }
        private bool Assignable(IntPtr expected, IntPtr actual)
        {
            Metadata(); if (AssignableOverride) return true;
            for (var depth = 0; actual != IntPtr.Zero && depth < 32; ++depth, actual = ClassRow(actual).Parent)
                if (actual == expected) return true;
            return false;
        }
        private IntPtr Fields(IntPtr cls, IntPtr iterator)
        {
            Metadata(); var rows = _fields.Where(row => row.Class == cls).ToArray(); var index = Marshal.ReadIntPtr(iterator).ToInt32();
            if (index < 0 || index >= rows.Length) return IntPtr.Zero;
            Marshal.WriteIntPtr(iterator, new IntPtr(index + 1)); return rows[index].Pointer;
        }
        private IntPtr FindField(IntPtr cls, string name)
        {
            Metadata(); for (var depth = 0; cls != IntPtr.Zero && depth < 32; ++depth, cls = ClassRow(cls).Parent)
            { var row = _fields.FirstOrDefault(field => field.Class == cls && field.Name == name); if (row is not null) return row.Pointer; }
            return IntPtr.Zero;
        }
        private IntPtr FieldParent(IntPtr field) { Metadata(); var row = FieldRowFor(field); return row.OwnerOverride == IntPtr.Zero ? row.Class : row.OwnerOverride; }
        private IntPtr FieldName(IntPtr field) { Metadata(); return Text(FieldRowFor(field).Name); }
        private IntPtr FieldType(IntPtr field) { Metadata(); return FieldRowFor(field).Type; }
        private uint FieldFlags(IntPtr field) { Metadata(); return FieldRowFor(field).Flags; }
        private int FieldOffset(IntPtr field) { Metadata(); return FieldRowFor(field).Offset; }
        private void ReadField(IntPtr obj, IntPtr field, IntPtr output)
        {
            ++InstanceReads; BeforeRead?.Invoke(false, InstanceReads);
            if (!_instances.Contains(obj) || field != Cached.Pointer) { ExportFailure = "Unexpected instance read"; Marshal.WriteIntPtr(output, IntPtr.Zero); }
            else Marshal.WriteIntPtr(output, Marshal.ReadIntPtr(obj, Cached.Offset));
            AfterRead?.Invoke(false, InstanceReads);
        }
        private void ReadStatic(IntPtr field, IntPtr output)
        {
            ++GenericStaticReads;
            if (field != Offset.Pointer) { ExportFailure = "Unexpected static read"; Marshal.WriteInt32(output, 0); }
            else Marshal.WriteInt32(output, Marshal.ReadInt32(StaticFields, Offset.Offset));
        }
        private IntPtr StaticData(IntPtr cls)
        {
            ++StaticReads; BeforeRead?.Invoke(true, StaticReads);
            if (cls != UnityObject.Class) { ExportFailure = "Unexpected static-data class"; return IntPtr.Zero; }
            var storage = Marshal.ReadIntPtr(cls, 0xB8); AfterRead?.Invoke(true, StaticReads); return storage;
        }
        private void Free(IntPtr value) => Marshal.FreeCoTaskMem(value);
        private uint NewHandle(IntPtr obj, bool pinned)
        { if (!_instances.Contains(obj) || pinned) { ExportFailure = "Unexpected GC root"; return 0; } ++NewRoots; var id = ++_nextHandle; _handles.Add(id, obj); return id; }
        private void FreeHandle(uint id) { if (!_handles.Remove(id)) ExportFailure = "Unexpected root free"; ++FreedRoots; }
        private IntPtr HandleTarget(uint id) => _handles.GetValueOrDefault(id);
        private IntPtr Invoke(IntPtr method, IntPtr instance, IntPtr args, IntPtr exception)
        { ++Invocations; Marshal.WriteIntPtr(exception, IntPtr.Zero); return IntPtr.Zero; }
        public void Dispose() { foreach (var pointer in _allocations) Marshal.FreeHGlobal(pointer); GC.KeepAlive(_exports); }
    }

    private static void Check(bool condition, string reason = "Unity backing runtime invariant failed.")
    { if (!condition) throw new InvalidOperationException(reason); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
