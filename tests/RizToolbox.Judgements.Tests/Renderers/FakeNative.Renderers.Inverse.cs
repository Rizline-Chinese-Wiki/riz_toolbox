using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal bool RendererInverseEnabled { get; private set; }
    internal bool RendererPartialDestroy;
    internal int RendererInverseWrites;
    internal readonly List<(string Method, IntPtr Target, bool AllowAssets)> RendererDestructions = new();
    internal readonly List<IntPtr> RendererDeathReads = new();
    internal Action<IntPtr>? BeforeRendererDestroy;
    private IntPtr _rendererGameObjectType, _rendererSystemArrayType;
    private readonly Dictionary<string, IntPtr> _rendererInverseMethods = new();
    private readonly Dictionary<IntPtr, IntPtr> _rendererGameObjects = new(), _rendererGameObjectTransforms = new();
    private readonly HashSet<IntPtr> _rendererDeadObjects = new();
    private readonly HashSet<IntPtr> _rendererInverseComponentClasses = new();
    internal void ConfigureRendererInverse()
    {
        if (RendererInverseEnabled) throw new InvalidOperationException("Renderer inverse fixture is once-configured.");
        RendererInverseEnabled = true;
        _rendererGameObjectType = SeekType("UnityEngine.GameObject");
        _rendererSystemArrayType = SeekType("System.Array");
        var unity = SeekType("UnityEngine.Object");
        void Method(IntPtr cls, string name, bool isStatic, IntPtr returns, params IntPtr[] parameters)
            => _rendererInverseMethods.Add(name, GameplayMethodAdd(cls, name, isStatic, returns, parameters));
        Method(_rendererComponentType, "get_gameObject", false, _rendererGameObjectType);
        Method(_rendererGameObjectType, "get_transform", false, _rendererTransformType);
        Method(_rendererTransformType, "get_childCount", false, Int32);
        Method(_rendererTransformType, "GetChild", false, _rendererTransformType, Int32);
        Method(_rendererTransformType, "SetParent", false, Void, _rendererTransformType, Boolean);
        Method(_rendererTransformType, "set_localPosition", false, Void, _rendererVectorType);
        Method(_rendererTransformType, "set_localRotation", false, Void, _rendererQuaternionType);
        Method(_rendererTransformType, "set_localScale", false, Void, _rendererVectorType);
        Method(_rendererSystemArrayType, "GetValue", false, Object, Int32);
        Method(_rendererSystemArrayType, "SetValue", false, Void, Object, Int32);
        Method(unity, "DestroyImmediate", true, Void, unity, Boolean);
        Method(unity, "Destroy", true, Void, unity);
        foreach (var child in RendererChildren) RendererAttachGameObject(child);
        foreach (var component in RendererNotes.Concat(RendererPools)
            .Concat(new[] { GameplayController, GameplayInformation, GameplayProgress, GameplayPause, CursorControl }).Distinct())
        {
            _rendererInverseComponentClasses.Add(ObjectClass(component));
            if (!_rendererTransforms.ContainsKey(component)) _rendererTransforms[component] = RendererNewTransform(IntPtr.Zero, false);
        }
        foreach (var transform in _rendererTrs.Keys)
            if (!_rendererGameObjects.ContainsKey(transform))
            {
                var gameObject = SeekObject(_rendererGameObjectType);
                _rendererGameObjects[transform] = gameObject; _rendererGameObjectTransforms[gameObject] = transform;
            }
        foreach (var component in _rendererTransforms)
            _rendererGameObjects[component.Key] = _rendererGameObjects[component.Value];
    }
    private void RendererAttachGameObject(IntPtr child)
    {
        var gameObject = SeekObject(_rendererGameObjectType); var transform = _rendererTransforms[child];
        var component = RendererReference(child, "targetComponent");
        _rendererGameObjects[child] = gameObject; _rendererGameObjects[component] = gameObject;
        _rendererGameObjects[transform] = gameObject; _rendererGameObjectTransforms[gameObject] = transform;
    }
    internal IntPtr RendererGameObject(IntPtr child) => _rendererGameObjects[child];
    internal void RendererAliasGameObject(IntPtr child, IntPtr original)
        => _rendererGameObjects[child] = _rendererGameObjects[original];
    internal IntPtr RendererAttachForeignDescendant(IntPtr parentChild)
    {
        var transform = RendererNewTransform(RendererChildTransform(parentChild), false);
        var gameObject = SeekObject(_rendererGameObjectType);
        _rendererGameObjects[transform] = gameObject; _rendererGameObjectTransforms[gameObject] = transform;
        return gameObject;
    }
    internal bool RendererIsDead(IntPtr value) => _rendererDeadObjects.Contains(value);
    private IntPtr[] RendererTransformChildren(IntPtr parent) => _rendererTrs
        .Where(pair => pair.Value.Parent == parent && !_rendererDeadObjects.Contains(pair.Key)).Select(pair => pair.Key).ToArray();
    private void RendererInverseField(IntPtr owner, IntPtr field, IntPtr value)
    {
        if (!_rendererStorageFields.Contains(field) || !_gameplayStorage.ContainsKey((owner, field)))
            throw new InvalidOperationException("The renderer inverse attempted unrelated native field storage.");
        RendererReceiversRooted &= HasRootFor(owner); ++RendererInverseWrites;
        var type = _types[_fieldMetadata[field].Type];
        if (!type.ValueType)
        {
            RendererReceiversRooted &= value == IntPtr.Zero || HasRootFor(value);
            _gameplayStorage[(owner, field)] = BitConverter.GetBytes(value.ToInt64());
        }
        else
        {
            var bytes = new byte[type.Size]; Marshal.Copy(value, bytes, 0, bytes.Length);
            _gameplayStorage[(owner, field)] = bytes;
        }
    }
    private bool RendererTryInverseInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (!RendererInverseEnabled) return false;
        if (_gameplayMethods.TryGetValue(method, out var identity) && identity.Name == "op_Implicit"
            && identity.Class == ClassOf(SeekType("UnityEngine.Object")))
        {
            var value = Marshal.ReadIntPtr(arguments);
            if (_rendererGameObjects.ContainsKey(value) || _rendererGameObjectTransforms.ContainsKey(value))
            {
                ++Invocations; Marshal.WriteIntPtr(exception, IntPtr.Zero);
                RendererReceiversRooted &= HasRootFor(value); RendererDeathReads.Add(value);
                result = Box(Boolean, new[] { (byte)(_rendererDeadObjects.Contains(value) ? 0 : 1) }); return true;
            }
        }
        var pair = _rendererInverseMethods.FirstOrDefault(row => row.Value == method);
        if (pair.Key is null) return false;
        ++Invocations; Marshal.WriteIntPtr(exception, IntPtr.Zero);
        var name = pair.Key;
        if (name is "DestroyImmediate" or "Destroy")
        {
            var target = Marshal.ReadIntPtr(arguments);
            var assets = name == "DestroyImmediate" && Marshal.ReadByte(Marshal.ReadIntPtr(arguments, IntPtr.Size)) != 0;
            RendererReceiversRooted &= receiver == IntPtr.Zero && HasRootFor(target);
            RendererDestructions.Add((name, target, assets)); ++RendererDestroyCalls;
            BeforeRendererDestroy?.Invoke(target);
            if (assets || !_rendererGameObjectTransforms.ContainsKey(target))
                throw new InvalidOperationException("Synthetic destruction needs a retained exact GameObject and allowDestroyingAssets=false.");
            // Deferred Destroy intentionally leaves objects alive in this call.
            if (name == "Destroy") return true;
            _rendererDeadObjects.Add(target);
            if (!RendererPartialDestroy)
            {
                var stack = new Stack<IntPtr>(); stack.Push(_rendererGameObjectTransforms[target]);
                while (stack.TryPop(out var transform))
                {
                    foreach (var childTransform in RendererTransformChildren(transform)) stack.Push(childTransform);
                    if (_rendererGameObjects.TryGetValue(transform, out var gameObject))
                    {
                        _rendererDeadObjects.Add(gameObject);
                        foreach (var component in _rendererGameObjects.Where(row => row.Value == gameObject).Select(row => row.Key))
                            _rendererDeadObjects.Add(component);
                    }
                    _rendererDeadObjects.Add(transform);
                }
            }
            return true;
        }
        RendererReceiversRooted &= HasRootFor(receiver);
        if (name == "get_gameObject") result = _rendererGameObjects[receiver];
        else if (name == "get_transform") result = _rendererGameObjectTransforms[receiver];
        else if (name == "get_childCount") result = Box(Int32, BitConverter.GetBytes(RendererTransformChildren(receiver).Length));
        else if (name == "GetChild") result = RendererTransformChildren(receiver)[Marshal.ReadInt32(Marshal.ReadIntPtr(arguments))];
        else if (name is "GetValue" or "SetValue")
        {
            if (!_rendererArrays.ContainsKey(receiver)) throw new InvalidOperationException("The renderer inverse used a foreign array.");
            var index = Marshal.ReadInt32(Marshal.ReadIntPtr(arguments, name == "GetValue" ? 0 : IntPtr.Size));
            if (name == "GetValue") result = _rendererArrays[receiver][index];
            else
            {
                var value = Marshal.ReadIntPtr(arguments); RendererReceiversRooted &= value == IntPtr.Zero || HasRootFor(value);
                RendererSetSlot(receiver, index, value); ++RendererInverseWrites;
            }
        }
        else
        {
            var before = _rendererTrs[receiver]; ++RendererInverseWrites;
            if (name == "SetParent")
            {
                var parent = Marshal.ReadIntPtr(arguments);
                if (Marshal.ReadByte(Marshal.ReadIntPtr(arguments, IntPtr.Size)) != 0)
                    throw new InvalidOperationException("The inverse fixture expects exact local TRS with worldPositionStays=false.");
                RendererReceiversRooted &= parent == IntPtr.Zero || HasRootFor(parent);
                _rendererTrs[receiver] = before with { Parent = parent };
            }
            else if (name == "set_localRotation") _rendererTrs[receiver] = before with
                { Rotation = Marshal.PtrToStructure<NativeRendererQuaternion>(Marshal.ReadIntPtr(arguments)) };
            else if (name == "set_localPosition") _rendererTrs[receiver] = before with
                { Position = Marshal.PtrToStructure<NativeVector3>(Marshal.ReadIntPtr(arguments)) };
            else if (name == "set_localScale") _rendererTrs[receiver] = before with
                { Scale = Marshal.PtrToStructure<NativeVector3>(Marshal.ReadIntPtr(arguments)) };
            else throw new InvalidOperationException("Unrecognized renderer inverse native method.");
        }
        AfterRendererGetter?.Invoke(name, receiver); return true;
    }
}
