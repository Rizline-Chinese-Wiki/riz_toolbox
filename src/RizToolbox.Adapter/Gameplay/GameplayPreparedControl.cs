using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed record GameplayControlConfirmation(bool? Observed, string Evidence,
    double? Value = null, GameplayControlValueUnit Unit = GameplayControlValueUnit.None);
internal sealed record GameplayControlRestoration(bool OriginalStateRestored, string Evidence);

/// <summary>Prepare is read-only and owns every prerequisite/baseline needed by Commit. A value transaction
/// must validate every affected game/audio participant, commit once, read back the actual value and unit,
/// and verify restoration of all participants on failure. Dispose only releases preparation resources.</summary>
internal abstract class GameplayPreparedControl : IDisposable
{
    internal virtual bool CanRestore => false;
    internal virtual double ValueTolerance => 0;
    internal abstract void Commit();
    internal abstract GameplayControlConfirmation Confirm();
    internal virtual void Restore() => throw new NotSupportedException("This command has no validated restoration transaction.");
    /// <summary>Separate actual baseline readback after the restoration command; a returned setter is not restoration evidence.</summary>
    internal virtual GameplayControlRestoration ConfirmRestored() => new(false, "Original state has not been read back.");
    public abstract void Dispose();
}

/// <summary>Existing exact void commands retain their irreversible dispatch/readback semantics.</summary>
internal sealed class GameplayPreparedCommand : GameplayPreparedControl
{
    private readonly GameplayCapture _capture;
    private readonly GameplayControlAction _action;
    internal GameplayPreparedCommand(GameplayCapture capture, GameplayControlAction action) => (_capture, _action) = (capture, action);
    internal override void Commit() => _capture.Dispatch(_action);
    internal override GameplayControlConfirmation Confirm() => new(_capture.Postcondition(_action),
        "Exact instance void() command returned; immediate state is distinct from full gameplay/result behavior.");
    public override void Dispose() { }
}
