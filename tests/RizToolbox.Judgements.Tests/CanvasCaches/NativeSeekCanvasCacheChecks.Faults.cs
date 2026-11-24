using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekCanvasCacheChecks
{
    private static bool InArrayWriter() => Environment.StackTrace.Contains("WriteRootedCanvasCacheSingle", StringComparison.Ordinal);
    private static bool InScalarWriter() => Environment.StackTrace.Contains("WriteCanvasCacheScalar", StringComparison.Ordinal);
    private static void PartialScalars()
    {
        foreach (var after in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare(); var n = f.Native; var baseline = n.CanvasCacheSnapshot();
            if (after) n.FailCanvasCacheScalarAfter = 1; else n.FailCanvasCacheScalarBefore = 1;
            var error = Reject(p.Commit); var j = p.GetJournal();
            Check(ReferenceEquals(error.Data["SeekCanvasCacheCleanupOwner"], p) && j.CommitEntered && !j.CommitFinished
                && !j.AppliedValuesVerified && j.SideEffectsUnknown && j.HasRetainedResponsibility
                && j.AttemptedWrites == 10 && j.Stores.Count == 10 && j.Stores[^1].Ordinal == 9
                && j.Stores[^1].NativeEntered && !j.Stores[^1].NativeReturned
                && j.Stores[^1].Readback == (after ? Bits(5) : baseline[9])
                && n.CanvasCacheScalarCalls.Count == 1 && n.CanvasCacheScalarCalls[0].Stored == after
                && n.CanvasCacheObservedStores.Count == (after ? 10 : 9),
                "Partially entered official scalar storage lost the exact public owner or before/after raw-bit evidence.");
            f.Clear(); NoNative(n, () => Reject(p.Commit)); p.Restore();
            Check(p.ConfirmRestored().OriginalStateRestored && n.CanvasCacheSnapshot().SequenceEqual(baseline)
                && p.GetJournal().AttemptedRestores == (after ? 10 : 9)
                && p.GetJournal().Stores.Where(row => row.Direction == SeekCanvasCacheDirection.Restore).All(row => row.Ordinal < 10)
                && !p.SideEffectsUnknown && !p.HasRetainedResponsibility,
                "Partial scalar inverse overwrote an untouched slot, retried storage, or failed to close pure-cache responsibility.");
        }
        foreach (var at in new[] { "metadata", "root" })
        {
            using var f = new Fixture(); var p = f.Prepare(); var n = f.Native; var fired = false;
            var original = n.CanvasCacheBits(3);
            void Change()
            {
                if (fired || n.CanvasCacheObservedStores.Count != 9 || !InScalarWriter()) return;
                fired = true; n.CanvasCacheSetBits(3, -1, Bits(77));
            }
            if (at == "root") n.AfterCanvasSourceRoot = (_, pointer, pinned) =>
            { if (!pinned && pointer == n.GeometryCanvasControl) Change(); };
            else n.AfterJudgeMetadata = (name, field) =>
            { if (name == "field.flags" && field == n.CanvasCacheField(3)) Change(); };
            var error = Reject(p.Commit); f.Clear(); f.Retain(p, error);
            Check(fired && n.CanvasCacheScalarCalls.Count == 0 && n.CanvasCacheBits(3) == Bits(77)
                && p.GetJournal().AttemptedWrites == 9 && p.GetJournal().UnrecognizedState && p.HasRetainedResponsibility,
                "Scalar metadata/temporary-root callback changed current raw bits but a later setter overwrote them: " + at);
            var stores = n.CanvasCacheObservedStores.Count; n.CanvasCacheSetBits(3, -1, original); Reject(p.Restore);
            Check(p.GetJournal().UnrecognizedState && p.GetJournal().AttemptedRestores == 0 && p.HasRetainedResponsibility
                && n.CanvasCacheObservedStores.Count == stores && n.CanvasCacheScalarCalls.Count == 0,
                "Returning a late scalar third state to its original expected bits erased sticky evidence or admitted an inverse: " + at);
        }
    }
    private static void PinAndPoststoreWithdrawal()
    {
        foreach (var kind in new[] { "revision", "backend", "dispose" })
        {
            using var f = new Fixture(); var p = f.Prepare(); var n = f.Native; var fired = false;
            n.AfterCanvasSourceRoot = (_, pointer, pinned) =>
            {
                if (fired || !pinned || pointer != n.CanvasCacheArray(0) || !InArrayWriter()) return;
                fired = true;
                if (kind == "dispose") p.Dispose(); else Revoke(f, kind);
                n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
            };
            Reject(p.Commit); f.Clear();
            Check(fired && p.GetJournal().AttemptedWrites == 0 && n.CanvasCacheObservedStores.Count == 0
                && n.CanvasCacheScalarCalls.Count == 0, "The final actual array writer pin callback reached storage after withdrawal: " + kind);
            OnlyCleanup(n, "Array pin withdrawal inspected metadata/storage after callback return.");
        }
        foreach (var at in new[] { "pin", "size" })
        {
            using var f = new Fixture(); var p = f.Prepare(); var n = f.Native;
            var original = n.CanvasCacheArray(0); var oldBits = n.CanvasCacheBits(0, 0); var fired = false;
            var replacement = n.CanvasSourceCloneArray(original);
            void Replace()
            {
                if (fired || !InArrayWriter()) return;
                fired = true; n.CanvasCacheReplace(0, replacement);
            }
            if (at == "pin") n.AfterCanvasSourceRoot = (_, pointer, pinned) => { if (pinned && pointer == original) Replace(); };
            else n.AfterGeometryArrayExport = (name, pointer) => { if (name == "size" && pointer == original) Replace(); };
            Reject(p.Commit); f.Clear();
            Check(fired && p.GetJournal().AttemptedWrites == 0 && n.CanvasCacheObservedStores.Count == 0
                && Marshal.ReadInt32(original, n.ArrayHeaderBytes) == oldBits && n.CanvasCacheBits(0, 0) == oldBits,
                "Final pin/size callback detached the saved array but production code still wrote it: " + at);
        }
        using (var conditional = new Fixture())
        {
            var p = conditional.Prepare(); var n = conditional.Native; var fired = false;
            var original = n.CanvasCacheBits(0, 0);
            n.AfterGeometryArrayExport = (name, pointer) =>
            {
                if (fired || name != "size" || pointer != n.CanvasCacheArray(0) || !InArrayWriter()) return;
                fired = true; n.CanvasCacheSetBits(0, 0, Bits(77));
            };
            Reject(p.Commit); conditional.Clear();
            Check(fired && p.GetJournal().AttemptedWrites == 0 && p.GetJournal().UnrecognizedState
                && n.CanvasCacheObservedStores.Count == 0 && n.CanvasCacheBits(0, 0) == Bits(77),
                "A changed expected raw array value was overwritten after the writer's pin/official-size callbacks.");
            n.CanvasCacheSetBits(0, 0, original); Reject(p.Restore);
            Check(p.GetJournal().UnrecognizedState && p.GetJournal().AttemptedRestores == 0
                && n.CanvasCacheObservedStores.Count == 0 && n.CanvasCacheScalarCalls.Count == 0,
                "Returning a late array third state to its original expected bits erased sticky evidence or admitted an inverse.");
        }
        foreach (var at in new[] { "revision", "backend", "dispose", "source" })
        {
            using var f = new Fixture(); var p = f.Prepare(); var n = f.Native; var fired = false;
            n.AfterCanvasCacheObservedStore = row =>
            {
                if (fired || row.Kind != SeekCanvasCacheKind.FloorPositions) return;
                fired = true;
                if (at == "dispose") p.Dispose();
                else if (at == "source") n.CanvasSourceSet(n.CanvasSourcePoint(0, true, 0), "value", 123f);
                else Revoke(f, at);
                n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
            };
            var error = Reject(p.Commit); f.Clear(); f.Retain(p, error);
            Check(fired && p.GetJournal().AttemptedWrites == 1 && n.CanvasCacheObservedStores.Count == 1
                && p.GetJournal().Stores[0].NativeEntered && p.HasRetainedResponsibility && p.SideEffectsUnknown
                && ReferenceEquals(error.Data["SeekCanvasCacheCleanupOwner"], p),
                "Actual poststore callback lost the pending first-slot owner or allowed another physical store: " + at);
            if (at != "source") OnlyCleanup(n, "Array poststore withdrawal continued exports instead of known temporary cleanup only.");
            var writes = n.CanvasCacheObservedStores.Count; Reject(p.Restore);
            Check(n.CanvasCacheObservedStores.Count == writes, "Revoked/source-changed partial cache owner reached an inverse store.");
        }
    }
    private static void CallbackWithdrawal()
    {
        foreach (var at in new[] { "field", "cache-size", "sample-size" })
        foreach (var kind in new[] { "revision", "backend", "dispose" })
        {
            using var f = new Fixture(); var p = f.Prepare(); var n = f.Native; var fired = false;
            void Withdraw()
            {
                if (fired) return; fired = true;
                if (kind == "dispose") p.Dispose(); else Revoke(f, kind);
                n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
            }
            if (at == "field") n.AfterCanvasSourceRead = (name, owner) => { if (name == "nowTime" && owner == n.GeometryCanvasControl) Withdraw(); };
            else n.AfterGeometryArrayExport = (name, pointer) =>
            { if (name == "size" && pointer == (at == "cache-size" ? n.CanvasCacheArray(1) : n.CanvasSourceSamples[0])) Withdraw(); };
            Reject(p.VerifyPrepared); f.Clear();
            Check(fired && n.CanvasCacheObservedStores.Count == 0 && p.GetJournal().AttemptedWrites == 0,
                "Read callback revocation reached a cache store: " + at + "/" + kind);
            OnlyCleanup(n, "Field/array/sample read callback withdrawal continued native inspection.");
            NoNative(n, () => Reject(p.VerifyPrepared));
        }
        foreach (var after in new[] { false, true })
        foreach (var kind in new[] { "revision", "backend", "dispose" })
        {
            using var f = new Fixture(); var p = f.Prepare(); var n = f.Native; var fired = false;
            void Withdraw(string _)
            {
                if (fired) return; fired = true;
                if (kind == "dispose") p.Dispose(); else Revoke(f, kind);
                n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
            }
            if (after) { n.AfterCanvasCacheScalar = Withdraw; n.FailCanvasCacheScalarAfter = 1; }
            else { n.BeforeCanvasCacheScalar = Withdraw; n.FailCanvasCacheScalarBefore = 1; }
            var error = Reject(p.Commit); f.Clear(); f.Retain(p, error);
            Check(fired && n.CanvasCacheScalarCalls.Count == 1 && p.GetJournal().AttemptedWrites == 10
                && p.GetJournal().Stores[^1].NativeEntered && !p.GetJournal().Stores[^1].NativeReturned
                && p.HasRetainedResponsibility && p.SideEffectsUnknown
                && ReferenceEquals(error.Data["SeekCanvasCacheCleanupOwner"], p),
                "Partial scalar exception/withdrawal lost entered storage responsibility or dispatched the next scalar.");
            OnlyCleanup(n, "Throwing scalar callback withdrawal continued getters, metadata or error inspection.");
            NoNative(n, () => { Reject(p.Commit); Reject(p.Restore); });
        }
    }
    private static void ThirdStates()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Native.CanvasCacheSetBits(0, 0, Bits(77)); Reject(p.Commit);
            Check(p.GetJournal().AttemptedWrites == 0 && f.Native.CanvasCacheObservedStores.Count == 0,
                "A changed precommit baseline was overwritten.");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); p.Commit(); Check(p.Confirm().Observed == true, "Initial complete target cache confirmation failed.");
            var target = p.GetJournal().Slots[0].TargetBits; f.Native.CanvasCacheSetBits(0, 0, Bits(77));
            Check(DoesNotConfirm(() => p.Confirm().Observed == true) && p.GetJournal().UnrecognizedState
                && !p.GetJournal().AppliedValuesVerified, "An actual third state left stale target proof or no sticky evidence.");
            var writes = f.Native.CanvasCacheObservedStores.Count; Reject(p.Restore);
            Check(f.Native.CanvasCacheBits(0, 0) == Bits(77), "Conditional inverse overwrote another owner's third state.");
            f.Native.CanvasCacheSetBits(0, 0, target); Reject(p.Restore); f.Retain(p);
            Check(f.Native.CanvasCacheObservedStores.Count == writes && p.GetJournal().UnrecognizedState && p.HasRetainedResponsibility,
                "A third state returning to target erased sticky evidence or admitted another inverse.");
        }
        using var unattempted = new Fixture(); var q = unattempted.Prepare(); unattempted.Native.FailCanvasCacheScalarBefore = 1;
        Reject(q.Commit); unattempted.Clear(); unattempted.Native.CanvasCacheSetBits(4, -1, Bits(77));
        var before = unattempted.Native.CanvasCacheObservedStores.Count; Reject(q.Restore); unattempted.Retain(q);
        Check(q.GetJournal().UnrecognizedState && q.GetJournal().AttemptedRestores == 0
            && unattempted.Native.CanvasCacheBits(4) == Bits(77) && unattempted.Native.CanvasCacheObservedStores.Count == before,
            "Restoration ignored/overwrote an externally changed never-attempted scalar or manufactured successful rollback.");
    }
}
