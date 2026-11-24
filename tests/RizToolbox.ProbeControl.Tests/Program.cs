using System.Text;
using System.Text.Json;
using RizToolbox.Examples;
using RizToolbox.Sdk;

// This executable compiles the production internal mailbox source into an isolated,
// SDK-only assembly. No game, native library, clock provider, account, or UI is loaded.
var checks = new (string Name, Action Run)[]
{
    ("constructor is inert and status describes this activation", ConstructorAndStatus),
    ("mailbox shows action readiness from the current sample without acquiring controls", SampledReadiness),
    ("pause dispatch uses the explicit run and generation", Pause),
    ("resume dispatch uses the explicit run and generation", Resume),
    ("speed dispatch preserves the rate value and unit", Speed),
    ("seek dispatch preserves game seconds and a negative position", Seek),
    ("seek dispatch preserves a fractional chart coordinate and its unit", SeekChart),
    ("SDK refusal and Dispatched receipt are preserved verbatim", SdkResults),
    ("stale lease run is released without execution", StaleRun),
    ("stale lease generation is released without execution", StaleGeneration),
    ("failed acquisition consumes the nonce and reports its SDK error", AcquireFailure),
    ("lease acquisition cannot outlive request expiry", ExpiredDuringAcquire),
    ("scope cancellation during acquisition releases the lease without execution", CancelledDuringAcquire),
    ("old activation request cannot execute in a new activation", OldSession),
    ("completed receipt survives reactivation without a fake replacement", CompletedReceiptSurvivesReactivation),
    ("duplicate nonce never executes again, even with changed request bytes", DuplicateNonce),
    ("same nonce with different GUID letter case cannot execute again", NonceCase),
    ("result write failure retries the original result without executing again", PendingResult),
    ("status write failure blocks request processing", StatusWriteFailure),
    ("cancelled scope and disposed mailbox do not touch controls", Lifetime),
    ("malformed, unknown, duplicate, and invalid fields never execute", InvalidRequests),
    ("missing or malformed session ID produces only an input error", InvalidSessionIds),
    ("invalid request with valid IDs consumes its nonce", InvalidNonceConsumed),
    ("request over the list size limit is never read", ListedSizeLimit),
    ("changed read size over limit is refused", ReadSizeLimit),
    ("list and read length mismatch is refused", ReadLengthMismatch),
    ("32 commands are the activation limit", CommandBudget),
    ("validation failures also consume the 32-command budget", ValidationBudget),
    ("SDK exception becomes an effects-unknown result and never replays", ExecuteException)
};

var failed = 0;
foreach (var (name, run) in checks)
{
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
}
Console.WriteLine($"{checks.Length - failed}/{checks.Length} mailbox checks passed.");
return failed == 0 ? 0 : 1;

static void ConstructorAndStatus()
{
    using var f = new Fixture();
    Check(f.Mailbox.SessionId.Length == 32 && Guid.TryParseExact(f.Mailbox.SessionId, "N", out _), "Session is not a GUID N token.");
    Check(f.Storage.Writes == 0 && f.Storage.Reads == 0 && f.Storage.Lists == 0 && f.Controls.Acquires == 0,
        "Constructor performed I/O or acquired a lease.");
    f.Tick();
    using var status = f.File(ModuleControlMailbox.StatusName);
    var root = status.RootElement;
    Equal(1, root.GetProperty("schemaVersion").GetInt32(), "status schema");
    Equal(f.Mailbox.SessionId, root.GetProperty("sessionId").GetString(), "status session");
    Check(root.GetProperty("active").GetBoolean(), "status is not active");
    Equal(0, root.GetProperty("commandsConsumed").GetInt32(), "initial command count");
    Equal(ModuleControlMailbox.MaximumCommands, root.GetProperty("maxCommands").GetInt32(), "command cap");
    Equal(7L, root.GetProperty("state").GetProperty("controllerGeneration").GetInt64(), "status state projection");
    Equal("run-A", root.GetProperty("state").GetProperty("observedRunId").GetString(), "status run projection");
    Check(f.Controls.StateReads == 0, "Mailbox queried GetState instead of using the supplied observation.");
}

