using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Extends the already validated official-export fixture without changing its
// shared sources. The production RunValue owns every retained source object.
sealed partial class FakeNative
{
    internal IntPtr TargetHold;
    internal readonly IntPtr[] TargetHoldLists = new IntPtr[2];
    internal readonly List<IntPtr> TargetChallenges = new();
    internal readonly List<(string Method, double Input, float Output)> TargetConversions = new();
    internal Action<string, IntPtr>? AfterTargetFieldRead;
    internal int TargetForbiddenUpdates;
    private IntPtr _targetHoldType, _targetChallengeType, _targetChallengeListType, _targetFingerType, _targetReverse, _targetInitCount, _targetInitItem;
    internal TargetProfileValue TargetProfile = null!;
    internal sealed class TargetProfileValue : ISeekTargetNativeProfile
    {
        private readonly FakeNative _native; internal Action? OnVerify;
        internal TargetProfileValue(FakeNative native) => _native = native;
        public IntPtr ChartClass => _native.SeekProfile.ChartClass;
        public IntPtr ProgressClass => _native.SeekProfile.ProgressClass;
        public IntPtr InformationClass => _native.ClassOf(_native.SeekType("InLevel.LevelInformation"));
        public IntPtr NoteClass => _native.ClassOf(_native.SeekType("InLevel.NoteInformation"));
        public IntPtr JudgeClass => _native.ClassOf(_native._judgeType);
        public IntPtr HoldClass => _native.ClassOf(_native._targetHoldType);
        public IntPtr ChallengeTimeClass => _native.ClassOf(_native._targetChallengeType);
        public void VerifyCurrent() { OnVerify?.Invoke(); _native.SeekProfile.VerifyCurrent(); }
    }
    internal void ConfigureTargetState(NoteViewShape shape = NoteViewShape.Complete)
    {
        ConfigureJudgeQueues(shape);
        _targetHoldType = SeekType("InLevel.HoldControl"); _targetChallengeType = SeekType("InLevel.ChallengeTime");
        _targetChallengeListType = SeekType("System.Collections.Generic.List<InLevel.ChallengeTime>");
        _targetFingerType = AddType("System.Collections.Generic.List<System.Int32>", 21, false, IntPtr.Size);
        _targetReverse = GameplayMethodAdd(SeekType("InLevel.ProgressControl"), "RealTimeToChartTime", false, Single, _gameplayDouble);
        _targetInitCount = GameplayMethodAdd(_judgeListType, "get_Count", false, Int32);
        _targetInitItem = GameplayMethodAdd(_judgeListType, "get_Item", false, SeekType("InLevel.NoteInformation"), Int32);
        TargetHold = SeekObject(_targetHoldType);
        var information = SeekType("InLevel.LevelInformation"); var noteClass = SeekType("InLevel.NoteInformation");
        SeekField("target.info.hold", information, "holdControl", _targetHoldType, GameplayInformation, TargetHold, 0x48);
        SeekField("target.hold.info", _targetHoldType, "levelInformation", information, TargetHold, GameplayInformation, 0x20);
        SeekField("target.hold.now", _targetHoldType, "_nowTime", Single, TargetHold, 6789f, 0x38);
        SeekField("target.hold.speed", _targetHoldType, "_speed", Single, TargetHold, 1.75f, 0x3c);
        SeekField("target.hold.length", _targetHoldType, "_holdLineLength", Single, TargetHold, 2.5f, 0x40);
        SeekField("target.progress.offset", SeekType("InLevel.ProgressControl"), "_offset", _gameplayDouble, GameplayProgress, .2d, 0x48);
        SeekField("target.progress.now", SeekType("InLevel.ProgressControl"), "_nowTime", _gameplayDouble, GameplayProgress, 3.8d, 0x50);
        SeekField("target.progress.prepared", SeekType("InLevel.ProgressControl"), "prepared", Boolean, GameplayProgress, true, 0x41);
        SeekField("target.chart.delay", _seekChartType, "chartDelayMs", Int32, SeekChart, 37000, 0x14);
        SeekField("target.info.chartNow", information, "nowTime", Single, GameplayInformation, 9876f, 0xb0);
        SeekField("target.info.realNow", information, "nowRealTime", Single, GameplayInformation, 5432f, 0xb4);
        for (var index = 0; index < 2; ++index)
        {
            TargetHoldLists[index] = JudgeNewList(0, index == 0 ? int.MinValue : int.MaxValue,
                new[] { IntPtr.Zero, new IntPtr(JudgeRuntimeReferences[0]), IntPtr.Zero });
            SeekField("target.hold.list." + index, _targetHoldType, index == 0 ? "holdNotes" : "runningHoldNotes",
                _judgeListType, TargetHold, TargetHoldLists[index], 0x28 + index * IntPtr.Size);
        }
        var windows = new[] { ("judgeTime", .15f), ("maxPerfectJudgeTimeInChallengeTime", .03f),
            ("normalPerfectJudgeTimeInChallengeTime", .06f), ("badTime", .2f) };
        for (var index = 0; index < windows.Length; ++index)
        {
            var (name, value) = windows[index];
            SeekField("target.info.window." + index, information, name, Single, GameplayInformation, value, 0xa0 + index * 4);
            SeekField("target.judge.window." + index, _judgeType, index == 3 ? name : "_" + name, Single, JudgeOwner, value, 0x54 + index * 4);
        }
        SeekField("target.judge.ready", _judgeType, "_instantiateReady", Boolean, JudgeOwner, true, 0x6c);
        SeekField("target.judge.auto", _judgeType, "<AutoPlay>k__BackingField", Boolean, JudgeOwner, false, 0x79);
        SeekField("target.judge.freeHold", _judgeType, "<FreeHold>k__BackingField", Boolean, JudgeOwner, false, 0x7a);
        SeekField("target.judge.badHit", _judgeType, "<InstantiateBadHit>k__BackingField", Boolean, JudgeOwner, true, 0x78);
        SeekField("target.judge.now", _judgeType, "_nowTime", Single, JudgeOwner, 8765f, 0x64);
        SeekField("target.info.complete", information, "challengeCompletely", Boolean, GameplayInformation, false, 0xbc);
        SeekField("target.info.challenge", information, "inChallengeTime", Boolean, GameplayInformation, false, 0xbd);
        SeekField("target.info.combo", information, "challengeTimeComboNum", Int32, GameplayInformation, 271, 0x94);
        SeekField("target.info.now", information, "nowSyncedTime", Single, GameplayInformation, 4321f, 0xb8);
        TargetSetChallenges();
        foreach (var note in _referenceArrays[CursorAllArray])
        {
            var data = SeekValue<IntPtr>(note, "noteData"); var type = TargetInt(data, "type"); var time = SeekValue<float>(data, "time");
            var end = type == 2 ? (float)_seekLists[SeekValue<IntPtr>(data, "otherInformations")][0] : time;
            var key = "target.note." + note.ToInt64();
            SeekField(key + ".real", noteClass, "realTime", Single, note, time * .5f, 0x34);
            SeekField(key + ".end", noteClass, "holdEndRealTime", Single, note, end * .5f, 0x38);
            SeekField(key + ".challenge", noteClass, "inChallengeTime", Boolean, note, false, 0x56);
            SeekField(key + ".static", noteClass, "isStaticNote", Boolean, note, false, 0x54);
            SeekField(key + ".length", noteClass, "lastFrameHoldLineLength", Single, note, 5.5f, 0x50);
            var transform = SeekType("UnityEngine.Transform");
            SeekField(key + ".endFlag", noteClass, "holdEndFlag", transform, note, type == 2 ? SeekObject(transform) : IntPtr.Zero, 0x40);
            SeekField(key + ".holdLine", noteClass, "holdLine", transform, note, type == 2 ? SeekObject(transform) : IntPtr.Zero, 0x48);
            SeekField(key + ".fingers", noteClass, "fingerIds", _targetFingerType, note, IntPtr.Zero, 0x58);
            for (var slot = 0; slot < FlagNames.Length; ++slot)
                SeekField(key + ".flag." + slot, noteClass, FlagNames[slot], Boolean, note, slot == 0, FlagOffsets[slot]);
        }
        JudgeSetHold(0);
        Add("il2cpp_class_get_methods", "Ptr2", nameof(TargetMethods));
        Add("il2cpp_class_get_method_from_name", "MethodLookup", nameof(TargetNamedMethod));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(TargetInvoke));
        Add("il2cpp_field_get_value", "FieldGet", nameof(TargetReadField));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread(); TargetProfile = new(this);
        JudgeQueueRememberOtherStorage();
    }
    internal IntPtr TargetNote(int index) => _referenceArrays[CursorAllArray][index];
    internal IntPtr TargetData(int index) => SeekValue<IntPtr>(TargetNote(index), "noteData");
    private IntPtr TargetField(IntPtr owner, string name) => _structFields[ObjectClass(owner)].Single(field => _fieldMetadata[field].Name == name);
    internal int TargetInt(IntPtr owner, string name) => BitConverter.ToInt32(_gameplayStorage[(owner, TargetField(owner, name))]);
    internal float TargetFloat(IntPtr owner, string name) => BitConverter.ToSingle(_gameplayStorage[(owner, TargetField(owner, name))]);
    internal bool TargetBoolean(IntPtr owner, string name) => _gameplayStorage[(owner, TargetField(owner, name))][0] != 0;
    internal void TargetSet(IntPtr owner, string name, object value) => _gameplayStorage[(owner, TargetField(owner, name))] = value switch
    {
        bool boolean => new[] { boolean ? (byte)1 : (byte)0 }, int integer => BitConverter.GetBytes(integer),
        float single => BitConverter.GetBytes(single), double number => BitConverter.GetBytes(number), IntPtr reference => BitConverter.GetBytes(reference.ToInt64()),
        _ => throw new ArgumentException("Unsupported target fixture field value.")
    };
    internal void TargetSetChart(int ordinal, int type, float time, float? end = null)
    {
        var data = TargetData(ordinal); TargetSet(data, "type", type); TargetSet(data, "time", time);
        _seekLists[SeekValue<IntPtr>(data, "otherInformations")] = type == 2 ? new object[] { end ?? time, 0f, 0f } : Array.Empty<object>();
    }
    internal void TargetSetTiming(int ordinal, float realTime, float? end = null, bool challenge = false)
    {
        var note = TargetNote(ordinal); TargetSet(note, "realTime", realTime); TargetSet(note, "holdEndRealTime", end ?? realTime);
        TargetSet(note, "inChallengeTime", challenge);
    }
    internal IntPtr TargetNonNullFingers(int ordinal)
    { var list = SeekObject(_targetFingerType); TargetSet(TargetNote(ordinal), "fingerIds", list); return list; }
    internal void TargetInitialJudgeNotes(params int[] ordinals)
    {
        var values = _referenceArrays[JudgeBacking(0)]; Array.Clear(values);
        for (var index = 0; index < ordinals.Length; ++index) values[index] = TargetNote(ordinals[index]);
        JudgeSetList(0, "_size", ordinals.Length);
    }
    internal void TargetSetChallenges(params (float Start, float End)[] times)
    {
        TargetChallenges.Clear();
        foreach (var (name, offset) in new[] { ("checkPoint", 0x10), ("start", 0x14), ("end", 0x18), ("transTime", 0x1c) })
            SeekField("target.challenge.template." + name, _targetChallengeType, name, Single, IntPtr.Zero, 0f, offset);
        for (var index = 0; index < times.Length; ++index)
        {
            var challenge = SeekObject(_targetChallengeType); TargetChallenges.Add(challenge); var key = "target.challenge." + index;
            foreach (var (name, value, offset) in new[] { ("checkPoint", times[index].Start, 0x10), ("start", times[index].Start, 0x14),
                ("end", times[index].End, 0x18), ("transTime", 0f, 0x1c) })
                SeekField(key + "." + name, _targetChallengeType, name, Single, challenge, value, offset);
        }
        SeekField("target.chart.challenges", _seekChartType, "challengeTimes", _targetChallengeListType, SeekChart,
            SeekList(_targetChallengeListType, _targetChallengeType, TargetChallenges.Cast<object>().ToArray()), 0x28);
    }
    internal void TargetMetadata(string owner, string name, string type, int offset)
    {
        var value = owner == "judge" ? JudgeOwner : owner == "info" ? GameplayInformation : TargetNote(0);
        _fieldMetadata[TargetField(value, name)] = (name, SeekType(type, type == "System.Single"), offset);
    }
    private bool TargetCollectionClass(IntPtr cls) => cls == ClassOf(_targetChallengeListType) || cls == ClassOf(_seekKeyListType);
    private bool TargetMethodClass(IntPtr cls) => TargetCollectionClass(cls) || cls == ClassOf(SeekType("InLevel.ProgressControl"));
    private IntPtr TargetMethods(IntPtr cls, IntPtr iterator) => TargetMethodClass(cls) ? GameplayMethods(cls, iterator) : JudgeQueueMethods(cls, iterator);
    private IntPtr TargetNamedMethod(IntPtr cls, string name, int count) => TargetMethodClass(cls)
        ? _gameplayMethods.FirstOrDefault(row => row.Value.Class == cls && row.Value.Name == name && row.Value.Parameters.Length == count).Key
        : JudgeQueueNamedMethod(cls, name, count);
    private void TargetReadField(IntPtr owner, IntPtr field, IntPtr result)
    { NoteViewReadField(owner, field, result); AfterTargetFieldRead?.Invoke(_fieldMetadata[field].Name, owner); }
    private IntPtr TargetInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception)
    {
        if (method == _targetInitCount || method == _targetInitItem)
        {
            ++Invocations; AllGameplayReceiversRooted &= HasRootFor(receiver); Marshal.WriteIntPtr(exception, IntPtr.Zero);
            var size = TargetInt(receiver, "_size");
            if (method == _targetInitCount) return Box(Int32, BitConverter.GetBytes(size));
            var index = Marshal.ReadInt32(Marshal.ReadIntPtr(arguments));
            if ((uint)index >= (uint)size) throw new InvalidOperationException("The initialization reader left the actual Judge.notes active prefix.");
            return _referenceArrays[SeekValue<IntPtr>(receiver, "_items")][index];
        }
        if (method == _targetReverse)
        {
            ++Invocations; AllGameplayReceiversRooted &= receiver == GameplayProgress && HasRootFor(receiver); Marshal.WriteIntPtr(exception, IntPtr.Zero);
            var input = Marshal.PtrToStructure<double>(Marshal.ReadIntPtr(arguments));
            var bpm = SeekValue<float>(SeekChart, "bPM"); var shifts = _seekLists[SeekValue<IntPtr>(SeekChart, "bpmShifts")];
            double result;
            if (shifts.Length == 0) result = input * bpm / 60d;
            else
            {
                var point = (IntPtr)shifts[0];
                foreach (var value in shifts) { if (SeekValue<float>((IntPtr)value, "floorPosition") <= input) point = (IntPtr)value; }
                result = (input - SeekValue<float>(point, "floorPosition")) * bpm / 60d * SeekValue<float>(point, "value") + SeekValue<float>(point, "time");
            }
            TargetConversions.Add(("RealTimeToChartTime", input, (float)result)); return Box(Single, BitConverter.GetBytes((float)result));
        }
        if (_gameplayMethods.TryGetValue(method, out var converter) && converter.Class == ClassOf(SeekType("InLevel.ProgressControl"))
            && converter.Name == "ChartTimeToRealTime")
        {
            var result = SeekInvoke(method, receiver, arguments, exception);
            TargetConversions.Add((converter.Name, Marshal.PtrToStructure<double>(Marshal.ReadIntPtr(arguments)), BitConverter.ToSingle(RenderedValue(result))));
            return result;
        }
        if (_gameplayMethods.TryGetValue(method, out var row) && TargetCollectionClass(row.Class))
        {
            if (row.Name is not ("get_Count" or "get_Item") || !_seekLists.ContainsKey(receiver))
                throw new InvalidOperationException("The target reader left the official ChallengeTime List getter surface.");
            return SeekInvoke(method, receiver, arguments, exception);
        }
        if (_gameplayMethods.TryGetValue(method, out row) && row.Name is "Update" or "ActiveNotes" or "SetNotes")
        { ++TargetForbiddenUpdates; throw new InvalidOperationException("Target capture must not run gameplay/hold/judge consumers."); }
        return JudgeQueueInvoke(method, receiver, arguments, exception);
    }
    private static byte[] RenderedValue(IntPtr boxed)
    { var value = new byte[sizeof(float)]; Marshal.Copy(boxed, value, 0, value.Length); return value; }
}
