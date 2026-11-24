namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekFingerIdsChecks
{
    private static void ExternalStorage()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(); var scratch = f.Native.FingerCreatedLists.Single(); var fired = false;
            f.Native.AfterNoteViewFieldRead = (name, owner) =>
            {
                if (fired || name != "fingerIds" || owner != f.Native.FingerNotes[0]
                    || !InMethod("WriteField", "NativeSeekNoteFlagsAccess")) return;
                fired = true; f.Native.FingerSetList(scratch, "_version", 1234567);
            };
            Expect<InvalidOperationException>(p.Commit); f.ClearCallbacks();
            Check(fired && f.Native.FingerReference(0) == IntPtr.Zero
                && !f.Native.FingerWrites.Any(row => row.Operation == "fingerIds" && row.Note == 0),
                "A completely filled scratch List changed before publication but was attached to its note.");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); p.Commit(); var detached = f.Baseline[2].List; var other = f.Baseline[1].Storage!; var fired = false;
            Check(f.Native.FingerReference(2) == IntPtr.Zero, "The late-restore fixture must first detach its original List.");
            f.Native.AfterFingerStore = row =>
            {
                if (fired || row.Operation != "SetValue" || row.Receiver != other.Backing) return;
                fired = true; f.Native.FingerSetList(detached, "_version", 1234567);
            };
            TryRestore(p); f.ClearCallbacks();
            Check(fired && f.Native.FingerReference(2) == IntPtr.Zero && !p.ConfirmRestored().OriginalStateRestored,
                "An old detached List changed after the initial restore check but was reattached before final confirmation.");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); var failed = false;
            f.Native.AfterFingerStore = row =>
            { if (!failed && row.Operation == "Add" && row.Receiver == f.Baseline[1].List) { failed = true; throw new IOException("Synthetic partial prefix."); } };
            Expect<IOException>(p.Commit); f.ClearCallbacks(); f.Native.FingerSetList(f.Baseline[2].List, "_version", 1234567);
            var writes = f.Native.FingerWrites.Count; TryRestore(p);
            Check(failed && f.Native.FingerWrites.Count == writes && p.SideEffectsUnknown,
                "A foreign unattempted original List was overwritten during restoration.");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); p.Commit(); FakeNative.FingerWrite? changed = null;
            f.Native.AfterFingerStore = row =>
            {
                if (changed is not null || row.Operation != "SetValue") return;
                changed = row; f.Native.FingerSetSlot(row.Receiver, row.Slot!.Value, 1234567);
            };
            TryRestore(p); f.ClearCallbacks();
            Check(changed is not null && f.Native.FingerArrayCapacity(changed.Receiver)[changed.Slot!.Value] == 1234567
                && !f.Native.FingerWrites.Any(row => row.Ordinal > changed.Ordinal && row.Operation == "SetValue"
                    && row.Receiver == changed.Receiver && row.Slot == changed.Slot) && !p.ConfirmRestored().OriginalStateRestored,
                "An already restored capacity slot changed externally and was overwritten a second time.");
        }
    }
    private static void ForeignGrowth()
    {
        for (var mode = 0; mode < 3; ++mode)
        {
            using var f = new Fixture(configure: n =>
            {
                n.FingerCapacity(1, mode == 2 ? 4 : 0);
                if (mode == 2)
                {
                    var list = n.FingerReference(1); n.FingerSetList(list, "_size", 0);
                    for (var slot = 0; slot < 4; ++slot) n.FingerSetSlot(n.FingerBacking(list), slot, 0);
                    n.FingerCapacity(4, 0);
                }
            });
            var targets = f.Targets; targets[1] = targets[1] with { FingerIds = Enumerable.Repeat(42, mode == 1 ? 6 : 1).ToArray() };
            if (mode == 2) targets[4] = targets[4] with { FingerIds = new[] { 42 } };
            var selected = f.Baseline[mode == 2 ? 4 : 1].List; var p = f.Prepare(targets); var fired = false; IntPtr foreign = IntPtr.Zero;
            f.Native.AfterFingerStore = row =>
            {
                if (fired || row.Operation != "Add" || row.Receiver != selected
                    || f.Native.FingerScalar(selected, "_size") != (mode == 1 ? 5 : 1)) return;
                fired = true;
                if (mode == 0) { f.Native.FingerReplaceBacking(selected, 7); foreign = f.Native.FingerBacking(selected); }
                else if (mode == 1)
                { foreign = f.Native.FingerGrowths[^1].OldBacking; f.Native.FingerSetSlot(foreign, 0, 1234567); }
                else { foreign = f.Native.FingerBacking(f.Baseline[1].List); f.Native.FingerSetList(selected, "_items", foreign); }
            };
            Expect<InvalidOperationException>(p.Commit); f.ClearCallbacks(); var writes = f.Native.FingerWrites.Count; TryRestore(p);
            Check(fired && p.SideEffectsUnknown && f.Native.FingerWrites.Count == writes && !p.ConfirmRestored().OriginalStateRestored
                && (mode == 1 ? f.Native.FingerArrayCapacity(foreign)[0] == 1234567 : f.Native.FingerBacking(selected) == foreign),
                "Unrecognized growth capacity, an altered intermediate old array or an introduced positive alias was accepted and overwritten.");
        }
    }
    private static void GuardAndSource()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(); var writes = f.Native.FingerWrites.Count; var fired = false;
            bool IsFingerList(IntPtr target) => f.Native.FingerCreatedLists.Contains(target) || f.Baseline.Any(row => row.List != IntPtr.Zero && row.List == target);
            void Withdraw() { if (fired) return; fired = true; f.State.ObservePause(true); }
            f.Native.AfterFingerRoot = (_, target) =>
            { if (IsFingerList(target) && (InMethod("InvokeCore", "NativeRuntime") || InMethod("WriteExactCore", "NativeRuntime"))) Withdraw(); };
            f.Native.DuringHandleTarget = target =>
            { if (f.Native.FingerNotes.Contains(target) && InMethod("WriteExactCore", "NativeRuntime")) Withdraw(); };
            Expect<InvalidOperationException>(p.Commit); f.ClearCallbacks();
            Check(fired && p.AttemptedWrites == 0 && f.Native.FingerWrites.Count == writes && f.Native.FingerStorageEquals(f.Baseline),
                "A run revision changed during final native preparation but crossed the live-write dispatch guard.");
        }
        foreach (var (key, value) in new[] { ("pause.paused", false), ("progress.ready", false), ("progress.playing", true), ("info.over", true) })
        {
            using var f = new Fixture(); f.Native.FingerForeignSource(key, value); var roots = f.Native.ActiveRoots;
            Expect<InvalidOperationException>(() => f.Prepare());
            Check(f.Native.ActiveRoots == roots && f.Native.FingerCreatedLists.Count == 0 && f.Native.FingerWrites.Count == 0,
                "A source outside ready-paused allocated scratch or admitted fingerIds writes: " + key);
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Native.NoteViewSwapAllSlots(); var writes = f.Native.FingerWrites.Count;
            Expect<InvalidOperationException>(p.Commit); Check(p.AttemptedWrites == 0 && f.Native.FingerWrites.Count == writes, "A changed actual NoteView reached a fingerIds writer.");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); p.Commit(); var writes = f.Native.FingerWrites.Count; var attempted = p.AttemptedWrites; var fired = false;
            f.Native.AfterFingerStore = row =>
            {
                if (fired || row.Operation != "fingerIds" || row.Note != 0 || row.Requested != 0) return;
                fired = true; f.Native.NoteViewSwapAllSlots();
            };
            Expect<InvalidOperationException>(p.Restore); f.ClearCallbacks();
            Check(fired && f.Native.FingerReference(0) == IntPtr.Zero && p.AttemptedWrites == attempted && p.SideEffectsUnknown
                && f.Native.FingerWrites.Skip(writes).Count(row => row.Operation == "fingerIds" && row.Note == 0
                    && row.Requested == 0 && row.Stored) == 1,
                "Restore completed normally after its final setter changed the complete actual NoteView, or lost that setter's stored responsibility.");
        }
    }
}
