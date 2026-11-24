using System.Runtime.InteropServices;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Adds only synthetic official iterator/owner fields to the existing export
// fixture. NativeJudgementBackend must acquire all lifecycle roots itself.
sealed partial class FakeNative
{
    internal IntPtr HoldJudge, HoldIterator, HoldHandle;
    internal IntPtr HoldIteratorClass => ClassOf(_seekCoroutineType);
    internal IntPtr HoldHandleClass => ClassOf(SeekType("UnityEngine.Coroutine"));
    internal void ConfigureHoldLifecycle()
    {
        ConfigureNoteFlags();
        // This reader consumes the official reference-array storage directly,
        // rather than the older synthetic Array.GetValue transport.
        GameplayControllers = Box(_referenceArrayType, new byte[ArrayHeaderBytes + IntPtr.Size]);
        _referenceArrays[GameplayControllers] = new[] { GameplayController };
        _referenceRanks[GameplayControllers] = 1;
        Marshal.WriteIntPtr(GameplayControllers, ClassOf(_referenceArrayType));
        Marshal.WriteIntPtr(GameplayControllers, ArrayHeaderBytes, GameplayController);
        Add("il2cpp_class_get_rank", "Flags", nameof(HoldArrayRank));
        Add("il2cpp_class_get_element_class", "Ptr1", nameof(HoldArrayElementClass));
        Add("il2cpp_array_element_size", "ArrayElementSize", nameof(HoldArrayElementSize));
        Add("il2cpp_array_length", "ArraySize", nameof(HoldArrayLength));
        Add("il2cpp_array_get_byte_length", "ArraySize", nameof(HoldArrayBytes));
        Add("il2cpp_object_get_size", "ArraySize", nameof(HoldObjectSize));
        Add("il2cpp_array_object_header_size", "ReferenceArrayHeaderSize", nameof(HoldArrayHeaderSize));
        var judge = SeekType("InLevel.NoteJudgeControl"); var info = SeekType("InLevel.LevelInformation");
        HoldJudge = SeekObject(judge);
        ConfigureHoldNativeOwner(judge);
        SeekField("hold.info.judge", info, "noteJudgeControl", judge, GameplayInformation, HoldJudge, 0x58);
        SeekField("hold.judge.info", judge, "levelInformation", info, HoldJudge, GameplayInformation, 0x20);
        FlagForeignSource("seek.info.owner", HoldJudge);
        HoldIterator = HoldCreateIterator();
        HoldHandle = SeekObject(SeekType("UnityEngine.Coroutine"));
        ConfigureHoldDiscoveryMetadata();
        ConfigureHoldSourceGuardExports();
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
    }
    private bool IsHoldDiscoveryArray(IntPtr cls)
        => cls != IntPtr.Zero && (cls == ClassOf(_referenceArrayType) || cls == _holdResultArrayClass);
    private uint HoldArrayRank(IntPtr cls) => IsHoldDiscoveryArray(cls) ? HoldDiscoveryRank : 0u;
    private IntPtr HoldArrayElementClass(IntPtr cls)
        => cls == _holdResultArrayClass ? _holdResultElementClass : cls == ClassOf(_referenceArrayType) ? ClassOf(SeekType("UnityEngine.Object")) : IntPtr.Zero;
    private int HoldArrayElementSize(IntPtr cls) => IsHoldDiscoveryArray(cls) ? IntPtr.Size : ElementSize(cls);
    private uint HoldArrayLength(IntPtr value)
        => value == GameplayControllers ? checked((uint)_referenceArrays[value].Length) : ArrayLength(value);
    private uint HoldArrayBytes(IntPtr value)
        => value == GameplayControllers ? checked(HoldArrayLength(value) * (uint)IntPtr.Size) : ArrayBytes(value);
    private uint HoldObjectSize(IntPtr value)
        => value == GameplayControllers ? checked((uint)ArrayHeaderBytes + HoldArrayBytes(value)) : ObjectSize(value);
    private uint HoldArrayHeaderSize() => checked((uint)ArrayHeaderBytes);
    internal IntPtr HoldCreateHandle() => SeekObject(SeekType("UnityEngine.Coroutine"));
    internal void HoldReplaceNote(IntPtr iterator, int noteOrdinal)
    {
        SeekField("hold.changed.note", _seekCoroutineType, "holdNoteInformation", SeekType("InLevel.NoteInformation"),
            iterator, FlagNotes[noteOrdinal], 0x28);
    }
    internal IntPtr HoldCreateIterator(int noteOrdinal = 2)
    {
        var iterator = SeekObject(_seekCoroutineType); var prefix = "hold.iterator." + iterator.ToInt64();
        SeekField(prefix + ".owner", _seekCoroutineType, "<>4__this", SeekType("InLevel.NoteJudgeControl"), iterator, HoldJudge, 0x20);
        SeekField(prefix + ".note", _seekCoroutineType, "holdNoteInformation", SeekType("InLevel.NoteInformation"), iterator, FlagNotes[noteOrdinal], 0x28);
        SeekField(prefix + ".state", _seekCoroutineType, "<>1__state", Int32, iterator, 0, 0x10);
        SeekField(prefix + ".current", _seekCoroutineType, "<>2__current", Object, iterator, IntPtr.Zero, 0x18);
        return iterator;
    }
}
