namespace RizToolbox.Adapter.AudioContent;

internal enum NativeLocalActiveConsumerObligation
{
    ReturnRequest, ProviderWithdrawal, FactoryDeathSeal, ReceiverDeathSources,
    TrackerGenerations, InlineCompletion, SchedulerRows, WaitPublications,
    CancellationRequests, NativeActivity, HostActivity, UnityEventConsumers, CutoutDispatch,
    FutureNodeSource, FutureSchedulerSource, FutureWaitSource, AsyncConsumers,
    GlobalReferences, SceneResultConsumers
}
internal enum NativeLocalActiveConsumerProofState { Missing, Pending, Proven, Unknown }
internal sealed record NativeLocalActiveConsumerCheck(NativeLocalActiveConsumerObligation Obligation,
    NativeLocalActiveConsumerProofState State, string Detail);
internal sealed record NativeLocalActiveConsumerAssessment(bool MayClose,
    IReadOnlyList<NativeLocalActiveConsumerCheck> Checks, string Evidence);

/// <summary>Read-only facts from the coordinator's actual retained owner. An
/// implementation must establish the exact launch and its own native source;
/// this interface is not an external permission setter or a release token.</summary>
internal interface INativeLocalActiveConsumerClosureSource
{
    NativeLocalActiveConsumerCheck ReadActiveConsumerClosure(LocalRunLaunchCommitState exactLaunch,
        NativeLocalActiveConsumerObligation obligation);
}

/// <summary>Only the actual launch coordinator's single issued instance can
/// verify its return. Scalar assessments are diagnostic and cannot mint this
/// verifier, a registry token, production admission, or a root-free permit.</summary>
internal sealed class NativeLocalActiveConsumersEndedVerifier : ILocalRunConsumersEndedVerifier
{
    private readonly NativeLocalRunLaunchCoordinator _owner;
    private readonly LocalRunProtectionHost _host;
    private readonly LocalRunSessionRegistry.SessionHandle _session;
    private readonly object _returnIssuer;
    private readonly LocalRunRetirementRequest _request;
    private bool _verifying;
    internal NativeLocalActiveConsumerAssessment? LastAssessment { get; private set; }

    internal NativeLocalActiveConsumersEndedVerifier(NativeLocalRunLaunchCoordinator owner,
        LocalRunProtectionHost host, LocalRunSessionRegistry.SessionHandle session,
        object returnIssuer, LocalRunRetirementRequest request)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(returnIssuer);
        ArgumentNullException.ThrowIfNull(request);
        owner.RequireActiveConsumerIssuer(host, session, returnIssuer, request);
        (_owner, _host, _session, _returnIssuer, _request) = (owner, host, session, returnIssuer, request);
    }

    internal bool IsFor(LocalRunProtectionHost host, LocalRunSessionRegistry.SessionHandle session)
        => ReferenceEquals(_host, host) && ReferenceEquals(_session, session)
            && _owner.IsIssuedActiveConsumersVerifier(this, host, session, _returnIssuer, _request);

    public bool Verify(LocalRunRetirementRequest request, out string evidence)
    {
        evidence = string.Empty;
        if (_verifying || request != _request || !IsFor(_host, _session)) return false;
        _verifying = true;
        try
        {
            LastAssessment = _owner.AssessActiveConsumers(this, _session, _request);
            evidence = LastAssessment.Evidence;
            return LastAssessment.MayClose && IsFor(_host, _session);
        }
        finally { _verifying = false; }
    }

    // Called after the registry's verification callback and the host's next
    // native drain, immediately before classification withdrawal/Close.
    internal bool ReverifyBeforeClose(LocalRunProtectionHost host, LocalRunSessionRegistry.SessionHandle session)
        => IsFor(host, session) && Verify(Request(host.Sessions.Read(session)), out _);

    internal static LocalRunRetirementRequest Request(LocalRunSessionSnapshot value)
        => new(value.Identity, value.ControllerIdentity, value.ResultSceneIdentity,
            value.ResultSceneGeneration, value.Result?.NativeResultIdentity ?? 0);
    internal static bool MatchesProtectedReturn(LocalRunRetirementRequest expected, LocalRunSessionSnapshot actual)
        => actual.ReturnRequested && actual.Phase == LocalRunSessionPhase.Returning
            && !actual.CleanupAttempted && !actual.ProtectionRetired && Request(actual) == expected;

    // This pure completeness checker is shared with finite regressions. Its
    // successful result is not accepted directly by any production Close API.
    internal static NativeLocalActiveConsumerAssessment Assess(IEnumerable<NativeLocalActiveConsumerCheck> observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        var input = observed.ToArray(); var checks = new List<NativeLocalActiveConsumerCheck>();
        foreach (var obligation in Enum.GetValues<NativeLocalActiveConsumerObligation>())
        {
            var rows = input.Where(row => row is not null && row.Obligation == obligation).ToArray();
            checks.Add(rows.Length switch {
                0 => new(obligation, NativeLocalActiveConsumerProofState.Missing, "No exact owner supplied this obligation."),
                1 => rows[0] with { Detail = Bounded(rows[0].Detail) },
                _ => new(obligation, NativeLocalActiveConsumerProofState.Unknown, "Duplicate consumer evidence cannot replace one exact owner.")
            });
        }
        var valid = input.All(row => row is not null && Enum.IsDefined(row.Obligation)
            && Enum.IsDefined(row.State) && !string.IsNullOrWhiteSpace(row.Detail));
        var ready = valid && checks.All(row => row.State == NativeLocalActiveConsumerProofState.Proven);
        var reason = ready ? "Exact active consumer obligations verified; source/receiver/generation/return identities retained."
            : string.Join("; ", checks.Where(row => row.State != NativeLocalActiveConsumerProofState.Proven)
                .Select(row => $"{row.Obligation}={row.State}: {row.Detail}"));
        if (!valid) reason = "Malformed consumer evidence. " + reason;
        return new(ready, Array.AsReadOnly(checks.ToArray()), Bounded(reason));
    }
    private static string Bounded(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Missing owner evidence." : value.Length > 768 ? value[..768] : value;
}
