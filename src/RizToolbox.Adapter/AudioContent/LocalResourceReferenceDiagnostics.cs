using RizToolbox.Runtime;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record LocalResourceReferenceEvidence(string Id, string Status, IReadOnlyDictionary<string, string> Values);
internal sealed record LocalResourceReferenceDiagnosticReport(string Stage, string Build, DateTimeOffset CapturedUtc,
    IReadOnlyList<LocalResourceReferenceEvidence> Evidence);

/// <summary>Explicit opt-in, main-thread read-only evidence. Not a capability, loader, or ResourceReference factory.</summary>
internal sealed class LocalResourceReferenceDiagnostics
{
    private const string Sprite = "UnityEngine.Sprite", Audio = "UnityEngine.AudioClip";
    private readonly NativeRuntime _api;
    private readonly AdapterBuildProfile _profile;
    internal LocalResourceReferenceDiagnostics(NativeRuntime api, AdapterBuildProfile profile) { _api = api; _profile = profile; }

    internal LocalResourceReferenceDiagnosticReport CaptureMetadata(IntPtr gameAssemblyModule)
    {
        RequireBuild(gameAssemblyModule);
        var rows = new List<LocalResourceReferenceEvidence>();
        Capture(rows, "inside.params", () =>
        {
            var cls = _api.Class("Assembly-CSharp.dll", "", "InsideLevelParams");
            CaptureField(rows, "inside.cover", cls, "coverReference", Sprite, gameAssemblyModule);
            CaptureField(rows, "inside.music", cls, "musicReference", Audio, gameAssemblyModule);
            CaptureMethod(rows, "inside.ctor", cls, ".ctor", new[] { "ILevelMetadata", Interface(Sprite), Interface(Audio), "InLevel.Chart", "JudgeData" }, "System.Void", gameAssemblyModule);
        });
        Capture(rows, "sprite.concrete", () =>
        {
            var parent = _api.Class("Assembly-CSharp.dll", "", "WorkshopLevelCardControl");
            var machine = _api.NestedClass(parent, "<StartPlayLevel>d__41");
            var field = _api.InspectMetadataFieldClass(machine, "<coverReference>5__2", Concrete(Sprite));
            AddField(rows, "sprite.concrete.field", field);
            CaptureConcreteMethods(rows, "sprite.concrete", field.FieldClass.Class, Sprite, gameAssemblyModule);
        });
        Add(rows, "audio.concrete", "unknown", ("Reason", "No exact ResourceReference<AudioClip> field is known. Requires an existing live reference; no generic wrapper is constructed."));
        Add(rows, "interface.dispatch", "unknown", ("Reason", "Metadata-only capture does not resolve an interface against an existing object and does not prove candidate RVA AEF1F0 or interface slot1 mapping."));
        return Report("metadata-only", rows);
    }

