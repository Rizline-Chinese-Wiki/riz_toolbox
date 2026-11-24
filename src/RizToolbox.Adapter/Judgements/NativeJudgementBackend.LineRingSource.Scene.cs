using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class CanvasSource
        {
            private sealed partial class LineRingReader
            {
                private (long UI, long Camera, long Canvas)? _sceneObjects;

                public SeekLineRingSceneSample ReadSceneSample()
                {
                    try
                    {
                        VerifySource();
                        var information = _source._core._information;
                        var infoAddress = _source._core._informationAddress;
                        if (Address(information) != infoAddress)
                            throw Changed("The actual scene LevelInformation lost its original root.");
                        var ui = RequiredReference(information, _classes[2], "uIControl", "InLevel.UIControl", 0x68);
                        var camera = RequiredReference(information, _classes[2], "cameraControl", "InLevel.CameraControl", 0x78);
                        var canvas = RequiredReference(ui, _classes[8], "gameSceneCanvas", "UnityEngine.Transform", 0x20);
                        var identity = (UI: Address(ui), Camera: Address(camera), Canvas: Address(canvas));
                        if (_sceneObjects is { } original && original != identity)
                            throw Changed("The original LevelInformation camera/UI/scene object identity changed.");
                        if (!Alive(ui) || !Alive(camera))
                            throw Changed("The actual camera or UI component was destroyed.");
                        VerifyTransform(canvas);
                        if (Address(RequiredReference(camera, _classes[9], "levelInformation", "InLevel.LevelInformation", 0x20)) != infoAddress)
                            throw Changed("The actual CameraControl no longer points to the original LevelInformation.");

                        var levelWidth = Scalar<float>(information, _classes[2], "sceneWidth", "System.Single", 0xc4);
                        var lineWidth = Scalar<float>(_control, _classes[3], "_sceneWidth", "System.Single", 0x44);
                        var lineScale = Scalar<float>(_control, _classes[3], "_gameSceneScale", "System.Single", 0x4c);
                        var lineNowTime = Scalar<float>(_control, _classes[3], "_nowTime", "System.Single", 0x40);
                        var lineSpeed = Scalar<float>(_control, _classes[3], "_speed", "System.Single", 0x48);
                        var cameraX = Scalar<float>(camera, _classes[9], "xPosition", "System.Single", 0x30);
                        var cameraScale = Scalar<float>(camera, _classes[9], "scale", "System.Single", 0x34);
                        var localScale = TransformValue<NativeVector3>("get_localScale", canvas);
                        if (!float.IsFinite(levelWidth) || !float.IsFinite(lineWidth) || !float.IsFinite(lineScale)
                            || !float.IsFinite(lineNowTime) || !float.IsFinite(lineSpeed)
                            || !float.IsFinite(cameraX) || !float.IsFinite(cameraScale)
                            || !float.IsFinite(localScale.X) || !float.IsFinite(localScale.Y) || !float.IsFinite(localScale.Z))
                            throw new LineRingObservationRefusedException("Actual camera/UI/scene inputs are nonfinite.");

                        if (Address(RequiredReference(information, _classes[2], "uIControl", "InLevel.UIControl", 0x68)) != identity.UI
                            || Address(RequiredReference(information, _classes[2], "cameraControl", "InLevel.CameraControl", 0x78)) != identity.Camera
                            || Address(RequiredReference(ui, _classes[8], "gameSceneCanvas", "UnityEngine.Transform", 0x20)) != identity.Canvas
                            || Address(RequiredReference(camera, _classes[9], "levelInformation", "InLevel.LevelInformation", 0x20)) != infoAddress)
                            throw Changed("Actual camera/UI/scene associations changed during scalar reads.");
                        VerifyTransform(canvas);
                        VerifySource(); VerifyLocal();
                        _sceneObjects ??= identity;
                        return new(infoAddress, identity.UI, identity.Camera, identity.Canvas,
                            BitConverter.SingleToInt32Bits(levelWidth), BitConverter.SingleToInt32Bits(lineWidth),
                            BitConverter.SingleToInt32Bits(lineScale), BitConverter.SingleToInt32Bits(lineNowTime),
                            BitConverter.SingleToInt32Bits(lineSpeed), BitConverter.SingleToInt32Bits(cameraX),
                            BitConverter.SingleToInt32Bits(cameraScale), localScale);
                    }
                    catch (Exception error)
                    {
                        if (!SeekCanvasBasisCallerWithdrawal.Is(error) && error is not LineRingObservationRefusedException)
                            _expired = true;
                        if (_expired) error.Data["SeekLineRingSourceExpired"] = true;
                        ObserveCleanupFailure(error); _basis.AttachProvenance(error); throw;
                    }
                }
            }
        }
    }
}
