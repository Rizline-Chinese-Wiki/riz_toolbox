using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class CanvasSource
        {
            private sealed partial class LineRingReader
            {
                private IntPtr[] ReadClasses() => new[]
                {
                    N(() => _profile.ChartClass), N(() => _profile.ProgressClass), N(() => _profile.InformationClass),
                    N(() => _profile.LineControlClass), N(() => _profile.LineClass), N(() => _profile.LinePointClass),
                    N(() => _profile.GroupClass), N(() => _profile.TransformClass),
                    N(() => _profile.UIClass), N(() => _profile.CameraClass)
                };
                private void VerifyClasses()
                {
                    if (_classes.Length != 10 || _classes[0] != _source._classes[0] || _classes[1] != _source._classes[1]
                        || _classes[2] != _source._classes[2] || _classes[7] != _source._classes[6])
                        throw new UnsupportedSignatureException("The line/ring profile belongs to another exact Canvas/chart source.");
                    var names = new[] { "Chart", "ProgressControl", "LevelInformation", "LineControl", "LineInformation", "LinePoint", "LinesGroupInformation", "Transform", "UIControl", "CameraControl" };
                    for (var i = 0; i < names.Length; ++i)
                    {
                        var slot = i;
                        var actual = N(() => _source._core._owner._api.Class(slot == 7 ? "UnityEngine.CoreModule.dll" : "Assembly-CSharp.dll",
                            slot == 7 ? "UnityEngine" : "InLevel", names[slot]));
                        if (actual == IntPtr.Zero || actual != _classes[slot])
                            throw new UnsupportedSignatureException("The line/ring profile names another exact declared class: " + names[slot]);
                    }
                }
                private NativeExactReadField Field(IntPtr cls, string name, string type, int offset)
                {
                    var key = (cls, name, type, offset);
                    var field = N(() => _source._core._owner._api.InspectLineRingSourceField(cls, name, type, offset, VerifyLocal));
                    if (_descriptors.TryGetValue(key, out var expected))
                    {
                        if (field != expected) throw Changed("A saved/current line/ring source field changed: " + name);
                    }
                    else _descriptors.Add(key, field);
                    return field;
                }
                private void Layouts()
                {
                    Field(_classes[2], "lineControl", "InLevel.LineControl", 0x38);
                    Field(_classes[2], "uIControl", "InLevel.UIControl", 0x68);
                    Field(_classes[2], "cameraControl", "InLevel.CameraControl", 0x78);
                    Field(_classes[2], "sceneWidth", "System.Single", 0xc4);
                    Field(_classes[8], "gameSceneCanvas", "UnityEngine.Transform", 0x20);
                    Field(_classes[9], "levelInformation", "InLevel.LevelInformation", 0x20);
                    Field(_classes[9], "xPosition", "System.Single", 0x30);
                    Field(_classes[9], "scale", "System.Single", 0x34);
                    Field(_classes[3], "levelInformation", "InLevel.LevelInformation", 0x20);
                    Field(_classes[3], "dynamicLines", "InLevel.LineInformation[]", 0x28);
                    Field(_classes[3], "lineGroups", "InLevel.LinesGroupInformation[]", 0x30);
                    Field(_classes[3], "_canvasControl", "InLevel.CanvasControl", 0x38);
                    Field(_classes[3], "_nowTime", "System.Single", 0x40);
                    Field(_classes[3], "_sceneWidth", "System.Single", 0x44);
                    Field(_classes[3], "_speed", "System.Single", 0x48);
                    Field(_classes[3], "_gameSceneScale", "System.Single", 0x4c);
                    Field(_classes[4], "lineIsStatic", "System.Boolean", 0x20);
                    Field(_classes[4], "canvasIndex", "System.Int32", 0x24);
                    Field(_classes[4], "startLinePoint", "InLevel.LinePoint", 0x30);
                    Field(_classes[4], "endLinePoint", "InLevel.LinePoint", 0x38);
                    Field(_classes[4], "endTransform", "UnityEngine.Transform", 0x40);
                    Field(_classes[4], "endLineInformation", "InLevel.LineInformation", 0x58);
                    Field(_classes[4], "linesGroup", "InLevel.LinesGroupInformation", 0x60);
                    Field(_classes[5], "time", "System.Single", 0x10);
                    Field(_classes[5], "xPosition", "System.Single", 0x14);
                    Field(_classes[5], "easeType", "System.Int32", 0x1c);
                    Field(_classes[5], "canvasIndex", "System.Int32", 0x20);
                    Field(_classes[5], "floorPosition", "System.Single", 0x24);
                    Field(_classes[6], "judgeRingTransform", "UnityEngine.Transform", 0x20);
                    Field(_classes[6], "ringActive", "System.Boolean", 0x29);
                    Field(_classes[6], "lines", "InLevel.LineInformation[]", 0x68);
                    Field(_classes[6], "nowLineIndex", "System.Int32", 0x70);
                }
                private T Scalar<T>(NativeRuntime.RootHandle receiver, IntPtr cls, string name, string type, int offset) where T : struct
                {
                    var field = Field(cls, name, type, offset);
                    return N(() => _source._core._owner._api.ReadLineRingSourceField<T>(receiver, field, VerifyLocal));
                }
                private NativeRuntime.RootHandle? AdoptPublication(IntPtr pointer)
                {
                    // This check is managed, before Root/newHandle can run.
                    // A full table has not consumed the GC-owned reference. Ack
                    // it here, then the publisher's post guard throws outside
                    // PublicationAccepted; no hidden unconfirmed token remains.
                    if (_publicationRefusal is not null) return null;
                    if (pointer != IntPtr.Zero && !_source._core._retained.ContainsKey(pointer.ToInt64())
                        && _source._core._roots.Count >= _source._core._rootLimit)
                    {
                        _publicationRefusal = new("The shared line/ring source root budget cannot admit this complete observation.");
                        return null;
                    }
                    // Never catch arbitrary allocation/adoption failures and
                    // pretend to acknowledge their unknown native ownership.
                    return _source.AdoptPublication(pointer);
                }
                private NativeRuntime.RootHandle? Reference(NativeRuntime.RootHandle receiver, IntPtr cls,
                    string name, string type, int offset)
                {
                    var field = Field(cls, name, type, offset);
                    NativeRuntime.RootHandle? result = null; var published = false;
                    N(() =>
                    {
                        _source._core._owner._api.PublishLineRingSourceReferenceField(receiver, field, pointer =>
                        {
                            if (published) throw Changed("A line/ring source field was published twice.");
                            published = true;
                            // Adoption registers the exact physical root before
                            // class inspection or the caller's post-call guard.
                            if (pointer != IntPtr.Zero) result = AdoptPublication(pointer);
                        }, VerifyLocal); return 0;
                    });
                    if (!published) throw Changed("A line/ring source field had no reference publication: " + name);
                    if (result is not null && N(() => _source._core._owner._api.ObjectClass(_source.Target(result))) != field.FieldClass)
                        throw new UnsupportedSignatureException("A line/ring source reference has another exact field class: " + name);
                    return result;
                }
                private NativeRuntime.RootHandle RequiredReference(NativeRuntime.RootHandle receiver, IntPtr cls,
                    string name, string type, int offset)
                    => Reference(receiver, cls, name, type, offset)
                        ?? throw new InvalidDataException("A required complete line/ring container or association is absent: " + name);
                private long Address(NativeRuntime.RootHandle? root)
                    => root is null ? 0 : N(() => _source.Target(root)).ToInt64();
                private NativeRuntime.RootHandle Root(long reference)
                {
                    var root = N(() => _source.Root(reference));
                    if (Address(root) != reference) throw Changed("A retained line/ring object changed its exact identity.");
                    return root;
                }
                private long[] ArrayReferences(NativeRuntime.RootHandle array, IntPtr element, int maximum)
                {
                    var cls = N(() => _source._core._owner._api.ObjectClass(_source.Target(array)));
                    var observed = N(() => _source._core._owner._api.ReadRootedLineRingSourceArray(array, cls, element, maximum,
                        (_, pointer) => { if (pointer != IntPtr.Zero) AdoptPublication(pointer); }, VerifyLocal));
                    if (observed.Array.ToInt64() != Address(array) || observed.Class != cls || observed.ElementClass != element
                        || observed.Length != observed.Items.Count)
                        throw Changed("A complete line/ring source vector lost its actual identity.");
                    var result = new long[observed.Length];
                    for (var i = 0; i < result.Length; ++i)
                    {
                        var reference = observed.Items[i].ToInt64();
                        if (reference != 0 && N(() => _source._core._owner._api.ObjectClass(_source.Target(Root(reference)))) != element)
                            throw new UnsupportedSignatureException("A complete line/ring vector slot has another exact element class.");
                        result[i] = reference;
                    }
                    return result;
                }
                private void VerifyGetters(bool initial)
                {
                    var methods = new (string Name, IntPtr Class, Func<IntPtr> Selected)[]
                    {
                        ("get_transform", _component, () => _profile.GetTransformMethod),
                        ("get_parent", _classes[7], () => _profile.GetParentMethod),
                        ("get_position", _classes[7], () => _profile.GetPositionMethod),
                        ("get_localPosition", _classes[7], () => _profile.GetLocalPositionMethod),
                        ("get_localRotation", _classes[7], () => _profile.GetLocalRotationMethod),
                        ("get_localScale", _classes[7], () => _profile.GetLocalScaleMethod)
                    };
                    foreach (var item in methods)
                    {
                        var actual = N(() => _source._core._owner._api.InspectLineRingSourceGetter(item.Class, item.Name, IntPtr.Zero, VerifyLocal));
                        var selected = N(item.Selected);
                        if (selected == IntPtr.Zero || actual.MethodInfo != selected
                            || !initial && (!_getters.TryGetValue(item.Name, out var saved) || !SameMethod(saved, actual)))
                            throw new UnsupportedSignatureException("A line/ring getter differs from its exact current source profile: " + item.Name);
                        if (initial) _getters.Add(item.Name, actual);
                    }
                }
                private NativeMetadataMethod Getter(string name)
                {
                    var saved = _getters[name];
                    var actual = N(() => _source._core._owner._api.InspectLineRingSourceGetter(saved.DeclaringClass.Class, name, IntPtr.Zero, VerifyLocal));
                    if (!SameMethod(saved, actual)) throw Changed("A saved/current line/ring getter changed: " + name);
                    return actual;
                }
                private NativeRuntime.RootHandle? TransformReference(string getter, NativeRuntime.RootHandle receiver, bool required)
                {
                    var method = Getter(getter);
                    NativeRuntime.RootHandle? result = null; var published = false;
                    N(() =>
                    {
                        _source._core._owner._api.InvokeReferenceWithPublicationGuarded(method.MethodInfo, _source.Target(receiver), pointer =>
                        {
                            if (published) throw Changed("A line/ring Transform getter published twice.");
                            published = true;
                            if (pointer != IntPtr.Zero) result = AdoptPublication(pointer);
                        }, VerifyLocal); return 0;
                    });
                    if (!published || required && result is null)
                        throw new InvalidDataException("A required actual line/ring Transform is absent: " + getter);
                    if (result is not null && N(() => _source._core._owner._api.ObjectClass(_source.Target(result))) != _classes[7])
                        throw new UnsupportedSignatureException("A line/ring getter returned another exact Transform class.");
                    return result;
                }
                private T TransformValue<T>(string getter, NativeRuntime.RootHandle receiver) where T : struct
                {
                    var method = Getter(getter);
                    return N(() => _source._core._owner._api.InvokeValueWithPublicationGuarded<T>(method.MethodInfo,
                        _source.Target(receiver), VerifyLocal));
                }
                private bool Alive(NativeRuntime.RootHandle receiver)
                {
                    var target = N(() => _source.Target(receiver));
                    var method = N(() => _source._core._owner._api.Method(_source._core._owner._object, "op_Implicit", 1,
                        isStatic: true, parameterTypes: new[] { "UnityEngine.Object" }));
                    return N(() =>
                    {
                        using var argument = A.Ref(target);
                        return _source._core._owner._api.InvokeValueWithPublicationGuarded<bool>(method, IntPtr.Zero, VerifyLocal, argument);
                    });
                }
                private void VerifyTransform(NativeRuntime.RootHandle root)
                {
                    if (N(() => _source._core._owner._api.ObjectClass(_source.Target(root))) != _classes[7] || !Alive(root))
                        throw Changed("An actual line/ring Transform died or changed its exact class.");
                }
            }
        }
    }
}
