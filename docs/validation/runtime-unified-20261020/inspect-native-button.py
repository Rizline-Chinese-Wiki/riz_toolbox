"""Verify only the frozen native-button session. Never reads the running game.

Run with: C:\\Python314\\python.exe docs/validation/runtime-unified-20261020/inspect-native-button.py
The action-source windows below come from the coordinating task's observations;
the diagnostic log itself does not identify a human or automation operator.
"""

import collections
import hashlib
import json
import re
from datetime import datetime
from pathlib import Path


ROOT = Path(__file__).resolve().parent / "native-button"
SESSION = ROOT / "session"
EXPECTED_SESSION = "20261020T044257527Z-6d89f92920c54e4c987431041cd2c0f6"
EXPECTED_FINGERPRINT = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require(condition, message):
    if not condition:
        raise AssertionError(message)


export = load(SESSION / "export-manifest.json")
session = load(SESSION / "session.json")
status = load(ROOT / "status-at-export.json")
package = load(ROOT / "package-manifest.json")
require(export["sessionId"] == session["sessionId"] == EXPECTED_SESSION, "Frozen session identity changed")
require(export["incompleteTailsDropped"] == 0, "Export dropped a tail")

artifacts = []
for item in export["files"]:
    relative = Path(item["path"])
    require(not relative.is_absolute() and len(relative.parts) == 1, "Unexpected export file path")
    path = SESSION / relative
    require(path.stat().st_size == item["bytes"], f"Export length differs: {relative}")
    require(sha256(path) == item["sha256"], f"Export digest differs: {relative}")
    artifacts.append({"path": path.relative_to(ROOT).as_posix(), "bytes": item["bytes"], "sha256": item["sha256"]})

events = []
references = {}
for path in sorted(SESSION.glob("events-*.jsonl")):
    require(path.name in {item["path"] for item in export["files"]}, "Unmanifested event file")
    for line_number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        event = json.loads(line)
        require(event["sessionId"] == EXPECTED_SESSION, "Mixed sessions")
        require(event["sequence"] == len(events) + 1, "Missing, duplicate or reordered event")
        events.append(event)
        references[event["sequence"]] = f"{path.relative_to(ROOT).as_posix()}:{line_number}"

require(len(events) == export["exportedEvents"] == 2434, "Frozen export event count changed")
require(status["diagnostics"]["SessionId"] == EXPECTED_SESSION, "Status belongs to another session")
require(status["diagnostics"]["Written"] == len(events), "Status/export count differs")
require(status["gameBuild"]["Fingerprint"] == EXPECTED_FINGERPRINT, "Unexpected game fingerprint")
require(status["sdkVersion"] == "1.2.0" and package["version"] == "0.3.0", "Unexpected SDK/package version")
require(len(package["files"]) == 17, "Unexpected recorded package file count")
require(len({item["path"] for item in package["files"]}) == 17, "Duplicate package file")
require(all(re.fullmatch(r"[0-9a-f]{64}", item["sha256"]) for item in package["files"]), "Invalid package digest")
require(all(plugin["Version"] == package["version"] for plugin in status["plugins"]), "Plugin/package version mismatch")


def attribution(event):
    stamp = datetime.fromisoformat(event["timestamp"])
    if stamp.date().isoformat() == "2026-10-20" and (12, 43) <= (stamp.hour, stamp.minute) <= (12, 45):
        return "coordinator-observed-agent-action-window; attribution external to this diagnostic log"
    if stamp.date().isoformat() == "2026-10-20" and (stamp.hour, stamp.minute) == (12, 56):
        return "additional-execution-observed; operator not established by this diagnostic log"
    return "operator-not-established"


def ref(event, payload=None):
    value = {"sequence": event["sequence"], "timestamp": event["timestamp"],
             "sourceRef": references[event["sequence"]]}
    if payload is not None:
        value["payload"] = payload
    return value


def extract(marker):
    return [ref(event, json.loads(event["message"].split(marker, 1)[1]))
            for event in events if marker in event["message"]]


