"""Verify the frozen 666-package cleanup session; never touches the game.

Run from any directory with Python 3:
  python docs/validation/runtime-visual-scope-cleanup-20261020/inspect-cleanup.py
The assertions cover two formal disables with live Float leases and one
intervening enable. They do not prove final native destruction or all B13.
"""
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parent
session_dir = root / "session-cleanup"
expected_session = "20261020T151803129Z-5c04823ca56c45d29847ade3937d65b8"
profile = "rizline.pc.translucent-ui-float.v1"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def evidence_file(path):
    return {"path": path.relative_to(root).as_posix(), "sha256": sha(path)}


manifest = read(session_dir / "export-manifest.json")
assert manifest["sessionId"] == expected_session
assert manifest["exportedEvents"] == 273
assert manifest["incompleteTailsDropped"] == 0
for entry in manifest["files"]:
    path = session_dir / entry["path"]
    assert path.is_file()
    assert path.stat().st_size == entry["bytes"]
    assert sha(path) == entry["sha256"]
assert read(session_dir / "session.json")["sessionId"] == expected_session

events = []
for path in sorted(session_dir.glob("events-*.jsonl")):
    for line_number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        event = json.loads(line)
        event["_sourceRef"] = f"{path.relative_to(root).as_posix()}:{line_number}"
        events.append(event)
assert len(events) == 273
assert [event["sequence"] for event in events] == list(range(1, 274))
assert all(event["sessionId"] == expected_session for event in events)
assert not [event for event in events if event["level"] in {"Error", "Fatal"}]
assert not [event for event in events if "Owned resource cleanup failed" in event.get("message", "")]


def ref(event):
    return {key: event[key] for key in ("sequence", "timestamp")} | {"sourceRef": event["_sourceRef"]}


def payload(event, marker):
    message = event["message"]
    prefix = "[example.panel] MaterialProbe " + marker + " "
    assert message.startswith(prefix)
    return json.loads(message[len(prefix):])


def marked(marker):
    prefix = "[example.panel] MaterialProbe " + marker + " "
    return [event for event in events if event.get("message", "").startswith(prefix)]


created = marked("CREATED")
applied = marked("APPLIED")
restored = marked("RESTORE REQUESTED")
released = marked("CLONE RELEASED")
requests = marked("CREATE REQUEST")
assert all(len(items) == 2 for items in [created, applied, restored, released, requests])
disable_commands = [event for event in events
                    if event.get("message", "").strip() == "Lifecycle command disable example.panel: True"]
assert len(disable_commands) == 2
enable_between = [event for event in events
                  if event.get("message", "").strip() == "Lifecycle command enable example.panel: True"
                  and disable_commands[0]["sequence"] < event["sequence"] < requests[1]["sequence"]]
assert len(enable_between) == 1

rounds = []
for index in range(2):
    request, create, apply, restore, release = [
        payload(items[index], marker) for items, marker in [
            (requests, "CREATE REQUEST"), (created, "CREATED"), (applied, "APPLIED"),
            (restored, "RESTORE REQUESTED"), (released, "CLONE RELEASED")]]
    assert all(item["profile"] == profile for item in [request, create, apply, restore, release])
    assert request["Target"] == apply["Target"]
    assert request["colors"] == []
    assert request["floats"] == [
        {"Name": "_Brightness", "Value": 0.7},
        {"Name": "_Vibrancy", "Value": 1.1},
        {"Name": "_Flatten", "Value": 0.1}]
    assert create["IsValid"] is True and create["Asset"]["Kind"] == 3
    assert create["EstimatedBytes"] == 65536
    assert create["Asset"] == release["Asset"]
    assert apply["IsActive"] is True
    assert apply["LeaseId"] == restore["LeaseId"]
    assert restore["reason"] == release["reason"] == "plugin-scope-ended"
    assert restore["activeAfterDispose"] is False
    assert release["managedLeaseValidAfterDispose"] is False
    ordered = [items[index]["sequence"] for items in
               [requests, created, applied, restored, released, disable_commands]]
    assert ordered == sorted(ordered) and len(set(ordered)) == len(ordered)
    target_audits = [event for event in marked("TARGET AUDIT")
                    if event["sequence"] < requests[index]["sequence"]
                    and payload(event, "TARGET AUDIT")["Target"] == request["Target"]]
    assert target_audits
    target = payload(target_audits[-1], "TARGET AUDIT")
    assert target["description"] == (
        "MainMenuCanvas/BG: material slot=graphic; source=unity.ui.translucent-image.material")
    pending = [event for event in events if event.get("message", "").startswith(
        f"Visual cleanup pending: owned Material/{create['Asset']['Id']};")]
    assert pending and all("roots/budget retained." in event["message"] for event in pending)
    rounds.append({
        "round": index + 1, "profile": profile, "target": request["Target"],
        "description": target["description"], "asset": create["Asset"],
        "leaseId": apply["LeaseId"], "create": ref(created[index]),
        "apply": ref(applied[index]), "restoreRequest": ref(restored[index]),
        "managedRelease": ref(released[index]), "formalDisable": ref(disable_commands[index]),
        "nativeCleanupPending": [ref(event) | {"message": event["message"]} for event in pending],
    })
