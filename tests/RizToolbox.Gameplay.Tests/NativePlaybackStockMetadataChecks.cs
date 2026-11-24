using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class NativePlaybackStockMetadataChecks
{
    // Explicit offline runner. The mapped image is never initialized or called; all IL2CPP
    // exports below are synthetic metadata delegates backed by separately owned buffers.
    internal static int RunMapped(string path)
    {
        path = Path.GetFullPath(path);
        if (!path.StartsWith("D:\\riz-build-cache-next\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use a fresh isolated D-drive evidence copy.");
        var module = LoadLibraryEx(path, IntPtr.Zero, 1); // DONT_RESOLVE_DLL_REFERENCES
        if (module == IntPtr.Zero) throw new InvalidOperationException("Cannot map the offline evidence image: " + Marshal.GetLastWin32Error());
        var passed = 0; var failed = 0; NativePlaybackStockCodeProfile? unloadProfile = null;
        void Case(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
        }
        try
        {
            Case("stock resolver issues twenty-one exact declarations and twenty entries with one physical pitch alias", () =>
            {
                using var f = new MetadataFixture(module);
                var targets = f.Api.ResolvePlaybackStockTargets(module); targets.VerifyCurrent();
                Check(targets.Module == module && targets.Pause.Address == IntPtr.Add(module, 0x3fbd90)
                    && targets.ReplayScheduled.Abi == ExactHookAbi.PlaybackSingle1Void
                    && targets.ResumeScheduled.Address == IntPtr.Add(module, 0x3fc060)
                    && targets.ProgressPlay.Abi == ExactHookAbi.PlaybackBoolean1Void
                    && targets.AudioPause.Address == IntPtr.Add(module, 0x1511640)
                    && targets.PlayScheduled.Abi == ExactHookAbi.PlaybackDouble1Void
                    && targets.SetClip.Abi == ExactHookAbi.PlaybackReference1Void
                    && targets.SetTime.Address == IntPtr.Add(module, 0x1511c00)
                    && targets.SetTime.Abi == ExactHookAbi.PlaybackSingle1Void
                    && targets.ProgressMusicPlayer.Address == IntPtr.Add(module,0x401700)
                    && targets.ProgressMusicPlayer.Abi == ExactHookAbi.PlaybackReference1Void
                    && targets.PlayerConstructor.Address == IntPtr.Add(module, 0x36c3e0)
                    && targets.PlayerConstructor.Abi == ExactHookAbi.PlaybackReference1Void
                    && targets.AudioPlayHelper.Address == IntPtr.Add(module, 0x1511680)
                    && targets.AudioPlayHelper.Abi == ExactHookAbi.PlaybackUInt64_1Void
                    && targets.AudioPlayDelayed.Abi == ExactHookAbi.PlaybackDouble1Void
                    && targets.AudioPlay.Address == IntPtr.Add(module, 0x1511780)
                    && targets.AudioStop.Abi == ExactHookAbi.PlaybackVoid0
                    && targets.AudioStopBoolean.Abi == ExactHookAbi.PlaybackBoolean1Void
                    && targets.AudioSetLoop.Address == IntPtr.Add(module, 0x1511a70)
                    && targets.ProgressClip.Address == IntPtr.Add(module, 0x4016f0)
                    && targets.ProgressClip.Abi == ExactHookAbi.PlaybackReference1Void
                    && targets.GetTime.Abi == ExactHookAbi.InstanceSingleReturn0, "Exact target/ABI mismatch.");
                Check(targets.PitchInstance.MethodInfo != targets.PitchStatic.MethodInfo
                    && targets.PitchInstance.Address == targets.PitchStatic.Address
                    && targets.PitchInstance.Abi == ExactHookAbi.PlaybackPitchSingle1Void
                    && targets.PitchStatic.Abi == ExactHookAbi.PlaybackPitchSingle1Void, "The physical pitch alias was duplicated or lost.");
                var field = f.Api.InspectProgressClipResourceField(targets.ProgressClip.DeclaringClass);
                Check(f.Rows.Count == 21 && f.Rows.Values.Select(row => Marshal.ReadIntPtr(row.Method)).Distinct().Count() == 20
                    && field.Field == f.ResourceField && field.Offset == 0x30 && field.Size == IntPtr.Size
                    && field.Type == f.ResourceType && field.Type == f.Rows["ProgressClip"].Parameters[0]
                    && field.FieldClass == f.ResourceClass && f.Types[field.Type].Code == 21,
                    "The new physical resource entry or closed constructed field metadata was omitted.");
                Check(f.Base.Invocations == 0 && f.Base.NewRoots == 0 && f.Base.ObjectAllocations == 0, "Metadata resolution invoked or allocated game objects.");
            });
            Case("stock target receipt is sealed and has no caller target constructor", () =>
            {
                var receipt = typeof(NativeRuntime.NativePlaybackStockTargets);
                Check(receipt.IsSealed && receipt.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .All(c => c.IsPrivate), "A caller can construct a stock target receipt.");
                Check(receipt.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                    .Where(m => m.ReturnType == receipt).All(m => m.GetParameters().Select(p => p.ParameterType)
                    .SequenceEqual(new[] { typeof(NativeRuntime), typeof(IntPtr) })), "A stock receipt accepts copied target/metadata input.");
            });

            var mutations = new (string Name, Action<MetadataFixture> Change)[]
            {
                ("resource setter has another exact name", f => f.Rename("ProgressClip", "set_Resource")),
                ("resource setter is missing from its declaring class", f => f.Rows["ProgressClip"].ListedClass = f.Source),
                ("resource setter has an ambiguous duplicate declaration", f => f.DuplicateResourceSetter()),
                ("resource setter has another declaring owner", f => f.Update("ProgressClip", row => row.DeclaringClass = f.Source)),
                ("resource setter has no parameter", f => f.SetParameters("ProgressClip")),
                ("resource setter has another parameter", f => f.SetParameters("ProgressClip", f.ResourceType, f.ResourceType)),
                ("resource setter takes AudioClip rather than its reference", f => f.Rows["ProgressClip"].Parameters[0] = f.Rows["Clip"].Parameters[0]),
                ("resource setter reference is a same-name foreign class", f => f.Rows["ProgressClip"].Parameters[0] = f.Base.AddType("IResourceReference<UnityEngine.AudioClip>", 21, false, 8)),
                ("resource parameter changed from GENERICINST21 to CLASS18", f => f.Types[f.ResourceType] = f.Types[f.ResourceType] with { Code = 18 }),
                ("resource reference changed to byref", f => f.Types[f.ResourceType] = f.Types[f.ResourceType] with { ByRef = true }),
                ("resource reference changed to value storage", f => f.Types[f.ResourceType] = f.Types[f.ResourceType] with { ValueType = true }),
                ("resource reference has wrong constructed type", f => f.Types[f.ResourceType] = f.Types[f.ResourceType] with { Name = "IResourceReference<UnityEngine.AudioSource>" }),
                ("resource class is an open generic", f => f.ResourceClassGeneric = true),
                ("resource class lacks closed inflation", f => f.ResourceClassInflated = false),
                ("resource class has no canonical constructed type", f => f.ResourceClassType = IntPtr.Zero),
                ("resource class canonical type is another reference", f => f.ResourceClassType = f.SourceType),
                ("resource class canonical type maps to a foreign constructed class", f => f.ResourceClassType = f.Base.AddType("IResourceReference<UnityEngine.AudioClip>", 21, false, IntPtr.Size)),
                ("resource class canonical type is byref", f => { f.ResourceClassType = f.ResourceTypeAlias(); f.Types[f.ResourceClassType] = f.Types[f.ResourceClassType] with { ByRef = true }; }),
                ("resource class name lacks generic arity", f => f.ResourceClassName("IResourceReference")),
                ("resource class has another namespace", f => f.ResourceClassName("IResourceReference`1", "Other")),
                ("resource class has no official image", f => f.ResourceImage = IntPtr.Zero),
                ("resource setter changed to static", f => f.Update("ProgressClip", row => row.Flags = 0x10)),
                ("resource setter changed to generic method", f => f.Rows["ProgressClip"].Generic = true),
                ("resource setter changed to inflated method", f => f.Rows["ProgressClip"].Inflated = true),
                ("resource setter changed to abstract method", f => f.Update("ProgressClip", row => row.Flags |= 0x400)),
                ("resource setter changed to nonvoid", f => f.Update("ProgressClip", row => row.ReturnType = f.Base.Single)),
                ("resource setter RVA differs", f => f.Layout("ProgressClip", value => { value.MethodPointer += 1; return value; })),
                ("resource backing field has another offset", f => f.ResourceFieldOffset = 0x38),
                ("resource backing field has another exact name", f => f.ResourceFieldName("<Resource>k__BackingField")),
                ("resource backing field takes AudioClip", f => f.ResourceFieldType = f.Rows["Clip"].Parameters[0]),
                ("resource backing field has a same-name foreign class", f => f.ResourceFieldType = f.Base.AddType("IResourceReference<UnityEngine.AudioClip>", 21, false, 8)),
                ("resource backing field has another owner", f => f.ResourceFieldParent = f.Source),
                ("resource backing field is static", f => f.ResourceFieldFlags = 0x10),
                ("resource backing field is literal", f => f.ResourceFieldFlags = 0x40),
                ("resource backing field is absent", f => f.ResourceFieldMissing = true),
                ("resource backing field is ambiguous", f => f.ResourceFieldDuplicated = true),
                ("resource backing field is thread static", f => f.ResourceFieldOffset = -1),
                ("resource backing field exceeds actual instance", f => f.ProgressInstanceBytes = 0x30),
                ("UInt64 PlayHelper replaced by Double", f => f.Rows["AudioPlayHelper"].Parameters[1] = f.Double),
                ("UInt64 PlayHelper changed to byref", f => f.Types[f.Unsigned] = f.Types[f.Unsigned] with { ByRef = true }),
                ("UInt64 storage changed to four bytes", f => f.Types[f.Unsigned] = f.Types[f.Unsigned] with { Size = 4 }),
                ("PlayHelper static changed to instance", f => f.Update("AudioPlayHelper", row => row.Flags = 0)),
                ("PlayHelper source is a same-name foreign class", f => f.Rows["AudioPlayHelper"].Parameters[0] = f.Base.AddType("UnityEngine.AudioSource", 18, false, 8)),
                ("direct Double Play changed to Single", f => f.Rows["AudioPlayDelayed"].Parameters[0] = f.Base.Single),
                ("direct Stop Boolean changed to Int32", f => f.Rows["AudioStopBoolean"].Parameters[0] = f.Base.Int32),
                ("loop setter changed to static", f => f.Update("AudioSetLoop", row => row.Flags = 0x10)),
                ("shared constructor reference changed to Single", f => f.Rows["PlayerConstructor"].Parameters[0] = f.Base.Single),
                ("shared constructor has a same-name foreign source class", f => f.Rows["PlayerConstructor"].Parameters[0] = f.Base.AddType("UnityEngine.AudioSource", 18, false, 8)),
                ("shared constructor changed to static", f => f.Update("PlayerConstructor", row => row.Flags |= 0x10u)),
                ("shared constructor changed to generic", f => f.Rows["PlayerConstructor"].Generic = true),
                ("shared constructor RVA differs", f => f.Layout("PlayerConstructor", value => { value.MethodPointer += 1; return value; })),
                ("static pitch alias changed to instance", f => f.Update("PitchStatic", row => row.Flags &= ~0x10u)),
                ("instance pitch alias changed to static", f => f.Update("PitchInstance", row => row.Flags |= 0x10)),
                ("generic pitch alias", f => f.Rows["PitchStatic"].Generic = true),
                ("inflated replay declaration", f => f.Rows["Replay"].Inflated = true),
                ("abstract pause declaration", f => f.Update("Pause", row => row.Flags |= 0x400)),
                ("Single time setter replaced with Double", f => f.Rows["Time"].Parameters[0] = f.Double),
                ("Double schedule replaced with Single", f => f.Rows["Schedule"].Parameters[0] = f.Base.Single),
                ("Boolean progress replaced with Int32", f => f.Rows["Progress"].Parameters[0] = f.Base.Int32),
                ("wrong declared class despite matching method enumeration", f => f.Update("Replay", row => row.DeclaringClass = f.Source)),
                ("void setter changed to Single return", f => f.Update("PitchInstance", row => row.ReturnType = f.Base.Single)),
                ("GetTime changed to void return", f => f.Update("GetTime", row => row.ReturnType = f.Base.Void)),
                ("correctly named byref Single", f => f.Types[f.Base.Single] = f.Types[f.Base.Single] with { ByRef = true }),
                ("Double storage is four bytes", f => f.Types[f.Double] = f.Types[f.Double] with { Size = 4 }),
                ("Single typecode is incompatible", f => f.Types[f.Base.Single] = f.Types[f.Base.Single] with { Code = 13 }),
                ("static source reference has a same-name foreign class", f => f.Rows["PitchStatic"].Parameters[0] = f.Base.AddType("UnityEngine.AudioSource", 18, false, 8)),
                ("Progress player reference has a same-name foreign class", f => f.Rows["ProgressPlayer"].Parameters[0] = f.Base.AddType("Pigeon.MUGToolkit.Core.MUGMusicPlayer", 18, false, 8)),
                ("clip reference has a same-name foreign class", f => f.Rows["Clip"].Parameters[0] = f.Base.AddType("UnityEngine.AudioClip", 18, false, 8)),
                ("source class changed to value storage", f => f.Types[f.SourceType] = f.Types[f.SourceType] with { ValueType = true }),
                ("entry differs from exact RVA", f => f.Layout("Schedule", value => { value.MethodPointer += 1; return value; })),
                ("MethodInfo flags differ from exports", f => f.Layout("Pause", value => { value.Flags ^= 1; return value; })),
                ("MethodInfo parameter count differs from exports", f => f.Layout("PitchStatic", value => { value.ParameterCount = 1; return value; })),
                ("MethodInfo implementation flags differ from exports", f => f.Layout("Clip", value => { value.ImplementationFlags ^= 1; return value; })),
                ("MethodInfo name pointer differs from exports", f => f.Layout("Pause", value => { value.Name = f.Rows["Replay"].NamePointer; return value; })),
                ("MethodInfo generic bits differ from exports", f => f.Layout("PitchInstance", value => { value.Bitfield = 3; return value; }))
            };
            foreach (var mutation in mutations)
                Case("stock resolver rejects " + mutation.Name, () =>
                {
                    using var f = new MetadataFixture(module); mutation.Change(f);
                    Reject(() => f.Api.ResolvePlaybackStockTargets(module));
                    Check(f.Base.Invocations == 0 && f.Base.NewRoots == 0, "Rejected metadata touched native objects.");
                });
            Case("closed resource reference image need not equal Progress image", () =>
            {
                using var f = new MetadataFixture(module); f.ResourceImage = new IntPtr(0x7002);
                f.Api.ResolvePlaybackStockTargets(module).VerifyCurrent();
                Check(f.Base.Invocations == 0 && f.Base.NewRoots == 0, "Image metadata verification touched objects.");
            });
            Case("resource field and parameter may use distinct type records for the same closed class", () =>
            {
                using var f = new MetadataFixture(module); var alias = f.ResourceTypeAlias();
                f.SetParameters("ProgressClip", alias);
                var targets = f.Api.ResolvePlaybackStockTargets(module); targets.VerifyCurrent();
                var field = f.Api.InspectProgressClipResourceField(targets.ProgressClip.DeclaringClass);
                Check(field.Type != alias && field.FieldClass == f.ResourceClass
                    && f.ResourceTypeClasses[alias] == field.FieldClass, "Equivalent closed-class type records were confused with a foreign class.");
                Check(f.Base.Invocations == 0 && f.Base.NewRoots == 0, "Constructed type identity verification touched objects.");
            });
            foreach (var mutation in mutations.Where(value => value.Name.StartsWith("resource ", StringComparison.Ordinal)))
                Case("issued stock receipt rejects " + mutation.Name, () =>
                {
                    using var f = new MetadataFixture(module); var targets = f.Api.ResolvePlaybackStockTargets(module);
                    mutation.Change(f); Reject(targets.VerifyCurrent);
                    Check(f.Base.Invocations == 0 && f.Base.NewRoots == 0, "Resource receipt recheck touched objects.");
                });
            Case("issued stock receipt rejects a replacement resource field identity", () =>
            {
                using var f = new MetadataFixture(module); var targets = f.Api.ResolvePlaybackStockTargets(module);
                f.ReplaceResourceField(); Reject(targets.VerifyCurrent);
                Check(f.Base.Invocations == 0 && f.Base.NewRoots == 0, "Resource field identity recheck touched objects.");
            });
            Case("resolved stock receipt rechecks fresh scalar metadata after issuance", () =>
            {
                using var f = new MetadataFixture(module); var targets = f.Api.ResolvePlaybackStockTargets(module);
                f.Types[f.Base.Single] = f.Types[f.Base.Single] with { ByRef = true };
                Reject(targets.VerifyCurrent);
            });
            Case("resolved stock receipt rejects alias implementation mutation after issuance", () =>
            {
                using var f = new MetadataFixture(module); var targets = f.Api.ResolvePlaybackStockTargets(module);
                f.Update("PitchStatic", row => row.ImplementationFlags ^= 1);
                Reject(targets.VerifyCurrent);
            });
            Case("stock resolver rejects an absent or foreign allocation-base module", () =>
            {
                using var f = new MetadataFixture(module);
                Reject(() => f.Api.ResolvePlaybackStockTargets(IntPtr.Zero));
                Reject(() => f.Api.ResolvePlaybackStockTargets(IntPtr.Add(module, 0x1000)));
            });
            Case("stock resolver and issued receipt refuse foreign threads before metadata reads", () =>
            {
                using var f = new MetadataFixture(module); var targets = f.Api.ResolvePlaybackStockTargets(module);
                var reads = f.Reads;
                Task.Run(() => WrongThread(() => f.Api.ResolvePlaybackStockTargets(module))).GetAwaiter().GetResult();
                Task.Run(() => WrongThread(targets.VerifyCurrent)).GetAwaiter().GetResult();
                Check(f.Reads == reads, "A foreign thread consulted metadata."); targets.VerifyCurrent();
            });
            Case("stock fixed profile verifies all twenty physical entries and five independent readers", () =>
            {
                var profile = new NativePlaybackStockCodeProfile(module); profile.VerifyCurrent(); unloadProfile = profile;
                Check(NativePlaybackStockCodeProfile.HookBodies.Count == 20 && NativePlaybackStockCodeProfile.ReadOnlyBodies.Count == 5,
                    "A stock/inline/leaf/reader body was omitted.");
            });
            foreach (var body in NativePlaybackStockCodeProfile.HookBodies.Concat(NativePlaybackStockCodeProfile.ReadOnlyBodies))
                Case("stock fixed profile rejects and retains withdrawal for " + body.Name, () =>
                {
                    var profile = new NativePlaybackStockCodeProfile(module);
                    var address = IntPtr.Add(module, body.Rva + body.Length - 1); var original = Marshal.ReadByte(address);
                    Write(address, (byte)(original ^ 1));
                    try { Reject(profile.VerifyCurrent); }
                    finally { Write(address, original); }
                    Reject(profile.VerifyCurrent); // restoring bytes does not revive a withdrawn receipt
                    new NativePlaybackStockCodeProfile(module).VerifyCurrent();
                });
        }
        finally { if (!FreeLibrary(module)) throw new InvalidOperationException("The offline evidence mapping did not unload."); }
        Case("stock fixed profile rejects an unloaded pinned module", () =>
        { Check(unloadProfile is not null, "No valid mapped profile was captured."); Reject(unloadProfile!.VerifyCurrent); });
        Console.WriteLine($"{passed}/{passed + failed} stock metadata/code checks passed. Game code calls=0; DllMain calls=0; native object allocations=0.");
        return failed == 0 ? 0 : 1;
    }

    private sealed partial class MetadataFixture : IDisposable
    {
        internal sealed class Row
        {
            internal IntPtr Method, ListedClass, DeclaringClass, NamePointer, ReturnType;
            internal IntPtr[] Parameters = Array.Empty<IntPtr>();
            internal uint Flags, ImplementationFlags;
            internal bool Generic, Inflated;
        }
        internal readonly FakeNative Base = new();
        internal readonly Dictionary<string, Row> Rows = new(StringComparer.Ordinal);
        internal readonly Dictionary<IntPtr, FakeNative.TypeInfo> Types;
        internal readonly IntPtr Source, SourceType, Double, Unsigned;
        internal readonly NativeRuntime Api;
        internal int Reads;
        private readonly Dictionary<string, Delegate> _exports;
        private readonly Dictionary<IntPtr, Row> _methods = new();
        private readonly Dictionary<(IntPtr Image, string Namespace, string Name), IntPtr> _classes = new();
        private readonly Dictionary<IntPtr, (IntPtr Name, IntPtr Namespace)> _classNames = new();
        private readonly List<IntPtr> _allocations = new();
        private readonly IntPtr _assemblies, _gameImage = new(0x7001), _audioImage = new(0x7002), _gameName, _audioName;

        internal MetadataFixture(IntPtr module)
        {
            var fields = typeof(FakeNative).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
            Types = (Dictionary<IntPtr, FakeNative.TypeInfo>)fields.Single(f => f.Name == "_types").GetValue(Base)!;
            _exports = new((Dictionary<string, Delegate>)fields.Single(f => f.Name == "_exports").GetValue(Base)!);
            _gameName = String("Assembly-CSharp.dll"); _audioName = String("UnityEngine.AudioModule.dll");
            _assemblies = Allocate(3 * IntPtr.Size); Marshal.WriteIntPtr(_assemblies, _gameImage); Marshal.WriteIntPtr(_assemblies, IntPtr.Size, _audioImage);
            var player = Class(_gameImage, "Pigeon.MUGToolkit.Core", "MUGMusicPlayer", out var playerType);
            var progress = Class(_gameImage, "InLevel", "ProgressControl", out var progressType);
            ConfigureStartMetadata(module, progress, progressType);
            Source = Class(_audioImage, "UnityEngine", "AudioSource", out var sourceType); SourceType = sourceType;
            Class(_audioImage, "UnityEngine", "AudioClip", out var clipType);
            Double = Base.AddType("System.Double", 13, true, 8);
            Unsigned = Base.AddType("System.UInt64", 11, true, 8);
            Add("PlayerConstructor", player, ".ctor", 0x36c3e0, module, sourceType);
            Add("AudioPlayHelper", Source, "PlayHelper", 0x1511680, module, sourceType, Unsigned);
            Update("AudioPlayHelper", row => row.Flags = 0x10);
            Add("AudioPlayDelayed", Source, "Play", 0x1511730, module, Double);
            Add("AudioPlay", Source, "Play", 0x1511780, module);
            Add("AudioStop", Source, "Stop", 0x1511810, module);
            Add("AudioStopBoolean", Source, "Stop", 0x1511850, module, Base.Boolean);
            Add("AudioSetLoop", Source, "set_loop", 0x1511a70, module, Base.Boolean);
            Add("Pause", player, "Pause", 0x3fbd90, module);
            Add("Replay", player, "ReplayScheduled", 0x3fbe30, module, Base.Single);
            Add("Resume", player, "ResumeScheduled", 0x3fc060, module, Base.Single);
            Add("Progress", progress, "Play", 0x400700, module, Base.Boolean);
            Add("AudioPause", Source, "Pause", 0x1511640, module);
            Add("Schedule", Source, "PlayScheduled", 0x15116d0, module, Double);
            Add("PitchInstance", Source, "set_pitch", 0x15117c0, module, Base.Single);
            Add("PitchStatic", Source, "SetPitch", 0x15117c0, module, sourceType, Base.Single);
            Update("PitchStatic", row => row.Flags = 0x10);
            Add("Clip", Source, "set_clip", 0x1511a20, module, clipType);
            Add("Time", Source, "set_time", 0x1511c00, module, Base.Single);
            Add("ProgressPlayer", progress, "set_MusicPlayer", 0x401700, module, playerType);
            ConfigureProgressResourceMetadata(module, progress);
            Add("GetTime", player, "GetTime", 0x3fbb10, module);
            Update("GetTime", row => row.ReturnType = Base.Single);
            Export("il2cpp_domain_get", "Ptr0", nameof(Domain));
            Export("il2cpp_domain_get_assemblies", "Ptr2", nameof(Assemblies));
            Export("il2cpp_assembly_get_image", "Ptr1", nameof(Image));
            Export("il2cpp_image_get_name", "Ptr1", nameof(ImageName));
            Export("il2cpp_class_from_name", "ClassLookup", nameof(FindClass));
            Export("il2cpp_class_get_name", "Ptr1", nameof(ClassName));
            Export("il2cpp_class_get_namespace", "Ptr1", nameof(ClassNamespace));
            Export("il2cpp_class_get_methods", "Ptr2", nameof(Methods));
            Export("il2cpp_method_get_name", "Ptr1", nameof(MethodName));
            Export("il2cpp_method_get_class", "Ptr1", nameof(MethodClass));
            Export("il2cpp_method_get_param_count", "Flags", nameof(ParameterCount));
            Export("il2cpp_method_get_flags", "MethodFlags", nameof(Flags));
            Export("il2cpp_method_get_param", "ParameterType", nameof(Parameter));
            Export("il2cpp_method_get_return_type", "Ptr1", nameof(Return));
            Export("il2cpp_method_is_generic", "Predicate", nameof(Generic));
            Export("il2cpp_method_is_inflated", "Predicate", nameof(Inflated));
            Api = new NativeRuntime(name => Marshal.GetFunctionPointerForDelegate(_exports[name])); Api.BindCurrentThread();
        }
        private IntPtr Class(IntPtr image, string ns, string name, out IntPtr type)
        {
            type = Base.AddType(ns + "." + name, 18, false, 8); var cls = Base.ClassOf(type);
            _classes.Add((image, ns, name), cls); _classNames.Add(cls, (String(name), String(ns))); return cls;
        }
        private void Add(string key, IntPtr cls, string name, int rva, IntPtr module, params IntPtr[] parameters)
        {
            var method = Allocate(Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
            var args = parameters.Length == 0 ? IntPtr.Zero : Allocate(parameters.Length * IntPtr.Size);
            for (var i = 0; i < parameters.Length; ++i) Marshal.WriteIntPtr(args, i * IntPtr.Size, parameters[i]);
            var row = new Row { Method = method, ListedClass = cls, DeclaringClass = cls, NamePointer = String(name),
                ReturnType = Base.Void, Parameters = parameters };
            Rows.Add(key, row); _methods.Add(method, row);
            Marshal.StructureToPtr(new HookTargetValidation.MethodInfo2022 { MethodPointer = IntPtr.Add(module, rva),
                VirtualMethodPointer = IntPtr.Add(module, rva), Name = row.NamePointer, Class = cls, ReturnType = row.ReturnType,
                Parameters = args, ParameterCount = (byte)parameters.Length, Slot = ushort.MaxValue }, method, false);
        }
        internal void Update(string key, Action<Row> action)
        {
            var row = Rows[key]; action(row);
            Layout(key, value => { value.Class = row.DeclaringClass; value.ReturnType = row.ReturnType;
                value.Flags = (ushort)row.Flags; value.ImplementationFlags = (ushort)row.ImplementationFlags; return value; });
        }
        internal void Layout(string key, Func<HookTargetValidation.MethodInfo2022, HookTargetValidation.MethodInfo2022> change)
        { var pointer = Rows[key].Method; Marshal.StructureToPtr(change(Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(pointer)), pointer, false); }
        internal void Rename(string key, string name)
        {
            var pointer = String(name); Rows[key].NamePointer = pointer;
            Layout(key, value => { value.Name = pointer; return value; });
        }
        internal void SetParameters(string key, params IntPtr[] parameters)
        {
            Rows[key].Parameters = parameters;
            var args = parameters.Length == 0 ? IntPtr.Zero : Allocate(parameters.Length * IntPtr.Size);
            for (var index = 0; index < parameters.Length; ++index) Marshal.WriteIntPtr(args, index * IntPtr.Size, parameters[index]);
            Layout(key, value => { value.Parameters = args; value.ParameterCount = checked((byte)parameters.Length); return value; });
        }
        private IntPtr Allocate(int bytes) { var pointer = Marshal.AllocHGlobal(bytes); _allocations.Add(pointer); return pointer; }
        private IntPtr String(string text) { var pointer = Marshal.StringToHGlobalAnsi(text); _allocations.Add(pointer); return pointer; }
        private void Export(string name, string kind, string handler)
        {
            var type = typeof(NativeRuntime).GetNestedType(kind, BindingFlags.NonPublic)!;
            _exports[name] = Delegate.CreateDelegate(type, this, GetType().GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)!);
        }
        private IntPtr Domain() { Reads++; return new(0x7000); }
        private IntPtr Assemblies(IntPtr domain, IntPtr count)
        { Reads++; Marshal.WriteIntPtr(count, new(_clockReadCoreName == IntPtr.Zero ? 2 : 3)); return _assemblies; }
        private IntPtr Image(IntPtr assembly) { Reads++; return assembly; }
        private IntPtr ImageName(IntPtr image)
        { Reads++; return image == _gameImage ? _gameName : image == _clockReadCoreImage ? _clockReadCoreName : _audioName; }
        private IntPtr FindClass(IntPtr image, string ns, string name) { Reads++; return _classes.GetValueOrDefault((image, ns, name)); }
        private IntPtr ClassName(IntPtr cls) { Reads++; return _classNames[cls].Name; }
        private IntPtr ClassNamespace(IntPtr cls) { Reads++; return _classNames[cls].Namespace; }
        private IntPtr Methods(IntPtr cls, IntPtr iterator)
        {
            Reads++; var rows = Rows.Values.Where(row => row.ListedClass == cls).ToArray(); var index = Marshal.ReadIntPtr(iterator).ToInt32();
            if (index >= rows.Length) return IntPtr.Zero; Marshal.WriteIntPtr(iterator, new(index + 1)); return rows[index].Method;
        }
        private IntPtr MethodName(IntPtr method) { Reads++; return _methods[method].NamePointer; }
        private IntPtr MethodClass(IntPtr method) { Reads++; return _methods[method].DeclaringClass; }
        private uint ParameterCount(IntPtr method) { Reads++; return (uint)_methods[method].Parameters.Length; }
        private uint Flags(IntPtr method, out uint implementation) { Reads++; var row = _methods[method]; implementation = row.ImplementationFlags; return row.Flags; }
        private IntPtr Parameter(IntPtr method, uint index) { Reads++; return _methods[method].Parameters[index]; }
        private IntPtr Return(IntPtr method) { Reads++; return _methods[method].ReturnType; }
        private bool Generic(IntPtr method) { Reads++; return _methods[method].Generic; }
        private bool Inflated(IntPtr method) { Reads++; return _methods[method].Inflated; }
        public void Dispose() { foreach (var pointer in _allocations) Marshal.FreeHGlobal(pointer); Base.Dispose(); GC.KeepAlive(_exports); }
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is UnsupportedSignatureException or MissingMemberException) { return; }
        throw new InvalidOperationException("Expected stock metadata/code evidence to be rejected.");
    }
    private static void WrongThread(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException("Foreign metadata thread was accepted."); }
    private static void Write(IntPtr address, byte value)
    {
        if (!VirtualProtect(address, 1, 0x40, out var old)) throw new InvalidOperationException("Cannot edit the isolated mapping.");
        try { Marshal.WriteByte(address, value); }
        finally { VirtualProtect(address, 1, old, out _); FlushInstructionCache(GetCurrentProcess(), address, 1); }
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool VirtualProtect(IntPtr address, nuint bytes, uint protect, out uint old);
    [DllImport("kernel32")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, nuint bytes);
}