static void Pause() => ValidAction("pause", GameplayControlAction.Pause, null, GameplayControlValueUnit.None);
static void SampledReadiness()
{
    using var f = new Fixture();
    var operations = new[] {
        new GameplayControlAvailability(GameplayControlAction.Pause, true, "adapted")
            { StateReadiness = Result.Fail(new(ErrorCode.InvalidGameState, "Countdown active.")) },
        new GameplayControlAvailability(GameplayControlAction.Skip, true, "adapted") { StateReadiness = Result.Ok() },
        new GameplayControlAvailability(GameplayControlAction.Seek, false, "no source", new(ErrorCode.NotReady, "No source."))
    };
    var state = new GameplayControlSnapshot(7, "run-A", true, true, true, true, false, false, false, false,
        operations, Array.Empty<GameplayModification>());
    f.Mailbox.Tick(new(3, state, operations));
    using var status = f.File(ModuleControlMailbox.StatusName);
    var items = status.RootElement.GetProperty("state").GetProperty("operations");
    Check(items[0].GetProperty("supported").GetBoolean() && !items[0].GetProperty("ready").GetBoolean()
        && items[0].GetProperty("error").GetProperty("message").GetString() == "Countdown active."
        && items[1].GetProperty("ready").GetBoolean() && items[2].GetProperty("ready").ValueKind == JsonValueKind.Null
        && items[2].GetProperty("error").GetProperty("message").GetString() == "No source.",
        "Mailbox mixed a game-state refusal, ready action and unsupported action.");
    Check(f.Controls.StateReads == 0 && f.Controls.Acquires == 0, "Reporting a sample requested controls.");
}
static void Resume() => ValidAction("resume", GameplayControlAction.Resume, null, GameplayControlValueUnit.None);
static void Speed() => ValidAction("set-speed", GameplayControlAction.SetSpeed, 0.75, GameplayControlValueUnit.PlaybackRateMultiplier, "rate");
static void Seek() => ValidAction("seek", GameplayControlAction.Seek, -3.25, GameplayControlValueUnit.GameSeconds, "game-seconds");
static void SeekChart() => ValidAction("seek", GameplayControlAction.Seek, 96.5, GameplayControlValueUnit.ChartCoordinates, "chart-coordinates");

static void ValidAction(string action, GameplayControlAction expected, double? value, GameplayControlValueUnit unit, string? unitText = null)
{
    using var f = new Fixture();
    var nonce = f.PutRequest(action, value, unitText);
    f.Tick();
    Equal(1, f.Controls.Acquires, "acquisition count");
    Check(f.Controls.AllowExperimental && f.Controls.AcquireToken == f.Cancellation.Token, "Acquire must opt in with the scope token.");
    Equal(1, f.Controls.Lease.Requests.Count, "explicit Execute count");
    Equal(0, f.Controls.Lease.ActionOverloadCalls, "convenience Execute overload was used");
    var request = f.Controls.Lease.Requests.Single();
    Equal(expected, request.Action, "action");
    Equal(7L, request.ControllerGeneration, "generation");
    Equal("run-A", request.ObservedRunId, "run");
    Equal(value, request.Value, "value");
    Equal(unit, request.Unit, "unit");
    Check(f.Controls.Lease.ExecuteToken == f.Cancellation.Token, "Execute did not receive the scope token.");
    Equal(1, f.Controls.Lease.Disposes, "lease release count");
    using var result = f.File(ModuleControlMailbox.ResultName);
    var root = result.RootElement;
    Equal(f.Mailbox.SessionId, root.GetProperty("sessionId").GetString(), "result session");
    Equal(nonce, root.GetProperty("nonce").GetString(), "result nonce");
    Equal(action, root.GetProperty("action").GetString(), "result action");
    Equal("execute", root.GetProperty("stage").GetString(), "result stage");
    Check(root.GetProperty("result").GetProperty("success").GetBoolean(), "SDK success was lost");
    var receipt = root.GetProperty("result").GetProperty("value");
    Equal("sdk-dispatched", receipt.GetProperty("evidence").GetString(), "SDK receipt evidence");
    var state = receipt.GetProperty("modification").GetProperty("transactionState");
    Check(state.ValueKind == JsonValueKind.Number && state.GetInt32() == (int)GameplayControlTransactionState.Dispatched
        || state.ValueKind == JsonValueKind.String && state.GetString() == nameof(GameplayControlTransactionState.Dispatched),
        "Dispatched was upgraded or changed.");
    Check(receipt.GetProperty("immediatePostconditionObserved").ValueKind == JsonValueKind.Null,
        "An unobserved postcondition was claimed.");
}

static void SdkResults()
{
    using var f = new Fixture();
    var sdkError = new OperationError(ErrorCode.InvalidGameState, "sdk-denied", "riz.gameplay.controls", "run-A", false);
    f.Controls.Lease.NextResult = Result<GameplayControlReceipt>.Fail(sdkError);
    f.PutRequest("pause");
    f.Tick();
    using var result = f.File(ModuleControlMailbox.ResultName);
    var root = result.RootElement;
    Equal("execute", root.GetProperty("stage").GetString(), "SDK failure stage");
    var actual = root.GetProperty("result");
    Check(!actual.GetProperty("success").GetBoolean(), "SDK failure was converted to success");
    var error = actual.GetProperty("error");
    Equal((int)ErrorCode.InvalidGameState, error.GetProperty("code").GetInt32(), "SDK error code");
    Equal("sdk-denied", error.GetProperty("message").GetString(), "SDK error message");
    Equal("riz.gameplay.controls", error.GetProperty("capabilityId").GetString(), "SDK capability");
    Equal("run-A", error.GetProperty("targetId").GetString(), "SDK target");
}

