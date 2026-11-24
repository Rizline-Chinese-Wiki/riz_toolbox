using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    internal sealed partial class SeekHoldEpoch
    {
        private long Reference(NativeRuntime.RootHandle root, IntPtr cls, string name, string type, int offset)
        {
            Local(); var field = _owner._api.InspectExactDeclaredReadField(cls, name, type, false, offset); Local();
            long result = 0; var published = false;
            _owner._api.PublishExactReferenceField(root, field, value =>
            {
                if (published) throw new InvalidOperationException("A Hold reference was published twice.");
                published = true;
                if (value != IntPtr.Zero) result = Target(Adopt(value)).ToInt64();
            });
            Local(); if (!published) throw new InvalidDataException("A Hold reference was not published."); return result;
        }
        private IteratorState ReadIterator(NativeRuntime.RootHandle root)
        {
            if (_owner._api.ObjectClass(Target(root)) != _profile.IteratorClass)
                throw new UnsupportedSignatureException("The actual Hold iterator has another exact class.");
            var cls = _profile.IteratorClass;
            IteratorState Read() => new(Value<int>(root, cls, "<>1__state", "System.Int32", 0x10),
                Reference(root, cls, "<>2__current", "System.Object", 0x18),
                Reference(root, cls, "<>4__this", "InLevel.NoteJudgeControl", 0x20),
                Reference(root, cls, "holdNoteInformation", "InLevel.NoteInformation", 0x28),
                Value<bool>(root, cls, "isPressLeft", "System.Boolean", 0x30),
                Value<bool>(root, cls, "<isNormalPerfect>5__2", "System.Boolean", 0x31),
                BitConverter.SingleToInt32Bits(Value<float>(root, cls, "<startTime>5__3", "System.Single", 0x34)),
                BitConverter.SingleToInt32Bits(Value<float>(root, cls, "<nextParticleTime>5__4", "System.Single", 0x38)));
            var first = Read(); var second = Read();
            if (first != second || !float.IsFinite(BitConverter.Int32BitsToSingle(first.StartBits))
                || !float.IsFinite(BitConverter.Int32BitsToSingle(first.NextParticleBits)))
                throw new InvalidOperationException("The actual Hold iterator changed between independent observations.");
            return second;
        }
        private float JudgeTime()
        {
            var value = Value<float>(_judge, _sourceProfile.JudgeClass, "_nowTime", "System.Single", 0x64);
            if (!float.IsFinite(value)) throw new InvalidDataException("The actual Hold judgement time is not finite.");
            return value;
        }
        private static bool SameTime(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        // Both modes are C# auto-properties in the pinned game. Capture and
        // every later check must name their actual declared storage fields.
        private (bool AutoPlay, bool FreeHold) ReadModes() =>
            (Value<bool>(_judge, _sourceProfile.JudgeClass, "<AutoPlay>k__BackingField", "System.Boolean", 0x79),
             Value<bool>(_judge, _sourceProfile.JudgeClass, "<FreeHold>k__BackingField", "System.Boolean", 0x7a));
        private void VerifyModes()
        {
            var actual = ReadModes();
            if (actual.AutoPlay != _autoPlay || actual.FreeHold != _freeHold)
                throw new InvalidOperationException("The actual Hold input mode changed during this epoch.");
        }
        private void VerifyOriginalFingers()
        {
            var fields = _fingers!.ReadFields();
            if (fields.Identity != _fingerFields.Identity || !fields.Notes.SequenceEqual(_fingerFields.Notes)
                || _fingerBaseline.Any(pair => !SeekFingerIdsState.Same(_fingers.ReadList(pair.Key), pair.Value)))
                throw new InvalidOperationException("The original complete Hold finger storage changed.");
        }
        private void VerifyTargetFingers()
        {
            var fields = _fingers!.ReadFields();
            if (fields.Identity != _fingerFields.Identity || fields.Notes.Count != FingerTargets.Count)
                throw new InvalidOperationException("The target Hold input graph changed.");
            for (var i = 0; i < fields.Notes.Count; ++i)
            {
                var actual = fields.Notes[i]; var expected = FingerTargets[i];
                if (actual.Reference != expected.Reference || (actual.ListReference == 0) != (expected.FingerIds is null))
                    throw new InvalidOperationException("The actual Hold input field differs from its complete target.");
                if (actual.ListReference != 0)
                {
                    var list = _fingers.ReadList(actual.ListReference);
                    if (!list.CapacitySlots.Take(list.Size).SequenceEqual(expected.FingerIds!))
                        throw new InvalidOperationException("The actual Hold target finger IDs changed.");
                }
            }
        }
        // A controlled native call may not silently adopt new flags or list
        // versions. Preserve its exact pre-call observations and compare all
        // selected storage after the physical finally has returned.
        private sealed record Inputs(SeekNoteFlagTarget[] Flags, SeekFingerIdsFieldSnapshot Fields,
            Dictionary<long, SeekFingerIdsListStorage> Lists);
        private Inputs ReadInputs()
        {
            var flags = _fingers!.Read().ToArray(); var fields = _fingers.ReadFields();
            return new(flags, fields, fields.Notes.Where(row => row.ListReference != 0).Select(row => row.ListReference)
                .Distinct().ToDictionary(reference => reference, reference => SeekFingerIdsState.Copy(_fingers.ReadList(reference), Local)));
        }
        private void VerifyInputs(Inputs expected)
        {
            var actual = ReadInputs();
            if (!actual.Flags.SequenceEqual(expected.Flags) || actual.Fields.Identity != expected.Fields.Identity
                || !actual.Fields.Notes.SequenceEqual(expected.Fields.Notes) || actual.Lists.Count != expected.Lists.Count
                || expected.Lists.Any(pair => !actual.Lists.TryGetValue(pair.Key, out var list) || !SeekFingerIdsState.Same(list, pair.Value)))
                throw new InvalidOperationException("The controlled Hold original changed note flags or exact finger storage.");
        }
        private void RestoreIteratorState(Iterator item)
        {
            var current = ReadIterator(item.Root); var original = item.Original;
            // Only the known paused yield-state transition is an owned inverse.
            // Other iterator fields must remain identical; no fabricated state
            // can erase an unexpected judgement/input side effect.
            if (current != original && (original.State != 2 || current.State != 3
                || (current with { State = original.State }) != original))
                throw new InvalidOperationException("The actual Hold restart changed more than its recognized paused yield state.");
            if (current.State != original.State)
                Write(item.Root, _profile.IteratorClass, "<>1__state", "System.Int32", 0x10, current.State, original.State);
            item.Current = ReadIterator(item.Root);
            if (item.Current != original) throw new InvalidOperationException("The retained original iterator state did not restore.");
        }
    }
}
