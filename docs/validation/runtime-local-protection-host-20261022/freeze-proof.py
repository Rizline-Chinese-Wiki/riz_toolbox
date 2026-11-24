"""Freeze a complete stopped session proving ordinary calls through all four installed gates.

Supply the actual session directory, tested package manifest and independent process-stop record.
No game launch, input, installation, marker change or normal-exit inference is performed here.
This verifies ordinary pass-through only: it does not claim LOCAL skip or playable activation.
"""
import argparse
import hashlib
import json
import shutil
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path, PureWindowsPath

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("session", type=Path)
parser.add_argument("--package-manifest", required=True, type=Path)
parser.add_argument("--stop-record", required=True, type=Path)
parser.add_argument("--game-dir", type=Path, default=Path(r"D:\SteamLibrary\steamapps\common\Rizline"))
args = parser.parse_args()
root = Path(__file__).resolve().parent
game = args.game_dir.resolve()
session = args.session.resolve()
assert session.is_dir()
segments = sorted(session.glob("events-*.jsonl"))
session_json = session / "session.json"
assert segments and session_json.is_file()

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))

sources = segments + [session_json, args.package_manifest.resolve(), args.stop_record.resolve()]
source_hashes = {path: sha(path) for path in sources}
stop = read(args.stop_record)
assert stop["running"] is False and type(stop["normalExitObserved"]) is bool
metadata = read(session_json)
assert metadata["sessionId"] == session.name
events = [json.loads(line) for segment in segments for line in segment.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
assert events and [event["sequence"] for event in events] == list(range(1, len(events) + 1))
assert {event["sessionId"] for event in events} == {session.name}
assert not any(event["level"] == "Error" for event in events), "Complete session includes Error events. Preserve it as failure evidence."
rows = [(event, json.loads(event["message"][len("LocalRunProtection "):])) for event in events
        if event.get("message", "").startswith("LocalRunProtection ")]
assert len(rows) >= 2, "Need installation baseline and later changed native counters."
last = {}
for event, value in rows:
    assert event["level"] == "Info" and len(event["message"][len("LocalRunProtection "):]) <= 1800
    assert value["AllGatesInstalled"] is True and value["AcceptingNewRuns"] is False
    for key in ["StopRequested", "Stopped", "CleanupUnknown", "HandlerWorkPending", "HandlerWorkUnknown"]:
        assert value[key] is False, key
    assert value["UnretiredSessions"] == 0 and value["RetainedBindings"] == 4 and value["Failure"] is None
    boundaries = value["Boundaries"]
    assert len(boundaries) == 4 and {row["Boundary"] for row in boundaries} == {0, 1, 2, 3}
    for boundary in boundaries:
        kind = boundary["Boundary"]
        for key, count in boundary.items():
            assert type(count) is int and count >= 0, (key, count)
        for key in ["Claimed", "Skipped", "Faults", "WrongThreadClaims", "SuppressedReentries"]:
            assert boundary[key] == 0, (kind, key)
        assert boundary["Classified"] == boundary["Continued"]
        assert boundary["OriginalCalls"] == boundary["NativeCalls"]
        assert boundary["Continued"] <= boundary["NativeCalls"]
        if kind in last:
            assert all(boundary[key] >= last[kind][key] for key in boundary if key != "Boundary"), "Counters must never reset or regress."
        last[kind] = boundary
first = {row["Boundary"]: row for row in rows[0][1]["Boundaries"]}
assert all(last[kind]["NativeCalls"] > first[kind]["NativeCalls"] for kind in range(4)), "Each gate needs a real later ordinary invocation."
assert any(event.get("eventCode") == "module.status" or "rizline.local-run-protection" in event.get("message", "") for event in events)

package = read(args.package_manifest)
assert package["schemaVersion"] == 2 and len(package["files"]) == 18
installed = []
seen = set()
for entry in package["files"]:
    relative = Path(entry["path"].replace("\\", "/"))
    assert not relative.is_absolute() and not PureWindowsPath(entry["path"]).drive and ".." not in relative.parts
    path = (game / relative).resolve()
    assert path.is_relative_to(game) and str(path).casefold() not in seen
    seen.add(str(path).casefold())
    expected = entry["sha256"].lower()
    assert len(expected) == 64 and sha(path) == expected
    installed.append(dict(path=entry["path"], expected=expected, actual=sha(path)))

destination = root / session.name
destination.mkdir(exist_ok=True)
for source in sources:
    target = destination / source.name
    if target.exists():
        assert sha(target) == source_hashes[source], "A frozen source differs; do not overwrite it."
    else:
        shutil.copy2(source, target)
    assert sha(target) == source_hashes[source]
assert sorted(session.glob("events-*.jsonl")) == segments
assert all(sha(path) == expected for path, expected in source_hashes.items()), "Source changed while freezing."
assert all(sha(game / entry["path"]) == entry["expected"] for entry in installed)
proof = dict(verifiedAtUtc=datetime.now(timezone.utc).isoformat(), sessionId=session.name,
    events=len(events), levels=dict(Counter(event["level"] for event in events)),
    warnings=[event for event in events if event["level"] == "Warning"],
    statusSequences=[event["sequence"] for event, _ in rows], firstStatus=rows[0][1], finalStatus=rows[-1][1],
    ordinaryNativeDeltas={kind: last[kind]["NativeCalls"] - first[kind]["NativeCalls"] for kind in range(4)},
    installedFiles=installed, stopRecord=stop,
    files=[dict(path=path.relative_to(root).as_posix(), sha256=sha(path)) for path in sorted(destination.iterdir()) if path.is_file() and path.name != "runtime-proof.json"],
    scope="Pinned metadata/RVA resolution and real ordinary pass-through at ResultStart, ControllerRetry, ResultRetry and ResultBack. No transaction handler or LOCAL run was admitted; LOCAL skip, first result-page ownership, activation, save isolation, globals restoration and native consumer retirement remain unverified.")
for path in [destination / "runtime-proof.json", root / "runtime-proof.json"]:
    if path.exists():
        previous = read(path)
        assert previous["sessionId"] == session.name and previous["installedFiles"] == installed
    path.write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(sessionId=session.name, events=len(events), levels=proof["levels"],
    ordinaryNativeDeltas=proof["ordinaryNativeDeltas"], installedMatched=len(installed), localAdmission=False)))
