using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

internal interface ISeekNoteVisibilityNativeProfile
{
    ISeekRendererNativeProfile Renderer { get; }
    ISeekHoldGeometryProfile Geometry { get; }
    IntPtr NoteControlClass { get; }
    IntPtr ActiveNotesMethod { get; }
    IntPtr SetNotesMethod { get; }
    void VerifyCurrent();
}

/// <summary>Reuses the pinned renderer and complete SetNotes/geometry profiles.
/// ActiveNotes is the real floor/line visibility producer, not a time partition.</summary>
internal sealed class NativeSeekNoteVisibilityProfile : ISeekNoteVisibilityNativeProfile
{
    private readonly NativeRuntime _api;
    private readonly IntPtr _module;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly NativeMetadataMethod _active, _set;
    private bool _checking, _faulted;
    public ISeekRendererNativeProfile Renderer { get; }
    public ISeekHoldGeometryProfile Geometry { get; }
    public IntPtr NoteControlClass { get; }
    public IntPtr ActiveNotesMethod => _active.MethodInfo;
    public IntPtr SetNotesMethod => _set.MethodInfo;

    internal NativeSeekNoteVisibilityProfile(NativeRuntime api, ISeekRendererNativeProfile renderer,
        ISeekHoldGeometryProfile geometry)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        _module = GetModuleHandle("GameAssembly.dll");
        if (_module == IntPtr.Zero || IntPtr.Size != 8 || !OperatingSystem.IsWindows())
            throw new UnsupportedSignatureException("Note visibility requires the pinned Windows x64 image.");
        renderer.VerifyCurrent(); geometry.VerifyCurrent();
        if (renderer.NoteClass != geometry.NoteClass || renderer.ChartClass != geometry.ChartClass
            || renderer.ProgressClass != geometry.ProgressClass)
            throw new UnsupportedSignatureException("Visibility profiles do not name the same actual source classes.");
        NoteControlClass = api.Class("Assembly-CSharp.dll", "InLevel", "NoteControl");
        _active = Bind("ActiveNotes", 0x3fc390); _set = Bind("SetNotes", 0x3fc620);
        VerifyCurrent();
    }
    private NativeMetadataMethod Bind(string name, int rva)
    {
        var method = _api.InspectDeclaredMethod(NoteControlClass, name, Array.Empty<string>(), "System.Void", _module);
        if (method.Entry?.Rva != rva || method.GenericMethod || method.InflatedMethod || (method.Flags & 0x10) != 0)
            throw new UnsupportedSignatureException("Visibility producer has another exact method: " + name);
        return method;
    }
    public void VerifyCurrent()
    {
        if (_thread != Environment.CurrentManagedThreadId || _checking || _faulted)
            throw new InvalidOperationException("The visibility profile is withdrawn, reentered or on another thread.");
        _checking = true;
        try
        {
            Renderer.VerifyCurrent(); Geometry.VerifyCurrent();
            if (GetModuleHandle("GameAssembly.dll") != _module)
                throw new UnsupportedSignatureException("The visibility module changed.");
            foreach (var expected in new[] { _active, _set })
            {
                var actual = Bind(expected.Name, checked((int)expected.Entry!.Rva));
                if (actual.MethodInfo != expected.MethodInfo || actual.Entry != expected.Entry
                    || actual.DeclaringClass != expected.DeclaringClass || actual.Flags != expected.Flags
                    || actual.ImplementationFlags != expected.ImplementationFlags)
                    throw new UnsupportedSignatureException("Visibility producer metadata changed.");
            }
            // Complete bytes reconstructed from the stored static-evidence.json instructions.
            var address = IntPtr.Add(_module, 0x3fc390);
            HookTargetValidation.VerifyExecutable(address, _module);
            HookTargetValidation.VerifyReadable(address, 656);
            var bytes = new byte[656]; Marshal.Copy(address, bytes, 0, bytes.Length);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(
                "0d4cd3797b9450ed2872ff052b1c63184977f00c26593e35c0dbf0be728680df", StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The complete ActiveNotes body changed.");
            var distance = IntPtr.Add(_module, 0x17c4e30);
            HookTargetValidation.VerifyReadable(distance, 4);
            if (Marshal.ReadInt32(distance) != BitConverter.SingleToInt32Bits(8f))
                throw new UnsupportedSignatureException("The actual ActiveNotes distance constant changed.");
        }
        catch { _faulted = true; throw; }
        finally { _checking = false; }
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
