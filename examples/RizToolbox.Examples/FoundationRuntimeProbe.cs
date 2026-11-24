using System.Text;
using System.Text.Json;
using RizToolbox.Sdk;

namespace RizToolbox.Examples;

/// <summary>Explicitly enabled integration checks. Uses only SDK contracts from two isolated packages.</summary>
internal static class FoundationRuntimeProbe
{
    private const string ServiceId = "example.scores/foundation-echo";
    private const string ProbeFile = "foundation/proof.txt";
    internal static void Start(IPluginContext context, bool provider)
    {
        var loaded = context.Configuration.Load("foundation-proof", () => new ProbeSettings());
        if (!loaded.Success) throw new InvalidOperationException(loaded.Error?.Message);
        if (loaded.Value?.Enabled != true) return;
        var settings = loaded.Value;
        var storage = Require(context.GetService<IPluginStorageService>(StorageCapabilityIds.Storage));
        var language = Require(context.GetService<IPluginLocalizationService>(StorageCapabilityIds.Localization));
        var services = Require(context.GetService<IPluginServices>(InterPluginCapabilityIds.Services));

        Check(storage.Subscribe(change => context.Log.Log(LogLevel.Info,
            $"FoundationProbe STORAGE CHANGE {change.Area}/{change.Name} revision={change.Revision}")));
        Check(storage.Write(PluginStorageArea.Data, ProbeFile, Encoding.UTF8.GetBytes(context.PluginId)));
        AssertLocalFile();
        Check(storage.Write(PluginStorageArea.Cache, ProbeFile, Encoding.UTF8.GetBytes("cache:" + context.PluginId)));
        var configurations = Check(storage.List(PluginStorageArea.Configuration));
        if (!configurations.Any(x => x.Name is "runtime-proof" or "runtime-proof.json"))
            Check(storage.SaveConfiguration("runtime-proof", 1, new ProbeConfiguration(context.PluginId, 1)));
        var migrated = Check(storage.LoadConfiguration("runtime-proof", 2,
            () => new ProbeConfiguration(context.PluginId, 2),
            new[] { new ConfigurationMigration(1, 2, old =>
                JsonSerializer.SerializeToElement(new {
                    owner = old.GetProperty("owner").GetString()!, revision = 2 })) },
            value => value.Owner == context.PluginId && value.Revision == 2 ? null : "Probe configuration owner or revision mismatch."));
        Check(storage.SaveConfiguration("runtime-proof", 2, migrated.Value));
        var quota = Check(storage.GetQuota());
        if (quota.UsedBytes <= 0 || quota.UsedBytes > quota.LimitBytes) throw new InvalidOperationException("Storage quota accounting mismatch.");
        context.Log.Log(LogLevel.Info, $"FoundationProbe STORAGE PASS owner={context.PluginId} schema={migrated.SchemaVersion} migrated={migrated.Migrated} bytes={quota.UsedBytes}");

        Check(language.Register("en", new Dictionary<string, string> { ["shared-key"] = context.PluginId + ":en" }, fallback: true));
        Check(language.Register("zh-CN", new Dictionary<string, string> { ["shared-key"] = context.PluginId + ":zh" }));
        Check(language.Subscribe(change => AssertLanguage(change.Language)));
        AssertLanguage(Check(language.GetState()).Language);

        if (provider)
        {
            var published = services.Publish<IPluginDataService>(ServiceId, new Version(1, 0), new EchoProvider(context.PluginId));
            if (!published.Success) throw new InvalidOperationException(published.Error?.Message);
            context.Log.Log(LogLevel.Info, "FoundationProbe SERVICE PUBLISHED generation=" + published.Value!.Descriptor.Generation);
        }
        else
        {
            var query = new ServiceQuery(ServiceId, new Version(1, 0), new Version(2, 0));
            IPluginDataService? current = null;
            double? readyAt = null;
            bool faultExercised = false;
            services.Watch(query, availability =>
            {
                context.Log.Log(LogLevel.Info, $"FoundationProbe SERVICE AVAILABILITY available={availability.Available} generation={availability.Service?.Generation}");
                current = null;
                if (!availability.Available) return;
                var resolved = services.Lookup<IPluginDataService>(query);
                if (!resolved.Available || resolved.Service is null) throw new InvalidOperationException(resolved.Error?.Message);
                current = resolved.Service;
                var reply = current.Invoke(new("echo", new Dictionary<string, string> { ["consumer"] = context.PluginId }));
                if (!reply.Success || reply.Value?.Value != "example.scores->" + context.PluginId)
                    throw new InvalidOperationException(reply.Error?.Message ?? "Cross-package result mismatch.");
                AssertLocalFile(); AssertLanguage(Check(language.GetState()).Language);
                context.Log.Log(LogLevel.Info, "FoundationProbe SERVICE CALL PASS " + reply.Value.Value);
            });
            context.Events.Subscribe<FrameUpdate>(frame =>
            {
                if (current is null) { readyAt = null; return; }
                readyAt ??= frame.ElapsedSeconds;
                if (faultExercised || settings.FaultAfterSeconds is not double delay || frame.ElapsedSeconds - readyAt < delay) return;
                faultExercised = true;
                try { current.Invoke(new("throw")); throw new InvalidOperationException("Provider did not throw."); }
                catch (Exception ex) when (ex.Message.Contains("ServiceProviderFailed", StringComparison.Ordinal))
                { context.Log.Log(LogLevel.Info, "FoundationProbe EXPECTED PROVIDER FAULT consumer-survived"); }
            });
        }

        bool dataChecked = false; double nextDataAttempt = 0;
        context.Events.Subscribe<FrameUpdate>(frame =>
        {
            if (dataChecked || frame.ElapsedSeconds < nextDataAttempt) return;
            nextDataAttempt = frame.ElapsedSeconds + 2;
            var lookup = context.GetService<IDataSnapshotService>(DataCapabilityIds.Read);
            if (!lookup.Available) return;
            var data = lookup.Service!;
            var header = Check(data.GetHeader());
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int offset = 0; offset < header.ChartCount;)
            {
                var page = Check(data.GetCharts(new(header.Token, offset, 128)));
                if (page.Header.Token != header.Token || page.Items.Count == 0) throw new InvalidOperationException("Snapshot page revision or progress mismatch.");
                foreach (var chart in page.Items) if (!ids.Add(chart.Id)) throw new InvalidOperationException("Duplicate chart ID across pages.");
                offset += page.Items.Count;
            }
            if (ids.Count != header.ChartCount) throw new InvalidOperationException("Incomplete catalog pages.");
            int scoreCount = 0;
            for (int offset = 0; offset < header.BestScoreCount;)
            {
                var page = Check(data.GetBestScores(new(header.Token, offset, 128)));
                if (page.Header.Token != header.Token || page.Items.Count == 0 || page.Items.Any(x => !ids.Contains(x.ChartId)))
                    throw new InvalidOperationException("Score snapshot does not match the catalog revision.");
                offset += page.Items.Count; scoreCount += page.Items.Count;
            }
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            var cancelled = data.GetCharts(new(header.Token), cancel.Token);
            if (cancelled.Success || cancelled.Error?.Code != ErrorCode.Cancelled) throw new InvalidOperationException("Cancelled page request was accepted.");
            context.Log.Log(LogLevel.Info, $"FoundationProbe DATA PASS charts={ids.Count} scores={scoreCount} session={header.Token.SessionId} revision={header.Token.Revision}");
            dataChecked = true;
        });

