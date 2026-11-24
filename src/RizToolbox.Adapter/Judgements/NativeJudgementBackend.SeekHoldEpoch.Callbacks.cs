using RizToolbox.Adapter.AudioContent;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    internal sealed partial class SeekHoldEpoch
    {
        internal bool ObserveStopCapture(HoldStopObservation observation)
        {
            Local();
            if (!_callingStop) return false;
            if (_stopObservation is not null || !ReferenceEquals(observation.Owner, _owner)
                || observation.Parent is not null || observation.Operation != NativeCoroutineCancellationOperation.StopAll
                || !observation.Frame.IsValid || observation.Frame.Self.ToInt64() != JudgeAddress)
                throw new InvalidOperationException("The controlled Hold Stop crossed its original receiver or physical call.");
            _stopObservation = observation; return true;
        }
        internal void ObserveStopCompletion(HoldStopObservation observation, NativeOperationExitFlags flags)
        {
            if (!ReferenceEquals(observation, _stopObservation)) return;
            Local();
            if (!_callingStop || flags != NativeOperationExitFlags.Returned || !observation.Completed)
            {
                ObserveStopFault(observation, "The actual controlled Hold Stop did not return normally.");
                throw new InvalidOperationException(_failure);
            }
            _stopReturned = true;
        }
        internal void ObserveStopFault(HoldStopObservation observation, string reason)
        {
            if (!ReferenceEquals(observation, _stopObservation)) return;
            _unknown = true; _stopReturned = false; _failure ??= reason; Retain();
        }
        internal void ObserveStartCapture(HoldStartObservation observation)
        {
            Local(); if (!_callingStart) return;
            var identity = observation.Scope.Identity;
            if (_startObservation is not null || identity.Enumerator != _startingEnumerator
                || identity.Owner != JudgeAddress || !identity.Run.SameGeneration(Run)
                || !ReferenceEquals(observation.Scope.Owner, _owner.HoldLifecycle))
                throw new InvalidOperationException("The controlled Hold Start crossed its actual retained iterator.");
            _startObservation = observation;
        }
    }
}
