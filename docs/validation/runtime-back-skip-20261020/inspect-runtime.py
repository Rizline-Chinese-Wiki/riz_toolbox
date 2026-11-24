"""Inspect the frozen 603-check package's Back/Skip session, never the live game.

Run from the repository root:
    C:\\Python314\\python.exe docs/validation/runtime-back-skip-20261020/inspect-runtime.py
Only runtime-proof.json is generated; assertions cover observations, not B01–B19 completion.
"""
import collections
import hashlib
import json
import re
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SESSION = ROOT / "session-back-skip"
EXPECTED_SESSION = "20261020T101511620Z-c6589396de114bf2aac7a3f1dfe89a7a"


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


export = load(SESSION / "export-manifest.json")
session = load(SESSION / "session.json")
status = load(ROOT / "status-back-skip.json")
package = load(ROOT / "package-manifest.json")
installation = load(ROOT / "installation-check.json")
build = load(ROOT / "build-summary.json")
require(export["sessionId"] == session["sessionId"] == status["diagnostics"]["SessionId"] == EXPECTED_SESSION,
        "Session identity mismatch")
require(export["incompleteTailsDropped"] == 0, "Export dropped an incomplete tail")
artifacts = []
for file in export["files"]:
    relative = Path(file["path"])
    require(not relative.is_absolute() and len(relative.parts) == 1, "Unexpected export path")
    path = SESSION / relative
    require(path.stat().st_size == file["bytes"] and sha(path) == file["sha256"], "Export length/hash differs")
    artifacts.append({"path": path.relative_to(ROOT).as_posix(), "bytes": file["bytes"], "sha256": file["sha256"]})
events, references = [], {}
for path in sorted(SESSION.glob("events-*.jsonl")):
    require(path.name in {file["path"] for file in export["files"]}, "Unmanifested event file")
    for line_number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        event = json.loads(line)
        require(event["sessionId"] == EXPECTED_SESSION and event["sequence"] == len(events) + 1,
                "Mixed sessions, missing/duplicate/reordered event")
        events.append(event)
        references[event["sequence"]] = f"{path.relative_to(ROOT).as_posix()}:{line_number}"
require(len(events) == export["exportedEvents"] == status["diagnostics"]["Written"] == 1095, "Frozen event count differs")
expected_files = {file["path"]: file["sha256"] for file in package["files"]}
installed_files = {file["path"]: file["sha256"] for file in installation["files"]}
require(len(expected_files) == len(package["files"]) == len(installed_files) == len(installation["files"]) == 17,
        "Expected seventeen unique package files")
require(expected_files == installed_files and all(file["matches"] for file in installation["files"]), "Installation hash mismatch")
require(datetime.fromisoformat(installation["observedAtUtc"]) < datetime.fromisoformat(session["startedAt"]),
        "Installation check was not before selected game session")
require(build["projects"] == 24 and build["passed"] == build["total"] == 603, "Unexpected offline build summary")
require(sum(item["passed"] for item in build["suites"]) == 603 and len(build["suites"]) == 24, "Offline suite sum differs")
require(package["version"] == "0.3.0" and status["sdkVersion"] == "1.2.0", "Unexpected package or SDK version")
require(status["gameBuild"]["Fingerprint"] == "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79",
        "Unexpected game fingerprint")


def ref(event, payload=None):
    item = {"sequence": event["sequence"], "timestamp": event["timestamp"], "sourceRef": references[event["sequence"]]}
    if payload is not None:
        item["payload"] = payload
    return item


def extract(marker):
    return [ref(event, json.loads(event["message"].split(marker, 1)[1]))
            for event in events if marker in event["message"]]


def probe(label):
    return extract("[example.panel] ModuleProbe " + label + " ")


def message(prefix):
    found = [dict(ref(event), message=event["message"]) for event in events if event["message"].startswith(prefix)]
    require(len(found) == 1, f"Expected one event: {prefix}")
    return found[0]


