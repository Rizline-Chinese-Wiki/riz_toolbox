using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Resolves every sealed authority before publishing the first detour policy.
/// This transport does not supply a launch handler or classify an unbound result screen.</summary>
internal sealed class NativeLocalRunBoundaryTransport : ILocalRunBoundaryTransport
{
    private readonly ExactHookBroker _broker;
    private readonly ExactHookBroker.LocalResultStartTarget _start;
    private readonly ExactHookBroker.LocalRunCommandTarget[] _commands;
    private NativeLocalRunBoundaryTransport(ExactHookBroker broker, ExactHookBroker.LocalResultStartTarget start,
        ExactHookBroker.LocalRunCommandTarget[] commands) => (_broker, _start, _commands) = (broker, start, commands);
    internal static NativeLocalRunBoundaryTransport Resolve(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        AdapterBuildProfile profile, IntPtr gameAssembly, string dobby)
    {
        main.VerifyAccess();
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || gameAssembly == IntPtr.Zero
            || profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("LOCAL run gates require the pinned Windows x64 Unity build.");
        var image = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using (var stream = File.OpenRead(image)) using (var sha = SHA256.Create())
            if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
                throw new UnsupportedSignatureException("LOCAL gate image differs from its pinned profile.");
        var start = ExactHookBroker.LocalResultStartTarget.Resolve(api, gameAssembly, profile.GameAssemblySha256);
        var commands = new[] { ExactHookBroker.LocalRunCommand.ControllerRetry, ExactHookBroker.LocalRunCommand.ResultRetry,
            ExactHookBroker.LocalRunCommand.ResultBack, ExactHookBroker.LocalRunCommand.ControllerLevelOver }.Select(command => ExactHookBroker.LocalRunCommandTarget.Resolve(
                api, gameAssembly, profile.GameAssemblySha256, command)).ToArray();
        return new(new ExactHookBroker(dobby, main, log), start, commands);
    }
    public ILocalRunBoundaryBinding Bind(LocalBoundaryKind boundary, Func<long, bool> classify, Action<long> handle)
    {
        if (boundary == LocalBoundaryKind.ResultStart)
        {
            var binding = _broker.BindLocalResultStartPolicy(_start,
                receiver => classify(receiver.ToInt64()) ? ExactHookBroker.LocalResultStartDecision.ClaimLocal : ExactHookBroker.LocalResultStartDecision.ContinueOriginal,
                receiver => handle(receiver.ToInt64()));
            return new Binding(boundary, binding, () => {
                var hook = binding.DescribeHook(); var policy = binding.DescribePolicy();
                return new(boundary, hook.NativeCalls, hook.OriginalCalls, policy.Classified, policy.Continued, policy.Claimed,
                    policy.Skipped, policy.Faults, policy.WrongThreadClaims, policy.SuppressedPolicyReentries);
            }, () => { var activity = binding.ReadActivity(); return new(activity.InFlight, activity.Known); });
        }
        var index = boundary switch {
            LocalBoundaryKind.ControllerRetry => 0, LocalBoundaryKind.ResultRetry => 1, LocalBoundaryKind.ResultBack => 2,
            LocalBoundaryKind.ControllerLevelOver => 3,
            _ => throw new UnsupportedSignatureException("Unknown LOCAL boundary transport request.")
        };
        var commandBinding = _broker.BindLocalRunCommandPolicy(_commands[index],
            receiver => classify(receiver.ToInt64()) ? ExactHookBroker.LocalRunCommandDecision.ClaimLocal : ExactHookBroker.LocalRunCommandDecision.ContinueOriginal,
            receiver => handle(receiver.ToInt64()));
        return new Binding(boundary, commandBinding, () => {
            var hook = commandBinding.DescribeHook(); var policy = commandBinding.DescribePolicy();
            return new(boundary, hook.NativeCalls, hook.OriginalCalls, policy.Classified, policy.Continued, policy.Claimed,
                policy.Skipped, policy.Faults, policy.WrongThreadClaims, policy.SuppressedPolicyReentries);
        }, () => { var activity = commandBinding.ReadActivity(); return new(activity.InFlight, activity.Known); });
    }
    private sealed class Binding : ILocalRunBoundaryBinding
    {
        private readonly IDisposable _binding;
        private readonly Func<LocalRunBoundaryCounters> _read;
        private readonly Func<LocalRunBoundaryActivity> _activity;
        internal Binding(LocalBoundaryKind boundary, IDisposable binding, Func<LocalRunBoundaryCounters> read, Func<LocalRunBoundaryActivity> activity)
            => (_binding, _read, _activity) = (binding, read, activity);
        public LocalRunBoundaryCounters Read() => _read();
        public LocalRunBoundaryActivity ReadActivity() => _activity();
        public void Dispose() => _binding.Dispose();
    }
}
