using System.Text.Json;
using RizToolbox.Sdk;

namespace RizToolbox.Loader;

internal enum RuntimeMode { Standard, Developer }

/// <summary>A startup value. Reading settings or a command never changes the active checking contract.</summary>
internal sealed class RuntimeConfiguration
{
    internal const string InvalidModeMessage = "运行模式无效；已关闭可选写入功能。";
    private const int MaximumConfigurationBytes = 16 * 1024;

    private RuntimeConfiguration(RuntimeMode mode, LogLevel minimumLogLevel, bool fullAuditEachTick,
        bool enableProofPlugins, string? sourcePath, OperationError? error = null, string? detail = null)
    {
        Mode = mode; MinimumLogLevel = minimumLogLevel; FullAuditEachTick = fullAuditEachTick;
        EnableProofPlugins = enableProofPlugins; SourcePath = sourcePath;
        ConfigurationError = error; ConfigurationDetail = detail;
    }

    internal RuntimeMode Mode { get; }
    internal LogLevel MinimumLogLevel { get; }
    internal bool FullAuditEachTick { get; }
    internal bool EnableProofPlugins { get; }
    internal bool AllowOptionalWrites => ConfigurationError is null;
    internal bool IsValid => ConfigurationError is null;
    internal string? SourcePath { get; }
    internal OperationError? ConfigurationError { get; }
    internal string? ConfigurationDetail { get; }
    internal string AssuranceBoundary => FullAuditEachTick
        ? "Full installation validation at startup and a fresh full audit for each accepted host tick; no audit grants a later operation permission. Native channel and operation guards remain required."
        : "Full installation validation at startup; accepted host ticks check the current native channel. Arbitrary third-party memory changes are not continuously audited. Native channel and operation guards remain required.";

    internal static RuntimeConfiguration Load(string path)
    {
        try
        {
            // Missing configuration selects Standard; a present but unreadable file is unsafe.
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > MaximumConfigurationBytes) return Invalid(path, "Runtime configuration exceeds 16 KiB.");
            using var reader = new StreamReader(input);
            return Parse(reader.ReadToEnd(), path);
        }
        catch (FileNotFoundException) { return Standard(path); }
        catch (DirectoryNotFoundException) { return Standard(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return Invalid(path, "Cannot read runtime configuration: " + ex.Message); }
    }

    internal static RuntimeConfiguration Parse(string json, string? sourcePath = null)
    {
        try
        {
            if (json.Length > MaximumConfigurationBytes) return Invalid(sourcePath, "Runtime configuration exceeds 16 KiB.");
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return Invalid(sourcePath, "Runtime configuration must be an object.");
            var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name is not ("schemaVersion" or "mode" or "logLevel" or "fullAuditEachTick" or "enableProofPlugins"))
                    return Invalid(sourcePath, "Unknown runtime configuration property: " + property.Name);
                if (!values.TryAdd(property.Name, property.Value)) return Invalid(sourcePath, "Duplicate runtime configuration property: " + property.Name);
            }
            if (values.TryGetValue("schemaVersion", out var version) &&
                (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schema) || schema != 1))
                return Invalid(sourcePath, "Runtime configuration schemaVersion must be 1.");

            var mode = RuntimeMode.Standard;
            if (values.TryGetValue("mode", out var configuredMode))
            {
                if (configuredMode.ValueKind != JsonValueKind.String) return Invalid(sourcePath, "Runtime mode must be Standard or Developer.");
                var name = configuredMode.GetString();
                if (string.Equals(name, "Developer", StringComparison.OrdinalIgnoreCase)) mode = RuntimeMode.Developer;
                else if (!string.Equals(name, "Standard", StringComparison.OrdinalIgnoreCase)) return Invalid(sourcePath, "Runtime mode must be Standard or Developer.");
            }
            var level = LogLevel.Info;
            if (values.TryGetValue("logLevel", out var configuredLevel))
            {
                if (configuredLevel.ValueKind != JsonValueKind.String ||
                    !Enum.TryParse(configuredLevel.GetString(), ignoreCase: true, out level) ||
                    !Enum.IsDefined(typeof(LogLevel), level) ||
                    !string.Equals(configuredLevel.GetString(), level.ToString(), StringComparison.OrdinalIgnoreCase))
                    return Invalid(sourcePath, "Runtime logLevel must be Debug, Info, Warning or Error.");
            }
            bool ReadFlag(string key)
            {
                if (!values.TryGetValue(key, out var value)) return false;
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new FormatException(key + " must be a boolean.");
                return value.GetBoolean();
            }
            var fullAudit = ReadFlag("fullAuditEachTick"); var proof = ReadFlag("enableProofPlugins");
            if (mode != RuntimeMode.Developer && (level == LogLevel.Debug || fullAudit || proof))
                return Invalid(sourcePath, "Debug logging, full tick audits and proof plugins require explicit Developer mode.");
            return new(mode, level, fullAudit, proof, sourcePath);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        { return Invalid(sourcePath, "Cannot parse runtime configuration: " + ex.Message); }
    }

    internal RuntimeConfigurationStatus CaptureStatus() => new(Mode.ToString(), MinimumLogLevel.ToString(),
        FullAuditEachTick, EnableProofPlugins, AllowOptionalWrites, IsValid, true, SourcePath,
        ConfigurationError, ConfigurationDetail, AssuranceBoundary);

    private static RuntimeConfiguration Standard(string? path) => new(RuntimeMode.Standard, LogLevel.Info, false, false, path);
    private static RuntimeConfiguration Invalid(string? path, string detail)
        => new(RuntimeMode.Standard, LogLevel.Info, false, false, path,
            new OperationError(ErrorCode.InvalidArgument, InvalidModeMessage), detail);
}

internal sealed record RuntimeConfigurationStatus(string Mode, string MinimumLogLevel, bool FullAuditEachTick,
    bool EnableProofPlugins, bool AllowOptionalWrites, bool IsValid, bool FixedAtStartup, string? SourcePath,
    OperationError? ConfigurationError, string? ConfigurationDetail, string AssuranceBoundary);
