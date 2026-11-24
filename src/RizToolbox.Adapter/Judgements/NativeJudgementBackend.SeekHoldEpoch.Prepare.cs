using RizToolbox.Adapter.Gameplay;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    internal sealed partial class SeekHoldEpoch
    {
        internal void Prepare()
        {
            Local(); _target.VerifySource(); _observer.VerifyDrained();
            if (!_run.Run.SameGeneration(_target.Run)) throw new InvalidOperationException("The independent Hold run differs.");
            _profile = _prepareProfile(_owner._api, _sourceProfile, _observer)
                ?? throw new InvalidOperationException("The actual Hold epoch profile is absent.");
            _fingers = new(_owner, _run, _sourceProfile, fingerIds: true);
            _fingers.BindLifetimeGuard(Local);
            _judge = Adopt(new(JudgeAddress), _sourceProfile.JudgeClass);
            _originalCount = _expectedCount = Count(); _originalJudgeTime = JudgeTime();
            (_autoPlay, _freeHold) = ReadModes();
            _fingerFields = _fingers.ReadFields();
            if (_fingerFields.Identity != JudgeIdentity.Notes) throw new InvalidOperationException("The Hold input source differs from its actual target notes.");
            foreach (var field in _fingerFields.Notes)
            {
                Adopt(new(field.Reference), _owner._note);
                if (field.ListReference != 0 && !_fingerBaseline.ContainsKey(field.ListReference))
                    _fingerBaseline.Add(field.ListReference, SeekFingerIdsState.Copy(_fingers.ReadList(field.ListReference), Local));
            }
            foreach (var record in _owner.HoldLifecycle.CaptureSeekRecords(this))
            {
                record.NativeOwnerBinding!.Source.VerifySource();
                var root = Adopt(new(record.Identity.Enumerator), _profile.IteratorClass);
                var state = ReadIterator(root);
                if (state.State is < 1 or > 4 || state.Owner != JudgeAddress || state.Note != record.Identity.Note
                    || !record.MoveObserved || record.YieldedMoves == 0)
                    throw new GameplayStateException(ErrorCode.NotReady, "An actual Hold predecessor is not a completed yielded iterator.");
                _original.Add(new(record, root, state));
            }
            if (_originalCount < 0 || _originalCount != _original.Count)
                throw new GameplayStateException(ErrorCode.NotReady, "The actual Hold count includes an unobserved predecessor.");
            var required = _target.CrossingHolds.Where(row => row.Disposition == SeekCrossingHoldDisposition.ContinuationRequired)
                .Select(row => row.Reference).ToArray();
            if (required.Distinct().Count() != required.Length) throw new InvalidDataException("Duplicate Hold continuation targets.");
            foreach (var note in required)
            {
                var old = _original.Where(row => row.Original.Note == note).ToArray();
                if (old.Length > 1) throw new GameplayStateException(ErrorCode.NotReady, "A Hold target has more than one actual predecessor.");
                var field = _fingerFields.Notes.Single(row => row.Reference == note);
                if (old.Length == 0 && !_autoPlay)
                    throw new GameplayStateException(ErrorCode.NotReady, "A manual closed-head Hold needs its actual predecessor direction and input; no finger or direction is synthesized.");
                if (!_autoPlay && !_freeHold && (field.ListReference == 0 || _fingerBaseline[field.ListReference].Size == 0))
                    throw new GameplayStateException(ErrorCode.NotReady, "The actual manual Hold predecessor has no retained finger IDs.");
                // AutoPlay's fixed Move body skips the direction/input branch.
                _directions.Add(note, old.Length == 1 && old[0].Original.Left);
            }
            RequiresContinuation = Array.AsReadOnly(required);
            var continued = required.ToHashSet();
            FingerTargets = Array.AsReadOnly(_target.FingerTargets.Select(row =>
            {
                var field = _fingerFields.Notes.Single(value => value.Reference == row.Reference);
                if (!continued.Contains(row.Reference) || field.ListReference == 0)
                    return new SeekFingerIdsTarget(row.Reference, field.ListReference == 0 ? null : Array.Empty<int>());
                var list = _fingerBaseline[field.ListReference];
                return new SeekFingerIdsTarget(row.Reference, Array.AsReadOnly(list.CapacitySlots.Take(list.Size).ToArray()));
            }).ToArray());
            if (!FingerTargets.Select(row => row.Reference).SequenceEqual(_fingerFields.Notes.Select(row => row.Reference)))
                throw new InvalidOperationException("The Hold input targets do not cover the complete actual note order.");
            _prepared = true; VerifyPreparedCore();
        }
        private void VerifyPreparedCore()
        {
            Source(); _observer.VerifyDrained(); _profile.VerifyCurrent(); _target.VerifySource();
            if (!_prepared || _stopped || _effectsEntered || Count() != _originalCount || !SameTime(JudgeTime(), _originalJudgeTime))
                throw new InvalidOperationException("The original Hold epoch is not at its captured paused boundary.");
            VerifyModes(); VerifyOriginalFingers(); VerifyRecords(_original, stopped: false);
            foreach (var item in _original)
                if (ReadIterator(item.Root) != item.Original) throw new InvalidOperationException("An original Hold iterator changed before Stop.");
        }
        internal void VerifyPrepared() => Operation(() => { VerifyPreparedCore(); return 0; });
        private void VerifyRecords(IReadOnlyList<Iterator> expected, bool stopped)
        {
            var active = _owner.HoldLifecycle.CaptureSeekRecords(this);
            var wanted = stopped ? Array.Empty<HoldCoroutineLifecycle.Record>() : expected.Select(item => item.Record).ToArray();
            if (active.Length != wanted.Length || active.Any(row => !wanted.Any(item => ReferenceEquals(row, item))))
                throw new InvalidOperationException("The actual scheduled Hold identities differ from this private epoch phase.");
            foreach (var item in expected)
            {
                var record = item.Record;
                if (record.SeekStopped != stopped || record.Unknown || record.NaturalCompletion || record.InFlight != 0
                    || !record.StartReturned || record.Handle == 0)
                    throw new InvalidOperationException("An original Hold scheduling record is not in its expected phase.");
                record.NativeOwnerBinding!.Source.VerifySource();
            }
        }
    }
}