static void StaleRun()
{
    using var f = new Fixture();
    f.Controls.Lease.RunId = "run-B";
    f.PutRequest("pause");
    f.Tick();
    StaleAssert(f);
}

static void StaleGeneration()
{
    using var f = new Fixture();
    f.Controls.Lease.Generation = 8;
    f.PutRequest("pause");
    f.Tick();
    StaleAssert(f);
}

static void StaleAssert(Fixture f)
{
    Equal(1, f.Controls.Acquires, "stale request did not acquire a lease");
    Equal(0, f.Controls.Lease.Requests.Count, "stale request reached Execute");
    Equal(1, f.Controls.Lease.Disposes, "stale lease was not released");
    using var result = f.File(ModuleControlMailbox.ResultName);
    Equal("validation", result.RootElement.GetProperty("stage").GetString(), "stale result stage");
    Equal((int)ErrorCode.TargetExpired, result.RootElement.GetProperty("result").GetProperty("error").GetProperty("code").GetInt32(),
        "stale error code");
}

static void AcquireFailure()
{
    using var f = new Fixture();
    var error = new OperationError(ErrorCode.Conflict, "lease held elsewhere", GameplayCapabilityIds.Controls, "run-A", true);
    f.Controls.AcquireError = error;
    var nonce = f.PutRequest("pause");
    f.Tick();
    Equal(0, f.Controls.Lease.Requests.Count, "acquisition failure executed");
    using (var result = f.File(ModuleControlMailbox.ResultName))
    {
        Equal("acquire", result.RootElement.GetProperty("stage").GetString(), "acquire stage");
        Equal("lease held elsewhere", result.RootElement.GetProperty("result").GetProperty("error").GetProperty("message").GetString(),
            "acquire SDK error");
    }
    f.Controls.AcquireError = null;
    f.PutRequest("resume", nonce: nonce);
    f.Tick();
    Equal(1, f.Controls.Acquires, "failed acquisition nonce was retried");
    Equal(0, f.Controls.Lease.Requests.Count, "failed acquisition nonce later executed");
}

static void ExpiredDuringAcquire()
{
    using var f = new Fixture();
    f.Controls.OnAcquire = () => f.Now = f.Now.AddSeconds(31);
    var nonce = f.PutRequest("pause"); // Default expiry is 30 seconds from the fixture clock.
    f.Tick();
    Equal(1, f.Controls.Acquires, "acquisition count");
    Equal(0, f.Controls.Lease.Requests.Count, "expired request reached Execute");
    Equal(1, f.Controls.Lease.Disposes, "expired lease was not released");
    using (var result = f.File(ModuleControlMailbox.ResultName))
    {
        Equal("validation", result.RootElement.GetProperty("stage").GetString(), "expiry result stage");
        Equal((int)ErrorCode.TimedOut,
            result.RootElement.GetProperty("result").GetProperty("error").GetProperty("code").GetInt32(),
            "expiry error code");
    }
    f.PutRequest("resume", nonce: nonce);
    f.Tick();
    Equal(1, f.Controls.Acquires, "expired request nonce acquired again");
}

static void CancelledDuringAcquire()
{
    using var f = new Fixture();
    f.Controls.OnAcquire = f.Cancellation.Cancel;
    f.PutRequest("pause");
    f.Tick();
    Equal(1, f.Controls.Acquires, "acquisition count");
    Equal(0, f.Controls.Lease.Requests.Count, "cancelled request reached Execute");
    Equal(1, f.Controls.Lease.Disposes, "cancelled lease was not released");
    using (var result = f.File(ModuleControlMailbox.ResultName))
    {
        Equal("validation", result.RootElement.GetProperty("stage").GetString(), "cancellation result stage");
        Equal((int)ErrorCode.Cancelled,
            result.RootElement.GetProperty("result").GetProperty("error").GetProperty("code").GetInt32(),
            "cancellation error code");
    }
    f.Tick();
    Equal(1, f.Controls.Acquires, "cancelled scope acquired again");
}

static void OldSession()
{
    var storage = new MemoryStorage();
    string old;
    using (var first = new Fixture(storage))
    {
        old = first.Mailbox.SessionId;
        first.PutRequest("pause");
    }
    using var second = new Fixture(storage);
    Check(second.Mailbox.SessionId != old, "reactivation reused the session ID");
    second.Tick();
    second.Tick(); // The next status write carries the rejected old-session input.
    Equal(0, second.Controls.Acquires, "old-session request acquired controls");
    Equal(0, second.Controls.Lease.Requests.Count, "old-session request executed");
    Check(!storage.Has(ModuleControlMailbox.ResultName), "unknown old request produced a fake completion result");
    using var status = second.File(ModuleControlMailbox.StatusName);
    Equal(second.Mailbox.SessionId, status.RootElement.GetProperty("sessionId").GetString(), "new status session");
    Equal(0, status.RootElement.GetProperty("commandsConsumed").GetInt32(), "old request consumed the new budget");
    Equal((int)ErrorCode.ScopeExpired, status.RootElement.GetProperty("inputError").GetProperty("code").GetInt32(),
        "old-session input error");
}

