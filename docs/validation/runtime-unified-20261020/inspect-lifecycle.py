"""Recheck preserved real-session evidence; does not launch or control the game."""
import hashlib
import json
import re
from pathlib import Path

root = Path(__file__).parent / "lifecycle-probe-fixed"
paths = sorted((root / "session").glob("events-*.jsonl"))
events = [json.loads(line) for path in paths for line in path.read_text(encoding="utf-8-sig").splitlines()]
assert events and len({event["sessionId"] for event in events}) == 1
assert all(a["sequence"] < b["sequence"] for a, b in zip(events, events[1:]))

def payloads(marker):
    return [{"sequence": event["sequence"], "timestamp": event["timestamp"],
             "payload": json.loads(event["message"].split(marker, 1)[1])}
            for event in events if marker in event["message"]]

starts = payloads("[example.events] Observed game.run.started: ")
finishes = payloads("[example.events] Observed game.run.finished: ")
assert len(starts) == len(finishes) == 2
assert len({item["payload"]["RunId"] for item in starts}) == 2
for start, finish in zip(starts, finishes):
    assert start["sequence"] < finish["sequence"]
    assert start["payload"]["RunId"] == finish["payload"]["RunId"]
    assert start["payload"]["ChartId"] == finish["payload"]["ChartId"]
    assert finish["payload"]["AssociatedWithObservedStart"]
    assert finish["payload"]["Crashed"] and not finish["payload"]["Skipped"]
pauses = [event for event in events if event["message"].startswith("Observed lifecycle Paused:")]
resumes = [event for event in events if event["message"].startswith("Observed lifecycle Resumed:")]
assert len(pauses) == len(resumes) == 1
second_id = starts[1]["payload"]["RunId"]
assert all("run=" + second_id in event["message"] for event in pauses + resumes)
assert starts[1]["sequence"] < pauses[0]["sequence"] < resumes[0]["sequence"] < finishes[1]["sequence"]
hooks = []
for event in events:
    if "ObservationProbe HOOK COUNTS " not in event["message"]:
        continue
    native = json.loads(event["message"].split("; ", 1)[1])
    assert all(item["NativeCalls"] == item["OriginalCalls"] for item in native)
    match = re.search(r"before=(\d+) after=(\d+)", event["message"])
    assert match and match[1] == match[2]
    hooks.append(native)
assert hooks[-1][0]["NativeCalls"] == 959
cancelled = [event for event in events if "RUN SCOPE CANCELLED active=False" in event["message"]]
released = [event for event in events if "RUN SCOPE RELEASE PASS" in event["message"]]
assert len(cancelled) == len(released) == 4
assert not [event for event in events if event["level"] == "Error"]
status = json.loads((root / "status-final.json").read_text(encoding="utf-8-sig"))
score = next(plugin for plugin in status["plugins"] if plugin["Id"] == "example.scores")
assert not score["DesiredEnabled"] and score["State"] == 0
result = {
    "schemaVersion": 1, "kind": "real-game-preserved-session-check",
    "sessionId": events[0]["sessionId"], "events": len(events),
    "packageManifestSha256": hashlib.sha256((root / "package-manifest.json").read_bytes()).hexdigest(),
    "starts": starts, "finishes": finishes,
    "pauseSequence": pauses[0]["sequence"], "resumeSequence": resumes[0]["sequence"],
    "scopeCancellations": len(cancelled), "scopeReleaseChecks": len(released),
    "lastHookDiagnostics": hooks[-1], "errorEvents": 0,
    "persistedScoresDisabled": True,
    "limitations": [
        "Both completed runs have actual Crashed=true; neither proves normal completion.",
        "Natural pause/resume was sampled from the owned pause object. Existing NormalMode Pause/Resume machine-entry counters remained zero.",
        "Early return and all new modules' full acceptance remain incomplete.",
        "The game had its existing life-100, hidden-health and free-hold modifiers enabled. This is not the game-only performance baseline."]}
(root / "evidence-summary.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print("Real session checked: 2 associated failed runs, distinct retry ID, same-run pause/resume, 959 original calls, 4 scope releases.")
