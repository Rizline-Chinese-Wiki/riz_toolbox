"""Recheck frozen SDK control/cancellation evidence; never touches live game files.

Usage from the repository root:
    C:\\Python314\\python.exe docs/validation/runtime-cancellation-20261020/inspect-runtime.py

Only runtime-proof.json is generated. A successful inspector run proves the
listed observations, including missing Skip end evidence, not full acceptance.
"""

import collections
import hashlib
import json
import re
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SESSION = ROOT / "session-pause-retry-skip"
EXPECTED_SESSION = "20261020T074327281Z-df2283a1652748e9893b06518df5b40f"


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


manifest = read(SESSION / "export-manifest.json")
session = read(SESSION / "session.json")
status = read(ROOT / "status-pause-retry-skip.json")
installation = read(ROOT / "installation-check.json")
package = read(ROOT / "package-manifest.json")
require(manifest["sessionId"] == session["sessionId"] == status["diagnostics"]["SessionId"] == EXPECTED_SESSION,
        "Mixed diagnostic session identities")
require(manifest["incompleteTailsDropped"] == 0, "Export dropped incomplete tail evidence")
artifact_checks = []
for file in manifest["files"]:
    relative = Path(file["path"])
    require(not relative.is_absolute() and len(relative.parts) == 1, "Unexpected export file path")
    path = SESSION / relative
    require(path.stat().st_size == file["bytes"] and sha(path) == file["sha256"], f"Export hash/length mismatch: {relative}")
    artifact_checks.append({"path": path.relative_to(ROOT).as_posix(), "bytes": file["bytes"], "sha256": file["sha256"]})
events, source_refs = [], {}
for path in sorted(SESSION.glob("events-*.jsonl")):
    require(path.name in {item["path"] for item in manifest["files"]}, "Unmanifested event file")
    for line_number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        event = json.loads(line)
        require(event["sessionId"] == EXPECTED_SESSION and event["sequence"] == len(events) + 1,
                "Noncontiguous sequence or mixed session")
        events.append(event)
        source_refs[event["sequence"]] = f"{path.relative_to(ROOT).as_posix()}:{line_number}"
require(len(events) == manifest["exportedEvents"] == status["diagnostics"]["Written"] == 1064, "Frozen event count differs")

expected_files = {item["path"]: item["sha256"] for item in package["files"]}
installed_files = {item["path"]: item["sha256"] for item in installation["files"]}
require(len(expected_files) == len(package["files"]) == len(installed_files) == len(installation["files"]) == 17,
        "Expected seventeen unique installed files")
require(expected_files == installed_files and all(item["matches"] for item in installation["files"]),
        "Frozen installed-file check does not match package manifest")
require(datetime.fromisoformat(installation["observedAtUtc"]) < datetime.fromisoformat(session["startedAt"]),
        "Installation check was not recorded before this session")
require(status["gameBuild"]["Fingerprint"] == "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79",
        "Unexpected game build fingerprint")
require(status["sdkVersion"] == "1.2.0" and package["version"] == "0.3.0", "Unexpected package/SDK version")
require(all(plugin["Version"] == package["version"] for plugin in status["plugins"]), "Plugin/package version differs")


def ref(event, payload=None):
    record = {"sequence": event["sequence"], "timestamp": event["timestamp"], "sourceRef": source_refs[event["sequence"]]}
    if payload is not None:
        record["payload"] = payload
    return record


def extract(marker):
    return [ref(event, json.loads(event["message"].split(marker, 1)[1]))
            for event in events if marker in event["message"]]


def probe(label):
    return extract("[example.panel] ModuleProbe " + label + " ")


def one_at(records, sequence):
    return next(item for item in records if item["sequence"] == sequence)


starts = extract("[example.events] Observed game.run.started: ")
result_events = extract("[example.events] Observed game.run.finished: ")
require(len(starts) == 2 and not result_events, "Expected two starts and no actual score-result event")
old_run, new_run = (item["payload"]["RunId"] for item in starts)
require(old_run != new_run and starts[0]["payload"]["ChartId"] == starts[1]["payload"]["ChartId"],
        "Retry reused RunId or changed chart")
controls = probe("CONTROL RECEIPT")
require([item["payload"]["action"] for item in controls] == [0, 1, 0, 2, 3], "Unexpected control sequence")
require(all(item["payload"]["result"]["Success"] for item in controls), "A control receipt failed")
pause, resume, second_pause, retry, skip = controls
for item in controls:
    modification = item["payload"]["result"]["Value"]["Modification"]
    require(modification["Dispatched"] and not modification["LocalResultBehaviorVerified"] and
            not modification["SubmissionBehaviorVerified"], "Unexpected experimental result/submission verification flags")
    require(modification["ObservedRunId"] == (new_run if item is skip else old_run), "Control audit run association differs")
