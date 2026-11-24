"""Record the later failed 666 run separately from the successful cleanup test."""
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parent
folder = root / "session-auto-return-failure"
session_id = "20261020T151803129Z-5c04823ca56c45d29847ade3937d65b8"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def ref(event):
    return {key: event[key] for key in ["sequence", "timestamp"]} | {
        "sourceRef": f"session-auto-return-failure/events-000001.jsonl:{event['sequence']}"}


manifest = read(folder / "export-manifest.json")
assert manifest["sessionId"] == session_id and manifest["exportedEvents"] == 949
assert manifest["incompleteTailsDropped"] == 0
for item in manifest["files"]:
    path = folder / item["path"]
    assert path.stat().st_size == item["bytes"] and sha(path) == item["sha256"]
lines = (folder / "events-000001.jsonl").read_text(encoding="utf-8-sig").splitlines()
prefix = (root / "session-cleanup/events-000001.jsonl").read_text(encoding="utf-8-sig").splitlines()
assert len(prefix) == 273 and lines[:273] == prefix
events = list(map(json.loads, lines))
assert len(events) == 949 and [e["sequence"] for e in events] == list(range(1, 950))
assert all(e["sessionId"] == session_id for e in events)


def parse_marker(marker, owner="example.panel"):
    prefix = f"[{owner}] {marker} "
    return [(e, json.loads(e["message"][len(prefix):])) for e in events
            if e.get("message", "").startswith(prefix + "{")]


lifecycle = parse_marker("ObservationProbe LIFECYCLE", "example.events")
starts = [(e, v) for e, v in lifecycle if v["Kind"] == 2]
assert len(starts) == 1
run_id = starts[0][1]["Snapshot"]["RunId"]
assert run_id == "6fb6c0ef2a0e417c91714c841682f48e"
assert not [(e, v) for e, v in lifecycle if v["Kind"] == 5 or v["EndReason"] is not None or v["Result"] is not None]
assert not [e for e in events if e.get("message", "").startswith(
    ("Observed lifecycle RunEnded:", "Observed game event game.run.ended:"))]
awaiting = next((e, v) for e, v in lifecycle if v["Snapshot"]["Phase"] == 4)
returned = next((e, v) for e, v in lifecycle
                if e["sequence"] > awaiting[0]["sequence"] and v["Snapshot"]["Scene"] == "LevelList")
assert returned[1]["Snapshot"]["RunId"] == run_id and returned[1]["Snapshot"]["Phase"] == 4

withdrawn = [e for e in events if e.get("message", "").startswith("Judgement identity coverage withdrawn for this run:")]
assert len(withdrawn) == 1 and withdrawn[0]["sequence"] == 689
assert "ReadHoldNote" in withdrawn[0]["exception"]
statuses = parse_marker("ModuleProbe JUDGEMENT STATUS")
last_status_event, last_status = statuses[-1]
assert last_status["Success"] is True
judgement = last_status["Value"]
assert judgement["Installed"] is True and judgement["RunIdentityAvailable"] is False
assert judgement["Observations"] == 281 and judgement["UnassociatedCalls"] == 8
assert "Native metadata memory is unreadable" in judgement["UnavailableReason"]
assert sum(x["CapturedCalls"] for x in judgement["Sources"]) == 281
sdk_samples = parse_marker("ModuleProbe JUDGEMENT")
assert len(sdk_samples) == 7
challenge = [(e, v) for e, v in sdk_samples if v["SourceId"] == "auto.challenge-max"]
assert len(challenge) == 1 and challenge[0][1]["OriginalReturned"] is True
assert challenge[0][1]["RuntimeNote"]["ObservedRunId"] == run_id
assert challenge[0][1]["CountersAfter"]["MaxPerfect"] == 1
assert challenge[0][1]["CountersBefore"]["MaxPerfect"] == 0

