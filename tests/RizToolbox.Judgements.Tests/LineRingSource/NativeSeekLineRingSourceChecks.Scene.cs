using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Judgements.Tests.NoteFlags;

namespace RizToolbox.Judgements.Tests.LineRingSource;

internal static partial class NativeSeekLineRingSourceChecks
{
    private static void FiniteScene(FakeNative n)
    {
        n.LineRingSet(n.LineRingControl, "_sceneWidth", 1920f);
        n.LineRingSet(n.LineRingControl, "_gameSceneScale", 2f);
    }

    private static void SceneCurrentInputs()
    {
        using var f = new Fixture(n =>
        {
            FiniteScene(n);
            n.LineRingSet(n.LineRingControl, "_sceneWidth", 1600f);
            n.LineRingSet(n.LineRingControl, "_gameSceneScale", 1.5f);
            n.LineRingSet(n.GeometryCamera, "scale", 2.5f);
            n.GeometrySetTransform(n.GeometryUiRoot, n.GeometryTransforms[n.GeometryUiRoot] with { Scale = new(3.5f, 3, 4) });
        });
        var n = f.Native; var basis = f.Prepare(); var source = f.Capture(basis);
        var ranges = n.CanvasSourceRangeAttempts;
        var priorUIReads = n.LineRingGetterCalls.Count(row => row.Name == "get_localScale" && row.Receiver == n.GeometryUiRoot);
        var old = source.ReadSceneObservation();
        Check(ReferenceEquals(old.OriginalSource, source)
            && ReferenceEquals(old.SourceIdentity.CanvasCapture, basis.SourceCapture)
            && ReferenceEquals(old.SourceIdentity.CanvasPlan, basis.SourceCapture.Plan)
            && old.Current.Information == n.GameplayInformation.ToInt64()
            && old.Current.UIControl == n.GeometryUi.ToInt64()
            && old.Current.CameraControl == n.GeometryCamera.ToInt64()
            && old.Current.GameSceneCanvas == n.GeometryUiRoot.ToInt64()
            && old.Current.LevelSceneWidthBits == Bits(1920f)
            && old.Current.LineSceneWidthBits == Bits(1600f)
            && old.Current.LineGameSceneScaleBits == Bits(1.5f)
            && old.Current.CameraXBits == Bits(7f) && old.Current.CameraScaleBits == Bits(2.5f)
            && Same(old.Current.GameSceneLocalScale, new NativeVector3(3.5f, 3, 4))
            && !old.AtomicSnapshot && !old.DeterministicTarget && !old.CompleteSeekPermitted
            && n.CanvasSourceRangeAttempts == ranges
            && n.LineRingGetterCalls.Count(row => row.Name == "get_localScale" && row.Receiver == n.GeometryUiRoot) > priorUIReads,
            "The real retained backend did not read its original camera/UI/scene fields and Transform getter.");
        n.LineRingSet(n.GameplayInformation, "sceneWidth", 2000f);
        n.LineRingSet(n.LineRingControl, "_sceneWidth", 1700f);
        n.LineRingSet(n.LineRingControl, "_gameSceneScale", 1.25f);
        n.LineRingSet(n.GeometryCamera, "xPosition", 8f);
        n.LineRingSet(n.GeometryCamera, "scale", 4f);
        n.GeometrySetTransform(n.GeometryUiRoot, n.GeometryTransforms[n.GeometryUiRoot] with { Scale = new(5, 6, 7) });
        var changed = source.ReadSceneObservation();
        Check(old.Current.LevelSceneWidthBits == Bits(1920f) && old.Current.LineSceneWidthBits == Bits(1600f)
            && old.Current.LineGameSceneScaleBits == Bits(1.5f) && old.Current.CameraXBits == Bits(7f)
            && old.Current.CameraScaleBits == Bits(2.5f) && Same(old.Current.GameSceneLocalScale, new(3.5f, 3, 4))
            && changed.Current.LevelSceneWidthBits == Bits(2000f) && changed.Current.LineSceneWidthBits == Bits(1700f)
            && changed.Current.LineGameSceneScaleBits == Bits(1.25f) && changed.Current.CameraXBits == Bits(8f)
            && changed.Current.CameraScaleBits == Bits(4f) && Same(changed.Current.GameSceneLocalScale, new(5, 6, 7))
            && ReferenceEquals(changed.OriginalSource, source) && !source.GetJournal().SourceExpired,
            "A mutable current scene scalar was frozen as a target or prior historical result was changed.");
        NoWrites(n, "A scene observation invoked a native producer or writer.");
    }

