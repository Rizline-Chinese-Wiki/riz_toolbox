using System.Collections;
using System.Runtime.InteropServices;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal readonly NativeVector3BufferBits ProducerInitialStart = new(unchecked((int)0x7fc12345),
        unchecked((int)0x80000000), 0x3f800000);
    internal readonly NativeVector3BufferBits ProducerInitialEnd = new(0x40000000, 0x40400000, 0x40800000);
    internal readonly NativeVector3BufferBits ProducerInitialNew = new(0x40a00000, unchecked((int)0x7fc54321),
        unchecked((int)0x80000000));
    internal const int ProducerCountCanary = 0x12345678;
    internal uint? ProducerObjectSizeOverride;
    internal IntPtr ProducerArrayClass => ClassOf(_lineBufferArrayType);
    private HashSet<uint>? _producerEvidenceHandles;
    private int _producerRetainedRootHistory, _producerRetainedFreeHistory;
    private readonly HashSet<JudgeMetadataCall> _producerObservedJudgeMetadata = new();
    private readonly HashSet<IntPtr> _producerObservedJudgeReferences = new();
    internal long ProducerCompactedJudgeMetadata, ProducerCompactedJudgeReferences;
    internal int ProducerHistoryCompactions;
    private readonly BitArray _producerDiscardedFreeHandles = new(1024);
    private bool _producerDuplicateDiscardedFree;
    internal long ProducerCompactedRootHistory, ProducerCompactedFreeHistory;

    // Compaction changes only historical storage. Keep an exact, compact bit
    // for every discarded free so the combined fixture still proves that no
    // handle was released twice, including across a compaction boundary.
    internal bool ProducerCompleteRootHistory()
        => !_producerDuplicateDiscardedFree
            && ProducerCompactedRootHistory + NoteViewRootLog.Count == NewRoots
            && ProducerCompactedFreeHistory + NoteViewFreeLog.Count == FreeHandleAttempts
            && NoteViewFreeLog.Select(row => row.Handle).Distinct().Count() == NoteViewFreeLog.Count
            && NoteViewFreeLog.All(row => row.Handle >= _producerDiscardedFreeHandles.Length
                || !_producerDiscardedFreeHandles[checked((int)row.Handle)]);

    // These renderer-specific cases assert the cumulative export/read/root/free
    // counters, current handles, original owner identities, and failed frees.
    // Bound only their redundant historical observations. Keep every HGlobal,
    // active root, failed-free record, callback and actual native operation.
    internal void ProducerBeginBoundedHistory()
    {
        if (_producerEvidenceHandles is not null)
            throw new InvalidOperationException("Producer history tracking starts once on this fixture.");
        _producerEvidenceHandles = new(_handles.Keys);
        ProducerCompactHistory(force: true);
    }

    private void ProducerCompactHistory(bool force = false)
    {
        var evidence = _producerEvidenceHandles;
        if (evidence is null) return;
        const int interval = 8192;
        if (!force && CanvasSourceReads.Count < interval && CanvasSourceCountReads.Count < interval
            && CanvasSourceItemReads.Count < interval
            && JudgeMetadataLog.Count < interval && JudgeInspectedCapacityReferences.Count < interval
            && NoteViewRootLog.Count - _producerRetainedRootHistory < interval
            && NoteViewFreeLog.Count - _producerRetainedFreeHistory < interval) return;
        // A persistent root adopted after the initial boundary joins the proof
        // set while live. Preserve sampled temporary handles conservatively too.
        evidence.UnionWith(_handles.Keys);
        evidence.UnionWith(NoteViewFreeLog.Where(row => row.InjectedFailure).Select(row => row.Handle));
        ProducerCompactedRootHistory += NoteViewRootLog.RemoveAll(row => !evidence.Contains(row.Handle));
        NoteViewFreeLog.RemoveAll(row =>
        {
            if (evidence.Contains(row.Handle) || row.InjectedFailure) return false;
            var index = checked((int)row.Handle);
            if (index >= _producerDiscardedFreeHandles.Length)
                _producerDiscardedFreeHandles.Length = Math.Max(checked(index + 1), checked(_producerDiscardedFreeHandles.Length * 2));
            if (_producerDiscardedFreeHandles[index]) _producerDuplicateDiscardedFree = true;
            _producerDiscardedFreeHandles[index] = true;
            ++ProducerCompactedFreeHistory;
            return true;
        });
        _producerRetainedRootHistory = NoteViewRootLog.Count;
        _producerRetainedFreeHistory = NoteViewFreeLog.Count;
        CanvasSourceReads.Clear(); CanvasSourceCountReads.Clear(); CanvasSourceItemReads.Clear();
        // The complete renderer cases do not assert these historical Judge
        // sequences. Every metadata operation, callback and rooted-reference
        // inspection still executes; retain each distinct observation and its
        // cumulative count, without growing a multi-billion-entry history.
        // Other Judge tests never enable this renderer-only boundary.
        ProducerCompactedJudgeMetadata += JudgeMetadataLog.Count;
        ProducerCompactedJudgeReferences += JudgeInspectedCapacityReferences.Count;
        _producerObservedJudgeMetadata.UnionWith(JudgeMetadataLog);
        _producerObservedJudgeReferences.UnionWith(JudgeInspectedCapacityReferences);
        JudgeMetadataLog.Clear(); JudgeInspectedCapacityReferences.Clear();
        ++ProducerHistoryCompactions;
    }

    private void ConfigureLineProducerControl()
    {
        foreach (var (name, offset, bits) in new[]
        {
            ("startPosition", 0x50, ProducerInitialStart),
            ("endPosition", 0x5c, ProducerInitialEnd),
            ("newLinePosition", 0x68, ProducerInitialNew)
        })
        {
            SeekField("producer.control." + name, _geometryLineControlType, name,
                _geometryVectorType, LineRingControl, 0f, offset);
            var bytes = new[] { bits.X, bits.Y, bits.Z }.SelectMany(BitConverter.GetBytes).ToArray();
            _gameplayStorage[(LineRingControl, TargetField(LineRingControl, name))] = bytes;
            Marshal.Copy(bytes, 0, IntPtr.Add(LineRingControl, offset), 12);
        }
        SeekField("producer.control.ease", _geometryLineControlType, "easeType", Int32, LineRingControl, -17, 0x80);
        SeekField("producer.control.count", _geometryLineControlType, "linePointCount", Int32,
            LineRingControl, ProducerCountCanary, 0x84);
        Marshal.WriteInt32(LineRingControl, 0x80, -17);
        Marshal.WriteInt32(LineRingControl, 0x84, ProducerCountCanary);
        Add("il2cpp_field_get_value", "FieldGet", nameof(ProducerReadControlField));
        Add("il2cpp_object_get_size", "ArraySize", nameof(ProducerControlObjectSize));
        Add("il2cpp_class_instance_size", "ArraySize", nameof(ProducerControlInstanceSize));
    }

    internal NativeLineProducerControlSnapshot ProducerRawControl()
    {
        NativeVector3BufferBits V(int offset) => new(Marshal.ReadInt32(LineRingControl, offset),
            Marshal.ReadInt32(LineRingControl, offset + 4), Marshal.ReadInt32(LineRingControl, offset + 8));
        return new(LineRingControl, V(0x50), V(0x5c), V(0x68), Marshal.ReadInt32(LineRingControl, 0x80));
    }
    internal int ProducerRawCount => Marshal.ReadInt32(LineRingControl, 0x84);
    internal void ProducerOverwrite(NativeLineProducerControlSnapshot state)
    {
        if (state.Control != LineRingControl) throw new InvalidOperationException("Foreign producer injection.");
        foreach (var (offset, value) in new[] { (0x50, state.StartPosition), (0x5c, state.EndPosition), (0x68, state.NewLinePosition) })
        {
            Marshal.WriteInt32(LineRingControl, offset, value.X);
            Marshal.WriteInt32(LineRingControl, offset + 4, value.Y);
            Marshal.WriteInt32(LineRingControl, offset + 8, value.Z);
        }
        Marshal.WriteInt32(LineRingControl, 0x80, state.EaseType);
    }

    private void ProducerReadControlField(IntPtr owner, IntPtr field, IntPtr output)
    {
        ProducerCompactHistory();
        var name = _fieldMetadata[field].Name;
        if (owner == LineRingControl && name is "startPosition" or "endPosition" or "newLinePosition" or "easeType" or "linePointCount")
        {
            ++FieldReads; AllGameplayReceiversRooted &= HasRootFor(owner);
            var offset = _fieldMetadata[field].Offset;
            var size = name is "easeType" or "linePointCount" ? 4 : 12;
            var bytes = new byte[size]; Marshal.Copy(IntPtr.Add(owner, offset), bytes, 0, size);
            Marshal.Copy(bytes, 0, output, size);
            CanvasSourceReads.Add((name, owner)); AfterCanvasSourceRead?.Invoke(name, owner);
            return;
        }
        LineRendererReadField(owner, field, output);
    }
    private uint ProducerControlObjectSize(IntPtr owner)
        => owner == LineRingControl ? ProducerObjectSizeOverride ?? 0x88u : LineRendererObjectSize(owner);
    private uint ProducerControlInstanceSize(IntPtr cls)
        => cls == ClassOf(_geometryLineControlType) ? 0x88u : LineRendererInstanceSize(cls);
}
