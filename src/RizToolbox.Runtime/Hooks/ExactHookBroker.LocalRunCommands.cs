using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal enum LocalRunCommand { ControllerRetry, ResultRetry, ResultBack, ControllerLevelOver }
    internal enum LocalRunCommandDecision { ContinueOriginal, ClaimLocal }

    /// <summary>Sealed authority for the pinned no-argument instance commands.
    /// These are host isolation entries, never plugin mutation APIs or stock loaders.</summary>
    internal sealed class LocalRunCommandTarget
    {
        private const string SupportedImage = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
        internal VerifiedHookTarget Target { get; }
        internal LocalRunCommand Command { get; }
        private LocalRunCommandTarget(VerifiedHookTarget target, LocalRunCommand command) => (Target, Command) = (target, command);

        internal static LocalRunCommandTarget Resolve(NativeRuntime api, IntPtr gameAssembly, string gameAssemblySha256, LocalRunCommand command)
        {
            // Reject invalid authorities before touching caller/native metadata.
            if (!Enum.IsDefined(typeof(LocalRunCommand), command))
                throw new UnsupportedSignatureException("Unknown LOCAL run command.");
            if (!string.Equals(gameAssemblySha256, SupportedImage, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("LOCAL command isolation requires the pinned GameAssembly image.");
            ArgumentNullException.ThrowIfNull(api);
            if (gameAssembly == IntPtr.Zero) throw new UnsupportedSignatureException("LOCAL command isolation requires the verified module.");
            var (type, member, rva) = command switch
            {
                LocalRunCommand.ControllerRetry => ("NormalModeInLevelController", "Retry", 0x3C1310),
                LocalRunCommand.ResultRetry => ("LevelOverControl", "Retry", 0x3CFF20),
                LocalRunCommand.ResultBack => ("LevelOverControl", "Back", 0x3CE020),
                LocalRunCommand.ControllerLevelOver => ("NormalModeInLevelController", "LevelOver", 0x3C0C00),
                _ => throw new UnsupportedSignatureException("Unknown LOCAL run command.")
            };
            var cls = api.Class("Assembly-CSharp.dll", "", type);
            return new(api.ResolveExactHook("local.run.command." + command, gameAssembly, cls, member,
                Array.Empty<string>(), rva, ExactHookAbi.InstanceVoid), command);
        }
    }

    internal sealed record LocalRunCommandDiagnostics(LocalRunCommand Command, long Classified, long Continued,
        long Claimed, long Skipped, long Faults, long WrongThreadClaims, long SuppressedPolicyReentries);

    internal sealed class LocalRunCommandBinding : IDisposable
    {
        private readonly LocalResultStartBinding _shared;
        internal LocalRunCommand Command { get; }
        internal LocalRunCommandBinding(LocalRunCommand command, LocalResultStartBinding shared) => (Command, _shared) = (command, shared);
        internal LocalRunCommandDiagnostics DescribePolicy()
        {
            var value = _shared.DescribePolicy();
            return new(Command, value.Classified, value.Continued, value.Claimed, value.Skipped, value.Faults,
                value.WrongThreadClaims, value.SuppressedPolicyReentries);
        }
        internal HookDiagnostics DescribeHook() => _shared.DescribeHook();
        internal ManagedBoundaryActivity ReadActivity() => _shared.ReadActivity();
        // Logical detach restores stock forwarding on later entries. A host must
        // retain every required gate until verified retirement of all LOCAL consumers.
        public void Dispose() => _shared.Dispose();
    }

    /// <param name="classify">Any-thread root-free exact identity lookup. Provider state,
    /// metadata.From or a current LOCAL run alone cannot identify the receiver.</param>
    /// <param name="handleLocal">Host command transaction. Claim suppresses stock before all
    /// observers even on handler failure, worker entry, reentry or in-flight detach.</param>
    internal LocalRunCommandBinding BindLocalRunCommandPolicy(LocalRunCommandTarget target,
        Func<IntPtr, LocalRunCommandDecision> classify, Action<IntPtr> handleLocal)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(classify); ArgumentNullException.ThrowIfNull(handleLocal);
        var shared = BindLocalVoidPolicy(target.Target,
            receiver => (LocalResultStartDecision)(int)classify(receiver), handleLocal, target.Command.ToString(),
            target.Command == LocalRunCommand.ControllerLevelOver ? MarkLocalProgressTerminalForControllerClaim : null);
        return new(target.Command, shared);
    }
}
