"""Freeze/execute the offline owned native playback producer; no game modifications.
Program registration is a named snapshot-only overlay owned by this runner.
All failures and source/output evidence remain in the fresh D: directory.
"""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[3]
OWNED = [
    "src/RizToolbox.Adapter/Gameplay/NativePlaybackActualSource.cs",
    "src/RizToolbox.Adapter/Gameplay/NativePlaybackOwnedCallTransport.cs",
    "src/RizToolbox.Adapter/Gameplay/NativePlaybackOccurrenceRateProvider.cs",
    "tests/RizToolbox.Gameplay.Tests/FakeNative.PlaybackOccurrence.cs",
    "tests/RizToolbox.Gameplay.Tests/NativePlaybackOccurrenceProviderChecks.cs",
    "native/RizToolbox.PlaybackOwnedFixture/PlaybackOwnedFixture.cpp",
    "scripts/Build-PlaybackOwnedFixture.ps1",
    "docs/validation/gameplay-occurrence-provider-20261030/run-checks.py",
]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def inventory(root):
    return [dict(path=p.relative_to(root).as_posix(), bytes=p.stat().st_size, sha256=sha(p))
            for p in sorted(root.rglob("*")) if p.is_file() and not {"bin", "obj"}.intersection(p.relative_to(root).parts)]


def run(args, name, command, env=None, cwd=None):
    with (args.output / (name + ".stdout.log")).open("wb") as stdout, (args.output / (name + ".stderr.log")).open("wb") as stderr:
        result = subprocess.run(command, cwd=cwd, env=env, stdout=stdout, stderr=stderr, check=False)
    return dict(command=command, exitCode=result.returncode, stdout=name + ".stdout.log", stderr=name + ".stderr.log")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--base", type=Path, default=Path("D:/riz-build-cache-next/foundation-offline-rate-core-r1-20261030/source-snapshot"))
    args = parser.parse_args()
    args.output = args.output.resolve()
    if args.output.exists() or args.output.drive.lower() != "d:":
        raise SystemExit("Output must be a fresh nonexistent D: directory; failed runs are preserved.")
    if not args.base.is_dir():
        raise SystemExit("The named frozen base is missing.")
    args.output.mkdir(parents=True)
    snapshot = args.output / "source-snapshot"
    shutil.copytree(args.base, snapshot, ignore=shutil.ignore_patterns("bin", "obj"))
    overlays = []
    for relative in OWNED:
        live, target = ROOT / relative, snapshot / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(live, target)
        overlays.append(dict(path=relative, sha256=sha(live), bytes=live.stat().st_size))
    write(args.output / "live-owned-before.json", overlays)
    program = snapshot / "tests/RizToolbox.Gameplay.Tests/Program.cs"
    source = program.read_text(encoding="utf-8-sig")
    registration = ".Concat(NativePlaybackOccurrenceProviderChecks.All)"
    if registration not in source:
        old = ".Concat(NativePlaybackProviderChecks.All).ToArray();"
        if source.count(old) != 1:
            raise RuntimeError("The frozen Program registration point changed.")
        source = source.replace(old, ".Concat(NativePlaybackProviderChecks.All)" + registration + ".ToArray();")
        program.write_text(source, encoding="utf-8", newline="\n")
    write(args.output / "snapshot-registration.json", dict(path="tests/RizToolbox.Gameplay.Tests/Program.cs", sha256=sha(program),
          scope="snapshot-only registration of independent owned native producer checks"))
    before = inventory(snapshot)
    write(args.output / "source-before.json", before)
    with zipfile.ZipFile(args.output / "source-snapshot.zip", "x", zipfile.ZIP_DEFLATED) as archive:
        for row in before:
            archive.write(snapshot / row["path"], row["path"])
    (args.output / "NuGet.Config").write_text("<configuration><packageSources><clear /></packageSources></configuration>\n", encoding="utf-8")
    result = dict(kind="controlled-native-owned-playback", flavor=None, observedAt=dt.datetime.now(dt.timezone.utc).isoformat(),
                  passed=False, productionAdmission=False, productionReturnAuthority=False, realStockHookCoverage=False,
                  nakedRuntimeWriterCoverage=False, gameExecuted=False, installed=False,
                  base=str(args.base), inputCount=len(before), sourceArchiveSha256=sha(args.output / "source-snapshot.zip"), stages=[])
    write(args.output / "test-result.json", result)
    try:
        native = args.output / "native"
        pwsh = shutil.which("pwsh")
        if pwsh is None:
            raise RuntimeError("The already installed pwsh executable is unavailable.")
        stage = run(args, "native-build", [pwsh, "-NoProfile", "-File",
                    str(snapshot / "scripts/Build-PlaybackOwnedFixture.ps1"), "-OutputDirectory", str(native)], cwd=str(snapshot))
        result["stages"].append(stage)
        if stage["exitCode"] != 0:
            return 1
        dll = native / "RizToolbox.PlaybackOwnedFixture.dll"
        result["nativeDllSha256"] = sha(dll)
        dotnet = "C:/Program Files/dotnet/dotnet.exe"
        project = snapshot / "tests/RizToolbox.Gameplay.Tests/RizToolbox.Gameplay.Tests.csproj"
        stage = run(args, "managed-build", [dotnet, "build", str(project), "-c", "Release", "-m:1", "--configfile",
                    str(args.output / "NuGet.Config"), "-p:UseSharedCompilation=false", "--nologo"], cwd=str(snapshot))
        result["stages"].append(stage)
        if stage["exitCode"] != 0:
            return 1
        env = os.environ.copy(); env["RIZ_PLAYBACK_OWNED_FIXTURE"] = str(dll)
        stage = run(args, "tests", [dotnet, str(project.parent / "bin/Release/net8.0/RizToolbox.Gameplay.Tests.dll")], env=env, cwd=str(snapshot))
        result["stages"].append(stage)
        text = (args.output / "tests.stdout.log").read_text(encoding="utf-8")
        match = re.search(r"(\d+)/(\d+) gameplay primitive checks passed", text)
        result["checks"] = dict(passed=int(match[1]), total=int(match[2])) if match else None
        result["passed"] = stage["exitCode"] == 0 and match is not None and match[1] == match[2]
        return 0 if result["passed"] else 1
    except Exception as error:
        result["failure"] = repr(error)
        raise
    finally:
        after = inventory(snapshot); write(args.output / "source-after.json", after)
        owned_after = [dict(path=row["path"], sha256=sha(ROOT / row["path"]), bytes=(ROOT / row["path"]).stat().st_size) for row in overlays]
        write(args.output / "live-owned-after.json", owned_after)
        result["frozenInputsUnchanged"] = before == after
        result["liveOwnedUnchanged"] = overlays == owned_after
        result["passed"] = result["passed"] and result["frozenInputsUnchanged"] and result["liveOwnedUnchanged"]
        result["completedAt"] = dt.datetime.now(dt.timezone.utc).isoformat()
        write(args.output / "test-result.json", result)
        print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    raise SystemExit(main())
