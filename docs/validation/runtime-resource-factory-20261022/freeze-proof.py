"""Freeze and verify one actual game session; never launches or controls the game."""
import hashlib
import json
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent
session = Path(sys.argv[1]).resolve()
assert session.is_dir(), session
repo = root.parents[2]
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline")
build = repo / "docs/validation/foundation-resource-factory-20261021"

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

events = []
logs = sorted(session.glob("events-*.jsonl"))
assert logs, "No event segments"
for path in logs:
    rows = [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    events.extend(rows)
assert events, "Empty session"
assert [e["sequence"] for e in events] == list(range(1, len(events) + 1)), "Missing/duplicate event sequence"
assert len({e["sessionId"] for e in events}) == 1
assert events[0]["sessionId"] == session.name
assert not [e for e in events if e.get("level") == "Error"], "Session contains Error records"
assert not [e for e in events if "factory unavailable" in e.get("message", "") or "cleanup outcome unknown" in e.get("message", "")]

def one(stage):
    prefix = "LocalResourceReferenceProbe " + stage + " "
    matches = [(e, json.loads(e["message"][len(prefix):])) for e in events if e.get("message", "").startswith(prefix)]
    assert len(matches) == 1, (stage, len(matches))
    return matches[0]

started_event, started = one("factory-started")
observed_event, observed = one("factory-release-observed")
verified_event, verified = one("factory-verified")
assert started_event["sequence"] < observed_event["sequence"] < verified_event["sequence"]
assert started["resources"] == 2 and started["sampleFrames"] == 80 and started["channels"] == 1 and started["frequency"] == 8000
assert started["canDestroyFalseReleaseReturned"] and started["canDestroyTrueReleaseReturned"]
assert started["destructionVerified"] is False
assert observed["frame"] > observed["startFrame"] == started["startFrame"]
assert observed["preservedAfterRelease"] and observed["destroyedAfterRelease"]
assert verified["frame"] > verified["cleanupRequestedFrame"] == observed["frame"]
for key in ("canDestroyFalsePreserved", "canDestroyTrueDestroyed", "explicitProbeCleanupDestroyed", "exactGetterReturnedSource", "checkedOnLaterFrames", "bothObservationRootsReleased"):
    assert verified[key] is True, key
assert verified["gameResourcesUsed"] is False

package = json.loads((build / "package-manifest.json").read_text(encoding="utf-8-sig"))
installed = [dict(path=f["path"], expected=f["sha256"], actual=sha(game / f["path"])) for f in package["files"]]
assert all(f["expected"] == f["actual"] for f in installed), "Installed package changed"
snapshot = root / session.name
snapshot.mkdir(exist_ok=True)
for path in logs:
    target = snapshot / path.name
    if path.resolve() != target.resolve():
        shutil.copy2(path, target)
if (session / "status.json").is_file():
    target = snapshot / "status.json"
    if (session / "status.json").resolve() != target.resolve():
        shutil.copy2(session / "status.json", target)
proof = dict(verifiedAtUtc=datetime.now(timezone.utc).isoformat(), sessionId=session.name,
    eventCount=len(events), errorCount=0, stages=[dict(sequence=e["sequence"], timestamp=e["timestamp"], values=v)
        for e, v in ((started_event, started), (observed_event, observed), (verified_event, verified))],
    snapshotFiles=[dict(path=p.relative_to(root).as_posix(), sha256=sha(p)) for p in sorted(snapshot.iterdir()) if p.is_file()],
    installedFiles=installed,
    boundary="Exact AudioClip wrapper constructor/getter/Release and later-frame Unity lifetime only. No local song launch, Sprite wrapper lifecycle, actual audible playback, latency, GPU/native heap measurement, or complete B15 claim.")
(root / "runtime-proof.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(sessionId=session.name, events=len(events), installed=len(installed), stages=proof["stages"]), ensure_ascii=False))
