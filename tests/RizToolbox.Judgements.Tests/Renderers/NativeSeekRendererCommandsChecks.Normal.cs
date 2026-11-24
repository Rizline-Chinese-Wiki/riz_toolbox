using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekRendererCommandsChecks
{
    private static void Known(SeekRendererEffectsJournal journal, int attempts)
    {
        Check(journal.Prepared && journal.ExecutionEntered && journal.ExecutionFinished && journal.AttemptedCommands == attempts
            && !journal.SideEffectsUnknown && !journal.HasRetainedResponsibility && journal.Failure is null
            && journal.Commands.Count == 5 && journal.Commands.All(row => row.ExpectedReachableStateObserved && !row.SideEffectsUnknown),
            "The returned journal lost exact completed command/effect evidence.");
        UnchangedNotes(journal);
    }
    private static void Trs(FakeNative.RendererTrs value, IntPtr parent, bool active)
        => Check(value.Parent == parent && value.Position == new NativeVector3(0, 0, 0)
            && value.Rotation == new NativeRendererQuaternion(0, 0, 0, 1)
            && value.Scale == new NativeVector3(active ? 1 : 0, active ? 1 : 0, active ? 1 : 0),
            "The exact pooled local Transform state differs from the native GetItem/ReturnItem branch.");
    private static void Preserve()
    {
        using var f = new Fixture(); var p = f.Prepare(); var prepared = p.GetJournal();
        Check(prepared.Prepared && !prepared.ExecutionEntered && prepared.Commands.Count == 0 && f.Native.RendererCalls.Count == 0
            && f.Native.RendererInstantiations == 0, "Prepare acquired or released engine pool state.");
        var result = p.Execute(); Known(result, 0);
        Check(result.Commands.All(row => !row.NativeEntered && !row.NativeReturned && ReferenceEquals(row.Before, row.After))
            && f.Native.RendererCalls.Count == 0 && f.Native.RendererGets.Count == 0 && f.Native.RendererReturns.Count == 0,
            "Preserve dispatched a native command or invented a native return.");
        Expect<InvalidOperationException>(() => p.Execute()); p.Dispose();
        var exports = f.Native.Invocations + f.Native.FieldReads + f.Native.FreeHandleAttempts; var after = p.GetJournal();
        Check(after.Disposed && after.Baseline == prepared.Baseline && after.Commands.Count == 5
            && exports == f.Native.Invocations + f.Native.FieldReads + f.Native.FreeHandleAttempts,
            "Logical disposal lost the immutable effects journal or made a native observation.");
        Check(p.GetType().GetInterfaces().All(type => type.Name != "IGameplayValueParticipant"), "Irreversible renderer commands were exposed as reversible value participants.");
    }
    private static void AppearFifo()
    {
        using var f = new Fixture(FakeNative.RendererShape.Inactive, 5, n =>
        {
            var list = n.RendererReference(n.RendererPools[0], "_childList");
            n.RendererSet(n.RendererList(list).Capacity[0], "parentPool", IntPtr.Zero);
        });
        var pool = f.Native.RendererPools[0]; var listRef = f.Native.RendererReference(pool, "_childList");
        var before = f.Native.RendererList(listRef); var fifo = before.Capacity.Take(before.Size).ToArray();
        var input = f.All(SeekRendererCommandKind.Appear).Reverse().ToArray(); var original = input.ToArray(); var p = f.Prepare(input);
        Array.Fill(input, new SeekRendererCommand(0, SeekRendererCommandKind.Preserve));
        var result = p.Execute(); Known(result, 5);
        Check(f.Native.RendererCalls.Select(row => row.Note).SequenceEqual(new[] { 4, 3, 2, 1, 0 })
            && result.Commands.Select(row => row.Command).SequenceEqual(original)
            && f.Native.RendererGets.Select(row => row.Child).SequenceEqual(fifo) && f.Native.RendererInstantiations == 0,
            "Explicit caller order was reconstructed, aliased input was reused, or shared-pool FIFO changed.");
        for (var index = 0; index < fifo.Length; ++index)
        {
            var plan = f.Native.RendererPlans[4 - index];
            Check(f.Native.RendererReference(plan, "poolChild") == fifo[index] && f.Native.RendererBoolean(plan, "active")
                && f.Native.RendererReference(fifo[index], "parentPool") == pool, "GetItem failed to publish its actual child/owner at the native tail.");
            Trs(f.Native.RendererTransform(fifo[index]), f.Native.RendererReference(plan, "poolChildTransformPlan"), true);
        }
        var after = f.Native.RendererList(listRef);
        Check(after.Backing == before.Backing && after.Size == 0 && after.Version == unchecked(int.MaxValue + 5)
            && after.Capacity.Take(5).All(value => value == IntPtr.Zero) && after.Capacity.Skip(5).SequenceEqual(before.Capacity.Skip(5)),
            "FIFO RemoveAt lost signed version wrap, the original backing or full inactive tail.");
    }
    private static void Mixed()
    {
        using var f = new Fixture(FakeNative.RendererShape.Mixed, 3);
        var old = new[] { 1, 3 }.ToDictionary(index => index, index => (Child: f.Native.RendererReference(f.Native.RendererPlans[index], "poolChild"),
            Trs: f.Native.RendererTransform(f.Native.RendererReference(f.Native.RendererPlans[index], "poolChild"))));
        var p = f.Prepare(f.All(SeekRendererCommandKind.Appear)); Known(p.Execute(), 5);
        Check(f.Native.RendererGets.Count == 3 && f.Native.RendererInstantiations == 0 && f.Native.RendererPlans.All(plan => f.Native.RendererBoolean(plan, "active")),
            "Mixed Appear acquired for an already active plan or missed an inactive plan.");
        foreach (var pair in old) Check(f.Native.RendererReference(f.Native.RendererPlans[pair.Key], "poolChild") == pair.Value.Child
            && f.Native.RendererTransform(pair.Value.Child) == pair.Value.Trs, "An active Appear no-op replaced its child or rewrote TRS.");
    }
    private static void CleanReturn()
    {
        using var f = new Fixture(FakeNative.RendererShape.Active, 0); var pool = f.Native.RendererPools[0];
        var children = f.Native.RendererPlans.Select(plan => f.Native.RendererReference(plan, "poolChild")).ToArray();
        var list = f.Native.RendererReference(pool, "_childList"); var p = f.Prepare(f.All(SeekRendererCommandKind.Disappear)); var result = p.Execute(); Known(result, 5);
        var after = f.Native.RendererList(list);
        Check(!result.DeferredEffectsPending && after.Size == 5 && after.Capacity.Count == 8 && after.Capacity.Take(5).SequenceEqual(children)
            && after.Capacity.Skip(5).All(value => value == IntPtr.Zero) && after.Version == unchecked(int.MaxValue + 5)
            && f.Native.RendererInteger(pool, "_childCount") == 5 && f.Native.RendererInteger(pool, "poolSize") == 5
            && f.Native.RendererBackingGrowths.Select(row => row.Capacity).SequenceEqual(new[] { 4, 8 }),
            "Clean ReturnItem lost exact List growth, FIFO append, signed version or pool counters.");
        for (var index = 0; index < 5; ++index)
        {
            Check(!f.Native.RendererBoolean(f.Native.RendererPlans[index], "active")
                && f.Native.RendererReference(f.Native.RendererPlans[index], "poolChild") == children[index], "Disappear nulled a retained renderer child or left it active.");
            Trs(f.Native.RendererTransform(children[index]), new IntPtr(result.Baseline!.Pools.Single().Transform), false);
        }
    }
    private static void DirtyReturn()
    {
        using var f = new Fixture(FakeNative.RendererShape.Dirty, 0); var pool = f.Native.RendererPools[0];
        var children = f.Native.RendererPlans.Select(plan => f.Native.RendererReference(plan, "poolChild")).ToArray();
        var trs = children.Select(f.Native.RendererTransform).ToArray(); var p = f.Prepare(f.All(SeekRendererCommandKind.Disappear));
        var result = p.Execute(); Known(result, 5); var dirty = f.Native.RendererList(f.Native.RendererReference(pool, "_dirtyChildList"));
        Check(result.DeferredEffectsPending && result.Commands.All(row => row.DeferredEffectsPending)
            && dirty.Size == 5 && dirty.Capacity.Count == 8 && dirty.Version == unchecked(int.MinValue + 5)
            && dirty.Capacity.Take(5).SequenceEqual(children) && dirty.Capacity.Skip(5).All(value => value == IntPtr.Zero)
            && f.Native.RendererInteger(pool, "_childCount") == 0 && f.Native.RendererInteger(pool, "poolSize") == 5
            && children.Select(f.Native.RendererTransform).SequenceEqual(trs),
            "Dirty return was counted or reset as a clean return instead of retaining deferred native effects.");
        p.Dispose(); Check(p.GetJournal().Disposed && p.GetJournal().DeferredEffectsPending && !p.GetJournal().SideEffectsUnknown
            && f.Native.RendererDestroyCalls == 0, "GC root cleanup claimed that queued engine return/reset had completed.");
    }
    private static void Growth()
    {
        using var f = new Fixture(FakeNative.RendererShape.Growth); var pool = f.Native.RendererPools[0];
        var initial = f.Native.RendererChildren.ToHashSet(); var p = f.Prepare(f.All(SeekRendererCommandKind.Appear)); var result = p.Execute(); Known(result, 5);
        var available = f.Native.RendererList(f.Native.RendererReference(pool, "_childList"));
        Check(result.Commands.All(row => row.GrowthObserved) && f.Native.RendererInstantiations == 5 && f.Native.RendererCreatedChildren.Count == 5
            && f.Native.RendererCreatedChildren.Distinct().Count() == 5 && f.Native.RendererCreatedChildren.All(child => !initial.Contains(child) && f.Native.HasRootFor(child))
            && f.Native.RendererGets.Select(row => row.Child).SequenceEqual(f.Native.RendererCreatedChildren)
            && f.Native.RendererInteger(pool, "poolSize") == 5 && f.Native.RendererInteger(pool, "_childCount") == 0
            && available.Size == 0 && available.Capacity.Count == 4 && available.Capacity.All(value => value == IntPtr.Zero)
            && available.Version == unchecked(int.MaxValue + 10), "Continuous zero-stock GetItem growth was predicted as existing inventory or lost fresh published identities.");
        p.Dispose(); Check(f.Native.RendererCreatedChildren.All(child => !f.Native.HasRootFor(child)) && f.Native.RendererDestroyCalls == 0,
            "Normal command disposal leaked its own growth roots or fabricated engine destruction.");
    }
    private static void ParentPool()
    {
        IntPtr returnPool = IntPtr.Zero; using var f = new Fixture(FakeNative.RendererShape.Active, 0, n =>
            returnPool = n.RendererMakeReturnPool(n.RendererReference(n.RendererPlans[0], "poolChild")));
        var configuredPool = f.Native.RendererPools[0]; var child = f.Native.RendererReference(f.Native.RendererPlans[0], "poolChild");
        var commands = f.All(SeekRendererCommandKind.Preserve); commands[0] = commands[0] with { Kind = SeekRendererCommandKind.Disappear };
        var p = f.Prepare(commands); Known(p.Execute(), 1);
        Check(f.Native.RendererReturns.Single() == (returnPool, child) && f.Native.RendererInteger(returnPool, "_childCount") == 1
            && f.Native.RendererInteger(configuredPool, "_childCount") == 0 && f.Native.RendererInteger(configuredPool, "poolSize") == 4,
            "Disappear returned through plan.pool rather than the actual child's parentPool closure.");
    }
}
