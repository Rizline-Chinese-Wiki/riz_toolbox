namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekFingerIdsChecks
{
    private static void NativeFaults()
    {
        foreach (var operation in new[] { "Clear", "Add", "FieldNew", "FieldNull" })
        foreach (var after in new[] { false, true })
        {
            using var f = new Fixture(); var p = f.Prepare(); FakeNative.FingerWrite? failed = null;
            void Fault(FakeNative.FingerWrite row)
            {
                if (failed is not null || (operation.StartsWith("Field", StringComparison.Ordinal)
                    ? row.Operation != "fingerIds" || row.Note != (operation == "FieldNew" ? 0 : 2)
                    : row.Operation != operation || row.Receiver != f.Baseline[1].List)) return;
                failed = row; if (after) f.Native.FailFingerAfterStore = row.Ordinal; else f.Native.FailFingerBeforeStore = row.Ordinal;
            }
            if (after) f.Native.AfterFingerStore = Fault; else f.Native.BeforeFingerStore = Fault;
            Expect<IOException>(p.Commit); f.ClearCallbacks();
            Check(failed is not null && failed.Stored == after && p.AttemptedWrites != 0 && p.SideEffectsUnknown && p.Failure is not null,
                "A native before/after field or List failure lost its entered-write responsibility: " + operation); Restored(f, p);
        }
    }
    private static void RestorationFaults()
    {
        foreach (var operation in new[] { "SetValue", "_size", "_items", "_version", "fingerIds" })
        foreach (var after in new[] { false, true })
        {
            using var f = new Fixture(); var targets = f.Targets;
            targets[1] = targets[1] with { FingerIds = Enumerable.Range(0, 9).ToArray() };
            var p = f.Prepare(targets); p.Commit(); var attempted = p.AttemptedWrites; FakeNative.FingerWrite? failed = null;
            void Fault(FakeNative.FingerWrite row)
            {
                if (failed is not null || row.Operation != operation) return; failed = row;
                if (after) f.Native.FailFingerAfterStore = row.Ordinal; else f.Native.FailFingerBeforeStore = row.Ordinal;
            }
            if (after) f.Native.AfterFingerStore = Fault; else f.Native.BeforeFingerStore = Fault;
            p.Restore(); f.ClearCallbacks(); var restored = p.ConfirmRestored().OriginalStateRestored;
            Check(failed is not null && failed.Stored == after && p.AttemptedWrites == attempted
                && !f.Native.FingerWrites.Any(row => row.Ordinal > failed.Ordinal && row.Operation == failed.Operation
                    && row.Receiver == failed.Receiver && row.Slot == failed.Slot)
                && restored == after && f.Native.FingerStorageEquals(f.Baseline) == after && p.SideEffectsUnknown == !after,
                "A restoration dispatch was blindly retried or falsely confirmed after native failure: " + operation);
        }
    }
    private static void UnknownStates()
    {
        foreach (var operation in new[] { "Clear", "Add", "SetValue", "_size", "_items", "_version", "fingerIds" })
        {
            using var f = new Fixture(); var targets = f.Targets; targets[1] = targets[1] with { FingerIds = Enumerable.Range(0, 9).ToArray() };
            var p = f.Prepare(targets); var forward = operation is "Clear" or "Add"; if (!forward) p.Commit();
            FakeNative.FingerWrite? failed = null;
            f.Native.BeforeFingerStore = row =>
            {
                if (failed is not null || row.Operation != operation || forward && row.Receiver != f.Baseline[1].List) return;
                failed = row;
                switch (operation)
                {
                    case "Clear": f.Native.FingerSetSlot(f.Native.FingerBacking(row.Receiver), 0, 0); break;
                    case "Add": f.Native.FingerSetSlot(f.Native.FingerBacking(row.Receiver), 0, checked((int)row.Requested)); break;
                    case "SetValue": f.Native.FingerSetSlot(row.Receiver, row.Slot!.Value, 1234567); break;
                    case "_size": f.Native.FingerSetList(row.Receiver, "_size", 1); break;
                    case "_items": f.Native.FingerReplaceBacking(row.Receiver); break;
                    case "_version": f.Native.FingerSetList(row.Receiver, "_version", 1234567); break;
                    case "fingerIds":
                        var third = f.Baseline.Select(item => item.List).First(value => value != IntPtr.Zero
                            && value != new IntPtr(row.Requested) && value != f.Native.FingerReference(row.Note));
                        f.Native.FingerSetReference(row.Note, third); break;
                }
                throw new IOException("Synthetic unrecognized partial Int32/field store.");
            };
            if (forward) Expect<IOException>(p.Commit); else p.Restore(); f.ClearCallbacks();
            var writes = f.Native.FingerWrites.Count; if (forward) TryRestore(p);
            Check(failed is not null && !failed.Stored && p.SideEffectsUnknown && !p.ConfirmRestored().OriginalStateRestored
                && f.Native.FingerWrites.Count == writes,
                "Unrecognized partial storage or a third field reference was overwritten or falsely confirmed: " + operation);
        }
        using (var f = new Fixture(FakeNative.NoteViewShape.MultipleSegments))
        {
            var p = f.Prepare(); var ordinal = f.Native.FingerNotes.Count - 1;
            var scratch = f.Native.FingerCreatedLists.Last(); FakeNative.FingerWrite? failed = null;
            FakeNative.FingerListState? beforeScratch = null;
            f.Native.BeforeFingerStore = row =>
            {
                if (failed is not null || row.Operation != "Add" || row.Receiver != scratch) return;
                failed = row; beforeScratch = f.Native.FingerStorage(scratch);
                f.Native.FingerSetList(scratch, "_version", 1234567);
                throw new IOException("Synthetic unknown unpublished scratch Add.");
            };
            Expect<IOException>(p.Commit); f.ClearCallbacks();
            Check(failed is not null && !failed.Stored && f.Native.FingerReference(0) != IntPtr.Zero
                && f.Native.FingerReference(2) == IntPtr.Zero && f.Native.FingerReference(ordinal) == IntPtr.Zero,
                "The unknown scratch fixture did not retain an unpublished final note after a real live-write prefix.");
            var pending = Field(p, "_pending"); var access = Field(p, "_access");
            var retained = (System.Collections.IDictionary)Field(access, "_retained"); var root = retained[scratch.ToInt64()]!;
            var attempted = p.AttemptedWrites; var writes = f.Native.FingerWrites.Count;
            p.Restore(); var confirmation = p.ConfirmRestored(); var unknown = Items(Field(p, "_unknownScratch"));
            static object Property(object owner, string name) => owner.GetType().GetProperty(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner)!;
            Check(confirmation.OriginalStateRestored && f.Native.FingerStorageEquals(f.Baseline) && p.SideEffectsUnknown
                && Field(p, "_pending") is null && unknown.Length == 1 && ReferenceEquals(unknown[0], pending)
                && (int)Property(pending, "Ordinal") == ordinal && (long)Property(pending, "ListReference") == scratch.ToInt64()
                && (long)Property(pending, "FieldBefore") == 0 && Property(pending, "Kind").ToString() == "Add"
                && beforeScratch is not null && (int)Property(Property(pending, "ListBefore"), "Version") == beforeScratch.Version
                && Items(Field(access, "_roots")).Any(value => ReferenceEquals(value, root))
                && ((RizToolbox.Runtime.NativeRuntime.RootHandle)root).Target == scratch,
                "Source restoration discarded or falsely resolved the exact pending unpublished scratch mutation/root owner.");
            Check(p.AttemptedWrites == attempted && f.Native.FingerScalar(scratch, "_version") == 1234567
                && !f.Native.FingerWrites.Skip(writes).Any(row => row.Receiver == scratch)
                && !f.Native.FingerWrites.Any(row => row.Operation == "fingerIds" && row.Note == ordinal)
                && confirmation.Evidence.Contains("Unpublished scratch", StringComparison.Ordinal)
                && confirmation.Evidence.Contains("unknown", StringComparison.OrdinalIgnoreCase),
                "An unknown scratch was retried/published or its native effects were omitted from restoration evidence.");
            p.Dispose(); Check(p.SideEffectsUnknown && f.Native.NoteViewHandlesFor(scratch).Length == 0 && f.Native.FingerStorageEquals(f.Baseline),
                "Disposal undid source state, lost the unknown-effects claim or failed to release the known scratch root once.");
        }
    }
}
