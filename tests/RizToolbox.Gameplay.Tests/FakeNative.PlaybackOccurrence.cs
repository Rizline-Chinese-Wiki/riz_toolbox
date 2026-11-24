using System.Runtime.InteropServices;
using RizToolbox.Runtime;

// Extends the shared unmanaged export/metadata fixture only for the owned native playback lane.
// The native stub is a separate DLL; DSP/audio operations still enter actual NativeRuntime APIs.
sealed partial class FakeNative
{
    internal bool OccurrenceAudioPlaying;
    internal bool OccurrenceDropSchedule, OccurrenceIgnorePitch;
    internal int OccurrenceFailPitchNumber, OccurrenceFailScheduleNumber, OccurrencePauseCalls, OccurrenceStopCalls;
    internal Action? AfterOccurrenceSchedule, BeforeOccurrenceMediaRead, AfterOccurrenceDspRead;
    internal readonly List<double> OccurrenceScheduleArguments = new();
    internal readonly List<string> OccurrenceCalls = new();

    internal void ConfigurePlaybackOccurrence()
    {
        ConfigureGameplay();
        IntPtr Type(string name) => _types.Single(row => row.Value.Name == name).Key;
        GameplayPlayer = Box(Type("Pigeon.MUGToolkit.Core.MUGMusicPlayer"), new byte[0x40]);
        foreach (var instance in new[] { GameplayController, GameplayInformation, GameplayProgress, GameplayPause,
            GameplayPlayer, GameplaySource, GameplayClip }) Marshal.WriteIntPtr(instance, GameplayApi.ObjectClass(instance));
        GameplayField("progress.player", Type("InLevel.ProgressControl"), "<MusicPlayer>k__BackingField",
            Type("Pigeon.MUGToolkit.Core.MUGMusicPlayer"), GameplayProgress, GameplayPlayer);
        GameplayField("progress.source", Type("InLevel.ProgressControl"), "audioSource", Type("UnityEngine.AudioSource"), GameplayProgress, GameplaySource);
        GameplayField("player.source", Type("Pigeon.MUGToolkit.Core.MUGMusicPlayer"), "audioSource", Type("UnityEngine.AudioSource"), GameplayPlayer, GameplaySource);
        var sourceField = _gameplayFields["player.source"].Field;
        _fieldMetadata[sourceField] = ("audioSource", Type("UnityEngine.AudioSource"), 0x10);
        var nullable = AddType("System.Nullable<System.Double>", 17, true, 16);
        GameplayField("player.origin", Type("Pigeon.MUGToolkit.Core.MUGMusicPlayer"), "startPlayTime", nullable, GameplayPlayer, false);
        var origin = _gameplayFields["player.origin"].Field; _fieldMetadata[origin] = ("startPlayTime", nullable, 0x18);
        SetOccurrenceOrigin(null);
        var source = Type("UnityEngine.AudioSource");
        GameplayMethodAdd(source, "get_clip", false, Type("UnityEngine.AudioClip"));
        GameplayMethodAdd(source, "set_clip", false, Void, Type("UnityEngine.AudioClip"));
        GameplayMethodAdd(source, "get_isActiveAndEnabled", false, Boolean);
        GameplayMethodAdd(source, "set_time", false, Void, Single);
        GameplayMethodAdd(source, "PlayScheduled", false, Void, _gameplayDouble);
        GameplayMethodAdd(source, "Pause", false, Void); GameplayMethodAdd(source, "Stop", false, Void);
        var dsp = AddType("Pigeon.MUGToolkit.Core.Services.DspTimeService", 18, false, 8);
        GameplayMethodAdd(dsp, "get_DspTime", true, _gameplayDouble);
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(PlaybackOccurrenceInvoke));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
    }
    internal void SetOccurrenceOrigin(double? origin)
    {
        for (var i = 0; i < 16; i++) Marshal.WriteByte(GameplayPlayer, 0x18 + i, 0);
        if (origin is not double value) return;
        Marshal.WriteByte(GameplayPlayer, 0x18, 1); Marshal.WriteInt64(GameplayPlayer, 0x20, BitConverter.DoubleToInt64Bits(value));
    }
    private IntPtr PlaybackOccurrenceInvoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        if (!_gameplayMethods.TryGetValue(method, out var row)) return GameplayInvoke(method, instance, arguments, exception);
        Marshal.WriteIntPtr(exception, IntPtr.Zero);
        AllGameplayReceiversRooted &= instance == IntPtr.Zero || HasRootFor(instance);
        switch (row.Name)
        {
            case "get_DspTime":
                var dsp = GameplayDsp; AfterOccurrenceDspRead?.Invoke(); return Box(_gameplayDouble, BitConverter.GetBytes(dsp));
            case "get_clip": return GameplayClip;
            case "get_isPlaying": return Box(Boolean, new[] { OccurrenceAudioPlaying ? (byte)1 : (byte)0 });
            case "get_time": BeforeOccurrenceMediaRead?.Invoke(); return Box(Single, BitConverter.GetBytes(GameplayPosition));
            case "Pause" when instance == GameplaySource:
                OccurrenceCalls.Add("Pause"); OccurrencePauseCalls++; OccurrenceAudioPlaying = false; return IntPtr.Zero;
            case "Stop" when instance == GameplaySource:
                OccurrenceCalls.Add("Stop"); OccurrenceStopCalls++; OccurrenceAudioPlaying = false; GameplayPosition = 0; return IntPtr.Zero;
            case "set_time":
                GameplayPosition = Marshal.PtrToStructure<float>(Marshal.ReadIntPtr(arguments)); OccurrenceCalls.Add("time:" + GameplayPosition); return IntPtr.Zero;
            case "set_pitch":
                var pitch = Marshal.PtrToStructure<float>(Marshal.ReadIntPtr(arguments));
                GameplayPitchRevision++; GameplayPitchWrites++; GameplayPitchAttempts.Add(pitch); OccurrenceCalls.Add("pitch:" + pitch);
                if (!OccurrenceIgnorePitch) GameplayPitch = pitch;
                AfterGameplayPitchWrite?.Invoke();
                if (GameplayPitchWrites == OccurrenceFailPitchNumber) throw new IOException("actual owned fixture pitch changed before failure");
                return IntPtr.Zero;
            case "PlayScheduled":
                var start = Marshal.PtrToStructure<double>(Marshal.ReadIntPtr(arguments));
                OccurrenceScheduleArguments.Add(start); OccurrenceCalls.Add("schedule:" + start.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                AfterOccurrenceSchedule?.Invoke();
                if (OccurrenceScheduleArguments.Count == OccurrenceFailScheduleNumber) throw new IOException("actual owned fixture PlayScheduled failed after entry");
                if (OccurrenceDropSchedule) throw new IOException("actual owned fixture did not return a schedule");
                return IntPtr.Zero;
            case "set_clip":
                GameplayClip = Marshal.ReadIntPtr(arguments); OccurrenceCalls.Add("clip"); return IntPtr.Zero;
            default: return GameplayInvoke(method, instance, arguments, exception);
        }
    }
}