require(pause["payload"]["result"]["Value"]["ImmediatePostconditionObserved"] is True and
        second_pause["payload"]["result"]["Value"]["ImmediatePostconditionObserved"] is True,
        "Pause immediate condition was not true")
require(resume["payload"]["result"]["Value"]["ImmediatePostconditionObserved"] is False,
        "Resume immediate observation changed; inspect countdown behavior again")

clocks = probe("GAME CLOCK")
paused_clocks = [one_at(clocks, sequence) for sequence in (569, 642, 660)]
game_fields = ("GameTime", "GameRealTime", "GameSyncedTime")
for field in game_fields:
    values = [item["payload"]["Value"][field] for item in paused_clocks]
    require(all(item["Available"] and item["Unit"] == 1 for item in values), "Game clock unavailable or unit changed")
    require(values[0]["Value"] == values[1]["Value"] == values[2]["Value"], "Game time advanced during sampled pause")
require(all(item["payload"]["Value"]["ObservedRunId"] == old_run for item in paused_clocks), "Paused clock RunId differs")
first_clock, last_clock = (item["payload"]["Value"] for item in (paused_clocks[0], paused_clocks[-1]))
host_delta = last_clock["HostMonotonicTime"]["Value"] - first_clock["HostMonotonicTime"]["Value"]
dsp_delta = last_clock["AudioDspTime"]["Value"] - first_clock["AudioDspTime"]["Value"]
require(host_delta > 350 and dsp_delta > 350, "Insufficient independent host/DSP pause observation span")
after_resume = one_at(clocks, 708)
require(after_resume["payload"]["Value"]["ObservedRunId"] == old_run and
        all(after_resume["payload"]["Value"][field]["Value"] > last_clock[field]["Value"] for field in game_fields),
        "Clocks did not advance again on the same run after Resume")
require(all(not item["payload"]["Value"]["SecondsMappingVerified"] for item in clocks), "Seconds mapping claim changed")

lifecycle = extract("[example.events] ObservationProbe LIFECYCLE ")
run_ends = [item for item in lifecycle if item["payload"]["Kind"] == 5]
require(len(run_ends) == 1, "Frozen session does not contain exactly one verified RunEnded")
retry_end = run_ends[0]
change = retry_end["payload"]
require(change["EndReason"] == 4 and change["Snapshot"]["RunId"] == old_run and change["Result"] is None,
        "Retry end is not Aborted for the old run without a score result")
require(change["Observation"]["Source"] == 2 and "Retry returned" in change["Observation"]["Evidence"] and
        "activeSelf true to false" in change["Observation"]["Evidence"], "Retry callback postcondition evidence differs")
require(retry["sequence"] < retry_end["sequence"] < starts[1]["sequence"], "Retry boundary ordering differs")
paused_lifecycle = [item for item in lifecycle if item["payload"]["Snapshot"]["Phase"] == 3 and
                    item["payload"]["Snapshot"]["RunId"] == old_run]
resumed_lifecycle = [item for item in lifecycle if resume["sequence"] < item["sequence"] < second_pause["sequence"] and
                     item["payload"]["Snapshot"]["Phase"] == 2 and item["payload"]["Snapshot"]["RunId"] == old_run]
require(paused_lifecycle and resumed_lifecycle, "Pause/resume lifecycle follow-up was not observed")

cancelled = [dict(ref(event), owner=event.get("context", {}).get("pluginId")) for event in events
             if "RUN SCOPE CANCELLED active=False" in event["message"]]
require(len(cancelled) == 4, "Expected two owners cancelled at each of Retry and Skip scene boundary")
retry_cancelled, skip_cancelled = cancelled[:2], cancelled[2:]
require({item["owner"] for item in retry_cancelled} == {"example.events", "example.panel"} and
        {item["owner"] for item in skip_cancelled} == {"example.events", "example.panel"}, "Cancelled scope owners differ")
require(max(item["sequence"] for item in retry_cancelled) < retry_end["sequence"], "Retry ordinary callback preceded scope cancellation")
skip_scene = next(item for item in lifecycle if item["sequence"] > skip["sequence"] and
                  item["payload"]["Kind"] == 1 and item["payload"]["Snapshot"]["Scene"] == "LevelList")
require(max(item["sequence"] for item in skip_cancelled) < skip_scene["sequence"], "Skip ordinary scene callback preceded cancellation")
skip_lost = next(item for item in lifecycle if item["sequence"] > skip["sequence"] and
                 item["payload"]["Snapshot"]["RunId"] == new_run and item["payload"]["Snapshot"]["Phase"] == 6)
require(skip_lost["payload"]["EndReason"] is None and skip_lost["payload"]["Result"] is None,
        "Skip disappearance unexpectedly claims a verified end")

