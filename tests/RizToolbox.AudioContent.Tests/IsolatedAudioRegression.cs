using System.Diagnostics;
using System.Reflection;

/// <summary>Fault cases deliberately retain production unknown owners. Run them
/// in a fresh test executable instead of raising quotas or clearing static owners.</summary>
internal static class IsolatedAudioRegression
{
    internal static void Run(string exactName)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test process path is unavailable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--isolated-case"); start.ArgumentList.Add(exactName);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Isolated test process did not start.");
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
            throw new TimeoutException("Isolated test process did not finish: " + exactName + "; pid=" + process.Id);
        var body = output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult();
        if (process.ExitCode != 0 || !body.Contains("PASS " + exactName, StringComparison.Ordinal))
            throw new InvalidOperationException("Isolated case failed (exit " + process.ExitCode + "): " + body);
    }
}
