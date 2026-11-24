namespace RizToolbox.Runtime;

/// <summary>Bounded temporary responsibility used only by the explicit publication seams.
/// Unknown frees retain their records and process slot; a later no-op is never a retry proof.</summary>
internal sealed class NativePublicationCall : IDisposable
{
    internal const int MaximumCalls = 32, MaximumArguments = 128, MaximumRoots = 129;
    private static readonly object Gate = new();
    private static readonly HashSet<NativePublicationCall> ProcessOwners = new();
    private readonly Action _verify;
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[MaximumRoots];
    private readonly NativeRuntime.Arg?[] _arguments = new NativeRuntime.Arg?[MaximumArguments];
    private readonly Exception?[] _failures = new Exception?[MaximumRoots + MaximumArguments];
    private int _rootCount, _argumentCount, _failureCount;
    private bool _cleanupAttempted, _publicationFailed;
    private Exception? _operationFailure;
    // This is never freed here: notification transfers output responsibility before calling
    // the publisher. Retention only isolates a publisher that threw before confirming receipt.
    private NativeRuntime.RootHandle? _unconfirmedPublishedRoot;
    private IntPtr _unconfirmedPublishedToken;
    internal static int RetainedCallCount { get { lock (Gate) return ProcessOwners.Count; } }
    internal static int RetainedRootRecordCount { get { lock (Gate) return ProcessOwners.Sum(x => x._roots.Count(r => r is not null)); } }
    internal static int RetainedArgumentRecordCount { get { lock (Gate) return ProcessOwners.Sum(x => x._arguments.Count(a => a is not null)); } }
    internal static int UnconfirmedPublicationCount { get { lock (Gate) return ProcessOwners.Count(x => x._publicationFailed
        && (x._unconfirmedPublishedRoot is not null || x._unconfirmedPublishedToken != IntPtr.Zero)); } }

    internal NativePublicationCall(Action verify, NativeRuntime.Arg[] args)
    {
        _verify = verify; verify(); ArgumentNullException.ThrowIfNull(args);
        if (args.Length > MaximumArguments) throw new UnsupportedSignatureException("Publication calls accept at most 128 argument records.");
        lock (Gate)
        {
            if (ProcessOwners.Count >= MaximumCalls)
                throw new InvalidOperationException("Publication calls exhaust the 32-call live/unknown responsibility capacity.");
            ProcessOwners.Add(this);
        }
        // Copy into fixed storage only after reservation. A reservation rejection never
        // consumes the caller's arguments and no native allocation has happened yet.
        for (var i = 0; i < args.Length; i++) _arguments[_argumentCount++] = args[i];
    }
    internal NativeRuntime.RootHandle Root(Func<NativeRuntime.RootHandle> allocate)
    {
        _verify();
        if (_cleanupAttempted || _rootCount >= MaximumRoots)
            throw new InvalidOperationException("Publication temporary root capacity is exhausted or cleanup has started.");
        var root = allocate(); _roots[_rootCount++] = root; return root;
    }
    internal void TransferRoot(NativeRuntime.RootHandle root)
    {
        _verify();
        if (_cleanupAttempted) throw new InvalidOperationException("Publication cleanup has already started.");
        for (var i = 0; i < _rootCount; i++)
            if (ReferenceEquals(_roots[i], root)) { _roots[i] = null; return; }
        throw new InvalidOperationException("Publication cannot transfer a root outside its temporary responsibility.");
    }
    internal void BeginPublication(IntPtr token) => _unconfirmedPublishedToken = token;
    internal void BeginPublication(NativeRuntime.RootHandle root) => _unconfirmedPublishedRoot = root;
    internal void PublicationAccepted() { _unconfirmedPublishedRoot = null; _unconfirmedPublishedToken = IntPtr.Zero; }
    internal void PublicationFailed() => _publicationFailed = true;
    internal void OperationFailed(Exception error) => _operationFailure = error;
    public void Dispose()
    {
        _verify();
        if (_cleanupAttempted) throw new InvalidOperationException("Publication cleanup will not be retried.");
        _cleanupAttempted = true;
        for (var i = _rootCount - 1; i >= 0; i--)
        {
            var root = _roots[i]; if (root is null) continue;
            try { root.Dispose(); _roots[i] = null; }
            catch (Exception error) { _failures[_failureCount++] = error; }
        }
        for (var i = _argumentCount - 1; i >= 0; i--)
        {
            var argument = _arguments[i]; if (argument is null) continue;
            try { argument.Dispose(); _arguments[i] = null; }
            catch (Exception error) { _failures[_failureCount++] = error; }
        }
        if (_failureCount != 0)
        {
            var failures = _failures.Take(_failureCount).Select(error => error!).ToArray();
            var primary = _operationFailure;
            var error = new AggregateException("Publication temporary cleanup is unknown; failed records and process quota are retained without retry.",
                primary is null ? failures : new Exception[] { primary }.Concat(failures));
            // The public operation may not have entered a native setter. Its
            // owner still needs the exact retained temporary responsibility.
            error.Data["NativePublicationCleanupOwner"] = this;
            error.Data["NativePublicationCleanupFailure"] = new AggregateException("Unconfirmed publication temporary frees.", failures);
            throw error;
        }
        if (!_publicationFailed) lock (Gate) ProcessOwners.Remove(this);
    }
}
