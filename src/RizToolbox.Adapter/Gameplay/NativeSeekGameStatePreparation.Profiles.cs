using System.Runtime.CompilerServices;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekGameStatePreparation
{
    private static readonly ConditionalWeakTable<GameplayClockNativeProfile, Profiles> ProfileOwners = new();
    [ThreadStatic] private static bool _preparingProfiles;

    // A probe validates fixed native dependencies without traversing chart notes.
    // The exact retained run, target and mutable graph are checked by Prepare.
    internal static void VerifyDependencies(NativeRuntime api, GameplayClockNativeProfile clockProfile,
        JudgementModule.SeekBackendAccess judgement)
        => GetProfiles(api, clockProfile, judgement).Verify();

    private static Profiles GetProfiles(NativeRuntime api, GameplayClockNativeProfile clockProfile,
        JudgementModule.SeekBackendAccess judgement)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(clockProfile);
        ArgumentNullException.ThrowIfNull(judgement); judgement.VerifyCurrent();
        var start = judgement.StartCoroutineBinding; var move = judgement.HoldMoveBinding;
        if (ProfileOwners.TryGetValue(clockProfile, out var value))
        {
            if (!ReferenceEquals(value.Api, api))
                throw new InvalidOperationException("This Seek profile belongs to another native runtime.");
            if (ReferenceEquals(value.Start, start) && ReferenceEquals(value.Move, move))
            { value.Verify(); return value; }
        }
        if (_preparingProfiles) throw new InvalidOperationException("Seek native profile construction cannot synchronously reenter.");
        _preparingProfiles = true;
        try
        {
            var replacement = new Profiles(api, clockProfile, start, move);
            judgement.VerifyCurrent();
            if (!ReferenceEquals(judgement.StartCoroutineBinding, start) || !ReferenceEquals(judgement.HoldMoveBinding, move))
                throw new InvalidOperationException("The installed Hold bindings changed during Seek profile construction.");
            replacement.Verify();
            ProfileOwners.Remove(clockProfile); ProfileOwners.Add(clockProfile, replacement);
            return replacement;
        }
        finally { _preparingProfiles = false; }
    }

    internal sealed class Profiles
    {
        internal readonly NativeRuntime Api;
        internal readonly ISeekChartNativeProfile Clock;
        internal readonly ExactHookBroker.Binding Start, Move;
        internal readonly ISeekTargetNativeProfile Target;
        internal readonly ISeekHoldNativeProfile Hold;
        internal readonly ISeekHoldGeometryProfile Geometry;
        internal readonly ISeekRendererNativeProfile Renderer;
        internal readonly ISeekCanvasNativeProfile Canvas;
        internal readonly ISeekCanvasPositionNativeProfile Position;
        internal readonly ISeekLineRingSourceProfile Lines;
        internal readonly ISeekLineSetPositionsNativeProfile LineRenderer;
        internal readonly ISeekNoteVisibilityNativeProfile Visibility;
        private readonly int _thread = Environment.CurrentManagedThreadId;

        internal Profiles(NativeRuntime api, GameplayClockNativeProfile clock, ExactHookBroker.Binding start, ExactHookBroker.Binding move)
        {
            Api = api; Clock = clock; Start = start; Move = move;
            Target = new NativeSeekTargetNativeProfile(api, clock); Hold = new NativeSeekHoldNativeProfile(api, Target);
            Geometry = new NativeSeekHoldGeometryProfile(api, Hold);
            Renderer = new NativeSeekRendererProfile(api, clock, start); Canvas = new NativeSeekCanvasNativeProfile(api, clock);
            Position = new NativeSeekCanvasPositionNativeProfile(api);
            Lines = new NativeSeekLineRingSourceProfile(api, Canvas); LineRenderer = new NativeSeekLineSetPositionsNativeProfile(api, Lines.LineClass);
            Visibility = new NativeSeekNoteVisibilityProfile(api, Renderer, Geometry);
        }

        internal Profiles(NativeRuntime api, ISeekChartNativeProfile clock, ExactHookBroker.Binding start, ExactHookBroker.Binding move,
            ISeekTargetNativeProfile target, ISeekHoldNativeProfile hold, ISeekHoldGeometryProfile geometry,
            ISeekRendererNativeProfile renderer, ISeekCanvasNativeProfile canvas, ISeekCanvasPositionNativeProfile position,
            ISeekLineRingSourceProfile lines, ISeekLineSetPositionsNativeProfile lineRenderer, ISeekNoteVisibilityNativeProfile visibility)
        {
            Api = api; Clock = clock; Start = start; Move = move; Target = target; Hold = hold;
            Geometry = geometry; Renderer = renderer; Canvas = canvas; Position = position;
            Lines = lines; LineRenderer = lineRenderer; Visibility = visibility;
        }

        internal void Verify()
        {
            if (_thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("The native Seek profiles belong to their owner thread.");
            Clock.VerifyCurrent(); Target.VerifyCurrent(); Hold.VerifyCurrent(); Geometry.VerifyCurrent();
            Renderer.VerifyCurrent(); Canvas.VerifyCurrent(); Position.VerifyCurrent();
            Visibility.VerifyCurrent();
            Lines.VerifyCurrent(); LineRenderer.VerifyCurrent(() =>
            {
                if (_thread != Environment.CurrentManagedThreadId)
                    throw new InvalidOperationException("The native Seek profiles belong to their owner thread.");
            });
        }
    }
}
