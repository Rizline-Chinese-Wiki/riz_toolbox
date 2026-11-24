namespace RizToolbox.Sdk;

public enum MutationStrategy { PriorityOverride, OrderedTransform, Exclusive }
public enum ExternalWritePolicy
{
    /// <summary>A game write becomes the new baseline; active mutations are recomputed on top of it.</summary>
    AdoptAndReapply,
    /// <summary>A game write ends all leases and remains untouched.</summary>
    PreserveExternalAndInvalidate,
    /// <summary>Explicit ownership: reassert the mutation while held. Release still restores only a value last written by this channel.</summary>
    RejectExternalWhileHeld
}

public sealed record MutationKey<T>(TargetHandle<T> Target, string Property);
public sealed record MutationHolder<T>(string LeaseId, string Owner, MutationStrategy Strategy,
    int Priority, long RegistrationOrder, T? Candidate, bool Winner, string Reason);
public sealed record MutationSnapshot<T>(MutationKey<T> Key, bool Valid, MutationStrategy Strategy,
    ExternalWritePolicy ExternalWrites, T Baseline, T Effective, IReadOnlyList<MutationHolder<T>> Holders,
    string? LastError = null);

public interface IMutationLease<T> : IDisposable
{
    string LeaseId { get; }
    MutationKey<T> Key { get; }
    bool IsActive { get; }
    /// <summary>Updates an override or exclusive value. Transform leases reject SetValue.</summary>
    OperationResult SetValue(T value);
}

/// <summary>Only adapter-declared, generation-specific properties can be mutated. All returned leases are scope-owned.</summary>
public interface IMutationService
{
    OperationResult<IMutationLease<T>> Override<T>(MutationKey<T> key, T value, int priority = 0);
    /// <summary>Pure transforms run by ascending order, then registration order. Never mutate the input snapshot.</summary>
    OperationResult<IMutationLease<T>> Transform<T>(MutationKey<T> key, Func<T, T> transform, int order = 0);
    OperationResult<IMutationLease<T>> AcquireExclusive<T>(MutationKey<T> key, T initialValue);
    OperationResult<MutationSnapshot<T>> Describe<T>(MutationKey<T> key);
}
