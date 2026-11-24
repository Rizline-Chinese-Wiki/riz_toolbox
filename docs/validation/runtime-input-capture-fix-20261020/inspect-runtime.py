"""Verify the frozen session before the requested physical F8 test. No live access or build.
Run: C:/Python314/python.exe docs/validation/runtime-input-capture-fix-20261020/inspect-runtime.py
"""
import hashlib
import json
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
SESSION = ROOT / "session-before-physical-key"
SESSION_ID = "20261020T123129566Z-247ebfd445b848c3ac851cdda280c4ed"
def load(path): return json.loads(path.read_text(encoding="utf-8-sig"))
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def require(value, message):
    if not value: raise AssertionError(message)

export, session, status = load(SESSION / "export-manifest.json"), load(SESSION / "session.json"), load(ROOT / "status-before-physical-key.json")
require(export["sessionId"] == session["sessionId"] == status["diagnostics"]["SessionId"] == SESSION_ID, "Session identity differs")
require(export["incompleteTailsDropped"] == 0, "Incomplete exported lines")
artifacts, events, references = [], [], {}
for item in export["files"]:
    path = SESSION / item["path"]
    require(Path(item["path"]).name == item["path"] and path.stat().st_size == item["bytes"] and sha(path) == item["sha256"], "Export content differs")
    artifacts.append({"path": path.relative_to(ROOT).as_posix(), "sha256": sha(path)})
for path in sorted(SESSION.glob("events-*.jsonl")):
    for line, body in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        event = json.loads(body)
        require(event["sessionId"] == SESSION_ID and event["sequence"] == len(events) + 1, "Event sequence differs")
        events.append(event); references[event["sequence"]] = f"{path.relative_to(ROOT).as_posix()}:{line}"
require(len(events) == export["exportedEvents"] == status["diagnostics"]["Written"] == 469, "Frozen event count differs")
require(status["diagnostics"]["DiskFailures"] == 0, "Diagnostic write failures")
manifest, install, build = load(ROOT / "package-manifest.json"), load(ROOT / "installation-check.json"), load(ROOT / "build-summary.json")
expected, actual = ({x["path"]: x["sha256"] for x in source["files"]} for source in (manifest, install))
require(len(expected) == len(actual) == 17 and expected == actual and all(x["matches"] for x in install["files"]), "Installed package differs")
require(datetime.fromisoformat(install["observedAtUtc"]) < datetime.fromisoformat(session["startedAt"]), "Installation identity was not captured before startup")
require(build["projects"] == 25 and build["passed"] == build["total"] == sum(x["passed"] for x in build["suites"]) == 644, "Full build differs")
require(sha(ROOT / "build.log") == build["logSha256"], "Build log changed")
previous = {x["path"]: x["sha256"] for x in load(ROOT.parent / "runtime-input-retirement-20261020" / "package-manifest.json")["files"]}
changed = [{"path": key, "previousSha256": previous.get(key), "currentSha256": digest} for key, digest in expected.items() if previous.get(key) != digest]
require(any(x["path"].endswith("RizToolbox.Adapter.dll") for x in changed), "Fixed input package not distinguished")
def ref(event): return {"sequence": event["sequence"], "timestamp": event["timestamp"], "sourceRef": references[event["sequence"]]}
def matching(marker): return [event for event in events if marker in event["message"]]
def payloads(marker): return [(event, json.loads(event["message"].split(marker, 1)[1])) for event in matching(marker)]
bound = matching("Raw input capture bound after original InputManager.InvokeAfterUpdateCallback@12FED70")
registered = payloads("[example.panel] ModuleProbe INPUT REGISTRATION ")
devices = payloads("[example.panel] ModuleProbe INPUT DEVICES ")
require(len(bound) == 1 and len(registered) == 1 and registered[0][1]["Success"], "Native capture or SDK action registration missing")
require(any(any(d["Device"] == 1 and d["Support"] == 2 for d in payload) for _, payload in devices), "No actual successful keyboard sample")
require(not matching("Verified after-input capture is unavailable") and not matching("after-input capture failed"), "Native capture failed")
f8, edges = matching("ModuleProbe INPUT F8"), matching("Input edge captured after original input update:")
comparisons = payloads("Input source comparison: ")
keyboard_times = [p.get("Keyboard.current.lastUpdateTime") for _, p in comparisons]
materials = lambda label: payloads("[example.panel] MaterialProbe " + label + " ")
created, applied, restore, released, counts = [materials(name) for name in ("CREATED", "APPLIED", "RESTORE REQUESTED", "CLONE RELEASED", "RESOURCE COUNTS")]
require(len(created) == len(applied) == len(restore) == len(released) == 2, "Expected two material cycles")
require(len({p["Asset"]["Id"] for _, p in created}) == 2 and {p["reason"] for _, p in restore} == {"manual", "scene-change"}, "Clone or cleanup identities differ")
cycles = []
for index in range(2):
    c, a, r, done = created[index], applied[index], restore[index], released[index]
    require(c[1]["IsValid"] and a[1]["IsActive"] and not r[1]["activeAfterDispose"] and not done[1]["managedLeaseValidAfterDispose"], "Lease lifecycle differs")
    require(c[1]["Asset"] == done[1]["Asset"] and a[1]["LeaseId"] == r[1]["LeaseId"], "Material lifecycle refers to another object")
    require(c[0]["sequence"] < a[0]["sequence"] < r[0]["sequence"] < done[0]["sequence"], "Lifecycle ordering differs")
    zero = next((event for event, p in counts if event["sequence"] > done[0]["sequence"] and p["retainedProbeClones"] == p["activeProbeOverrides"] == p["estimatedRetainedProbeBytes"] == 0), None)
    require(zero is not None, "Probe reservations not cleared")
    cycles.append({"created": ref(c[0]), "applied": ref(a[0]), "restoreRequested": ref(r[0]), "cloneLeaseReleased": ref(done[0]), "probeCountsZero": ref(zero), "reason": r[1]["reason"]})
