using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

internal interface ISeekLineSetPositionsNativeProfile
{
    IntPtr LineClass { get; }
    IntPtr Vector3ArrayClass { get; }
    IntPtr SetPositionsMethod { get; }
    void VerifyCurrent(Action guard);
    void InvokeExact(NativeRuntime.RootHandle line, NativeRuntime.RootHandle scratch,
        int count, Action guard, Action verifySource, Action beforeEntry);
}

/// <summary>Exact LineInformation.SetPositions(Vector3[],Int32) declaration,
/// complete 288-byte body pin and guarded runtime-invoke transport. The profile
/// does not own the original line/scratch roots, produce values or close Unity
/// consumer effects. A later command owner supplies its source guard/journal.</summary>
internal sealed class NativeSeekLineSetPositionsNativeProfile : ISeekLineSetPositionsNativeProfile
{
    internal const string Fingerprint = GameplayClockNativeProfile.Fingerprint;
    internal const string FrozenBodySha256 = "e948e88fdbba2a25281ae2fb8ced224a3b9197915714dfef210fdc19e86fb8e6";
    internal const bool DirectGeneratedAbiDispatch = false;
    internal const bool RuntimeEffectsVerified = false;
    internal const bool UnityConsumerClosureKnown = false;
    internal const bool CompleteSeekPermitted = false;
    internal const string Evidence = "Pinned GameAssembly " + Fingerprint
        + "; exact current LineInformation.SetPositions(Vector3[],Int32) MethodInfo2022/entry"
        + " and complete 288-byte body SHA, canonical rank-one twelve-byte Vector3[] and Int32 ABI."
        + " Dispatch uses il2cpp_runtime_invoke with managed exception out pointer and retained roots."
        + " Producer, saved-field readback, Unity effects and complete Seek remain open.";

    private readonly NativeRuntime _api;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly IntPtr _module;
    private readonly string _path;
    private readonly NativeLineSetPositionsSignature _signature;
    private int _checking, _faulted;
    public IntPtr LineClass => _signature.LineClass;
    public IntPtr Vector3ArrayClass => _signature.ArrayClass;
    public IntPtr SetPositionsMethod => _signature.Method.MethodInfo;

    internal NativeSeekLineSetPositionsNativeProfile(NativeRuntime api, IntPtr originalLineClass)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new UnsupportedSignatureException("SetPositions evidence requires the pinned Windows x64 image.");
        _module = GetModuleHandle("GameAssembly.dll");
        if (_module == IntPtr.Zero) throw new UnsupportedSignatureException("The exact SetPositions image is absent.");
        var path = new StringBuilder(32768);
        var count = GetModuleFileName(_module, path, path.Capacity);
        if (count == 0 || count >= path.Capacity)
            throw new UnsupportedSignatureException("The mapped SetPositions image path is unavailable.");
        _path = path.ToString();
        using (var file = File.OpenRead(_path))
        using (var hash = SHA256.Create())
            if (!Convert.ToHexString(hash.ComputeHash(file)).Equals(Fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The SetPositions image differs from the pinned source evidence.");
        _signature = _api.InspectLineSetPositionsSignature(originalLineClass, _module, () => { });
        VerifyCurrent(() => { });
    }

    public void VerifyCurrent(Action guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("SetPositions profile belongs to its owner thread.");
        var guardFailed = false;
        void Guard()
        {
            try { guard(); }
            catch { guardFailed = true; throw; }
        }
        if (Volatile.Read(ref _faulted) != 0)
            throw new UnsupportedSignatureException("The SetPositions profile has been withdrawn.");
        if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0)
            throw new InvalidOperationException("SetPositions profile verification cannot reenter.");
        try
        {
            // Reserve before caller callbacks: a guard can synchronously
            // reenter verification or withdraw its source lifetime.
            Guard();
            VerifyModule(Guard);
            var current = _api.InspectLineSetPositionsSignature(LineClass, _module, Guard);
            if (!Same(_signature, current))
                throw new UnsupportedSignatureException("The exact SetPositions MethodInfo or ABI changed.");
            var address = IntPtr.Add(_module, NativeRuntime.ExactLineSetPositionsRva);
            Guard(); HookTargetValidation.VerifyExecutable(address, _module); Guard();
            var bytes = new byte[NativeRuntime.ExactLineSetPositionsBodyLength];
            Guard(); var process = GetCurrentProcess(); Guard();
            if (!ReadProcessMemory(process, address, bytes, (nuint)bytes.Length, out var read)
                || read != (nuint)bytes.Length)
                throw new UnsupportedSignatureException("The complete SetPositions body is not readable from the mapped image.");
            Guard();
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(FrozenBodySha256, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The complete two-argument SetPositions body differs from frozen evidence.");
            VerifyModule(Guard);
        }
        catch { if (!guardFailed) Interlocked.Exchange(ref _faulted, 1); throw; }
        finally { Volatile.Write(ref _checking, 0); }
    }

    public void InvokeExact(NativeRuntime.RootHandle line, NativeRuntime.RootHandle scratch,
        int count, Action guard, Action verifySource, Action beforeEntry)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(verifySource);
        ArgumentNullException.ThrowIfNull(beforeEntry);
        _api.InvokeLineSetPositionsWithPublicationAdmission(_signature, line, scratch, count,
            guard, () => VerifyCurrent(guard), verifySource, beforeEntry);
    }

    private void VerifyModule(Action guard)
    {
        guard(); var byName = GetModuleHandle("GameAssembly.dll"); guard();
        var byPath = GetModuleHandle(_path); guard();
        if (byName != _module || byPath != _module)
            throw new UnsupportedSignatureException("The SetPositions module changed or was unloaded.");
    }

    private static bool Same(NativeLineSetPositionsSignature left, NativeLineSetPositionsSignature right)
    {
        var a = left.Method; var b = right.Method;
        return left.LineClass == right.LineClass && left.ArrayClass == right.ArrayClass
            && left.VectorClass == right.VectorClass && left.CountClass == right.CountClass
            && left.Module == right.Module && a.MethodInfo == b.MethodInfo
            && a.DeclaringClass == b.DeclaringClass && a.Name == b.Name && a.ReturnType == b.ReturnType
            && a.ParameterTypes.SequenceEqual(b.ParameterTypes) && a.Flags == b.Flags
            && a.ImplementationFlags == b.ImplementationFlags && a.GenericMethod == b.GenericMethod
            && a.InflatedMethod == b.InflatedMethod && a.Entry == b.Entry;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module, StringBuilder path, int size);
    [DllImport("kernel32")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address,
        [Out] byte[] bytes, nuint length, out nuint read);
}
