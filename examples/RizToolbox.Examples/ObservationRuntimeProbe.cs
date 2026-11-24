using System.Text.Json;
using RizToolbox.Sdk;

namespace RizToolbox.Examples;

internal static class ObservationRuntimeProbe
{
    internal static void Start(IPluginContext context, bool color = false)
    {
        var config = context.Configuration.Load("foundation-proof", () => new ProbeSettings());
        if (!config.Success) throw new InvalidOperationException(config.Error?.Message);
        if (config.Value?.Enabled != true) return;
        bool hooksReady = false, lifecycleReady = false, colorReady = false;
        long before = 0, after = 0; long? priorRevision = null;
        IHookService? hooks = null;
        double next = 0, reportAt = 0;
        IPluginScope? runScope = null;
        context.Events.Subscribe<RunStarted>(_ =>
        {
            // Legacy run boundaries expire old scopes before public listeners run.
            runScope = ((IScopedPluginContext)context).CreateScope("observed-run", ScopeLifetime.Run);
            var ownedScope = runScope;
            runScope.Own(runScope.Lifetime.Register(() => context.Log.Log(LogLevel.Info,
                $"ObservationProbe RUN SCOPE CANCELLED active={ownedScope.IsActive}")));
            context.Log.Log(LogLevel.Info, "ObservationProbe RUN SCOPE CREATED active=" + runScope.IsActive);
        });
        context.Events.Subscribe<RunFinished>(_ =>
        {
            if (runScope?.IsActive == true) throw new InvalidOperationException("Run scope outlived the result boundary.");
            context.Log.Log(LogLevel.Info, "ObservationProbe RUN SCOPE RELEASE PASS");
        });
        context.Events.Subscribe<FrameUpdate>(frame =>
        {
            if (frame.ElapsedSeconds < next) return;
            next = frame.ElapsedSeconds + 1;
            if (!hooksReady && context.GetService<IHookService>(HookCapabilityIds.Observe) is { Available: true, Service: { } service })
            {
                hooks = service;
                Observe(GameHookPoints.Judge, HookPhase.Before, _ => before++);
                Observe(GameHookPoints.Judge, HookPhase.After, _ => after++);
                Observe(GameHookPoints.Pause, HookPhase.After, _ => context.Log.Log(LogLevel.Info, "ObservationProbe EXACT PAUSE"));
                Observe(GameHookPoints.Resume, HookPhase.After, _ => context.Log.Log(LogLevel.Info, "ObservationProbe EXACT RESUME"));
                if (hooks.RequestMutation(GameHookPoints.Judge.Id).Success) throw new InvalidOperationException("Read-only hook accepted mutation.");
                hooksReady = true;
                // System.Type is a contract identity, not a JSON value. Log its name
                // without asking the serializer to traverse runtime type metadata.
                context.Log.Log(LogLevel.Info, "ObservationProbe HOOKS READY " + JsonSerializer.Serialize(
                    hooks.GetTargets().Select(target => new
                    {
                        target.Id, target.Version, PayloadType = target.PayloadType.FullName, target.Signature
                    })));
            }
            if (!lifecycleReady && context.GetService<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle) is { Available: true, Service: { } lifecycle })
            {
                var subscription = lifecycle.Subscribe(change =>
                {
                    if (priorRevision is long prior && change.Snapshot.Revision <= prior) throw new InvalidOperationException("Lifecycle revision duplicated or regressed.");
                    priorRevision = change.Snapshot.Revision;
                    context.Log.Log(LogLevel.Info, "ObservationProbe LIFECYCLE " + JsonSerializer.Serialize(change));
                    if (change.Kind == GameLifecycleChangeKind.RunEnded)
                    {
                        context.Log.Log(LogLevel.Info, $"ObservationProbe RUN COUNTS before={before} after={after} resultMiss={change.Result?.Miss}");
                    }
                });
                if (!subscription.Success) throw new InvalidOperationException(subscription.Error!.Message);
                priorRevision = subscription.Value!.InitialSnapshot.Revision;
                lifecycleReady = true;
                context.Log.Log(LogLevel.Info, "ObservationProbe INITIAL " + JsonSerializer.Serialize(subscription.Value.InitialSnapshot));
            }
            if (color && !colorReady && context.GetService<ITargetService>(TargetCapabilityIds.Targets) is { Available: true, Service: { } targets }
                && context.GetService<IMutationService>(TargetCapabilityIds.Mutations) is { Available: true, Service: { } mutations })
            {
                var target = targets.GetTargets().Where(x => x.Metadata.Description?.Contains("EdgeCoverCanvas/T", StringComparison.Ordinal) == true)
                    .OrderBy(x => x.Key, StringComparer.Ordinal).FirstOrDefault();
                if (target is not null)
                {
                    var match = targets.Resolve<ResourceColor>(new(Key: target.Key));
                    if (match.Kind != TargetMatchKind.Unique || match.Target is null) throw new InvalidOperationException("Expected unique edge-cover target.");
                    var key = new MutationKey<ResourceColor>(match.Target, "color");
                    var lease = mutations.Override(key, new ResourceColor(.3f, 1f, .4f), 30);
                    if (!lease.Success) throw new InvalidOperationException(lease.Error?.Message);
                    var snapshot = mutations.Describe(key);
                    if (!snapshot.Success || snapshot.Value!.Holders.Count < 3) throw new InvalidOperationException("New and legacy APIs did not share three color holders.");
                    context.Log.Log(LogLevel.Info, "ObservationProbe THREE COLOR HOLDERS " + JsonSerializer.Serialize(snapshot.Value));
                    colorReady = true;
                }
            }
            if (hooks is not null && frame.ElapsedSeconds >= reportAt)
            {
                reportAt = frame.ElapsedSeconds + 10;
                context.Log.Log(LogLevel.Info, $"ObservationProbe HOOK COUNTS before={before} after={after}; " + JsonSerializer.Serialize(hooks.GetDiagnostics()));
            }
        });
        void Observe<T>(HookPoint<T> point, HookPhase phase, Action<T> callback)
        {
            var result = hooks!.Subscribe(point, phase, callback, context.PluginId == "example.scores" ? 20 : 10);
            if (!result.Success) throw new InvalidOperationException(result.Error?.Message);
        }
    }
    private sealed record ProbeSettings(bool Enabled = false);
}
