using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalRunTerminationBoundaryBackend : INativeLocalInitializationSettingsBackend
{
    private IntPtr _mappedSpeedGetter, _offsetMillisecondsGetter;
    private float _offsetMillisecondsDivisor;
    private bool _settingsLayoutVerified;
    // Resolve code/layout authority before native callbacks are installed. This
    // performs no settings getter invocation and never writes play parameters.
    internal void PrepareInitializationSettings()
        => Observe(() => { ResolveInitializationSettings(); return true; });
    public InactiveControllerParameterInputs ReadInitializationParameterState(IDisposable ownedBundle)
        => Observe(() => {
            if (ownedBundle is not OwnedNativeLocalPlayBundle bundle)
                throw new InvalidOperationException("Settings verification requires the exact production-owned bundle.");
            var value = bundle.Read(); return new InactiveControllerParameterInputs(value.ParametersIdentity, value.Parameters);
        });
    public NativeLocalInitializationSettings ReadInitializationSettings()
        => Observe(() => {
            ResolveInitializationSettings();
            return new NativeLocalInitializationSettings(
                _api.InvokeValueWithPublication<float>(_mappedSpeedGetter, IntPtr.Zero),
                _api.InvokeValueWithPublication<float>(_offsetMillisecondsGetter, IntPtr.Zero),
                _offsetMillisecondsDivisor, _mappedSpeedGetter.ToInt64(), _offsetMillisecondsGetter.ToInt64());
        });
    private void ResolveInitializationSettings()
    {
        if (_settingsLayoutVerified) return;
        // Fixed SHA sample 853d...b79: these bounded functions read SettingsControl
        // static state and compute/return Single values. They never receive or
        // write InsideLevelParams. Metadata/cctor initialization remains stock.
        // These hashes cover 390A80..390AF0 and 390B90..390BE0, respectively.
        VerifySettingsCode(0x390A80, 0x70, "fc779841b00bea882de3f3f70815962986d740e16dd0a17e01d5ba4bad45cbc4");
        VerifySettingsCode(0x390B90, 0x50, "360a26f669bdf4670cc88c4055e403e575e55579c57081eb269246aef2939206");
        var constant = IntPtr.Add(_module, 0x17C4C50);
        HookTargetValidation.VerifyReadable(constant, 4);
        var divisorBits = Marshal.ReadInt32(constant);
        if (divisorBits != NativeLocalInitializationSettingsReceipt.OffsetDivisorBits)
            throw new UnsupportedSignatureException("The native Single millisecond divisor differs from pinned bytes 00 00 7A 44.");
        _offsetMillisecondsDivisor = BitConverter.Int32BitsToSingle(divisorBits);
        var settings = _api.Class("Assembly-CSharp.dll", "", "SettingsControl");
        _mappedSpeedGetter = ResolveGetter(settings, "get_MappedSpeed", 0x390A80);
        // Exact native body reads the same SettingsControl static offset(+14)
        // consumed at stock getter 3BF12F, whose 3BF134 DIVSS reads 17C4C50.
        _offsetMillisecondsGetter = ResolveGetter(settings, "get_OffsetInMillisecond", 0x390B90);
        _settingsLayoutVerified = true;
    }
    private IntPtr ResolveGetter(IntPtr settings, string name, int rva)
    {
        var method = _api.InspectDeclaredStaticMethod(settings, name, Array.Empty<string>(), "System.Single", _module);
        if (method.Entry?.Rva != rva || (method.Flags & (0x10 | 0x400)) != 0x10 || method.GenericMethod || method.InflatedMethod)
            throw new UnsupportedSignatureException("The actual settings source differs from the pinned declared static Single getter.");
        return method.MethodInfo;
    }
    private void VerifySettingsCode(int rva, int size, string expectedHash)
    {
        var address = IntPtr.Add(_module, rva); HookTargetValidation.VerifyExecutable(address, _module);
        HookTargetValidation.VerifyReadable(address, (nuint)size); var bytes = new byte[size]; Marshal.Copy(address, bytes, 0, size);
        using var sha = SHA256.Create();
        if (!string.Equals(Convert.ToHexString(sha.ComputeHash(bytes)), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("The settings getter read-only body differs from its pinned finite native range.");
    }
}
