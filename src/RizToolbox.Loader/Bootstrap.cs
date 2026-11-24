using System.Security.Cryptography;
using RizToolbox.Adapter;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Loader;

public static class Bootstrap
{
    private static RuntimeBridge? _bridge;
    private static ToolboxHost? _host;
    private static int _started;
    internal static string GameRoot => Path.GetDirectoryName(Environment.ProcessPath!)!;
    internal static string DataRoot => Path.Combine(GameRoot, "BepInEx", "RizToolbox");

    public static void Start()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
        Directory.CreateDirectory(DataRoot);
        var configuration = RuntimeConfiguration.Load(Path.Combine(DataRoot, "runtime.json"));
        var log = new StructuredDiagnosticLogger(new DiagnosticOptions(Path.Combine(DataRoot, "diagnostics"))
        {
            TextLogPath = Path.Combine(DataRoot, "toolbox.log"),
            MinimumLevel = configuration.MinimumLogLevel
        });
        AppDomain.CurrentDomain.ProcessExit += (_, _) => log.Dispose();
        var startup = new RuntimeStartupStatus(DataRoot, configuration, log);
        try
        {
            if (configuration.ConfigurationError is { } error)
                log.Log(LogLevel.Error, error.Message + " " + configuration.ConfigurationDetail);
            log.Log(LogLevel.Info, $"Runtime mode {configuration.Mode}; full tick audit={configuration.FullAuditEachTick}; proof plugins={configuration.EnableProofPlugins}; optional writes={configuration.AllowOptionalWrites}; fixed at startup. {configuration.AssuranceBoundary}");
            string hash;
            using (var assemblyFile = File.OpenRead(Path.Combine(GameRoot, "GameAssembly.dll")))
            using (var sha256 = SHA256.Create())
                hash = Convert.ToHexString(sha256.ComputeHash(assemblyFile)).ToLowerInvariant();
            var profile = BuildProfiles.Select(Path.Combine(Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location)!, "profiles"), hash);
            log.Log(LogLevel.Info, $"RizToolbox 0.3 starting; adapter profile {profile.Id}; diagnostic session {log.SessionId}.");
            var tick = profile.Types[profile.TickType];
            _bridge = new RuntimeBridge(tick.Assembly,
                tick.Namespace, tick.Name, profile.TickMethod, (api, hostTickSequence) =>
                {
                    if (_host is { } host)
                    {
                        host.Tick(hostTickSequence);
                        return;
                    }
                    startup.RunFirstTick(hostTickSequence, () =>
                    {
                        _host = new ToolboxHost(api, profile, log, configuration);
                        log.TryLog(LogLevel.Info, "Unity main-thread host initialized.");
                    }, () => _host!.Tick(hostTickSequence));
                }, log, options: new RuntimeExecutionOptions(configuration.Mode == RuntimeMode.Developer, configuration.FullAuditEachTick),
                measure: sample => _host?.RecordRuntimeEntry(sample), admissionFailed: startup.AdmissionFailed);
            _bridge.Start();
            if (_bridge.IsReady) startup.TransportAttached();
            else startup.TransportUnavailable(_bridge.StartupFailureDetail);
        }
        catch (Exception ex)
        {
            startup.TransportUnavailable(ex.Message);
            log.Log(LogLevel.Error, "当前游戏版本或安装状态不受支持。 Host not started.", ex);
        }
    }
}
