using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum NativeLocalObserverRetirementPhase { Prepared, Withdrawn, FreeAttempted, Completed, Unknown }

/// <summary>Retained exact objects, never an external boolean permission. The
/// actual launch coordinator rechecks its closed session/native drain on each
/// use. A distinct controlled context cannot retire any production owner.</summary>
internal sealed class NativeLocalActiveObserverRetirementContext
{
    private readonly OwnedNativeInactiveController _factory;
    private readonly NativeLocalActiveDestructionTracker.FactoryRetirementSeal _death;
    private readonly NativeLocalActiveSourceClosureSeal? _sources;
    private readonly NativeLocalRunLaunchCoordinator? _authority;
    private readonly Action? _controlledVerify;
    internal LocalRunLaunchCommitState Launch { get; }
    internal LocalRunSessionIdentity Session => Launch.Identity;
    internal bool Production => _authority is not null;
    internal NativeLocalActiveObserverRetirementContext(LocalRunLaunchCommitState launch,
        OwnedNativeInactiveController factory, NativeLocalActiveDestructionTracker.FactoryRetirementSeal death,
        NativeLocalActiveSourceClosureSeal sources, NativeLocalRunLaunchCoordinator authority)
    {
        ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(death); ArgumentNullException.ThrowIfNull(sources); ArgumentNullException.ThrowIfNull(authority);
        if (!death.Production || !factory.IsProductionActiveFactory || !sources.Production)
            throw new InvalidOperationException("A production observer context requires its actual production source and factory.");
        (Launch, _factory, _death, _sources, _authority) = (launch, factory, death, sources, authority);
        Verify();
    }
    private NativeLocalActiveObserverRetirementContext(LocalRunLaunchCommitState launch,
        OwnedNativeInactiveController factory, NativeLocalActiveDestructionTracker.FactoryRetirementSeal death, Action controlledVerify)
    {
        if (death.Production || factory.IsProductionActiveFactory)
            throw new InvalidOperationException("Controlled observer cleanup cannot consume a production source.");
        (Launch, _factory, _death, _controlledVerify) = (launch, factory, death, controlledVerify); Verify();
    }
    internal static NativeLocalActiveObserverRetirementContext ForControlled(LocalRunLaunchCommitState launch,
        OwnedNativeInactiveController factory, NativeLocalActiveDestructionTracker.FactoryRetirementSeal death, Action verify)
    {
        ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(death); ArgumentNullException.ThrowIfNull(verify);
        return new(launch, factory, death, verify);
    }
    internal void Verify()
    {
        _death.ValidateFor(Launch, _factory);
        _factory.VerifyActiveRootsClosedForObservationRetirement(Launch, _death);
        if (_authority is not null)
        {
            _sources!.ValidateFor(Launch, _factory);
            foreach (var obligation in new[] { NativeLocalActiveConsumerObligation.FutureNodeSource,
                NativeLocalActiveConsumerObligation.FutureSchedulerSource, NativeLocalActiveConsumerObligation.FutureWaitSource })
                if (_sources.ReadActiveConsumerClosure(Launch, obligation).State != NativeLocalActiveConsumerProofState.Proven)
                    throw new InvalidOperationException("Observer retirement lost an independently proven owned future-source closure.");
            _authority.VerifyActiveControllerFactoryRetirement(_factory, _death);
        }
        else _controlledVerify!();
    }
}

internal interface INativeLocalObserverRetirementOwner
{
    void RequireRetirementTransaction(NativeLocalObserverRetirementTransaction transaction,
        NativeLocalActiveObserverRetirementContext context);
}
internal interface INativeLocalObserverRetirementBackend
{
    IDisposable[] RetirementBindings();
    ExactHookBroker.ManagedBoundaryActivity ReadRetirementActivity();
    int RetirementRootSlots { get; }
    int RetirementRootCount { get; }
    bool HasRetirementRoot(int slot);
    void FreeRetirementRootOnce(int slot);
}

internal sealed class NativeLocalObserverRetirementReceipt
{
    private readonly NativeLocalObserverRetirementTransaction _transaction;
    internal LocalRunSessionIdentity Session { get; }
    internal int WithdrawnBindings { get; }
    internal int ReleasedRoots { get; }
    internal NativeLocalObserverRetirementReceipt(NativeLocalObserverRetirementTransaction transaction, object issuer,
        LocalRunSessionIdentity session, int bindings, int roots)
    {
        transaction.RequireIssuingReceipt(issuer);
        (_transaction, Session, WithdrawnBindings, ReleasedRoots) = (transaction, session, bindings, roots);
    }
    internal void ValidateFor(NativeLocalActiveObserverRetirementContext context, object exactOwner)
        => _transaction.RequireCompletedReceipt(this, context, exactOwner);
}