starts = extract("[example.events] Observed game.run.started: ")
require(len(starts) == 2, "Expected two actual starts")
old_run, new_run = (item["payload"]["RunId"] for item in starts)
require(old_run != new_run and starts[0]["payload"]["ChartId"] == starts[1]["payload"]["ChartId"], "Run identity reused or chart changed")
lifecycles = {owner: extract(f"[{owner}] ObservationProbe LIFECYCLE ") for owner in ("example.events", "example.panel")}
ends = {owner: [item for item in records if item["payload"]["Kind"] == 5] for owner, records in lifecycles.items()}
require(all(len(items) == 2 for items in ends.values()), "Expected two end observations per owner, without duplicates")
for owner, records in lifecycles.items():
    observed_starts = [item for item in records if item["payload"]["Kind"] == 2]
    require([item["payload"]["Snapshot"]["RunId"] for item in observed_starts] == [old_run, new_run],
            f"Missing or duplicate lifecycle start for {owner}")
    require(all(item["payload"]["Result"] is None for item in records), "Unexpected real or synthetic lifecycle result")
for records in ends.values():
    for item, run, reason in zip(records, (old_run, new_run), (4, 3)):
        value = item["payload"]
        require(value["Snapshot"]["RunId"] == value["RelatedRunId"] == run and value["EndReason"] == reason,
                "Wrong run association or terminal reason")
        require(value["Result"] is None and value["Observation"]["Source"] == 2 and
                value["Observation"]["SamplingIntervalMilliseconds"] == 0, "Invented result or non-callback end")
require(all([item["payload"] for item in ends["example.events"]][index] ==
            [item["payload"] for item in ends["example.panel"]][index] for index in range(2)), "Observers disagree on terminal payload")
require(all(item["payload"]["Snapshot"]["Phase"] != 6 for records in lifecycles.values() for item in records),
        "ObservationLost occurred in frozen session")
actual_results = extract("[example.events] Observed game.run.finished: ")
require(not actual_results, "This session unexpectedly contains a real result; re-review")
host_ends = [dict(ref(event), message=event["message"]) for event in events if event["message"].startswith("Observed lifecycle RunEnded:")]
require(len(host_ends) == 2, "Duplicate host terminal event")

back_request = message("Accepted Back request retained")
back_terminal = message("Accepted Back terminal cancellation queued")
skip_queued = message("Accepted Skip termination queued")
back_end, skip_end = ends["example.events"]
require(old_run in back_request["message"] and old_run in back_terminal["message"], "Back token run differs")
require(starts[0]["sequence"] < back_request["sequence"] < back_terminal["sequence"] < back_end["sequence"] < starts[1]["sequence"],
        "Back request/terminal/start ordering differs")
require("matching original Update" in back_end["payload"]["Observation"]["Evidence"] and
        "_terminated and controller._skip false to true" in back_end["payload"]["Observation"]["Evidence"],
        "Back terminal evidence differs")
require(new_run in skip_queued["message"] and "Original NormalModeInLevelController.Skip returned" in
        skip_end["payload"]["Observation"]["Evidence"] and "PauseUI.enabled true to false" in
        skip_end["payload"]["Observation"]["Evidence"], "Skip exact side-effect evidence differs")
controls = probe("CONTROL RECEIPT")
require([item["payload"]["action"] for item in controls] == [0, 3] and
        all(item["payload"]["result"]["Success"] for item in controls), "Unexpected Pause/Skip receipts")
pause, skip = controls
require(skip["payload"]["result"]["Value"]["Modification"]["ObservedRunId"] == new_run, "Skip receipt run differs")
require(all(not item["payload"]["result"]["Value"]["Modification"][field] for item in controls
            for field in ("LocalResultBehaviorVerified", "SubmissionBehaviorVerified")), "Experimental flags changed")

audits = probe("RUN MODIFICATIONS")
require(all(item["payload"]["Success"] and not item["payload"]["Truncated"] for item in audits), "Audit failed or was truncated")
post_skip_audit = next(item for item in audits if skip["sequence"] < item["sequence"] < skip_end["sequence"])
require(skip_queued["sequence"] < skip["sequence"] < post_skip_audit["sequence"] < skip_end["sequence"],
        "Queued termination did not defer until after control receipt/audit")
records = audits[-1]["payload"]["records"]
require([item["Sequence"] for item in records] == [1, 2] and [item["Operation"] for item in records] == ["Pause", "Skip"] and
        [item["RunId"] for item in records] == [old_run, new_run], "Old/new run audit attribution differs")
require(all(item["Outcome"] == 1 and item["Association"] == 2 for item in records), "Audit association/outcome differs")
after_new_controller = next(item for item in audits if back_end["sequence"] < item["sequence"] < starts[1]["sequence"] and
                            item["payload"]["lifecycle"]["Scene"] == "Level")