pending = matching("Visual cleanup pending: owned Material/")
require(len(pending) == 2 and all("roots/budget retained" in x["message"] for x in pending), "Native deferred-cleanup retention not observed")
require(matching("PreviousScene = MainMenu, Scene = LevelList"), "Actual scene transition missing")
modules = {x["Manifest"]["Id"]: {"state": x["State"], "tier": x["VerificationTier"], "failure": x["Failure"]} for x in status["adapterModules"]}
launcher = load(ROOT / "launcher-last-launch.json")
require(datetime.fromisoformat(launcher["observedAtUtc"]) < datetime.fromisoformat(session["startedAt"]) and launcher["steamAppId"] == "2272590", "Launcher/session ordering differs")
result = {
    "sessionId": SESSION_ID, "events": len(events), "artifacts": artifacts, "changedFromFirst644Package": changed,
    "buildChecks": 644, "buildProjects": 25, "installedFiles": 17,
    "inputCaptureBound": ref(bound[0]), "sdkRegistration": ref(registered[0][0]), "keyboardCaptureSucceeded": True,
    "selectedSessionF8Callbacks": len(f8), "selectedSessionCapturedInputEdges": len(edges),
    "keyboardSourceSampleTimes": keyboard_times, "materialCycles": cycles,
    "deferredNativeCleanupRetained": [ref(event) for event in pending], "modules": modules,
    "limits": ["Physical F8 response still pending; tool F8 and Return left the title screen unchanged while a pointer click advanced it.",
               "No input edge or F8 callback in this selected export; registration/device readiness do not prove an action.",
               "Probe counts are managed probe leases, not GPU or all native allocations.",
               "Deferred-retirement entry was observed; final physical cleanup has no independent receipt in this package.",
               "No TMP Color, full judgement service, native helper game integration or whole B01-B19 completion claim."]
}
(ROOT / "runtime-proof.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"events": len(events), "inputCaptureBound": True, "sdkRegistration": True, "f8Callbacks": len(f8), "materialCycles": len(cycles), "proofSha256": sha(ROOT / "runtime-proof.json")}, ensure_ascii=False))
