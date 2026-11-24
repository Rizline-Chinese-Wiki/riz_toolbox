#pragma warning disable CS0649 // Reused synthetic fixture fault knobs intentionally retain default values.
using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Extends the existing official-export synthetic fixture. Only the three
// declared Boolean flags may reach an actual NativeRuntime field setter.
sealed partial class FakeNative
{
    internal static readonly string[] FlagNames = { "isActive", "destroyed", "missed" };
    internal static readonly int[] FlagOffsets = { 0x30, 0x31, 0x55 };
    internal sealed record FlagWrite(int Ordinal, int Note, int Slot, IntPtr Owner, IntPtr Field,
        bool Before, bool Requested) { internal bool Stored; }
    internal enum FlagMetadataFault { Static, InitOnly, WrongType, WrongWidth, WrongOffset }
    internal readonly List<FlagWrite> FlagWrites = new();
    internal readonly List<string> FlagViolations = new();
    internal readonly List<IntPtr> FlagNotes = new();
    internal Action<FlagWrite>? BeforeFlagStore, AfterFlagStore;
    internal Action<string>? AfterFlagMetadata;
    internal bool FlagOwnersRooted = true;
    internal int FailFlagBeforeStore, FailFlagAfterStore;
    internal IntPtr DeadFlagNote;
    internal int FlagFullGraphReadsInsideSelected;
    private readonly IntPtr[] _flagFields = new IntPtr[3];
    private readonly Dictionary<IntPtr, uint> _flagMetadataFlags = new();
    private readonly Dictionary<(IntPtr Owner, IntPtr Field), byte[]> _flagUnselected = new();
    private IntPtr _flagAssemblies;