require(after_new_controller["payload"]["records"][0]["RunId"] == old_run, "New controller reassigned old audit record")
require(audits[-1]["payload"]["Counters"]["Recorded"] == 2 and audits[-1]["payload"]["activeEffects"] == 0,
        "Unexpected final audit counters")

cancelled = [dict(ref(event), owner=event["context"]["pluginId"]) for event in events if "RUN SCOPE CANCELLED active=False" in event["message"]]
require(len(cancelled) == 4, "Expected exactly four scope cancellation observations")
for boundary, cancellations, index in ((back_terminal, cancelled[:2], 0), (skip_queued, cancelled[2:], 1)):
    require({item["owner"] for item in cancellations} == {"example.events", "example.panel"}, "Scope owner differs")
    require(boundary["sequence"] < min(item["sequence"] for item in cancellations) and
            max(item["sequence"] for item in cancellations) < min(items[index]["sequence"] for items in ends.values()),
            "Ordinary terminal callback preceded scope invalidation")
require(post_skip_audit["sequence"] < cancelled[2]["sequence"], "Deferred dispatch ordering differs")

modules = {item["Manifest"]["Id"]: item for item in status["adapterModules"]}
required_modules = ("rizline.runs", "rizline.ui-input", "rizline.run-modifications", "rizline.gameplay-primitives",
                    "rizline.retry-cancellation", "rizline.back-cancellation", "rizline.skip-termination", "rizline.visual.material")
require(all(modules[key]["State"] == 1 and modules[key]["VerificationTier"] == 2 and modules[key]["Failure"] is None
            for key in required_modules), "Expected module unavailable at export")
capabilities = {item["Id"]: item for item in status["capabilityDetails"]}
required_capabilities = ("riz.lifecycle.retry-cancellation.observe", "riz.lifecycle.back-cancellation.observe",
                         "riz.lifecycle.skip-termination.observe")
require(all(capabilities[key]["Available"] for key in required_capabilities), "Cancellation capability unavailable")
devices = probe("INPUT DEVICES")
require(len(devices) == 1, "Expected initial device support snapshot")
device_map = {item["Device"]: item for item in devices[0]["payload"]}
require(device_map[1]["Support"] == 2 and device_map[2]["Support"] == 1 and "disabled" in device_map[2]["Reason"],
        "Keyboard/mouse support classification differs")

button_pattern = re.compile(r"Owned button activation observed after original: native=(\d+); original=(\d+); reentries=(\d+); wrongThread=(\d+)\.")
button_counts, hook_counts = [], []
for event in events:
    match = button_pattern.fullmatch(event["message"])
    if match:
        values = dict(zip(("native", "original", "reentries", "wrongThread"), map(int, match.groups())))
        require(values["native"] == values["original"], "Button original imbalance")
        button_counts.append(ref(event, values))
    if "ObservationProbe HOOK COUNTS " in event["message"]:
        before, after = map(int, re.search(r"before=(\d+) after=(\d+)", event["message"]).groups())
        hooks = json.loads(event["message"].split("; ", 1)[1])
        require(before == after and all(item["NativeCalls"] == item["OriginalCalls"] for item in hooks), "Hook original imbalance")
        hook_counts.append(ref(event, hooks))
require(button_counts[-1]["payload"]["native"] == 7 and hook_counts[-1]["payload"][0]["NativeCalls"] == 106,
        "Observed final hook counts differ")
errors = [dict(ref(event), message=event["message"]) for event in events if event["level"] == "Error"]
require(not errors, "Frozen session contains Error events")
material_actions = [ref(event) for event in events if "MaterialProbe BUTTON" in event["message"]]
require(not material_actions, "This selected session unexpectedly includes material actions; re-review")

