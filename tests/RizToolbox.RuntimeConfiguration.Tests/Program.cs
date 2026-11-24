using System.Text.Json;
using RizToolbox.Loader;
using RizToolbox.Sdk;

var checks = new (string Name, Action Run)[]
{
    ("missing configuration selects Standard Info without creating a file", Missing),
    ("Developer permits only independently explicit diagnostics", Developer),
    ("invalid modes and settings enter safe observation with a structured reason", Invalid),
    ("loaded contract stays fixed after the disk configuration changes", FixedStartup),
    ("unreadable and oversized configuration cannot enable optional writes", Unreadable),
    ("status reports the active mode and changed audit assurance", Status)
};
var failures = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine("PASS " + check.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + check.Name + ": " + ex); }
}
Console.WriteLine($"{checks.Length - failures}/{checks.Length} runtime configuration checks passed.");
return failures == 0 ? 0 : 1;

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Standard(RuntimeConfiguration value)
{
    Check(value.IsValid && value.Mode == RuntimeMode.Standard && value.MinimumLogLevel == LogLevel.Info &&
        value.AllowOptionalWrites && !value.FullAuditEachTick && !value.EnableProofPlugins, "Standard defaults changed.");
}
static void Missing()
{
    using var fixture = new Files();
    var path = fixture.Path("missing.json");
    Standard(RuntimeConfiguration.Load(path));
    Standard(RuntimeConfiguration.Parse("{}"));
    Standard(RuntimeConfiguration.Parse("{\"mode\":\"Standard\"}"));
    Check(!File.Exists(path), "Reading defaults created or rewrote user configuration.");
}
static void Developer()
{
    var minimal = RuntimeConfiguration.Parse("{\"mode\":\"Developer\"}");
    Check(minimal.IsValid && minimal.Mode == RuntimeMode.Developer && minimal.MinimumLogLevel == LogLevel.Info &&
        !minimal.FullAuditEachTick && !minimal.EnableProofPlugins && minimal.AllowOptionalWrites, "Developer mode silently enabled diagnostics.");
    var full = RuntimeConfiguration.Parse("{\"schemaVersion\":1,\"mode\":\"Developer\",\"logLevel\":\"Debug\",\"fullAuditEachTick\":true,\"enableProofPlugins\":true}");
    Check(full.IsValid && full.MinimumLogLevel == LogLevel.Debug && full.FullAuditEachTick && full.EnableProofPlugins, "Explicit Developer diagnostics were not enabled.");
    var audit = RuntimeConfiguration.Parse("{\"mode\":\"Developer\",\"fullAuditEachTick\":true}");
    Check(audit.IsValid && audit.FullAuditEachTick && !audit.EnableProofPlugins && audit.MinimumLogLevel == LogLevel.Info, "Full audit implicitly enabled proof or Debug.");
}
static void Invalid()
{
    var cases = new[]
    {
        "{\"mode\":\"developr\"}", "{\"mode\":1}", "{\"mode\":null}", "{\"mode\":\"\"}",
        "{\"schemaVersion\":2}", "{\"schemaVersion\":\"1\"}", "{\"schemaVersion\":1.5}",
        "{\"mode\":\"Standard\",\"mode\":\"Developer\"}", "{\"Mode\":\"Developer\"}",
        "{\"fullAuditEachTick\":true}", "{\"enableProofPlugins\":true}", "{\"logLevel\":\"Debug\"}",
        "{\"mode\":\"Developer\",\"logLevel\":\"0\"}", "{\"mode\":\"Developer\",\"logLevel\":\"999\"}",
        "{\"mode\":\"Developer\",\"logLevel\":\"Debug, Info\"}", "{\"mode\":\"Developer\",\"logLevel\":\"Trace\"}",
        "{\"mode\":\"Developer\",\"fullAuditEachTick\":\"true\"}", "{\"mode\":\"Developer\",\"enableProofPlugins\":null}",
        "{\"mode\":\"Developer\",\"unknownSetting\":true}", "null", "[]", "{"
    };
    foreach (var json in cases)
    {
        var value = RuntimeConfiguration.Parse(json, "user-runtime.json");
        Check(!value.IsValid && !value.AllowOptionalWrites && value.Mode == RuntimeMode.Standard && value.MinimumLogLevel == LogLevel.Info &&
            !value.FullAuditEachTick && !value.EnableProofPlugins, "Unsafe configuration did not close optional work: " + json);
        Check(value.ConfigurationError is { Code: ErrorCode.InvalidArgument, Message: RuntimeConfiguration.InvalidModeMessage } &&
            !string.IsNullOrWhiteSpace(value.ConfigurationDetail) && value.SourcePath == "user-runtime.json", "Safe fallback omitted its structured reason: " + json);
    }
}
static void FixedStartup()
{
    using var fixture = new Files();
    var path = fixture.Path("runtime.json");
    const string developer = "{\"mode\":\"Developer\",\"fullAuditEachTick\":true,\"enableProofPlugins\":true}";
    File.WriteAllText(path, developer);
    var active = RuntimeConfiguration.Load(path);
    Check(File.ReadAllText(path) == developer, "Loading changed user bytes.");
    File.WriteAllText(path, "{\"mode\":\"Standard\"}");
    Check(active.Mode == RuntimeMode.Developer && active.FullAuditEachTick && active.EnableProofPlugins, "A disk change mutated the current contract.");
    Standard(RuntimeConfiguration.Load(path));
    Check(active.FullAuditEachTick, "A new startup load revoked the existing contract.");
}
static void Unreadable()
{
    using var fixture = new Files();
    var path = fixture.Path("locked.json");
    File.WriteAllText(path, "{\"mode\":\"Developer\",\"enableProofPlugins\":true}");
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        var denied = RuntimeConfiguration.Load(path);
        Check(!denied.IsValid && !denied.AllowOptionalWrites && !denied.EnableProofPlugins, "Unreadable configuration selected valid defaults.");
    }
    var oversized = fixture.Path("oversized.json");
    File.WriteAllText(oversized, new string(' ', 17 * 1024) + "{}");
    Check(!RuntimeConfiguration.Load(oversized).AllowOptionalWrites, "Oversized file enabled optional writes.");
    Check(!RuntimeConfiguration.Parse(new string(' ', 17 * 1024) + "{}").AllowOptionalWrites, "Oversized input enabled optional writes.");
}
static void Status()
{
    using var standard = JsonDocument.Parse(JsonSerializer.Serialize(RuntimeConfiguration.Parse("{}").CaptureStatus()));
    Check(standard.RootElement.GetProperty("Mode").GetString() == "Standard" && standard.RootElement.GetProperty("MinimumLogLevel").GetString() == "Info" &&
        standard.RootElement.GetProperty("FixedAtStartup").GetBoolean(), "Status omitted the actual startup contract.");
    Check(standard.RootElement.GetProperty("AssuranceBoundary").GetString()!.Contains("not continuously audited"), "Standard implies continuous third-party memory audit.");
    var audit = RuntimeConfiguration.Parse("{\"mode\":\"Developer\",\"fullAuditEachTick\":true}").CaptureStatus();
    Check(audit.FullAuditEachTick && audit.AssuranceBoundary.Contains("no audit grants a later operation permission"), "Developer lent audit authority to future operations.");
    var denied = RuntimeConfiguration.Parse("{\"mode\":\"bad\"}").CaptureStatus();
    Check(!denied.IsValid && !denied.AllowOptionalWrites && denied.ConfigurationError?.Code == ErrorCode.InvalidArgument, "Status hid invalid configuration.");
}

sealed class Files : IDisposable
{
    private readonly string _root = System.IO.Path.Combine(AppContext.BaseDirectory, "runtime-configuration-" + Guid.NewGuid().ToString("N"));
    internal Files() => Directory.CreateDirectory(_root);
    internal string Path(string name) => System.IO.Path.Combine(_root, name);
    public void Dispose()
    {
        foreach (var path in Directory.EnumerateFiles(_root)) File.Delete(path);
        Directory.Delete(_root);
    }
}
