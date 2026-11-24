using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekLineEndpointCommands
{
    private void StoreProducerWorld(IRetainedSeekLineRendererAccess access,
        SeekLineRingDynamicOccurrence row, NativeLineProducerField field, NativeVector3 value)
    {
        var bits = new NativeVector3BufferBits(BitConverter.SingleToInt32Bits(value.X),
            BitConverter.SingleToInt32Bits(value.Y), BitConverter.SingleToInt32Bits(value.Z));
        StoreProducerControl(access, row.Ordinal, row.Line, new(field, 0, 2, bits), restore: false);
        StoreProducerControl(access, row.Ordinal, row.Line, new(field, 2, 1, bits), restore: false);
    }

    private void StoreProducerControl(IRetainedSeekLineRendererAccess access, int occurrence, long line,
        NativeLineProducerPatch patch, bool restore)
    {
        ObserveRendererExpected(access);
        var before = _rendererExpectedControl ?? throw new InvalidOperationException("The original producer baseline is absent.");
        var target = NativeRuntime.PatchLineProducerControl(before, patch);
        var index = AddRendererStep(restore ? SeekLineRendererCommandKind.RestoreProducerControl
            : SeekLineRendererCommandKind.ProducerControl, occurrence, line);
        _rendererCommands[index] = _rendererCommands[index] with
        { BeforeControl = before, TargetControl = target, ControlPatch = patch };
        Exception? failure = null;
        try
        {
            // The complete original producer/saved/array storage is checked at
            // every word admission. Canvas/world/scene inputs additionally
            // bracket each occurrence and the final native consumer call.
            var result = access.WriteProducerControl(before, patch,
                () => ObserveRendererExpected(access), () => AdmitRendererStep(index));
            if (result.StoreEntered) ConfirmRendererEntry(index, returned: true);
            if (result.Before != before || result.After != target || result.Patch != patch)
                throw new InvalidOperationException("The selected producer word did not return its complete commanded control state.");
        }
        catch (Exception error)
        {
            failure = error; AdoptCleanup(error);
            if (error.Data["LineProducerControlStoreEntered"] is true) ConfirmRendererEntry(index, returned: false);
        }
        FinishRendererStep(access, index, failure);
    }

    private void StoreProducerSlice(IRetainedSeekLineRendererAccess access, int occurrence, long line,
        NativeVector3BufferSlice slice, bool restore)
    {
        ObserveRendererExpected(access);
        var before = _rendererExpectedScratch ?? throw new InvalidOperationException("The original producer array is absent.");
        var target = NativeRuntime.PatchLineVector3BufferSlice(before, slice);
        var index = AddRendererStep(restore ? SeekLineRendererCommandKind.RestoreArraySlice
            : SeekLineRendererCommandKind.ArraySlice, occurrence, line);
        _rendererCommands[index] = _rendererCommands[index] with
        { ArraySlice = slice, BeforePoint = before.Items[slice.Index], TargetPoint = target.Items[slice.Index] };
        Exception? failure = null;
        try
        {
            var result = access.WriteProducerArraySlice(before, slice,
                () => ObserveRendererExpected(access), () => AdmitRendererStep(index));
            if (result.StoreEntered) ConfirmRendererEntry(index, returned: true);
            if (!SameBuffer(result.Before, before) || !SameBuffer(result.After, target) || result.Slice != slice)
                throw new InvalidOperationException("The selected producer array word altered another element or tail.");
        }
        catch (Exception error)
        {
            failure = error; AdoptCleanup(error);
            if (error.Data["LineVector3SliceStoreEntered"] is true) ConfirmRendererEntry(index, returned: false);
        }
        FinishRendererStep(access, index, failure);
    }
}
