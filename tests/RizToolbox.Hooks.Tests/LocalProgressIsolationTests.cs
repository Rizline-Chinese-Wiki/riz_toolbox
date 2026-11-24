using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using Identity = RizToolbox.Runtime.Hooks.ExactHookBroker.LocalProgressIdentity;
using Mark = RizToolbox.Runtime.Hooks.ExactHookBroker.LocalProgressTerminalMark;

internal static class LocalProgressIsolationTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("LOCAL progress/Timeline sealed authorities reject unpinned images and generic static binding", Authority),
        ("LOCAL Timeline UInt64 value/return layout rejects changed fields sizes byrefs and alignments", HandleLayouts),
        ("LOCAL exact Progress around preserves original observers and only claimed tails suppress all three Timeline ABIs", OrdinaryAndTerminal),
        ("LOCAL progress recursion and in-flight detach retain claims while nested ordinary progress masks them", ReentryAndDetach),
        ("LOCAL terminal context never flows to worker Timeline entries", ThreadIsolation),
        ("LOCAL unknown progress classification remains visible until original and cleanup leave", UnknownClassification),
        ("LOCAL progress original failures restore contexts and preserve exception identity", OriginalFailure),
        ("LOCAL progress/Timeline ownership rejects competitors aliases and foreign-thread disposal", Ownership),
        ("LOCAL Timeline real cdecl thunks preserve UInt64 struct return args and exact empty suppressed handle", CdeclAbi)
        ,("Sealed LOCAL LevelOver marks Progress tails before failing busy handlers and worker rejection", LevelOverBeforeHandler)
        ,("Sealed LOCAL LevelOver keeps claimed unknown ordinary and mismatching frames protected without contaminating child contexts", LevelOverUnknownFrames)
        ,("Sealed LOCAL LevelOver reentry after detach marks a fresh exact Progress frame before skipping its handler", LevelOverReentry)
        ,("An unknown claimed Progress frame survives same-receiver recursion through an ordinary child", UnknownClaimedRecursion)
    };
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("LOCAL progress/Timeline isolation invariant failed."); }
    private static T Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static readonly IntPtr Local = new(unchecked((long)0x12345678ABCDEF00));
    private static readonly IntPtr Ordinary = new(0x4444);
    private static readonly IntPtr Method = new(unchecked((long)0x76543210FEDCBA00));
    private static readonly Identity Token = new("exact-session", 23, 0x3333, Local.ToInt64());
    private static VerifiedHookTarget Target(string id, ExactHookAbi abi)
        => new(id, new(Interlocked.Add(ref Addresses.Next, 16)), Method, new(0x4321), abi);
    // Only fixtures bypass metadata authority. They prove delegate/context logic,
    // never game metadata acceptance, Dobby patching or runtime LOCAL readiness.
    private static ExactHookBroker.LocalProgressIsolationTargets Targets()
        => (ExactHookBroker.LocalProgressIsolationTargets)typeof(ExactHookBroker.LocalProgressIsolationTargets)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                Enumerable.Repeat(typeof(VerifiedHookTarget), 4).ToArray(), null)!.Invoke(new object[] {
                    Target("progress", ExactHookAbi.InstanceVoid), Target("mode", ExactHookAbi.StaticTimelineModeVoid),
                    Target("instant", ExactHookAbi.StaticTimelineInstantaneousHandle), Target("end", ExactHookAbi.StaticTimelineEndRangeVoid) });
    private static void Authority()
    {
        Throws<UnsupportedSignatureException>(() => ExactHookBroker.LocalProgressIsolationTargets.Resolve(null!, new(1), "wrong"));
        Throws<ArgumentNullException>(() => ExactHookBroker.LocalProgressIsolationTargets.Resolve(null!, new(1),
            "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"));
        Check(typeof(ExactHookBroker.LocalProgressIsolationTargets).GetConstructors().Length == 0);
        Check(Marshal.SizeOf<ExactHookBroker.TimelineEventHandle>() == 8
            && Marshal.OffsetOf<ExactHookBroker.TimelineEventHandle>(nameof(ExactHookBroker.TimelineEventHandle.Value)) == IntPtr.Zero);
        using var fixture = new ProgressFixture();
        foreach (var abi in new[] { ExactHookAbi.StaticTimelineModeVoid, ExactHookAbi.StaticTimelineInstantaneousHandle, ExactHookAbi.StaticTimelineEndRangeVoid })
            Throws<UnsupportedSignatureException>(() => fixture.Broker.Bind(Target("generic.rejected", abi), _ => true, (_, _, _, _) => { }));
        Check(fixture.Engine.Installs == 0);
    }
    private static void OrdinaryAndTerminal()
    {
        using var f = new ProgressFixture(); var terminal = false; var progressCalls = 0; var order = new List<string>();
        ExactHookBroker.LocalProgressIsolationBinding? binding = null; ExactHookBroker.Binding? observation = null;
        f.ProgressOriginal = (self, info) =>
        {
            Check(self == Local && info == Method); ++progressCalls; order.Add("original");
            Check(binding!.Read(Token) is { InFlight: 1, UnknownInFlight: 0, Known: true });
            Check(observation!.ReadActivity() is { InFlight: 1, Known: true });
            if (terminal) Check(binding.MarkTerminal(Token) == Mark.MarkedCurrentContext);
            f.Timeline(expectEmpty: terminal);
        };
        using (observation = f.Broker.Bind(f.Targets.Progress, _ => true, (phase, _, _, _) => { order.Add(phase.ToString()); Check(observation!.ReadActivity().InFlight == 1); }))
        using (binding = f.Bind())
        {
            f.Progress(Local); terminal = true; f.Progress(Local); f.Timeline(false);
            Check(progressCalls == 2 && order.SequenceEqual(new[] { "Before", "original", "After", "Before", "original", "After" }));
            Check(f.ModeCalls == 2 && f.InstantCalls == 2 && f.EndCalls == 2
                && binding.Describe() is { InFlight: 0, UnknownInFlight: 0, Entered: 2, Exited: 2, TerminalMarks: 1, SuppressedTimelineCalls: 3 });
            Check(binding.MarkTerminal(Token) == Mark.NoCurrentContext && observation.ReadActivity().InFlight == 0);
        }
    }
    private static void HandleLayouts()
    {
        var valid = new NativeTypeLayout("Steamworks.Data.TimelineEventHandle", NativeValueKind.Struct, 8,
            Fields: new[] { new NativeFieldLayout("Value", "System.UInt64", 0) });
        HookTargetValidation.ValidateTimelineHandleLayout(valid, 8);
        foreach (var invalid in new[] { valid with { Size = 16 }, valid with { ByRef = true }, valid with { Kind = NativeValueKind.Reference },
            valid with { Name = "OtherHandle" }, valid with { Fields = Array.Empty<NativeFieldLayout>() },
            valid with { Fields = new[] { new NativeFieldLayout("Value", "System.UInt32", 0) } },
            valid with { Fields = new[] { new NativeFieldLayout("Value", "System.UInt64", 8) } },
            valid with { Fields = new[] { new NativeFieldLayout("Value", "System.UInt64", 0), new NativeFieldLayout("Extra", "System.UInt64", 8) } } })
            Throws<UnsupportedSignatureException>(() => HookTargetValidation.ValidateTimelineHandleLayout(invalid, 8));
        Throws<UnsupportedSignatureException>(() => HookTargetValidation.ValidateTimelineHandleLayout(valid, 4));
    }
    private static void ReentryAndDetach()
    {
        using var f = new ProgressFixture(); var depth = 0; var calls = 0; ExactHookBroker.LocalProgressIsolationBinding? binding = null;
        f.ProgressOriginal = (self, _) =>
        {
            ++calls;
            if (self == Ordinary)
            { Check(binding!.MarkTerminal(Token) == Mark.DifferentCurrentContext); f.Timeline(false); return; }
            if (depth++ == 0)
            {
                Check(binding!.MarkTerminal(Token) == Mark.MarkedCurrentContext); binding.Dispose();
                f.Progress(Ordinary);
                // A wrong trailing identity cannot revoke an already claimed
                // same-receiver recursive context after logical detachment.
                ((ExactHookBroker.InstanceVoid)f.Engine.Replacements[f.Targets.Progress.Address])(Local, new(0xBAD));
                f.Timeline(true);
            }
            else
            { Check(binding!.Read(Token).InFlight == 2); f.Timeline(true); }
            --depth;
        };
        binding = f.Bind(); f.Progress(Local);
        Check(calls == 3 && f.ModeCalls == 1 && f.InstantCalls == 1 && f.EndCalls == 1
            && binding.Describe() is { InFlight: 0, Entered: 3, Exited: 3, SuppressedTimelineCalls: 6 });
        f.ProgressOriginal = (_, _) => { ++calls; f.Timeline(false); }; f.Progress(Local);
        Check(calls == 4 && binding.MarkTerminal(Token) == Mark.Detached);
    }
    private static void ThreadIsolation()
    {
        using var f = new ProgressFixture(); ExactHookBroker.LocalProgressIsolationBinding? binding = null;
        f.ProgressOriginal = (_, _) =>
        {
            Check(binding!.MarkTerminal(Token) == Mark.MarkedCurrentContext);
            Exception? failure = null; var worker = new Thread(() => { try { f.Timeline(false); } catch (Exception error) { failure = error; } });
            worker.Start(); worker.Join(); Check(failure is null); f.Timeline(true);
        };
        using (binding = f.Bind()) f.Progress(Local);
        Check(f.ModeCalls == 1 && f.InstantCalls == 1 && f.EndCalls == 1);
    }
    private static void UnknownClassification()
    {
        using var f = new ProgressFixture(); var calls = 0; ExactHookBroker.LocalProgressIsolationBinding? binding = null;
        f.ProgressOriginal = (_, _) =>
        {
            ++calls; Check(binding!.Read(Token) is { InFlight: 0, UnknownInFlight: 1, Known: false });
            Check(binding.MarkTerminal(Token) == Mark.UnknownCurrentContext); f.Timeline(false);
        };
        using (binding = f.Bind(_ => throw new IOException("pure snapshot failed")))
        {
            f.Progress(Local); Check(calls == 1 && binding.Describe() is { InFlight: 0, UnknownInFlight: 0, ClassificationFaults: 1, UnknownObserved: 1 });
            Check(binding.Read(Token).Known && binding.MarkTerminal(Token) == Mark.NoCurrentContext);
        }
    }
    private static void OriginalFailure()
    {
        using var f = new ProgressFixture(); var error = new IOException("original remains original");
        ExactHookBroker.LocalProgressIsolationBinding? binding = null; var calls = 0;
        f.ProgressOriginal = (_, _) => { ++calls; Check(binding!.MarkTerminal(Token) == Mark.MarkedCurrentContext); f.Timeline(true); throw error; };
        using (binding = f.Bind())
        {
            Check(ReferenceEquals(Throws<IOException>(() => f.Progress(Local)), error)); f.Timeline(false);
            Check(calls == 1 && binding.Describe() is { InFlight: 0, Exited: 1 } && binding.Read(Token).InFlight == 0);
        }
    }
    private static void Ownership()
    {
        using var f = new ProgressFixture(); using var binding = f.Bind();
        Throws<InvalidOperationException>(() => f.Bind());
        Throws<UnsupportedSignatureException>(() => f.Broker.Bind(f.Targets.Progress with { MethodInfo = new(7) }, _ => true, (_, _, _, _) => { }));
        Task.Run(() => Throws<InvalidOperationException>(() => binding.Dispose())).GetAwaiter().GetResult();
        f.ProgressOriginal = (_, _) =>
        {
            binding.Dispose(); Throws<InvalidOperationException>(() => f.Bind());
            Check(binding.MarkTerminal(Token) == Mark.MarkedCurrentContext); f.Timeline(true);
        };
        f.Progress(Local);
        using var rebound = f.Bind(); Check(f.Engine.Installs == 4);
    }
    private static T Cdecl<T>(Delegate replacement, Type result, params Type[] parameters) where T : Delegate
    {
        var pointer = Marshal.GetFunctionPointerForDelegate(replacement); var dynamic = new DynamicMethod("local_timeline_cdecl", result, parameters);
        var il = dynamic.GetILGenerator(); for (var index = 0; index < parameters.Length; ++index) il.Emit(OpCodes.Ldarg, (short)index);
        il.Emit(OpCodes.Ldc_I8, pointer.ToInt64()); il.Emit(OpCodes.Conv_I);
        il.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, result, parameters); il.Emit(OpCodes.Ret);
        return (T)dynamic.CreateDelegate(typeof(T));
    }
    private static void CdeclAbi()
    {
        using var f = new ProgressFixture(); using var binding = f.Bind();
        f.ModeEntry = Cdecl<ExactHookBroker.StaticTimelineMode>(f.Engine.Replacements[f.Targets.Mode.Address], typeof(void), typeof(int), typeof(IntPtr));
        f.InstantEntry = Cdecl<ExactHookBroker.StaticTimelineInstantaneous>(f.Engine.Replacements[f.Targets.Instantaneous.Address], typeof(ExactHookBroker.TimelineEventHandle),
            typeof(IntPtr), typeof(IntPtr), typeof(IntPtr), typeof(uint), typeof(float), typeof(int), typeof(IntPtr));
        f.EndEntry = Cdecl<ExactHookBroker.StaticTimelineEndRange>(f.Engine.Replacements[f.Targets.EndRange.Address], typeof(void), typeof(ExactHookBroker.TimelineEventHandle), typeof(float), typeof(IntPtr));
        f.Timeline(false);
        f.ProgressOriginal = (_, _) => { Check(binding.MarkTerminal(Token) == Mark.MarkedCurrentContext); f.Timeline(true); };
        var progress = Cdecl<ExactHookBroker.InstanceVoid>(f.Engine.Replacements[f.Targets.Progress.Address], typeof(void), typeof(IntPtr), typeof(IntPtr));
        progress(Local, Method); f.Timeline(false);
        Check(f.ModeCalls == 2 && f.InstantCalls == 2 && f.EndCalls == 2 && binding.Describe().SuppressedTimelineCalls == 3);
    }
    private static ExactHookBroker.LocalRunCommandTarget LevelOverToken(VerifiedHookTarget target)
        => (ExactHookBroker.LocalRunCommandTarget)typeof(ExactHookBroker.LocalRunCommandTarget)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(VerifiedHookTarget), typeof(ExactHookBroker.LocalRunCommand) }, null)!
            .Invoke(new object[] { target, ExactHookBroker.LocalRunCommand.ControllerLevelOver });
    private static void LevelOverBeforeHandler()
    {
        for (var variant = 0; variant < 3; ++variant)
        {
            using var f = new ProgressFixture(); using var progress = f.Bind();
            var target = Target("sealed.levelover", ExactHookAbi.InstanceVoid); var originalCalls = 0; var handled = 0;
            f.Engine.Originals[target.Address] = new ExactHookBroker.InstanceVoid((_, _) => ++originalCalls);
            using var command = f.Broker.BindLocalRunCommandPolicy(LevelOverToken(target),
                _ => variant == 2 ? throw new IOException("sealed LevelOver classifier failed") : ExactHookBroker.LocalRunCommandDecision.ClaimLocal,
                _ => { ++handled; throw new InvalidOperationException("Host handler is busy or faulted."); });
            f.ProgressOriginal = (_, _) => {
                ((ExactHookBroker.InstanceVoid)f.Engine.Replacements[target.Address])(new(Token.ControllerIdentity), Method);
                f.Timeline(true);
            };
            if (variant != 1) f.Progress(Local);
            else
            {
                Exception? failure = null; var thread = new Thread(() => { try { f.Progress(Local); } catch (Exception error) { failure = error; } });
                thread.Start(); thread.Join(); Check(failure is null);
            }
            Check(originalCalls == 0 && handled == (variant == 0 ? 1 : 0) && f.ModeCalls == 0 && f.InstantCalls == 0 && f.EndCalls == 0
                && progress.Describe().SuppressedTimelineCalls == 3 && progress.Read(Token).InFlight == 0);
            f.Timeline(false);
        }
    }
    private static void LevelOverUnknownFrames()
    {
        for (var variant = 0; variant < 3; ++variant)
        {
            using var f = new ProgressFixture(); var target = Target("sealed.levelover.unknown", ExactHookAbi.InstanceVoid);
            f.Engine.Originals[target.Address] = new ExactHookBroker.InstanceVoid((_, _) => throw new InvalidOperationException("Claimed LevelOver reached stock."));
            using var command = f.Broker.BindLocalRunCommandPolicy(LevelOverToken(target), _ => ExactHookBroker.LocalRunCommandDecision.ClaimLocal,
                _ => throw new InvalidOperationException("No usable queue transaction."));
            using var progress = f.Bind(variant == 0 ? _ => throw new IOException("pure classifier unknown")
                : variant == 1 ? _ => null : self => self == Local ? Token : null);
            var controller = variant == 2 ? Token.ControllerIdentity + 1 : Token.ControllerIdentity;
            f.ProgressOriginal = (self, _) => {
                if (self == Ordinary) { f.Timeline(false); return; }
                ((ExactHookBroker.InstanceVoid)f.Engine.Replacements[target.Address])(new(controller), Method);
                f.Progress(Ordinary); // Separate ordinary child remains an ABI pass-through.
                f.Timeline(true);
            };
            f.Progress(Local);
            Check(progress.Describe().UnknownObserved > 0 && progress.Describe().InFlight == 0
                && f.ModeCalls == 1 && f.InstantCalls == 1 && f.EndCalls == 1);
            f.Timeline(false); // No thread-local protection survives finally.
        }
    }
    private static void LevelOverReentry()
    {
        using var f = new ProgressFixture(); using var progress = f.Bind(); var depth = 0; var handled = 0;
        var target = Target("sealed.levelover.reentry", ExactHookAbi.InstanceVoid);
        f.Engine.Originals[target.Address] = new ExactHookBroker.InstanceVoid((_, _) => throw new InvalidOperationException("Claimed LevelOver reached stock."));
        ExactHookBroker.LocalRunCommandBinding? command = null;
        command = f.Broker.BindLocalRunCommandPolicy(LevelOverToken(target), _ => ExactHookBroker.LocalRunCommandDecision.ClaimLocal,
            _ => { ++handled; command!.Dispose(); f.Progress(Ordinary); throw new IOException("Detached busy handler failed."); });
        f.ProgressOriginal = (_, _) => {
            ++depth;
            ((ExactHookBroker.InstanceVoid)f.Engine.Replacements[target.Address])(new(Token.ControllerIdentity), Method);
            f.Timeline(true); --depth;
        };
        f.Progress(Local); Check(depth == 0 && handled == 1 && progress.Describe().SuppressedTimelineCalls == 6
            && command.DescribePolicy().SuppressedPolicyReentries == 1 && command.DescribeHook().OriginalCalls == 0);
        command.Dispose();
    }
    private static void UnknownClaimedRecursion()
    {
        using var f = new ProgressFixture(); var target = Target("sealed.levelover.unknown-recursion", ExactHookAbi.InstanceVoid);
        f.Engine.Originals[target.Address] = new ExactHookBroker.InstanceVoid((_, _) => throw new InvalidOperationException("Claim reached stock."));
        using var command = f.Broker.BindLocalRunCommandPolicy(LevelOverToken(target), _ => ExactHookBroker.LocalRunCommandDecision.ClaimLocal, _ => { });
        using var progress = f.Bind(self => self == Local ? throw new IOException("unknown LOCAL classifier") : null);
        var depth = 0;
        f.ProgressOriginal = (self, _) => {
            if (self == Ordinary)
            {
                f.Timeline(false); f.Progress(Local); f.Timeline(false); return;
            }
            if (depth++ == 0)
            {
                ((ExactHookBroker.InstanceVoid)f.Engine.Replacements[target.Address])(new(Token.ControllerIdentity), Method);
                f.Progress(Ordinary);
            }
            else Check(progress.Describe().UnknownInFlight == 2);
            f.Timeline(true); --depth;
        };
        f.Progress(Local); Check(depth == 0 && progress.Describe() is { InFlight: 0, UnknownInFlight: 0, SuppressedTimelineCalls: 6 }
            && f.ModeCalls == 2 && f.InstantCalls == 2 && f.EndCalls == 2);
    }

    private sealed class ProgressEngine : INativeDetourEngine
    {
        internal readonly Dictionary<IntPtr, Delegate> Originals = new(), Replacements = new();
        internal int Installs;
        public Delegate Install(VerifiedHookTarget target, Delegate replacement) { ++Installs; Replacements.Add(target.Address, replacement); return Originals[target.Address]; }
    }
    private sealed class ProgressFixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new(); internal readonly Logger Log = new();
        internal readonly ProgressEngine Engine = new(); internal readonly ExactHookBroker Broker;
        internal readonly ExactHookBroker.LocalProgressIsolationTargets Targets = LocalProgressIsolationTests.Targets();
        internal ExactHookBroker.InstanceVoid ProgressOriginal = (_, _) => { };
        internal ExactHookBroker.StaticTimelineMode? ModeEntry;
        internal ExactHookBroker.StaticTimelineInstantaneous? InstantEntry;
        internal ExactHookBroker.StaticTimelineEndRange? EndEntry;
        internal int ModeCalls, InstantCalls, EndCalls;
        private const ulong Handle = 0xF123456789ABCDEF;
        private static readonly float Offset = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC01234));
        internal ProgressFixture()
        {
            Broker = new(Engine, Main, Log);
            Engine.Originals[Targets.Progress.Address] = new ExactHookBroker.InstanceVoid((self, info) => ProgressOriginal(self, info));
            Engine.Originals[Targets.Mode.Address] = new ExactHookBroker.StaticTimelineMode((mode, method) => { Check(mode == int.MinValue && method == Method); ++ModeCalls; });
            Engine.Originals[Targets.Instantaneous.Address] = new ExactHookBroker.StaticTimelineInstantaneous((title, description, icon, priority, offset, clip, method) =>
            {
                Check(title == Local && description == Method && icon == Ordinary && priority == uint.MaxValue && clip == int.MinValue
                    && BitConverter.SingleToInt32Bits(offset) == BitConverter.SingleToInt32Bits(Offset) && method == Method);
                ++InstantCalls; return new(Handle);
            });
            Engine.Originals[Targets.EndRange.Address] = new ExactHookBroker.StaticTimelineEndRange((handle, offset, method) =>
            { Check(handle.Value == Handle && BitConverter.SingleToInt32Bits(offset) == BitConverter.SingleToInt32Bits(Offset) && method == Method); ++EndCalls; });
        }
        internal ExactHookBroker.LocalProgressIsolationBinding Bind(Func<IntPtr, Identity?>? classify = null)
            => Broker.BindLocalProgressIsolation(Targets, classify ?? (receiver => receiver == Local ? Token : null));
        internal void Progress(IntPtr receiver) => ((ExactHookBroker.InstanceVoid)Engine.Replacements[Targets.Progress.Address])(receiver, Method);
        internal void Timeline(bool expectEmpty)
        {
            (ModeEntry ?? (ExactHookBroker.StaticTimelineMode)Engine.Replacements[Targets.Mode.Address])(int.MinValue, Method);
            var result = (InstantEntry ?? (ExactHookBroker.StaticTimelineInstantaneous)Engine.Replacements[Targets.Instantaneous.Address])
                (Local, Method, Ordinary, uint.MaxValue, Offset, int.MinValue, Method);
            Check(result.Value == (expectEmpty ? 0 : Handle));
            (EndEntry ?? (ExactHookBroker.StaticTimelineEndRange)Engine.Replacements[Targets.EndRange.Address])(new(Handle), Offset, Method);
        }
        public void Dispose() => Main.Dispose();
    }
}
