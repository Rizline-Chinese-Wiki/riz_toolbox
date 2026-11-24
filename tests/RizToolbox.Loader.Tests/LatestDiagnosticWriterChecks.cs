using System.Diagnostics;
using System.Runtime.CompilerServices;
using RizToolbox.Core.Diagnostics;

internal static class LatestDiagnosticWriterChecks
{
    internal static readonly (string Name, Action Run)[] All =
    {
        ("latest diagnostics retain only the running and latest copy while coalescing a slow consumer", SlowConsumer),
        ("terminal diagnostics seal input and persist only the selected newest revision", Terminal),
        ("diagnostic persistence failures complete attempts without claiming a written revision", Failure),
        ("bounded diagnostic disposal leaves an active consumer's synchronizer usable until return", DisposeWhileBlocked),
        ("an idle diagnostic consumer wakes for completion and keeps snapshot reads inert", IdleCompletion)
    };

    private sealed record Copy(long Revision);
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void Complete<T>(LatestDiagnosticWriter<T> writer) where T : class
    {
        writer.RequestComplete();
        Check(SpinWait.SpinUntil(() => writer.GetSnapshot().Completed, TimeSpan.FromSeconds(5)),
            "The diagnostic consumer did not leave after its actual operation returned.");
    }

    private static void SlowConsumer()
    {
        using var entered = new ManualResetEventSlim();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<long>();
        var active = 0;
        using var writer = new LatestDiagnosticWriter<Copy>(async copy =>
        {
            Check(Interlocked.Increment(ref active) == 1, "Persistence callbacks ran concurrently.");
            try
            {
                if (copy.Revision == 1) { entered.Set(); await release.Task.ConfigureAwait(false); }
                writes.Add(copy.Revision);
            }
            finally { Interlocked.Decrement(ref active); }
        });
        try
        {
            var first = PublishOne(writer, 1);
            Check(entered.Wait(TimeSpan.FromSeconds(5)), "The first actual persistence callback did not enter.");
            var pending = PublishFlood(writer, 2, 1001);
            var held = writer.GetSnapshot();
            Check(held.Published == 1001 && held.Queued == 1 && held.Running == 1 && held.Peak == 2
                && held.Coalesced == 999 && held.Rejected == 0 && held.Failures == 0 && held.LastAttempt == 1
                && held.LastWritten == 0 && !held.Completed && !held.Closed,
                "The held consumer did not retain one running plus one latest copy with exact coalescing counters.");
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(first.IsAlive && pending[^1].IsAlive && pending.Take(pending.Length - 1).All(reference => !reference.IsAlive),
                "The writer retained superseded copies beyond its two bounded slots.");
            var timeout = writer.WaitThrough(1001, TimeSpan.FromMilliseconds(20));
            Check(!timeout.Completed && !timeout.Persisted && writer.GetSnapshot().LastAttempt == 1,
                "A wait timeout consumed pending work or pretended the blocked operation had finished.");
            using var waiting = new ManualResetEventSlim();
            var concurrentWait = Task.Run(() => { waiting.Set(); return writer.WaitThrough(1001, TimeSpan.FromSeconds(5)); });
            Check(waiting.Wait(TimeSpan.FromSeconds(5)), "The concurrent completion waiter did not start.");
            Check(writer.Publish(1002, new(1002)) && writer.GetSnapshot().Coalesced == 1000,
                "A concurrent completion wait retained the publish lock or lost latest-copy coalescing.");
            release.SetResult(true);
            var result = writer.WaitThrough(1002, TimeSpan.FromSeconds(5));
            var waited = concurrentWait.GetAwaiter().GetResult();
            Check(result.Completed && result.Persisted && waited.Completed && waited.Persisted
                && writes.SequenceEqual(new long[] { 1, 1002 })
                && writer.WaitThrough(2, TimeSpan.Zero).Persisted,
                "Persistence did not run only first then latest, or a newer success failed to satisfy a coalesced revision.");
            Complete(writer);
            var done = writer.GetSnapshot();
            Check(done.Completed && done.Closed && done.Queued == 0 && done.Running == 0
                && done.Coalesced == 1000 && done.LastWritten == 1002 && done.Failures == 0 && active == 0,
                "A completed writer still retained work or changed its final write evidence.");
        }
        finally { release.TrySetResult(true); }
    }

    private static void Terminal()
    {
        using var entered = new ManualResetEventSlim();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<long>();
        using var writer = new LatestDiagnosticWriter<Copy>(async copy =>
        {
            writes.Add(copy.Revision);
            if (copy.Revision == 1) { entered.Set(); await release.Task.ConfigureAwait(false); }
        });
        try
        {
            Check(writer.Publish(1, new(1)) && entered.Wait(TimeSpan.FromSeconds(5)), "Initial diagnostic did not enter.");
            Check(!writer.Publish(1, new(1)) && !writer.Publish(0, new(0)), "Non-increasing revisions were accepted.");
            Check(writer.Publish(2, new(2)) && writer.Publish(3, new(3), terminal: true), "The terminal latest copy was rejected.");
            Check(!writer.Publish(4, new(4)) && !writer.Publish(3, new(3), terminal: true), "Terminal sealing accepted more work.");
            writer.RequestComplete(); writer.RequestComplete();
            var sealedState = writer.GetSnapshot();
            Check(sealedState.Closed && !sealedState.Completed && sealedState.Coalesced == 1
                && sealedState.Published == 3 && sealedState.Rejected == 4 && sealedState.Queued == 1 && sealedState.Running == 1,
                "Sealing or repeated completion dropped the pending terminal copy or changed revision accounting.");
            release.SetResult(true);
            var result = writer.WaitThrough(3, TimeSpan.FromSeconds(5));
            Check(result.Completed && result.Persisted && writes.SequenceEqual(new long[] { 1, 3 }),
                "A terminal copy did not follow the already-running write exactly once.");
            Complete(writer);
            var missing = writer.WaitThrough(4, TimeSpan.FromSeconds(5));
            Check(!missing.Completed && !missing.Persisted && missing.WriterCompleted,
                "A closed writer fabricated completion or persistence for a rejected future revision.");
        }
        finally { release.TrySetResult(true); }
    }

