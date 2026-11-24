using RizToolbox.Adapter.Judgements;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static class HoldContextRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Challenge hold VFX clobbered RSI resolves only from the retained MoveNext receiver", ClobberedRegister),
        ("Boolean ABI preserves raw AL receiver MethodInfo and releases after original", BooleanAbi),
        ("Suppressed nested MoveNext cannot borrow the outer hold identity", Nested),
        ("Boolean original exception propagates once and releases context and invocation depth", OriginalFailure),
        ("Boolean capture after and release failures preserve original and other observer cleanup", ObserverFailures),
        ("Withdrawing during MoveNext denies identity and defers root release to native return", Withdrawal),
        ("Boolean wrong-thread unmatched and detached entries never capture hold metadata", Isolation),
        ("Out-of-order hold scope release cannot restore an old coroutine", OutOfOrder)
    };
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Hold context invariant failed."); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Expected fail-closed hold context rejection.");
    }
    private static void ClobberedRegister()
    {
        var depth = 1; using var contexts = new HoldCoroutineContext(() => depth);
        var reader = new Reader(contexts); var root = new Lease();
        foreach (var site in JudgementSourceResolver.Sites.Where(x => x.NoteSource == JudgementNoteSource.HoldCoroutine))
        {
            var frame = new NativeJudgeFrame { Rdi = 50, Xmm2 = new() { Low = unchecked((uint)BitConverter.SingleToInt32Bits(site.Sentinel!.Value)) } };
            Reject(() => JudgementSourceResolver.Resolve(site, frame, reader));
            using (contexts.Enter(80, root))
                foreach (var rsi in new ulong[] { 80, 0, 0x3f800000, 0xffffffff })
                {
                    frame.Rsi = rsi;
                    Check(JudgementSourceResolver.Resolve(site, frame, reader) == new JudgementNoteSourceValue(50, 60));
                }
            Reject(() => JudgementSourceResolver.Resolve(site, frame, reader));
        }
        Check(root.Releases == 2);
    }
    private static void BooleanAbi()
    {
        using var f = new BooleanFixture(); var roots = new List<Lease>(); var after = 0; var calls = 0;
        byte result = 0;
        f.Engine.Original = new ExactHookBroker.InstanceBooleanReturn0((receiver, method) => {
            calls++; Check(receiver == new IntPtr(80) && method == new IntPtr(90) && f.Context.Current() == 80);
            Check(roots.Last().Releases == 0 && ExactHookBroker.BooleanInvocationDepth == 1); return result;
        });
        using var binding = f.Bind(receiver => { var root = new Lease(); roots.Add(root); return f.Context.Enter(receiver.ToInt64(), root); },
            _ => { after++; Check(f.Context.Current() == 80 && roots.Last().Releases == 0); });
        foreach (var value in new byte[] { 0, 1, 0x80, 0xff }) { result = value; Check(f.Engine.Invoke() == value); }
        Check(calls == 4 && after == 4 && roots.All(x => x.Releases == 1) && ExactHookBroker.BooleanInvocationDepth == 0);
        Reject(() => f.Context.Current());
    }
    private static void Nested()
    {
        using var f = new BooleanFixture(); var calls = 0; var captures = 0; var root = new Lease();
        f.Engine.Original = new ExactHookBroker.InstanceBooleanReturn0((_, _) => {
            if (++calls == 1) {
                Check(f.Context.Current() == 80); Check(f.Engine.Invoke() == 9);
                Check(f.Context.Current() == 80 && root.Releases == 0); return 7;
            }
            Check(ExactHookBroker.BooleanInvocationDepth == 2); Reject(() => f.Context.Current()); return 9;
        });
        using var binding = f.Bind(receiver => { captures++; return f.Context.Enter(receiver.ToInt64(), root); }, _ => { });
        Check(f.Engine.Invoke() == 7 && calls == 2 && captures == 1 && root.Releases == 1);
        Check(binding.Describe().SuppressedReentries == 1 && ExactHookBroker.BooleanInvocationDepth == 0);
    }
    private static void OriginalFailure()
    {
        using var f = new BooleanFixture(); var root = new Lease(); var after = 0; var calls = 0;
        var failure = new InvalidOperationException("original identity"); var fail = true;
        f.Engine.Original = new ExactHookBroker.InstanceBooleanReturn0((_, _) => { calls++; if (fail) throw failure; return 1; });
        using var binding = f.Bind(receiver => f.Context.Enter(receiver.ToInt64(), root), _ => after++);
        Exception? actual = null; try { f.Engine.Invoke(); } catch (Exception error) { actual = error; }
        Check(ReferenceEquals(actual, failure) && calls == 1 && after == 0 && root.Releases == 1 && ExactHookBroker.BooleanInvocationDepth == 0);
        Reject(() => f.Context.Current());
        fail = false; Check(f.Engine.Invoke() == 1 && calls == 2 && after == 1 && root.Releases == 2);
    }
    private static void ObserverFailures()
    {
        using var f = new BooleanFixture(); var calls = 0; var after = 0;
        var failedCleanup = new Lease { FailRelease = true }; var good = new Lease();
        f.Engine.Original = new ExactHookBroker.InstanceBooleanReturn0((_, _) => { calls++; return 0x80; });
        using var one = f.Bind<Lease>(_ => throw new InvalidOperationException("capture"), _ => { throw new Exception("unreachable"); });
        using var two = f.Bind(_ => failedCleanup, _ => throw new InvalidOperationException("after"));
        using var three = f.Bind(_ => good, _ => after++);
        Check(f.Engine.Invoke() == 0x80 && calls == 1 && after == 1 && good.Releases == 1 && failedCleanup.Releases == 1);
        Check(ExactHookBroker.BooleanInvocationDepth == 0);
    }
    private static void Withdrawal()
    {
        using var f = new BooleanFixture(); var root = new Lease(); var after = 0; ExactHookBroker.Binding? binding = null;
        f.Engine.Original = new ExactHookBroker.InstanceBooleanReturn0((_, _) => {
            Check(f.Context.Current() == 80); binding!.Dispose(); f.Context.Dispose();
            Reject(() => f.Context.Current()); Check(root.Releases == 0); return 1;
        });
        using (binding = f.Bind(receiver => f.Context.Enter(receiver.ToInt64(), root), _ => after++))
            Check(f.Engine.Invoke() == 1 && root.Releases == 1 && after == 0);
    }
    private static void Isolation()
    {
        using var f = new BooleanFixture(); var calls = 0; var metadata = 0; var captures = 0;
        f.Engine.Original = new ExactHookBroker.InstanceBooleanReturn0((_, _) => { Interlocked.Increment(ref calls); return 1; });
        using var binding = f.Broker.BindRetainedBooleanObservation(f.Target,
            receiver => { metadata++; return receiver == new IntPtr(80); },
            _ => { captures++; return new Lease(); }, _ => { });
        byte actual = 0;
        var thread = new Thread(() => actual = f.Engine.Invoke()); thread.Start(); thread.Join();
        Check(actual == 1 && metadata == 0 && captures == 0 && calls == 1);
        Check(f.Engine.Invoke(new(81)) == 1 && metadata == 1 && captures == 0);
        binding.Dispose(); Check(f.Engine.Invoke() == 1 && captures == 0 && calls == 3);
    }
    private static void OutOfOrder()
    {
        var depth = 1; using var context = new HoldCoroutineContext(() => depth);
        var outerRoot = new Lease(); var innerRoot = new Lease();
        var outer = context.Enter(80, outerRoot); depth = 2; var inner = context.Enter(81, innerRoot);
        outer.Dispose(); Reject(() => context.Current()); inner.Dispose(); depth = 1;
        Reject(() => context.Current()); outer.Dispose();
        Check(outerRoot.Releases == 1 && innerRoot.Releases == 1);
    }
    private sealed class Reader : IJudgementNoteSourceReader
    {
        private readonly HoldCoroutineContext _context;
        internal Reader(HoldCoroutineContext context) => _context = context;
        public void RequireOwner(long owner) { Check(owner == 50); }
        public void RequireNote(long note) { Check(note == 60); }
        public long ReadWaitingNote(long owner, bool drag, int index) => throw new Exception("Unexpected source.");
        public long ReadHoldNote(long expectedOwner) { Check(_context.Current() == 80 && expectedOwner == 50); return 60; }
    }
    private sealed class Lease : IDisposable
    {
        internal int Releases; internal bool FailRelease;
        public void Dispose() { Releases++; if (FailRelease) throw new InvalidOperationException("release"); }
    }
    private sealed class Engine : INativeDetourEngine
    {
        internal Delegate Original = null!; private Delegate _replacement = null!;
        public Delegate Install(VerifiedHookTarget target, Delegate replacement) { _replacement = replacement; return Original; }
        internal byte Invoke(IntPtr? receiver = null) => ((ExactHookBroker.InstanceBooleanReturn0)_replacement)(receiver ?? new(80), new(90));
    }
    private sealed class BooleanFixture : IDisposable
    {
        private static long _addresses = 0x60000000;
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Engine Engine = new();
        internal readonly HoldCoroutineContext Context = new(() => ExactHookBroker.BooleanInvocationDepth);
        internal readonly ExactHookBroker Broker;
        internal readonly VerifiedHookTarget Target = new("test.hold", new(Interlocked.Add(ref _addresses, 16)), new(123), new(456), ExactHookAbi.InstanceBooleanReturn0);
        internal BooleanFixture() => Broker = new(Engine, Main, new QuietLog());
        internal ExactHookBroker.Binding Bind<T>(Func<IntPtr, T> capture, Action<T> after) where T : class, IDisposable
            => Broker.BindRetainedBooleanObservation(Target, _ => true, capture, after);
        public void Dispose() { Context.Dispose(); Main.Dispose(); }
    }
}
