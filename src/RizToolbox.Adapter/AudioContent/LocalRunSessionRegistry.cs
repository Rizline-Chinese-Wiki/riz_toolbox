using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum LocalRunSessionPhase
{ Prepared, PendingCommit, Committed, ResultCapturing, ResultReady, Returning, CleanupUnknown, Closed }
internal enum LocalBoundaryKind { ControllerRetry, ResultStart, ResultRetry, ResultBack, ControllerLevelOver }
internal enum LocalRunResultClaimKind { InvokeOnce, InProgress, Cached, Failed, Unavailable }
internal sealed record LocalRunSessionDescriptor(LocalContentId Song, long SongRevision, string ChartId);
internal sealed record LocalRunSessionIdentity(string SessionId, LocalContentId Song, long SongRevision,
    string ChartId, long RunGeneration);
internal sealed record LocalRunCapturedResult(LocalRunSessionIdentity Session, long NativeResultIdentity, RunFinished Copy);
internal sealed record LocalRunSessionSnapshot(LocalRunSessionIdentity Identity, LocalRunSessionPhase Phase,
    long MetadataIdentity, long ControllerIdentity, long ResultSceneIdentity, long ResultSceneGeneration,
    bool ProviderWithdrawn, bool ReturnRequested, bool ResultCallClaimed, bool CleanupAttempted,
    bool ProtectionRetired, int OutstandingOwnershipRecords, string? ConsumerEndEvidence, string? Failure, LocalRunCapturedResult? Result);
internal sealed record LocalRunRetirementRequest(LocalRunSessionIdentity Identity, long ControllerIdentity,
    long ResultSceneIdentity, long ResultSceneGeneration, long ResultIdentity);

/// <summary>
/// Host-only trust boundary. The native adapter must verify the controller and bound result-screen
/// Unity objects are destroyed, all scene/result consumers and in-flight old callbacks have ended,
/// globals no longer reference this run, and no old entry can occur again. A button click, scene
/// name change or metadata comparison alone cannot establish these facts.
/// This transaction layer does not perform or pretend to verify those native actions.
/// </summary>
internal interface ILocalRunConsumersEndedVerifier
{
    bool Verify(LocalRunRetirementRequest request, out string evidence);
}

/// <summary>
/// Internal LOCAL transaction and ownership registry.
/// Main-thread publication supplies immutable, root-free identity snapshots for hook classifiers.
/// Submitted ownership survives provider withdrawal and abandoned managed handles until verified retirement.
/// </summary>
internal sealed class LocalRunSessionRegistry
{
    internal const int MaximumProcessSessions = 32;
    private static readonly object ProcessGate = new();
    private static readonly HashSet<Entry> ProcessOwners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly HashSet<Entry> _entries = new();
    private PublishedIdentity[] _published = Array.Empty<PublishedIdentity>();
    private long _generation;
    private bool _busy;
    internal static int RetainedProcessSessionCount { get { lock (ProcessGate) return ProcessOwners.Count; } }