static void CompletedReceiptSurvivesReactivation()
{
    var storage = new MemoryStorage();
    string oldSession;
    string oldNonce;
    byte[] completed;
    using (var first = new Fixture(storage))
    {
        oldSession = first.Mailbox.SessionId;
        oldNonce = first.PutRequest("pause");
        first.Tick();
        Equal(1, first.Controls.Lease.Requests.Count, "old request did not complete");
        completed = storage.Bytes(ModuleControlMailbox.ResultName).ToArray();
    }
    using var second = new Fixture(storage);
    Check(second.Mailbox.SessionId != oldSession, "reactivation reused the old session");
    second.Tick();
    second.Tick();
    Equal(0, second.Controls.Acquires, "completed old request acquired controls again");
    Check(completed.SequenceEqual(storage.Bytes(ModuleControlMailbox.ResultName)),
        "new activation overwrote the completed SDK receipt");
    using var result = second.File(ModuleControlMailbox.ResultName);
    Equal(oldSession, result.RootElement.GetProperty("sessionId").GetString(), "receipt session for sender correlation");
    Equal(oldNonce, result.RootElement.GetProperty("nonce").GetString(), "receipt nonce for sender correlation");
    using var status = second.File(ModuleControlMailbox.StatusName);
    Equal(second.Mailbox.SessionId, status.RootElement.GetProperty("sessionId").GetString(), "new status session");
    Equal((int)ErrorCode.ScopeExpired, status.RootElement.GetProperty("inputError").GetProperty("code").GetInt32(),
        "stale request input error");
}

static void DuplicateNonce()
{
    using var f = new Fixture();
    var nonce = f.PutRequest("pause");
    f.Tick();
    var first = f.Storage.Bytes(ModuleControlMailbox.ResultName).ToArray();
    f.Tick(); // Unchanged input is also a duplicate.
    Equal(1, f.Controls.Lease.Requests.Count, "unchanged request replayed");
    f.PutRequest("resume", nonce: nonce);
    f.Tick();
    Equal(1, f.Controls.Lease.Requests.Count, "same nonce with changed bytes replayed");
    Check(first.SequenceEqual(f.Storage.Bytes(ModuleControlMailbox.ResultName)), "duplicate nonce replaced its original result");
    f.Tick();
    using var status = f.File(ModuleControlMailbox.StatusName);
    Equal(1, status.RootElement.GetProperty("commandsConsumed").GetInt32(), "duplicate consumed another command slot");
}

static void NonceCase()
{
    using var f = new Fixture();
    string nonce;
    do nonce = Guid.NewGuid().ToString("N"); while (nonce == nonce.ToUpperInvariant());
    f.PutRequest("pause", nonce: nonce);
    f.Tick();
    f.PutRequest("resume", nonce: nonce.ToUpperInvariant());
    f.Tick();
    Equal(1, f.Controls.Lease.Requests.Count, "uppercase spelling replayed the same GUID nonce");
}

static void PendingResult()
{
    using var f = new Fixture();
    var nonce = f.PutRequest("pause");
    f.Storage.FailWrites(ModuleControlMailbox.ResultName, 2);
    f.Tick();
    Equal(1, f.Controls.Lease.Requests.Count, "initial control execution");
    Check(!f.Storage.Has(ModuleControlMailbox.ResultName), "failed result write was treated as success");
    f.Tick();
    Equal(1, f.Controls.Lease.Requests.Count, "pending result re-executed");
    Check(!f.Storage.Has(ModuleControlMailbox.ResultName), "second result write unexpectedly succeeded");
    f.Tick();
    Equal(1, f.Controls.Lease.Requests.Count, "successful retry re-executed");
    using var result = f.File(ModuleControlMailbox.ResultName);
    Equal(nonce, result.RootElement.GetProperty("nonce").GetString(), "pending result nonce");
    Equal("execute", result.RootElement.GetProperty("stage").GetString(), "pending result stage");
    Equal(3, f.Storage.WritesFor(ModuleControlMailbox.ResultName), "result write retry count");
}

static void StatusWriteFailure()
{
    using var f = new Fixture();
    f.PutRequest("pause");
    f.Storage.FailWrites(ModuleControlMailbox.StatusName, 1);
    f.Tick();
    Equal(0, f.Storage.Lists, "request was listed after status write failure");
    Equal(0, f.Controls.Acquires, "request was processed after status write failure");
    f.Tick();
    Equal(1, f.Controls.Lease.Requests.Count, "request did not recover after status write succeeded");
}

