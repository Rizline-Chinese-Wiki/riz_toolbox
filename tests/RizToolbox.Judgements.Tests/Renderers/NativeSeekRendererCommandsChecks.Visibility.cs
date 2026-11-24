using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekRendererCommandsChecks
{
    private static void VisibilityBatch()
    {
        using var f = new Fixture(FakeNative.RendererShape.Inactive, 5, native =>
        {
            for (var index = 0; index < native.FlagNotes.Count; ++index)
            { native.FlagForeignValue(index, 0, false); native.FlagForeignValue(index, 1, false); }
        });
        var commands = f.Prepare(f.All(SeekRendererCommandKind.Appear));
        var before = commands.GetJournal().Baseline!;
        commands.Execute();
        var actualPools = (SeekRendererGraphSnapshot)Field(commands, "_current");
        // Actual native renderer dispatch supplies the complete pool/TRS result.
        // These explicit flags isolate the new batch recognizer; this is not an
        // execution of the pinned game ActiveNotes/SetNotes bodies.
        var after = actualPools with { Notes = actualPools.Notes.Select(note => note with { IsActive = true }).ToArray() };
        var order = f.Native.RendererNotes.Select(note => note.ToInt64()).ToArray();
        var effect = SeekRendererState.RecognizeVisibility(before, after, order, new HashSet<long>());
        Check(effect.Recognized && !effect.Growth && !effect.Deferred, "The batch lost actual shared-pool FIFO effects.");
        Check(!SeekRendererState.RecognizeVisibility(before, after, order.Reverse().ToArray(), new HashSet<long>()).Recognized,
            "An invented activation order substituted another child assignment.");
        var foreign = after with { Notes = after.Notes.Select((note, index) => index == 0 ? note with { Missed = !note.Missed } : note).ToArray() };
        Check(!SeekRendererState.RecognizeVisibility(before, foreign, order, new HashSet<long>()).Recognized,
            "An unrelated missed-flag write was attributed to ActiveNotes.");
        Check(!SeekRendererState.RecognizeVisibility(before, after, order.Take(4).ToArray(), new HashSet<long>()).Recognized,
            "Partial membership was accepted as the actual complete scan.");
    }
}
