using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RizToolbox.Sdk;

namespace RizToolbox.Core.Diagnostics;

/// <summary>
/// Copies bounded values into a bounded queue. One consumer owns serialization and storage; no Unity work is performed.
/// Written confirms a completed stream write and flush, not durable storage after a power loss.
/// </summary>
public sealed class StructuredDiagnosticLogger : ILogger, IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly object _gate = new();
    private readonly DiagnosticOptions _options;
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private readonly Dictionary<RepeatKey, RepeatEntry> _repeats = new();
    private readonly long[] _levels = new long[4];
    private readonly PendingEvent?[] _queue;
    private readonly FlushAttempt?[] _flushAttempts;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Task _consumer;
    private readonly Action<string, long>? _beforeStorage;
    private readonly string? _initialPathError;
    private int _head, _tail, _queued, _pendingEvents, _peakPendingEvents;
    private long _pendingBytes, _peakPendingBytes, _enqueued, _processed, _queueDropped, _queueAbandoned;
    private long _lastProcessedSequence, _lastSuccessfulFlushSequence, _flushTimeouts;
    private int _flushHead, _flushTail, _pendingFlushAttempts, _peakPendingFlushAttempts;
    private long? _asyncFlushCutoff;
    private FlushAttempt? _activeFlush, _lastCompletedFlush, _disposeFlush;
    private DiagnosticFlushResult? _closedFlushResult;
    private bool _acceptingWrites = true, _disposeRequested, _consumerCompleted;

    // These storage objects and retry state belong exclusively to the consumer.
    private readonly Queue<FileEntry> _files = new();
    private readonly string? _directory;
    private FileStream? _stream;
    private FileStream? _sessionLock;
    private int _fileIndex;
    private long _currentBytes, _storedBytes;
    private int _retainedFiles;
    private long _retainedBytes;
    private long _attempted, _written, _textWritten, _filtered, _repeatSuppressed, _rateSuppressed;
    private long _diskFailures, _diskSkipped, _internalFailures, _oversized, _sequence;
    private long _rateStart = Stopwatch.GetTimestamp(), _nextStructuredRetry, _nextTextRetry;
    private int _rateCount;
    private string? _lastError;
    private bool _metadataWritten;

    public StructuredDiagnosticLogger(DiagnosticOptions options) : this(options, null) { }

    // Narrow test seam at actual consumer storage boundaries, outside the producer gate.
    internal StructuredDiagnosticLogger(DiagnosticOptions options, Action<string, long>? beforeStorage)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        Validate(options);
        _queue = new PendingEvent?[options.MaxQueuedEvents];
        // Distinct cutoffs are bounded by pending events, one active processed cutoff,
        // and the single final disposal attempt. Calls at the same cutoff share an attempt.
        _flushAttempts = new FlushAttempt?[options.MaxQueuedEvents + 2];
        _beforeStorage = beforeStorage;
        SessionId = options.SessionId ?? $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        if (!Regex.IsMatch(SessionId, "^[A-Za-z0-9_-]{1,80}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("SessionId must contain 1..80 ASCII letters, digits, '-' or '_'.", nameof(options));
        try
        {
            _directory = Path.Combine(Path.GetFullPath(options.RootDirectory), SessionId);
        }
        catch (Exception ex)
        {
            _initialPathError = SafeError(ex);
        }
        _consumer = Task.Run(ConsumeAsync);
    }

    public string SessionId { get; }
    /// <summary>Completes after the consumer has released its storage handles, including after a disposal timeout.</summary>
    public Task Completion => _consumer;

    public void Log(LogLevel level, string message, Exception? exception = null) =>
        Write(level, "log", message, exception: exception);

    public ILogger ForContext(DiagnosticContext context) => new ContextLogger(this,
        context ?? throw new ArgumentNullException(nameof(context)));

    public void Write(LogLevel level, string eventCode, string message, DiagnosticContext? context = null,
        Exception? exception = null, IReadOnlyDictionary<string, string?>? fields = null)
    {
        lock (_gate)
        {
            if (!_acceptingWrites) return;
            _attempted++;
            if ((int)level < 0 || (int)level >= _levels.Length)
            { _internalFailures++; _lastError = "Invalid diagnostic level."; return; }
            _levels[(int)level]++;
            if (level < _options.MinimumLevel) { _filtered++; return; }
        }
        // Caller-controlled formatting and enumeration must not hold the shared memory gate.
        // Limits bound retained output; arbitrary custom formatters cannot be preempted here.
        try
        {
            var code = Bound(eventCode, 64) ?? "log";
            var text = Bound(message, _options.MaxMessageCharacters) ?? string.Empty;
            var safeContext = Sanitize(context);
            var exceptionText = ExceptionText(exception);
            var safeFields = Sanitize(fields);
            var charge = Charge(code, text, safeContext, exceptionText, safeFields);
            var key = new RepeatKey(level, code, text, safeContext);
            lock (_gate)
            {
                if (!_acceptingWrites) return;
                var now = Stopwatch.GetTimestamp();
                if (IsRepeated(key, now)) { _repeatSuppressed++; return; }
                if (Elapsed(now, _rateStart) >= _options.RateWindow)
                { _rateStart = now; _rateCount = 0; }
                if (_rateCount >= _options.MaxEventsPerWindow) { _rateSuppressed++; return; }
                _rateCount++;
                if (_pendingEvents >= _options.MaxQueuedEvents || charge > _options.MaxQueuedBytes - _pendingBytes)
                { _queueDropped++; return; }
                var nextSequence = checked(_sequence + 1);
                var entry = new DiagnosticEvent(1, SessionId, nextSequence, DateTimeOffset.UtcNow, level,
                    code, text, safeContext, exceptionText, safeFields);
                _queue[_tail] = new PendingEvent(entry, charge);
                _sequence = nextSequence;
                _tail = (_tail + 1) % _queue.Length;
                _queued++; _pendingEvents++; _pendingBytes += charge; _enqueued++;
                _peakPendingEvents = Math.Max(_peakPendingEvents, _pendingEvents);
                _peakPendingBytes = Math.Max(_peakPendingBytes, _pendingBytes);
                Signal();
            }
        }
        catch (Exception ex) { RecordInternalFailure(ex); }
    }

    public DiagnosticSnapshot GetSnapshot()
    {
        lock (_gate) return Snapshot();
    }

    /// <summary>Requests a flush through the current accepted sequence without waiting on storage.</summary>
    public void RequestFlush()
    {
        lock (_gate)
        {
            if (!_acceptingWrites || _consumerCompleted) return;
            _asyncFlushCutoff = _sequence;
            Signal();
        }
    }

    /// <summary>Waits for an attempt with a fixed captured sequence, releasing the memory gate while waiting.</summary>
    public DiagnosticFlushResult Flush(TimeSpan timeout)
    {
        ValidateTimeout(timeout, nameof(timeout), allowZero: true);
        var started = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            if (_consumerCompleted)
                return _closedFlushResult!;
            if (!_acceptingWrites && !_disposeRequested) return new(false, false, _sequence);
            var request = _disposeRequested ? _disposeFlush! : RequestExplicitFlushUnderLock();
            while (request.Result is null && !_consumerCompleted)
            {
                var remaining = timeout - Elapsed(Stopwatch.GetTimestamp(), started);
                if (remaining <= TimeSpan.Zero)
                {
                    _flushTimeouts++;
                    return new(false, false, request.ThroughSequence) { TimedOut = true };
                }
                Monitor.Wait(_gate, remaining);
            }
            return request.Result ?? new(false, false, request.ThroughSequence);
        }
    }

    /// <summary>Uses the configured finite flush deadline. Inspect counters or the timed overload for the outcome.</summary>
    public void Flush() => Flush(_options.FlushTimeout);

    public void Dispose()
    {
        var started = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            if (_disposeRequested) return;
            _disposeRequested = true;
            _acceptingWrites = false;
            if (_consumerCompleted) return;
            _disposeFlush = RequestExplicitFlushUnderLock(forceNew: true);
            while (!_consumerCompleted)
            {
                var remaining = _options.DisposeTimeout - Elapsed(Stopwatch.GetTimestamp(), started);
                if (remaining <= TimeSpan.Zero) { _flushTimeouts++; return; }
                Monitor.Wait(_gate, remaining);
            }
        }
    }

    private FlushAttempt RequestExplicitFlushUnderLock(bool forceNew = false)
    {
        var through = _sequence;
        if (!forceNew && _pendingFlushAttempts > 0)
        {
            var last = _flushAttempts[(_flushTail + _flushAttempts.Length - 1) % _flushAttempts.Length]!;
            if (last.ThroughSequence == through) return last;
        }
        var request = new FlushAttempt(through);
        AddFlushAttemptUnderLock(request, prepend: false);
        Signal();
        return request;
    }

    private void AddFlushAttemptUnderLock(FlushAttempt attempt, bool prepend)
    {
        if (_pendingFlushAttempts == _flushAttempts.Length)
            throw new InvalidOperationException("Diagnostic flush control capacity invariant was violated.");
        if (prepend)
        {
            _flushHead = (_flushHead + _flushAttempts.Length - 1) % _flushAttempts.Length;
            _flushAttempts[_flushHead] = attempt;
        }
        else
        {
            _flushAttempts[_flushTail] = attempt;
            _flushTail = (_flushTail + 1) % _flushAttempts.Length;
        }
        _pendingFlushAttempts++;
        _peakPendingFlushAttempts = Math.Max(_peakPendingFlushAttempts, _pendingFlushAttempts);
    }

    private void SelectFlushUnderLock()
    {
        if (_activeFlush is not null) return;
        if (_asyncFlushCutoff is { } through && (_pendingFlushAttempts == 0 ||
            through < _flushAttempts[_flushHead]!.ThroughSequence))
            AddFlushAttemptUnderLock(new FlushAttempt(through), prepend: _pendingFlushAttempts > 0);
        if (_pendingFlushAttempts > 0) _activeFlush = _flushAttempts[_flushHead];
    }

    // Called only under the gate. One wake token and a fixed attempt ring cannot grow with request traffic.
    private void Signal() { if (_wake.CurrentCount == 0) _wake.Release(); }

    private async Task ConsumeAsync()
    {
        try
        {
            InitializeStorage();
            while (true)
            {
                PendingEvent? item = null;
                FlushAttempt? flush = null;
                lock (_gate)
                {
                    SelectFlushUnderLock();
                    if (_activeFlush is not null && _lastProcessedSequence >= _activeFlush.ThroughSequence)
                        flush = _activeFlush;
                    else if (_queued > 0)
                    {
                        item = _queue[_head]!;
                        _queue[_head] = null;
                        _head = (_head + 1) % _queue.Length;
                        _queued--;
                    }
                    else if (_disposeRequested) break;
                }
                if (flush is not null)
                {
                    var snapshotWritten = PersistSnapshot(flush.ThroughSequence);
                    lock (_gate)
                    {
                        flush.Result = new(true, snapshotWritten, flush.ThroughSequence);
                        _lastCompletedFlush = flush;
                        if (snapshotWritten)
                            _lastSuccessfulFlushSequence = Math.Max(_lastSuccessfulFlushSequence, flush.ThroughSequence);
                        _flushAttempts[_flushHead] = null;
                        _flushHead = (_flushHead + 1) % _flushAttempts.Length;
                        _pendingFlushAttempts--;
                        _activeFlush = null;
                        if (_asyncFlushCutoff is { } asyncCutoff && asyncCutoff <= flush.ThroughSequence)
                            _asyncFlushCutoff = null;
                        Monitor.PulseAll(_gate);
                    }
                    continue;
                }
                if (item is not null)
                {
                    ProcessEvent(item.Entry);
                    lock (_gate)
                    {
                        _processed++; _lastProcessedSequence = item.Entry.Sequence;
                        _pendingEvents--; _pendingBytes -= item.Charge;
                    }
                    continue;
                }
                await _wake.WaitAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            lock (_gate) _acceptingWrites = false;
            RecordInternalFailure(ex);
        }
        finally
        {
            // A caller timing out never takes these handles away from a busy consumer.
            try { _stream?.Dispose(); } catch (Exception ex) { RecordDiskFailure(ex); }
            try { _sessionLock?.Dispose(); } catch (Exception ex) { RecordDiskFailure(ex); }
            _stream = null; _sessionLock = null;
            lock (_gate)
            {
                var latest = _lastCompletedFlush;
                _closedFlushResult = _pendingFlushAttempts == 0 && _asyncFlushCutoff is null &&
                    latest is not null && latest.ThroughSequence == _sequence && latest.Result is not null
                    ? latest.Result : new(false, false, _sequence);
                Array.Clear(_flushAttempts, 0, _flushAttempts.Length);
                _pendingFlushAttempts = 0;
                _activeFlush = null; _asyncFlushCutoff = null;
                _queueAbandoned += _pendingEvents;
                _pendingEvents = 0; _pendingBytes = 0; _queued = 0;
                Array.Clear(_queue, 0, _queue.Length);
                _acceptingWrites = false;
                _consumerCompleted = true;
                Monitor.PulseAll(_gate);
            }
            _wake.Dispose();
        }
    }

    private void InitializeStorage()
    {
        if (_directory is null)
        {
            RecordDiskFailure(_initialPathError ?? "Diagnostic path is unavailable.");
            _nextStructuredRetry = long.MaxValue;
            return;
        }
        try { PrepareSession(); }
        catch (Exception ex) { StructuredFailure(ex); }
    }

    private void ProcessEvent(DiagnosticEvent entry)
    {
        try
        {
            var bytes = Encode(entry);
            if (bytes.LongLength > _options.MaxFileBytes)
            {
                lock (_gate) _oversized++;
                entry = entry with { Message = "Diagnostic event exceeded the configured file size.",
                    Context = null, Exception = null, Fields = null };
                bytes = Encode(entry);
            }
            WriteStructured(bytes, entry.Sequence);
            if (_options.TextLogPath is not null) WriteText(entry);
        }
        catch (Exception ex) { RecordInternalFailure(ex); }
    }

    private bool PersistSnapshot(long throughSequence)
    {
        if (_directory is null || Stopwatch.GetTimestamp() < _nextStructuredRetry) return false;
        try
        {
            _beforeStorage?.Invoke("snapshot-write", throughSequence);
            _stream?.Flush();
            if (!_metadataWritten) return false;
            var destination = Path.Combine(_directory, "snapshot.json");
            EnsureNoLinks(_directory);
            EnsureNoLinksIfExists(destination);
            var pending = destination + ".pending";
            EnsureNoLinksIfExists(pending);
            // The completed file describes this successful flush; memory advances only after the atomic move.
            var snapshot = GetSnapshot();
            snapshot = snapshot with { LastSuccessfulFlushSequence = Math.Max(snapshot.LastSuccessfulFlushSequence, throughSequence) };
            File.WriteAllBytes(pending, JsonSerializer.SerializeToUtf8Bytes(snapshot, Json));
            File.Move(pending, destination, true);
            return true;
        }
        catch (Exception ex)
        { RecordDiskFailure(ex); _nextStructuredRetry = RetryAt(); return false; }
    }

    private void WriteStructured(byte[] bytes, long sequence)
    {
        if (_directory is null || Stopwatch.GetTimestamp() < _nextStructuredRetry)
        { lock (_gate) _diskSkipped++; return; }
        try
        {
            EnsureNoLinks(_directory);
            if (!_metadataWritten)
                PrepareSession();
            if (_stream is null || _currentBytes + bytes.LongLength > _options.MaxFileBytes)
                Rotate();
            _beforeStorage?.Invoke("structured-write", sequence);
            _stream!.Write(bytes, 0, bytes.Length);
            _stream.Flush();
            _currentBytes += bytes.LongLength; _storedBytes += bytes.LongLength;
            lock (_gate)
            { _written++; _retainedFiles = _files.Count; _retainedBytes = _storedBytes; }
            _nextStructuredRetry = 0;
        }
        catch (Exception ex)
        {
            StructuredFailure(ex);
            try { _stream?.Dispose(); } catch { }
            _stream = null;
            lock (_gate) { _retainedFiles = _files.Count; _retainedBytes = _storedBytes; }
        }
    }

    private void StructuredFailure(Exception ex)
    {
        RecordDiskFailure(ex);
        // A collision or incomplete ownership marker can never retry into an existing session.
        _nextStructuredRetry = _directory is not null && !_metadataWritten &&
            (Directory.Exists(_directory) || File.Exists(_directory)) ? long.MaxValue : RetryAt();
        if (!_metadataWritten)
        {
            try { _sessionLock?.Dispose(); } catch { }
            _sessionLock = null;
        }
    }

    private void Rotate()
    {
        _stream?.Dispose(); _stream = null;
        if (_files.Count > 0) _files.Last().Bytes = _currentBytes;
        while (_files.Count >= _options.MaxFiles)
        {
            var oldest = _files.Peek();
            EnsureNoLinksIfExists(oldest.Path);
            File.Delete(oldest.Path);
            _files.Dequeue(); _storedBytes -= oldest.Bytes;
        }
        var path = Path.Combine(_directory!, $"events-{++_fileIndex:D6}.jsonl");
        _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete);
        _currentBytes = 0;
        _files.Enqueue(new FileEntry(path));
    }

    private void WriteText(DiagnosticEvent entry)
    {
        if (Stopwatch.GetTimestamp() < _nextTextRetry) { lock (_gate) _diskSkipped++; return; }
        try
        {
            var path = Path.GetFullPath(_options.TextLogPath!);
            EnsureNoLinks(Path.GetDirectoryName(path)!);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            EnsureNoLinksIfExists(path);
            var line = $"{entry.Timestamp:O} [{entry.Level}] {entry.Message} {entry.Exception}";
            line = line.Replace("\r", "\\r").Replace("\n", "\\n") + Environment.NewLine;
            var bytes = Encoding.UTF8.GetBytes(line);
            if (bytes.LongLength > _options.MaxFileBytes)
                bytes = Encoding.UTF8.GetBytes($"{entry.Timestamp:O} [{entry.Level}] Diagnostic event exceeded the configured file size.{Environment.NewLine}");
            if (File.Exists(path) && new FileInfo(path).Length > _options.MaxFileBytes)
                File.Delete(path); // An oversized legacy file cannot be retained inside a bounded rotation.
            if (File.Exists(path) && new FileInfo(path).Length + bytes.LongLength > _options.MaxFileBytes)
            {
                for (var index = _options.MaxFiles - 1; index >= 1; index--)
                {
                    var destination = path + "." + index;
                    EnsureNoLinksIfExists(destination);
                    if (index == _options.MaxFiles - 1) File.Delete(destination);
                    var source = index == 1 ? path : path + "." + (index - 1);
                    EnsureNoLinksIfExists(source);
                    if (File.Exists(source)) File.Move(source, destination, true);
                }
                if (_options.MaxFiles == 1) File.Delete(path);
            }
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                stream.Write(bytes, 0, bytes.Length);
            lock (_gate) _textWritten++;
            _nextTextRetry = 0;
        }
        catch (Exception ex) { RecordDiskFailure(ex); _nextTextRetry = RetryAt(); }
    }

    private void WriteMetadata()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, sessionId = SessionId, startedAt = _started,
            limits = new { _options.MaxFileBytes, _options.MaxFiles, _options.MaxSessions, _options.MaxEventsPerWindow,
                _options.MaxQueuedEvents, _options.MaxQueuedBytes,
                rateWindowMilliseconds = _options.RateWindow.TotalMilliseconds,
                _options.RepeatBurst, repeatWindowMilliseconds = _options.RepeatWindow.TotalMilliseconds }
        }, Json);
        using (var stream = new FileStream(Path.Combine(_directory!, "session.json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            stream.Write(bytes, 0, bytes.Length);
        _metadataWritten = true;
    }

    private void PrepareRoot(string root)
    {
        Directory.CreateDirectory(root);
        var owned = new List<(string Path, DateTimeOffset Started)>();
        var inspected = 0;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (++inspected > 512) throw new IOException("Diagnostic root exceeds the bounded session scan.");
            if (!Regex.IsMatch(Path.GetFileName(directory), "^[A-Za-z0-9_-]{1,80}$", RegexOptions.CultureInvariant)) continue;
            var metadata = Path.Combine(directory, "session.json");
            if (!File.Exists(metadata)) continue;
            EnsureNoLinks(metadata);
            if (new FileInfo(metadata).Length > 4096) continue;
            using var json = JsonDocument.Parse(File.ReadAllBytes(metadata));
            if (!json.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1 ||
                !json.RootElement.TryGetProperty("sessionId", out var id) || id.GetString() != Path.GetFileName(directory) ||
                !json.RootElement.TryGetProperty("startedAt", out var started) || !started.TryGetDateTimeOffset(out var time)) continue;
            owned.Add((directory, time));
        }
        var remaining = Math.Max(0, owned.Count - _options.MaxSessions + 1);
        foreach (var old in owned.OrderBy(entry => entry.Started).ThenBy(entry => entry.Path, StringComparer.Ordinal))
        {
            if (remaining == 0) break;
            var lockPath = Path.Combine(old.Path, "session.lock");
            EnsureNoLinksIfExists(lockPath);
            FileStream? sessionLock = null;
            try { sessionLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { continue; } // A running session cannot be pruned by another logger.
            using (sessionLock)
            {
            // No recursive deletion: every file and its ownership marker are checked before removing anything.
            var files = Directory.EnumerateFileSystemEntries(old.Path).Take(69).ToArray();
            if (files.Length > 68 || files.Any(path => Directory.Exists(path) ||
                !(Path.GetFileName(path) is "session.json" or "snapshot.json" or "snapshot.json.pending" or "session.lock" ||
                  Regex.IsMatch(Path.GetFileName(path), "^events-[0-9]{6,12}\\.jsonl$", RegexOptions.CultureInvariant))))
                throw new IOException("Old diagnostic session contains unowned entries; automatic retention stopped.");
            foreach (var path in files) EnsureNoLinks(path);
            foreach (var path in files.Where(path => path != lockPath)) File.Delete(path);
            }
            File.Delete(lockPath);
            Directory.Delete(old.Path, false);
            remaining--;
        }
        if (remaining > 0) throw new IOException("Diagnostic session budget is held by active sessions.");
    }

    private void PrepareSession()
    {
        // A session is an ownership boundary. Never append to a pre-existing session, even if it looks valid.
        if (Directory.Exists(_directory) || File.Exists(_directory))
            throw new IOException("The diagnostic session already exists; choose a new SessionId.");
        var root = Path.GetDirectoryName(_directory!)!;
        EnsureNoLinks(root);
        Directory.CreateDirectory(root);
        var retentionLock = Path.Combine(root, "retention.lock");
        EnsureNoLinksIfExists(retentionLock);
        // The same gate also applies across processes; a busy gate becomes a throttled storage diagnostic.
        using var gate = new FileStream(retentionLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        PrepareRoot(root);
        Directory.CreateDirectory(_directory!);
        _sessionLock = new FileStream(Path.Combine(_directory!, "session.lock"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.Read);
        WriteMetadata();
    }

    private DiagnosticSnapshot Snapshot() => new(SessionId, _started, DateTimeOffset.UtcNow,
        _directory, _options.MinimumLevel, _attempted, _written, _textWritten, _filtered,
        _repeatSuppressed, _rateSuppressed, _diskFailures, _diskSkipped, _internalFailures,
        _oversized, _retainedFiles, _retainedBytes, _repeats.Count, _lastError,
        new ReadOnlyDictionary<string, long>(Enumerable.Range(0, _levels.Length)
            .ToDictionary(index => ((LogLevel)index).ToString(), index => _levels[index])))
    {
        Enqueued = _enqueued, Processed = _processed, QueueDropped = _queueDropped,
        QueueAbandoned = _queueAbandoned, PendingEvents = _pendingEvents, PendingBytes = _pendingBytes,
        PeakPendingEvents = _peakPendingEvents, PeakPendingBytes = _peakPendingBytes,
        LastEnqueuedSequence = _sequence, LastProcessedSequence = _lastProcessedSequence,
        LastSuccessfulFlushSequence = _lastSuccessfulFlushSequence, FlushTimeouts = _flushTimeouts,
        PendingFlushAttempts = _pendingFlushAttempts, PeakPendingFlushAttempts = _peakPendingFlushAttempts,
        AcceptingWrites = _acceptingWrites, ConsumerCompleted = _consumerCompleted
    };

    private bool IsRepeated(RepeatKey key, long now)
    {
        if (!_repeats.TryGetValue(key, out var entry))
        {
            if (_repeats.Count >= _options.MaxRepeatKeys)
            {
                var oldest = _repeats.MinBy(pair => pair.Value.LastSeen).Key;
                _repeats.Remove(oldest);
            }
            _repeats[key] = new RepeatEntry(now);
            return false;
        }
        entry.LastSeen = now;
        if (Elapsed(now, entry.WindowStart) >= _options.RepeatWindow)
        { entry.WindowStart = now; entry.Count = 0; }
        entry.Count++;
        return entry.Count > _options.RepeatBurst;
    }

    private static TimeSpan Elapsed(long now, long start) => TimeSpan.FromSeconds((now - start) / (double)Stopwatch.Frequency);
    private long RetryAt()
    {
        var delay = _options.DiskRetryInterval.TotalSeconds * Stopwatch.Frequency;
        return Stopwatch.GetTimestamp() + (long)Math.Min(delay, long.MaxValue / 2d);
    }
    private void RecordDiskFailure(Exception ex) => RecordDiskFailure(SafeError(ex));
    private void RecordDiskFailure(string error)
    { lock (_gate) { _diskFailures++; _lastError = error; } }
    private void RecordInternalFailure(Exception ex)
    {
        var error = SafeError(ex);
        lock (_gate) { _internalFailures++; _lastError = error; }
    }
    private static string SafeError(Exception ex)
    { try { return Bound(ex.GetType().Name + ": " + ex.Message, 512)!; } catch { return "Diagnostic storage failure."; } }
    private string? ExceptionText(Exception? exception)
    {
        if (exception is null) return null;
        try { return Bound(exception.ToString(), _options.MaxExceptionCharacters); }
        catch (Exception ex)
        { RecordInternalFailure(ex); return Bound("Exception text unavailable.", _options.MaxExceptionCharacters); }
    }
    private static string? Bound(string? value, int limit) => value is null ? null : value.Length <= limit ? value : value[..limit];
    private static DiagnosticContext? Sanitize(DiagnosticContext? context) => context is null ? null : new(
        Bound(context.PluginId, 256), Bound(context.CapabilityId, 256), Bound(context.TargetId, 256),
        Bound(context.RunId, 256), context.SceneGeneration, Bound(context.CorrelationId, 256));
    private IReadOnlyDictionary<string, string?>? Sanitize(IReadOnlyDictionary<string, string?>? fields)
    {
        if (fields is null) return null;
        var copy = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (_options.MaxFields > 0)
        {
            using var iterator = fields.GetEnumerator();
            for (var inspected = 0; inspected < _options.MaxFields && iterator.MoveNext(); inspected++)
            {
                var pair = iterator.Current;
                copy[Bound(pair.Key, 64)!] = Bound(pair.Value, _options.MaxFieldValueCharacters);
            }
        }
        return new ReadOnlyDictionary<string, string?>(copy);
    }
    private long Charge(string code, string message, DiagnosticContext? context, string? exception,
        IReadOnlyDictionary<string, string?>? fields)
    {
        // UTF-16 strings plus conservative fixed object/collection overhead. This is a queue charge,
        // independent of JSON size and the consumer's bounded serialization buffer.
        var bytes = 256L + StringCharge(SessionId) + StringCharge(code) + StringCharge(message) + StringCharge(exception);
        if (context is not null)
            bytes += 96 + StringCharge(context.PluginId) + StringCharge(context.CapabilityId) +
                StringCharge(context.TargetId) + StringCharge(context.RunId) + StringCharge(context.CorrelationId);
        if (fields is not null)
        {
            bytes += 128;
            foreach (var pair in fields) bytes += 96 + StringCharge(pair.Key) + StringCharge(pair.Value);
        }
        return bytes;
    }
    private static long StringCharge(string? value) => value is null ? 0 : 32L + value.Length * 2L;
    private static byte[] Encode(DiagnosticEvent entry) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, Json) + "\n");
    private static void EnsureNoLinksIfExists(string path) { if (File.Exists(path) || Directory.Exists(path)) EnsureNoLinks(path); }
    private static void EnsureNoLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Diagnostic paths cannot traverse links or reparse points.");
            current = Path.GetDirectoryName(current);
        }
    }
    private static void Validate(DiagnosticOptions options)
    {
        if (options.MaxFileBytes is < 1024 or > 67_108_864 || options.MaxFiles is < 1 or > 64 || options.MaxSessions is < 1 or > 64 ||
            options.MaxMessageCharacters is < 16 or > 65_536 || options.MaxExceptionCharacters is < 16 or > 65_536 ||
            options.MaxFields is < 0 or > 64 || options.MaxFieldValueCharacters is < 16 or > 4096 ||
            options.MaxRepeatKeys is < 1 or > 4096 || options.RepeatBurst is < 1 or > 10_000 ||
            options.MaxEventsPerWindow is < 1 or > 100_000 || options.RepeatWindow <= TimeSpan.Zero ||
            options.RateWindow <= TimeSpan.Zero || options.DiskRetryInterval < TimeSpan.Zero ||
            options.DiskRetryInterval > TimeSpan.FromDays(1) || options.MaxQueuedEvents is < 1 or > 65_536 ||
            options.MaxQueuedBytes is < 1024 or > 268_435_456 ||
            options.FlushTimeout <= TimeSpan.Zero || options.FlushTimeout > TimeSpan.FromMinutes(1) ||
            options.DisposeTimeout <= TimeSpan.Zero || options.DisposeTimeout > TimeSpan.FromMinutes(1) ||
            !Enum.IsDefined(typeof(LogLevel), options.MinimumLevel))
            throw new ArgumentOutOfRangeException(nameof(options), "Diagnostic limits are invalid.");
    }
    private static void ValidateTimeout(TimeSpan timeout, string name, bool allowZero)
    {
        if (timeout < TimeSpan.Zero || (!allowZero && timeout == TimeSpan.Zero) || timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(name, "Diagnostic deadlines must be finite and at most one minute.");
    }
    private sealed record DiagnosticEvent(int SchemaVersion, string SessionId, long Sequence,
        DateTimeOffset Timestamp, LogLevel Level, string EventCode, string Message,
        DiagnosticContext? Context, string? Exception, IReadOnlyDictionary<string, string?>? Fields);
    private sealed record PendingEvent(DiagnosticEvent Entry, long Charge);
    private sealed record RepeatKey(LogLevel Level, string Code, string Message, DiagnosticContext? Context);
    private sealed class FlushAttempt
    {
        public FlushAttempt(long throughSequence) => ThroughSequence = throughSequence;
        public long ThroughSequence { get; }
        // Published once under the gate; waiters retain this result even after later attempts complete.
        public DiagnosticFlushResult? Result { get; set; }
    }
    private sealed class RepeatEntry
    {
        public RepeatEntry(long now) { WindowStart = LastSeen = now; Count = 1; }
        public long WindowStart, LastSeen;
        public int Count;
    }
    private sealed class FileEntry
    { public FileEntry(string path) => Path = path; public string Path { get; } public long Bytes; }
    private sealed class ContextLogger : ILogger
    {
        private readonly StructuredDiagnosticLogger _owner;
        private readonly DiagnosticContext _context;
        public ContextLogger(StructuredDiagnosticLogger owner, DiagnosticContext context) { _owner = owner; _context = context; }
        public void Log(LogLevel level, string message, Exception? exception = null) =>
            _owner.Write(level, "log", message, _context, exception);
    }
}