def probe(label):
    return extract("[example.panel] ModuleProbe " + label + " ")


buttons = probe("BUTTON")
for button in buttons:
    button["actionSource"] = attribution(events[button["sequence"] - 1])
require([item["payload"]["id"] for item in buttons[:5]] == ["audio", "stop", "snapshot", "navigation", "pause"],
        "Initial button sequence changed")

loads, plays, stops = probe("AUDIO LOADED"), probe("AUDIO PLAY"), probe("AUDIO STOP")
require(len(loads) == len(plays) == len(stops) == 8, "Expected eight observed playback cycles")
require(all(item["payload"] == {"SampleFrames": 8000, "Channels": 1, "Frequency": 8000, "IsValid": True}
            for item in loads), "PCM metadata differs")
require(all(item["payload"]["Success"] and item["payload"]["Error"] is None for item in plays + stops),
        "An audio API receipt failed")

effect_pattern = re.compile(r"^Run modification observed: (?P<owner>[^/]+)/(?P<source>[^/]+)/(?P<operation>[^;]+); "
                            r"run=(?P<run>[^;]+); outcome=(?P<outcome>\w+)$")
effects = []
for event in events:
    match = effect_pattern.fullmatch(event["message"])
    if not match:
        continue
    record = ref(event, match.groupdict())
    record["actionSource"] = attribution(event)
    effects.append(record)
audio_effects = [item for item in effects if item["payload"]["source"] == "riz.audio.playback"]
require(len(audio_effects) == 16, "Expected sixteen audio activation/release observations")
for activated, released, loaded, played, stopped in zip(audio_effects[::2], audio_effects[1::2], loads, plays, stops):
    require(activated["payload"]["outcome"] == "EffectActivated" and
            released["payload"]["outcome"] == "EffectReleased", "Audio effect observation did not alternate")
    require(activated["payload"]["run"] == released["payload"]["run"], "Audio run association differs")
    require(loaded["sequence"] < activated["sequence"] < played["sequence"] < released["sequence"] < stopped["sequence"],
            "Audio evidence ordering differs")

starts = extract("[example.events] Observed game.run.started: ")
finishes = extract("[example.events] Observed game.run.finished: ")
require(len(starts) == 2 and len({item["payload"]["RunId"] for item in starts}) == 2, "Expected two distinct actual starts")
require(starts[0]["payload"]["ChartId"] == starts[1]["payload"]["ChartId"], "Retry chart differs")
require(not finishes, "This frozen evidence previously had no verified finish; re-review new evidence")
run_ids = {item["payload"]["RunId"] for item in starts}
require(all(item["payload"]["run"] in run_ids | {"unassociated"} for item in audio_effects), "Unknown audio run association")

controls = probe("CONTROL RECEIPT")
for control in controls:
    control["actionSource"] = attribution(events[control["sequence"] - 1])
pause_refusals = [item for item in controls if item["payload"]["action"] == 0]
require(len(pause_refusals) == 6, "Expected six pause refusals")
require(all(not item["payload"]["result"]["Success"] and
            "CanDoOperation" in item["payload"]["result"]["Error"]["Message"] for item in pause_refusals),
        "Pause was not refused by the original game predicate")
retry = next(item for item in controls if item["payload"]["action"] == 2 and item["payload"]["result"]["Success"])
modification = retry["payload"]["result"]["Value"]["Modification"]
require(modification["Dispatched"] and modification["ObservedRunId"] == starts[0]["payload"]["RunId"], "Retry source run differs")
require(not modification["LocalResultBehaviorVerified"] and not modification["SubmissionBehaviorVerified"],
        "Experimental receipt verification flags changed")

scene_changes = extract("[example.events] Observed game.scene.changed: ")
nav_targets, nav_receipts = probe("NAVIGATION TARGETS"), probe("NAVIGATION RECEIPT")
require(len(nav_receipts) == 1 and nav_receipts[0]["payload"]["Success"], "Expected one accepted menu navigation")
nav_target = next(item for item in nav_targets if item["sequence"] < nav_receipts[0]["sequence"] and item["payload"][0]["Available"])
destination = next(item for item in scene_changes if item["sequence"] > nav_receipts[0]["sequence"])
require(destination["payload"]["PreviousScene"] == "MainMenu" and destination["payload"]["Scene"] == "LevelList",
        "Observed menu destination differs")