# Compact audits are valid JSON with an intentional last-four-record projection.
# Union the preserved projections by record sequence without inventing omitted fields.
audits = probe("RUN MODIFICATIONS")
record_union = {}
for item in audits:
    require(item["payload"]["Success"], "Compact audit call failed")
    for record in item["payload"]["records"]:
        prior = record_union.setdefault(record["Sequence"], record)
        require(prior == record, "Compact record content changed for one sequence")
require(sorted(record_union) == list(range(1, 8)), "Preserved compact snapshots do not cover all seven record identities")
retry_audit = next(record for record in record_union.values() if record["Operation"] == "Retry")
skip_audit = next(record for record in record_union.values() if record["Operation"] == "Skip")
require(retry_audit["RunId"] == old_run and skip_audit["RunId"] == new_run and retry_audit["Association"] == 2,
        "Retry/Skip modification audit associated with the wrong run")
active_audit = one_at(audits, 151)
stopped_audit = one_at(audits, 186)
require(active_audit["payload"]["activeEffects"] == 1 and stopped_audit["payload"]["activeEffects"] == 0,
        "Audio active/stop audit state differs")
audio_records = [record_union[1], record_union[2]]
require(audio_records[0]["EffectId"] == audio_records[1]["EffectId"] and
        [item["Outcome"] for item in audio_records] == [4, 6] and all(item["RunId"] is None for item in audio_records),
        "Audio activation/release identity differs")
audio_loaded, audio_played, audio_stopped = probe("AUDIO LOADED"), probe("AUDIO PLAY"), probe("AUDIO STOP")
require(len(audio_loaded) == len(audio_played) == len(audio_stopped) == 1, "Expected one observed test-tone cycle")
require(audio_loaded[0]["payload"] == {"SampleFrames": 8000, "Channels": 1, "Frequency": 8000, "IsValid": True},
        "Test-tone PCM metadata differs")
require(all(item["payload"]["Success"] and item["payload"]["Error"] is None for item in audio_played + audio_stopped),
        "Audio play or stop API receipt failed")
require(audio_loaded[0]["sequence"] < audio_played[0]["sequence"] < active_audit["sequence"] <
        audio_stopped[0]["sequence"] < stopped_audit["sequence"], "Audio playback/audit ordering differs")
final_audit = audits[-1]["payload"]
require(final_audit["Counters"]["Recorded"] == 7 and final_audit["activeEffects"] == 0, "Final compact audit counters differ")
require(all(value == 0 for key, value in final_audit["Counters"].items() if key not in {"Recorded", "Unassociated"}),
        "Audit reported a drop/rejection/internal failure")

button_pattern = re.compile(r"Owned button activation observed after original: native=(\d+); original=(\d+); reentries=(\d+); wrongThread=(\d+)\.")
native_buttons = []
for event in events:
    match = button_pattern.fullmatch(event["message"])
    if match:
        counts = dict(zip(("native", "original", "reentries", "wrongThread"), map(int, match.groups())))
        require(counts["native"] == counts["original"], "Native button original count differs")
        native_buttons.append(ref(event, counts))
require(native_buttons[-1]["payload"]["native"] == 13, "Last observed button count differs")
hook_samples = []
for event in events:
    if "ObservationProbe HOOK COUNTS " not in event["message"]:
        continue
    before, after = map(int, re.search(r"before=(\d+) after=(\d+)", event["message"]).groups())
    hooks = json.loads(event["message"].split("; ", 1)[1])
    require(before == after and all(item["NativeCalls"] == item["OriginalCalls"] for item in hooks), "Native hook original imbalance")
    hook_samples.append(ref(event, hooks))
require(hook_samples[-1]["payload"][0]["NativeCalls"] == 604, "Final judge count differs")
buttons = probe("BUTTON")
errors = [dict(ref(event), message=event["message"]) for event in events if event["level"] == "Error"]
warnings = [dict(ref(event), message=event["message"]) for event in events if event["level"] == "Warning"]
require(not errors, "Unexpected Error event in frozen session")

