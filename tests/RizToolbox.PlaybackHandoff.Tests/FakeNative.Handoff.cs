using System.Runtime.InteropServices;

sealed partial class FakeNative
{
    // Allocate an independent seven-object graph under the same metadata/runtime.
    // Retain the predecessor buffers and field storage until its actual borrowers
    // drain; changing the fixture's current graph never frees an old object.
    internal void NextRootedStockGraph()
    {
        T Export<T>(string name) where T : Delegate
            => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_stockModule, name));
        var dsp = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(GameplayPlayer, 48));
        var replacements = new Dictionary<IntPtr, IntPtr>();
        foreach (var previous in new[] { GameplayController, GameplayInformation, GameplayPause, GameplayClip })
            replacements[previous] = Box(_boxedTypes[previous], new byte[16]);
        replacements[GameplaySource] = Export<RootedCreate>("StockFixtureCreateSource")();
        replacements[GameplayPlayer] = Export<RootedChild>("StockFixtureCreatePlayer")(replacements[GameplaySource]);
        replacements[GameplayProgress] = Export<RootedChild>("StockFixtureCreateProgress")(replacements[GameplayPlayer]);
        foreach (var pair in replacements)
        {
            _boxedTypes[pair.Value] = _boxedTypes[pair.Key];
            Marshal.WriteIntPtr(pair.Value, ClassOf(_boxedTypes[pair.Value]));
        }
        foreach (var pair in _gameplayStorage.ToArray())
        {
            if (!replacements.TryGetValue(pair.Key.Owner, out var owner)) continue;
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
        GameplayController = replacements[GameplayController]; GameplayInformation = replacements[GameplayInformation];
        GameplayPause = replacements[GameplayPause]; GameplayClip = replacements[GameplayClip];
        GameplaySource = replacements[GameplaySource]; GameplayPlayer = replacements[GameplayPlayer]; GameplayProgress = replacements[GameplayProgress];
        _referenceArrays[GameplayControllers] = new[] { GameplayController };
        Marshal.WriteIntPtr(GameplaySource, 40, GameplayClip);
        Marshal.WriteInt64(GameplaySource, 24, BitConverter.DoubleToInt64Bits(3));
        SetRootedDsp(dsp + 1); // Global DSP remains monotonic across logical runs.
    }
}