static void Lifetime()
{
    using (var f = new Fixture())
    {
        f.PutRequest("pause");
        f.Cancellation.Cancel();
        f.Tick();
        Equal(0, f.Storage.Writes, "cancelled mailbox wrote status");
        Equal(0, f.Controls.Acquires, "cancelled mailbox acquired controls");
    }
    using (var f = new Fixture())
    {
        f.PutRequest("resume");
        f.Mailbox.Dispose();
        f.Tick();
        Equal(0, f.Storage.Writes, "disposed mailbox wrote status");
        Equal(0, f.Controls.Acquires, "disposed mailbox acquired controls");
    }
}

static void InvalidRequests()
{
    using var seed = new Fixture();
    var baseRequest = seed.Request("pause");
    var variants = new (string Name, byte[] Bytes)[]
    {
        ("malformed JSON", Encoding.UTF8.GetBytes("{")),
        ("JSON array", Encoding.UTF8.GetBytes("[]")),
        ("missing nonce", Encode(Without(baseRequest, "nonce"))),
        ("invalid nonce", Encode(With(baseRequest, "nonce", "not-a-guid"))),
        ("duplicate nonce", Encoding.UTF8.GetBytes(Serialize(baseRequest)[..^1] + ",\"nonce\":\"" + Guid.NewGuid().ToString("N") + "\"}")),
        ("unknown field", Encode(With(baseRequest, "surprise", true))),
        ("wrong schema", Encode(With(baseRequest, "schemaVersion", 2))),
        ("blank run", Encode(With(baseRequest, "expectedRunId", " "))),
        ("long run", Encode(With(baseRequest, "expectedRunId", new string('x', 129)))),
        ("generation zero", Encode(With(baseRequest, "expectedGeneration", 0))),
        ("unsupported action", Encode(With(baseRequest, "action", "retry"))),
        ("expired", Encode(With(baseRequest, "expiresUtc", seed.Now.AddSeconds(-1).ToString("O")))),
        ("too-far expiry", Encode(With(baseRequest, "expiresUtc", seed.Now.AddSeconds(126).ToString("O")))),
        ("valueless with value", Encode(With(baseRequest, "value", 2.0))),
        ("valueless with unit", Encode(With(baseRequest, "unit", "rate"))),
        ("nonpositive speed", Encode(With(seed.Request("set-speed", 0, "rate"), "value", -1.0))),
        ("speed wrong unit", Encode(With(seed.Request("set-speed", 1.2, "rate"), "unit", "game-seconds"))),
        ("speed with chart unit", Encode(seed.Request("set-speed", 1.2, "chart-coordinates"))),
        ("speed missing value", Encode(Without(seed.Request("set-speed", 1.2, "rate"), "value"))),
        ("seek wrong unit", Encode(With(seed.Request("seek", 3.5, "game-seconds"), "unit", "rate"))),
        ("seek missing unit", Encode(Without(seed.Request("seek", 3.5, "game-seconds"), "unit"))),
        ("NaN literal", Encoding.UTF8.GetBytes(Serialize(seed.Request("set-speed", 1.2, "rate")).Replace("1.2", "NaN", StringComparison.Ordinal))),
        ("huge exponent", Encoding.UTF8.GetBytes(Serialize(seed.Request("set-speed", 1.2, "rate")).Replace("1.2", "1e999", StringComparison.Ordinal)))
    };
    foreach (var (name, bytes) in variants)
    {
        using var f = new Fixture();
        // Use this activation's session for every case so validation, not stale-session refusal, is exercised.
        var text = Encoding.UTF8.GetString(bytes).Replace(seed.Mailbox.SessionId, f.Mailbox.SessionId, StringComparison.Ordinal);
        f.Storage.Put(ModuleControlMailbox.RequestName, Encoding.UTF8.GetBytes(text));
        f.Tick();
        Check(f.Controls.Acquires == 0 && f.Controls.Lease.Requests.Count == 0, name + " reached controls");
        Check(f.Storage.Has(ModuleControlMailbox.ResultName) || f.Logs.Any(item => item.Contains("INPUT REFUSED", StringComparison.Ordinal)),
            name + " produced no input rejection or validation result");
    }
}

static void InvalidNonceConsumed()
{
    using var f = new Fixture();
    var nonce = Guid.NewGuid().ToString("N");
    f.Storage.Put(ModuleControlMailbox.RequestName, Encode(With(f.Request("pause", nonce: nonce), "surprise", 1)));
    f.Tick();
    Equal(0, f.Controls.Acquires, "invalid request acquired controls");
    using (var result = f.File(ModuleControlMailbox.ResultName))
        Equal("validation", result.RootElement.GetProperty("stage").GetString(), "invalid request stage");
    f.PutRequest("resume", nonce: nonce);
    f.Tick();
    Equal(0, f.Controls.Acquires, "invalid request nonce later executed");
    f.Tick();
    using var status = f.File(ModuleControlMailbox.StatusName);
    Equal(1, status.RootElement.GetProperty("commandsConsumed").GetInt32(), "invalid request budget count");
}

