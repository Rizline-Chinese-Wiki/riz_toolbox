using System.Runtime.InteropServices;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal sealed record InitializationWrite(int Ordinal, IntPtr Owner, string Field, int Bits) { internal bool Stored; }
    internal readonly List<InitializationWrite> InitializationWrites = new();
    internal Action<InitializationWrite>? BeforeInitializationWrite, AfterInitializationWrite;
    internal int FailInitializationBefore, FailInitializationAfter;
    internal bool InitializationReceiversRooted = true;
    private Dictionary<(IntPtr Owner, IntPtr Field), byte[]> _initializationBaseline = new();
    internal void ConfigureJudgeInitialization()
    {
        ConfigureTargetState();
        TargetSet(JudgeOwner, "_instantiateReady", false); TargetInitialJudgeNotes(0, 1, 2, 3, 4); TargetSetChallenges((0, 2));
        TargetSet(JudgeOwner, "_judgeTime", 9f); TargetSet(JudgeOwner, "badTime", 7f);
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(InitializationSetValue));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread(); InitializationRemember();
    }
    internal void InitializationRemember() => _initializationBaseline = _gameplayStorage.ToDictionary(x => x.Key, x => x.Value.ToArray());
    internal bool InitializationBaselineMatches() => _initializationBaseline.All(x => _gameplayStorage.TryGetValue(x.Key, out var value) && value.SequenceEqual(x.Value));
    internal bool InitializationOtherMatches() => _initializationBaseline.Where(x => !InitializationAllowed(x.Key.Owner, x.Key.Field))
        .All(x => _gameplayStorage.TryGetValue(x.Key, out var value) && value.SequenceEqual(x.Value));
    private bool InitializationAllowed(IntPtr owner, IntPtr field)
    {
        var name = _fieldMetadata[field].Name;
        return owner == JudgeOwner && name is "_judgeTime" or "_maxPerfectJudgeTimeInChallengeTime" or "_normalPerfectJudgeTimeInChallengeTime" or "badTime" or "_instantiateReady"
            || owner == GameplayInformation && name == "challengeTimeComboNum"
            || NoteViewRuntimeNotes.Contains(owner) && name == "inChallengeTime";
    }
    internal void InitializationFieldOffset(string name, int offset)
    {
        var field = TargetField(JudgeOwner, name); var old = _fieldMetadata[field];
        _fieldMetadata[field] = (old.Name, old.Type, offset);
    }
    private void InitializationSetValue(IntPtr owner, IntPtr field, IntPtr input)
    {
        if (!InitializationAllowed(owner, field)) { SetFieldValue(owner, field, input); return; }
        InitializationReceiversRooted &= HasRootFor(owner); ++ValueFieldWrites;
        var width = _fieldMetadata[field].Type == Boolean ? 1 : 4;
        var bytes = new byte[width]; Marshal.Copy(input, bytes, 0, width);
        var row = new InitializationWrite(InitializationWrites.Count + 1, owner, _fieldMetadata[field].Name, width == 1 ? bytes[0] : BitConverter.ToInt32(bytes));
        InitializationWrites.Add(row); BeforeInitializationWrite?.Invoke(row);
        if (row.Ordinal == FailInitializationBefore) throw new IOException("Synthetic initialization before-storage failure.");
        _gameplayStorage[(owner, field)] = bytes; row.Stored = true; AfterInitializationWrite?.Invoke(row);
        if (row.Ordinal == FailInitializationAfter) throw new IOException("Synthetic initialization after-storage failure.");
    }
}
