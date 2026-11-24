using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekRendererAccess : INativeSeekNoteVisibilityAccess
    {
        private ISeekNoteVisibilityNativeProfile? _visibilityProfile;
        public void VerifyVisibilityProfile(ISeekNoteVisibilityNativeProfile profile)
        {
            VerifySource(); Native(() => { profile.VerifyCurrent(); return 0; });
            if (!ReferenceEquals(profile.Renderer, _profile) || profile.NoteControlClass != _controlClass
                || profile.Geometry.InformationClass != _owner._info || profile.Geometry.NoteClass != _owner._note
                || _visibilityProfile is not null && !ReferenceEquals(_visibilityProfile, profile))
                throw new UnsupportedSignatureException("Visibility must use the original renderer access and its exact composed profile.");
            _visibilityProfile = profile;
            foreach (var field in VisibilityControlFields)
            {
                var metadata = Native(() => _owner._api.InspectExactDeclaredReadField(_controlClass, field.Name, field.Type, false, field.Offset));
                if ((metadata.Flags & (0x20u | 0x40u)) != 0) throw new UnsupportedSignatureException("A visibility control field is not mutable.");
            }
        }
        private static readonly (string Name, string Type, int Offset)[] VisibilityControlFields =
        {
            ("_speed", "System.Single", 0x40), ("_hasNote", "System.Boolean", 0x44),
            ("_activeHasNote", "System.Boolean", 0x4c), ("_startIndex", "System.Int32", 0x48),
            ("_activeStartIndex", "System.Int32", 0x50)
        };
        public SeekNoteVisibilitySnapshot ReadVisibility(ISeekNoteVisibilityNativeProfile profile)
        {
            VerifyReady();
            if (!ReferenceEquals(_visibilityProfile, profile)) throw new InvalidOperationException("The actual visibility source was not prepared.");
            var first = ReadVisibilityOnce(profile); var second = ReadVisibilityOnce(profile);
            if (!SeekNoteVisibilityState.Same(first, second)) throw new InvalidOperationException("The complete actual visibility basis changed between observations.");
            VerifyReady(); return second;
        }
        private long[] ReadVisibilityBasisArray(NativeRuntime.RootHandle array, IntPtr elementClass)
        {
            var geometry = (_visibilityProfile ?? throw new InvalidOperationException("No actual visibility profile exists.")).Geometry;
            if (elementClass != geometry.TransformClass && elementClass != geometry.EaseInfoClass)
                throw new UnsupportedSignatureException("Visibility basis arrays require their original exact Transform or EaseInfor class.");
            var arrayClass = ClassOf(array);
            // These are Canvas source inputs, not renderer plans or pool
            // children. Keep the existing narrow Canvas reader and adopt each
            // loaded token before any class check or source withdrawal guard.
            var observed = Native(() => _owner._api.ReadRootedCanvasSourceArray(array, arrayClass,
                elementClass, NativeSeekRendererCommands.MaximumArrayCapacity, (_, pointer) =>
                {
                    if (pointer != IntPtr.Zero) Adopt(pointer);
                    else VerifyLocal();
                }, VerifyLocal));
            if (observed.Array != Target(array) || observed.Class != arrayClass || observed.ElementClass != elementClass
                || observed.Length != observed.Items.Count)
                throw new InvalidOperationException("A complete visibility basis array changed its retained identity or complete length.");
            var values = new long[observed.Length];
            for (var slot = 0; slot < values.Length; ++slot)
            {
                var pointer = observed.Items[slot]; values[slot] = pointer.ToInt64();
                if (pointer != IntPtr.Zero && ClassOf(Root(values[slot])) != elementClass)
                    throw new UnsupportedSignatureException("An actual visibility basis slot has another exact element class.");
            }
            return values;
        }
        private SeekNoteVisibilitySnapshot ReadVisibilityOnce(ISeekNoteVisibilityNativeProfile profile)
        {
            var geometry = profile.Geometry;
            var words = new List<long>();
            void Word(long value) { if (words.Count >= NativeSeekRendererCommands.MaximumTotalCapacity) throw new NotSupportedException("Visibility source exceeds its complete observation bound."); words.Add(value); }
            void Single(float value) { if (!float.IsFinite(value)) throw new InvalidDataException("Visibility source contains a nonfinite value."); Word(BitConverter.SingleToInt32Bits(value)); }
            void Transform(NativeRuntime.RootHandle root)
            {
                var t = ReadTransform(root); Word(t.Reference); Word(t.Parent);
                Single(t.LocalPosition.X); Single(t.LocalPosition.Y); Single(t.LocalPosition.Z);
                Single(t.LocalRotation.X); Single(t.LocalRotation.Y); Single(t.LocalRotation.Z); Single(t.LocalRotation.W);
                Single(t.LocalScale.X); Single(t.LocalScale.Y); Single(t.LocalScale.Z);
                var world = Native(() => _owner._api.InvokeValueWithPublication<NativeVector3>(geometry.GetPositionMethod, Target(root)));
                Single(world.X); Single(world.Y); Single(world.Z);
            }
            void Singles(NativeRuntime.RootHandle array)
            {
                var cls = ClassOf(array); var result = Native(() => _owner._api.ReadRootedGeometrySingleArray(array, cls, NativeRuntime.MaximumGeometryArrayLength));
                if (result.Array != Target(array) || result.Class != cls || result.Length != result.Items.Count)
                    throw new InvalidOperationException("Visibility vector lost its actual retained identity.");
                Word(Target(array).ToInt64()); Word(result.Length); foreach (var value in result.Items) Single(value);
            }
            var time = Value<float>(_information, _owner._info, "nowTime", "System.Single", 0xb0);
            var speed = Value<float>(_information, _owner._info, "speed", "System.Single", 0x98);
            if (!float.IsFinite(time) || !float.IsFinite(speed) || speed <= 0) throw new InvalidDataException("Visibility target time/speed is invalid.");
            var canvas = RequiredReference(_information, _owner._info, "canvasControl", "InLevel.CanvasControl", 0x70, geometry.CanvasClass);
            if (ReferenceToken(_control, _controlClass, "_canvasControl", "InLevel.CanvasControl", 0x38) != Target(canvas).ToInt64())
                throw new InvalidOperationException("The actual NoteControl has another canvas producer.");
            Word(Target(canvas).ToInt64());
            var floors = Reference(canvas, geometry.CanvasClass, "floorPositions", "System.Single[]", 0x38)!;
            Singles(floors);
            var canvases = Reference(canvas, geometry.CanvasClass, "canvasTransforms", "UnityEngine.Transform[]", 0x30)!;
            var canvasRefs = ReadVisibilityBasisArray(canvases, geometry.TransformClass);
            Word(Target(canvases).ToInt64()); Word(canvasRefs.Length);
            foreach (var reference in canvasRefs) { if (reference == 0) throw new InvalidDataException("A visibility canvas Transform is absent."); Transform(Root(reference)); }
            var ui = RequiredReference(_information, _owner._info, "uIControl", "InLevel.UIControl", 0x68, geometry.UIClass);
            var uiTransform = RequiredReference(ui, geometry.UIClass, "gameSceneCanvas", "UnityEngine.Transform", 0x20, geometry.TransformClass);
            var camera = RequiredReference(_information, _owner._info, "cameraControl", "InLevel.CameraControl", 0x78, geometry.CameraClass);
            Word(Target(ui).ToInt64()); Word(Target(camera).ToInt64()); Transform(uiTransform); Transform(ComponentTransform(camera));
            // Easing is actual table input, not an inferred formula.
            var easeField = Native(() => _owner._api.InspectExactDeclaredReadField(geometry.EaseClass, "EaseInfos", "InLevel.EaseInfor[]", true, 0));
            NativeRuntime.RootHandle? ease = null; var published = false;
            Native(() => { _owner._api.PublishExactStaticReferenceField(easeField, pointer =>
                { if (published) throw new InvalidOperationException("Ease table published twice."); published = true; if (pointer != IntPtr.Zero) ease = Adopt(pointer); }); return 0; });
            if (!published) throw new InvalidOperationException("Ease table source was not published.");
            Word(ease is null ? 0 : Target(ease).ToInt64());
            if (ease is not null)
            {
                var refs = ReadVisibilityBasisArray(ease, geometry.EaseInfoClass); Word(refs.Length);
                foreach (var reference in refs)
                {
                    if (reference == 0) throw new InvalidDataException("An actual ease row is absent.");
                    var row = Root(reference); Word(reference); Word(Value<int>(row, geometry.EaseInfoClass, "code", "System.Int32", 0x10));
                    Singles(Reference(row, geometry.EaseInfoClass, "progress", "System.Single[]", 0x18)!);
                }
            }
            var heads = new SeekNoteVisibilityHead[_notes.Length];
            for (var index = 0; index < _notes.Length; ++index)
            {
                VerifyRendererNote(index); var note = _notes[index];
                var data = RequiredReference(note, _owner._note, "noteData", "InLevel.Note", 0x28, _owner._noteData);
                var line = RequiredReference(note, _owner._note, "lineInformation", "InLevel.LineInformation", 0x20, geometry.LineClass);
                Word(Target(note).ToInt64()); Word(Target(data).ToInt64()); Word(Target(line).ToInt64());
                Word(Value<int>(data, _owner._noteData, "type", "System.Int32", 0x10));
                Single(Value<float>(data, _owner._noteData, "time", "System.Single", 0x14));
                Single(Value<float>(data, _owner._noteData, "floorPosition", "System.Single", 0x18));
                Word(Value<bool>(line, geometry.LineClass, "lineIsStatic", "System.Boolean", 0x20) ? 1 : 0);
                Word(Value<bool>(line, geometry.LineClass, "isVisible", "System.Boolean", 0x51) ? 1 : 0);
                Word(Value<int>(line, geometry.LineClass, "canvasIndex", "System.Int32", 0x24));
                foreach (var field in new[] { ("startLinePoint", 0x30), ("endLinePoint", 0x38) })
                {
                    var point = RequiredReference(line, geometry.LineClass, field.Item1, "InLevel.LinePoint", field.Item2, geometry.LinePointClass);
                    Word(Target(point).ToInt64()); Single(Value<float>(point, geometry.LinePointClass, "time", "System.Single", 0x10));
                    Single(Value<float>(point, geometry.LinePointClass, "xPosition", "System.Single", 0x14));
                    Word(Value<int>(point, geometry.LinePointClass, "easeType", "System.Int32", 0x1c));
                    Word(Value<int>(point, geometry.LinePointClass, "canvasIndex", "System.Int32", 0x20));
                    Single(Value<float>(point, geometry.LinePointClass, "floorPosition", "System.Single", 0x24));
                }
                Transform(ComponentTransform(line));
                Transform(RequiredReference(line, geometry.LineClass, "endTransform", "UnityEngine.Transform", 0x40, geometry.TransformClass));
                var head = ReadTransform(ComponentTransform(note));
                if (new[] { head.LocalPosition.X, head.LocalPosition.Y, head.LocalPosition.Z,
                    head.LocalScale.X, head.LocalScale.Y, head.LocalScale.Z, head.LocalRotation.X,
                    head.LocalRotation.Y, head.LocalRotation.Z, head.LocalRotation.W }.Any(value => !float.IsFinite(value)))
                    throw new InvalidDataException("Actual note head geometry is nonfinite.");
                heads[index] = new(Target(note).ToInt64(), Value<bool>(note, _owner._note, "isStaticNote", "System.Boolean", 0x54), head);
            }
            var controlSpeed = Value<float>(_control, _controlClass, "_speed", "System.Single", 0x40);
            var cursor = new NativeNoteCursorState(Value<bool>(_control, _controlClass, "_hasNote", "System.Boolean", 0x44),
                Value<int>(_control, _controlClass, "_startIndex", "System.Int32", 0x48),
                Value<bool>(_control, _controlClass, "_activeHasNote", "System.Boolean", 0x4c),
                Value<int>(_control, _controlClass, "_activeStartIndex", "System.Int32", 0x50));
            if (!float.IsFinite(controlSpeed) || cursor.StartIndex < 0 || cursor.StartIndex > _view!.RenderNotes.Count
                || cursor.ActiveStartIndex < 0 || cursor.ActiveStartIndex > _notes.Length)
                throw new InvalidDataException("Actual visibility control state is out of range.");
            return new(time, speed, controlSpeed, cursor, Array.AsReadOnly(words.ToArray()), Array.AsReadOnly(heads), _view!.RenderNotes);
        }
        public void WriteVisibilityControl(SeekNoteVisibilitySnapshot expected, float speed, NativeNoteCursorState cursors, Action entered)
        {
            var profile = _visibilityProfile ?? throw new InvalidOperationException("No actual visibility profile exists.");
            if (!SeekNoteVisibilityState.Same(ReadVisibility(profile), expected)) throw new InvalidOperationException("Visibility control changed before its selected writes.");
            WriteVisibilityValue(VisibilityControlFields[0], expected.ControlSpeed, speed, entered);
            for (var index = 0; index < 4; ++index)
            {
                var descriptor = VisibilityControlFields[index + 1];
                if (index < 2) WriteVisibilityValue(descriptor, expected.Cursors.At(index) != 0, cursors.At(index) != 0, entered);
                else WriteVisibilityValue(descriptor, expected.Cursors.At(index), cursors.At(index), entered);
            }
        }
        private void WriteVisibilityValue<T>((string Name, string Type, int Offset) descriptor, T expected, T value, Action entered) where T : struct
        {
            VerifyReady();
            if (!EqualityComparer<T>.Default.Equals(Value<T>(_control, _controlClass, descriptor.Name, descriptor.Type, descriptor.Offset), expected))
                throw new InvalidOperationException("A visibility control field changed before its conditional write.");
            Native(() => { using var argument = A.Value(value); _owner._api.WriteExactGuarded(_control, _controlClass,
                descriptor.Name, descriptor.Type, argument, () => { VerifyLocal(); entered(); }); return 0; });
            if (!EqualityComparer<T>.Default.Equals(Value<T>(_control, _controlClass, descriptor.Name, descriptor.Type, descriptor.Offset), value))
                throw new InvalidOperationException("A visibility control setter did not read back.");
        }
        public void DispatchVisibility(ISeekNoteVisibilityNativeProfile profile, bool active, SeekNoteVisibilitySnapshot expected, Action entered)
        {
            VerifyVisibilityProfile(profile);
            if (!SeekNoteVisibilityState.Same(ReadVisibility(profile), expected)) throw new InvalidOperationException("The actual visibility producer input changed.");
            var method = active ? profile.ActiveNotesMethod : profile.SetNotesMethod;
            var receiver = Target(_control);
            Native(() => { _owner._api.InvokeVoidWithPublicationGuarded(method, receiver, () => { VerifyLocal(); entered(); }); return 0; });
        }
        public void RestoreVisibilityHeads(ISeekNoteVisibilityNativeProfile profile, SeekNoteVisibilitySnapshot expected,
            SeekNoteVisibilitySnapshot baseline, Action entered)
        {
            if (!SeekNoteVisibilityState.Same(ReadVisibility(profile), expected)) throw new InvalidOperationException("The current note heads differ from the producer's exact result.");
            foreach (var pair in expected.Heads.Zip(baseline.Heads))
            {
                if (pair.First.Note != pair.Second.Note || pair.First.Transform.Reference != pair.Second.Transform.Reference)
                    throw new InvalidOperationException("The selected actual note head changed identity.");
                var current = pair.First.Transform; var original = pair.Second.Transform;
                if (SeekRendererState.Same(current.LocalPosition, original.LocalPosition)) continue;
                var root = Root(current.Reference);
                if (!SeekRendererState.Same(ReadTransform(root), current)) throw new InvalidOperationException("A note head changed before restoration.");
                Native(() => { using var argument = A.Value(original.LocalPosition); _owner._api.InvokeVoidWithPublicationGuarded(
                    profile.Geometry.SetLocalPositionMethod, Target(root), () => { VerifyLocal(); entered(); }, argument); return 0; });
                if (!SeekRendererState.Same(ReadTransform(root), original)) throw new InvalidOperationException("The original note head did not read back.");
            }
        }
    }
}
