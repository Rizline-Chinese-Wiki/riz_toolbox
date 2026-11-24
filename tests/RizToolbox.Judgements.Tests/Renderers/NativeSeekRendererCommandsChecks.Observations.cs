using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekRendererCommandsChecks
{
    private static void Aliases()
    {
        using (var f = new Fixture(FakeNative.RendererShape.Inactive, 4, n => n.RendererAliasPlan(1, 0)))
        {
            var p = f.Prepare(f.All(SeekRendererCommandKind.Appear)); var result = p.Execute(); Known(result, 5);
            // Snapshot rows are keyed/sorted by native address, whose allocator
            // order is independent of the original actual-note array slots.
            var first = result.Baseline!.Notes.Single(note => note.Reference == f.Native.RendererNotes[0].ToInt64());
            var alias = result.Baseline.Notes.Single(note => note.Reference == f.Native.RendererNotes[1].ToInt64());
            var shared = f.Native.RendererPlans[0].ToInt64();
            Check(f.Native.RendererGets.Count == 4 && first.Plans.Single() == shared && alias.Plans.Single() == shared
                && f.Native.RendererInstantiations == 0, "Shared renderer identity was expanded into duplicate native acquisitions.");
        }
        using (var f = new Fixture(FakeNative.RendererShape.Active, 0, n => n.RendererAliasActiveChild(1, 0)))
        {
            Expect<InvalidDataException>(() => f.Prepare()); Check(f.Native.RendererCalls.Count == 0, "Contradictory active-child ownership entered native code.");
        }
        foreach (var backing in new[] { false, true })
        {
            using var f = new Fixture(FakeNative.RendererShape.Inactive, 2, n =>
            {
                var pool = n.RendererPools[0]; var available = n.RendererReference(pool, "_childList");
                if (backing) n.RendererSet(n.RendererReference(pool, "_dirtyChildList"), "_items", n.RendererReference(available, "_items"));
                else { n.RendererSet(pool, "_dirtyChildList", available); n.RendererSet(pool, "poolSize", 4); }
            });
            Expect<NotSupportedException>(() => f.Prepare()); Check(f.Native.RendererCalls.Count == 0, "Unsupported pool List/storage aliases dispatched native commands.");
        }
        using (var f = new Fixture(FakeNative.RendererShape.Growth, 0, n =>
        {
            var pool = n.RendererPools[0]; var available = n.RendererReference(pool, "_childList");
            n.RendererSet(n.RendererReference(pool, "_dirtyChildList"), "_items", n.RendererReference(available, "_items"));
        }))
        {
            var p = f.Prepare(f.All(SeekRendererCommandKind.Appear)); Known(p.Execute(), 5);
            Check(f.Native.RendererInstantiations == 5, "A harmless shared empty backing was rejected or used as positive inventory.");
        }
    }
    private static void FullArrays()
    {
        using var f = new Fixture(FakeNative.RendererShape.Inactive, 1, n =>
        {
            var list = n.RendererReference(n.RendererPools[0], "_childList"); n.RendererReplaceBacking(list, 5001);
            var array = n.RendererReference(list, "_items"); var child = n.RendererList(list).Capacity[0];
            n.RendererSetSlot(array, 4097, child); n.RendererSetSlot(array, 4999, child); n.RendererSetSlot(array, 5000, IntPtr.Zero);
        });
        var raw = f.Native.RendererList(f.Native.RendererReference(f.Native.RendererPools[0], "_childList")); var p = f.Prepare();
        var baseline = p.GetJournal().Baseline!; var stored = baseline.Pools.Single().Available;
        Check(stored.CapacitySlots.Count == 5001 && stored.CapacitySlots.SequenceEqual(raw.Capacity.Select(value => value.ToInt64()))
            && stored.Size == 1 && stored.Version == int.MaxValue && baseline.Pools.Single().Dirty.Version == int.MinValue
            && stored.CapacitySlots[4097] == stored.CapacitySlots[4999] && stored.CapacitySlots[5000] == 0,
            "The new renderer reference-array bridge truncated at 4096 or discarded complete duplicate/null tail storage.");
        Known(p.Execute(), 0);
        // Returned journal rows remain immutable managed samples when native
        // tail storage changes after successful execution.
        f.Native.RendererSetSlot(raw.Backing, 4999, IntPtr.Zero);
        Check(stored.CapacitySlots[4999] == raw.Capacity[4999].ToInt64() && p.GetJournal().Baseline!.Pools.Single().Available.CapacitySlots[4999] != 0,
            "A journal exposed a mutable native-array view after the observation completed.");
    }
    private static void Rejections()
    {
        for (var mode = 0; mode < 5; ++mode)
        {
            using var f = new Fixture(); var all = f.All(SeekRendererCommandKind.Preserve);
            IReadOnlyList<SeekRendererCommand> invalid = mode switch
            {
                0 => all.Take(4).ToArray(), 1 => all.Append(all[0]).ToArray(),
                2 => all.Select((command, index) => index == 4 ? all[0] : command).ToArray(),
                3 => all.Select((command, index) => index == 4 ? command with { Reference = 0 } : command).ToArray(),
                _ => all.Select((command, index) => index == 4 ? command with { Kind = (SeekRendererCommandKind)99 } : command).ToArray()
            };
            Expect<InvalidDataException>(() => f.Prepare(invalid));
            Check(f.Native.RendererCalls.Count == 0 && f.Native.RendererInstantiations == 0, "An incomplete or malformed explicit command sequence acquired pool effects.");
        }
        foreach (var fault in Enum.GetValues<FakeNative.RendererMetadataFault>())
        {
            using var f = new Fixture(configure: n => n.RendererChangeMetadata(fault));
            Expect<UnsupportedSignatureException>(() => f.Prepare());
            Check(f.Native.RendererCalls.Count == 0 && f.Native.RendererInstantiations == 0, "Unsupported exact List/array metadata reached renderer dispatch.");
        }
    }
    private static void ForeignGrowth()
    {
        foreach (var reuse in new[] { true, false })
        {
            using var f = new Fixture(FakeNative.RendererShape.Growth); var p = f.Prepare(f.All(SeekRendererCommandKind.Appear));
            if (reuse) f.Native.RendererReusePrefabOnGrowth = true;
            else f.Native.AfterRendererCommand = _ => f.Native.RendererReplaceBacking(f.Native.RendererReference(f.Native.RendererPools[0], "_childList"), 7);
            var failure = Expect<InvalidOperationException>(() => p.Execute()); f.ClearCallbacks();
            var journal = p.GetJournal();
            Check(p.AttemptedCommands == 1 && f.Native.RendererCalls.Count == 1 && journal.Commands.Single().NativeEntered
                && !journal.Commands.Single().ExpectedReachableStateObserved && journal.Commands.Single().SideEffectsUnknown,
                "A known-child substitution or non-doubling growth array was accepted as attributable fresh native growth.");
            RetainEffects(f, p, failure);
        }
    }
}