static void InvalidSessionIds()
{
    foreach (var session in new (string Name, object? Value)[]
        { ("missing", null), ("malformed", "not-a-guid"), ("null", null) })
    {
        using var f = new Fixture();
        var request = f.Request("pause");
        if (session.Name == "missing") request.Remove("sessionId");
        else request["sessionId"] = session.Value;
        f.Storage.Put(ModuleControlMailbox.RequestName, Encode(request));
        f.Tick();
        f.Tick(); // The next status write carries the input error from the rejected request.
        Equal(0, f.Controls.Acquires, session.Name + " session acquired controls");
        Check(!f.Storage.Has(ModuleControlMailbox.ResultName), session.Name + " session produced a correlated result");
        using var status = f.File(ModuleControlMailbox.StatusName);
        Check(status.RootElement.GetProperty("inputError").ValueKind == JsonValueKind.Object,
            session.Name + " session did not produce a status input error");
    }
}

static void ListedSizeLimit()
{
    using var f = new Fixture();
    f.PutRequest("pause");
    f.Storage.AdvertisedRequestBytes = 4097;
    f.Tick();
    Equal(0, f.Storage.Reads, "oversize list entry was read");
    Equal(0, f.Controls.Acquires, "oversize list entry reached controls");
    f.Tick();
    using var status = f.File(ModuleControlMailbox.StatusName);
    Check(status.RootElement.GetProperty("inputError").ValueKind == JsonValueKind.Object, "oversize input error was not published");
}

static void ReadSizeLimit()
{
    using var f = new Fixture();
    f.Storage.Put(ModuleControlMailbox.RequestName, new byte[4097]);
    f.Storage.AdvertisedRequestBytes = 100;
    f.Tick();
    Equal(1, f.Storage.Reads, "request was not read");
    Equal(0, f.Controls.Acquires, "oversize read reached controls");
}

static void ReadLengthMismatch()
{
    using var f = new Fixture();
    f.PutRequest("pause");
    f.Storage.AdvertisedRequestBytes = f.Storage.Bytes(ModuleControlMailbox.RequestName).Length - 1;
    f.Tick();
    Equal(1, f.Storage.Reads, "listed request was not read");
    Equal(0, f.Controls.Acquires, "length-changed request reached controls");
    f.Tick();
    using var status = f.File(ModuleControlMailbox.StatusName);
    Check(status.RootElement.GetProperty("inputError").ValueKind == JsonValueKind.Object,
        "length mismatch did not produce an input error");
}

static void CommandBudget()
{
    using var f = new Fixture();
    for (var i = 0; i < ModuleControlMailbox.MaximumCommands; i++)
    {
        f.PutRequest(i % 2 == 0 ? "pause" : "resume");
        f.Tick();
    }
    Equal(ModuleControlMailbox.MaximumCommands, f.Controls.Lease.Requests.Count, "first 32 commands");
    f.PutRequest("pause");
    f.Tick();
    Equal(ModuleControlMailbox.MaximumCommands, f.Controls.Lease.Requests.Count, "33rd command executed");
    using (var result = f.File(ModuleControlMailbox.ResultName))
        Equal((int)ErrorCode.QuotaExceeded, result.RootElement.GetProperty("result").GetProperty("error").GetProperty("code").GetInt32(),
            "budget error code");
    f.Tick();
    using var status = f.File(ModuleControlMailbox.StatusName);
    Equal(ModuleControlMailbox.MaximumCommands, status.RootElement.GetProperty("commandsConsumed").GetInt32(), "budget status");
}

static void ValidationBudget()
{
    using var f = new Fixture();
    for (var i = 0; i < ModuleControlMailbox.MaximumCommands; i++)
    {
        f.Storage.Put(ModuleControlMailbox.RequestName, Encode(With(f.Request("pause"), "surprise", i)));
        f.Tick();
    }
    Equal(0, f.Controls.Acquires, "invalid requests reached controls");
    f.PutRequest("pause");
    f.Tick();
    Equal(0, f.Controls.Acquires, "valid 33rd request executed after 32 validation failures");
    using var result = f.File(ModuleControlMailbox.ResultName);
    Equal((int)ErrorCode.QuotaExceeded,
        result.RootElement.GetProperty("result").GetProperty("error").GetProperty("code").GetInt32(),
        "validation budget error code");
}

