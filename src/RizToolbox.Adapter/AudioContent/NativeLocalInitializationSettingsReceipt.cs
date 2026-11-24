namespace RizToolbox.Adapter.AudioContent;

internal readonly record struct NativeLocalInitializationSettings(float MappedSpeed, float OffsetMilliseconds,
    float OffsetDivisor, long MappedSpeedMethod, long OffsetMillisecondsMethod)
{
    internal int SpeedBits => BitConverter.SingleToInt32Bits(MappedSpeed);
    internal int OffsetMillisecondsBits => BitConverter.SingleToInt32Bits(OffsetMilliseconds);
    internal int DivisorBits => BitConverter.SingleToInt32Bits(OffsetDivisor);
}

internal interface INativeLocalInitializationSettingsBackend
{
    InactiveControllerParameterInputs ReadInitializationParameterState(IDisposable ownedBundle);
    NativeLocalInitializationSettings ReadInitializationSettings();
}

// One storage object is allocated with each existing graph reservation, before
// native launch. It only observes the actual getter; it never writes parameters
// or substitutes a guessed speed/default for stock settings initialization.
internal sealed class NativeLocalInitializationSettingsReceipt
{
    internal const int OffsetDivisorBits = 0x447A0000; // RVA 17C4C50 bytes 00 00 7A 44, IEEE Single 1000.
    private LocalRunSessionIdentity? _identity;
    private long _controller, _parameters;
    private LocalNativeParametersObservation? _baseline;
    private NativeLocalInitializationSettings _settings;
    private bool _attempted, _beforeRecorded, _verified, _unknown;
    internal bool IsVerified => _verified && !_unknown;
    internal bool IsUnknown => _unknown;
    internal LocalNativeParametersObservation? Baseline => _baseline;
    internal void BeginAttempt(LocalRunSessionIdentity identity, long exactController)
    {
        if (_attempted || _unknown || identity is null || exactController == 0) { _unknown = true; throw new InvalidOperationException("Settings initialization is once-only for this exact reserved run."); }
        _attempted = true; _identity = identity; _controller = exactController;
    }
    internal void RecordBefore(NativeInactiveControllerParameterBindingObservation setterBaseline,
        InactiveControllerParameterInputs input, NativeLocalInitializationSettings settings)
    {
        try
        {
            if (!_attempted || _beforeRecorded || _unknown) throw new InvalidOperationException("No fresh settings initialization attempt exists.");
            ArgumentNullException.ThrowIfNull(setterBaseline);
            ValidateSettings(settings); ValidateInput(input);
            var baseline = setterBaseline.ReferencesAndScalars;
            if (setterBaseline.Controller != _controller || setterBaseline.Parameters != input.ParametersIdentity
                || baseline is null || !SameReferencesAndBits(baseline, input.Parameters))
                throw new InvalidOperationException("Initialization input changed after the exact completed inactive parameter setter.");
            // Baseline scalar bits are observations from the real constructor and
            // setter. They need not equal the values stock initialization writes.
            _parameters = setterBaseline.Parameters; _baseline = baseline; _settings = settings; _beforeRecorded = true;
        }
        catch { MarkUnknown(); throw; }
    }
    internal void VerifyReturned(LocalRunSessionIdentity identity, long exactController, long actualReturned,
        InactiveControllerParameterInputs input, NativeLocalInitializationSettings settings)
    {
        try
        {
            if (!_beforeRecorded || _verified || _unknown || identity != _identity || exactController != _controller
                || actualReturned != _parameters || input.ParametersIdentity != _parameters)
                throw new InvalidOperationException("The settings receipt does not match this actual original getter return.");
            ValidateSettings(settings); ValidateInput(input);
            if (settings.SpeedBits != _settings.SpeedBits || settings.OffsetMillisecondsBits != _settings.OffsetMillisecondsBits
                || settings.DivisorBits != _settings.DivisorBits || settings.MappedSpeedMethod != _settings.MappedSpeedMethod
                || settings.OffsetMillisecondsMethod != _settings.OffsetMillisecondsMethod)
                throw new InvalidOperationException("The exact settings source changed across the stock initialization getter.");
            var current = input.Parameters; var baseline = _baseline!;
            if (current.Metadata != baseline.Metadata || current.CoverReference != baseline.CoverReference
                || current.MusicReference != baseline.MusicReference || current.Chart != baseline.Chart || current.JudgeData != baseline.JudgeData)
                throw new InvalidOperationException("Stock initialization changed the owned five-reference parameter closure.");
            var expectedOffset = settings.OffsetMilliseconds / settings.OffsetDivisor;
            if (current.SpeedBits != settings.SpeedBits || current.OffsetBits != BitConverter.SingleToInt32Bits(expectedOffset))
                throw new InvalidOperationException("The actual initialized parameters differ from MappedSpeed and the proven millisecond conversion.");
            _verified = true;
        }
        catch { MarkUnknown(); throw; }
    }
    internal void MarkUnknown() { _unknown = true; _verified = false; }
    private static bool SameReferencesAndBits(LocalNativeParametersObservation expected, LocalNativeParametersObservation actual)
        => expected.Metadata == actual.Metadata && expected.CoverReference == actual.CoverReference
            && expected.MusicReference == actual.MusicReference && expected.Chart == actual.Chart
            && expected.JudgeData == actual.JudgeData && expected.SpeedBits == actual.SpeedBits && expected.OffsetBits == actual.OffsetBits;
    private static void ValidateInput(InactiveControllerParameterInputs input)
    {
        if (input is null || input.Parameters is not { } value || input.ParametersIdentity == 0 || value.Metadata == 0
            || value.CoverReference == 0 || value.MusicReference == 0 || value.Chart == 0 || value.JudgeData == 0)
            throw new InvalidOperationException("Settings initialization requires the actual complete owned parameter graph.");
    }
    private static void ValidateSettings(NativeLocalInitializationSettings settings)
    {
        if (!float.IsFinite(settings.MappedSpeed) || settings.MappedSpeed <= 0 || !float.IsFinite(settings.OffsetMilliseconds)
            || settings.DivisorBits != OffsetDivisorBits || settings.MappedSpeedMethod == 0 || settings.OffsetMillisecondsMethod == 0
            || settings.MappedSpeedMethod == settings.OffsetMillisecondsMethod)
            throw new InvalidOperationException("Actual finite settings and the pinned Single millisecond divisor are required.");
    }
}
