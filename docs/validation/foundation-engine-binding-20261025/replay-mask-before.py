"""Build an isolated old-ledger/new-test replay; never rebuild shared projects.

The root builder may run this after the shared Release DLLs are stable. A
compiler/loader/process failure is not a reproduced red assertion. Only the
selected current Foreign case's own Check failure establishes redObserved.
All generated sources, logs and a result are retained under mask-before-replay.
"""
import argparse
import hashlib
import json
import re
import shutil
import subprocess
import sys
import uuid
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from xml.sax.saxutils import escape


batch = Path(__file__).resolve().parent
repo = batch.parents[2]
ledger_path = "src/RizToolbox.Adapter/AudioContent/NativeEngineCoroutineIdentityLedger.cs"
test_path = "tests/RizToolbox.AudioContent.Tests/NativeEngineCoroutineIdentityTests.cs"
replacement = "BeforeMaskIdentityLedger"
case_fragment = "nearest foreign Start"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def renamed(text):
    assert "BeforeMaskIdentityLedger" not in text
    result, count = re.subn(r"\bNativeEngineCoroutineIdentityLedger\b", replacement, text)
    assert count > 1
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=shutil.which("dotnet"))
    args = parser.parse_args()
    if not args.dotnet:
        parser.error("dotnet was not found; supply --dotnet with the SDK executable")
    run = batch / "mask-before-replay" / (
        datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex[:8])
    run.mkdir(parents=True, exist_ok=False)
    result = {
        "kind": "engine-identity-before-mask-isolated-replay",
        "recordedAtUtc": datetime.now(timezone.utc).isoformat(),
        "redObserved": False,
        "sharedProjectsRebuilt": False,
        "gameExecuted": False,
        "installed": False,
        "caseFragment": case_fragment,
        "outputDirectory": str(run),
        "inputs": [],
    }
    exit_code = 1
    try:
        source_zip = batch / "before-mask-fix/source-inputs.zip"
        manifest = json.loads((batch / "before-mask-fix/build-inputs-before.json").read_text(encoding="utf-8-sig"))
        source_row = next(row for row in manifest["files"] if row["path"] == ledger_path)
        with zipfile.ZipFile(source_zip) as archive:
            old_bytes = archive.read(ledger_path)
        assert hashlib.sha256(old_bytes).hexdigest() == source_row["sha256"]
        old = old_bytes.decode("utf-8-sig")
        # This replay must exercise the archived pre-fix responsibility filter.
        assert "nearest is null || nearest.Relevant" in old
        assert "var relevant = IsRelevantFrame(frame);" not in old
        current_test = repo / test_path
        test_text = current_test.read_text(encoding="utf-8-sig")
        assert "masked.Relevant&&masked.NodeCall is null&&masked.Identity is null" in test_text
        result["sourceArchiveSha256"] = sha(source_zip)
        result["archivedLedgerSha256"] = source_row["sha256"]
        result["currentTestSha256"] = sha(current_test)
        (run / "BeforeMaskIdentityLedger.cs").write_text(renamed(old), encoding="utf-8")
        (run / "NativeEngineCoroutineIdentityTests.cs").write_text(renamed(test_text), encoding="utf-8")

        # The complete unchanged test source mentions these two fixtures in
        # other, unexecuted cases. Include their existing sources for compile
        # resolution, without running their Cases or mocking those APIs.
        for name in ("NativeLocalInitializationCoroutineRegression.cs", "NativeLocalTerminalCoroutineRegression.cs"):
            original = repo / "tests/RizToolbox.AudioContent.Tests" / name
            (run / name).write_bytes(original.read_bytes())
            result["inputs"].append({"path": str(original), "sha256": sha(original), "role": "compile-only-fixture"})
        (run / "FixtureAssert.cs").write_text('''static class Assert
{
    public static void Check(bool value) { if (!value) throw new Exception("Compile fixture assertion failed."); }
    public static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("Compile fixture equality failed."); }
    public static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Compile fixture expected exception."); }
}
''', encoding="utf-8")
        (run / "Program.cs").write_text('''using System.Text.Json;
var rows = NativeEngineCoroutineIdentityTests.Cases.Where(row => row.Name.Contains("nearest foreign Start", StringComparison.Ordinal)).ToArray();
if (rows.Length != 1) { Console.Error.WriteLine("Expected exactly one selected Foreign case."); return 2; }
try
{
    rows[0].Test();
    Console.Error.WriteLine("The archived implementation unexpectedly passed the strengthened case.");
    return 4;
}
catch (InvalidOperationException error) when (
    error.Message == "Engine tracker identity check failed." &&
    error.StackTrace is { } stack &&
    stack.Contains("NativeEngineCoroutineIdentityTests.Check(", StringComparison.Ordinal) &&
    stack.Contains("NativeEngineCoroutineIdentityTests.Foreign(", StringComparison.Ordinal))
{
    Console.WriteLine(JsonSerializer.Serialize(new { redObserved = true, caseName = rows[0].Name,
        exceptionType = error.GetType().FullName, error.Message, stackTrace = error.StackTrace,
        sharedProjectsRebuilt = false, gameExecuted = false, installed = false }));
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 3; }
''', encoding="utf-8")
        references = []
        for component in ("Adapter", "Core", "Runtime", "Sdk"):
            path = repo / f"src/RizToolbox.{component}/bin/Release/net6.0/RizToolbox.{component}.dll"
            assert path.is_file(), path
            result["inputs"].append({"path": str(path), "sha256": sha(path), "role": "direct-release-dll"})
            references.append(f'<Reference Include="RizToolbox.{component}"><HintPath>{escape(str(path))}</HintPath><Private>true</Private></Reference>')
        (run / "Replay.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType>
    <AssemblyName>RizToolbox.AudioContent.Tests</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <EnableNETAnalyzers>false</EnableNETAnalyzers><Optimize>false</Optimize>
    <DebugType>portable</DebugType><NoWarn>$(NoWarn);CS0436</NoWarn>
  </PropertyGroup>
  <ItemGroup>''' + "\n".join(references) + '''</ItemGroup>
</Project>
''', encoding="utf-8")
        result["generatedSources"] = [
            {"path": path.name, "sha256": sha(path)}
            for path in sorted(run.iterdir()) if path.suffix in (".cs", ".csproj")]
        build_command = [args.dotnet, "build", str(run / "Replay.csproj"), "-c", "Release", "--nologo"]
        result["buildCommand"] = build_command
        built = subprocess.run(build_command, cwd=run, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=300)
        (run / "build.stdout.log").write_text(built.stdout, encoding="utf-8")
        (run / "build.stderr.log").write_text(built.stderr, encoding="utf-8")
        result["buildExitCode"] = built.returncode
        if built.returncode != 0:
            raise RuntimeError("Isolated replay build failed; this is not red assertion evidence.")
        command = [args.dotnet, str(run / "bin/Release/net8.0/RizToolbox.AudioContent.Tests.dll")]
        result["runCommand"] = command
        executed = subprocess.run(command, cwd=run, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60)
        (run / "run.stdout.log").write_text(executed.stdout, encoding="utf-8")
        (run / "run.stderr.log").write_text(executed.stderr, encoding="utf-8")
        result["runExitCode"] = executed.returncode
        if executed.returncode != 0:
            raise RuntimeError("Replay process did not report the specific test assertion; no red proof.")
        lines = [line for line in executed.stdout.splitlines() if line.startswith("{")]
        assert len(lines) == 1
        assertion = json.loads(lines[0])
        assert assertion["redObserved"] is True and case_fragment in assertion["caseName"]
        assert assertion["exceptionType"] == "System.InvalidOperationException"
        assert assertion["Message"] == "Engine tracker identity check failed."
        for row in result["inputs"]:
            assert sha(Path(row["path"])) == row["sha256"], row["path"]
        result["assertion"] = assertion
        result["allInputHashesUnchanged"] = True
        result["redObserved"] = True
        exit_code = 0
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
    result["logHashes"] = {
        path.name: sha(path) for path in sorted(run.glob("*.log"))}
    result["exitCode"] = exit_code
    (run / "result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"redObserved": result["redObserved"], "exitCode": exit_code, "result": str(run / "result.json")}, ensure_ascii=False))
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