    private static void Failure()
    {
        var writes = new List<long>();
        using var writer = new LatestDiagnosticWriter<Copy>(copy =>
        {
            writes.Add(copy.Revision);
            if (copy.Revision == 1) throw new IOException("disk failure\n" + new string('x', 2048));
            if (copy.Revision == 2) return Task.FromException(new IOException("async disk failure"));
            return Task.CompletedTask;
        });
        Check(writer.Publish(1, new(1)), "Failure revision was rejected.");
        var failed = writer.WaitThrough(1, TimeSpan.FromSeconds(5));
        var first = writer.GetSnapshot();
        Check(failed.Completed && !failed.Persisted && first.Failures == 1 && first.LastAttempt == 1
            && first.LastCompleted == 1 && first.LastWritten == 0 && first.LastError is { Length: <= 512 }
            && !first.LastError.Any(char.IsControl),
            "A persistence exception was treated as written or retained unbounded exception text.");
        Check(writer.Publish(2, new(2)), "A failed attempt poisoned later publication.");
        var asynchronousFailure = writer.WaitThrough(2, TimeSpan.FromSeconds(5));
        Check(asynchronousFailure.Completed && !asynchronousFailure.Persisted
            && writer.GetSnapshot().Failures == 2 && writer.GetSnapshot().LastWritten == 0,
            "An asynchronous persistence failure was treated as successful writing.");
        Check(writer.Publish(3, new(3), terminal: true), "An asynchronous failure poisoned the terminal publication.");
        var recovered = writer.WaitThrough(3, TimeSpan.FromSeconds(5));
        Complete(writer);
        var last = writer.GetSnapshot();
        Check(recovered.Completed && recovered.Persisted && writer.WaitThrough(1, TimeSpan.Zero).Persisted
            && writes.SequenceEqual(new long[] { 1, 2, 3 }) && last.Failures == 2 && last.LastWritten == 3,
            "The consumer did not continue after failure or retried the failed revision.");
    }

    private static void IdleCompletion()
    {
        var calls = 0;
        using var writer = new LatestDiagnosticWriter<Copy>(_ => { Interlocked.Increment(ref calls); return Task.CompletedTask; });
        var before = writer.GetSnapshot();
        Check(before == writer.GetSnapshot() && before.Published == 0 && before.Queued == 0 && before.Running == 0
            && before.LastWritten == 0 && !before.Closed && !before.Completed, "Reading writer diagnostics dispatched persistence work.");
        Complete(writer);
        var done = writer.GetSnapshot();
        Check(done.Completed && done.Closed && done.Peak == 0 && done.Published == 0 && calls == 0,
            "Completing an idle writer failed to wake it or invented a pending copy.");
        var missing = writer.WaitThrough(1, TimeSpan.Zero);
        Check(!missing.Completed && !missing.Persisted && missing.WriterCompleted
            && !writer.Publish(1, new(1)) && writer.GetSnapshot().Rejected == 1,
            "Idle completion accepted a copy after retiring its consumer.");
    }

    private static void DisposeWhileBlocked()
    {
        using var entered = new ManualResetEventSlim();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<long>();
        var writer = new LatestDiagnosticWriter<Copy>(async copy =>
        {
            writes.Add(copy.Revision);
            if (copy.Revision == 1) { entered.Set(); await release.Task.ConfigureAwait(false); }
        });
        try
        {
            Check(writer.Publish(1, new(1)) && entered.Wait(TimeSpan.FromSeconds(5)), "Blocked writer did not enter.");
            Check(writer.Publish(2, new(2)), "Pending diagnostic was rejected before completion was requested.");
            var watch = Stopwatch.StartNew();
            writer.Dispose();
            Check(watch.Elapsed < TimeSpan.FromSeconds(2), "Default disposal waited indefinitely for the persistence callback.");
            var held = writer.GetSnapshot();
            Check(held.Closed && !held.Completed && held.Running == 1 && held.Queued == 1 && held.LastWritten == 0
                && !writer.Publish(3, new(3)), "Timed-out disposal freed active work or reopened collection.");
            var wait = writer.WaitThrough(2, TimeSpan.FromMilliseconds(20));
            Check(!wait.Completed && !wait.Persisted, "Timeout was confused with a successful final write.");
            release.SetResult(true);
            var finished = writer.WaitThrough(2, TimeSpan.FromSeconds(5));
            Complete(writer); writer.Dispose();
            Check(finished.Completed && finished.Persisted && writes.SequenceEqual(new long[] { 1, 2 })
                && writer.GetSnapshot().Failures == 0, "Disposal destroyed a synchronizer still used by the running consumer.");
        }
        finally { release.TrySetResult(true); writer.Dispose(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PublishOne(LatestDiagnosticWriter<Copy> writer, long revision)
    {
        var copy = new Copy(revision); var reference = new WeakReference(copy);
        Check(writer.Publish(revision, copy), "Initial publication failed."); return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] PublishFlood(LatestDiagnosticWriter<Copy> writer, long first, long last)
    {
        var references = new WeakReference[checked((int)(last - first + 1))];
        for (var revision = first; revision <= last; revision++)
        {
            var copy = new Copy(revision); references[checked((int)(revision - first))] = new(copy);
            Check(writer.Publish(revision, copy), "Increasing publication was rejected while the consumer was held.");
        }
        return references;
    }
}