/// <summary>Actual owner-held transaction. Its receipt constructor cannot pass
/// the owner's private transaction identity through a caller-created copy.
/// Withdrawal is followed by real physical activity reads; it is never drain.</summary>
internal sealed class NativeLocalObserverRetirementTransaction
{
    private readonly IMainThreadDispatcher _main;
    private readonly INativeLocalObserverRetirementOwner _owner;
    private readonly NativeLocalActiveObserverRetirementContext _context;
    private readonly object _issuer = new();
    private NativeLocalObserverRetirementReceipt? _receipt;
    private bool _busy, _issuing;
    private int _withdrawn, _released;
    internal NativeLocalObserverRetirementPhase Phase { get; private set; }
    internal bool IsCompleted => Phase == NativeLocalObserverRetirementPhase.Completed;
    internal NativeLocalObserverRetirementTransaction(IMainThreadDispatcher main,
        INativeLocalObserverRetirementOwner owner, NativeLocalActiveObserverRetirementContext context)
        => (_main, _owner, _context) = (main, owner, context);

    internal NativeLocalObserverRetirementReceipt? TryComplete(NativeLocalActiveObserverRetirementContext context,
        INativeLocalObserverRetirementBackend backend, Action verifyOwnerFacts, Action markCompleted, Action<string> markUnknown)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(backend); ArgumentNullException.ThrowIfNull(verifyOwnerFacts);
        ArgumentNullException.ThrowIfNull(markCompleted); ArgumentNullException.ThrowIfNull(markUnknown);
        if (!ReferenceEquals(context, _context) || _busy || Phase == NativeLocalObserverRetirementPhase.Unknown)
            throw new InvalidOperationException("Observer retirement cannot switch its exact context, reenter or retry Unknown.");
        if (_receipt is not null) { RequireCompletedReceipt(_receipt, context, _owner); return _receipt; }
        void Verify(bool requireDrained)
        {
            _owner.RequireRetirementTransaction(this, context); context.Verify(); verifyOwnerFacts();
            var activity = backend.ReadRetirementActivity();
            if (!activity.Known || activity.InFlight < 0 || requireDrained && activity.InFlight != 0)
                throw new InvalidOperationException("Actual observer transport is unknown or changed its drained boundary.");
        }
        _busy = true;
        try
        {
            Verify(requireDrained: false);
            if (Phase == NativeLocalObserverRetirementPhase.Prepared)
            {
                var bindings = backend.RetirementBindings();
                if (bindings is null || bindings.Length is 0 or > 64 || bindings.Any(binding => binding is null)
                    || bindings.Distinct(ReferenceEqualityComparer.Instance).Count() != bindings.Length)
                    throw new InvalidOperationException("The exact owner has incomplete or duplicate logical binding responsibility.");
                foreach (var binding in bindings) { Verify(requireDrained: false); binding.Dispose(); ++_withdrawn; Verify(requireDrained: false); }
                Phase = NativeLocalObserverRetirementPhase.Withdrawn;
            }
            var drain = backend.ReadRetirementActivity();
            if (!drain.Known || drain.InFlight < 0) throw new InvalidOperationException("Withdrawn observer's physical activity became Unknown.");
            if (drain.InFlight != 0) return null; // Retain all roots for a later actual drain.
            Verify(requireDrained: true);
            Phase = NativeLocalObserverRetirementPhase.FreeAttempted;
            if (backend.RetirementRootSlots is < 0 or > 2048)
                throw new InvalidOperationException("Observer root responsibility exceeds its fixed bound.");
            for (var slot = backend.RetirementRootSlots - 1; slot >= 0; --slot)
            {
                if (!backend.HasRetirementRoot(slot)) continue;
                Verify(requireDrained: true); backend.FreeRetirementRootOnce(slot); ++_released; Verify(requireDrained: true);
            }
            Verify(requireDrained: true);
            if (backend.RetirementRootCount != 0) throw new InvalidOperationException("An independently owned observer root did not complete its one free.");
            _issuing = true;
            try { _receipt = new(this, _issuer, _context.Session, _withdrawn, _released); }
            finally { _issuing = false; }
            Phase = NativeLocalObserverRetirementPhase.Completed;
            markCompleted(); return _receipt;
        }
        catch (Exception error)
        {
            Phase = NativeLocalObserverRetirementPhase.Unknown;
            try { markUnknown("Observer retirement retained Unknown: " + error.GetType().Name); } catch { }
            throw;
        }
        finally { _busy = false; }
    }
    internal void RequireIssuingReceipt(object issuer)
    {
        if (!_issuing || !ReferenceEquals(issuer, _issuer) || Phase != NativeLocalObserverRetirementPhase.FreeAttempted)
            throw new InvalidOperationException("Only this actual owner transaction may publish its completed receipt.");
    }
    internal void RequireCompletedReceipt(NativeLocalObserverRetirementReceipt receipt,
        NativeLocalActiveObserverRetirementContext context, object exactOwner)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(_receipt, receipt) || !ReferenceEquals(_context, context) || !ReferenceEquals(_owner, exactOwner)
            || Phase != NativeLocalObserverRetirementPhase.Completed || receipt.Session != context.Session)
            throw new InvalidOperationException("Observer retirement receipt is foreign, superseded or not completed.");
        _owner.RequireRetirementTransaction(this, context); context.Verify();
    }
}
