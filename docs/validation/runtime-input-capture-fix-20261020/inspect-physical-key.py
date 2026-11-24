"""Verify the selected physical-key session independently of the earlier no-key snapshot.
Run from repository root with Python 3; no live reads, builds or game control.
"""
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent
def load(path): return json.loads(path.read_text(encoding="utf-8-sig"))
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def require(value, message):
    if not value: raise AssertionError(message)
def read_export(folder):
    manifest = load(folder / "export-manifest.json"); records, refs = [], {}
    require(manifest["incompleteTailsDropped"] == 0, "Incomplete export")
    for entry in manifest["files"]:
        path = folder / entry["path"]
        require(Path(entry["path"]).name == entry["path"] and path.stat().st_size == entry["bytes"] and sha(path) == entry["sha256"], "Export identity changed")
    for path in sorted(folder.glob("events-*.jsonl")):
        for line, raw in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
            event = json.loads(raw)
            require(event["sequence"] == len(records) + 1 and event["sessionId"] == manifest["sessionId"], "Noncontinuous session")
            records.append(event); refs[event["sequence"]] = f"{path.relative_to(ROOT).as_posix()}:{line}"
    require(len(records) == manifest["exportedEvents"], "Export count differs")
    return manifest, records, refs

export, events, refs = read_export(ROOT / "session-physical-key")
before_export, before, _ = read_export(ROOT / "session-before-physical-key")
status = load(ROOT / "status-physical-key.json")
require(export["sessionId"] == before_export["sessionId"] == status["diagnostics"]["SessionId"] == "20261020T123129566Z-247ebfd445b848c3ac851cdda280c4ed", "Different session")
require(len(events) == status["diagnostics"]["Written"] == 561 and len(before) == 469 and events[:len(before)] == before, "Earlier snapshot was changed or is not a prefix")
require(status["diagnostics"]["DiskFailures"] == 0, "Runtime diagnostic failures")
edges = [event for event in events if "Input edge captured after original input update:" in event["message"]]
drains = [event for event in events if "Input update edges drained on Host FrameUpdate:" in event["message"]]
callbacks = [event for event in events if "[example.panel] ModuleProbe INPUT F8 " in event["message"]]
require(len(edges) == len(drains) == len(callbacks) == 1, "Expected one real edge, one drain and one callback")
edge, drain, callback = edges[0], drains[0], callbacks[0]
match = re.search(r"invocation=(\d+); updateType=(\d+); updateCount=(\d+); pressed=Keyboard/F8; queued=1\.", edge["message"])
require(match is not None, "Not a captured F8 edge")
invocation, update_type, update_count = map(int, match.groups())
require(f"updates=1; firstInvocation={invocation}; lastInvocation={invocation}; actions=1." in drain["message"], "Drain does not refer to the captured invocation")
require(469 < edge["sequence"] < drain["sequence"] < callback["sequence"], "Physical key ordering differs")
context = json.loads(callback["message"].split("ModuleProbe INPUT F8 ", 1)[1])
require(context["sceneGeneration"] == 3, "Callback scene generation differs")
comparisons = [(event, json.loads(event["message"].split("Input source comparison: ", 1)[1])) for event in events if "Input source comparison: " in event["message"]]
physical_comparison = next((item for item in comparisons if edge["sequence"] < item[0]["sequence"] < callback["sequence"]), None)
require(physical_comparison is not None, "No device provenance at physical edge")
device = physical_comparison[1]
require(device["Keyboard.current.deviceId"] == "1" and device["Keyboard.current.native"] == "true" and device["Keyboard.current.enabled"] == "true", "Physical edge device source differs")
earlier_time = float(comparisons[0][1]["Keyboard.current.lastUpdateTime"])
require(float(device["Keyboard.current.lastUpdateTime"]) > earlier_time, "Native keyboard did not advance")
def ref(event): return {"sequence": event["sequence"], "timestamp": event["timestamp"], "sourceRef": refs[event["sequence"]]}
result = {"sessionId": export["sessionId"], "events": 561, "preservedEarlierEvents": 469,
          "operator": "User confirmed pressing physical F8 in the active task", "invocation": invocation,
          "updateType": update_type, "updateCount": update_count, "captured": ref(edge), "drained": ref(drain),
          "callback": ref(callback), "deviceObservation": ref(physical_comparison[0]),
          "callbackContext": context, "selectedSessionEdgeCount": 1, "selectedSessionDrainCount": 1,
          "selectedSessionCallbackCount": 1, "keyboardDeviceId": 1, "nativeKeyboardTimeBefore": earlier_time,
          "nativeKeyboardTimeAtEdge": float(device["Keyboard.current.lastUpdateTime"]),
          "limits": ["One physical F8 edge in update type 1; no claim of actual manual-update, held/repeat or remap coverage.",
                     "No actual modal/text-edit/dual-plugin focus matrix in this selected physical-key test.",
                     "Does not complete whole B14 or B01-B19."]}
(ROOT / "physical-key-proof.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"events": 561, "invocation": invocation, "updateType": update_type, "updateCount": update_count,
                  "callbacks": 1, "proofSha256": sha(ROOT / "physical-key-proof.json")}, ensure_ascii=False))
