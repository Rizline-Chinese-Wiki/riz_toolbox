"""Freeze the 766 runtime session; never alters game state or configuration."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
GAME = Path(r"D:\SteamLibrary\steamapps\common\Rizline")
DATA = GAME / "BepInEx" / "RizToolbox"
SESSION = "20261021T044121502Z-991c2e0274194ba9838a0e3aa1f1b476"
START = "2026-10-21T04:41:21"
CAPTURE = datetime.now(timezone.utc)
OUT = ROOT / ("evidence-snapshot-" + CAPTURE.strftime("%Y%m%dT%H%M%SZ"))
OUT.mkdir(exist_ok=False)

manifest = []
sources = {
    "toolbox.log": DATA / "toolbox.log",
    "status.json": DATA / "status.json",
    "session.json": DATA / "diagnostics" / SESSION / "session.json",
    "events-000001.jsonl": DATA / "diagnostics" / SESSION / "events-000001.jsonl",
    "bepinex-log.txt": GAME / "BepInEx" / "LogOutput.log",
    "successful-launcher-observation.json": REPO / "artifacts" / "game-launcher" / "last-launch.json",
}
for name, source in sources.items():
    if not source.exists():
        manifest.append({"name": name, "source": str(source), "missing": True})
        continue
    before = source.stat()
    data = source.read_bytes()
    after = source.stat()
    (OUT / name).write_bytes(data)
    manifest.append({"name": name, "source": str(source), "bytes": len(data),
        "sha256": hashlib.sha256(data).hexdigest(),
        "readCompletedUtc": datetime.now(timezone.utc).isoformat(),
        "sourceSizeBefore": before.st_size, "sourceSizeAfter": after.st_size,
        "sourceLastWriteUtc": datetime.fromtimestamp(after.st_mtime, timezone.utc).isoformat(),
        "sourceChangedDuringRead": before.st_size != after.st_size or before.st_mtime_ns != after.st_mtime_ns})

all_lines = (OUT / "toolbox.log").read_text(encoding="utf-8-sig").splitlines()
lines = [(i, line) for i, line in enumerate(all_lines, 1)
         if re.match(r"\d{4}-\d\d-\d\dT", line) and line[:19] >= START]
(OUT / "current-session.log").write_text("\n".join(line for _, line in lines) + "\n", encoding="utf-8")

def events(marker):
    found = []
    for number, line in lines:
        if marker not in line:
            continue
        tail = line.split(marker, 1)[1].strip()
        try:
            payload = json.loads(tail)
        except ValueError:
            payload = tail
        found.append({"toolboxLine": number, "timestamp": line.split(" ", 1)[0], "value": payload})
    return found

lifecycle = events("[example.panel] ObservationProbe LIFECYCLE ")
controls = events("ModuleProbe CONTROL RECEIPT ")
clocks = events("ModuleProbe GAME CLOCK ")
judgements = events("ModuleProbe JUDGEMENT ")
statuses = events("ModuleProbe JUDGEMENT STATUS ")
material = events("MaterialProbe ")
tmp_discovery = [{"toolboxLine": n, "text": line} for n, line in lines
                 if "Real game resource discovered: game.ui/tmp-text/" in line]
tmp_baseline = [{"toolboxLine": n, "text": line} for n, line in lines
                if "Game resource read-back verified: game.ui/tmp-text/" in line]
errors = [{"toolboxLine": n, "text": line} for n, line in lines
          if "[Error]" in line or "Exception" in line]
warnings = [{"toolboxLine": n, "text": line} for n, line in lines if "[Warning]" in line]
pause_clocks = [c for c in clocks if c["value"].get("Success") and c["value"]["Value"].get("GameTime", {}).get("Value") == 89.76773071289062]
pause_comparison = None
if len(pause_clocks) >= 2:
    first, second = pause_clocks[0], pause_clocks[1]
    a, b = first["value"]["Value"], second["value"]["Value"]
    pause_comparison = {"firstLine": first["toolboxLine"], "secondLine": second["toolboxLine"],
        "gameTimeDelta": b["GameTime"]["Value"] - a["GameTime"]["Value"],
        "gameRealTimeDelta": b["GameRealTime"]["Value"] - a["GameRealTime"]["Value"],
        "gameSyncedTimeDelta": b["GameSyncedTime"]["Value"] - a["GameSyncedTime"]["Value"],
        "audioDspDeltaSeconds": b["AudioDspTime"]["Value"] - a["AudioDspTime"]["Value"],
        "hostDeltaSeconds": b["HostMonotonicTime"]["Value"] - a["HostMonotonicTime"]["Value"],
        "secondsMappingVerified": a["SecondsMappingVerified"] or b["SecondsMappingVerified"]}

summary = {
    "capturedAtUtc": CAPTURE.isoformat(), "diagnosticSession": SESSION,
    "sessionStartedAtUtc": START + "Z", "sourceLogHasHistoricalSessions": True,
    "currentSessionLogLines": len(lines),
    "currentSessionFirstTimestamp": lines[0][1].split(" ")[0] if lines else None,
    "currentSessionLastTimestamp": lines[-1][1].split(" ")[0] if lines else None,
    "structuredEventsFileBytes": (OUT / "events-000001.jsonl").stat().st_size,
    "lifecycle": lifecycle, "controls": controls, "clockSamples": clocks,
    "pauseClockComparison": pause_comparison, "judgementStatusSamples": statuses,
    "loggedJudgementSamples": judgements,
    "judgementSampleCountIsNotTotal": True,
    "tmpDiscoveredCount": len(tmp_discovery), "tmpBaselineReadbackCount": len(tmp_baseline),
    "tmpDiscovery": tmp_discovery, "tmpBaselineReadback": tmp_baseline,
    "materialProbeEvents": material, "currentSessionErrors": errors, "currentSessionWarnings": warnings,
    "boundaries": [
        "The structured diagnostic JSONL can be buffered during gameplay. Use the final snapshot byte count and a per-session parse to establish availability.",
        "A successful runtime_invoke receipt is not proof of full result/submission behavior.",
        "Raw game time and judgement delta units are not calibrated seconds.",
        "TMP discovery/baseline getter readback does not prove TMP override/restoration or GPU/pixel output.",
        "Material probe released lease counts do not prove physical GPU resource destruction.",
        "Original launch-attempt.json and launcher-result.json outside this snapshot record the earlier Steam/login failure; they are preserved.",
    ]
}
(OUT / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
(OUT / "evidence-manifest.json").write_text(json.dumps({"capturedAtUtc": CAPTURE.isoformat(), "files": manifest}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
compact = {
    "path": str(OUT), "lines": len(lines), "lastTimestamp": summary["currentSessionLastTimestamp"],
    "controls": [(c["timestamp"], c["value"].get("action"), c["value"].get("result", {}).get("Success")) for c in controls],
    "lifecycle": [(e["timestamp"], e["value"]["Kind"], e["value"]["Snapshot"]["Revision"], e["value"]["Snapshot"].get("RunId"), e["value"].get("EndReason")) for e in lifecycle],
    "errors": len(errors), "warnings": len(warnings), "tmpDiscovery": len(tmp_discovery),
    "tmpBaseline": len(tmp_baseline), "pauseComparison": pause_comparison,
}
print(json.dumps(compact, ensure_ascii=False, indent=2))
