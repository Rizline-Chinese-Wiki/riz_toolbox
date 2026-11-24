using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private bool _seekDisposed;
    /// <summary>Full-chart read path only. The caller supplies its already verified
    /// fixed clock profile, including any real GetTime hook lease. No default
    /// unhooked profile is constructed and no native reset authority is issued.</summary>
    private sealed class NativeSeekChartReader : IRetainedSeekChartReader
    {
        private readonly NativeJudgementBackend _owner;
        private readonly ObservedJudgementRun _run;
        private readonly ISeekChartNativeProfile _profile;
        private readonly Roots _roots = new();
        private readonly Dictionary<long, NativeRuntime.RootHandle> _retained = new();
        private readonly NativeRuntime.RootHandle _controllerRoot, _informationRoot, _chartRoot, _progressRoot;
        private readonly IntPtr _keyPoint;
        private readonly long _controllerAddress, _informationAddress, _chartAddress, _progressAddress;
        private const int MaximumRetainedReferences = 2 * (2 * NativeSeekChartPlan.MaximumNotes
            + 2 * NativeSeekChartPlan.MaximumLines + NativeSeekChartPlan.MaximumBpmShifts) + 16;
        private bool _disposed;

        internal NativeSeekChartReader(NativeJudgementBackend owner, RunValue run, ISeekChartNativeProfile profile)
        {
            _owner = owner; _run = run.Run; _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            try
            {
                profile.VerifyCurrent();
                if (profile.ChartClass != owner._chart)
                    throw new UnsupportedSignatureException("The fixed converter belongs to another chart metadata identity.");
                _controllerRoot = Exact(run.Controller.Target, owner._controller);
                _informationRoot = Exact(run.Information.Target, owner._info);
                _chartRoot = Exact(run.Chart.Target, owner._chart);
                var api = owner._api;
                _progressRoot = Exact(api.Read<IntPtr>(_informationRoot.Target, "progressControl"), profile.ProgressClass);
                _controllerAddress = _controllerRoot.Target.ToInt64();
                _informationAddress = _informationRoot.Target.ToInt64();
                _chartAddress = _chartRoot.Target.ToInt64();
                _progressAddress = _progressRoot.Target.ToInt64();
                _keyPoint = api.Class("Assembly-CSharp.dll", "InLevel", "KeyPoint");
                api.RequireInstanceFieldLayout(owner._chart, "bPM", "System.Single", 0x30);
                api.RequireInstanceFieldLayout(owner._chart, "bpmShifts", "System.Collections.Generic.List<InLevel.KeyPoint>", 0x38);
                api.RequireInstanceFieldLayout(owner._chart, "lines", "System.Collections.Generic.List<InLevel.Line>", 0x40);
                api.RequireInstanceFieldLayout(owner._line, "notes", "System.Collections.Generic.List<InLevel.Note>", 0x18);
                foreach (var (field, type, offset) in new[] { ("type", "System.Int32", 0x10), ("time", "System.Single", 0x14),
                    ("floorPosition", "System.Single", 0x18), ("otherInformations", "System.Collections.Generic.List<System.Single>", 0x20) })
                    api.RequireInstanceFieldLayout(owner._noteData, field, type, offset);
                foreach (var (field, type, offset) in new[] { ("time", "System.Single", 0x10), ("value", "System.Single", 0x14),
                    ("easeType", "System.Int32", 0x18), ("floorPosition", "System.Single", 0x1c) })
                    api.RequireInstanceFieldLayout(_keyPoint, field, type, offset);
                ReadIdentity();
            }
            catch { _roots.Dispose(); throw; }
        }
        public void VerifyAccess()
        {
            _chartRoot.VerifyOwnerThread();
            if (_disposed || _owner._seekDisposed) throw new ObjectDisposedException(nameof(NativeSeekChartReader));
        }
        public void VerifyLocal(SeekChartIdentity expected)
        {
            VerifyAccess();
            // No RootHandle.Target here: its native getter may synchronously reenter.
            if (expected.Chart != _chartAddress || expected.Progress != _progressAddress
                || expected.Run != _owner._state.CaptureJudgementRun(_informationAddress, _controllerAddress)
                || !_owner._state.MatchesCurrentGameplayRun(_informationAddress, _controllerAddress,
                    expected.Run.RunId, expected.Run.SceneGeneration))
                throw new InvalidOperationException("The captured run ended or changed at the local publication boundary.");
        }
        private void RequireRetentionCapacity()
        {
            if (_retained.Count >= MaximumRetainedReferences)
                throw new InvalidOperationException("The independently retained chart exceeded its graph-reference bound.");
        }
        private NativeRuntime.RootHandle Exact(IntPtr value, IntPtr type)
        {
            if (_retained.TryGetValue(value.ToInt64(), out var root))
            {
                if (!_owner._api.ExactHookReceiverMatches(root.Target, type))
                    throw new UnsupportedSignatureException("A retained graph reference changed its exact type.");
                return root;
            }
            RequireRetentionCapacity();
            root = _roots.Add(_owner._api.RetainExactObject(value, type));
            _retained.Add(value.ToInt64(), root); return root;
        }
        private NativeRuntime.RootHandle List(IntPtr value)
        {
            if (_retained.TryGetValue(value.ToInt64(), out var root)) return root;
            RequireRetentionCapacity();
            root = _roots.Add(_owner._api.Root(value)); _retained.Add(value.ToInt64(), root); return root;
        }
        public SeekChartIdentity ReadIdentity()
        {
            VerifyAccess(); _profile.VerifyCurrent();
            var api = _owner._api;
            var controller = _controllerRoot.Target; var information = _informationRoot.Target; var chart = _chartRoot.Target;
            var current = _owner._state.CaptureJudgementRun(information.ToInt64(), controller.ToInt64());
            if (current is null || !_run.SameGeneration(current)
                || controller.ToInt64() != _controllerAddress || information.ToInt64() != _informationAddress
                || chart.ToInt64() != _chartAddress || !_owner.Alive(controller) || !_owner.Alive(information)
                || api.Read<IntPtr>(controller, "LevelInformation") != information
                || api.Read<IntPtr>(information, "chart") != chart
                || _owner._api.Read<IntPtr>(_informationRoot.Target, "progressControl") != _progressRoot.Target
                || _owner._api.Read<IntPtr>(_progressRoot.Target, "levelInformation") != _informationRoot.Target)
                throw new InvalidOperationException("The full-chart reader lost its captured run or progress/graph association.");
            var lines = List(_owner._api.Read<IntPtr>(_chartRoot.Target, "lines"));
            return new(current, _chartRoot.Target.ToInt64(), _progressRoot.Target.ToInt64(), lines.Target.ToInt64());
        }
        public int ReadLineCount()
        {
            VerifyAccess();
            var lines = List(_owner._api.Read<IntPtr>(_chartRoot.Target, "lines"));
            return _owner.Count(lines.Target, NativeSeekChartPlan.MaximumLines);
        }
        public SeekChartMapping ReadMapping()
        {
            VerifyAccess();
            var api = _owner._api;
            var list = List(api.Read<IntPtr>(_chartRoot.Target, "bpmShifts"));
            var count = _owner.Count(list.Target, NativeSeekChartPlan.MaximumBpmShifts);
            var points = new SeekChartBpmPoint[count];
            for (var i = 0; i < count; ++i)
            {
                var point = Exact(_owner.Item(list.Target, i), _keyPoint);
                points[i] = new(point.Target.ToInt64(), api.Read<float>(point.Target, "time"),
                    api.Read<float>(point.Target, "value"), api.Read<int>(point.Target, "easeType"),
                    api.Read<float>(point.Target, "floorPosition"));
            }
            if (_owner.Count(list.Target, NativeSeekChartPlan.MaximumBpmShifts) != count)
                throw new InvalidOperationException("The complete BPM list changed during traversal.");
            return new(api.Read<float>(_chartRoot.Target, "bPM"), list.Target.ToInt64(), count, Array.AsReadOnly(points));
        }
        public SeekChartRawLine ReadLine(int ordinal)
        {
            VerifyAccess();
            var api = _owner._api;
            var lines = List(api.Read<IntPtr>(_chartRoot.Target, "lines"));
            var line = Exact(_owner.Item(lines.Target, ordinal), _owner._line);
            var notes = List(api.Read<IntPtr>(line.Target, "notes"));
            var count = _owner.Count(notes.Target, NativeSeekChartPlan.MaximumNotes);
            var copied = new SeekChartRawNote[count];
            for (var i = 0; i < count; ++i)
            {
                var note = Exact(_owner.Item(notes.Target, i), _owner._noteData);
                var extra = List(api.Read<IntPtr>(note.Target, "otherInformations"));
                var extraCount = _owner.Count(extra.Target, 3);
                var values = new float[extraCount];
                for (var j = 0; j < extraCount; ++j)
                    values[j] = api.Unbox<float>(api.CallTyped(extra.Target, "get_Item", new[] { "System.Int32" }, A.Value(j)));
                if (_owner.Count(extra.Target, 3) != extraCount)
                    throw new InvalidOperationException("A note information list changed during traversal.");
                copied[i] = new(note.Target.ToInt64(), extra.Target.ToInt64(), api.Read<int>(note.Target, "type"),
                    api.Read<float>(note.Target, "time"), api.Read<float>(note.Target, "floorPosition"),
                    Array.AsReadOnly(values));
            }
            if (_owner.Count(notes.Target, NativeSeekChartPlan.MaximumNotes) != count)
                throw new InvalidOperationException("A complete chart note list changed during traversal.");
            return new(line.Target.ToInt64(), notes.Target.ToInt64(), count, Array.AsReadOnly(copied));
        }
        public float ChartTimeToGameSeconds(double chartCoordinate)
        {
            VerifyAccess(); _profile.VerifyCurrent();
            // Existing fixed metadata/body profile: instance Single(Double), RVA
            // 0x400400, full 624-byte SHA. No local BPM formula or offset is added.
            var value = _owner._api.Unbox<float>(_owner._api.CallTyped(_progressRoot.Target,
                "ChartTimeToRealTime", new[] { "System.Double" }, A.Value(chartCoordinate)));
            _profile.VerifyCurrent(); return value;
        }
        public void Dispose()
        {
            _chartRoot.VerifyOwnerThread();
            if (_disposed) return;
            _disposed = true;
            _retained.Clear(); _roots.Dispose();
        }
    }
}
