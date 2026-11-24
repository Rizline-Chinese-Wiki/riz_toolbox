"""Freeze the N1 dependency archive plus only N2-owned inputs, then build and run the real checks."""
from pathlib import Path
from datetime import datetime, timezone
from zipfile import ZipFile, ZIP_DEFLATED
import argparse
import hashlib
import json
import re
import shutil
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("--output", required=True)
parser.add_argument("--baseline", default=r"D:/riz-build-cache-next/foundation-structured-errors-core-r2-20261029/source-snapshot.zip")
args = parser.parse_args()
root = Path(__file__).resolve().parents[3]
output = Path(args.output).resolve()
if output.exists():
    raise SystemExit("A fresh, nonexistent output directory is required; prior results are never overwritten.")
output.mkdir(parents=True)
baseline = Path(args.baseline).resolve()
source = output / "source-snapshot"
source.mkdir()
digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
with ZipFile(baseline) as archive:
    for entry in archive.infolist():
        target = (source / entry.filename).resolve()
        if not target.is_relative_to(source):
            raise ValueError("Archive entry leaves the isolated source directory.")
    archive.extractall(source)

inputs = [root / "src/RizToolbox.Sdk/GameplayContracts.cs", root / "docs/Gameplay-primitives.md"]
for relative in ("src/RizToolbox.Adapter/Gameplay", "tests/RizToolbox.Gameplay.Tests"):
    inputs.extend(path for path in (root / relative).rglob("*") if path.is_file()
                  and path.suffix in (".cs", ".csproj") and not {"obj", "bin"}.intersection(path.parts))
overlay = []
for path in sorted(inputs):
    relative = path.relative_to(root)
    target = source / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(path, target)
    overlay.append({"path": relative.as_posix(), "sha256": digest(path), "bytes": path.stat().st_size})
files = sorted(path for path in source.rglob("*") if path.is_file())
before = [{"path": path.relative_to(source).as_posix(), "sha256": digest(path), "bytes": path.stat().st_size} for path in files]
(output / "source-before.json").write_text(json.dumps(before, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
with ZipFile(output / "source-snapshot.zip", "w", ZIP_DEFLATED) as archive:
    for path in files:
        archive.write(path, path.relative_to(source))
results = {"observedAtUtc": datetime.now(timezone.utc).isoformat(), "baseline": str(baseline), "baselineSha256": digest(baseline),
           "overlay": overlay, "sourceFiles": len(files), "sourceArchiveSha256": digest(output / "source-snapshot.zip"),
           "runtimeGameVerified": False, "nativeValueControlsSupported": False, "checks": []}
artifacts = output / "artifacts"
for project in ("Gameplay", "RunModification", "Sdk"):
    name = f"RizToolbox.{project}.Tests"
    command = ["dotnet", "build", str(source / "tests" / name / f"{name}.csproj"), "-c", "Release", "--artifacts-path", str(artifacts), "--verbosity", "minimal"]
    print("BUILD", name, flush=True)
    built = subprocess.run(command, cwd=source, capture_output=True, text=True, encoding="utf-8", errors="replace")
    (output / f"{project}-build.stdout.log").write_text(built.stdout, encoding="utf-8")
    (output / f"{project}-build.stderr.log").write_text(built.stderr, encoding="utf-8")
    row = {"project": name, "buildCommand": command, "buildExitCode": built.returncode}
    if built.returncode == 0:
        dll = artifacts / "bin" / name / "release" / f"{name}.dll"
        ran = subprocess.run(["dotnet", str(dll)], cwd=source, capture_output=True, text=True, encoding="utf-8", errors="replace")
        (output / f"{project}-tests.stdout.log").write_text(ran.stdout, encoding="utf-8")
        (output / f"{project}-tests.stderr.log").write_text(ran.stderr, encoding="utf-8")
        row.update(testCommand=["dotnet", str(dll)], testExitCode=ran.returncode, assemblySha256=digest(dll),
                   summaries=[line for line in ran.stdout.splitlines() if re.search(r"\d+/\d+", line)])
        print("TEST", name, ran.returncode, row["summaries"], flush=True)
    else:
        print(built.stdout[-6000:], built.stderr[-2000:], flush=True)
    results["checks"].append(row)
    (output / "test-result.json").write_text(json.dumps(results, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if built.returncode != 0 or row.get("testExitCode") != 0:
        break
after = [{"path": row["path"], "sha256": digest(source / row["path"]), "bytes": (source / row["path"]).stat().st_size} for row in before]
results["sourceUnchanged"] = before == after
results["overlayWorktreeUnchanged"] = all(digest(root / row["path"]) == row["sha256"] for row in overlay)
results["passed"] = len(results["checks"]) == 3 and all(row.get("buildExitCode") == 0 and row.get("testExitCode") == 0 for row in results["checks"]) and results["sourceUnchanged"] and results["overlayWorktreeUnchanged"]
(output / "source-after.json").write_text(json.dumps(after, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
(output / "test-result.json").write_text(json.dumps(results, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"passed": results["passed"], "checks": results["checks"], "sourceUnchanged": results["sourceUnchanged"], "overlayWorktreeUnchanged": results["overlayWorktreeUnchanged"]}, ensure_ascii=False), flush=True)
raise SystemExit(0 if results["passed"] else 1)