static void ExecuteException()
{
    using var f = new Fixture();
    var nonce = f.PutRequest("pause");
    f.Controls.Lease.ThrowOnExecute = true;
    f.Tick();
    Equal(1, f.Controls.Lease.Requests.Count, "SDK Execute entry count");
    using (var result = f.File(ModuleControlMailbox.ResultName))
    {
        Equal("exception", result.RootElement.GetProperty("stage").GetString(), "exception stage");
        var message = result.RootElement.GetProperty("result").GetProperty("error").GetProperty("message").GetString()!;
        Check(message.Contains("effects may be unknown", StringComparison.OrdinalIgnoreCase), "exception hid side-effect uncertainty");
    }
    f.Controls.Lease.ThrowOnExecute = false;
    f.PutRequest("resume", nonce: nonce);
    f.Tick();
    Equal(1, f.Controls.Lease.Requests.Count, "exception nonce replayed");
}

static Dictionary<string, object?> With(Dictionary<string, object?> source, string name, object? value)
{
    var copy = new Dictionary<string, object?>(source, StringComparer.Ordinal) { [name] = value };
    return copy;
}
static Dictionary<string, object?> Without(Dictionary<string, object?> source, string name)
{
    var copy = new Dictionary<string, object?>(source, StringComparer.Ordinal);
    copy.Remove(name);
    return copy;
}
static string Serialize(Dictionary<string, object?> request) => JsonSerializer.Serialize(request);
static byte[] Encode(Dictionary<string, object?> request) => JsonSerializer.SerializeToUtf8Bytes(request);
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
}

internal sealed class Fixture : IDisposable
{
    internal static readonly DateTimeOffset FixedNow = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    internal DateTimeOffset Now { get; set; } = FixedNow;
    internal MemoryStorage Storage { get; }
    internal ControlService Controls { get; } = new();
    internal CancellationTokenSource Cancellation { get; } = new();
    internal List<string> Logs { get; } = new();
    internal ModuleControlMailbox Mailbox { get; }

    internal Fixture(MemoryStorage? storage = null)
    {
        Storage = storage ?? new MemoryStorage();
        Mailbox = new ModuleControlMailbox(Storage, () => Controls, Cancellation.Token,
            (message, _) => Logs.Add(message), () => Now);
    }

    internal Dictionary<string, object?> Request(string action, double? value = null, string? unit = null,
        string? nonce = null, string? run = null, long generation = 7)
    {
        var request = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = 1,
            ["sessionId"] = Mailbox.SessionId,
            ["nonce"] = nonce ?? Guid.NewGuid().ToString("N"),
            ["expectedRunId"] = run ?? "run-A",
            ["expectedGeneration"] = generation,
            ["action"] = action,
            ["expiresUtc"] = Now.AddSeconds(30).ToString("O")
        };
        if (value.HasValue) request["value"] = value.Value;
        if (unit is not null) request["unit"] = unit;
        return request;
    }

    internal string PutRequest(string action, double? value = null, string? unit = null,
        string? nonce = null, string? run = null, long generation = 7)
    {
        var request = Request(action, value, unit, nonce, run, generation);
        Storage.Put(ModuleControlMailbox.RequestName, JsonSerializer.SerializeToUtf8Bytes(request));
        return (string)request["nonce"]!;
    }

    internal void Tick() => Mailbox.Tick(Snapshot());
    internal JsonDocument File(string name) => JsonDocument.Parse(Storage.Bytes(name));
    public void Dispose() { Mailbox.Dispose(); Cancellation.Dispose(); }

    private static GameplayControlObservationSnapshot Snapshot()
    {
        var state = new GameplayControlSnapshot(7, "run-A", true, true, true, true, false, false, false, true,
            Array.Empty<GameplayControlAvailability>(), Array.Empty<GameplayModification>());
        return new GameplayControlObservationSnapshot(3, state, Array.Empty<GameplayControlAvailability>());
    }
}

