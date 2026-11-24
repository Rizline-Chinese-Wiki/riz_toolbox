using System.Runtime.InteropServices;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// The real owner reader uses official IL2CPP metadata and field_get_value.
// Keep the opaque native pointer in synthetic field storage: HoldJudge is only
// 16 bytes long, so writing to its advertised 0x10 slot would exceed it.
sealed partial class FakeNative
{
    internal enum HoldNativeOwnerMetadataFault
    {
        None, Static, WrongOffset, ForeignDeclaringClass, ByRef, WrongWidth, WrongType, ForeignPointerClass
    }

    private IntPtr _holdIntPtrType, _holdCachedPtrField;
    private IntPtr _holdUnityObjectClass, _holdComponentClass, _holdBehaviourClass, _holdMonoBehaviourClass;
    private IntPtr _holdJudgeClass;
    internal IntPtr HoldNativeOwner { get; private set; }
    internal IntPtr HoldCachedPtrField => _holdCachedPtrField;
    internal IntPtr HoldUnityObjectClass => _holdUnityObjectClass;
    internal IntPtr HoldMonoBehaviourClass => _holdMonoBehaviourClass;
    internal IntPtr HoldManagedOwnerClass => _holdJudgeClass;
    internal int HoldNativeOwnerReadCount { get; private set; }
    internal Action<int, IntPtr>? AfterHoldNativeOwnerRead;

    private void ConfigureHoldNativeOwner(IntPtr judgeType)
    {
        _holdIntPtrType = AddType("System.IntPtr", 24, true, 8);
        _holdUnityObjectClass = ClassOf(SeekType("UnityEngine.Object"));
        _holdComponentClass = ClassOf(SeekType("UnityEngine.Component"));
        _holdBehaviourClass = ClassOf(SeekType("UnityEngine.Behaviour"));
        _holdMonoBehaviourClass = ClassOf(SeekType("UnityEngine.MonoBehaviour"));
        _holdJudgeClass = ClassOf(judgeType);

        // This value represents an opaque native MonoBehaviour identity, not
        // an allocation or a pointer that the fixture or product may follow.
        HoldNativeOwner = new IntPtr(0x71500010);
        SeekField("hold.native.owner", SeekType("UnityEngine.Object"), "m_CachedPtr",
            _holdIntPtrType, HoldJudge, HoldNativeOwner, 0x10);
        _holdCachedPtrField = _gameplayFields["hold.native.owner"].Field;

        Add("il2cpp_class_get_parent", "Ptr1", nameof(HoldClassParent));
        Add("il2cpp_class_is_assignable_from", "Assignable", nameof(HoldAssignable));
        Add("il2cpp_class_from_name", "ClassLookup", nameof(HoldFindClass));
        Add("il2cpp_field_get_value", "FieldGet", nameof(HoldReadField));
    }

    internal void SetHoldNativeOwner(IntPtr owner)
    {
        if (_holdCachedPtrField == IntPtr.Zero)
            throw new InvalidOperationException("ConfigureHoldLifecycle must run before setting its native owner.");
        HoldNativeOwner = owner;
        SetGameplayField("hold.native.owner", owner);
    }

    internal void SetHoldManagedOwnerClassToForeign()
        => _boxedTypes[HoldJudge] = SeekType("Fake.ForeignHoldJudge");

    internal void RestoreHoldManagedOwnerClass()
        => _boxedTypes[HoldJudge] = _classes[_holdJudgeClass];

    internal void SetHoldNativeOwnerMetadataFault(HoldNativeOwnerMetadataFault fault)
    {
        if (_holdCachedPtrField == IntPtr.Zero)
            throw new InvalidOperationException("ConfigureHoldLifecycle must run before changing owner metadata.");
        _fieldMetadata[_holdCachedPtrField] = ("m_CachedPtr", _holdIntPtrType, 0x10);
        _types[_holdIntPtrType] = _types[_holdIntPtrType] with { ByRef = false, Size = 8 };
        _gameplayFieldParents[_holdCachedPtrField] = _holdUnityObjectClass;
        _flagMetadataFlags.Remove(_holdCachedPtrField);
        switch (fault)
        {
            case HoldNativeOwnerMetadataFault.None:
                break;
            case HoldNativeOwnerMetadataFault.Static:
                _flagMetadataFlags[_holdCachedPtrField] = 0x10;
                break;
            case HoldNativeOwnerMetadataFault.WrongOffset:
                _fieldMetadata[_holdCachedPtrField] = ("m_CachedPtr", _holdIntPtrType, 0x18);
                break;
            case HoldNativeOwnerMetadataFault.ForeignDeclaringClass:
                _gameplayFieldParents[_holdCachedPtrField] = ClassOf(SeekType("Fake.ForeignUnityObject"));
                break;
            case HoldNativeOwnerMetadataFault.ByRef:
                _types[_holdIntPtrType] = _types[_holdIntPtrType] with { ByRef = true };
                break;
            case HoldNativeOwnerMetadataFault.WrongWidth:
                _types[_holdIntPtrType] = _types[_holdIntPtrType] with { Size = 4 };
                break;
            case HoldNativeOwnerMetadataFault.WrongType:
                _fieldMetadata[_holdCachedPtrField] = ("m_CachedPtr", Int32, 0x10);
                break;
            case HoldNativeOwnerMetadataFault.ForeignPointerClass:
                _fieldMetadata[_holdCachedPtrField] = ("m_CachedPtr", AddType("System.IntPtr", 24, true, 8), 0x10);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }

    private IntPtr HoldClassParent(IntPtr cls)
    {
        if (cls == GameplayControllerBaseClass) return _holdMonoBehaviourClass;
        if (cls == _holdJudgeClass) return _holdMonoBehaviourClass;
        if (cls == _holdMonoBehaviourClass) return _holdBehaviourClass;
        if (cls == _holdBehaviourClass) return _holdComponentClass;
        if (cls == _holdComponentClass) return _holdUnityObjectClass;
        if (cls == _holdUnityObjectClass) return ClassOf(Object);
        return ClassParent(cls);
    }

    private bool HoldAssignable(IntPtr target, IntPtr source)
    {
        var current = source;
        for (var depth = 0; current != IntPtr.Zero && depth < 8;
            current = HoldClassParent(current), ++depth)
            if (target == current) return true;
        return GameplayAssignable(target, source);
    }

    private IntPtr HoldFindClass(IntPtr image, string ns, string name)
    {
        if (_gameplayImages.TryGetValue(image, out var assembly) && assembly == "mscorlib.dll"
            && ns == "System" && name == "IntPtr")
            return ClassOf(_holdIntPtrType);
        return FlagFindClass(image, ns, name);
    }

    private void HoldReadField(IntPtr owner, IntPtr field, IntPtr output)
    {
        NoteViewReadField(owner, field, output);
        if (owner != HoldJudge || field != _holdCachedPtrField) return;
        var observed = Marshal.ReadIntPtr(output);
        ++HoldNativeOwnerReadCount;
        AfterHoldNativeOwnerRead?.Invoke(HoldNativeOwnerReadCount, observed);
    }
}
