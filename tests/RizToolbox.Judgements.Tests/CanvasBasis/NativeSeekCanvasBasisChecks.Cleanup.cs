using System.Reflection;
using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.CanvasBasis;

internal static partial class NativeSeekCanvasBasisChecks
{
    private static readonly List<(object Owner, Exception Failure)> RetainedManagedCounterOwners = new();

    private static void BudgetsAndUnknownCleanup()
    {
        // Exhaustion is a managed admission refusal, not a native free failure.
        // Reserve real empty owners so the boundary needs no lifetime of loops
        // or synthetic native acquisition; preserve their retained registry slots.
        using (var f = new Fixture())
        {
            var cache = NativeSeekCanvasCacheParticipant.Reserve();
            var position = NativeSeekCanvasPositionCommands.Reserve();
            typeof(NativeSeekCanvasCacheParticipant).GetField("_observationRevision", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(cache, long.MaxValue - 1);
            typeof(NativeSeekCanvasPositionCommands).GetField("_observationRevision", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(position, long.MaxValue - 1);
            NoNative(f.Native, () =>
            {
                var cacheFailure = Reject(cache.VerifyPrepared); var positionFailure = Reject(position.VerifyPrepared);
                Check(cache.ObservationRevision == long.MaxValue && position.ObservationRevision == long.MaxValue
                    && cache.HasRetainedResponsibility && position.HasRetainedResponsibility
                    && cache.GetJournal().CleanupFailure is null && position.GetJournal().CleanupFailure is null
                    && !cache.SideEffectsUnknown && !position.SideEffectsUnknown
                    && ReferenceEquals(Data(cacheFailure, "SeekCanvasCacheCleanupOwner"), cache)
                    && ReferenceEquals(Data(positionFailure, "SeekCanvasPositionCleanupOwner"), position),
                    "Managed revision exhaustion lost its original owner, dispatched native, or claimed an unknown free.");
                Reject(cache.Dispose); Reject(position.Dispose);
                RetainedManagedCounterOwners.Add((cache, cacheFailure)); RetainedManagedCounterOwners.Add((position, positionFailure));
            });
        }
        foreach (var type in new[] { typeof(NativeSeekCanvasBasisCommands), typeof(NativeSeekCanvasCacheParticipant),
            typeof(NativeSeekCanvasPositionCommands), typeof(NativeSeekCanvasTargetCapture) })
        {
            using var f = new Fixture();
            var owners = type.GetField("Owners", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            int Count() => (int)owners.GetType().GetProperty("Count")!.GetValue(owners)!;
            var initial = Count(); var reserved = new List<IDisposable>();
            var reserve = type.GetMethod("ReserveOwner", BindingFlags.NonPublic | BindingFlags.Instance)!;
            try
            {
                while (Count() < 256)
                {
                    var value = Activator.CreateInstance(type, nonPublic: true)!;
                    reserve.Invoke(value, null); reserved.Add((IDisposable)value);
                }
                NoNative(f.Native, () => Reject(() => f.Prepare()));
                Check(Count() == 256, "A capacity refusal consumed or discarded another original owner: " + type.Name);
                NoWrites(f.Native, "A bounded basis/child/source factory reached a writer before its owner reservation.");
            }
            finally
            {
                foreach (var value in reserved.AsEnumerable().Reverse()) value.Dispose();
            }
            Check(Count() == initial, "Pure managed reservation cleanup changed the pre-existing owners: " + type.Name);
        }

        // Unknown cache pins must freeze both siblings even when no position
        // setter has entered. The exact temporary publication remains separate.
        foreach (var store in new[] { false, true })
        {
            using var f = new Fixture(); var basis = f.Prepare(); var n = f.Native;
            var cache = basis.OriginalCacheOwner; var position = basis.OriginalPositionOwner;
            var roots = n.CanvasBasisCurrentRoots(); var fired = false; uint failed = 0;
            n.AfterCanvasSourceRoot = (handle, pointer, pinned) =>
            {
                if (fired || !pinned || pointer != n.CanvasCacheArray(0) || store && !InArrayWriter()) return;
                fired = true; failed = handle; n.FailFreeHandle = handle;
            };
            var failure = Reject(() => { if (store) basis.Execute(); else basis.VerifyPrepared(); }); f.Clear();
            f.AcceptUnknownPublication();
            var journal = basis.GetJournal(); var original = Data(failure, "SeekCanvasCacheSourceCleanupOwner");
            Check(fired && failed != 0 && original is not null
                && ReferenceEquals(Data(failure, "SeekCanvasCacheCleanupOwner"), cache)
                && ReferenceEquals(Data(failure, "SeekCanvasBasisOwner"), basis)
                && ReferenceEquals(journal.SourceCleanupOwner, original)
                && journal.HasRetainedResponsibility && journal.CleanupFailure is not null
                && cache.GetJournal().AttemptedWrites == (store ? 1 : 0) && position.GetJournal().AttemptedWrites == 0
                && n.NoteViewFreeLog.Count(row => row.Handle == failed && row.InjectedFailure) == 1,
                "Unknown cache pin lost original child/publication/source ownership or crossed into position execution.");
            OriginalRootsRemain(n, roots, "Unknown cache pin cleanup freed independently retained source roots.");
            NoNative(n, () =>
            {
                Reject(basis.VerifyPrepared); Reject(basis.Execute); Reject(basis.RestoreSelectedValues);
                Reject(position.VerifyPrepared); Reject(cache.VerifyPrepared); Reject(basis.Dispose);
            });
        }

        // A getter receiver/result free is already a native responsibility,
        // independently of whether a setter ever entered.
        foreach (var resultBox in new[] { false, true })
        {
            using var f = new Fixture(); var basis = f.Prepare(); var n = f.Native;
            var position = basis.OriginalPositionOwner; var roots = n.CanvasBasisCurrentRoots();
            var fired = false; uint failed = 0;
            if (!resultBox) n.AfterGeometryGetter = (_, receiver) =>
            {
                if (fired) return; fired = true;
                failed = n.NoteViewHandlesFor(receiver).Last(); n.FailFreeHandle = failed;
            };
            else n.AfterCanvasSourceRoot = (handle, pointer, _) =>
            {
                if (fired || pointer != n.CanvasPositionLastBox) return;
                fired = true; failed = handle; n.FailFreeHandle = handle;
            };
            var failure = Reject(basis.VerifyPrepared); f.Clear(); f.AcceptUnknownPublication();
            var journal = basis.GetJournal();
            Check(fired && failed != 0 && ReferenceEquals(Data(failure, "SeekCanvasPositionCleanupOwner"), position)
                && Data(failure, "SeekCanvasPositionSourceCleanupOwner") is object
                && position.GetJournal().AttemptedWrites == 0 && journal.CleanupFailure is not null
                && journal.HasRetainedResponsibility
                && n.NoteViewFreeLog.Count(row => row.Handle == failed && row.InjectedFailure) == 1,
                "Unknown position getter cleanup lost its actual publication or falsely required an entered setter.");
            OriginalRootsRemain(n, roots, "An unknown getter cleanup freed the complete shared source.");
            NoNative(n, () => { Reject(position.VerifyPrepared); Reject(basis.OriginalCacheOwner.VerifyPrepared); Reject(basis.Dispose); });
        }

        // Source construction fails after its capture/public owner exists but
        // before the aggregate factory can receive it.
        using (var f = new Fixture())
        {
            var n = f.Native; var point = n.CanvasSourcePoint(0, true, 2); var fired = false; uint failed = 0;
            n.AfterCanvasSourceRange = () =>
            {
                if (fired) return; fired = true; failed = n.NoteViewHandlesFor(point).Last(); n.FailFreeHandle = failed;
                throw new IOException("Synthetic shared source construction cleanup failure.");
            };
            var failure = Reject(() => f.Prepare()); f.Clear();
            var owner = Owner<NativeSeekCanvasBasisCommands>(failure, "SeekCanvasBasisOwner");
            Check(fired && failed != 0 && owner is not null && owner.HasRetainedResponsibility
                && owner.GetJournal().SourceCleanupOwner is object && owner.GetJournal().SourceCapture is not null
                && n.NoteViewFreeLog.Count(row => row.Handle == failed && row.InjectedFailure) == 1,
                "A failed factory lost the original capture/holder/public coordinator before assignment.");
            NoWrites(n, "Failed source construction reached a cache or position writer.");
            NoNative(n, () => { Reject(owner!.VerifyPrepared); Reject(owner!.Dispose); });
        }

        using (var f = new Fixture())
        {
            var basis = f.Prepare(); f.Run.Dispose(); var n = f.Native;
            var failed = n.NoteViewHandlesFor(n.CanvasCacheArray(1)).Last(); n.FailFreeHandle = failed;
            var failure = Reject(basis.Dispose); var journal = basis.GetJournal();
            Check(journal.Disposed && journal.HasRetainedResponsibility && journal.CleanupFailure is not null
                && ReferenceEquals(Data(failure, "SeekCanvasBasisOwner"), basis)
                && journal.SourceCleanupOwner is object && n.ActiveRoots == 0
                && n.NoteViewFreeLog.Count(row => row.Handle == failed && row.InjectedFailure) == 1,
                "Terminal shared source free lost original owner or skipped independent known cleanup.");
            NoNative(n, () => Reject(basis.Dispose));
        }

        // A later cache readback cleanup failure cannot hide already-entered
        // position engine effects, and disposal must retain both original owners.
        using (var f = new Fixture())
        {
            var basis = f.Prepare(); basis.Execute(); var n = f.Native;
            var cache = basis.OriginalCacheOwner; var position = basis.OriginalPositionOwner;
            var roots = n.CanvasBasisCurrentRoots(); var fired = false;
            n.AfterCanvasSourceRoot = (handle, pointer, pinned) =>
            {
                if (fired || !pinned || pointer != n.CanvasCacheArray(0)) return;
                fired = true; n.FailFreeHandle = handle;
            };
            var failure = Reject(basis.Dispose); f.Clear(); f.AcceptUnknownPublication();
            var journal = basis.GetJournal();
            Check(fired && journal.HasRetainedResponsibility && journal.ExternalEffectsUnclosed
                && ReferenceEquals(journal.OriginalCacheOwner, cache) && ReferenceEquals(journal.OriginalPositionOwner, position)
                && ReferenceEquals(Data(failure, "SeekCanvasPositionEffectsOwner"), position)
                && ReferenceEquals(Data(failure, "SeekCanvasCacheCleanupOwner"), cache)
                && journal.Failures.Count >= 2,
                "Aggregate disposal replaced or skipped one of the independent effects/cleanup owners.");
            OriginalRootsRemain(n, roots, "Mixed cache cleanup and position effects released the shared physical source.");
            NoNative(n, () => Reject(basis.Dispose));
        }
    }
}