retry_scenes = [item for item in scene_changes if retry["sequence"] < item["sequence"] < starts[1]["sequence"]]
require([item["payload"]["Scene"] for item in retry_scenes] == ["ToLevel", "Level"], "Retry scene transition differs")

cancelled = [ref(event) for event in events if "RUN SCOPE CANCELLED active=False" in event["message"]]
require(len(cancelled) == 2, "Expected two cancelled observer scopes at retry scene change")
observation_lost = [dict(ref(event), message=event["message"]) for event in events
                    if event["message"].startswith("Observed lifecycle ObservationChanged:") and "phase=ObservationLost" in event["message"]]
require(observation_lost and starts[0]["payload"]["RunId"] in observation_lost[0]["message"], "Missing explicit observation-loss evidence")
run_ended = [ref(event) for event in events if event["message"].startswith("Observed lifecycle RunEnded:")]
require(not run_ended, "Re-review newly observed run end evidence")

# Preserve malformed inner payloads as evidence failures. Outer event JSON and
# export hashes can be valid even when the logger shortened message strings.
audits, audit_failures = [], []
audit_marker = "[example.panel] ModuleProbe RUN MODIFICATIONS "
for event in events:
    if audit_marker not in event["message"]:
        continue
    encoded = event["message"].split(audit_marker, 1)[1]
    try:
        audits.append(ref(event, json.loads(encoded)))
    except json.JSONDecodeError as error:
        audit_failures.append(dict(ref(event), messageCharacters=len(event["message"]),
                                   payloadCharacters=len(encoded), jsonError=str(error),
                                   preservedPrefix=encoded[:80], preservedSuffix=encoded[-160:]))
require(len(audits) == 2 and len(audit_failures) == 26, "Audit parseability changed; re-review")
require(all(item["messageCharacters"] == 2048 for item in audit_failures), "Audit truncation length changed")
require(audits[0]["payload"]["Value"]["Records"] == [] and audits[0]["payload"]["Value"]["ActiveEffects"] == [],
        "Initial audit was not empty")
initial_record = audits[1]["payload"]["Value"]["Records"][0]
initial_effect = audits[1]["payload"]["Value"]["ActiveEffects"][0]
require(initial_record["EffectId"] == initial_effect["EffectId"] and initial_record["RunId"] is None,
        "Initial parseable active effect is not the unassociated playback")

errors = [dict(ref(event), message=event["message"]) for event in events if event["level"] == "Error"]
warnings = [dict(ref(event), message=event["message"]) for event in events if event["level"] == "Warning"]
require(not errors, "The frozen session contains an Error event")
scores = next(plugin for plugin in status["plugins"] if plugin["Id"] == "example.scores")
require(not scores["DesiredEnabled"] and scores["State"] == 0, "Scores-disabled persistence differs")

