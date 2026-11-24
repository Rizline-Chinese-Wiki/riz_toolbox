using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Observe each already configured official export after its real synthetic
// delegate returns. This keeps every native output, root and exception path
// in the fixture that the production Runtime was built to use.
sealed partial class FakeNative
{
    internal readonly List<string> HoldSourceGuardExportLog = new();
    internal Action<string>? AfterHoldSourceGuardExport;
    private readonly Dictionary<IntPtr, IntPtr> _holdSourceClassImages = new();
    private readonly Dictionary<IntPtr, uint> _holdSourceInstanceMinimums = new();

    private void ObserveHoldSourceGuardExport(string name)
    {
        HoldSourceGuardExportLog.Add(name);
        AfterHoldSourceGuardExport?.Invoke(name);
    }

    private void ConfigureHoldSourceGuardExports()
    {
        ConfigureHoldSourceGuardMetadata();
        var observeMethod = GetType().GetMethod(nameof(ObserveHoldSourceGuardExport),
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var row in _exports.ToArray())
        {
            var delegateType = row.Value.GetType();
            var invoke = delegateType.GetMethod("Invoke")!;
            var arguments = invoke.GetParameters()
                .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name)).ToArray();
            var original = Expression.Invoke(Expression.Constant(row.Value, delegateType), arguments);
            var observe = Expression.Call(Expression.Constant(this), observeMethod, Expression.Constant(row.Key));
            Expression body;
            if (invoke.ReturnType == typeof(void))
            {
                body = Expression.Block(original, observe);
            }
            else
            {
                var result = Expression.Variable(invoke.ReturnType, "result");
                body = Expression.Block(new[] { result }, Expression.Assign(result, original), observe, result);
            }
            _exports[row.Key] = Expression.Lambda(delegateType, body, arguments).Compile();
        }
    }

    private void ConfigureHoldSourceGuardMetadata()
    {
        var gameImage = _gameplayImages.Single(row => row.Value == "Assembly-CSharp.dll").Key;
        var coreImage = _gameplayImages.Single(row => row.Value == "UnityEngine.CoreModule.dll").Key;
        void Register(IntPtr cls, IntPtr image, uint minimum)
        {
            _holdSourceClassImages.Add(cls, image);
            _holdSourceInstanceMinimums.Add(cls, minimum);
        }

        // Exact class handles are captured after the graph is built. A later
        // same-named foreign class cannot acquire an image or storage bound.
        Register(ClassOf(_seekCoroutineType), gameImage, 0x30);
        Register(_holdJudgeClass, gameImage, 0x28);
        Register(ClassOf(SeekType("InLevel.NoteInformation")), gameImage, 0x58);
        Register(ClassOf(SeekType("InLevel.LevelInformation")), gameImage, 0x60);
        Register(GameplayControllerClass, gameImage, 0x38);
        Register(GameplayControllerBaseClass, gameImage, 0x38);
        Register(_holdUnityObjectClass, coreImage, 0x18);
        Register(_holdComponentClass, coreImage, 0x18);
        Register(_holdBehaviourClass, coreImage, 0x18);
        Register(_holdMonoBehaviourClass, coreImage, 0x18);
        Register(ClassOf(SeekType("UnityEngine.Coroutine")), coreImage, 0x10);

        Add("il2cpp_class_get_nested_types", "Ptr2", nameof(HoldSourceNested));
        Add("il2cpp_class_get_image", "Ptr1", nameof(HoldSourceClassImage));
        Add("il2cpp_class_instance_size", "ArraySize", nameof(HoldSourceInstanceSize));
    }

    internal void AddHoldControllerInformationRedeclaration(bool onDerivedClass)
    {
        const string key = "hold.controller.redeclared";
        var type = onDerivedClass ? _gameplayControllerType : _gameplayControllerBaseType;
        var info = SeekType("InLevel.LevelInformation");
        GameplayField(key, type, "LevelInformation", info, GameplayController, GameplayInformation);
        _fieldMetadata[_gameplayFields[key].Field] = ("LevelInformation", info, 0x28);
    }

    internal void ReplaceHoldControllerParent() => _gameplayControllerParent = _holdMonoBehaviourClass;

    internal IntPtr CreateHoldControllerBaseObject() => SeekObject(_gameplayControllerBaseType);

    private IntPtr HoldSourceNested(IntPtr parent, IntPtr iterator)
        => parent == _holdJudgeClass ? SeekNested(parent, iterator) : IntPtr.Zero;

    private IntPtr HoldSourceClassImage(IntPtr cls)
        => _holdSourceClassImages.TryGetValue(cls, out var image) ? image : IntPtr.Zero;

    private uint HoldSourceInstanceSize(IntPtr cls)
    {
        if (!_holdSourceInstanceMinimums.TryGetValue(cls, out var minimum)) return 0;
        long required = minimum;
        if (_structFields.TryGetValue(cls, out var fields))
            foreach (var field in fields)
            {
                var metadata = _fieldMetadata[field];
                if (metadata.Offset < 0) continue;
                var type = _types[metadata.Type];
                var width = type.ValueType ? Math.Max(1, type.Size) : IntPtr.Size;
                required = Math.Max(required, (long)metadata.Offset + width);
            }
        var alignment = IntPtr.Size;
        return checked((uint)((required + alignment - 1) / alignment * alignment));
    }
}
