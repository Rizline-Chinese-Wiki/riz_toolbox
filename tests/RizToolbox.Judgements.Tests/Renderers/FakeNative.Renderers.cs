using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal enum RendererShape { Inactive, Active, Mixed, Growth, Dirty }
    internal sealed record RendererNativeCall(int Ordinal, int Note, string Operation)
    { internal bool Returned; internal int Steps; }
    internal sealed record RendererPoolList(IntPtr Reference, IntPtr Backing, int Size, int Version, IReadOnlyList<IntPtr> Capacity);
    internal sealed record RendererTrs(IntPtr Parent, NativeVector3 Position, NativeRendererQuaternion Rotation, NativeVector3 Scale);
    internal readonly List<IntPtr> RendererNotes = new(), RendererPlans = new(), RendererPools = new(), RendererChildren = new(), RendererCreatedChildren = new();
    internal readonly List<RendererNativeCall> RendererCalls = new();
    internal readonly List<(IntPtr Pool, IntPtr Child)> RendererGets = new(), RendererReturns = new();
    internal Action<RendererNativeCall>? BeforeRendererCommand, AfterRendererCommand;
    internal Action<RendererNativeCall, int, string>? AfterRendererStep;
    internal Action<uint, IntPtr>? AfterRendererRoot;
    internal Action<string, IntPtr>? AfterRendererArrayExport;
    internal bool RendererReceiversRooted = true, RendererPublishedBeforeInspection = true;
    internal int RendererInstantiations, RendererDestroyCalls = 0;
    internal bool RendererFailHiddenGrowth, RendererReusePrefabOnGrowth;
    private IntPtr _rendererType, _rendererArrayType, _rendererPoolType, _rendererChildType, _rendererChildArrayType, _rendererListType;
    private IntPtr _rendererComponentType, _rendererTransformType, _rendererSpriteType, _rendererVectorType, _rendererQuaternionType;
    private IntPtr _rendererAppear, _rendererDisappear, _rendererComponentTransform, _rendererParent, _rendererPosition, _rendererRotation, _rendererScale;
    private readonly Dictionary<IntPtr, IntPtr[]> _rendererArrays = new();
    private readonly Dictionary<IntPtr, IntPtr> _rendererArrayTypes = new(), _rendererTransforms = new();
    private readonly Dictionary<IntPtr, RendererTrs> _rendererTrs = new();
    private readonly HashSet<IntPtr> _rendererStorageFields = new();
    private readonly Dictionary<(IntPtr Owner, IntPtr Field), byte[]> _rendererOtherStorage = new();
    internal RendererProfileValue RendererProfile = null!;
    internal sealed class RendererProfileValue : ISeekRendererNativeProfile
    {
        private readonly FakeNative _native; internal Action? OnVerify;
        internal RendererProfileValue(FakeNative native) => _native = native;
        public IntPtr ChartClass => _native.SeekProfile.ChartClass;
        public IntPtr ProgressClass => _native.SeekProfile.ProgressClass;
        public IntPtr NoteClass => _native.ClassOf(_native.SeekType("InLevel.NoteInformation"));
        public IntPtr RendererClass => _native.ClassOf(_native._rendererType);
        public IntPtr PoolClass => _native.ClassOf(_native._rendererPoolType);
        public IntPtr ChildClass => _native.ClassOf(_native._rendererChildType);
        public IntPtr AppearMethod => _native._rendererAppear;
        public IntPtr DisappearMethod => _native._rendererDisappear;
        public void VerifyCurrent() { OnVerify?.Invoke(); _native.SeekProfile.VerifyCurrent(); }
    }
    internal void ConfigureRenderers(RendererShape shape = RendererShape.Mixed, int available = 2, bool reuseSource = false)
    {
        if (!reuseSource) ConfigureNoteFlags(); RendererNotes.AddRange(_referenceArrays[CursorAllArray]);
        _rendererType = AddType("InLevel.NoteInformation.NoteRenderer", 18, false, IntPtr.Size);
        _rendererArrayType = AddType("InLevel.NoteInformation.NoteRenderer[]", 29, false, IntPtr.Size);
        _rendererPoolType = SeekType("InLevel.EasyPool"); _rendererChildType = SeekType("InLevel.EasyPoolChild");
        _rendererChildArrayType = AddType("InLevel.EasyPoolChild[]", 29, false, IntPtr.Size);
        _rendererListType = AddType("System.Collections.Generic.List<InLevel.EasyPoolChild>", 21, false, IntPtr.Size);
        _rendererComponentType = SeekType("UnityEngine.Component"); _rendererTransformType = SeekType("UnityEngine.Transform");
        _rendererSpriteType = SeekType("UnityEngine.SpriteRenderer");
        _rendererVectorType = reuseSource ? _geometryVectorType : AddStruct("UnityEngine.Vector3", 12, new("x", "System.Single", 0), new("y", "System.Single", 4), new("z", "System.Single", 8));
        _rendererQuaternionType = reuseSource ? _geometryQuaternionType : AddStruct("UnityEngine.Quaternion", 16, new("x", "System.Single", 0), new("y", "System.Single", 4), new("z", "System.Single", 8), new("w", "System.Single", 12));
        _rendererAppear = GameplayMethodAdd(SeekType("InLevel.NoteInformation"), "Appear", false, Void);
        _rendererDisappear = GameplayMethodAdd(SeekType("InLevel.NoteInformation"), "Disappear", false, Void);
        _rendererComponentTransform = reuseSource ? _geometryMethods["get_transform"] : GameplayMethodAdd(_rendererComponentType, "get_transform", false, _rendererTransformType);
        _rendererParent = reuseSource ? _geometryMethods["get_parent"] : GameplayMethodAdd(_rendererTransformType, "get_parent", false, _rendererTransformType);
        _rendererPosition = reuseSource ? _geometryMethods["get_localPosition"] : GameplayMethodAdd(_rendererTransformType, "get_localPosition", false, _rendererVectorType);
        _rendererRotation = reuseSource ? _geometryMethods["get_localRotation"] : GameplayMethodAdd(_rendererTransformType, "get_localRotation", false, _rendererQuaternionType);
        _rendererScale = reuseSource ? _geometryMethods["get_localScale"] : GameplayMethodAdd(_rendererTransformType, "get_localScale", false, _rendererVectorType);
        var pool = SeekObject(_rendererPoolType); RendererPools.Add(pool); _rendererTransforms[pool] = RendererNewTransform(IntPtr.Zero, false);
        var prefab = RendererNewChild(pool, prefab: true);
        var free = Enumerable.Range(0, shape == RendererShape.Growth ? 0 : available).Select(_ => RendererNewChild(pool)).ToList();
        for (var note = 0; note < RendererNotes.Count; ++note)
        {
            var active = shape is RendererShape.Active or RendererShape.Dirty || shape == RendererShape.Mixed && note % 2 != 0;
            var child = active ? RendererNewChild(pool, dirty: shape == RendererShape.Dirty) : IntPtr.Zero;
            var plan = SeekObject(_rendererType); RendererPlans.Add(plan); var target = RendererNewTransform(IntPtr.Zero, false);
            RendererField("renderer.plan." + note + ".index", _rendererType, "poolIndex", Int32, plan, note, 0x10);
            RendererField("renderer.plan." + note + ".pool", _rendererType, "pool", _rendererPoolType, plan, pool, 0x18);
            RendererField("renderer.plan." + note + ".child", _rendererType, "poolChild", _rendererChildType, plan, child, 0x20);
            RendererField("renderer.plan." + note + ".parent", _rendererType, "poolChildTransformPlan", _rendererTransformType, plan, target, 0x28);
            RendererField("renderer.plan." + note + ".active", _rendererType, "active", Boolean, plan, active, 0x30);
            if (active) _rendererTrs[_rendererTransforms[child]] = new(target, new(7, 8, 9), new(.1f, .2f, .3f, .4f), new(2, 3, 4));
            RendererField("renderer.note." + note + ".plans", SeekType("InLevel.NoteInformation"), "noteRenderers", _rendererArrayType,
                RendererNotes[note], RendererArray(_rendererArrayType, new[] { plan }), 0x60);
            SeekField("renderer.note." + note + ".index", SeekType("InLevel.NoteInformation"), "_index", Int32, RendererNotes[note], int.MinValue + note, 0x68);
        }
        var tails = free.Count == 0 ? Array.Empty<IntPtr>() : new[] { IntPtr.Zero, free[0], free[0] };
        var availableList = RendererNewList(free.Count, int.MaxValue, free.Concat(tails).ToArray());
        var dirtyList = RendererNewList(0, int.MinValue, Array.Empty<IntPtr>());
        RendererField("renderer.pool.prefab", _rendererPoolType, "easyPoolChild", _rendererChildType, pool, prefab, 0x20);
        RendererField("renderer.pool.size", _rendererPoolType, "poolSize", Int32, pool, RendererChildren.Count - 1, 0x28);
        RendererField("renderer.pool.count", _rendererPoolType, "_childCount", Int32, pool, free.Count, 0x2c);
        RendererField("renderer.pool.available", _rendererPoolType, "_childList", _rendererListType, pool, availableList, 0x30);
        RendererField("renderer.pool.dirty", _rendererPoolType, "_dirtyChildList", _rendererListType, pool, dirtyList, 0x38);
        ConfigureRendererExports(); GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
        RendererProfile = new(this); RendererRememberOtherStorage();
    }
    private void RendererField(string key, IntPtr type, string name, IntPtr fieldType, IntPtr owner, object value, int offset)
    { SeekField(key, type, name, fieldType, owner, value, offset); _rendererStorageFields.Add(_gameplayFields[key].Field); }
    private IntPtr RendererNewTransform(IntPtr parent, bool pooled)
    {
        var transform = SeekObject(_rendererTransformType);
        _rendererTrs.Add(transform, new(parent, new(1, 2, 3), new(.1f, .2f, .3f, .4f), pooled ? new(0, 0, 0) : new(2, 3, 4))); return transform;
    }
    private IntPtr RendererNewChild(IntPtr pool, bool prefab = false, bool dirty = false)
    {
        var child = SeekObject(_rendererChildType); RendererChildren.Add(child); var component = SeekObject(_rendererSpriteType);
        _rendererTransforms[child] = RendererNewTransform(_rendererTransforms[pool], true); _rendererTransforms[component] = _rendererTransforms[child];
        var key = "renderer.child." + RendererChildren.Count;
        RendererField(key + ".component", _rendererChildType, "targetComponent", _rendererComponentType, child, component, 0x20);
        RendererField(key + ".pool", _rendererChildType, "parentPool", _rendererPoolType, child, pool, 0x28);
        RendererField(key + ".dirty", _rendererChildType, "dirty", Boolean, child, dirty, 0x38);
        if (RendererInverseEnabled) RendererAttachGameObject(child);
        return child;
    }
    private IntPtr RendererArray(IntPtr type, IntPtr[] slots)
    {
        var array = GameplayAllocate(checked(ArrayHeaderBytes + slots.Length * IntPtr.Size));
        for (var index = 0; index < ArrayHeaderBytes; ++index) Marshal.WriteByte(array, index, 0);
        Marshal.WriteIntPtr(array, ClassOf(type)); _boxedTypes[array] = type; _rendererArrayTypes[array] = type; _rendererArrays[array] = slots.ToArray();
        for (var index = 0; index < slots.Length; ++index) Marshal.WriteIntPtr(array, ArrayHeaderBytes + index * IntPtr.Size, slots[index]); return array;
    }
    private IntPtr RendererNewList(int size, int version, IntPtr[] slots)
    {
        var list = SeekObject(_rendererListType); var key = "renderer.list." + _gameplayFields.Count;
        RendererField(key + ".items", _rendererListType, "_items", _rendererChildArrayType, list, RendererArray(_rendererChildArrayType, slots), 0x10);
        RendererField(key + ".size", _rendererListType, "_size", Int32, list, size, 0x18);
        RendererField(key + ".version", _rendererListType, "_version", Int32, list, version, 0x1c); return list;
    }
    private IntPtr RendererStorageField(IntPtr owner, string name) => _structFields[ObjectClass(owner)].Single(field => _fieldMetadata[field].Name == name);
    internal IntPtr RendererReference(IntPtr owner, string name) => new(BitConverter.ToInt64(_gameplayStorage[(owner, RendererStorageField(owner, name))]));
    internal int RendererInteger(IntPtr owner, string name) => BitConverter.ToInt32(_gameplayStorage[(owner, RendererStorageField(owner, name))]);
    internal bool RendererBoolean(IntPtr owner, string name) => _gameplayStorage[(owner, RendererStorageField(owner, name))][0] != 0;
    internal void RendererSet(IntPtr owner, string name, object value) => _gameplayStorage[(owner, RendererStorageField(owner, name))] = value switch
    { int integer => BitConverter.GetBytes(integer), IntPtr reference => BitConverter.GetBytes(reference.ToInt64()), bool boolean => new[] { boolean ? (byte)1 : (byte)0 }, _ => throw new ArgumentException("Renderer storage value.") };
    internal IntPtr[] RendererSlots(IntPtr array) => _rendererArrays[array].ToArray();
    internal void RendererSetSlot(IntPtr array, int slot, IntPtr value)
    { _rendererArrays[array][slot] = value; Marshal.WriteIntPtr(array, ArrayHeaderBytes + slot * IntPtr.Size, value); }
    internal RendererPoolList RendererList(IntPtr list) => new(list, RendererReference(list, "_items"), RendererInteger(list, "_size"),
        RendererInteger(list, "_version"), Array.AsReadOnly(RendererSlots(RendererReference(list, "_items"))));
    internal RendererTrs RendererTransform(IntPtr component) => _rendererTrs[_rendererTransforms.TryGetValue(component, out var transform) ? transform : component];
    internal IntPtr RendererChildTransform(IntPtr child) => _rendererTransforms[child];
    internal void RendererReplaceBacking(IntPtr list, int capacity)
    {
        var old = RendererList(list); var values = new IntPtr[capacity]; Array.Copy(old.Capacity.ToArray(), values, Math.Min(old.Capacity.Count, capacity));
        RendererSet(list, "_items", RendererArray(_rendererChildArrayType, values));
    }
    internal void RendererRememberOtherStorage()
    {
        _rendererOtherStorage.Clear(); foreach (var row in _gameplayStorage)
            if (!_rendererStorageFields.Contains(row.Key.Field)) _rendererOtherStorage[row.Key] = row.Value.ToArray();
    }
    internal bool RendererOtherStorageUnchanged() => _rendererOtherStorage.All(row => _gameplayStorage.TryGetValue(row.Key, out var value) && value.SequenceEqual(row.Value));
}
