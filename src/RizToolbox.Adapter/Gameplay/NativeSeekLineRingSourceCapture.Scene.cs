using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

// Historical observations of the actual current producer inputs. These are
// values read from the original retained LevelInformation, not target values.
internal sealed record SeekLineRingSceneSample(long Information, long UIControl,
    long CameraControl, long GameSceneCanvas, int LevelSceneWidthBits,
    int LineSceneWidthBits, int LineGameSceneScaleBits, int LineNowTimeBits,
    int LineSpeedBits, int CameraXBits,
    int CameraScaleBits, NativeVector3 GameSceneLocalScale)
{
    internal float LevelSceneWidth => BitConverter.Int32BitsToSingle(LevelSceneWidthBits);
    internal float LineSceneWidth => BitConverter.Int32BitsToSingle(LineSceneWidthBits);
    internal float LineGameSceneScale => BitConverter.Int32BitsToSingle(LineGameSceneScaleBits);
    internal float LineNowTime => BitConverter.Int32BitsToSingle(LineNowTimeBits);
    internal float LineSpeed => BitConverter.Int32BitsToSingle(LineSpeedBits);
    internal float CameraX => BitConverter.Int32BitsToSingle(CameraXBits);
    internal float CameraScale => BitConverter.Int32BitsToSingle(CameraScaleBits);
    internal float GameSceneScaleX => GameSceneLocalScale.X;
}

internal sealed record SeekLineRingSceneObservation(
    NativeSeekLineRingSourceCapture OriginalSource,
    SeekLineRingSourceIdentity SourceIdentity,
    SeekLineRingSceneSample Current)
{
    internal bool AtomicSnapshot => false;
    internal bool DeterministicTarget => false;
    internal bool CompleteSeekPermitted => false;
}

// The two source observations remain sequential historical measurements.
// Keeping both original owners makes a later consumer's provenance check exact.
internal sealed record SeekLineEndpointWorldSceneObservation(
    SeekLineEndpointAppliedWorldObservation AppliedWorld,
    SeekLineRingSceneObservation Scene)
{
    internal bool AtomicSnapshot => false;
    internal bool DeterministicTarget => false;
    internal bool CompleteSeekPermitted => false;
}

internal interface IRetainedSeekLineRingSceneReader
{
    SeekLineRingSceneSample ReadSceneSample();
}

internal sealed partial class NativeSeekLineRingSourceCapture
{
    private static bool SameScene(SeekLineRingSceneSample a, SeekLineRingSceneSample b)
        => a.Information == b.Information && a.UIControl == b.UIControl
            && a.CameraControl == b.CameraControl && a.GameSceneCanvas == b.GameSceneCanvas
            && a.LevelSceneWidthBits == b.LevelSceneWidthBits
            && a.LineSceneWidthBits == b.LineSceneWidthBits
            && a.LineGameSceneScaleBits == b.LineGameSceneScaleBits
            && a.LineNowTimeBits == b.LineNowTimeBits && a.LineSpeedBits == b.LineSpeedBits
            && a.CameraXBits == b.CameraXBits && a.CameraScaleBits == b.CameraScaleBits
            && BitConverter.SingleToInt32Bits(a.GameSceneLocalScale.X) == BitConverter.SingleToInt32Bits(b.GameSceneLocalScale.X)
            && BitConverter.SingleToInt32Bits(a.GameSceneLocalScale.Y) == BitConverter.SingleToInt32Bits(b.GameSceneLocalScale.Y)
            && BitConverter.SingleToInt32Bits(a.GameSceneLocalScale.Z) == BitConverter.SingleToInt32Bits(b.GameSceneLocalScale.Z);

    /// <summary>Read actual camera/UI/scene scalars on the original line reader
    /// child, with a complete source recheck on each side. A later consumer
    /// must reenter its own operation; this value is never a write ticket.</summary>
    internal SeekLineRingSceneObservation ReadSceneObservation()
    {
        VerifyThread();
        try
        {
            return Operation(() =>
            {
                var source = VerifyInOperation(baseline: false);
                if (_reader is not IRetainedSeekLineRingSceneReader scene)
                    throw new InvalidOperationException("The original retained line reader has no camera/UI scene capability.");
                var first = scene.ReadSceneSample(); Local();
                var second = scene.ReadSceneSample(); Local();
                if (!SameScene(first, second))
                    throw new InvalidOperationException("Actual camera/UI/scene inputs changed between complete observations.");
                VerifyInOperation(baseline: false);
                if (second.Information != source.Identity.Information)
                    throw new InvalidOperationException("The scene observation belongs to another LevelInformation.");
                return new SeekLineRingSceneObservation(this, source.Identity, second);
            });
        }
        catch
        {
            _sourceVerified = false; _baselineVerified = false;
            throw;
        }
    }

    internal SeekLineEndpointWorldSceneObservation ReadSceneAfterAppliedWorld(
        SeekLineEndpointAppliedWorldObservation world)
    {
        VerifyThread(); ArgumentNullException.ThrowIfNull(world);
        if (_endpointWriterOwner is null)
            throw new InvalidOperationException("The original line source has no adopted endpoint writer.");
        _endpointWriterOwner.RequireIssuedWorldObservation(world);
        if (!ReferenceEquals(world.OriginalSource, this) || world.SourceIdentity != Snapshot.Identity)
            throw new InvalidOperationException("The applied world receipt does not belong to this exact original source and writer.");
        var scene = ReadSceneObservation();
        _endpointWriterOwner.RequireIssuedWorldObservation(world);
        if (scene.SourceIdentity != world.SourceIdentity)
            throw new InvalidOperationException("The actual scene read changed the original line source identity.");
        return new(world, scene);
    }
}