    private sealed record PublishedIdentity(SessionHandle Session, long Controller, long ResultScene);
    private sealed class Entry
    {
        internal LocalRunSessionRegistry Owner = null!;
        internal SessionHandle Handle = null!;
        internal LocalRunSessionIdentity Identity = null!;
        internal long Metadata, Controller, ResultScene, ResultSceneGeneration;
        internal LocalRunSessionPhase Phase;
        internal bool ProviderWithdrawn, ReturnRequested, ResultClaimed, CaptureFailed, CleanupAttempted, ProtectionRetired;
        internal string? Failure, ConsumerEndEvidence;
        // Complete bundle, controller root, result-scene root, actual-result root. Ownership is
        // recorded in fixed slots so adoption itself cannot allocate after the caller transfers it.
        internal readonly IDisposable?[] Ownership = new IDisposable?[4];
        internal CommitClaim? Commit;
        internal ResultCaptureClaim? Capture;
        internal object? Retirement;
        internal LocalRunCapturedResult? Result;
        internal LocalRunResultObservation? ResultObservation;
    }
    internal sealed class SessionHandle
    {
        internal readonly LocalRunSessionRegistry Owner;
        internal readonly object EntryIdentity;
        internal SessionHandle(LocalRunSessionRegistry owner, object entry) => (Owner, EntryIdentity) = (owner, entry);
    }
    internal sealed class CommitClaim
    {
        internal readonly SessionHandle Session;
        internal CommitClaim(SessionHandle session) => Session = session;
    }
    internal sealed class ResultCaptureClaim
    {
        internal readonly SessionHandle Session;
        internal ResultCaptureClaim(SessionHandle session) => Session = session;
    }
    internal sealed class ConsumersEndedToken
    {
        internal readonly SessionHandle Session;
        internal readonly object Retirement;
        internal readonly string Evidence;
        internal ConsumersEndedToken(SessionHandle session, object retirement, string evidence)
            => (Session, Retirement, Evidence) = (session, retirement, evidence);
    }
    internal sealed record ResultClaim(LocalRunResultClaimKind Kind, ResultCaptureClaim? Claim = null,
        LocalRunCapturedResult? Cached = null);

