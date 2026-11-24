using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockRootedSource
{
    private bool _preparingValueWriter;

    // Reserve before the first baseline getter can reenter this owner. The
    // operation pin owns lifetime; this flag owns only the single writer slot.
    private void BeginValueWriterPreparation()
    {
        Open();
        if (_preparingValueWriter || _timeWriter is not null || _pitchWriter is not null)
            throw new GameplayStateException(ErrorCode.Conflict,
                "This exact source already has a preparing or prepared value writer.");
        _preparingValueWriter = true;
    }

    private IDisposable EnterOperation()
    {
        VerifyThread();
        return new Operation(this, _operations.Enter());
    }
    private sealed class Operation : IDisposable
    {
        private readonly NativePlaybackStockRootedSource _owner;
        private readonly IDisposable _scope;
        private readonly Operation _registered;
        private bool _released;
        internal Operation(NativePlaybackStockRootedSource owner, IDisposable scope)
            => (_owner, _scope, _registered) = (owner, scope, this);
        public void Dispose()
        {
            _owner.VerifyThread();
            if (!ReferenceEquals(_registered, this)) throw new InvalidOperationException("Copied rooted read responsibility.");
            if (_released) return;
            _scope.Dispose(); _released = true;
            if (_owner._disposed)
            {
                // A reentrant getter can close the route, but its original
                // seven roots stay retained until the real accessor unwinds.
                // Cleanup failure remains retained and cannot hide that read's error.
                try { _owner.PumpCompleted(); }
                catch (Exception error) { _owner._fault ??= "Rooted operation retirement failed: " + error.Message; }
            }
        }
    }
}