result = {
    "schemaVersion": 1, "kind": "frozen-sdk-control-and-cancellation-runtime-proof",
    "sessionId": EXPECTED_SESSION,
    "scope": "Observed SDK Pause/Resume, accepted Retry abort, and Skip transition; partial foundation acceptance only.",
    "integrity": {"events": len(events), "strictContiguousSingleSession": True, "incompleteTailsDropped": 0,
                  "firstTimestamp": events[0]["timestamp"], "lastTimestamp": events[-1]["timestamp"], "artifacts": artifact_checks,
                  "inputSha256": {name: sha(ROOT / name) for name in
                                  ("package-manifest.json", "installation-check.json", "status-pause-retry-skip.json")}},
    "installation": {"recordedFileHashMatches": 17, "manifestVersion": package["version"], "sdkVersion": status["sdkVersion"],
                     "checkedAtUtc": installation["observedAtUtc"], "gameBuild": status["gameBuild"],
                     "limitation": "Checks the preserved pre-launch installed-file hashes against the frozen package manifest; does not independently hash DLLs from process memory."},
    "controls": {"receipts": controls, "buttons": buttons, "buttonCounts": dict(collections.Counter(item["payload"]["id"] for item in buttons)),
                 "allFiveDispatchReceiptsSucceeded": True, "pauseImmediateConditionTrue": True,
                 "resumeImmediateCondition": False, "resumeLaterObserved": True,
                 "limitation": "A successful dispatch is not an immediate full-flow guarantee. Resume's immediate condition was false; later lifecycle and same-run clock advancement supply separate follow-up evidence. Result/submission flags remain false."},
    "clock": {"pausedSamples": paused_clocks, "hostAdvanceWhileGameValuesFrozenSeconds": host_delta,
              "dspAdvanceWhileGameValuesFrozenSeconds": dsp_delta, "afterResumeSample": after_resume,
              "pausedLifecycleSamples": paused_lifecycle, "resumedLifecycleSamples": resumed_lifecycle,
              "secondsMappingVerified": False, "limitation": "GameTime/RealTime/SyncedTime retain their declared game-unit readings; this proves freeze/advance behavior, not a calibrated mapping to seconds or all timing modes."},
    "retry": {"oldRunStart": starts[0], "receipt": retry, "verifiedRunEnd": retry_end,
              "endReason": {"value": 4, "name": "Aborted"}, "scoreResult": None,
              "scopeCancelledBeforeOrdinaryCallback": retry_cancelled, "auditRecord": retry_audit,
              "auditAfterNewController": one_at(audits, 782), "newRunStart": starts[1], "runIdNotReused": True,
              "limitation": "The accepted Retry callback and activeSelf postcondition prove this abort path. No completed score result is fabricated and unrelated exit paths remain unverified."},
    "skip": {"receipt": skip, "auditRecord": skip_audit, "levelListSceneObserved": skip_scene,
             "scopeCancelledBeforeOrdinarySceneCallback": skip_cancelled, "observationLost": skip_lost,
             "verifiedRunEnd": False, "verifiedSkippedEndReason": False,
             "limitation": "Skip dispatched and returned to LevelList, then the controller became ObservationLost. This session has no second RunEnded, no Skipped reason and no real score-result event."},
    "nativeOriginalCalls": {"lastOwnedButtonObservation": native_buttons[-1], "allObservedButtonSamplesBalanced": True,
                            "buttonSampleCount": len(native_buttons), "lastGameplayHookSample": hook_samples[-1],
                            "allObservedGameplayHookSamplesBalanced": True,
                            "limitation": "Button native/original counts are cumulative shared hook diagnostics, not a claim that all 13 calls were module-probe clicks. Final judge count is 604/604; the native NormalMode resume entry remains 0 despite later resumed chart progression."},
    "audio": {"loaded": audio_loaded, "played": audio_played, "stopped": audio_stopped,
              "activeAudit": active_audit, "stoppedAudit": stopped_audit, "sameEffectActivationReleaseRecords": audio_records,
              "audibilityOrLatencyVerified": False},
    "compactAudit": {"snapshots": audits, "allPreservedPayloadsValidJson": True,
                     "uniqueProjectedRecords": [record_union[index] for index in sorted(record_union)],
                     "lastSnapshotCounters": final_audit["Counters"], "lastSnapshotActiveEffects": final_audit["activeEffects"],
                     "intentionalTruncatedProjectionCount": sum(item["payload"]["Truncated"] for item in audits),
                     "limitation": "Truncated=true explicitly marks a bounded last-four-record projection, not invalid JSON. Earlier snapshots cover compact identities 1–7. Compact records omit fields such as full run summaries; this is not full journal/result/submission acceptance."},
    "diagnostics": {"errorEvents": errors, "warningEvents": warnings, "counterSnapshot": status["diagnostics"]},
    "notCompleted": ["Skip-specific RunEnded/Skipped evidence", "Normal completion and real score-result behavior in this session",
                     "Other exit paths and late-result origin", "All B01–B19 acceptance matrices", "Gamepad/keyboard/modal and all UI ownership cases",
                     "Full audio/content/replacement arbitration and latency verification", "Performance baseline acceptance"],
}
(ROOT / "runtime-proof.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print("Verified 1064 frozen events and 17 pre-launch installed hashes; Pause freeze/Resume advance; Retry Aborted with "
      "two-owner scope cancellation before callbacks; new RunId; button 13/13, judge 604/604. "
      "Skip returned to LevelList but has no verified end. Full foundation acceptance remains unproven.")
