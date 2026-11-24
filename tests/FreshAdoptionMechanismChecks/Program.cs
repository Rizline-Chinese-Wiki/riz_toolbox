using System.Reflection;
using System.Text.Json;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Sdk;

var checks = 0;
void Check(bool value, string reason) { ++checks; if (!value) throw new InvalidOperationException(reason); }
void Reject(Action action, ErrorCode code)
{
    ++checks;
    try { action(); }
    catch (GameplayStateException error) when (error.Code == code) { return; }
    throw new InvalidOperationException("Expected rejection: " + code);
}
try
{
    var reads = new PlaybackFreshReadScopes();
    Check(reads.Active == 0, "Starts without borrowed reads");
    using (var outer = reads.Enter())
    {
        Check(reads.Active == 1, "Outer accessor responsibility registered");
        using (var inner = reads.Enter())
        {
            Check(reads.Active == 2, "Nested getter responsibility independent");
            reads.Close();
            Check(reads.Active == 2, "Cancellation does not erase live getter scopes");
            Reject(() => reads.Enter(), ErrorCode.TargetExpired);
            Reject(reads.VerifyOpen, ErrorCode.TargetExpired); // Tail check after the last getter cancelled.
        }
        Check(reads.Active == 1, "Inner finally leaves outer borrower retained");
    }
    Check(reads.Active == 0, "Only both actual finally exits drain reads");
    Reject(reads.VerifyOpen, ErrorCode.TargetExpired);
    var workers = new PlaybackFreshReadScopes(); var actual = workers.Enter();
    Task.Run(() =>
    {
        Reject(() => workers.Enter(), ErrorCode.WrongThread);
        Reject(workers.Close, ErrorCode.WrongThread);
        Reject(actual.Dispose, ErrorCode.WrongThread);
        Reject(workers.VerifyOpen, ErrorCode.WrongThread);
    }).GetAwaiter().GetResult();
    Check(workers.Active == 1, "Worker rejects preserve the owner's live read");
    workers.VerifyOpen(); ++checks;
    var copy = (IDisposable)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(actual, null)!;
    Reject(copy.Dispose, ErrorCode.TargetExpired);
    Check(workers.Active == 1, "Copied scope cannot drain the actual read");
    actual.Dispose(); actual.Dispose(); Check(workers.Active == 0, "Owner release is exact and idempotent");
    using (workers.Enter()) Check(workers.Active == 1, "Owner can still read after rejected worker close");
    var unwind = new PlaybackFreshReadScopes(); var original = new InvalidOperationException("original getter failure");
    try { using var scope = unwind.Enter(); unwind.Close(); throw original; }
    catch (InvalidOperationException error) { Check(ReferenceEquals(error, original), "Scope cleanup preserves the original getter exception"); }
    Check(unwind.Active == 0, "Exceptional getter finally drains exactly once");
    Reject(() => unwind.Enter(), ErrorCode.TargetExpired);
    // Legacy accessor regression and the shared read-scope mechanism only.
    // This deliberately does not construct a formal capture/adoption/stock grant.
    using (var native = new FakeNative())
    {
        native.ConfigurePlaybackOccurrence();
        var binding = new NativePlaybackBinding("legacy-accessor-only", 3, 1, 1,
            native.GameplayController.ToInt64(), native.GameplayInformation.ToInt64(), native.GameplayProgress.ToInt64(),
            native.GameplayPlayer.ToInt64(), native.GameplaySource.ToInt64(), native.GameplayClip.ToInt64(), native.GameplayGetTimeMethod.ToInt64());
        var accessor = new NativePlaybackActualSource(native.GameplayApi, binding, native.GameplayPause.ToInt64());
        Check(native.ActiveRoots == 7, "Legacy accessor still retains seven independent handles");
        _ = accessor.Read(1, 0); ++checks;
        Task.Run(() => Reject(accessor.Dispose, ErrorCode.WrongThread)).GetAwaiter().GetResult();
        Check(native.ActiveRoots == 7, "Wrong-thread legacy dispose preserves its roots");
        var getterReads = new PlaybackFreshReadScopes(); var inGetter = false;
        native.AfterOccurrenceDspRead = () =>
        {
            inGetter = true; getterReads.Close();
            Check(native.ActiveRoots == 7 && getterReads.Active == 1, "Actual NativeRuntime getter cancellation retains all seven roots and its read responsibility");
            inGetter = false;
        };
        var successful = false;
        try
        {
            using var read = getterReads.Enter();
            _ = accessor.ReadDsp();
            getterReads.VerifyOpen(); // The production plain tail guard after a reentrant getter.
            successful = true;
        }
        catch (GameplayStateException error) when (error.Code == ErrorCode.TargetExpired) { ++checks; }
        Check(!successful && !inGetter && getterReads.Active == 0 && native.ActiveRoots == 7, "Cancelled getter has no successful result and unwinds before cleanup");
        var beforeFree = native.FreedRoots;
        accessor.Dispose(); Check(native.ActiveRoots == 0 && native.FreedRoots == beforeFree + 7, "Owner frees exactly seven roots after the getter has left its stack");
        accessor.Dispose(); Check(native.FreedRoots == beforeFree + 7, "Legacy repeated cleanup remains idempotent");
    }
    Console.WriteLine(JsonSerializer.Serialize(new { passed=true, checks, tested="fresh read responsibility mechanism and legacy accessor", formalAdoptionExecuted=false, stockRuntimeTested=false, productionAdmission=false, fabricatedAuthority=false }));
    return 0;
}
catch (Exception error)
{
    Console.WriteLine(JsonSerializer.Serialize(new { passed=false, checks, error=error.ToString(), formalAdoptionExecuted=false }));return 1;
}
