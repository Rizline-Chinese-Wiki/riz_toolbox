"""Root-run integration preflight for the exact built, owned process only.

This records a preflight, not production admission or the complete fault matrix.
It never configures hooks or loads native dependencies in the parent process.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--build", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--mode", choices=("native", "exceptions", "normal", "detach", "original-seh", "original-cpp"), default="native")
    args = parser.parse_args()
    build, output = args.build.resolve(), args.output.resolve()
    if not args.build.is_absolute() or not args.output.is_absolute() or output.exists():
        raise ValueError("Use absolute paths and preserve earlier preflight output.")
    manifest_path = build / "publication-build-manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    if manifest["kind"] != "engine-publication-build":
        raise ValueError("The exact owned publication build is required.")
    selected = {Path(item["path"]).name: item for item in manifest["files"] if item["profile"] in ("shared", "owned")}
    names = ("EnginePublication.Host.exe", "RizEngineOwnedTargets.dll", "RizToolbox.EngineNodeKernel.dll", "RizToolbox.EngineBootstrap.dll")
    if set(selected) != set(names):
        raise ValueError("The four exact owned binaries are required.")
    for item in selected.values():
        if digest(Path(item["path"])) != item["sha256"]:
            raise ValueError("A built owned binary changed.")
    output.mkdir(parents=True)
    inputs = []
    for name in names:
        shutil.copy2(selected[name]["path"], output / name)
        inputs.append({"path": str(output / name), "sha256": digest(output / name)})
    command = [str(output / names[0]), "--output", str(output / "case.json")]
    if args.mode == "exceptions":
        command.append("--exceptions")
    elif args.mode != "native":
        repo = Path(__file__).resolve().parents[3]
        managed = repo / "tests/RizToolbox.Runtime.Tests/bin/Release/net8.0"
        runtime = managed / "RizToolbox.Runtime.Tests.runtimeconfig.json"
        assembly = managed / "RizToolbox.Runtime.Tests.dll"
        hostfxr = Path("C:/Program Files/dotnet/host/fxr/8.0.24/hostfxr.dll")
        for path in (runtime, assembly, hostfxr):
            if not path.is_file():
                raise ValueError("The exact .NET 8 host/runtime inputs are missing.")
        managed_args = {"mode": args.mode, "output": str(output / "managed-result.json")}
        for role, name in zip(("executable", "target", "kernel", "bootstrap"), names):
            managed_args[role + "Path"] = str(output / name)
            managed_args[role + "Sha256"] = digest(output / name)
        config = output / "managed-args.json"
        config.write_text(json.dumps(managed_args, indent=2), encoding="utf-8")
        for flag, path in (("--runtime-config", runtime), ("--managed-assembly", assembly), ("--hostfxr", hostfxr), ("--managed-args", config)):
            command.extend((flag, str(path)))
        for path in sorted(set(managed.glob("*.dll")) | {runtime, managed / "RizToolbox.Runtime.Tests.deps.json", hostfxr, config}):
            inputs.append({"path": str(path), "sha256": digest(path)})
    result = {"kind": "owned-engine-publication-preflight", "mode": args.mode, "passed": False,
              "buildManifest": str(manifest_path), "buildManifestSha256": digest(manifest_path),
              "command": command, "inputs": inputs, "gameExecuted": False, "installed": False,
              "productionAdmissionProved": False, "completeFaultMatrix": False}
    try:
        process = subprocess.run(command, cwd=output, capture_output=True, timeout=40, creationflags=subprocess.CREATE_NO_WINDOW)
        (output / "stdout.log").write_bytes(process.stdout)
        (output / "stderr.log").write_bytes(process.stderr)
        result["actualExitCode"] = process.returncode
        report = json.loads((output / "case.json").read_text(encoding="utf-8-sig"))
        result["actualProcessId"] = report["actualProcessId"]
        result["hostChecks"] = report["checks"]
        result["rowCount"] = report["finalSnapshot"]["rowCount"]
        result["hostPassed"] = report["passed"]
        result["hostError"] = report["error"]
        result["passed"] = process.returncode == 0 and report["passed"] and report["finished"]
        if args.mode not in ("native", "exceptions"):
            managed_result = json.loads((output / "managed-result.json").read_text(encoding="utf-8-sig"))
            result["managedPassed"] = managed_result["passed"]
            result["managedProcessId"] = managed_result["actualProcessId"]
            result["passed"] &= managed_result["passed"] and report["managedResult"] == 0 and managed_result["actualProcessId"] == report["actualProcessId"]
    except Exception as error:
        result["error"] = repr(error)
    result["allInputsUnchanged"] = all(digest(Path(item["path"])) == item["sha256"] for item in inputs)
    result["passed"] &= result["allInputsUnchanged"]
    (output / "preflight-result.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({key: value for key, value in result.items() if key not in ("command", "inputs")}))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