        void AssertLocalFile()
        {
            if (Encoding.UTF8.GetString(Check(storage.Read(PluginStorageArea.Data, ProbeFile))) != context.PluginId)
                throw new InvalidOperationException("Plugin storage namespaces collided.");
        }
        void AssertLanguage(string active)
        {
            var expected = context.PluginId + (active.Equals("zh-cn", StringComparison.OrdinalIgnoreCase) ? ":zh" : ":en");
            var actual = Check(language.Resolve("shared-key"));
            if (actual != expected) throw new InvalidOperationException("Plugin language namespaces or fallback collided.");
            context.Log.Log(LogLevel.Info, $"FoundationProbe LANGUAGE PASS language={active} value={actual}");
        }
    }

    private static T Require<T>(ServiceLookup<T> result) where T : class
        => result.Available && result.Service is not null ? result.Service : throw new InvalidOperationException(result.Error?.Message);
    private static T Check<T>(Result<T> result) => result.Success ? result.Value! : throw new InvalidOperationException(result.Error!.Message);
    private static void Check(Result result) { if (!result.Success) throw new InvalidOperationException(result.Error!.Message); }
    private sealed record ProbeSettings(bool Enabled = false, double? FaultAfterSeconds = null);
    private sealed record ProbeConfiguration(string Owner, int Revision);
    private sealed class EchoProvider : IPluginDataService
    {
        private readonly string _owner;
        internal EchoProvider(string owner) => _owner = owner;
        public OperationResult<ServiceData> Invoke(ServiceRequest request)
        {
            if (request.Operation == "throw") throw new InvalidOperationException("Expected integration provider failure.");
            return OperationResult<ServiceData>.Ok(new(_owner + "->" + request.Arguments!["consumer"]));
        }
    }
}
