using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        // The target planner uses the current root owner rather than borrowing
        // a short-lived caller chart/view. All publication bookkeeping precedes
        // post-call withdrawal checks; ordinary flags/fingerIds paths are unchanged.
        private NativeRuntime.RootHandle TargetAdopt(IntPtr pointer)
        {
            VerifyOwnerThread();
            if (pointer == IntPtr.Zero) throw new InvalidDataException("A required target-state reference is absent.");
            if (_retained.TryGetValue(pointer.ToInt64(), out var existing)) { VerifyLocal(); return existing; }
            if (_roots.Count >= _rootLimit) throw new NotSupportedException("Target-state roots exceed their complete budget.");
            var size = checked(_roots.Count + 1);
            _roots.EnsureCapacity(size); _retained.EnsureCapacity(size); _addresses.EnsureCapacity(size);
            var root = _owner._api.Root(pointer);
            _roots.Add(root); _retained.Add(pointer.ToInt64(), root); _addresses.Add(root, pointer.ToInt64());
            VerifyLocal(); return root;
        }
        private NativeRuntime.RootHandle TargetReference(NativeRuntime.RootHandle receiver, IntPtr cls, string name, string type, int offset)
        {
            var field = Native(() => _owner._api.InspectExactDeclaredReadField(cls, name, type, false, offset));
            NativeRuntime.RootHandle? value = null; var published = false;
            Native(() =>
            {
                _owner._api.PublishExactReferenceField(receiver, field, token =>
                {
                    if (published) throw new InvalidOperationException("A target reference was published twice.");
                    published = true; value = TargetAdopt(token);
                }); return 0;
            });
            if (!published || value is null || Native(() => _owner._api.ObjectClass(Target(value))) != field.FieldClass)
                throw new UnsupportedSignatureException("A target field has another actual reference class.");
            return value;
        }
        private int TargetCount(NativeRuntime.RootHandle list, int maximum)
        {
            var cls = Native(() => _owner._api.ObjectClass(Target(list)));
            var method = Native(() => _owner._api.Method(cls, "get_Count", 0, isStatic: false));
            var count = Native(() => _owner._api.InvokeValueWithPublication<int>(method, Target(list)));
            if (count < 0 || count > maximum) throw new InvalidDataException("The complete target-state List exceeds its bound.");
            return count;
        }
        private NativeRuntime.RootHandle TargetItem(NativeRuntime.RootHandle list, int index, IntPtr exactClass)
        {
            var cls = Native(() => _owner._api.ObjectClass(Target(list)));
            var method = Native(() => _owner._api.Method(cls, "get_Item", 1, isStatic: false, parameterTypes: new[] { "System.Int32" }));
            NativeRuntime.RootHandle? result = null; var published = false;
            Native(() =>
            {
                using var argument = A.Value(index);
                _owner._api.InvokeReferenceWithPublication(method, Target(list), token =>
                {
                    if (published) throw new InvalidOperationException("A target List item published twice.");
                    published = true; result = TargetAdopt(token);
                }, argument); return 0;
            });
            if (!published || result is null || Native(() => _owner._api.ObjectClass(Target(result))) != exactClass)
                throw new UnsupportedSignatureException("The target List item has another actual element class.");
            return result;
        }
        private float TargetConvert(string methodName, double value, ISeekChartNativeProfile profile)
        {
            Native(() => { profile.VerifyCurrent(); return 0; });
            var method = Native(() => _owner._api.InspectDeclaredMethod(_progressClass, methodName,
                new[] { "System.Double" }, "System.Single", IntPtr.Zero));
            if (method.GenericMethod || method.InflatedMethod || (method.Flags & 0x10) != 0)
                throw new UnsupportedSignatureException("The target time converter has another exact signature.");
            var converted = Native(() =>
            {
                using var argument = A.Value(value);
                return _owner._api.InvokeValueWithPublication<float>(method.MethodInfo, Target(_progress), argument);
            });
            Native(() => { profile.VerifyCurrent(); return 0; });
            if (!float.IsFinite(converted)) throw new InvalidDataException("The official target converter returned a nonfinite coordinate.");
            return converted;
        }
        private sealed class TargetChartReader : IRetainedSeekChartReader
        {
            private readonly NativeSeekNoteFlagsAccess _source;
            private readonly ISeekChartNativeProfile _profile;
            private readonly IntPtr _keyClass;
            private bool _closed;
            internal TargetChartReader(NativeSeekNoteFlagsAccess source, ISeekChartNativeProfile profile)
            {
                _source = source; _profile = profile;
                _keyClass = source.Native(() => source._owner._api.Class("Assembly-CSharp.dll", "InLevel", "KeyPoint"));
            }
            public void VerifyAccess()
            {
                _source.VerifyOwnerThread();
                if (_closed) throw new ObjectDisposedException(nameof(TargetChartReader));
                _source.VerifyCurrent(fullGraph: false);
            }
            private NativeSeekRuntimeNoteView View()
            {
                VerifyAccess();
                return _source._view ?? throw new InvalidOperationException("The independently owned target NoteView is absent.");
            }
            public SeekChartIdentity ReadIdentity()
            { VerifyAccess(); _source.VerifyCurrent(); return _source._noteViewIdentity.Chart; }
            public int ReadLineCount() => View().ChartLines.Count;
            public SeekChartRawLine ReadLine(int ordinal) => View().ChartLines[ordinal].Data;
            public SeekChartMapping ReadMapping()
            {
                VerifyAccess(); var source = _source;
                var shifts = source.TargetReference(source._chart, source._owner._chart, "bpmShifts", "System.Collections.Generic.List<InLevel.KeyPoint>", 0x38);
                var count = source.TargetCount(shifts, NativeSeekChartPlan.MaximumBpmShifts);
                var points = new SeekChartBpmPoint[count];
                for (var i = 0; i < count; ++i)
                {
                    var point = source.TargetItem(shifts, i, _keyClass);
                    points[i] = new(source.Target(point).ToInt64(),
                        source.ValueField<float>(point, _keyClass, "time", "System.Single", 0x10),
                        source.ValueField<float>(point, _keyClass, "value", "System.Single", 0x14),
                        source.ValueField<int>(point, _keyClass, "easeType", "System.Int32", 0x18),
                        source.ValueField<float>(point, _keyClass, "floorPosition", "System.Single", 0x1c));
                }
                if (source.TargetCount(shifts, NativeSeekChartPlan.MaximumBpmShifts) != count
                    || source.Target(source.TargetReference(source._chart, source._owner._chart, "bpmShifts", "System.Collections.Generic.List<InLevel.KeyPoint>", 0x38)) != source.Target(shifts))
                    throw new InvalidOperationException("The complete BPM source changed during target mapping capture.");
                return new(source.ValueField<float>(source._chart, source._owner._chart, "bPM", "System.Single", 0x30),
                    source.Target(shifts).ToInt64(), count, Array.AsReadOnly(points));
            }
            public float ChartTimeToGameSeconds(double coordinate)
            { VerifyAccess(); return _source.TargetConvert("ChartTimeToRealTime", coordinate, _profile); }
            public void VerifyLocal(SeekChartIdentity expected)
            {
                _source.VerifyLocal();
                if (_closed) throw new ObjectDisposedException(nameof(TargetChartReader));
                if (_source._noteViewIdentity.Chart != expected) throw new InvalidOperationException("The target chart identity changed.");
            }
            // The containing target reader owns physical roots and disposes them
            // only after its chart plan has closed. This adapter closes its lease.
            public void Dispose() { _source.VerifyOwnerThread(); _closed = true; }
        }
    }
}