internal sealed class MemoryStorage : IPluginStorageService
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _failedWrites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _writeCounts = new(StringComparer.Ordinal);
    internal long? AdvertisedRequestBytes { get; set; }
    internal int Reads { get; private set; }
    internal int Lists { get; private set; }
    internal int Writes { get; private set; }
    internal void Put(string name, byte[] bytes) => _files[name] = bytes.ToArray();
    internal bool Has(string name) => _files.ContainsKey(name);
    internal byte[] Bytes(string name) => _files.TryGetValue(name, out var value) ? value : throw new InvalidOperationException("Missing storage file: " + name);
    internal void FailWrites(string name, int count) => _failedWrites[name] = count;
    internal int WritesFor(string name) => _writeCounts.GetValueOrDefault(name);

    public Result<ConfigurationSnapshot<T>> LoadConfiguration<T>(string name, int schemaVersion, Func<T> defaults,
        IReadOnlyList<ConfigurationMigration>? migrations = null, Func<T, string?>? validate = null)
        => Result<ConfigurationSnapshot<T>>.Ok(new(schemaVersion, defaults()));
    public Result SaveConfiguration<T>(string name, int schemaVersion, T value, Func<T, string?>? validate = null) => Result.Ok();
    public Result<byte[]> Read(PluginStorageArea area, string name)
    {
        Reads++;
        return _files.TryGetValue(name, out var value) ? Result<byte[]>.Ok(value.ToArray())
            : Result<byte[]>.Fail(new(ErrorCode.TargetMissing, "No such data file."));
    }
    public Result Write(PluginStorageArea area, string name, byte[] data)
    {
        Writes++;
        _writeCounts[name] = _writeCounts.GetValueOrDefault(name) + 1;
        if (_failedWrites.TryGetValue(name, out var remaining) && remaining > 0)
        {
            _failedWrites[name] = remaining - 1;
            return Result.Fail(new(ErrorCode.QuotaExceeded, "Injected write failure."));
        }
        _files[name] = data.ToArray();
        return Result.Ok();
    }
    public Result Delete(PluginStorageArea area, string name) { _files.Remove(name); return Result.Ok(); }
    public Result<IReadOnlyList<PluginStorageEntry>> List(PluginStorageArea area)
    {
        Lists++;
        IReadOnlyList<PluginStorageEntry> entries = _files.Select(item => new PluginStorageEntry(area, item.Key,
            item.Key == ModuleControlMailbox.RequestName && AdvertisedRequestBytes.HasValue
                ? AdvertisedRequestBytes.Value : item.Value.Length)).ToArray();
        return Result<IReadOnlyList<PluginStorageEntry>>.Ok(entries);
    }
    public Result<PluginStorageQuota> GetQuota() => Result<PluginStorageQuota>.Ok(new(0, 1_000_000, 100_000));
    public Result<IDisposable> Subscribe(Action<PluginStorageChanged> handler) => Result<IDisposable>.Ok(new Noop());
    private sealed class Noop : IDisposable { public void Dispose() { } }
}

internal sealed class ControlService : IGameplayControlService
{
    internal ControlLease Lease { get; } = new();
    internal int Acquires { get; private set; }
    internal int StateReads { get; private set; }
    internal bool AllowExperimental { get; private set; }
    internal CancellationToken AcquireToken { get; private set; }
    internal OperationError? AcquireError { get; set; }
    internal Action? OnAcquire { get; set; }

    public Result<GameplayControlSnapshot> GetState(CancellationToken cancellationToken = default)
    {
        StateReads++;
        return Result<GameplayControlSnapshot>.Fail(new(ErrorCode.NotReady, "Not sampled in this test."));
    }
    public Result<IGameplayControlSubscription> Subscribe(Action<GameplayControlObservationSnapshot> handler,
        CancellationToken cancellationToken = default)
        => Result<IGameplayControlSubscription>.Fail(new(ErrorCode.OperationUnsupported, "Subscription is supplied by the fixture."));
    public Result<IGameplayControlLease> Acquire(bool allowExperimental = false, CancellationToken cancellationToken = default)
    {
        Acquires++;
        AllowExperimental = allowExperimental;
        AcquireToken = cancellationToken;
        var onAcquire = OnAcquire;
        OnAcquire = null;
        onAcquire?.Invoke();
        return AcquireError is null ? Result<IGameplayControlLease>.Ok(Lease) : Result<IGameplayControlLease>.Fail(AcquireError);
    }
}

internal sealed class ControlLease : IGameplayControlLease
{
    internal long Generation { get; set; } = 7;
    internal string? RunId { get; set; } = "run-A";
    internal List<GameplayControlRequest> Requests { get; } = new();
    internal int ActionOverloadCalls { get; private set; }
    internal int Disposes { get; private set; }
    internal CancellationToken ExecuteToken { get; private set; }
    internal Result<GameplayControlReceipt>? NextResult { get; set; }
    internal bool ThrowOnExecute { get; set; }
    public long ControllerGeneration => Generation;
    public string? ObservedRunId => RunId;
    public Result<IGameplayControlCommand> Submit(GameplayControlRequest request, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("The synchronous mailbox fixture verifies Execute.");
    public Result<GameplayControlReceipt> Execute(GameplayControlRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        ExecuteToken = cancellationToken;
        if (ThrowOnExecute) throw new InvalidOperationException("injected SDK exception");
        if (NextResult is not null) return NextResult;
        var modification = new GameplayModification("sdk-test", request.Action, request.ControllerGeneration,
            request.ObservedRunId, 1.5, true, "dispatched")
        {
            Request = request,
            TransactionState = GameplayControlTransactionState.Dispatched
        };
        return Result<GameplayControlReceipt>.Ok(new GameplayControlReceipt(modification, null, "sdk-dispatched"));
    }
    public Result<GameplayControlReceipt> Execute(GameplayControlAction action, CancellationToken cancellationToken = default)
    {
        ActionOverloadCalls++;
        return Result<GameplayControlReceipt>.Fail(new(ErrorCode.OperationUnsupported, "Action overload must not be used."));
    }
    public void Dispose() => Disposes++;
}
