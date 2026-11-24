"""Recheck the frozen 603 package's later material-refusal observations.

Run from the repository root:
    C:\\Python314\\python.exe docs/validation/runtime-back-skip-20261020/inspect-material-refusal.py
Only material-refusal-proof.json is generated. No game/UI/source operation occurs.
"""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SESSION = ROOT / "session-material-refusal"
EXPECTED_SESSION = "20261020T101511620Z-c6589396de114bf2aac7a3f1dfe89a7a"


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


export = load(SESSION / "export-manifest.json")
metadata = load(SESSION / "session.json")
status = load(ROOT / "status-material-refusal.json")
package = load(ROOT / "package-manifest.json")
require(export["sessionId"] == metadata["sessionId"] == status["diagnostics"]["SessionId"] == EXPECTED_SESSION,
        "Different selected session")
require(export["incompleteTailsDropped"] == 0, "Export dropped an incomplete tail")
artifacts = []
for item in export["files"]:
    relative = Path(item["path"])
    require(not relative.is_absolute() and len(relative.parts) == 1, "Unexpected export path")
    path = SESSION / relative
    require(path.stat().st_size == item["bytes"] and sha(path) == item["sha256"], "Export hash/length mismatch")
    artifacts.append({"path": path.relative_to(ROOT).as_posix(), "sha256": item["sha256"], "bytes": item["bytes"]})
events, refs = [], {}
for path in sorted(SESSION.glob("events-*.jsonl")):
    require(path.name in {item["path"] for item in export["files"]}, "Unmanifested event file")
    for line_number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        event = json.loads(line)
        require(event["sessionId"] == EXPECTED_SESSION and event["sequence"] == len(events) + 1,
                "Mixed sessions, duplicate, gap or reordering")
        events.append(event)
        refs[event["sequence"]] = f"{path.relative_to(ROOT).as_posix()}:{line_number}"
require(len(events) == export["exportedEvents"] == status["diagnostics"]["Written"] == 1237, "Frozen event count changed")
earlier = [json.loads(line) for path in sorted((ROOT / "session-back-skip").glob("events-*.jsonl"))
           for line in path.read_text(encoding="utf-8-sig").splitlines()]
require(len(earlier) == 1095 and events[:1095] == earlier, "Later export does not preserve the earlier frozen event prefix")
require(package["version"] == "0.3.0" and status["sdkVersion"] == "1.2.0", "Unexpected package/SDK version")
require(status["gameBuild"]["Fingerprint"] == "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79",
        "Unexpected game fingerprint")


def ref(event, payload=None):
    item = {"sequence": event["sequence"], "timestamp": event["timestamp"], "sourceRef": refs[event["sequence"]]}
    if payload is not None:
        item["payload"] = payload
    return item


def extract(marker):
    return [ref(event, json.loads(event["message"].split(marker, 1)[1])) for event in events if marker in event["message"]]


def material(label):
    return extract("[example.panel] MaterialProbe " + label + " ")


buttons = material("BUTTON")
missing = material("TARGET MISSING")
requests = material("CREATE REQUEST")
refusals = material("CREATE REFUSED")
counts = material("RESOURCE COUNTS")
target_audits = material("TARGET AUDIT")
targets = material("TARGET SELECTED")
require(len(missing) == 1 and missing[0]["sequence"] == 1110, "Expected exactly one unavailable-old-target refusal")
require(missing[0]["payload"]["code"] == 20, "Missing-target numeric error changed")
require(buttons[0]["payload"]["id"] == "tmp-parameters" and buttons[0]["sequence"] < missing[0]["sequence"],
        "Initial TMP attempt did not precede old-target rejection")
old_selection = next(item for item in targets if item["sequence"] == 915)
require(missing[0]["payload"]["Target"] == old_selection["payload"]["Target"], "Rejected old target differs from held selection")
require(old_selection["payload"]["description"] == "NormalModeController(Clone)/Image: material", "Old target role changed")
require(not any(item["sequence"] < missing[0]["sequence"] for item in requests), "Old target rejection happened after a create request")
profiles = ["rizline.pc.tmp-mobile-outline.v1", "rizline.pc.translucent-ui-float.v1"]
require(len(requests) == len(refusals) == 2 and [item["payload"]["profile"] for item in requests] == profiles,
        "Expected TMP and Translucent creation requests")
selected_canvas = next(item for item in targets if item["sequence"] == 1139)
require(selected_canvas["payload"]["description"] == "Canvas/DiscTabItem(Clone): material" and
        selected_canvas["payload"]["Scene"] == "LevelList", "Actual selected game target changed")
for request, refused, profile in zip(requests, refusals, profiles):
    require(request["sequence"] < refused["sequence"] and request["payload"]["profile"] == refused["payload"]["profile"] == profile,
            "Request/refusal ordering or profile differs")
    require(request["payload"]["Target"] == refused["payload"]["Target"] == selected_canvas["payload"]["Target"],
            "Refusal target does not match actual game target")
    require(refused["payload"]["code"] == 20 and refused["payload"]["error"] ==
            "UnsupportedShaderSchema: source shader is not the exact trusted resource shader.", "Not the expected strict shader refusal")
