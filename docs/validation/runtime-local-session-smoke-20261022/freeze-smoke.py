"""Freeze a stopped game session for host startup/menu smoke only; no LOCAL acceptance."""
import hashlib
import json
import shutil
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent
repo = root.parents[2]
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline")
session = Path(sys.argv[1]).resolve()
assert session.is_dir()
logs = sorted(session.glob("events-*.jsonl"))
events = [json.loads(line) for p in logs for line in p.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
assert events and [e["sequence"] for e in events] == list(range(1, len(events) + 1))
assert {e["sessionId"] for e in events} == {session.name}
assert not any(e["level"] == "Error" for e in events)
assert any(e["message"].startswith("RizToolbox 0.3 starting") for e in events)
assert any(e["message"].startswith("Unity tick bound") for e in events)
assert any(e["eventCode"] == "game.scene.changed" and "Scene = MainMenu" in e["message"] for e in events)
assert not any(e["message"].startswith("LocalMetadataProbe ") for e in events)
markers = ["local-native-metadata.enabled", "local-resource-reference.enabled", "local-resource-reference-factory.enabled"]
assert not any((game / "BepInEx/RizToolbox/probes" / name).exists() for name in markers)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


package = json.loads((repo / "docs/validation/foundation-local-session-20261022/package-manifest.json").read_text(encoding="utf-8-sig"))
installed = [dict(path=f["path"], expected=f["sha256"], actual=sha(game / f["path"])) for f in package["files"]]
assert all(f["expected"] == f["actual"] for f in installed)
snapshot = root / session.name
snapshot.mkdir(exist_ok=True)
for source in logs + [session / "session.json"]:
    if source.resolve() != (snapshot / source.name).resolve():
        shutil.copy2(source, snapshot / source.name)
proof = dict(verifiedAtUtc=datetime.now(timezone.utc).isoformat(), sessionId=session.name, eventCount=len(events),
    levels=dict(Counter(e["level"] for e in events)),
    warnings=[dict(sequence=e["sequence"], message=e["message"]) for e in events if e["level"] == "Warning"],
    files=[dict(path=p.relative_to(root).as_posix(), sha256=sha(p)) for p in sorted(snapshot.iterdir()) if p.is_file()],
    installedFiles=installed, temporaryProbeMarkersAbsent=markers,
    scope="895 package host startup, GameStart to MainMenu, followed by observed UI normal exit. No LOCAL policy installed, no native parameters construction or LOCAL gameplay/result/retry acceptance.")
(root / "runtime-smoke.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(sessionId=session.name, events=len(events), levels=proof["levels"], installedMatched=len(installed))))