result = {
    "schemaVersion": 1,
    "kind": "frozen-native-button-session-evidence-check",
    "scope": "Partial B14/B15 and lifecycle evidence only; no full foundation acceptance claim.",
    "sessionId": EXPECTED_SESSION,
    "integrity": {"events": len(events), "sequenceFirst": events[0]["sequence"], "sequenceLast": events[-1]["sequence"],
                  "strictContiguousSequence": True, "singleSession": True, "incompleteTailsDropped": 0,
                  "firstTimestamp": events[0]["timestamp"], "lastTimestamp": events[-1]["timestamp"], "artifacts": artifacts,
                  "exportManifestSha256": sha256(SESSION / "export-manifest.json"),
                  "statusSha256": sha256(ROOT / "status-at-export.json")},
    "packageIdentity": {"version": package["version"], "sdkVersion": status["sdkVersion"], "manifestFiles": len(package["files"]),
                        "manifestSha256": sha256(ROOT / "package-manifest.json"), "gameFingerprint": EXPECTED_FINGERPRINT,
                        "statusProcessId": status["processResources"]["processId"], "pluginVersionsAgreeWithManifest": True,
                        "loadedDllHashProof": False,
                        "limitation": "Recorded manifest hashes identify the package artifact. These frozen log/status files do not independently contain loaded DLL hashes or prove all 17 installed files matched at launch."},
    "actionProvenance": {"diagnosticLogContainsOperatorIdentity": False,
                         "initialWindow": "2026-10-20 12:43–12:45 +08:00; coordinator observed agent operations outside the log",
                         "additionalWindow": "2026-10-20 12:56 +08:00; actual execution is visible, operator is not established by the log",
                         "buttons": buttons, "counts": dict(collections.Counter(item["payload"]["id"] for item in buttons))},
    "audio": {"loaded": loads, "playReceipts": plays, "stopReceipts": stops, "effectObservations": audio_effects,
              "cyclesWithOrderedLoadActivatePlayReleaseStop": 8,
              "initialUnassociatedCycle": {"buttonSequence": 101, "loadSequence": 102, "activatedSequence": 103,
                                           "playSequence": 104, "stopButtonSequence": 114, "releasedSequence": 115, "stopSequence": 116},
              "runAssociations": dict(collections.Counter(item["payload"]["run"] for item in audio_effects)),
              "audibilityVerifiedByThisScript": False,
              "limitation": "Successful native API receipts and modification observations prove execution, not acoustic output, latency, sample accuracy or full replacement arbitration."},
    "navigation": {"availableTarget": nav_target, "acceptedReceipt": nav_receipts[0], "subsequentObservedScene": destination,
                   "laterTargetObservations": nav_targets[1:],
                   "limitation": "One MainMenu activate-selected transition reached LevelList; other routes and modal/focus rejection are not covered."},
    "gameplay": {"starts": starts, "finishes": finishes, "runEndedEvents": run_ended,
                 "verifiedStartFinishPairs": 0, "pauseRefusals": pause_refusals,
                 "successfulRetry": retry, "retrySceneTransitions": retry_scenes, "controls": controls,
                 "scopeCancellations": cancelled, "observationLost": observation_lost,
                 "limitation": "Two actual distinct starts and a retry transition are observed. The old controller becomes ObservationLost; no result/RunEnded proves termination, and refusal is not proof that pause/resume succeeded."},
    "runModificationAudit": {"completeParsedSnapshots": audits, "malformedSnapshots": audit_failures,
                             "completeCount": len(audits), "malformedCount": len(audit_failures),
                             "observedMalformedMessageCharacterLimit": 2048, "fullAuditAcceptance": False,
                             "limitation": "All 26 malformed payloads remain in the frozen JSONL with valid outer JSON. Their inner RUN MODIFICATIONS JSON is cut mid-value. No missing records/counters are reconstructed or treated as passed.",
                             "nextProbe": "Emit bounded independently valid JSON for counters, run summaries, each record and active effect, below the logger message limit. Include an explicit page/record count and verify release state after Stop and Retry."},
    "diagnostics": {"errorEvents": errors, "warningEvents": warnings, "persistedScoresDisabled": True,
                    "countersAtExport": status["diagnostics"],
                    "limitation": "Zero Error events does not repair truncated payloads or complete module acceptance."},
    "notProven": ["Full B14 input/modal/two-plugin/navigation matrix", "Full B15 audio/content/replacement acceptance",
                  "A completed or aborted result associated with either run in this session", "All run-modification audit records after stop/retry",
                  "Whether the extra 12:56 operations were issued by a human or an agent", "Full B01–B19 foundation completion"],
}
(ROOT / "evidence-summary.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"Verified {len(events)} frozen events; {len(buttons)} buttons; 8 audio cycles; 1 menu navigation; "
      f"2 run starts / 0 verified finishes; 2 complete + 26 truncated audit snapshots. Full acceptance remains unproven.")
