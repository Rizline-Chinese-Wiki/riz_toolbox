using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockRootedSource
{
    private readonly NativePlaybackFreshSourceCapture.Adoption? _freshAdoption;
    internal NativePlaybackStockRootedSource(NativeRuntime api, NativePlaybackFreshSourceCapture.Adoption adoption,
        NativePlaybackBinding binding, NativePlaybackRateOrigin origin, GameEventState.PlaybackRunAssociation run, PlaybackSourceGroupLease group,
        Action<ReadbackReceipt>? returned, Action<string>? withdrawn)
    {
        _freshAdoption = adoption ?? throw new ArgumentNullException(nameof(adoption));
        adoption.BindSource(this, api, run, group, binding, origin);
        _api = api; _group = group; _runAssociation = run; _returned = returned; _withdrawn = withdrawn;
        _binding = binding; _origin = origin; _actual = null!; _producer = null!; _runWithdrawal = null!;
        try
        {
            run.VerifyCurrent(_binding);
            _producer = new(group, new(_binding.Player), new(_binding.AudioSource), new(_binding.Progress), OnReturned);
            adoption.RegisterProducer(this, _producer); // Before any borrowed accessor/getter can reenter.
            _actual = new(api, adoption, this);
            VerifyGraph(); _lastDsp = _actual.ReadDsp();
            var initial = _actual.Read(checked((long)group.Epoch), 0);
            ValidateSample(initial); AcceptDsp(initial.DspSeconds); _selectedRate = initial.Pitch;
            _runWithdrawal = run.OnWithdrawn(_binding, reason => Fail(new GameplayStateException(ErrorCode.TargetExpired, reason)));
            VerifyGraph();
            // The original capture remains the sole native watch/root owner.
            // No native Claim, root retain or player-watch stage occurs here.
        }
        catch (Exception error)
        {
            _disposed = true; _fault ??= "Fresh rooted adoption failed: " + error.Message;
            adoption.Close(this, _fault); throw;
        }
        finally { adoption.FinishConstruction(); }
    }
    internal void VerifyFreshProducer(NativePlaybackFreshSourceCapture.Adoption adoption, NativePlaybackStockOccurrenceProducer producer)
    {
        VerifyThread(); adoption.VerifySourceRegistration(this);
        if (!ReferenceEquals(_freshAdoption, adoption) || !ReferenceEquals(_producer, producer))
            throw new GameplayStateException(ErrorCode.TargetExpired, "This is not the producer retained by the actual fresh rooted source.");
    }
    internal void StopFreshAdoption(NativePlaybackFreshSourceCapture.Adoption adoption, string reason)
    {
        VerifyThread();
        if (!ReferenceEquals(_freshAdoption, adoption))
            throw new GameplayStateException(ErrorCode.TargetExpired, "Another fresh adoption cannot stop this source.");
        adoption.VerifySourceRegistration(this);
        _operations.Close();
        _disposed = true; _fault ??= reason; _current = null; _resumeCountdown = null;
        _runWithdrawal?.Dispose();
        _producer?.Dispose(); // Keeps actual physical and writer duties until they exit.
    }
}
