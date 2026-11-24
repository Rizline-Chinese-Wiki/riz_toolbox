using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed record NativeTweenAdmissionMetadata(IntPtr TweenClass, NativeExactReadField CachedPointerField);

internal sealed partial class NativeRuntime
{
    /// <summary>Only the actual loaded pre-init producer can configure the private
    /// native policy. Copied snapshots and layout descriptions cannot create it.</summary>
    internal NativeTweenAdmission BindTweenAdmission()
    {
        VerifyAccess(); return NativeTweenAdmission.BindProduction(this, VerifyAccess);
    }

    internal NativeTweenAdmissionMetadata InspectTweenAdmissionMetadata(IntPtr module)
    {
        VerifyAccess();
        if (module == IntPtr.Zero) throw new UnsupportedSignatureException("Tween admission requires the actual pinned module.");
        var tween = Class("Assembly-CSharp.dll", "", "TweenInfo");
        var mono = Class("UnityEngine.CoreModule.dll", "UnityEngine", "MonoBehaviour");
        var behaviour = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Behaviour");
        var component = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Component");
        var unityObject = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        var systemObject = Class("mscorlib.dll", "System", "Object");
        var pointerClass = Class("mscorlib.dll", "System", "IntPtr");
        var type = _classType(tween);
        var image = ReadGraphExport<Ptr1>("il2cpp_class_get_image")(tween);
        if (type == IntPtr.Zero || _typeClass(type) != tween || _byRef(type) || _typeCode(type) != 18
            || _isValueType(tween) || ReadGraphExport<Predicate>("il2cpp_class_is_generic")(tween)
            || ReadGraphExport<Predicate>("il2cpp_class_is_inflated")(tween)
            || MetadataTypeName(type) != "TweenInfo" || Utf8(_className(tween)) != "TweenInfo" || Utf8(_classNamespace(tween)) != ""
            || image == IntPtr.Zero || Utf8(_imageName(image)) != "Assembly-CSharp.dll"
            || _classParent(tween) != mono || _classParent(mono) != behaviour || _classParent(behaviour) != component
            || _classParent(component) != unityObject || _classParent(unityObject) != systemObject
            || !_assignable(mono, tween) || !_assignable(unityObject, tween))
            throw new UnsupportedSignatureException("Tween admission requires the exact non-generic official MonoBehaviour parent chain.");
        var field = InspectExactDeclaredReadField(unityObject, "m_CachedPtr", "System.IntPtr", false, 0x10);
        if (field.FieldClass != pointerClass || field.Size != 8 || _typeCode(field.Type) != 24
            || Describe(field.Type) is not { Kind: NativeValueKind.IntPtr, Size: 8, ByRef: false }
            || Field(tween, "m_CachedPtr") != field.Field)
            throw new UnsupportedSignatureException("Tween admission cached pointer is not the official Object IntPtr8 field.");
        // Actual metadata must still match every fixed Tween declaration before
        // the existing native lanes can acquire a new admission policy.
        foreach (var operation in Enum.GetValues<NativeTweenOperation>()) ResolveTweenOperation(module, operation);
        return new(tween, field);
    }

    internal IntPtr ValidateRootedTweenAdmissionClaim(RootHandle root, NativeTweenAdmissionMetadata expected, IntPtr module)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(expected);
        var value = root.Target;
        if (InspectTweenAdmissionMetadata(module) != expected || ObjectClass(value) != expected.TweenClass)
            throw new UnsupportedSignatureException("The independently rooted claim is not this exact production TweenInfo.");
        // Zero is only an observation. It neither proves destruction nor grants
        // root retirement. The native Claim/Seal receipts govern admission only.
        _ = ReadExactField<IntPtr>(root, expected.CachedPointerField);
        if (root.Target != value || ObjectClass(value) != expected.TweenClass)
            throw new TargetExpiredException("The independently rooted Tween claim changed its identity.");
        return value;
    }
}