    private static void SceneIdentityDrift()
    {
        using var f = new Fixture(FiniteScene); var n = f.Native; var source = f.Capture();
        var prior = source.ReadSceneObservation();
        n.LineRingSet(n.GeometryUi, "gameSceneCanvas", n.LineRingLineTransform(n.LineRingOrphanDynamic));
        Reject(() => source.ReadSceneObservation());
        Check(source.GetJournal().SourceExpired && prior.Current.GameSceneCanvas == n.GeometryUiRoot.ToInt64(),
            "A same-class UI scene Transform replacement was accepted as the original source.");
        NoNative(n, () => Reject(() => source.ReadSceneObservation()));
    }

    private static void SceneScalarRefusal()
    {
        using var f = new Fixture(FiniteScene); var n = f.Native; var source = f.Capture();
        NoNative(n, () => Task.Run(() => Reject(() => source.ReadSceneObservation())).GetAwaiter().GetResult());
        source.ReadSceneObservation(); var fired = false;
        n.AfterCanvasSourceRead = (name, receiver) =>
        {
            if (fired || name != "scale" || receiver != n.GeometryCamera) return;
            fired = true; NoNative(n, () => Reject(() => source.ReadSceneObservation()));
            n.LineRingSet(receiver, "scale", 3f);
        };
        Reject(() => source.ReadSceneObservation()); f.Clear();
        Check(fired && !source.GetJournal().SourceExpired && source.ReadSceneObservation().Current.CameraScaleBits == Bits(3f),
            "A changing current camera scalar published a stable observation or permanently expired immutable source identity.");
        n.LineRingSet(n.GeometryCamera, "scale", float.NaN);
        Reject(() => source.ReadSceneObservation());
        Check(!source.GetJournal().SourceExpired, "Unsupported nonfinite current scalar permanently expired the source graph.");
        n.LineRingSet(n.GeometryCamera, "scale", 3f);
        Check(source.ReadSceneObservation().Current.CameraScaleBits == Bits(3f),
            "A later finite current observation could not reenter the original reader.");
    }

    private static void SceneWithdrawal()
    {
        using var f = new Fixture(FiniteScene); var n = f.Native; var basis = f.Prepare(); var source = f.Capture(basis);
        var fired = false;
        n.AfterCanvasSourceRead = (name, receiver) =>
        {
            if (fired || name != "scale" || receiver != n.GeometryCamera) return;
            fired = true; source.Dispose(); n.CanvasSourceExportLog.Clear(); n.CanvasSourceAuditExports = true;
        };
        Reject(() => source.ReadSceneObservation()); f.Clear();
        Check(fired && source.GetJournal().Disposed && n.CanvasSourceExportLog.All(name => name == "il2cpp_gchandle_free"),
            "Camera field callback withdrawal continued native inspection after the owner was withdrawn.");
        NoNative(n, () => Reject(() => source.ReadSceneObservation()));
        basis.VerifyPrepared();
    }

    private static void SceneAfterAppliedWorld()
    {
        using var f = new WriterFixture(parentEqual: false, configure: FiniteScene);
        var n = f.Native; var writer = f.Prepare(); var source = f.Source!;
        writer.Execute();
        var world = writer.ReadAppliedWorldObservation();
        var forgedEndpoints = world with { Endpoints = Array.Empty<SeekLineEndpointWorldObserved>() };
        var forgedCopy = world with { };
        NoNative(n, () =>
        {
            Reject(() => source.ReadSceneAfterAppliedWorld(forgedEndpoints));
            Reject(() => source.ReadSceneAfterAppliedWorld(forgedCopy));
        });
        var paired = source.ReadSceneAfterAppliedWorld(world);
        Check(ReferenceEquals(paired.AppliedWorld, world)
            && ReferenceEquals(paired.Scene.OriginalSource, source)
            && ReferenceEquals(world.CommandOwner, writer)
            && paired.Scene.SourceIdentity == world.SourceIdentity
            && paired.Scene.Current.CameraControl == n.GeometryCamera.ToInt64()
            && paired.Scene.Current.GameSceneCanvas == n.GeometryUiRoot.ToInt64()
            && paired.Scene.Current.CameraScaleBits == Bits(2f)
            && !paired.AtomicSnapshot && !paired.DeterministicTarget && !paired.CompleteSeekPermitted,
            "Applied endpoint world and current camera/UI source lost their original writer/capture identity.");
        writer.RestoreSelectedValues(); Check(writer.VerifyRestoredValues(), "Scene reading disturbed selected inverse.");
        NoNative(n, () => Reject(() => source.ReadSceneAfterAppliedWorld(world)));
        Reject(writer.Dispose); f.Retain();
    }
}