    /// <summary>Materializes only BCL reflection metadata, with the already-known Sprite closed field as an identity control.</summary>
    internal LocalResourceReferenceDiagnosticReport CaptureClosedWrapperMetadata(IntPtr gameAssemblyModule)
    {
        RequireBuild(gameAssemblyModule);
        var rows = new List<LocalResourceReferenceEvidence>();
        NativeMetadataClass? controlledSprite = null;
        Capture(rows, "local.wrapper.sprite.control", () =>
        {
            var definition = _api.Class("Assembly-CSharp.dll", "", "ResourceReference`1");
            var sprite = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Sprite");
            var parent = _api.Class("Assembly-CSharp.dll", "", "WorkshopLevelCardControl");
            var machine = _api.NestedClass(parent, "<StartPlayLevel>d__41");
            var known = _api.InspectMetadataFieldClass(machine, "<coverReference>5__2", Concrete(Sprite));
            var reflected = _api.ReflectSingleReferenceGenericClass(definition, sprite, Concrete(Sprite));
            if (reflected.Class != known.FieldClass.Class)
                throw new UnsupportedSignatureException("Reflected Sprite wrapper differs from the existing exact field's closed class.");
            controlledSprite = reflected;
            Add(rows, "local.wrapper.sprite.control", "closed-type-verified", ("ClosedClass", Pointer(reflected.Class)),
                ("ExactType", reflected.TypeName), ("Control", "Exact identity with the original <coverReference>5__2 field type."));
            CaptureConcreteMethods(rows, "local.wrapper.sprite", reflected.Class, Sprite, gameAssemblyModule);
        });
        if (controlledSprite is null)
            Add(rows, "local.wrapper.audio", "unknown", ("Reason", "The existing Sprite closed-type identity control did not pass; AudioClip reflection was not attempted."));
        else Capture(rows, "local.wrapper.audio", () =>
        {
            var definition = _api.Class("Assembly-CSharp.dll", "", "ResourceReference`1");
            var audio = _api.Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioClip");
            var owner = _api.Class("Assembly-CSharp.dll", "", "InsideLevelParams");
            var contract = _api.InspectMetadataFieldClass(owner, "musicReference", Interface(Audio));
            var reflected = _api.ReflectSingleReferenceGenericClass(definition, audio, Concrete(Audio), contract.FieldClass.Class);
            Add(rows, "local.wrapper.audio", "closed-type-verified", ("ClosedClass", Pointer(reflected.Class)), ("ExactType", reflected.TypeName),
                ("ExactContract", contract.FieldClass.TypeName), ("Allocation", "Only BCL Type/Array metadata; no ResourceReference or Unity resource instance was created."));
            CaptureConcreteMethods(rows, "local.wrapper.audio", reflected.Class, Audio, gameAssemblyModule);
        });
        return Report("closed-local-wrapper-metadata", rows);
    }

    /// <summary>Call only with an existing root held by an adapter. Accepts InsideLevelParams or LevelOverControl.</summary>
    internal LocalResourceReferenceDiagnosticReport CaptureExistingOwner(IntPtr gameAssemblyModule,
        NativeRuntime.RootHandle existingOwner, string exactOwnerType)
    {
        RequireBuild(gameAssemblyModule);
        if (exactOwnerType is not ("InsideLevelParams" or "LevelOverControl"))
            throw new ArgumentException("Only the two exact known resource owners may be inspected.", nameof(exactOwnerType));
        var rows = new List<LocalResourceReferenceEvidence>();
        var cls = _api.Class("Assembly-CSharp.dll", "", exactOwnerType);
        CaptureLive(rows, "existing.cover", existingOwner, cls, "coverReference", Sprite, gameAssemblyModule);
        CaptureLive(rows, "existing.music", existingOwner, cls, "musicReference", Audio, gameAssemblyModule);
        return Report("existing-owner:" + exactOwnerType, rows);
    }

    private void CaptureField(List<LocalResourceReferenceEvidence> rows, string id, IntPtr cls, string name, string resource, IntPtr module)
        => Capture(rows, id, () =>
        {
            var field = _api.InspectMetadataFieldClass(cls, name, Interface(resource)); AddField(rows, id, field);
            CaptureMethod(rows, id + ".get", field.FieldClass.Class, "get_Resource", Array.Empty<string>(), resource, module);
            CaptureMethod(rows, id + ".release", field.FieldClass.Class, "Release", Array.Empty<string>(), "System.Void", module);
        });

    private void CaptureLive(List<LocalResourceReferenceEvidence> rows, string id, NativeRuntime.RootHandle owner,
        IntPtr ownerClass, string name, string resource, IntPtr module) => Capture(rows, id, () =>
    {
        var value = _api.InspectExistingReference(owner, ownerClass, name, Interface(resource), module);
        AddField(rows, id + ".field", value.Field);
        Add(rows, id + ".dispatch", value.ResolvedMethod is null ? "unknown" : "resolved-methodinfo",
            ("Resolution", value.ResolutionStatus), ("ObjectClass", value.ObjectClass is null ? "unknown" : Pointer(value.ObjectClass.Class)),
            ("ObjectType", value.ObjectClass?.TypeName ?? "unknown"));
        if (value.InterfaceMethod is not null) AddMethod(rows, id + ".interface.release", value.InterfaceMethod);
        if (value.ResolvedMethod is not null) AddMethod(rows, id + ".resolved.release", value.ResolvedMethod);
        if (value.ObjectClass is { } objectClass && objectClass.TypeName == Concrete(resource))
            CaptureConcreteMethods(rows, id + ".concrete", objectClass.Class, resource, module);
        else Add(rows, id + ".concrete", "unknown", ("Reason", "No live object with the exact closed ResourceReference type was observed."));
    });

