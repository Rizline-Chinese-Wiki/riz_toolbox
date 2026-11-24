"""Check only the frozen 607-check material-slot runtime evidence.

Run from repository root:
    C:\\Python314\\python.exe docs/validation/runtime-material-slots-20261020/inspect-runtime.py
Writes runtime-proof.json; does not read/control a live game or execute dotnet.
"""
import hashlib
import json
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SESSION = ROOT / "session"
SESSION_ID = "20261020T103706806Z-674f077f11744d8bb9ca694bbe0ee0d6"


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


export = load(SESSION / "export-manifest.json")
session = load(SESSION / "session.json")
status = load(ROOT / "status-material.json")
manifest = load(ROOT / "package-manifest.json")
installation = load(ROOT / "installation-check.json")
build = load(ROOT / "build-summary.json")
require(export["sessionId"] == session["sessionId"] == status["diagnostics"]["SessionId"] == SESSION_ID, "Session mismatch")
require(export["incompleteTailsDropped"] == 0, "Export dropped incomplete tails")
artifacts = []
for file in export["files"]:
    relative = Path(file["path"])
    require(not relative.is_absolute() and len(relative.parts) == 1, "Unexpected export path")
    path = SESSION / relative
    require(path.stat().st_size == file["bytes"] and sha(path) == file["sha256"], "Export length/hash mismatch")
    artifacts.append({"path": path.relative_to(ROOT).as_posix(), "bytes": file["bytes"], "sha256": file["sha256"]})
events, references = [], {}
for path in sorted(SESSION.glob("events-*.jsonl")):
    require(path.name in {file["path"] for file in export["files"]}, "Unmanifested event file")
    for line_number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        event = json.loads(line)
        require(event["sessionId"] == SESSION_ID and event["sequence"] == len(events) + 1, "Event sequence/session mismatch")
        events.append(event)
        references[event["sequence"]] = f"{path.relative_to(ROOT).as_posix()}:{line_number}"
require(len(events) == export["exportedEvents"] == status["diagnostics"]["Written"] == 488, "Frozen event count differs")
expected = {file["path"]: file["sha256"] for file in manifest["files"]}
actual = {file["path"]: file["sha256"] for file in installation["files"]}
require(len(expected) == len(manifest["files"]) == len(actual) == len(installation["files"]) == 17 and expected == actual,
        "Seventeen installed-file hashes do not match manifest")
require(all(file["matches"] for file in installation["files"]), "A recorded installation mismatch exists")
require(datetime.fromisoformat(installation["observedAtUtc"]) < datetime.fromisoformat(session["startedAt"]),
        "Installation check was not before game session")
require(build["projects"] == 24 and build["passed"] == build["total"] == 607 and
        sum(item["passed"] for item in build["suites"]) == 607, "Unexpected full offline build")
require(any(item["summary"] == "47/47 visual checks passed." for item in build["suites"]), "Visual47 proof missing")
previous_path = ROOT.parent / "runtime-back-skip-20261020" / "package-manifest.json"
previous = {file["path"]: file["sha256"] for file in load(previous_path)["files"]}
changed = [{"path": path, "previous603Sha256": previous.get(path), "current607Sha256": digest}
           for path, digest in expected.items() if previous.get(path) != digest]
require(changed and any(item["path"].endswith("RizToolbox.Adapter.dll") for item in changed), "607 package is not distinguished from 603")


def ref(event, payload=None):
    record = {"sequence": event["sequence"], "timestamp": event["timestamp"], "sourceRef": references[event["sequence"]]}
    if payload is not None:
        record["payload"] = payload
    return record


def extract(marker):
    return [ref(event, json.loads(event["message"].split(marker, 1)[1])) for event in events if marker in event["message"]]


def material(label):
    return extract("[example.panel] MaterialProbe " + label + " ")


def at(items, sequence):
    return next(item for item in items if item["sequence"] == sequence)


