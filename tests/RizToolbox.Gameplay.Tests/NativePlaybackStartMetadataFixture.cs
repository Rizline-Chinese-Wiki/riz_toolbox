using System.Runtime.InteropServices;

internal static partial class NativePlaybackStockMetadataChecks
{
    private sealed partial class MetadataFixture
    {
        private IntPtr _startClass, _startProgress;
        private readonly Dictionary<string, IntPtr> _startFieldNames = new();
        private readonly Dictionary<IntPtr, (IntPtr Type, int Offset)> _startFields = new();
        private void ConfigureStartMetadata(IntPtr module, IntPtr progress, IntPtr progressType)
        {
            _startProgress = progress;
            _startClass = Class(_gameImage, "", "<Start>d__25", out _);
            Add("StartMoveNext", _startClass, "MoveNext", 0x404990, module);
            Update("StartMoveNext", value => value.ReturnType = Base.Boolean);
            AddField("<>1__state", Base.Int32, 0x10);
            AddField("<>2__current", Base.Object, 0x18);
            AddField("<>4__this", progressType, 0x20);
            Export("il2cpp_class_get_nested_types", "Ptr2", nameof(StartNested));
            Export("il2cpp_class_get_field_from_name", "FieldLookup", nameof(StartField));
            Export("il2cpp_field_get_type", "Ptr1", nameof(StartFieldType));
            Export("il2cpp_field_get_offset", "FieldOffset", nameof(StartFieldOffset));
            Export("il2cpp_field_get_flags", "Flags", nameof(StartFieldFlags));
        }
        private void AddField(string name, IntPtr type, int offset)
        {
            var field = Allocate(16); _startFieldNames.Add(name, field); _startFields.Add(field, (type, offset));
        }
        private IntPtr StartNested(IntPtr parent, IntPtr iterator)
        {
            Reads++;
            if (parent != _startProgress || Marshal.ReadIntPtr(iterator) != IntPtr.Zero) return IntPtr.Zero;
            Marshal.WriteIntPtr(iterator, new IntPtr(1)); return _startClass;
        }
        private IntPtr StartField(IntPtr cls, string name)
        { Reads++; return cls == _startClass ? _startFieldNames.GetValueOrDefault(name) : IntPtr.Zero; }
        private IntPtr StartFieldType(IntPtr field)
        {
            Reads++; var type = _startFields[field].Type;
            return _nativeTypeOriginals.Count == 0 ? type : _nativeClassTypes[Base.ClassOf(type)];
        }
        private int StartFieldOffset(IntPtr field) { Reads++; return _startFields[field].Offset; }
        private uint StartFieldFlags(IntPtr field) { Reads++; return 0; }
    }
}
