namespace RizToolbox.Adapter.Judgements;

internal sealed partial class HoldCoroutineLifecycle
{
    private Record? _seekRestart;
    private bool IsSeekRestart(Record record) => ReferenceEquals(_seekRestart, record);
    internal Record[] CaptureSeekRecords(NativeJudgementBackend.SeekHoldEpoch epoch)
    {
        _main.VerifyAccess(); epoch.VerifyLifecycle(this);
        lock (_gate)
        {
            if (_unknown || _disposed || _admissionClosed || _draining || _inFlight != 0)
                throw new InvalidOperationException("The actual Hold lifecycle is not known and drained for Seek.");
            var records = _records.Values.Where(record => record.Identity.Owner == epoch.JudgeAddress
                && record.Identity.Run.SameGeneration(epoch.Run) && !record.NaturalCompletion && !record.SeekStopped).ToArray();
            if (records.Any(record => record.Unknown || record.Epoch.Retired || record.InFlight != 0
                || !record.StartObserved || !record.StartReturned || record.Handle == 0
                || record.NativeOwnerBinding is null || record.Owned.AwaitingIndependentDeath))
                throw new InvalidOperationException("An actual Hold predecessor lacks its original completed scheduling identity.");
            return records;
        }
    }
    internal void MarkSeekStopped(NativeJudgementBackend.SeekHoldEpoch epoch, IReadOnlyList<Record> records)
    {
        _main.VerifyAccess(); epoch.VerifyStopCompleted(this);
        lock (_gate)
        {
            if (_unknown || _inFlight != 0) throw new InvalidOperationException("Hold callbacks did not drain after the actual Stop original.");
            foreach (var record in records)
            {
                VerifySeekRecord(epoch, record);
                record.SeekStopped = true;
            }
        }
    }
    private void VerifySeekRecord(NativeJudgementBackend.SeekHoldEpoch epoch, Record record)
    {
        if (!_records.TryGetValue(record.Identity.Enumerator, out var actual) || !ReferenceEquals(actual, record)
            || record.Identity.Owner != epoch.JudgeAddress || !record.Identity.Run.SameGeneration(epoch.Run)
            || record.InFlight != 0 || record.Unknown || record.Epoch.Retired)
            throw new InvalidOperationException("The actual retained Hold record changed at the controlled boundary.");
    }
    internal IDisposable BeginSeekRestart(NativeJudgementBackend.SeekHoldEpoch epoch, Record record)
    {
        _main.VerifyAccess(); epoch.VerifyRestart(this, record);
        lock (_gate)
        {
            VerifySeekRecord(epoch, record);
            if (_unknown || _inFlight != 0 || _seekRestart is not null || !record.SeekStopped || record.NaturalCompletion)
                throw new InvalidOperationException("The actual stopped Hold record has no controlled restart slot.");
            _seekRestart = record; return new SeekRestartScope(this, record);
        }
    }
    private sealed class SeekRestartScope : IDisposable
    {
        private readonly HoldCoroutineLifecycle _owner; private readonly Record _record; private bool _closed;
        internal SeekRestartScope(HoldCoroutineLifecycle owner, Record record) => (_owner, _record) = (owner, record);
        public void Dispose()
        {
            _owner._main.VerifyAccess(); if (_closed) return;
            lock (_owner._gate)
            {
                if (!ReferenceEquals(_owner._seekRestart, _record)) throw new InvalidOperationException("The original Hold restart scope changed.");
                _closed = true; _owner._seekRestart = null;
            }
        }
    }
}
