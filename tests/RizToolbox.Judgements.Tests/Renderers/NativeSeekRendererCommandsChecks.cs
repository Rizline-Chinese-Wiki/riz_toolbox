using System.Diagnostics;
using System.Reflection;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekRendererCommandsChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Renderer commands preserve complete actual state and journal without dispatch", Preserve),
        ("Renderer commands Appear preserves caller order and shared-pool FIFO identity", AppearFifo),
        ("Renderer commands active no-ops and mixed plans preserve note flags and indices", Mixed),
        ("Renderer commands clean Disappear returns FIFO members and zeroes pooled TRS", CleanReturn),
        ("Renderer commands dirty Disappear records deferred return without Reset", DirtyReturn),
        ("Renderer commands zero-inventory continuous growth roots fresh visible children", Growth),
        ("Renderer commands Disappear closes and uses the actual child parentPool", ParentPool),
        ("Renderer commands preserve shared plans and reject contradictory child or list aliases", Aliases),
        ("Renderer commands read complete large arrays tails nulls duplicates and signed versions", FullArrays),
        ("Renderer commands reject exact metadata and malformed complete command coverage", Rejections),
        ("Renderer commands retain native before after and internal-partial failures without retry", NativeFailures),
        ("Renderer commands hidden growth remains unknown even when visible state is unchanged", HiddenGrowth),
        ("Renderer commands reject known-child substitution and unrecognized growth storage", ForeignGrowth),
        ("Renderer commands final managed guard and ready-paused source precede dispatch", GuardAndSource),
        ("Renderer commands reject constructor and execution reentry while journals remain readable", Reentry),
        ("Renderer commands callback disposal closes reads and retains already entered effects", CallbackDispose),
        ("Renderer commands worker rejection performs zero exports and keeps journal reads managed", WrongThread),
        ("Renderer commands unknown accessor free retains its exact wrapper and never retries", UnknownFree),
        ("Visibility batch recognizes actual ordered shared-pool results and rejects foreign flags", VisibilityBatch),
        ("Renderer growth inverse restores graph then destroys only new owned GameObjects", GrowthInverse),
        ("Renderer growth inverse rejects foreign GameObject association before deletion", GrowthInverseForeign),
        ("Renderer growth inverse retains partial native destruction without fake restoration or retry", GrowthInversePartial),
        ("Renderer inverse restores same-call retired backing and rejects later foreign slots", RetiredBackingInverse)
    };
    private sealed record Responsibility(FakeNative Native, NativeSeekRendererCommands Commands, Exception Failure);
    private static readonly List<Responsibility> UnknownOwners = new();
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static T Expect<T>(Action operation) where T : Exception
    { try { operation(); } catch (T error) { return error; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static object Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static object[] Items(object value) => ((System.Collections.IEnumerable)value).Cast<object?>().Where(item => item is not null).Select(item => item!).ToArray();
    private static NativePublicationCall[] PublicationOwners() => Items(typeof(NativePublicationCall).GetField("ProcessOwners", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Cast<NativePublicationCall>().ToArray();
    private static bool InMethod(string method, string? type = null) => new StackTrace(false).GetFrames().Any(frame => frame.GetMethod() is { } member
        && member.Name == method && (type is null || member.DeclaringType?.Name == type));
    private sealed record Publications(int Calls, int Roots, int Arguments, int Unconfirmed)
    { internal static Publications Read() => new(NativePublicationCall.RetainedCallCount, NativePublicationCall.RetainedRootRecordCount,
        NativePublicationCall.RetainedArgumentRecordCount, NativePublicationCall.UnconfirmedPublicationCount); }
    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new(); internal readonly GameEventState State = new();
        internal readonly NativeJudgementBackend Backend; internal readonly JudgementRunCapture Run;
        private readonly List<NativeSeekRendererCommands> _commands = new();
        private readonly HashSet<NativeSeekRendererCommands> _retained = new();
        private Publications _publications = Publications.Read();
        internal Fixture(FakeNative.RendererShape shape = FakeNative.RendererShape.Mixed, int available = 2, Action<FakeNative>? configure = null)
        {
            Native.ConfigureRenderers(shape, available); configure?.Invoke(Native); Native.RendererRememberOtherStorage();
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-renderer-commands", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State); Run = Backend.CaptureRun() ?? throw new InvalidOperationException("No actual RunValue.");
            Check(Run.GetType().Name == "RunValue" && Run.Graph.Count == 5, "Renderer tests must execute the actual retained backend RunValue.");
        }
        internal SeekRendererCommand[] All(SeekRendererCommandKind kind) => Native.RendererNotes.Select(note => new SeekRendererCommand(note.ToInt64(), kind)).ToArray();
        internal NativeSeekRendererCommands Prepare(IReadOnlyList<SeekRendererCommand>? commands = null)
        { var value = Run.PrepareSeekRendererCommands(Native.RendererProfile, commands ?? All(SeekRendererCommandKind.Preserve)); _commands.Add(value); return value; }
        internal void ClearCallbacks()
        {
            Native.BeforeRendererCommand = null; Native.AfterRendererCommand = null; Native.AfterRendererStep = null;
            Native.AfterRendererRoot = null; Native.AfterRendererGetter = null; Native.AfterRendererArrayExport = null;
            Native.AfterNoteViewFieldRead = null; Native.AfterNoteViewObjectClass = null; Native.AfterNoteViewInvocation = null;
            Native.BeforeExport = null; Native.DuringHandleTarget = null; Native.RendererProfile.OnVerify = null;
            Native.BeforeRendererDestroy = null;
        }
        internal void Retain(NativeSeekRendererCommands value, Exception failure)
        { _retained.Add(value); UnknownOwners.Add(new(Native, value, failure)); }
        internal void RetainPublication(NativePublicationCall call)
        {
            Check(Publications.Read() == (_publications with { Calls = _publications.Calls + 1, Roots = _publications.Roots + 1 }),
                "Unknown command temporary-root cleanup retained the wrong bounded publication shape.");
            _publications = Publications.Read(); var frees = Native.FreeHandleAttempts; Expect<InvalidOperationException>(call.Dispose);
            Check(Native.FreeHandleAttempts == frees, "Unknown command publication cleanup retried its native free.");
        }
        public void Dispose()
        {
            ClearCallbacks();
            foreach (var value in _commands.AsEnumerable().Reverse())
                if (_retained.Contains(value))
                {
                    var frees = Native.FreeHandleAttempts;
                    try { value.Dispose(); } catch (Exception error) { Check(ReferenceEquals(error.Data["SeekRendererEffectsOwner"], value), "Retained renderer cleanup lost its exact effects owner."); }
                    Check(Native.FreeHandleAttempts == frees && value.HasRetainedResponsibility, "Retained renderer ownership retried cleanup or was discarded.");
                }
                else value.Dispose();
            Run.Dispose(); Backend.Dispose();
            Check(Native.NewRoots - Native.FreedRoots == Native.ActiveRoots && Native.FreeHandleAttempts == Native.NoteViewFreeLog.Count
                && Native.NoteViewFreeLog.Select(row => row.Handle).Distinct().Count() == Native.FreeHandleAttempts,
                "A renderer native root was lost or freed twice.");
            Check(_retained.Count != 0 || Native.ActiveRoots == 0, "Known renderer roots escaped cleanup.");
            Check(Native.RendererOtherStorageUnchanged() && Native.FlagWrites.Count == 0 && Native.CursorWriteLog.Count == 0
                && Native.JudgeQueueWrites.Count == 0 && Native.FingerWrites.Count == 0 && Native.FieldWrites == 0
                && Native.NoteViewDispatchViolations.Count == 0 && Native.RendererReceiversRooted && Native.RendererPublishedBeforeInspection
                && (Native.RendererInverseEnabled || Native.RendererDestroyCalls == 0) && Native.GameplayCommandCalls == 0 && Native.SeekConversions == 0,
                "Renderer commands changed unrelated state, inspected an unrooted publication or fabricated field/engine cleanup.");
            Check(Publications.Read() == _publications, "Renderer commands lost or leaked an unaccounted publication owner.");
            if (_retained.Count == 0) Native.Dispose();
        }
    }
    private static void RetainEffects(Fixture f, NativeSeekRendererCommands value, Exception failure)
    {
        Check(value.SideEffectsUnknown && value.HasRetainedResponsibility && ReferenceEquals(failure.Data["SeekRendererEffectsOwner"], value),
            "An entered unknown native effect lost its independent owner.");
        var error = Expect<InvalidOperationException>(value.Dispose); f.Retain(value, error);
        Check(ReferenceEquals(error.Data["SeekRendererEffectsOwner"], value) && f.Native.ActiveRoots > 0 && value.GetJournal().Disposed,
            "Unknown native effects were silently undone, physically released or no longer inspectable.");
    }
    private static void UnchangedNotes(SeekRendererEffectsJournal journal)
    {
        var baseline = journal.Baseline!.Notes.ToDictionary(note => note.Reference);
        foreach (var effect in journal.Commands)
        foreach (var note in effect.After?.Notes ?? Array.Empty<SeekRendererNoteState>())
            Check(note.Index == baseline[note.Reference].Index && note.IsActive == baseline[note.Reference].IsActive
                && note.Destroyed == baseline[note.Reference].Destroyed && note.Missed == baseline[note.Reference].Missed,
                "Appear/Disappear was conflated with note flags or SendRendererEvent's index.");
    }
}