targets = material("TARGET SELECTED")
audits = material("TARGET AUDIT")
missing = material("TARGET MISSING")
requests, created, applied = material("CREATE REQUEST"), material("CREATED"), material("APPLIED")
restore, released, counts = material("RESTORE REQUESTED"), material("CLONE RELEASED"), material("RESOURCE COUNTS")
require(len(missing) == 1 and missing[0]["sequence"] == 376 and missing[0]["payload"]["code"] == 20,
        "Expected first stale selection refusal")
stale = at(targets, 190)
selected = at(targets, 384)
require(stale["payload"]["Target"] == missing[0]["payload"]["Target"] and
        missing[0]["sequence"] < selected["sequence"] < requests[0]["sequence"], "Stale/refreshed target ordering differs")
require(selected["payload"]["Scene"] == "LevelList" and selected["payload"]["description"] ==
        "Canvas/BG: material slot=graphic; source=unity.ui.translucent-image.material", "Not actual Translucent game resource slot")
require("/slot/graphic/binding/" in selected["payload"]["Target"]["Key"], "Target lacks explicit slot identity")
require(len(requests) == len(created) == len(applied) == len(restore) == len(released) == 2, "Expected two create/apply/dispose cycles")
require(len({item["payload"]["Asset"]["Id"] for item in created}) == 2 and
        len({item["payload"]["LeaseId"] for item in applied}) == 2, "Clones or leases reused identity")
cycles = []
for index, (request, asset, override, stop, release) in enumerate(zip(requests, created, applied, restore, released)):
    profile = "rizline.pc.translucent-ui-float.v1"
    require(all(item["payload"]["profile"] == profile for item in (request, asset, override, stop, release)), "Unexpected profile")
    require(request["payload"]["Target"] == override["payload"]["Target"] == selected["payload"]["Target"], "Create/apply target differs")
    require(request["payload"]["colors"] == [] and request["payload"]["floats"] ==
            [{"Name": "_Brightness", "Value": 0.7}, {"Name": "_Vibrancy", "Value": 1.1}, {"Name": "_Flatten", "Value": 0.1}],
            "Requested Float values differ or Color was unexpectedly tested")
    require(asset["payload"]["IsValid"] and asset["payload"]["EstimatedBytes"] == 65536 and
            override["payload"]["IsActive"] and override["payload"]["assignmentReadback"] == "adapter Override success",
            "Creation/application did not report success")
    require(request["sequence"] < asset["sequence"] < override["sequence"] < stop["sequence"] < release["sequence"], "Cycle ordering differs")
    reason = "manual" if index == 0 else "scene-change"
    require(stop["payload"]["reason"] == release["payload"]["reason"] == reason and
            stop["payload"]["LeaseId"] == override["payload"]["LeaseId"] and
            not stop["payload"]["activeAfterDispose"], "Override disposal reason/identity differs")
    require(release["payload"]["Asset"] == asset["payload"]["Asset"] and
            release["payload"]["managedLeaseValidAfterDispose"] is False, "Disposed asset identity/validity differs")
    active_counts = next(item for item in counts if override["sequence"] < item["sequence"] < stop["sequence"])
    inactive_counts = next(item for item in counts if item["sequence"] > release["sequence"])
    require(active_counts["payload"]["activeProbeOverrides"] == active_counts["payload"]["retainedProbeClones"] == 1 and
            active_counts["payload"]["assetValid"] is True, "Active resource accounting differs")
    require(all(inactive_counts["payload"][field] == 0 for field in
                ("activeProbeOverrides", "retainedProbeClones", "estimatedRetainedProbeBytes")) and
            inactive_counts["payload"]["assetValid"] is None, "Released resource accounting differs")
    cycles.append({"request": request, "created": asset, "applied": override, "activeProbeCounts": active_counts,
                   "restoreRequested": stop, "cloneReleased": release, "releasedProbeCounts": inactive_counts})
final_counts = cycles[-1]["releasedProbeCounts"]
require([final_counts["payload"][field] for field in ("created", "released", "applied", "restoreRequests")] == [2, 2, 2, 2],
        "Final cumulative probe counters differ")
