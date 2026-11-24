using System.Runtime.InteropServices;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal void CanvasBasisUseStableBorrowedMetadata()
    {
        // Class/field/method names are borrowed engine metadata, not temporary
        // publications. Keep one stable allocation per current text, including
        // changed fault-injection names. Owned type_get_name buffers still use
        // the real fake il2cpp_free, and no handle/result allocation is released.
        _stableBorrowedMetadataText = new(StringComparer.Ordinal);
    }

    // Pure managed evidence helpers over the same official-export fixture.
    // They never acquire, drop, replace, or free a production native owner.
    internal IReadOnlyDictionary<uint, IntPtr> CanvasBasisCurrentRoots() => new Dictionary<uint, IntPtr>(_handles);
    internal int[] CanvasBasisDetachedBits(IntPtr array) => Enumerable.Range(0, _geometrySingleArrays[array].Length)
        .Select(index => Marshal.ReadInt32(array, ArrayHeaderBytes + index * sizeof(float))).ToArray();
    internal void CanvasBasisAddListTail(int move, bool speed)
    {
        var list = CanvasSourceList(move, speed); var original = CanvasSourceSlots(CanvasSourceBacking(list));
        var longer = CanvasArray(_seekKeyType, original.Concat(new[] { original[0] }).ToArray());
        TargetSet(list, "_items", longer); CanvasSourceRemember();
    }
    internal void CanvasBasisTrimFinishedDiagnostics()
    {
        // Only after every case assertion and final Dispose attempt. Keep native
        // allocations, the complete source graph, current roots, failed free
        // records, writer evidence, and the exact original public owners alive.
        CanvasSourceReads.Clear(); CanvasSourceCountReads.Clear(); CanvasSourceItemReads.Clear(); CanvasSourceExportLog.Clear();
        NoteViewFieldLog.Clear(); NoteViewDispatchLog.Clear(); CanvasPositionGetterCalls.Clear();
        NoteViewRootLog.RemoveAll(row => !_handles.ContainsKey(row.Handle));
        NoteViewFreeLog.RemoveAll(row => !row.InjectedFailure);
    }
}
