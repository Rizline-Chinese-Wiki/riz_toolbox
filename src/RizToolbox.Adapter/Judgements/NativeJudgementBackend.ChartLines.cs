using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private const int MaximumNoteBearingLines = 4096;

    // A stock chart can contain thousands of decorative lines. Admit a
    // wider physical scan only through the real closed list and its retained
    // backing vector. The note-bearing line and note-root budgets still apply
    // to the judgement coverage, using the chart's original ordinals.
    private sealed class ExpandedChartLineList : IDisposable
    {
        private readonly NativeRuntime _api;
        private readonly NativeRuntime.RootHandle _chart, _list, _array;
        private readonly NativeExactReadField _lines, _notes, _backing, _size, _version;
        private readonly int _count, _capturedVersion, _capacity;

        internal ExpandedChartLineList(NativeJudgementBackend owner, NativeRuntime.RootHandle chart,
            NativeRuntime.RootHandle list, int count)
        {
            (_api, _chart, _list, _count) = (owner._api, chart, list, count);
            _lines = _api.InspectExactDeclaredReadField(owner._chart, "lines", "System.Collections.Generic.List<InLevel.Line>", false, 0x40);
            _notes = _api.InspectExactDeclaredReadField(owner._line, "notes", "System.Collections.Generic.List<InLevel.Note>", false, 0x18);
            var cls = _lines.FieldClass;
            if (_api.ObjectClass(list.Target) != cls)
                throw new UnsupportedSignatureException("The actual judgement Chart.lines differs from its exact declared list class.");
            _backing = _api.InspectExactDeclaredReadField(cls, "_items", "InLevel.Line[]", false, 0x10);
            _size = _api.InspectExactDeclaredReadField(cls, "_size", "System.Int32", false, 0x18);
            _version = _api.InspectExactDeclaredReadField(cls, "_version", "System.Int32", false, 0x1C);
            _capturedVersion = _api.ReadExactField<int>(list, _version);
            _array = ReadReference(list, _backing);
            try
            {
                if (_api.Unbox<int>(_api.Call(_array.Target, "get_Rank")) != 1)
                    throw new UnsupportedSignatureException("The actual judgement Chart.lines backing storage is not a vector.");
                _capacity = ArrayLength();
                if (_capacity < count || _capacity > NativeArrayScanCursor.MaximumArrayLength)
                    throw new InvalidOperationException($"Judgement Chart.lines count {count} does not fit its validated backing capacity {_capacity}.");
                VerifyCurrent();
            }
            catch { _array.Dispose(); throw; }
        }

        internal NativeRuntime.RootHandle ReadNotes(NativeRuntime.RootHandle line) => ReadReference(line, _notes);

        internal void VerifyCurrent()
        {
            using var list = ReadReference(_chart, _lines);
            using var array = ReadReference(_list, _backing);
            if (list.Target != _list.Target || array.Target != _array.Target || ArrayLength() != _capacity
                || _api.ReadExactField<int>(_list, _size) != _count || _api.ReadExactField<int>(_list, _version) != _capturedVersion)
                throw new InvalidOperationException("The actual judgement Chart.lines list, logical size or backing storage changed during capture.");
        }

        private int ArrayLength() => _api.Unbox<int>(_api.Call(_array.Target, "get_Length"));

        private NativeRuntime.RootHandle ReadReference(NativeRuntime.RootHandle receiver, NativeExactReadField field)
        {
            NativeRuntime.RootHandle? retained = null;
            try
            {
                _api.PublishExactReferenceField(receiver, field, pointer =>
                {
                    if (pointer != IntPtr.Zero) retained = _api.Root(pointer);
                });
                if (retained is null || _api.ObjectClass(retained.Target) != field.FieldClass)
                    throw new UnsupportedSignatureException("The actual judgement source differs from its exact declared field: " + field.Name);
                return retained;
            }
            catch { retained?.Dispose(); throw; }
        }

        public void Dispose() => _array.Dispose();
    }
}
