using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

// A local mod-development launcher. It does not change Steam, global environment,
// license checks, game binaries or Doorstop's persistent disable setting.
var configurationPath = Path.Combine(AppContext.BaseDirectory, "riztoolbox-launch.json");
var proofPath = Path.Combine(AppContext.BaseDirectory, "last-launch.json");
try
{
    var config = JsonSerializer.Deserialize<LaunchConfiguration>(File.ReadAllText(configurationPath))
        ?? throw new InvalidDataException("Launch configuration is missing.");
    if (args.SequenceEqual(new[] { "--self-test" })) return LaunchSpec.SelfTest(config);
    if (args.Length != 0) throw new ArgumentException("The launcher reads riztoolbox-launch.json beside its executable.");
    var start = LaunchSpec.Prepare(config);
    if (!Process.GetProcessesByName("steam").Any()) throw new InvalidOperationException("Open Steam and sign in before launching this owned game.");
    if (Process.GetProcessesByName("Rizline").Any()) throw new InvalidOperationException("Close Rizline before starting another instance.");
    using var game = Process.Start(start) ?? throw new InvalidOperationException("Game process was not created.");
    File.WriteAllText(proofPath, JsonSerializer.Serialize(new {
        observedAtUtc = DateTimeOffset.UtcNow, processId = game.Id, gameDirectory = start.WorkingDirectory,
        steamAppId = start.Environment["SteamAppId"], removedInheritedDoorstopKeys = LaunchSpec.InheritedKeys,
        persistentConfigurationChanged = false, hostInitializationVerified = false
    }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception error)
{
    try { File.WriteAllText(proofPath, JsonSerializer.Serialize(new { error = error.GetType().Name, error.Message, launched = false })); } catch { }
    return 1;
}

internal sealed record LaunchConfiguration(int SchemaVersion, string GameDirectory);
internal static class LaunchSpec
{
    internal static readonly string[] InheritedKeys = { "DOORSTOP_DISABLE", "DOORSTOP_INITIALIZED", "DOORSTOP_INVOKE_DLL_PATH", "DOORSTOP_CORLIB_OVERRIDE" };
    private const string SupportedGameHash = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    internal static ProcessStartInfo Prepare(LaunchConfiguration config)
    {
        if (config.SchemaVersion != 1 || string.IsNullOrWhiteSpace(config.GameDirectory) || !Path.IsPathFullyQualified(config.GameDirectory))
            throw new InvalidDataException("A version-1 configuration with an absolute game directory is required.");
        var root = Path.GetFullPath(config.GameDirectory);
        var executable = Path.Combine(root, "Rizline.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Rizline.exe was not found in the selected directory.");
        using var assembly = File.OpenRead(Path.Combine(root, "GameAssembly.dll"));
        if (!Convert.ToHexString(SHA256.HashData(assembly)).Equals(SupportedGameHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The game build differs from this validated mod adapter.");
        var start = new ProcessStartInfo(executable) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true };
        NormalizeChildEnvironment(start);
        return start;
    }
    private static void NormalizeChildEnvironment(ProcessStartInfo start)
    {
        // Doorstop sets these during its own initialization. Steam can inherit them when
        // RestartAppIfNecessary starts the client. Removing them here affects only this child.
        foreach (var key in InheritedKeys) start.Environment.Remove(key);
        start.Environment["SteamAppId"] = "2272590";
        start.Environment["SteamGameId"] = "2272590";
    }
    internal static int SelfTest(LaunchConfiguration config)
    {
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); Console.WriteLine("PASS " + name); }
        var before = InheritedKeys.ToDictionary(x => x, Environment.GetEnvironmentVariable);
        var start = Prepare(config);
        Check(Path.GetFileName(start.FileName) == "Rizline.exe" && Path.IsPathFullyQualified(start.WorkingDirectory), "Exact supported game image and absolute working directory");
        Check(!start.UseShellExecute && start.ArgumentList.Count == 0, "Launch uses the executable directly without a shell or injected commands");
        var child = new ProcessStartInfo("fixture");
        foreach (var key in InheritedKeys) child.Environment[key] = "stale inherited fixture";
        child.Environment["UNCHANGED_FIXTURE"] = "keep";
        NormalizeChildEnvironment(child);
        Check(InheritedKeys.All(x => !child.Environment.ContainsKey(x)), "All four inherited initialization keys are removed from the child");
        Check(child.Environment["UNCHANGED_FIXTURE"] == "keep", "Unrelated child environment remains unchanged");
        Check(child.Environment["SteamAppId"] == "2272590" && child.Environment["SteamGameId"] == "2272590", "Child Steam application identity matches the selected installed game");
        Check(before.All(x => Environment.GetEnvironmentVariable(x.Key) == x.Value), "Parent environment is never changed");
        var refused = 0;
        foreach (var bad in new[] { config with { SchemaVersion = 2 }, config with { GameDirectory = "relative" }, config with { GameDirectory = "" } })
            try { Prepare(bad); } catch (InvalidDataException) { refused++; }
        Check(refused == 3, "Unknown schema and relative or empty roots are refused before starting a process");
        child.Environment["SteamAppId"] = "other";
        Check(start.Environment["SteamAppId"] == "2272590", "Prepared child environments are isolated");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.json"), JsonSerializer.Serialize(new { passed = checks.Count, total = 8, processStarted = false, checks }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Game launcher checks: {checks.Count}/8 passed."); return checks.Count == 8 ? 0 : 1;
    }
}