hook_events = [e for e in events if e.get("message", "").startswith("[example.events] ObservationProbe HOOK COUNTS ")]
last_hook = hook_events[-1]
hooks = json.loads(last_hook["message"].split("; ", 1)[1])
judge_hook = next(x for x in hooks if x["Id"] == "game.normal.judge")
assert all(judge_hook[key] == 479 for key in ["NativeCalls", "OriginalCalls", "BeforeDeliveries", "AfterDeliveries"])
errors = [e for e in events if e["level"] == "Error"]
assert [e["sequence"] for e in errors] == [929, 931]
assert all("VisualCleanupPending" in e["exception"] for e in errors)
assert "MaterialRuntimeProbe.NextTarget" in errors[0]["exception"]
waiting = next(e for e in events if e["sequence"] > errors[0]["sequence"]
               and "Adapter module 'rizline.visual.material' Waiting:" in e.get("message", ""))
restored = next(e for e in events if e["sequence"] > waiting["sequence"]
                and "Adapter module 'rizline.visual.material' active;" in e.get("message", ""))
status_path = root / "status-auto-return-failure.json"
status = read(status_path)
assert status["diagnostics"]["SessionId"] == session_id and status["diagnostics"]["Levels"]["Error"] == 2
plugin = next(x for x in status["plugins"] if x["Id"] == "example.panel")
assert plugin["State"] == 4 and plugin["DesiredEnabled"] is True
assert "VisualCleanupPending" in plugin["LastError"]
resources = next(x for x in status["pluginResources"] if x["PluginId"] == "example.panel")
assert all(resources[k] == 0 for k in ["Scopes", "RetainedOwnedResources", "CachedServiceViews"])

proof = {
    "diagnosticSession": session_id, "eventCount": 949, "unchangedCleanupPrefixEvents": 273,
    "packageManifestSha256": sha(root / "package-manifest.json"),
    "files": [{"path": p.relative_to(root).as_posix(), "sha256": sha(p)}
              for p in sorted(folder.glob("*")) + [status_path, root / "Player-auto-return-failure.log"]
              if p.is_file()],
    "runId": run_id,
    "lifecycle": [{"event": ref(e), "value": v} for e, v in lifecycle if e["sequence"] >= starts[0][0]["sequence"]],
    "observedRunEndCount": 0, "observedResultCount": 0, "normalCompletionProven": False,
    "judgementIdentityWithdrawal": ref(withdrawn[0]) | {
        "message": withdrawn[0]["message"], "exception": withdrawn[0]["exception"]},
    "lastDetailedJudgementStatus": ref(last_status_event) | {"value": judgement},
    "sdkSampleCount": len(sdk_samples),
    "newAutoChallengeSample": ref(challenge[0][0]) | {"value": challenge[0][1]},
    "lastHookCounts": ref(last_hook) | {"value": hooks},
    "errors": [ref(e) | {"message": e["message"], "exception": e["exception"]} for e in errors],
    "visualWaiting": ref(waiting) | {"message": waiting["message"]},
    "visualModuleRestored": ref(restored) | {"message": restored["message"]},
    "pluginAtFinalStatus": plugin, "pluginResourcesAtFinalStatus": resources,
    "limits": [
        "479 is the shared inner NormalMode hook count, not 479 successful identity observations; last detailed status reports 281 observations and 8 unassociated calls.",
        "The later exact unassociated count was not sampled after the final 479 hook calls.",
        "A single auto.challenge-max DTO is observed, followed by ReadHoldNote validation failure; complete challenge-source or hold coverage is not established.",
        "Observed playback, levelOver and return to LevelList do not prove NormalCompleted; no RunEnded or result appears in this frozen selected session.",
        "Visual availability waiting/recovery occurred after callback failure stopped example.panel; the 273-event cleanup success remains a separate earlier result.",
        "No final native release, full availability-transition matrix or full B01/B09/B11/B13 acceptance is inferred.",
    ],
}
(root / "auto-return-proof.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"events": 949, "unchangedPrefix": 273, "hookCalls": 479,
                  "lastIdentityObservations": 281, "lastUnassociated": 8,
                  "errors": 2, "runEnds": 0, "failureEvidenceVerified": True}))
