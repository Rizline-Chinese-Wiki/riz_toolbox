using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekRendererCommandsChecks
{
    private static void RetiredBackingInverse()
    {
        foreach (var foreign in new[] { false, true })
        {
            WithInverseFixture(new Fixture(FakeNative.RendererShape.Active, 0, native =>
            {
                native.ConfigureRendererInverse();
                var list = native.RendererReference(native.RendererPools[0], "_childList");
                native.RendererReplaceBacking(list, 1);
                // Both returns execute inside one actual Note.Disappear call.
                native.RendererSetPlans(0, native.RendererPlans[0], native.RendererPlans[1]);
                native.RendererSetPlans(1);
            }), f =>
            {
                var pool = f.Native.RendererPools[0];
                var list = f.Native.RendererReference(pool, "_childList");
                var original = f.Native.RendererList(list);
                var first = f.Native.RendererReference(f.Native.RendererPlans[0], "poolChild");
                var second = f.Native.RendererReference(f.Native.RendererPlans[1], "poolChild");
                var third = f.Native.RendererReference(f.Native.RendererPlans[2], "poolChild");
                var commands = f.All(SeekRendererCommandKind.Preserve);
                commands[0] = commands[0] with { Kind = SeekRendererCommandKind.Disappear };
                var p = f.Prepare(commands); p.RequireRestorable();
                var baseline = p.GetJournal().Baseline!;
                Check(original.Size == 0 && original.Capacity.Count == 1 && original.Capacity[0] == IntPtr.Zero,
                    "The retired-backing case did not start with the intended actual empty one-slot array.");
                p.Execute();
                var returned = f.Native.RendererList(list);
                Check(f.Native.RendererCalls.Count == 1 && f.Native.RendererCalls[0].Operation == "Disappear"
                    && f.Native.RendererCalls[0].Returned && f.Native.RendererReturns.Select(row => row.Child).SequenceEqual(new[] { first, second }),
                    "The case substituted separate snapshots or separate commands for the actual two-plan native call.");
                Check(returned.Backing != original.Backing && returned.Capacity.Count == 2 && returned.Size == 2
                    && returned.Capacity.SequenceEqual(new[] { first, second })
                    && f.Native.RendererBackingGrowths.Count == 1
                    && f.Native.RendererBackingGrowths[0].Before == original.Backing
                    && f.Native.RendererSlots(original.Backing).SequenceEqual(new[] { first }),
                    "The actual call did not write the original array before retiring it during capacity growth.");
                if (foreign)
                {
                    f.Native.RendererSetSlot(original.Backing, 0, third);
                    var error = Expect<InvalidOperationException>(p.Restore);
                    Check(f.Native.RendererSlots(original.Backing).SequenceEqual(new[] { third }) && f.Native.RendererDestroyCalls == 0,
                        "The inverse overwrote an unrecognized later write to the retired original backing.");
                    RetainEffects(f, p, error);
                    return;
                }
                p.Restore(); var restored = f.Native.RendererList(list);
                Check(p.VerifyRestoredValues() && !p.SideEffectsUnknown && !p.HasRetainedResponsibility
                    && restored.Backing == original.Backing && restored.Size == original.Size && restored.Version == original.Version
                    && restored.Capacity.SequenceEqual(original.Capacity),
                    "The known same-call retired backing was not restored in place with complete original storage.");
                Check(baseline.Plans.All(plan => f.Native.RendererReference(new(plan.Reference), "poolChild").ToInt64() == plan.Child
                    && f.Native.RendererBoolean(new(plan.Reference), "active") == plan.Active)
                    && f.Native.RendererInteger(pool, "poolSize") == baseline.Pools.Single().PoolSize
                    && f.Native.RendererInteger(pool, "_childCount") == baseline.Pools.Single().ChildCount
                    && f.Native.RendererDestroyCalls == 0 && f.Native.RendererInstantiations == 0,
                    "A pure List backing inverse changed original plan ownership, pool counts or native child lifetime.");
            });
        }
    }
}