scenes = extract("[example.events] Observed game.scene.changed: ")
scene_change = next(item for item in scenes if restore[1]["sequence"] < item["sequence"] and item["payload"]["PreviousScene"] == "LevelList")
require(scene_change["payload"]["Scene"] == "FakeMainMenu", "Scene-change cleanup destination differs")
errors = [dict(ref(event), message=event["message"]) for event in events if event["level"] == "Error"]
require(not errors, "Frozen session contains Error events")
require(not material("CREATE REFUSED"), "Unexpected creation refusal after refreshed target")
module = next(item for item in status["adapterModules"] if item["Manifest"]["Id"] == "rizline.visual.material")
require(module["State"] == 1 and module["VerificationTier"] == 2, "Material module not runtime-ready")
proof = {
    "schemaVersion": 1, "kind": "607-package-translucent-float-slot-runtime-proof", "sessionId": SESSION_ID,
    "scope": "Two actual Translucent Float create/apply cycles and managed lease disposal; partial B13 acceptance.",
    "integrity": {"events": 488, "strictContiguousSingleSession": True, "incompleteTailsDropped": 0,
                  "firstTimestamp": events[0]["timestamp"], "lastTimestamp": events[-1]["timestamp"], "artifacts": artifacts,
                  "inputSha256": {name: sha(ROOT / name) for name in ("build-summary.json", "package-manifest.json",
                                                                    "installation-check.json", "status-material.json", "Player-material.log")}},
    "packageAttribution": {"offlineChecks": 607, "projects": 24, "visualChecks": 47, "installedFileHashesMatched": 17,
                           "checkedAtUtc": installation["observedAtUtc"], "gameBuild": status["gameBuild"],
                           "changedFrom603": changed, "previous603ManifestSha256": sha(previous_path),
                           "backSkipAcceptanceImportedFrom603": False,
                           "limitation": "Different Adapter/package hashes and a new session are recorded. Previous Back/Skip/Retry/keyboard observations are not new-package runtime acceptance."},
    "targetSlot": {"staleSelection": stale, "firstMissingRefusal": missing[0], "refreshedActualGameTarget": selected,
                   "resourceIdFromTargetDescription": "unity.ui.translucent-image.material", "slot": "graphic",
                   "limitation": "The probe logs slot/source in metadata Description; it does not expose native pointers or all runtime target fields."},
    "cycles": cycles, "sceneChangeAfterSecondDisposal": scene_change,
    "finalProbeCounters": final_counts,
    "readbackMeaning": {"nativeFloatSetterAndGetterAccepted": True, "targetAssignmentAccepted": True,
                        "basis": "Production backend success requires each SetFloat/GetFloat finite-value comparison and the override channel's assignment readback; frozen logs record these success receipts, not raw getter dumps.",
                        "colorReadbackVerified": False, "publicBaselineRestoreReceipt": False, "gpuResourcesMeasured": False,
                        "visiblePixelChangeVerified": False, "fullRestorationAcceptance": False,
                        "limitation": "RESTORE REQUESTED records Dispose and active=false. CLONE RELEASED records managedLeaseValidAfterDispose=false; resource-count assetValid is null after the probe drops its reference. No public baseline/GPU/pixel receipt is inferred."},
    "discovery": {"observedTargetCap": 128, "fullDynamicAdmissionProven": False,
                  "limitation": "Priority sorting aids fresh discovery, but a full 128-binding set can still starve newly arriving targets. This session does not prove persistent rebinding or complete TMP discovery."},
    "errorEvents": errors,
    "remaining": ["Successful TMP Color/profile parameter creation and correct font-shared slot application",
                  "Independent baseline material restoration/readback after Dispose", "Visible pixels, shader persistence and GPU/native resource statistics",
                  "Two-provider arbitration, failure preservation and repeated resource/scene stress", "Dynamic 128-target admission starvation",
                  "Sprite/font and independent gameplay note/track/camera/postprocess targets", "Full B01–B19 acceptance"],
}
(ROOT / "runtime-proof.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print("Verified 488 events, 17 installed hashes and 607/24 offline build: old-target refusal; Canvas/BG Translucent Float "
      "created/applied twice; manual and scene-change Dispose invalidate leases and leave zero retained probe resources. "
      "TMP Color, baseline restore, pixels/GPU and full B13 acceptance remain unverified.")
