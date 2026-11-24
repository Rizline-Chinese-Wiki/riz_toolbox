using System.Runtime.InteropServices;

sealed partial class FakeNative
{
    // Replace actual fixture object addresses and all producer edges without
    // asking GameplayService to sample the intermediate graph.
    internal void ReplacePlaybackGraph(bool player, bool source, bool clip)
    {
        IntPtr Replace(IntPtr previous, int size)
        {
            var next = Box(_boxedTypes[previous], new byte[size]);
            Marshal.WriteIntPtr(next, GameplayApi.ObjectClass(previous));
            foreach (var row in _gameplayStorage.Where(row => row.Key.Owner == previous).ToArray())
                _gameplayStorage[(next, row.Key.Field)] = row.Value.ToArray();
            foreach (var key in _gameplayFields.Where(row => row.Value.Owner == previous).Select(row => row.Key).ToArray())
                _gameplayFields[key] = (next, _gameplayFields[key].Field);
            return next;
        }
        if (player) GameplayPlayer = Replace(GameplayPlayer, 0x40);
        if (source) GameplaySource = Replace(GameplaySource, 16);
        if (clip) GameplayClip = Replace(GameplayClip, 16);
        SetGameplayField("progress.player", GameplayPlayer);
        SetGameplayField("progress.source", GameplaySource);
        SetGameplayField("player.source", GameplaySource);
        SetOccurrenceOrigin(null);
    }
}