    internal void ConfigureNoteFlags(NoteViewShape shape = NoteViewShape.Complete, bool reuseSource = false)
    {
        if (!reuseSource) ConfigureNoteView(shape);
        // The general gameplay fixture has three game/Unity images. The new
        // official Boolean class check also requires the real core-library seam.
        if (!_gameplayImages.Values.Contains("mscorlib.dll")) _gameplayImages.Add(GameplayAllocate(8), "mscorlib.dll");
        _flagAssemblies = GameplayAllocate(_gameplayImages.Count * IntPtr.Size);
        var imageIndex = 0;
        foreach (var image in _gameplayImages.Keys)
            Marshal.WriteIntPtr(_flagAssemblies, imageIndex++ * IntPtr.Size, image);
        Add("il2cpp_domain_get_assemblies", "Ptr2", nameof(FlagAssemblies));
        Add("il2cpp_class_from_name", "ClassLookup", nameof(FlagFindClass));
        FlagNotes.AddRange(_referenceArrays[CursorAllArray]);
        var noteClass = SeekType("InLevel.NoteInformation");
        for (var note = 0; note < FlagNotes.Count; ++note)
        {
            var values = new[] { note % 2 == 0, note % 3 == 0, note % 2 != 0 };
            for (var slot = 0; slot < 3; ++slot)
            {
                var key = "flags." + note + "." + slot;
                SeekField(key, noteClass, FlagNames[slot], Boolean, FlagNotes[note], values[slot], FlagOffsets[slot]);
                _flagFields[slot] = _gameplayFields[key].Field;
            }
        }
        foreach (var row in _gameplayStorage)
            if (!_flagFields.Contains(row.Key.Field)) _flagUnselected.Add(row.Key, row.Value.ToArray());
        Add("il2cpp_field_get_flags", "Flags", nameof(FlagFieldFlags));
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(FlagSetValue));
        Add("il2cpp_field_set_value_object", "FieldSetObject", nameof(FlagRejectReferenceWrite));
        Add("il2cpp_class_get_methods", "Ptr2", nameof(FlagMethods));
        Add("il2cpp_class_get_method_from_name", "MethodLookup", nameof(FlagNamedMethod));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(FlagInvoke));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
    }
    private IntPtr FlagAssemblies(IntPtr _, IntPtr count)
    { Marshal.WriteIntPtr(count, new IntPtr(_gameplayImages.Count)); return _flagAssemblies; }
    private IntPtr FlagFindClass(IntPtr image, string ns, string name)
    {
        if (_gameplayImages[image] != "mscorlib.dll") return GameplayFindClass(image, ns, name);
        if (ns != "System") return IntPtr.Zero;
        return name switch
        {
            "Boolean" => ClassOf(Boolean), "Int32" => ClassOf(Int32), "Single" => ClassOf(Single),
            "Object" => ClassOf(Object), "String" => ClassOf(String), "Void" => ClassOf(Void),
            "Double" => ClassOf(_gameplayDouble), "Type" => ClassOf(_gameplayType), _ => IntPtr.Zero
        };
    }
    internal void FlagEmptyRuntime()
    {
        _referenceArrays[CursorAllArray] = Array.Empty<IntPtr>();
        _referenceArrays[CursorRenderArray] = Array.Empty<IntPtr>();
        FlagNotes.Clear();
    }
    private void ObserveFlagGraphRead(string name, IntPtr owner)
    {
        if (name != "type" || !SeekNotes.Contains(owner)) return;
        if (new System.Diagnostics.StackTrace(false).GetFrames().Any(frame => frame.GetMethod() is { } method
            && method.DeclaringType?.Name == "NativeSeekNoteFlagsAccess" && method.Name is "ReadNote" or "Write"))
            ++FlagFullGraphReadsInsideSelected;
    }
    internal SeekNoteFlagTarget[] FlagValues() => FlagNotes.Select((note, index) => new SeekNoteFlagTarget(
        note.ToInt64(), FlagValue(index, 0), FlagValue(index, 1), FlagValue(index, 2))).ToArray();
    internal bool FlagValue(int note, int slot) => _gameplayStorage[(FlagNotes[note], _flagFields[slot])][0] != 0;
    internal void FlagForeignValue(int note, int slot, bool value)
        => _gameplayStorage[(FlagNotes[note], _flagFields[slot])] = new[] { value ? (byte)1 : (byte)0 };
    internal void FlagForeignSource(string key, object value)
    {
        SetGameplayField(key, value); var row = _gameplayFields[key];
        _flagUnselected[(row.Owner, row.Field)] = _gameplayStorage[(row.Owner, row.Field)].ToArray();
    }
    internal bool FlagOtherStorageUnchanged() => _flagUnselected.All(row =>
        _gameplayStorage.TryGetValue(row.Key, out var value) && value.SequenceEqual(row.Value));
    internal void FlagChangeMetadata(int slot, FlagMetadataFault fault)
    {
        var field = _flagFields[slot]; var old = _fieldMetadata[field];
        switch (fault)
        {
            case FlagMetadataFault.Static: _flagMetadataFlags[field] = 0x10; break;
            case FlagMetadataFault.InitOnly: _flagMetadataFlags[field] = 0x20; break;
            case FlagMetadataFault.WrongType: _fieldMetadata[field] = (old.Name, Int32, old.Offset); break;
            case FlagMetadataFault.WrongWidth:
                _fieldMetadata[field] = (old.Name, AddType("System.Boolean", 2, true, 4), old.Offset); break;
            case FlagMetadataFault.WrongOffset: _fieldMetadata[field] = (old.Name, old.Type, old.Offset + 1); break;
        }
    }
    private uint FlagFieldFlags(IntPtr field)
    {
        var value = _flagMetadataFlags.TryGetValue(field, out var flags) ? flags : FieldFlags(field);
        var slot = Array.IndexOf(_flagFields, field);
        if (slot >= 0) AfterFlagMetadata?.Invoke(FlagNames[slot]);
        return value;
    }
    private void FlagSetValue(IntPtr owner, IntPtr field, IntPtr input)
    {
        var slot = Array.IndexOf(_flagFields, field); var note = FlagNotes.IndexOf(owner);
        if (slot < 0 || note < 0 || _fieldMetadata[field].Type != Boolean || _fieldMetadata[field].Offset != FlagOffsets[slot]
            || (_flagMetadataFlags.TryGetValue(field, out var flags) && (flags & 0x70) != 0))
        {
            FlagViolations.Add("value setter owner=" + owner + " field=" + field);
            throw new InvalidOperationException("A setter escaped the three selected per-note Boolean fields.");
        }
        ++ValueFieldWrites; FlagOwnersRooted &= HasRootFor(owner);
        var raw = Marshal.ReadByte(input);
        if (raw > 1) throw new InvalidDataException("Official Boolean storage must contain one canonical byte.");
        var row = new FlagWrite(FlagWrites.Count + 1, note, slot, owner, field, FlagValue(note, slot), raw != 0);
        FlagWrites.Add(row); BeforeFlagStore?.Invoke(row);
        if (row.Ordinal == FailFlagBeforeStore) throw new IOException("Synthetic flag setter failed before storage.");
        _gameplayStorage[(owner, field)] = new[] { raw }; row.Stored = true; AfterFlagStore?.Invoke(row);
        if (row.Ordinal == FailFlagAfterStore) throw new IOException("Synthetic flag setter failed after storage.");
    }
    private void FlagRejectReferenceWrite(IntPtr owner, IntPtr field, IntPtr value)
    {
        ++ObjectFieldWrites; FlagViolations.Add("reference setter owner=" + owner + " field=" + field);
        throw new InvalidOperationException("Note flags never write object references, fingerIds or renderers.");
    }
    private bool FlagAdditionalClass(IntPtr cls)
        => GameplayTypeName(cls) is "PauseUIControl" or "UnityEngine.Time" or "TouchBlocker" or "WindowControl";
    private IntPtr FlagMethods(IntPtr cls, IntPtr iterator)
    {
        if (!FlagAdditionalClass(cls)) return NoteViewMethods(cls, iterator);
        ++NoteViewMethodResolutions; return GameplayMethods(cls, iterator);
    }
    private IntPtr FlagNamedMethod(IntPtr cls, string name, int count)
    {
        if (!FlagAdditionalClass(cls)) return NoteViewNamedMethod(cls, name, count);
        ++NoteViewMethodResolutions;
        return _gameplayMethods.FirstOrDefault(row => row.Value.Class == cls && row.Value.Name == name
            && row.Value.Parameters.Length == count).Key;
    }
    private IntPtr FlagInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception)
    {
        if (DeadFlagNote != IntPtr.Zero && _gameplayMethods.TryGetValue(method, out var alive)
            && GameplayTypeName(alive.Class) == "UnityEngine.Object" && alive.Name == "op_Implicit"
            && Marshal.ReadIntPtr(arguments) == DeadFlagNote)
        {
            NoteViewInvoke(method, receiver, arguments, exception);
            return Box(Boolean, new byte[] { 0 });
        }
        if (!_gameplayMethods.TryGetValue(method, out var row) || !FlagAdditionalClass(row.Class))
            return NoteViewInvoke(method, receiver, arguments, exception);
        var type = GameplayTypeName(row.Class);
        var allowed = (type, row.Name) switch
        {
            ("PauseUIControl", "get_isActiveAndEnabled" or "get_CanDoOperation") => receiver == GameplayPause,
            ("UnityEngine.Time", "get_timeSinceLevelLoad") or ("TouchBlocker", "get_Blocked")
                or ("WindowControl", "get_HasActiveWindow") => receiver == IntPtr.Zero,
            _ => false
        };
        if (!allowed) NoteViewRejectDispatch("flags invoke " + type + "." + row.Name);
        ++NoteViewInvocations;
        var call = new NoteViewCall(type, row.Name, receiver, null);
        var result = SeekInvoke(method, receiver, arguments, exception); AfterNoteViewInvocation?.Invoke(call); return result;
    }
}
