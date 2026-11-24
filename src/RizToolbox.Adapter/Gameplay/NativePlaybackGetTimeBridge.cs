using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>The exact instance Single() return boundary, expressed as raw return bits. This is
/// deliberately distinct from OperationSingle1Void (void(Single)). The actual native broker must
/// call it only after the original completed normally, with its private exact-run authority.</summary>
internal sealed class NativePlaybackGetTimeBridge
{
    private readonly NativePlaybackClock _clock;
    private readonly NativePlaybackBinding _binding;
    private readonly NativePlaybackGetTimeCallProfile? _callProfile;

    internal NativePlaybackGetTimeBridge(NativePlaybackClock clock, NativePlaybackBinding binding,
        NativePlaybackGetTimeCallProfile? callProfile = null)
    {
        if (!binding.Valid || clock.Capture().Binding != binding
            || callProfile is not null && callProfile.MethodInfo.ToInt64() != binding.GetTimeMethod)
            throw new ArgumentException("The GetTime bridge requires the clock's exact source and run identity.", nameof(binding));
        _clock = clock;
        _binding = binding;
        _callProfile = callProfile;
    }

    internal bool TryReplace(NativePlaybackBinding currentBinding, long receiver, long methodInfo,
        bool originalCompleted, uint originalReturnBits, double dspTimeServiceSeconds, out uint returnBits)
        => TryReplace(currentBinding, receiver, methodInfo, 0, originalCompleted, originalReturnBits, dspTimeServiceSeconds, out returnBits);

    internal bool TryReplace(NativePlaybackBinding currentBinding, long receiver, long methodInfo, long callerReturnAddress,
        bool originalCompleted, uint originalReturnBits, double dspTimeServiceSeconds, out uint returnBits)
    {
        returnBits = originalReturnBits;
        if (!originalCompleted || currentBinding != _binding || receiver != _binding.Player)
            return false;
        if (_callProfile is not null)
        {
            try { if (!_callProfile.AllowsCall(methodInfo, callerReturnAddress)) return false; }
            catch (Exception error)
            {
                _clock.Withdraw("The fixed GetTime caller profile became unavailable: " + error.Message);
                return false;
            }
        }
        else if (methodInfo != _binding.GetTimeMethod) return false;
        if (!float.IsFinite(BitConverter.Int32BitsToSingle(unchecked((int)originalReturnBits))))
        {
            _clock.Withdraw("The exact original GetTime returned a nonfinite Single value.");
            return false;
        }
        if (!_clock.TryGetTime(currentBinding, dspTimeServiceSeconds, out var mediaSeconds)) return false;
        returnBits = unchecked((uint)BitConverter.SingleToInt32Bits(mediaSeconds));
        return true;
    }
}

/// <summary>The one statically proved null-MethodInfo consumer. Progress.Update clears EDX at
/// 0x401216 and calls GetTime at 0x401218, returning to 0x40121d. The resolved metadata method
/// remains part of the rooted binding; incoming RDX stays null when passed to the original.</summary>
internal sealed class NativePlaybackGetTimeCallProfile
{
    internal const int ProgressUpdateCallRva = 0x401218;
    internal const int ProgressUpdateReturnRva = 0x40121d;
    private readonly IntPtr _module;
    private readonly GameplayClockNativeProfile _profile;
    private readonly NativePlaybackGetTimeCallProfile _registered;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    internal IntPtr MethodInfo => _profile.GetTimeMethodInfo;
    internal NativePlaybackGetTimeCallProfile(GameplayClockNativeProfile profile)
    {
        // Capture before installing the GetTime return detour. At callback time the original
        // GetTime head is intentionally patched and is instead owned by the exact hook broker.
        profile.VerifyCurrent();
        _profile = profile;
        _module = profile.ModuleBase;
        _registered = this;
    }
    internal bool AllowsNull(long callerReturnAddress)
        => AllowsCall(0, callerReturnAddress);
    internal bool AllowsCall(long methodInfo, long callerReturnAddress)
    {
        VerifyRegistration();
        if (methodInfo != MethodInfo.ToInt64()
            && (methodInfo != 0 || !MatchesPinnedProgressReturn(_module.ToInt64(), callerReturnAddress))) return false;
        // First admission and the standalone return bridge retain the complete
        // consumer audit. Subsequent source reads check the attached lease below.
        _profile.VerifyCurrent();
        VerifyRegistration();
        return true;
    }
    internal void VerifyPreparedSource(NativePlaybackGetTimeHookLease expectedHook)
    {
        VerifyOperation(expectedHook);
        _profile.VerifyReadOnlyBodies();
        VerifyOperation(expectedHook);
    }
    internal void VerifyOperation(NativePlaybackGetTimeHookLease expectedHook)
    {
        VerifyRegistration();
        _profile.VerifyOperation(expectedHook);
        VerifyRegistration();
    }
    private void VerifyRegistration()
    {
        if (!ReferenceEquals(_registered, this) || _thread != Environment.CurrentManagedThreadId)
            throw new UnsupportedSignatureException("The fixed GetTime consumer belongs to its original registered owner thread.");
    }
    internal static bool MatchesPinnedProgressReturn(long moduleBase, long callerReturnAddress)
        => moduleBase > 0 && moduleBase <= long.MaxValue - ProgressUpdateReturnRva
            && callerReturnAddress == moduleBase + ProgressUpdateReturnRva;
}
