using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekRendererCommandsChecks
{
    private static void NativeFailures()
    {
        for (var mode = 0; mode < 3; ++mode)
        {
            using var f = new Fixture(FakeNative.RendererShape.Inactive, 5); var p = f.Prepare(f.All(SeekRendererCommandKind.Appear));
            if (mode == 0) f.Native.BeforeRendererCommand = _ => throw new IOException("Synthetic entered native failure before its first store.");
            else if (mode == 1) f.Native.AfterRendererCommand = _ => throw new IOException("Synthetic native failure after the complete command.");
            else f.Native.AfterRendererStep = (_, _, stage) => { if (stage == "RendererChildStored") throw new IOException("Synthetic internal partial native renderer stores."); };
            var failure = Expect<IOException>(() => p.Execute()); f.ClearCallbacks(); var row = p.GetJournal().Commands.Single();
            Check(p.AttemptedCommands == 1 && f.Native.RendererCalls.Count == 1 && row.NativeEntered && !row.NativeReturned && row.SideEffectsUnknown
                && row.Failure is not null && row.After is not null && f.Native.RendererGets.Count == (mode == 0 ? 0 : 1)
                && f.Native.RendererBoolean(f.Native.RendererPlans[0], "active") == (mode == 1),
                "An entered before/after/internal-partial failure lost the exact native attempted prefix or fabricated a returned command.");
            if (mode == 0) Check(row.ExpectedReachableStateObserved, "A visible no-store native failure did not preserve its observed before state.");
            if (mode == 2) Check(!row.ExpectedReachableStateObserved, "An internal renderer-child store without activation was falsely attributed to a complete Appear.");
            RetainEffects(f, p, failure); var calls = f.Native.RendererCalls.Count;
            Expect<ObjectDisposedException>(() => p.Execute()); Check(f.Native.RendererCalls.Count == calls, "Unknown renderer effects were dispatched again after logical disposal.");
        }
    }
    private static void HiddenGrowth()
    {
        using var f = new Fixture(FakeNative.RendererShape.Growth); var p = f.Prepare(f.All(SeekRendererCommandKind.Appear));
        var before = p.GetJournal().Baseline!; f.Native.RendererFailHiddenGrowth = true;
        var failure = Expect<IOException>(() => p.Execute()); f.ClearCallbacks(); var row = p.GetJournal().Commands.Single();
        var hidden = f.Native.RendererCreatedChildren.Single();
        Check(p.AttemptedCommands == 1 && f.Native.RendererInstantiations == 1 && !f.Native.HasRootFor(hidden)
            && f.Native.RendererInteger(f.Native.RendererPools[0], "poolSize") == before.Pools.Single().PoolSize
            && f.Native.RendererGets.Count == 0 && row.NativeEntered && row.After is not null && row.SideEffectsUnknown
            && row.After.Plans.Single(plan => plan.Reference == f.Native.RendererPlans[0].ToInt64()).Active == false
            && row.After.Pools.Single().Available.Backing == before.Pools.Single().Available.Backing,
            "A hidden Instantiate failure was inferred to have no native effects from unchanged reachable pool storage.");
        RetainEffects(f, p, failure);
        Check(f.Native.RendererDestroyCalls == 0 && p.GetJournal().Commands.Single().After!.Children.All(child => child.Reference != hidden.ToInt64()),
            "The journal invented a publication or cleanup for an unreachable native allocation.");
    }
    private static void GuardAndSource()
    {
        using (var f = new Fixture(FakeNative.RendererShape.Inactive, 5))
        {
            var p = f.Prepare(f.All(SeekRendererCommandKind.Appear)); var fired = false;
            f.Native.AfterRendererRoot = (_, target) =>
            {
                // Alive(note) also roots this note as a value-call argument.
                // Require the large exception-owning void core as well, so the
                // injection occurs at the command's final receiver-root stage.
                if (fired || target != f.Native.RendererNotes[0] || !InMethod("InvokeCore", "NativeRuntime")
                    || !InMethod("InvokeVoidWithPublicationCore", "NativeRuntime")) return;
                fired = true; f.State.ObservePause(true);
            };
            Expect<InvalidOperationException>(() => p.Execute()); f.ClearCallbacks();
            Check(fired && p.AttemptedCommands == 0 && f.Native.RendererCalls.Count == 0 && !p.SideEffectsUnknown && !p.HasRetainedResponsibility
                && p.GetJournal().Commands.Count == 1 && !p.GetJournal().Commands.Single().NativeEntered,
                "Managed withdrawal during final dispatch marshaling entered the native command or invented effects responsibility.");
        }
        foreach (var key in new[] { "pause.paused", "progress.playing", "info.over" })
        {
            using var f = new Fixture();
            f.Native.FlagForeignSource(key, key != "pause.paused"); f.Native.RendererRememberOtherStorage();
            Expect<InvalidOperationException>(() => f.Prepare()); Check(f.Native.RendererCalls.Count == 0, "A non-ready/playing/ending native source acquired renderer effects during preparation.");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(f.All(SeekRendererCommandKind.Appear)); f.Native.NoteViewSwapAllSlots();
            Expect<InvalidOperationException>(() => p.Execute());
            Check(p.AttemptedCommands == 0 && f.Native.RendererCalls.Count == 0, "An in-place allNotes change escaped full source verification before dispatch.");
        }
        using (var f = new Fixture(FakeNative.RendererShape.Inactive, 5))
        {
            var p = f.Prepare(f.All(SeekRendererCommandKind.Appear));
            f.Native.AfterRendererCommand = row => { if (row.Note == 4) f.Native.NoteViewSwapAllSlots(); };
            var failure = Expect<InvalidOperationException>(() => p.Execute()); f.ClearCallbacks();
            Check(p.AttemptedCommands == 5 && !p.GetJournal().ExecutionFinished, "Late complete NoteView mutation escaped Execute's terminal source boundary.");
            RetainEffects(f, p, failure);
        }
    }
}