result = {
    "schemaVersion": 1, "kind": "603-package-frozen-back-skip-runtime-proof", "sessionId": EXPECTED_SESSION,
    "scope": "Exact Back/Skip termination and scope/audit boundaries, not full B01–B19 acceptance.",
    "attribution": {"offlineBuild": {"projects": 24, "passed": 603, "total": 603, "completedAtUtc": build["completedAtUtc"]},
                    "packageVersion": package["version"], "sdkVersion": status["sdkVersion"], "gameBuild": status["gameBuild"],
                    "preLaunchInstalledHashMatches": 17, "checkedAtUtc": installation["observedAtUtc"],
                    "installationWrapperNote": installation.get("installWrapperNote"),
                    "selectedSessionOnly": True, "previous582PackageResultsImported": False,
                    "limitation": "The frozen build summary records its nativeAcceptance at build time; this separate selected-session proof records later installation/runtime observations. File hashes are recorded pre-launch, not independently read from process memory."},
    "integrity": {"events": len(events), "strictContiguousSingleSession": True, "incompleteTailsDropped": 0,
                  "firstTimestamp": events[0]["timestamp"], "lastTimestamp": events[-1]["timestamp"], "artifacts": artifacts,
                  "inputSha256": {name: sha(ROOT / name) for name in ("package-manifest.json", "installation-check.json",
                                                                    "status-back-skip.json", "Player-back-skip.log", "build-summary.json")}},
    "starts": starts,
    "back": {"request": back_request, "terminalQueued": back_terminal, "endObservers": {owner: items[0] for owner, items in ends.items()},
             "reason": {"value": 4, "name": "Aborted"}, "scopeInvalidatedBeforeOrdinaryCallbacks": cancelled[:2]},
    "skip": {"exactTerminationQueued": skip_queued, "receipt": skip, "auditBeforeDeferredTermination": post_skip_audit,
             "endObservers": {owner: items[1] for owner, items in ends.items()}, "reason": {"value": 3, "name": "Skipped"},
             "scopeInvalidatedBeforeOrdinaryCallbacks": cancelled[2:]},
    "lifecycleIntegrity": {"distinctRunIds": [old_run, new_run], "runIdNotReused": True, "hostRunEndedEvents": host_ends,
                           "oneStartAndEndPerRunPerObserver": True, "observationLostCount": 0,
                           "actualResultEvents": actual_results, "noSyntheticResult": True,
                           "limitation": "Two clean callback-driven terminations in this frozen session do not prove normal completion, all late-result origins or rapid/reentrant transitions."},
    "audit": {"snapshots": audits, "allInnerJsonValid": True, "allSnapshotsUntruncated": True,
              "newControllerRetainsOldRunRecord": after_new_controller, "finalRecords": records,
              "limitation": "This session contains old-run Pause and new-run Skip records, not a Retry record. Previous-package Retry audit evidence remains separate. Result/submission flags remain false."},
    "modules": {"selectedModuleStates": [{"id": key, "state": modules[key]["State"], "verificationTier": modules[key]["VerificationTier"]}
                                         for key in required_modules],
                "cancellationCapabilities": [capabilities[key] for key in required_capabilities],
                "activeModuleCount": sum(item["State"] == 1 for item in modules.values()),
                "waiting": [{"id": key, "state": item["State"], "failure": item["Failure"]} for key, item in modules.items() if item["State"] != 1],
                "limitation": "RuntimeProbed availability is symbol/object readiness, not proof that every module operation has passed its acceptance matrix."},
    "input": {"devices": devices, "keyboard": "RuntimeAvailable", "mouse": "SymbolVerified; current device disabled", "gamepad": "Unsupported",
              "limitation": "No declared keyboard action edge is proved by this snapshot. Native UGUI button activation remains independent of disabled Mouse.current."},
    "nativeOriginalCalls": {"lastButton": button_counts[-1], "lastGameplayHooks": hook_counts[-1], "allObservedSamplesBalanced": True},
    "material": {"probeWindowPresent": any("MaterialProbe WINDOW CREATED" in event["message"] for event in events),
                 "parameterOperationsVerifiedInSelectedSession": False, "limitation": "Material probe exists, but no material button action is part of this frozen session."},
    "errorEvents": errors,
    "remaining": ["Normal completion and complete B01/B09 flow matrix", "Late-result origin and rapid transitions",
                  "Full scope/queue/reentrancy/cleanup stress matrix", "Per-note judgement identity and clock-unit mapping",
                  "Full keyboard/mouse/gamepad/focus/two-plugin/mount acceptance", "Material parameter operation and visual/provider matrices",
                  "Local content/audio replacement matrices", "Performance baselines, twenty cycles and distribution upgrades"],
}
(ROOT / "runtime-proof.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print("Verified 1095 events / 17 pre-launch installed hashes / 603 offline checks: Back request+terminal -> Aborted; "
      "Skip queued -> receipt/audit -> Skipped; both owners' scopes invalid before callbacks; distinct RunIds; "
      "no synthetic result, duplicate end or ObservationLost. Full foundation acceptance remains incomplete.")
