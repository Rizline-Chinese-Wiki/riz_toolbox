using System.Runtime.InteropServices;
using RizToolbox.Runtime;

internal static partial class NativePlaybackStockMetadataChecks
{
    private sealed partial class MetadataFixture
    {
        internal IntPtr ClockInformationClass;
        private readonly Dictionary<(IntPtr Class, string Name), IntPtr> _clockFieldNames = new();
        private readonly Dictionary<IntPtr, (IntPtr Type, int Offset)> _clockFields = new();

        // Synthetic metadata for the already pinned mapped bodies. No IL2CPP
        // method is invoked and this cannot provide a gameplay run or roots.
        internal void ConfigureClockMetadata(IntPtr module)
        {
            if (_clockFields.Count != 0) throw new InvalidOperationException("Clock metadata is once-only.");
            var progress = _classes[(_gameImage, "InLevel", "ProgressControl")];
            var progressType = Types.Keys.Single(type => Base.ClassOf(type) == progress);
            ClockInformationClass = Class(_gameImage, "InLevel", "LevelInformation", out var informationType);
            var chart = Class(_gameImage, "InLevel", "Chart", out var chartType);
            foreach (var (name, offset) in new[] { ("nowTime", 0xb0), ("nowRealTime", 0xb4), ("nowSyncedTime", 0xb8) })
                ClockField(ClockInformationClass, name, Base.Single, offset);
            ClockField(ClockInformationClass, "progressControl", progressType, 0x28);
            ClockField(ClockInformationClass, "chart", chartType, 0x88);
            ClockField(progress, "levelInformation", informationType, 0x20);
            ClockField(progress, "prepareToPlay", Base.Boolean, 0x40);
            ClockField(progress, "isPlaying", Base.Boolean, 0x42);
            ClockField(progress, "_offset", Double, 0x48);
            ClockField(progress, "_nowTime", Double, 0x50);
            ClockField(progress, "timeReady", Base.Boolean, 0x65);
            ClockField(chart, "chartDelayMs", Base.Int32, 0x14);
            ClockField(chart, "bPM", Base.Single, 0x30);
            ClockField(chart, "bpmShifts", Base.AddType("System.Collections.Generic.List<InLevel.KeyPoint>", 21, false, IntPtr.Size), 0x38);
            Add("ClockToChart", progress, "RealTimeToChartTime", 0x400c80, module, Double);
            Update("ClockToChart", row => row.ReturnType = Base.Single);
            Add("ClockToReal", progress, "ChartTimeToRealTime", 0x400400, module, Double);
            Update("ClockToReal", row => row.ReturnType = Base.Single);
            Add("ClockUpdate", progress, "Update", 0x400fe0, module);
            Add("ClockDelay", chart, "get_ChartDelay", 0x3e5670, module);
            Update("ClockDelay", row => row.ReturnType = Base.Single);
            Export("il2cpp_class_get_field_from_name", "FieldLookup", nameof(ClockFieldLookup));
            Export("il2cpp_field_get_type", "Ptr1", nameof(ClockFieldType));
            Export("il2cpp_field_get_offset", "FieldOffset", nameof(ClockFieldOffset));
            Export("il2cpp_field_get_flags", "Flags", nameof(ClockFieldFlags));
        }

        private void ClockField(IntPtr cls, string name, IntPtr type, int offset)
        {
            var field = Allocate(16);
            _clockFieldNames.Add((cls, name), field); _clockFields.Add(field, (type, offset));
        }
        private IntPtr ClockFieldLookup(IntPtr cls, string name)
        {
            Reads++;
            return _clockFieldNames.TryGetValue((cls, name), out var field) ? field : DeclaredField(cls, name);
        }
        private IntPtr ClockFieldType(IntPtr field)
        {
            if (!_clockFields.TryGetValue(field, out var value)) return DeclaredFieldType(field);
            Reads++;
            return _nativeTypeOriginals.Count == 0 ? value.Type : _nativeClassTypes[Base.ClassOf(value.Type)];
        }
        private int ClockFieldOffset(IntPtr field)
        {
            if (!_clockFields.TryGetValue(field, out var value)) return DeclaredFieldOffset(field);
            Reads++; return value.Offset;
        }
        private uint ClockFieldFlags(IntPtr field)
        {
            if (!_clockFields.ContainsKey(field)) return DeclaredFieldFlags(field);
            Reads++; return 0;
        }
    }
}
