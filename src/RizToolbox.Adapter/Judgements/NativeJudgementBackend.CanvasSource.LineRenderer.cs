using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class CanvasSource
        {
            internal sealed partial class SharedBasisSource
            {
                // These are the original two child accesses, not new captures
                // or owners. A line consumer may inspect them inside the one
                // already-held shared operation without reentering its gate.
                private INativeSeekCanvasCacheAccess? _lineCacheAccess;
                private INativeSeekCanvasPositionAccess? _linePositionAccess;

                internal void VerifyAppliedForLineInOperation(NativeSeekCanvasTargetPlan plan)
                {
                    VerifySourceUse(); VerifySourceInOperation();
                    var capture = _capture ?? throw new InvalidOperationException("The original Canvas capture is absent.");
                    var cache = _lineCacheAccess ?? throw new InvalidOperationException("The original Canvas cache access is absent.");
                    var positions = _linePositionAccess ?? throw new InvalidOperationException("The original Canvas position access is absent.");
                    if (!ReferenceEquals(capture.Plan, plan) || !ReferenceEquals(cache.SourceCapture, capture)
                        || !ReferenceEquals(positions.SourceCapture, capture))
                        throw new InvalidOperationException("The line consumer attempted to substitute its Canvas source or Plan.");
                    for (var pass = 0; pass < 2; ++pass)
                    {
                        var actual = cache.ReadSnapshot();
                        var slots = cache.Slots;
                        if (actual.Count != slots.Count || slots.Count != checked(plan.Rows.Count * 3 + 2))
                            throw new InvalidOperationException("The complete original Canvas cache view changed length.");
                        for (var i = 0; i < slots.Count; ++i)
                            if (actual[i] != slots[i].TargetBits)
                                throw new InvalidOperationException("The original Canvas cache no longer has its applied target bits.");
                        var local = positions.ReadSnapshot();
                        if (local.Count != plan.Rows.Count)
                            throw new InvalidOperationException("The complete original Canvas position view changed length.");
                        for (var i = 0; i < local.Count; ++i)
                        {
                            var target = plan.Rows[i].LocalPosition;
                            if (BitConverter.SingleToInt32Bits(local[i].X) != BitConverter.SingleToInt32Bits(target.X)
                                || BitConverter.SingleToInt32Bits(local[i].Y) != BitConverter.SingleToInt32Bits(target.Y)
                                || BitConverter.SingleToInt32Bits(local[i].Z) != BitConverter.SingleToInt32Bits(target.Z))
                                throw new InvalidOperationException("The original Canvas Transform no longer has its applied local target.");
                        }
                        VerifySourceUse(); VerifySourceInOperation();
                    }
                }
            }
        }
    }
}
