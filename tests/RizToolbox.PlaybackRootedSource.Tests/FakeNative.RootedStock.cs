using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

// Actual stock fixture storage and detoured native bodies; synthetic IL2CPP
// metadata/export delegates for the real NativeRuntime graph/root/API accessor.
// Writable fixture MethodInfo classes are associated before any broker capture.
sealed partial class FakeNative
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr RootedCreate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr RootedChild(IntPtr parent);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr RootedMethod();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RootedInvoke(uint kind, IntPtr target, IntPtr self, ulong bits, IntPtr method, out uint code);
    private IntPtr _stockModule;
    internal Action? DuringRootedPitchRead, DuringRootedDspRead;
    internal bool WrongRootedPitchReadback;
    internal int RootedDspReads, RootedPitchReads;

    internal void ConfigureRootedStock(IntPtr module)
    {
        ConfigurePlaybackOccurrence(); _stockModule = module;
        IntPtr Type(string name) => _types.Single(row => row.Value.Name == name).Key;
        T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
        var replacements = new Dictionary<IntPtr, IntPtr>
        {
            [GameplaySource] = Export<RootedCreate>("StockFixtureCreateSource")()
        };
        replacements[GameplayPlayer] = Export<RootedChild>("StockFixtureCreatePlayer")(replacements[GameplaySource]);
        replacements[GameplayProgress] = Export<RootedChild>("StockFixtureCreateProgress")(replacements[GameplayPlayer]);
        foreach (var pair in replacements)
        {
            _boxedTypes[pair.Value] = _boxedTypes[pair.Key];
            Marshal.WriteIntPtr(pair.Value, ClassOf(_boxedTypes[pair.Value]));
        }
        var storage = _gameplayStorage.ToArray(); _gameplayStorage.Clear();
        foreach (var pair in storage)
        {
            var owner = replacements.GetValueOrDefault(pair.Key.Owner, pair.Key.Owner);
            var bytes = pair.Value;
            if (bytes.Length == 8 && replacements.TryGetValue(new(BitConverter.ToInt64(bytes)), out var changed))
                bytes = BitConverter.GetBytes(changed.ToInt64());
            _gameplayStorage[(owner, pair.Key.Field)] = bytes;
        }
        foreach (var key in _gameplayFields.Keys.ToArray())
        {
            var row = _gameplayFields[key];
            _gameplayFields[key] = (replacements.GetValueOrDefault(row.Owner, row.Owner), row.Field);
        }
        GameplaySource = replacements[GameplaySource]; GameplayPlayer = replacements[GameplayPlayer]; GameplayProgress = replacements[GameplayProgress];
        Marshal.WriteIntPtr(GameplayProgress, 0x38, GameplayPlayer);
        Marshal.WriteIntPtr(GameplaySource, 40, GameplayClip);
        Marshal.Copy(new[] { 3.0 }, 0, IntPtr.Add(GameplaySource, 24), 1);
        SetRootedDsp(100);
        var playerType = Type("Pigeon.MUGToolkit.Core.MUGMusicPlayer"); var sourceType = Type("UnityEngine.AudioSource");
        var progressType = Type("InLevel.ProgressControl");
        foreach (var row in new[] {
            ("StockMethodGetTime", playerType, "GetTime", false, Single, Array.Empty<IntPtr>()),
            ("StockMethodPause", playerType, "Pause", false, Void, Array.Empty<IntPtr>()),
            ("StockMethodReplay", playerType, "ReplayScheduled", false, Void, new[]{Single}),
            ("StockMethodResume", playerType, "ResumeScheduled", false, Void, new[]{Single}),
            ("StockMethodProgressPlay", progressType, "Play", false, Void, new[]{Boolean}),
            ("StockMethodPauseLeaf", sourceType, "Pause", false, Void, Array.Empty<IntPtr>()),
            ("StockMethodSetPitch", sourceType, "set_pitch", false, Void, new[]{Single}),
            ("StockMethodSetPitchAlias", sourceType, "SetPitch", true, Void, new[]{sourceType,Single}),
            ("StockMethodPlayScheduled", sourceType, "PlayScheduled", false, Void, new[]{_gameplayDouble}),
            ("StockMethodSetTime", sourceType, "set_time", false, Void, new[]{Single}),
            ("StockMethodSetClip", sourceType, "set_clip", false, Void, new[]{Type("UnityEngine.AudioClip")}),
            ("StockMethodProgressMusicPlayer", progressType, "set_MusicPlayer", false, Void, new[]{playerType}) })
        {
            var method = Export<RootedMethod>(row.Item1)();
            var cls = ClassOf(row.Item2);
            foreach (var prior in _gameplayMethods.Where(value => value.Value.Class == cls && value.Value.Name == row.Item3).Select(value => value.Key).ToArray())
                _gameplayMethods.Remove(prior);
            _gameplayMethods[method] = new(cls, row.Item3, row.Item4, row.Item5, row.Item6);
            Marshal.WriteIntPtr(method, 32, cls);
            if (row.Item3 == "GetTime") GameplayGetTimeMethod = method;
        }
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(RootedStockInvoke));
        Add("il2cpp_field_get_value", "FieldGet", nameof(RootedStockReadField));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
    }
    internal void SetRootedDsp(double dsp)
        => Marshal.WriteInt64(GameplayPlayer, 48, BitConverter.DoubleToInt64Bits(dsp));
    internal void SetRootedGraphValue(string key, object value) => SetGameplayField(key, value);
    private void RootedStockReadField(IntPtr owner, IntPtr field, IntPtr output)
    {
        var name = _fieldMetadata[field].Name;
        if (owner == GameplayPlayer && name == "audioSource")
        {
            FieldReads++; AllGameplayReceiversRooted &= HasRootFor(owner);
            Marshal.WriteIntPtr(output, Marshal.ReadIntPtr(owner, 16)); return;
        }
        if (owner == GameplayProgress && name == "<MusicPlayer>k__BackingField")
        {
            FieldReads++; AllGameplayReceiversRooted &= HasRootFor(owner);
            Marshal.WriteIntPtr(output, Marshal.ReadIntPtr(owner, 0x38)); return;
        }
        GameplayReadField(owner, field, output);
    }
    private IntPtr RootedStockInvoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        if (!_gameplayMethods.TryGetValue(method, out var row)) return GameplayInvoke(method, instance, arguments, exception);
        Marshal.WriteIntPtr(exception, IntPtr.Zero);
        AllGameplayReceiversRooted &= instance == IntPtr.Zero || HasRootFor(instance);
        switch (row.Name)
        {
            case "get_DspTime":
                RootedDspReads++; var dsp = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(GameplayPlayer, 48));
                DuringRootedDspRead?.Invoke(); return Box(_gameplayDouble, BitConverter.GetBytes(dsp));
            case "get_pitch":
                RootedPitchReads++; var pitch = Marshal.ReadInt32(GameplaySource, 16);
                DuringRootedPitchRead?.Invoke(); return Box(Single, BitConverter.GetBytes(WrongRootedPitchReadback ? 1 : BitConverter.Int32BitsToSingle(pitch)));
            case "get_time":
                return Box(Single, BitConverter.GetBytes((float)BitConverter.Int64BitsToDouble(Marshal.ReadInt64(GameplaySource, 24))));
            case "get_clip": return Marshal.ReadIntPtr(GameplaySource, 40);
            case "get_isPlaying": return Box(Boolean, new[]{Marshal.ReadInt32(GameplaySource,48) != 0 ? (byte)1 : (byte)0});
            case "set_MusicPlayer":
                var player = Marshal.ReadIntPtr(arguments);
                var writer = Marshal.GetDelegateForFunctionPointer<RootedInvoke>(NativeLibrary.GetExport(_stockModule,"StockFixtureInvoke"));
                if(writer(4,NativeLibrary.GetExport(_stockModule,"StockProgressMusicPlayer"),instance,unchecked((ulong)player.ToInt64()),method,out var writerError)!=1)
                    throw new InvalidOperationException("Actual Progress player invocation failed: "+writerError.ToString("x"));
                return IntPtr.Zero;
            case "set_time":
            case "set_pitch":
            case "SetPitch":
                var source = row.Static ? Marshal.ReadIntPtr(arguments) : instance;
                var bits = unchecked((uint)Marshal.ReadInt32(Marshal.ReadIntPtr(arguments, row.Static ? 8 : 0)));
                var invoke = Marshal.GetDelegateForFunctionPointer<RootedInvoke>(NativeLibrary.GetExport(_stockModule,"StockFixtureInvoke"));
                var target = NativeLibrary.GetExport(_stockModule,row.Name == "set_time" ? "StockSetTime" : "StockSetPitch");
                if(invoke(2,target,source,bits,method,out var error) != 1)
                    throw new InvalidOperationException("Actual stock pitch invocation failed: "+error.ToString("x"));
                return IntPtr.Zero;
            default: return PlaybackOccurrenceInvoke(method, instance, arguments, exception);
        }
    }
}
