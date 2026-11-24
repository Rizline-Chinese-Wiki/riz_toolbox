using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal readonly List<(IntPtr Before, IntPtr After, int Capacity)> RendererBackingGrowths = new();
    private void RendererListAdd(IntPtr list, IntPtr item)
    {
        var state = RendererList(list); var backing = state.Backing;
        if (state.Size == state.Capacity.Count)
        {
            var capacity = state.Capacity.Count == 0 ? 4 : checked(state.Capacity.Count * 2);
            var values = new IntPtr[capacity]; Array.Copy(state.Capacity.ToArray(), values, state.Size);
            backing = RendererArray(_rendererChildArrayType, values); RendererSet(list, "_items", backing);
            RendererBackingGrowths.Add((state.Backing, backing, capacity));
        }
        RendererSetSlot(backing, state.Size, item); RendererSet(list, "_size", state.Size + 1);
        RendererSet(list, "_version", unchecked(state.Version + 1));
    }
    private IntPtr RendererRemoveFirst(IntPtr list)
    {
        var state = RendererList(list); if (state.Size == 0) throw new InvalidOperationException("Synthetic empty FIFO.");
        var result = state.Capacity[0];
        for (var slot = 0; slot + 1 < state.Size; ++slot) RendererSetSlot(state.Backing, slot, state.Capacity[slot + 1]);
        RendererSetSlot(state.Backing, state.Size - 1, IntPtr.Zero); RendererSet(list, "_size", state.Size - 1);
        RendererSet(list, "_version", unchecked(state.Version + 1)); return result;
    }
    private void RendererStep(RendererNativeCall call, int slot, string stage)
    { ++call.Steps; AfterRendererStep?.Invoke(call, slot, stage); }
    private void RendererExecuteNative(IntPtr note, bool appear)
    {
        var ordinal = RendererNotes.IndexOf(note); if (ordinal < 0) throw new InvalidOperationException("Renderer native command used another actual note.");
        var call = new RendererNativeCall(RendererCalls.Count + 1, ordinal, appear ? "Appear" : "Disappear");
        RendererCalls.Add(call); BeforeRendererCommand?.Invoke(call);
        var plans = RendererSlots(RendererReference(note, "noteRenderers"));
        for (var slot = 0; slot < plans.Length; ++slot)
        {
            var plan = plans[slot]; var active = RendererBoolean(plan, "active");
            if (appear)
            {
                if (active) continue;
                var pool = RendererReference(plan, "pool"); var list = RendererReference(pool, "_childList");
                if (RendererInteger(pool, "_childCount") <= 0)
                {
                    ++RendererInstantiations;
                    var created = RendererReusePrefabOnGrowth ? RendererReference(pool, "easyPoolChild") : RendererNewChild(pool);
                    RendererCreatedChildren.Add(created); RendererStep(call, slot, "InstantiatedHidden");
                    if (RendererFailHiddenGrowth) throw new IOException("Synthetic Instantiate effect became unknown before reference publication.");
                    RendererListAdd(list, created); RendererSet(pool, "poolSize", RendererInteger(pool, "poolSize") + 1);
                    RendererSet(pool, "_childCount", RendererInteger(pool, "_childCount") + 1); RendererStep(call, slot, "GrowthPublished");
                }
                var child = RendererRemoveFirst(list); RendererSet(pool, "_childCount", RendererInteger(pool, "_childCount") - 1);
                var target = RendererReference(plan, "poolChildTransformPlan"); var transform = _rendererTransforms[child];
                _rendererTrs[transform] = new(target, new(0, 0, 0), new(0, 0, 0, 1), new(1, 1, 1));
                RendererSet(child, "parentPool", pool); RendererGets.Add((pool, child)); RendererStep(call, slot, "GetItemTaken");
                RendererSet(plan, "poolChild", child); RendererStep(call, slot, "RendererChildStored");
                RendererSet(plan, "active", true); RendererStep(call, slot, "RendererActivated");
            }
            else
            {
                if (!active) continue;
                var child = RendererReference(plan, "poolChild"); var pool = RendererReference(child, "parentPool");
                if (RendererBoolean(child, "dirty"))
                {
                    RendererListAdd(RendererReference(pool, "_dirtyChildList"), child); RendererStep(call, slot, "DirtyQueued");
                }
                else
                {
                    _rendererTrs[_rendererTransforms[child]] = new(_rendererTransforms[pool], new(0, 0, 0), new(0, 0, 0, 1), new(0, 0, 0));
                    RendererListAdd(RendererReference(pool, "_childList"), child); RendererSet(pool, "_childCount", RendererInteger(pool, "_childCount") + 1);
                    RendererStep(call, slot, "CleanReturned");
                }
                RendererReturns.Add((pool, child)); RendererSet(plan, "active", false); RendererStep(call, slot, "RendererDeactivated");
            }
        }
        AfterRendererCommand?.Invoke(call); call.Returned = true;
    }
    internal void RendererSetTrs(IntPtr transform, RendererTrs value) => _rendererTrs[transform] = value;
    internal void RendererSetPlans(int note, params IntPtr[] plans)
        => RendererSet(RendererNotes[note], "noteRenderers", RendererArray(_rendererArrayType, plans));
    internal void RendererAliasPlan(int note, int owner) => RendererSetPlans(note, RendererPlans[owner]);
    internal void RendererAliasActiveChild(int note, int owner)
        => RendererSet(RendererPlans[note], "poolChild", RendererReference(RendererPlans[owner], "poolChild"));
    internal IntPtr RendererMakeReturnPool(IntPtr child)
    {
        var originalPool = RendererReference(child, "parentPool");
        RendererSet(originalPool, "poolSize", RendererInteger(originalPool, "poolSize") - 1);
        var pool = SeekObject(_rendererPoolType); RendererPools.Add(pool); _rendererTransforms[pool] = RendererNewTransform(IntPtr.Zero, false);
        var prefab = RendererNewChild(pool, prefab: true);
        RendererField("renderer.extra.prefab", _rendererPoolType, "easyPoolChild", _rendererChildType, pool, prefab, 0x20);
        RendererField("renderer.extra.size", _rendererPoolType, "poolSize", Int32, pool, 1, 0x28);
        RendererField("renderer.extra.count", _rendererPoolType, "_childCount", Int32, pool, 0, 0x2c);
        RendererField("renderer.extra.available", _rendererPoolType, "_childList", _rendererListType, pool, RendererNewList(0, -7, Array.Empty<IntPtr>()), 0x30);
        RendererField("renderer.extra.dirty", _rendererPoolType, "_dirtyChildList", _rendererListType, pool, RendererNewList(0, 13, Array.Empty<IntPtr>()), 0x38);
        RendererSet(child, "parentPool", pool); return pool;
    }
}