require(counts and all(item["payload"][field] == 0 for item in counts
                       for field in ("created", "released", "applied", "restoreRequests", "retainedProbeClones",
                                     "estimatedRetainedProbeBytes", "activeProbeOverrides")), "Probe reports a material creation/application/resource")
require(all(item["payload"]["assetValid"] is None for item in counts), "Probe unexpectedly has a valid asset")
material_labels = {event["message"].split("MaterialProbe ", 1)[1].split(" {", 1)[0]
                   for event in events if "MaterialProbe " in event["message"]}
require(material_labels == {"BUTTON", "CREATE REFUSED", "CREATE REQUEST", "RESOURCE COUNTS", "TARGET AUDIT",
                            "TARGET MISSING", "TARGET SELECTED", "WINDOW CREATED"},
        "New material result label exists; inspect whether a success occurred")

registration = extract("[example.panel] ModuleProbe INPUT REGISTRATION ")
devices = extract("[example.panel] ModuleProbe INPUT DEVICES ")
f8 = [dict(ref(event), message=event["message"]) for event in events if "INPUT F8" in event["message"]]
require(len(registration) == len(devices) == 1 and registration[0]["payload"]["Success"], "Expected one successful input registration")
require(next(item for item in devices[0]["payload"] if item["Device"] == 1)["Support"] == 2, "Keyboard readiness differs")
require(not f8, "F8 callback now exists; re-review actual input evidence")
module = next(item for item in status["adapterModules"] if item["Manifest"]["Id"] == "rizline.visual.material")
require(module["State"] == 1 and module["VerificationTier"] == 2, "Material module was not runtime-ready")
errors = [dict(ref(event), message=event["message"]) for event in events if event["level"] == "Error"]
require(not errors, "Unexpected Error event")

proof = {
    "schemaVersion": 1, "kind": "603-package-material-refusal-only-runtime-proof", "sessionId": EXPECTED_SESSION,
    "scope": "Selected-target readiness and strict shader rejection, not successful material parameter application.",
    "integrity": {"events": len(events), "strictContiguousSingleSession": True, "incompleteTailsDropped": 0,
                  "firstTimestamp": events[0]["timestamp"], "lastTimestamp": events[-1]["timestamp"],
                  "earlierBackSkipPrefixEventsIdentical": 1095, "additionalEvents": 142, "artifacts": artifacts,
                  "inputSha256": {name: sha(ROOT / name) for name in ("package-manifest.json", "status-material-refusal.json")}},
    "attribution": {"packageVersion": package["version"], "sdkVersion": status["sdkVersion"], "same603GameSession": True,
                    "laterSourceFixIncluded": False,
                    "limitation": "This is a later frozen export of the same loaded 603 package. An in-progress source fix cannot change what this session executed."},
    "oldTargetRefusal": {"button": buttons[0], "previousSelectedTarget": old_selection, "refusal": missing[0],
                         "createRequestIssuedBeforeRefusal": False,
                         "limitation": "The held cross-scene selection was reported TARGET MISSING with numeric code 20. This does not establish a specific TargetExpired enum or prove native memory destruction."},
    "strictShaderRefusals": {"actualSelectedGameTarget": selected_canvas, "requests": requests, "refusals": refusals,
                             "typedTargetAudits": [item for item in target_audits if item["sequence"] > 1095],
                             "limitation": "The two profile requests reject the actual selected Canvas/DiscTabItem material because its shader is not the exact trusted resource shader. This proves refusal, not why TMP binding/profile selection needs a source repair."},
    "probeAccounting": {"samples": counts, "created": 0, "applied": 0, "retainedProbeClones": 0, "activeProbeOverrides": 0,
                        "successfulCreationOrApplyObserved": False,
                        "limitation": "These are probe-owned lease counters, not process-wide native visual allocation statistics or a proof of absence of every side effect."},
    "keyboard": {"registration": registration[0], "initialDeviceSupport": devices[0], "f8CallbacksObserved": f8,
                 "actionAcceptance": False,
                 "externalActionReport": "The coordinating agent reports one successful foreground-game computer-use F8 press before 18:25 +08:00. Its exact input-delivery observation is outside this diagnostic log.",
                 "limitation": "No INPUT F8 callback was observed anywhere in the 1237-event export. Support=2/RuntimeAvailable and successful registration do not prove an action callback or the root cause of its absence."},
    "materialModule": {"id": "rizline.visual.material", "state": module["State"], "verificationTier": module["VerificationTier"]},
    "errorEvents": errors,
    "notProven": ["Successful TMP or Translucent material creation/application/readback", "Stop/restore of an applied material lease",
                  "Full B13 two-provider/scene-rebuild/material allocation matrix", "Keyboard F8 action callback", "Cause of missing F8 delivery",
                  "Later native TMP property-slot fix correctness", "Full B01–B19 foundation completion"],
}
(ROOT / "material-refusal-proof.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print("Verified 1237 frozen events with identical earlier 1095 prefix: one unavailable-target refusal, two exact-shader "
      "refusals, zero created/applied probe resources, zero observed INPUT F8 callbacks. Material/input success remains unverified.")