    internal LocalRunSessionRegistry(IMainThreadDispatcher main)
    { ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); _main = main; }

    internal int UnretiredSessionCount { get { _main.VerifyAccess(); return _entries.Count(entry => !entry.ProtectionRetired); } }

    /// <summary>Pure lookup on any thread. Never calls RootHandle.Target or reads native metadata.</summary>
    internal bool IsProtected(LocalBoundaryKind boundary, long receiverIdentity)
        => FindProtectedSession(boundary, receiverIdentity) is not null;

    /// <summary>Pure any-thread identity lookup. The returned host token owns no new root
    /// and never authorizes a receiver that has not already been exactly bound.</summary>
    internal SessionHandle? FindProtectedSession(LocalBoundaryKind boundary, long receiverIdentity)
    {
        if (receiverIdentity == 0) return null;
        var snapshot = Volatile.Read(ref _published);
        var controller = boundary is LocalBoundaryKind.ControllerRetry or LocalBoundaryKind.ControllerLevelOver;
        if (!controller && boundary is not (LocalBoundaryKind.ResultStart or LocalBoundaryKind.ResultRetry or LocalBoundaryKind.ResultBack)) return null;
        foreach (var value in snapshot)
            if ((controller ? value.Controller : value.ResultScene) == receiverIdentity) return value.Session;
        return null;
    }

    /// <summary>Adopts the complete prepared bundle only when registration succeeds.</summary>
    internal SessionHandle RegisterPrepared(LocalRunSessionDescriptor descriptor, long metadataIdentity, IDisposable ownedBundle,
        LocalRunResultObservation? resultObservation = null)
    {
        Guard(); ArgumentNullException.ThrowIfNull(descriptor); ArgumentNullException.ThrowIfNull(ownedBundle);
        if (descriptor.Song is null || !Identifier(descriptor.Song.Namespace) || !Identifier(descriptor.Song.Id)
            || !Identifier(descriptor.ChartId) || descriptor.SongRevision <= 0 || metadataIdentity == 0)
            throw new ArgumentException("A LOCAL run requires exact bounded song/chart/revision and retained metadata identity.");
        var generation = checked(_generation + 1);
        var entry = new Entry { Owner = this, Identity = new(_sessionId, descriptor.Song with { }, descriptor.SongRevision,
            descriptor.ChartId, generation), Metadata = metadataIdentity, Phase = LocalRunSessionPhase.Prepared };
        var handle = new SessionHandle(this, entry); entry.Handle = handle;
        _entries.EnsureCapacity(_entries.Count + 1);
        lock (ProcessGate)
        {
            RequireDistinctLocked(entry, metadataIdentity, ownedBundle);
            if (ProcessOwners.Count >= MaximumProcessSessions)
                throw new InvalidOperationException("LOCAL host sessions exhaust the bounded process ownership capacity.");
            ProcessOwners.EnsureCapacity(ProcessOwners.Count + 1);
            ProcessOwners.Add(entry); _entries.Add(entry);
            entry.Ownership[0] = ownedBundle; _generation = generation;
        }
        if (resultObservation?.TryBind(entry.Identity) == true) entry.ResultObservation = resultObservation;
        return handle;
    }

    /// <summary>Must happen before clone/activation. This is a transaction claim, not native commit evidence.</summary>
    internal CommitClaim BeginCommit(SessionHandle session)
    {
        Guard(); var entry = Get(session); RequirePhase(entry, LocalRunSessionPhase.Prepared);
        var claim = new CommitClaim(session); entry.Commit = claim; entry.Phase = LocalRunSessionPhase.PendingCommit;
        return claim;
    }

    /// <summary>Authorizes only this session's actual adopted bundle and current pending claim.
    /// A matching numeric metadata pointer alone cannot authorize exporting another owner's parameters.</summary>
    internal void VerifyPendingBundleClaim(CommitClaim claim, long metadataIdentity, IDisposable ownedBundle)
    {
        Guard(); ArgumentNullException.ThrowIfNull(claim); ArgumentNullException.ThrowIfNull(ownedBundle);
        var entry = Get(claim.Session); RequirePhase(entry, LocalRunSessionPhase.PendingCommit);
        if (!ReferenceEquals(entry.Commit, claim) || entry.Metadata != metadataIdentity || !ReferenceEquals(entry.Ownership[0], ownedBundle))
            throw new InvalidOperationException("LOCAL pending claim does not own this exact prepared bundle and metadata.");
    }

    /// <summary>
    /// Adopts an externally validated exact controller root. The native adapter must bind at a safe
    /// pre-side-effect boundary; a pending session alone never classifies arbitrary receivers as LOCAL.
    /// </summary>
    internal void BindPendingController(CommitClaim claim, long controllerIdentity, IDisposable controllerRoot)
    {
        Guard(); ArgumentNullException.ThrowIfNull(claim); ArgumentNullException.ThrowIfNull(controllerRoot);
        var entry = Get(claim.Session); RequirePhase(entry, LocalRunSessionPhase.PendingCommit);
        if (!ReferenceEquals(entry.Commit, claim) || entry.Controller != 0 || controllerIdentity == 0)
            throw new InvalidOperationException("A current pending commit may bind its exact controller once.");
        lock (ProcessGate)
        {
            RequireDistinctLocked(entry, controllerIdentity, controllerRoot);
            var published = PublishCandidate(entry, controllerIdentity, 0);
            entry.Controller = controllerIdentity; entry.Ownership[1] = controllerRoot;
            Volatile.Write(ref _published, published);
        }
    }

    internal void Commit(CommitClaim claim)
    {
        Guard(); ArgumentNullException.ThrowIfNull(claim); var entry = Get(claim.Session);
        RequirePhase(entry, LocalRunSessionPhase.PendingCommit);
        if (!ReferenceEquals(entry.Commit, claim) || entry.Controller == 0)
            throw new InvalidOperationException("Commit requires the current pending claim and a retained exact controller.");
        entry.Phase = LocalRunSessionPhase.Committed;
    }

    /// <summary>Adopts only the specific result screen belonging to this run's verified transition.</summary>
    internal void BindResultScene(SessionHandle session, long resultSceneIdentity, long sceneGeneration, IDisposable resultSceneRoot)
    {
        Guard(); ArgumentNullException.ThrowIfNull(resultSceneRoot); var entry = Get(session);
        if (entry.Phase is not (LocalRunSessionPhase.Committed or LocalRunSessionPhase.ResultCapturing or LocalRunSessionPhase.ResultReady)
            || entry.ResultScene != 0 || resultSceneIdentity == 0 || sceneGeneration <= 0)
            throw new InvalidOperationException("Only a committed LOCAL run can bind its exact result scene once.");
        lock (ProcessGate)
        {
            RequireDistinctLocked(entry, resultSceneIdentity, resultSceneRoot);
            var published = PublishCandidate(entry, entry.Controller, resultSceneIdentity);
            entry.ResultScene = resultSceneIdentity; entry.ResultSceneGeneration = sceneGeneration; entry.Ownership[2] = resultSceneRoot;
            Volatile.Write(ref _published, published);
        }
    }

    /// <summary>The caller must perform the real ControllerOver only for InvokeOnce, never for another outcome.</summary>
    internal ResultClaim TryClaimResult(SessionHandle session)
    {
        Guard(); var entry = Get(session);
        if (entry.Phase == LocalRunSessionPhase.Closed || entry.ProtectionRetired) return new(LocalRunResultClaimKind.Unavailable);
        if (entry.Result is not null) return new(LocalRunResultClaimKind.Cached, Cached: entry.Result);
        if (entry.CaptureFailed) return new(LocalRunResultClaimKind.Failed);
        if (entry.ResultClaimed) return new(LocalRunResultClaimKind.InProgress);
        if (entry.Phase != LocalRunSessionPhase.Committed) return new(LocalRunResultClaimKind.Unavailable);
        var capture = new ResultCaptureClaim(session); var outcome = new ResultClaim(LocalRunResultClaimKind.InvokeOnce, capture);
        entry.Capture = capture; entry.ResultClaimed = true; entry.Phase = LocalRunSessionPhase.ResultCapturing;
        return outcome;
    }

    /// <summary>Adopts an externally validated actual-result root only after exact metadata and claim matching.</summary>
    internal LocalRunCapturedResult CompleteResult(ResultCaptureClaim claim, long resultIdentity, long resultMetadataIdentity,
        RunFinished copiedResult, IDisposable actualResultRoot)
    {
        Guard(); ArgumentNullException.ThrowIfNull(copiedResult); ArgumentNullException.ThrowIfNull(actualResultRoot);
        var entry = CurrentCapture(claim);
        if (resultIdentity == 0 || resultMetadataIdentity != entry.Metadata)
        {
            FailCapture(entry, "Actual result identity or retained metadata differs from this LOCAL run.");
            throw new InvalidOperationException(entry.Failure);
        }
        lock (ProcessGate)
        {
            RequireDistinctLocked(entry, resultIdentity, actualResultRoot);
            var result = new LocalRunCapturedResult(entry.Identity, resultIdentity, copiedResult with { });
            entry.Ownership[3] = actualResultRoot; entry.Result = result; entry.Phase = LocalRunSessionPhase.ResultReady;
            entry.ResultObservation?.Complete(result.Copy);
            entry.ResultObservation = null;
            return result;
        }
    }

    internal void FailResult(ResultCaptureClaim claim, string failure)
    {
        Guard(); var entry = CurrentCapture(claim);
        FailCapture(entry, BoundedEvidence(failure));
    }

    internal void MarkProviderWithdrawn(SessionHandle session)
    {
        Guard(); var entry = Get(session);
        if (entry.Phase == LocalRunSessionPhase.Closed) throw new InvalidOperationException("Closed LOCAL sessions cannot be withdrawn again.");
        entry.ProviderWithdrawn = true;
    }

    internal void BeginReturn(SessionHandle session)
    {
        Guard(); var entry = Get(session);
        if (entry.Phase == LocalRunSessionPhase.Closed || entry.CleanupAttempted || entry.Phase == LocalRunSessionPhase.ResultCapturing)
            throw new InvalidOperationException("An in-flight result or attempted cleanup cannot begin another return.");
        if (entry.ReturnRequested) return;
        var retirement = new object(); entry.Retirement = retirement; entry.ReturnRequested = true;
        if (entry.Phase != LocalRunSessionPhase.CleanupUnknown) entry.Phase = LocalRunSessionPhase.Returning;
        EndResultObservation(entry, ErrorCode.TargetExpired, "The LOCAL launch ended before an exact result was captured.");
    }

    /// <summary>No token is issued until an external native verifier confirms every consumer/global responsibility has ended.</summary>
    internal ConsumersEndedToken? VerifyConsumersEnded(SessionHandle session, ILocalRunConsumersEndedVerifier verifier)
    {
        Guard(); ArgumentNullException.ThrowIfNull(verifier); var entry = Get(session);
        if (!entry.ReturnRequested || entry.Retirement is null || entry.CleanupAttempted)
            throw new InvalidOperationException("Retirement verification requires an active return with no attempted cleanup.");
        var request = new LocalRunRetirementRequest(entry.Identity, entry.Controller, entry.ResultScene,
            entry.ResultSceneGeneration, entry.Result?.NativeResultIdentity ?? 0);
        _busy = true;
        try
        {
            if (!verifier.Verify(request, out var evidence)) return null;
            return new(session, entry.Retirement, BoundedEvidence(evidence));
        }
        finally { _busy = false; }
    }

    /// <summary>
    /// Retires classification before releasing any native root: the exact verifier token proves
    /// all old entries are unreachable. Unknown frees keep failed ownership/quota responsibility,
    /// never obsolete pointer classification; freed object addresses may subsequently be reused.
    /// </summary>
    internal bool Close(SessionHandle session, ConsumersEndedToken token)
    {
        Guard(); ArgumentNullException.ThrowIfNull(token); var entry = Get(session);
        if (!ReferenceEquals(token.Session, session) || !ReferenceEquals(token.Retirement, entry.Retirement) || !entry.ReturnRequested)
            throw new InvalidOperationException("Consumer-end proof belongs to another LOCAL run or retirement.");
        if (entry.Phase == LocalRunSessionPhase.Closed) return true;
        if (entry.CleanupAttempted) return false;
        var published = PublishCandidate(entry, 0, 0, remove: true);
        var failures = new List<Exception>(entry.Ownership.Length); _busy = true; entry.CleanupAttempted = true;
        try
        {
            // Do this before even the first Dispose callback. Process-owned managed records
            // cannot pin an identity whose native GC root has already been released.
            lock (ProcessGate)
            {
                entry.ConsumerEndEvidence = token.Evidence;
                entry.ProtectionRetired = true;
                Volatile.Write(ref _published, published);
            }
            for (var index = entry.Ownership.Length - 1; index >= 0; index--)
            {
                var owned = entry.Ownership[index]; if (owned is null) continue;
                try { owned.Dispose(); lock (ProcessGate) entry.Ownership[index] = null; }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count != 0)
            {
                entry.Phase = LocalRunSessionPhase.CleanupUnknown;
                entry.Failure = "LOCAL ownership cleanup is unconfirmed; failed frees will not be retried.";
                return false;
            }
            entry.Phase = LocalRunSessionPhase.Closed;
            _entries.Remove(entry); lock (ProcessGate) ProcessOwners.Remove(entry);
            return true;
        }
        finally { _busy = false; }
    }

    internal LocalRunSessionSnapshot Read(SessionHandle session)
    {
        _main.VerifyAccess(); var entry = Get(session);
        return new(entry.Identity, entry.Phase, entry.Metadata, entry.Controller, entry.ResultScene,
            entry.ResultSceneGeneration, entry.ProviderWithdrawn, entry.ReturnRequested, entry.ResultClaimed,
            entry.CleanupAttempted, entry.ProtectionRetired, entry.Ownership.Count(x => x is not null),
            entry.ConsumerEndEvidence, entry.Failure, entry.Result);
    }

    private Entry CurrentCapture(ResultCaptureClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim); var entry = Get(claim.Session);
        if (!ReferenceEquals(entry.Capture, claim) || entry.Phase != LocalRunSessionPhase.ResultCapturing || entry.CaptureFailed)
            throw new InvalidOperationException("Actual result completion requires this run's one in-flight claim.");
        return entry;
    }
    private static void FailCapture(Entry entry, string failure)
    {
        entry.CaptureFailed = true; entry.Phase = LocalRunSessionPhase.CleanupUnknown; entry.Failure = failure;
        EndResultObservation(entry, ErrorCode.ObservationUnavailable, failure);
    }
    internal void FailResultObservation(SessionHandle session, string reason)
    {
        _main.VerifyAccess();
        EndResultObservation(Get(session), ErrorCode.ObservationUnavailable, reason);
    }
    internal void FailPendingResultObservations(string reason)
    {
        _main.VerifyAccess();
        foreach (var entry in _entries) EndResultObservation(entry, ErrorCode.ObservationUnavailable, reason);
    }
    private static void EndResultObservation(Entry entry, ErrorCode code, string reason)
    {
        entry.ResultObservation?.End(code, reason);
        entry.ResultObservation = null;
    }
    private void Guard()
    {
        _main.VerifyAccess();
        if (_busy) throw new InvalidOperationException("Reentrant LOCAL session mutation is rejected without removing protection.");
    }
    private Entry Get(SessionHandle session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!ReferenceEquals(session.Owner, this) || session.EntryIdentity is not Entry entry || !ReferenceEquals(entry.Owner, this)
            || !ReferenceEquals(entry.Handle, session) || entry.Phase != LocalRunSessionPhase.Closed && !_entries.Contains(entry))
            throw new InvalidOperationException("LOCAL session handle does not belong to this registry.");
        return entry;
    }
    private static void RequirePhase(Entry entry, LocalRunSessionPhase expected)
    { if (entry.Phase != expected) throw new InvalidOperationException("LOCAL session phase is not " + expected + "."); }
    /// <summary>Caller holds ProcessGate across this check and adoption, including cross-registry writers.</summary>
    private static void RequireDistinctLocked(Entry entry, long identity, IDisposable ownership)
    {
        // During RegisterPrepared, entry has not yet joined ProcessOwners. Its declared
        // metadata identity is the very identity being registered, rather than a collision.
        var ownAlias = ProcessOwners.Contains(entry) && (identity == entry.Metadata || identity == entry.Controller
            || identity == entry.ResultScene || identity == entry.Result?.NativeResultIdentity);
        if (ownAlias || ProcessOwners.Any(x => !ReferenceEquals(x, entry) && !x.ProtectionRetired
                && (x.Metadata == identity || x.Controller == identity || x.ResultScene == identity || x.Result?.NativeResultIdentity == identity))
            || ProcessOwners.Any(x => x.Ownership.Any(held => ReferenceEquals(held, ownership))))
            throw new InvalidOperationException("LOCAL native identity or ownership record is already retained.");
    }
    private PublishedIdentity[] PublishCandidate(Entry changing, long controller, long resultScene, bool remove = false)
        => _entries.Where(x => !ReferenceEquals(x, changing) && !x.ProtectionRetired && (x.Controller != 0 || x.ResultScene != 0))
            .Select(x => new PublishedIdentity(x.Handle, x.Controller, x.ResultScene))
            .Concat(remove || controller == 0 && resultScene == 0 ? Array.Empty<PublishedIdentity>() : new[] { new PublishedIdentity(changing.Handle, controller, resultScene) }).ToArray();
    private static bool Identifier(string? value) => value is { Length: > 0 and <= 128 }
        && value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-');
    private static string BoundedEvidence(string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence) || evidence.Length > 1024 || evidence.Any(char.IsControl))
            throw new ArgumentException("A nonempty bounded verification/failure explanation is required.");
        return evidence;
    }
}