    private void CaptureConcreteMethods(List<LocalResourceReferenceEvidence> rows, string id, IntPtr cls, string resource, IntPtr module)
    {
        CaptureMethod(rows, id + ".ctor", cls, ".ctor", new[] { resource, "System.Boolean" }, "System.Void", module);
        CaptureMethod(rows, id + ".get", cls, "get_Resource", Array.Empty<string>(), resource, module);
        CaptureMethod(rows, id + ".release", cls, "Release", Array.Empty<string>(), "System.Void", module);
    }

    private void CaptureMethod(List<LocalResourceReferenceEvidence> rows, string id, IntPtr cls, string method,
        string[] parameters, string returns, IntPtr module)
        => Capture(rows, id, () => AddMethod(rows, id, _api.InspectDeclaredMethod(cls, method, parameters, returns, module)));

    private static void AddField(List<LocalResourceReferenceEvidence> rows, string id, NativeMetadataField field)
        => Add(rows, id, "metadata-observed", ("Field", field.Name), ("FieldInfo", Pointer(field.Field)),
            ("Il2CppType", Pointer(field.Type)), ("ClosedClass", Pointer(field.FieldClass.Class)), ("ExactType", field.FieldClass.TypeName));

    private static void AddMethod(List<LocalResourceReferenceEvidence> rows, string id, NativeMetadataMethod method)
        => Add(rows, id, "metadata-observed", ("MethodInfo", Pointer(method.MethodInfo)),
            ("DeclaringClass", Pointer(method.DeclaringClass.Class)), ("DeclaringType", method.DeclaringClass.TypeName),
            ("Signature", method.ReturnType + " " + method.Name + "(" + string.Join(",", method.ParameterTypes) + ")"),
            ("Flags", "0x" + method.Flags.ToString("X")), ("ImplementationFlags", "0x" + method.ImplementationFlags.ToString("X")),
            ("IsGenericMethod", method.GenericMethod.ToString()), ("IsInflatedMethod", method.InflatedMethod.ToString()),
            ("EntryStatus", method.EntryStatus), ("Entry", method.Entry is null ? "unknown" : Pointer(method.Entry.Address)),
            ("Rva", method.Entry is null ? "unknown" : "0x" + method.Entry.Rva.ToString("X")),
            ("MethodInfoSlot", method.Entry?.MethodInfoSlot.ToString() ?? "unknown"));

    private static void Capture(List<LocalResourceReferenceEvidence> rows, string id, Action action)
    {
        try { action(); }
        catch (Exception error) { Add(rows, id, "failed", ("Error", error.GetType().Name + ": " + error.Message)); }
    }
    private static void Add(List<LocalResourceReferenceEvidence> rows, string id, string status, params (string Key, string Value)[] values)
        => rows.Add(new(id, status, new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(values.ToDictionary(x => x.Key, x => x.Value))));
    private LocalResourceReferenceDiagnosticReport Report(string stage, List<LocalResourceReferenceEvidence> rows)
        => new(stage, _profile.GameAssemblySha256, DateTimeOffset.UtcNow, Array.AsReadOnly(rows.ToArray()));
    private void RequireBuild(IntPtr module)
    {
        if (module == IntPtr.Zero || !OperatingSystem.IsWindows() || IntPtr.Size != 8
            || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Read-only ResourceReference diagnostics require the pinned Windows x64 Unity 2022.3.62f2 build and GameAssembly module.");
    }
    private static string Interface(string resource) => "IResourceReference<" + resource + ">";
    private static string Concrete(string resource) => "ResourceReference<" + resource + ">";
    private static string Pointer(IntPtr value) => "0x" + unchecked((ulong)value.ToInt64()).ToString("X");
}
