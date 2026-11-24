using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekRendererCommandsChecks
{
    private static void WithInverseFixture(Fixture fixture, Action<Fixture> check)
    {
        try { check(fixture); }
        catch (Exception original)
        {
            try { fixture.Dispose(); }
            catch (Exception cleanup)
            {
                throw new AggregateException("The renderer inverse failed before fixture cleanup; original failure is first.", original, cleanup);
            }
            throw;
        }
        fixture.Dispose();
    }
    private static void GrowthInverse()
    {
        WithInverseFixture(new Fixture(FakeNative.RendererShape.Inactive, 1, native => native.ConfigureRendererInverse()), f =>
        {
        var oldChildren = f.Native.RendererChildren.ToArray();
        var oldObjects = oldChildren.Select(f.Native.RendererGameObject).ToHashSet();
        var p = f.Prepare(f.All(SeekRendererCommandKind.Appear)); p.RequireRestorable();
        var baseline = p.GetJournal().Baseline!; var pool = baseline.Pools.Single();
        p.Execute();
        Check(f.Native.RendererCreatedChildren.Count == 4, "The inverse case did not exercise actual mixed original and new child allocation.");
        f.Native.BeforeRendererDestroy = gameObject =>
        {
            Check(!oldObjects.Contains(gameObject), "An original child or prefab GameObject was selected for destruction.");
            Check(f.Native.RendererInteger(new(pool.Reference), "poolSize") == pool.PoolSize
                && f.Native.RendererInteger(new(pool.Reference), "_childCount") == pool.ChildCount,
                "Native destruction preceded restoration of the original pool counters.");
            var list = f.Native.RendererList(new(pool.Available.Reference));
            Check(list.Backing.ToInt64() == pool.Available.Backing && list.Size == pool.Available.Size
                && list.Version == pool.Available.Version && list.Capacity.Select(value => value.ToInt64()).SequenceEqual(pool.Available.CapacitySlots),
                "Native destruction preceded restoration of complete original pool List storage.");
            Check(baseline.Plans.All(plan => f.Native.RendererReference(new(plan.Reference), "poolChild").ToInt64() == plan.Child
                && f.Native.RendererBoolean(new(plan.Reference), "active") == plan.Active),
                "Native destruction preceded detachment of the new child from original renderer plans.");
        };
        p.Restore();
        Check(p.VerifyRestoredValues() && !p.SideEffectsUnknown && !p.HasRetainedResponsibility,
            "Actual growth inverse failed to confirm the original graph and completed child cleanup.");
        Check(f.Native.RendererDestructions.Count == 4 && f.Native.RendererDestructions.All(row => row.Method == "DestroyImmediate" && !row.AllowAssets)
            && f.Native.RendererDestructions.Select(row => row.Target).ToHashSet()
                .SetEquals(f.Native.RendererCreatedChildren.Select(f.Native.RendererGameObject)),
            "The inverse did not synchronously destroy each exact new GameObject once with assets excluded.");
        foreach (var child in f.Native.RendererCreatedChildren)
        {
            var objects = new[] { child, f.Native.RendererGameObject(child), f.Native.RendererChildTransform(child),
                f.Native.RendererReference(child, "targetComponent") };
            Check(objects.All(f.Native.RendererIsDead) && objects.All(f.Native.RendererDeathReads.Contains),
                "The inverse substituted returned for independent child/GameObject/component/Transform death observations.");
        }
        Check(oldChildren.All(child => !f.Native.RendererIsDead(child) && !f.Native.RendererIsDead(f.Native.RendererGameObject(child))),
            "An original child or prefab was destroyed.");
        p.Dispose(); Check(!p.HasRetainedResponsibility, "Completed native inverse kept an unresolved allocation owner.");
        });
    }
    private static void GrowthInverseForeign()
    {
        foreach (var descendant in new[] { false, true })
        {
        WithInverseFixture(new Fixture(FakeNative.RendererShape.Growth, 0, native => native.ConfigureRendererInverse()), f =>
        {
        var p = f.Prepare(f.All(SeekRendererCommandKind.Preserve).Select((command, index) =>
            index == 0 ? command with { Kind = SeekRendererCommandKind.Appear } : command).ToArray());
        p.RequireRestorable(); p.Execute();
        var created = f.Native.RendererCreatedChildren.Single(); var prefab = f.Native.RendererChildren[0];
        // This mutation occurs after the original creation snapshot and its
        // recognition. A later scan may reject it, never acquire its ownership.
        var foreign = descendant ? f.Native.RendererAttachForeignDescendant(created) : f.Native.RendererGameObject(prefab);
        if (!descendant) f.Native.RendererAliasGameObject(created, prefab);
        var error = Expect<InvalidOperationException>(p.Restore);
        Check(f.Native.RendererDestroyCalls == 0 && !f.Native.RendererIsDead(prefab)
            && !f.Native.RendererIsDead(f.Native.RendererGameObject(prefab)) && !f.Native.RendererIsDead(foreign),
            "A foreign shared GameObject or descendant moved into the frozen tree was deleted.");
        RetainEffects(f, p, error);
        });
        }
    }
    private static void GrowthInversePartial()
    {
        WithInverseFixture(new Fixture(FakeNative.RendererShape.Growth, 0, native => native.ConfigureRendererInverse()), f =>
        {
        var p = f.Prepare(f.All(SeekRendererCommandKind.Preserve).Select((command, index) =>
            index == 0 ? command with { Kind = SeekRendererCommandKind.Appear } : command).ToArray());
        p.RequireRestorable(); p.Execute(); f.Native.RendererPartialDestroy = true;
        var created = f.Native.RendererCreatedChildren.Single();
        var error = Expect<InvalidOperationException>(p.Restore);
        Check(f.Native.RendererDestroyCalls == 1 && f.Native.RendererIsDead(f.Native.RendererGameObject(created))
            && !f.Native.RendererIsDead(created) && !f.Native.RendererIsDead(f.Native.RendererChildTransform(created)),
            "The failure case did not expose an actual partially destroyed object graph.");
        var restored = false;
        try { restored = p.VerifyRestoredValues(); } catch (InvalidOperationException) { }
        Check(!restored && p.SideEffectsUnknown && p.HasRetainedResponsibility, "Partial native death was reported as restored.");
        RetainEffects(f, p, error);
        Check(f.Native.RendererDestroyCalls == 1, "Unknown native destruction was retried by readback or cleanup.");
        });
    }
}
