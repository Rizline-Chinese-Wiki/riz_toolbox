using System.Runtime.InteropServices;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

// The installed path always constructs NativeSeekHoldEpochProfile below.
// A controlled implementation lets offline tests run the original epoch and
// observer callbacks without executing a mapped game method or claiming its ABI.
internal interface ISeekHoldEpochProfile
{
    IntPtr IteratorClass { get; }
    IntPtr FactoryMethod { get; }
    IntPtr StartMethod { get; }
    IntPtr StopMethod { get; }
    void VerifyCurrent();
}

/// <summary>Exact existing factory/Start/Stop metadata and the retained original
/// Move body. Factory metadata and returned fields are checked independently;
/// no previously unrecorded factory-body hash or engine-detach proof is invented.</summary>
internal sealed class NativeSeekHoldEpochProfile : ISeekHoldEpochProfile
{
    private readonly NativeRuntime _api;
    private readonly ISeekTargetNativeProfile _source;
    private readonly JudgementModule.SeekBackendAccess _observer;
    private readonly IntPtr _module;
    private readonly NativeMetadataMethod _factory, _start, _stop;
    internal IntPtr IteratorClass { get; }
    internal IntPtr FactoryMethod => _factory.MethodInfo;
    internal IntPtr StartMethod => _start.MethodInfo;
    internal IntPtr StopMethod => _stop.MethodInfo;
    IntPtr ISeekHoldEpochProfile.IteratorClass => IteratorClass;
    IntPtr ISeekHoldEpochProfile.FactoryMethod => FactoryMethod;
    IntPtr ISeekHoldEpochProfile.StartMethod => StartMethod;
    IntPtr ISeekHoldEpochProfile.StopMethod => StopMethod;
    void ISeekHoldEpochProfile.VerifyCurrent() => VerifyCurrent();
    internal NativeSeekHoldEpochProfile(NativeRuntime api, ISeekTargetNativeProfile source,
        JudgementModule.SeekBackendAccess observer)
    {
        _api = api; _source = source; _observer = observer;
        _module = GetModuleHandle("GameAssembly.dll");
        if (_module == IntPtr.Zero) throw new UnsupportedSignatureException("The actual Hold source image is absent.");
        source.VerifyCurrent(); observer.VerifyDrained();
        IteratorClass = api.NestedClass(source.JudgeClass, "<CheckHold>d__32");
        var mono = api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "MonoBehaviour");
        _factory = Bind(source.JudgeClass, "CheckHold", "System.Collections.IEnumerator", 0x3fd010,
            "InLevel.NoteInformation", "System.Boolean");
        _start = Bind(mono, "StartCoroutine", "UnityEngine.Coroutine", 0x153d380, "System.Collections.IEnumerator");
        _stop = Bind(mono, "StopAllCoroutines", "System.Void", 0x153d6c0);
        foreach (var field in Fields)
        {
            var actual = api.InspectExactDeclaredReadField(IteratorClass, field.Name, field.Type, false, field.Offset);
            if ((actual.Flags & (0x20u | 0x40u)) != 0) throw new UnsupportedSignatureException("A Hold iterator field is not mutable.");
        }
        VerifyCurrent();
    }
    internal static readonly (string Name, string Type, int Offset)[] Fields =
    {
        ("<>1__state", "System.Int32", 0x10), ("<>2__current", "System.Object", 0x18),
        ("<>4__this", "InLevel.NoteJudgeControl", 0x20), ("holdNoteInformation", "InLevel.NoteInformation", 0x28),
        ("isPressLeft", "System.Boolean", 0x30), ("<isNormalPerfect>5__2", "System.Boolean", 0x31),
        ("<startTime>5__3", "System.Single", 0x34), ("<nextParticleTime>5__4", "System.Single", 0x38)
    };
    private NativeMetadataMethod Bind(IntPtr cls, string name, string returns, int rva, params string[] parameters)
    {
        var value = _api.InspectDeclaredMethod(cls, name, parameters, returns, _module);
        if (value.Entry?.Rva != rva || value.GenericMethod || value.InflatedMethod || (value.Flags & 0x10) != 0)
            throw new UnsupportedSignatureException("The actual Hold method differs: " + name);
        return value;
    }
    internal void VerifyCurrent()
    {
        _source.VerifyCurrent(); _observer.VerifyCurrent();
        if (GetModuleHandle("GameAssembly.dll") != _module || _observer.StartCoroutineBinding.TargetAddress != _start.Entry!.Address
            || _observer.StopAllBinding.TargetAddress != _stop.Entry!.Address
            || _observer.HoldMoveBinding.TargetAddress != IntPtr.Add(_module, 0x402670))
            throw new UnsupportedSignatureException("The actual Hold methods do not share the installed observer bindings.");
        foreach (var method in new[] { _factory, _start, _stop })
        {
            var actual = Bind(method.DeclaringClass.Class, method.Name, method.ReturnType, checked((int)method.Entry!.Rva), method.ParameterTypes.ToArray());
            if (actual.MethodInfo != method.MethodInfo || actual.Entry != method.Entry || actual.Flags != method.Flags
                || actual.ImplementationFlags != method.ImplementationFlags)
                throw new UnsupportedSignatureException("The prepared Hold method metadata changed.");
        }
        _observer.HoldMoveBinding.VerifyOriginalBody(_module, IntPtr.Add(_module, 0x402670), 3376,
            "ec99af25a26804583a58aa67a9811f2cef628c1d8b81e3bef3683a2c6f86ffe1");
        _observer.StartCoroutineBinding.VerifyOriginalBody(_module, IntPtr.Add(_module, 0x153d380), 272,
            "e24c0c93ed499166a8e93f9b8cd76b70eb1f7a3f1ad5d9c7f738ba93a3c19be4");
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
