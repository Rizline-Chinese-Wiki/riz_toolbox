using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal enum NativeTweenSourceObjectKind
{ Manager, Tween, TweenArray, ScheduledList, ReadyStack, UnityEvent, SingleUnityEvent }

internal sealed record NativeTweenSourceMetadata(IntPtr TweenClass, IntPtr ManagerClass,
    NativeExactReadField Playing, NativeExactReadField Scheduled, NativeExactReadField Ready,
    NativeExactReadField OnStart, NativeExactReadField OnComplete, NativeExactReadField OnTween);

internal sealed partial class NativeRuntime
{
    /// <summary>Binds only the real producer's complete source-span/relay/unwind
    /// publication, then independently validates the official post-init types.</summary>
    internal NativeTweenSource BindTweenSources()
    {
        VerifyAccess();
        return NativeTweenSource.BindProduction(this, VerifyAccess);
    }

    /// <summary>Reads only an already loaded configured journal. The returned
    /// reader and every bound row remain non-production diagnostics.</summary>
    internal NativeTweenSource OpenTweenSourceJournalReadOnly()
    { VerifyAccess(); return NativeTweenSource.OpenLoadedReadOnly(this, VerifyAccess); }

    internal NativeTweenSourceMetadata InspectTweenSourcesMetadata(IntPtr module)
    {
        VerifyAccess(); var tween = InspectTweenAdmissionMetadata(module).TweenClass;
        var manager = Class("Assembly-CSharp.dll", "", "TweenManager");
        var type = _classType(manager); var image = ReadGraphExport<Ptr1>("il2cpp_class_get_image")(manager);
        if (type == IntPtr.Zero || _typeClass(type) != manager || _byRef(type) || _typeCode(type) != 18 || _isValueType(manager)
            || ReadGraphExport<Predicate>("il2cpp_class_is_generic")(manager) || ReadGraphExport<Predicate>("il2cpp_class_is_inflated")(manager)
            || MetadataTypeName(type) != "TweenManager" || Utf8(_className(manager)) != "TweenManager" || Utf8(_classNamespace(manager)) != ""
            || image == IntPtr.Zero || Utf8(_imageName(image)) != "Assembly-CSharp.dll"
            || _classParent(manager) != Class("UnityEngine.CoreModule.dll", "UnityEngine", "MonoBehaviour"))
            throw new UnsupportedSignatureException("Tween sources require the exact official non-generic manager class.");
        var playing = InspectExactDeclaredReadField(manager, "playingTweens", "TweenInfo[]", false, 0x20);
        var scheduled = InspectExactDeclaredReadField(manager, "scheduledTweens", "System.Collections.Generic.List<TweenInfo>", false, 0x28);
        var ready = InspectExactDeclaredReadField(manager, "readyTweens", "System.Collections.Generic.Stack<TweenInfo>", false, 0x30);
        VerifyReferenceArrayClass(playing.FieldClass, tween);
        var list = InspectTweenQueueClass(scheduled.FieldClass, NativeTweenQueueKind.List, tween);
        var stack = InspectTweenQueueClass(ready.FieldClass, NativeTweenQueueKind.Stack, tween);
        if (list.ArrayClass != playing.FieldClass || stack.ArrayClass != playing.FieldClass)
            throw new UnsupportedSignatureException("Actual Tween source queues do not share their exact TweenInfo array class.");
        var start = InspectExactDeclaredReadField(tween, "onStart", "UnityEngine.Events.UnityEvent", false, 0x88);
        var complete = InspectExactDeclaredReadField(tween, "onComplete", "UnityEngine.Events.UnityEvent", false, 0x90);
        var progress = InspectExactDeclaredReadField(tween, "onTween", "UnityEngine.Events.UnityEvent<System.Single>", false, 0x98);
        var unityEvent = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "UnityEvent");
        var eventBase = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "UnityEventBase");
        var classImage = ReadGraphExport<Ptr1>("il2cpp_class_get_image");
        if (start.FieldClass != unityEvent || complete.FieldClass != unityEvent
            || _classParent(progress.FieldClass) != eventBase || classImage(progress.FieldClass) != classImage(eventBase)
            || _typeCode(progress.Type) != 21 || _isValueType(progress.FieldClass)
            || ReadGraphExport<Predicate>("il2cpp_class_is_generic")(progress.FieldClass)
            || !ReadGraphExport<Predicate>("il2cpp_class_is_inflated")(progress.FieldClass)
            || Utf8(_className(progress.FieldClass)) != "UnityEvent`1" || Utf8(_classNamespace(progress.FieldClass)) != "UnityEngine.Events")
            throw new UnsupportedSignatureException("Tween source callbacks differ from the official event and existing closed Single event classes.");
        return new(tween, manager, playing, scheduled, ready, start, complete, progress);
    }

    /// <summary>Checks an object that was already independently rooted before a
    /// source operation. Journal pointers are never passed here to acquire roots.
    /// The returned token is identity evidence, not a source-parent/death proof.</summary>
    internal IntPtr ValidateRootedTweenSourceObject(RootHandle root, NativeTweenSourceObjectKind kind,
        NativeTweenSourceMetadata expected, IntPtr module)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(expected);
        var value = root.Target;
        if (InspectTweenSourcesMetadata(module) != expected)
            throw new UnsupportedSignatureException("Tween source metadata changed after the rooted graph was retained.");
        var cls = kind switch {
            NativeTweenSourceObjectKind.Manager => expected.ManagerClass,
            NativeTweenSourceObjectKind.Tween => expected.TweenClass,
            NativeTweenSourceObjectKind.TweenArray => expected.Playing.FieldClass,
            NativeTweenSourceObjectKind.ScheduledList => expected.Scheduled.FieldClass,
            NativeTweenSourceObjectKind.ReadyStack => expected.Ready.FieldClass,
            NativeTweenSourceObjectKind.UnityEvent => expected.OnStart.FieldClass,
            NativeTweenSourceObjectKind.SingleUnityEvent => expected.OnTween.FieldClass,
            _ => throw new UnsupportedSignatureException("Unknown Tween source object role.") };
        if (ObjectClass(value) != cls || root.Target != value)
            throw new UnsupportedSignatureException("A pre-rooted Tween source object changed its exact role or identity.");
        return value;
    }
}