assert rounds[0]["asset"]["Id"] != rounds[1]["asset"]["Id"]
assert rounds[0]["leaseId"] != rounds[1]["leaseId"]

snapshots = []
status_names = ["status-before-disable.json", "status-after-disable.json",
                "status-after-enable.json", "status-after-disable-2.json"]
for name, active in zip(status_names, [True, False, True, False]):
    status = read(root / name)
    diagnostic = status["diagnostics"]
    assert diagnostic["SessionId"] == expected_session
    assert diagnostic["Levels"]["Error"] == 0
    assert all(diagnostic[key] == 0 for key in
               ["DiskFailures", "DiskSkipped", "InternalFailures", "RateSuppressed", "RepeatSuppressed"])
    plugin = next(item for item in status["plugins"] if item["Id"] == "example.panel")
    resources = next(item for item in status["pluginResources"] if item["PluginId"] == "example.panel")
    assert plugin["State"] == (2 if active else 0)
    assert plugin["DesiredEnabled"] is active
    assert plugin["LastError"] is None and plugin["Failure"] is None
    if active:
        assert resources["Scopes"] == 2
        assert resources["RetainedOwnedResources"] > 0 and resources["CachedServiceViews"] > 0
    else:
        assert all(resources[key] == 0 for key in ["Scopes", "RetainedOwnedResources", "CachedServiceViews"])
    for other in ["example.color-high", "example.color-low", "example.events"]:
        assert next(item for item in status["plugins"] if item["Id"] == other)["State"] == 2
    snapshots.append({
        "file": name, "snapshotAt": diagnostic["SnapshotAt"],
        "writtenEvents": diagnostic["Written"], "processId": status["processResources"]["processId"],
        "plugin": plugin, "resources": resources,
    })
assert len({item["processId"] for item in snapshots}) == 1
assert applied[0]["sequence"] < snapshots[0]["writtenEvents"] < restored[0]["sequence"]
assert disable_commands[0]["sequence"] <= snapshots[1]["writtenEvents"] < enable_between[0]["sequence"]
assert enable_between[0]["sequence"] <= snapshots[2]["writtenEvents"] < requests[1]["sequence"]
assert disable_commands[1]["sequence"] <= snapshots[3]["writtenEvents"] <= 273

build = read(root / "build-summary.json")
assert build["passed"] == build["total"] == 666 and build["projects"] == 25
assert build["logSha256"] == sha(root / "build.log")
package = read(root / "package-manifest.json")
installation = read(root / "installation-check.json")
expected = {entry["path"]: entry["sha256"] for entry in package["files"]}
assert len(expected) == len(installation["files"]) == 18
assert installation["allMatched"] is True
assert all(entry["actual"] == entry["expected"] == expected[entry["path"]]
           for entry in installation["files"])
integrity = read(root / "source-integrity-check.json")
assert integrity == {"checked": 242, "changed": []}
assert len(read(root / "source-manifest.json")["files"]) == 242

inputs = sorted(session_dir.glob("*")) + [root / name for name in status_names] + [
    root / name for name in ["package-manifest.json", "installation-check.json",
                            "build-summary.json", "source-manifest.json", "source-integrity-check.json"]]
proof = {
    "diagnosticSession": expected_session, "eventCount": len(events),
    "packageManifestSha256": sha(root / "package-manifest.json"),
    "files": [evidence_file(path) for path in inputs if path.is_file()],
    "build": {"passed": 666, "total": 666, "suites": 25},
    "installedHashesMatched": 18, "sourceInputsUnchangedAtInstallation": 242,
    "rounds": rounds, "interveningFormalEnable": ref(enable_between[0]),
    "statusSnapshots": snapshots,
    "errorEvents": [],
    "warningEvents": [ref(event) | {"message": event["message"]} for event in events if event["level"] == "Warning"],
    "limits": [
        "Two live MainMenuCanvas/BG Float clone/override leases across two formal disables and one intervening enable.",
        "Scope/resource/service-view counts describe managed host ownership, not physical native object destruction.",
        "Native cleanup-pending warnings retain roots and budget; no final physical destruction or material-baseline readback receipt is inferred.",
        "UI panel disappearance/restoration was observed by the operator through Computer Use; these JSON assertions verify logs and status only.",
        "No claim of all B05/B13, TMP Color, 20-cycle stress, GPU stability, normal game completion, or full capability matrix.",
        "Evidence belongs only to this installed package and the frozen 273-event selected session.",
    ],
}
(root / "cleanup-proof.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"events": 273, "formalDisables": 2, "interveningEnables": 1,
                  "createdAndApplied": 2, "managedReleases": 2, "errorEvents": 0,
                  "warningEvents": len(proof["warningEvents"]), "passed": True}))
