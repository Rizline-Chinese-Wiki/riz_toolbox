namespace RizToolbox.Sdk;

/// <summary>Stable numeric codes. Consumers must handle Unknown and future numeric values.</summary>
public enum ErrorCode
{
    Unknown = 0,
    InvalidArgument = 1,
    SdkIncompatible = 2,
    CapabilityUnavailable = 3,
    UnsupportedBuild = 4,
    UnsupportedSignature = 5,
    TargetExpired = 6,
    ScopeExpired = 7,
    WrongThread = 8,
    QueueFull = 9,
    Cancelled = 10,
    TimedOut = 11,
    DependencyUnsatisfied = 12,
    MigrationFailed = 13,
    MutationNotSupported = 14,
    Conflict = 15,
    ObservationUnavailable = 16,
    NotReady = 17,
    OperationUnsupported = 18,
    InvalidGameState = 19,
    TargetMissing = 20,
    AmbiguousTarget = 21,
    ResourceLoadFailed = 22,
    NavigationUnavailable = 23,
    ContentInvalid = 24,
    ConfigInvalid = 25,
    QuotaExceeded = 26,
    ServiceUnavailable = 27,
    InternalError = 28,
    ContractIdentityMismatch = 29,
    UnsupportedContract = 30,
    ServiceCallCycle = 31,
    ServiceCallDepthExceeded = 32,
    ServicePayloadTooLarge = 33,
    ServiceRequestRejected = 34,
    ServiceProviderFailed = 35,
    TargetTypeMismatch = 36,
    TargetGenerationExhausted = 37,
    MutationReentrancy = 38,
    MutationWriteFailed = 39,
    DependencyCycle = 40
}

public static class ErrorCodes
{
    /// <summary>Does not reinterpret messages. Unknown numeric values degrade to Unknown.</summary>
    public static ErrorCode Normalize(ErrorCode code) => Enum.IsDefined(typeof(ErrorCode), code) ? code : ErrorCode.Unknown;
    internal static OperationError RequireError(OperationError? error)
    {
        if (error is null) throw new ArgumentNullException(nameof(error));
        if (string.IsNullOrWhiteSpace(error.Message)) throw new ArgumentException("A structured failure requires a message.", nameof(error));
        return error;
    }
}

/// <summary>No exception/native pointer is part of this portable error. Retryable is a hint, not a retry instruction.</summary>
public sealed record OperationError(
    ErrorCode Code, string Message, string? CapabilityId = null, string? TargetId = null, bool Retryable = false)
{
    public ErrorCode KnownCode => ErrorCodes.Normalize(Code);
}

/// <summary>An operation either succeeds or carries a structured error. A nullable value alone does not signal failure.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(OperationResultJsonConverter))]
public sealed class Result
{
    public bool Success { get; }
    public OperationError? Error { get; }
    private Result(bool success, OperationError? error) => (Success, Error) = (success, error);
    public static Result Ok() => new(true, null);
    public static Result Fail(OperationError error) => new(false, ErrorCodes.RequireError(error));
}

[System.Text.Json.Serialization.JsonConverter(typeof(OperationResultJsonConverter))]
public sealed class Result<T>
{
    public bool Success { get; }
    public T? Value { get; }
    public OperationError? Error { get; }
    private Result(bool success, T? value, OperationError? error) => (Success, Value, Error) = (success, value, error);
    public static Result<T> Ok(T value) => new(true, value, null);
    public static Result<T> Fail(OperationError error) => new(false, default, ErrorCodes.RequireError(error));
}
