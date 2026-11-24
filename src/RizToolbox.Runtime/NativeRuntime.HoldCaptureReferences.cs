using System.Runtime.InteropServices;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    private IntPtr HoldCaptureReceiverClass(string fieldName, Action guard)
    {
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        if (fieldName == "levelInformation")
            return LineEndpointClass("Assembly-CSharp.dll", "InLevel", "NoteJudgeControl", guard);
        if (fieldName == "noteJudgeControl")
            return LineEndpointClass("Assembly-CSharp.dll", "InLevel", "LevelInformation", guard);
        if (fieldName == "LevelInformation")
            return LineEndpointClass("Assembly-CSharp.dll", "", "NormalModeInLevelController", guard);
        if (fieldName is not ("<>4__this" or "holdNoteInformation"))
            throw new UnsupportedSignatureException("The physical Hold declaring field is outside its exact graph.");

        var judge = LineEndpointClass("Assembly-CSharp.dll", "InLevel", "NoteJudgeControl", guard);
        var nested = O(() => ReadGraphExport<Ptr2>("il2cpp_class_get_nested_types"));
        using var iterator = Arg.Value(IntPtr.Zero);
        var found = IntPtr.Zero; var terminated = false;
        for (var index = 0; index < 4096; ++index)
        {
            var cls = O(() => nested(judge, iterator.Pointer));
            if (cls == IntPtr.Zero) { terminated = true; break; }
            if (Utf8(O(() => _className(cls))) != "<CheckHold>d__32") continue;
            if (found != IntPtr.Zero)
                throw new UnsupportedSignatureException("The physical Hold iterator class is ambiguous.");
            found = cls;
        }
        if (!terminated || found == IntPtr.Zero)
            throw new UnsupportedSignatureException("The canonical physical Hold iterator class is absent.");
        return found;
    }

    /// <summary>The physical Hold graph only reads exact instance references. Every
    /// official export is separated by the capture's withdrawal guard. The field
    /// class is checked against its canonical Assembly-CSharp declaration. The
    /// controller's LevelInformation slot is declared on its exact direct base.</summary>
    internal NativeExactReadField InspectHoldCaptureReferenceField(IntPtr receiverClass, string name,
        string expectedType, IntPtr expectedFieldClass, int expectedOffset, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard);
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        var shape = name switch
        {
            "<>4__this" => (Type: "InLevel.NoteJudgeControl", Offset: 0x20),
            "holdNoteInformation" => (Type: "InLevel.NoteInformation", Offset: 0x28),
            "levelInformation" => (Type: "InLevel.LevelInformation", Offset: 0x20),
            "noteJudgeControl" => (Type: "InLevel.NoteJudgeControl", Offset: 0x58),
            "LevelInformation" => (Type: "InLevel.LevelInformation", Offset: 0x28),
            _ => throw new UnsupportedSignatureException("The physical Hold field is outside its pinned graph.")
        };
        if (receiverClass == IntPtr.Zero || expectedFieldClass == IntPtr.Zero
            || expectedType != shape.Type || expectedOffset != shape.Offset
            || expectedOffset < 2 * IntPtr.Size)
            throw new UnsupportedSignatureException("The physical Hold field requires an exact declared game reference.");
        if (HoldCaptureReceiverClass(name, guard) != receiverClass)
            throw new UnsupportedSignatureException("The physical Hold field has another canonical receiver class.");
        var declaringClass = receiverClass;
        if (name == "LevelInformation")
        {
            declaringClass = LineEndpointClass("Assembly-CSharp.dll", "", "InLevelController", guard);
            if (declaringClass == receiverClass || O(() => _classParent(receiverClass)) != declaringClass
                || Utf8(O(() => _className(declaringClass))) != "InLevelController"
                || Utf8(O(() => _classNamespace(declaringClass))) != string.Empty)
                throw new UnsupportedSignatureException("The physical Hold controller has another immediate declaring base.");
        }
        var canonicalClass = LineEndpointClass("Assembly-CSharp.dll", "InLevel", expectedType[8..], guard);
        var getImage = O(() => ReadGraphExport<Ptr1>("il2cpp_class_get_image"));
        var fieldImage = O(() => getImage(expectedFieldClass));
        var declaringImage = O(() => getImage(declaringClass));
        var declaringType = O(() => _classType(declaringClass));
        var canonicalType = O(() => _classType(expectedFieldClass));
        if (canonicalClass != expectedFieldClass || fieldImage == IntPtr.Zero || declaringImage == IntPtr.Zero
            || Utf8(O(() => _imageName(fieldImage))) != "Assembly-CSharp.dll"
            || Utf8(O(() => _imageName(declaringImage))) != "Assembly-CSharp.dll"
            || (receiverClass != declaringClass && O(() => getImage(receiverClass)) != declaringImage)
            || Utf8(O(() => _classNamespace(expectedFieldClass))) != "InLevel"
            || Utf8(O(() => _className(expectedFieldClass))) != expectedType[8..]
            || declaringType == IntPtr.Zero || O(() => _typeClass(declaringType)) != declaringClass
            || canonicalType == IntPtr.Zero || O(() => _typeClass(canonicalType)) != expectedFieldClass
            || O(() => _byRef(canonicalType)) || O(() => _typeCode(canonicalType)) != 18
            || O(() => _isValueType(expectedFieldClass)) || O(() => _isEnum(expectedFieldClass)))
            throw new UnsupportedSignatureException("The Hold reference class is not its canonical exact declaration.");

        var getParent = O(() => ReadGraphExport<Ptr1>("il2cpp_field_get_parent"));
        IntPtr FindDeclaredField(IntPtr cls)
        {
            var match = IntPtr.Zero;
            using var iterator = Arg.Value(IntPtr.Zero);
            for (var index = 0; index < 256; ++index)
            {
                var field = O(() => _fields(cls, iterator.Pointer));
                if (field == IntPtr.Zero) return match;
                if (O(() => getParent(field)) != cls)
                    throw new UnsupportedSignatureException("A physical Hold field has a foreign declaring class.");
                if (Utf8(O(() => _fieldName(field))) != name) continue;
                if (match != IntPtr.Zero)
                    throw new UnsupportedSignatureException("The physical Hold field is ambiguous.");
                match = field;
            }
            throw new UnsupportedSignatureException("The physical Hold field declaration list exceeds its bound.");
        }

        var found = FindDeclaredField(declaringClass);
        if (found == IntPtr.Zero || O(() => _findField(declaringClass, name)) != found
            || (receiverClass != declaringClass && (O(() => _findField(receiverClass, name)) != found
                || FindDeclaredField(receiverClass) != IntPtr.Zero)))
            throw new UnsupportedSignatureException("The exact physical Hold field is absent or shadowed.");

        var flags = O(() => _fieldFlags(found));
        var type = O(() => _fieldType(found));
        var offset = O(() => _fieldOffset(found));
        if ((flags & (0x10u | 0x40u)) != 0 || type == IntPtr.Zero
            || O(() => _typeClass(type)) != expectedFieldClass || O(() => _byRef(type))
            || O(() => _typeCode(type)) != 18 || offset != expectedOffset)
            throw new UnsupportedSignatureException("The Hold field flags, reference type or official offset changed.");
        var instanceSize = O(() => ReadGraphExport<ArraySize>("il2cpp_class_instance_size"));
        var fieldEnd = (long)offset + IntPtr.Size;
        if (fieldEnd > O(() => instanceSize(declaringClass))
            || (receiverClass != declaringClass && fieldEnd > O(() => instanceSize(receiverClass))))
            throw new UnsupportedSignatureException("The exact Hold field exceeds its declaring instance storage.");
        return new(found, type, declaringClass, expectedFieldClass, name, expectedType, false,
            expectedOffset, IntPtr.Size, flags);
    }

    internal NativeExactReadField InspectNormalControllerInformationField(IntPtr controllerClass,
        IntPtr informationClass, Action guard)
        => InspectHoldCaptureReferenceField(controllerClass, "LevelInformation", "InLevel.LevelInformation",
            informationClass, 0x28, guard);

    // Formal graph readers already own the expected information root. Compare
    // identity at the exact field and keep the receiver alive through the native read.
    internal bool MatchesNormalControllerInformation(RootHandle receiver, RootHandle expectedInformation,
        IntPtr controllerClass, IntPtr informationClass, Action guard)
    {
        ArgumentNullException.ThrowIfNull(receiver); ArgumentNullException.ThrowIfNull(expectedInformation);
        var field = InspectNormalControllerInformationField(controllerClass, informationClass, guard);
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        var expected = O(() => expectedInformation.Target);
        if (O(() => _objectClass(expected)) != informationClass)
            throw new UnsupportedSignatureException("The retained LevelInformation has another exact class.");

        var value = O(() => receiver.Target);
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            guard();
            var temporary = call.Root(() => Root(value));
            guard();
            var target = RequireHoldReferenceReceiver(temporary, controllerClass, field, guard);
            if (target != value)
                throw new UnsupportedSignatureException("The retained Normal controller changed during the field read.");
            using var output = new Arg(IntPtr.Size);
            guard(); _fieldGet(target, field.Field, output.Pointer);
            var observed = Marshal.ReadIntPtr(output.Pointer);
            guard();
            return observed == expected && O(() => expectedInformation.Target) == expected;
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    private IntPtr RequireHoldReferenceReceiver(RootHandle receiver, IntPtr receiverClass,
        NativeExactReadField field, Action guard)
    {
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        var value = O(() => receiver.Target);
        if (O(() => _objectClass(value)) != receiverClass || O(() => _findField(receiverClass, field.Name)) != field.Field)
            throw new UnsupportedSignatureException("The rooted physical Hold field receiver has another exact class or field.");
        return value;
    }

    /// <summary>After field_get_value returns a reference, temporary rooting and
    /// publication precede the post-read withdrawal check. A failed publisher
    /// remains an explicit NativePublicationCall responsibility; an accepted
    /// root belongs to the original source capture, including after withdrawal.</summary>
    internal void PublishHoldCaptureReferenceField(RootHandle receiver, NativeExactReadField field,
        Func<IntPtr, RootHandle, bool> publishRoot, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(receiver);
        ArgumentNullException.ThrowIfNull(field); ArgumentNullException.ThrowIfNull(publishRoot);
        ArgumentNullException.ThrowIfNull(guard); guard();
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        var tokenPublished = false; var publicationAccepted = false;
        try
        {
            var receiverClass = field.Name == "LevelInformation" ? HoldCaptureReceiverClass(field.Name, guard) : field.DeclaringClass;
            var current = InspectHoldCaptureReferenceField(receiverClass, field.Name, field.TypeName,
                field.FieldClass, field.Offset, guard);
            if (current != field || field.IsStatic || field.Size != IntPtr.Size)
                throw new UnsupportedSignatureException("The saved physical Hold field differs from current official metadata.");
            var value = RequireHoldReferenceReceiver(receiver, receiverClass, field, guard);
            using var output = new Arg(IntPtr.Size);
            guard(); _fieldGet(value, field.Field, output.Pointer);
            var token = Marshal.ReadIntPtr(output.Pointer);
            // Do not run the source guard between the field read and publication.
            // The source can be withdrawn by a callback inside field_get_value.
            if (token == IntPtr.Zero)
                throw new InvalidDataException("A required physical Hold graph reference is absent.");
            call.BeginPublication(token); tokenPublished = true;
            var temporary = call.Root(() => Root(token));
            var transferred = publishRoot(token, temporary);
            if (transferred) call.TransferRoot(temporary);
            call.PublicationAccepted(); publicationAccepted = true;
            guard();
        }
        catch (Exception error)
        {
            if (tokenPublished && !publicationAccepted) call.PublicationFailed();
            call.OperationFailed(error); throw;
        }
        finally { call.Dispose(); }
    }
}
