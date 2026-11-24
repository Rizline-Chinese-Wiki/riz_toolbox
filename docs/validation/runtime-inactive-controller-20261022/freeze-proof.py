"""Verify/freeze a complete inactive-clone session after the operator normally exits the game.

Usage from the repository root:
  python docs/validation/runtime-inactive-controller-20261022/freeze-proof.py <session-directory>

No launch, input, activation, game DLL load, or source mutation occurs here. The diagnostics
format has no shutdown event: normal exit/process-stop is an external precondition, not a
claim inferred from the final log row. Source hashes are rechecked to reject a changing copy.
"""
import hashlib
import json
import shutil
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

assert len(sys.argv) == 2, "Supply one complete session directory after normal game exit."
root = Path(__file__).resolve().parent
repo = root.parents[2]
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline")
session = Path(sys.argv[1]).resolve()
assert session.is_dir(), session
logs = sorted(session.glob("events-*.jsonl"))
session_file = session / "session.json"
assert logs and session_file.is_file(), "Full events and the original session.json are required."


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


sources = logs + [session_file]
source_hashes = {path: sha(path) for path in sources}
metadata = json.loads(session_file.read_text(encoding="utf-8-sig"))
assert metadata["sessionId"] == session.name
events = [json.loads(line) for path in logs
          for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
assert events and [event["sequence"] for event in events] == list(range(1, len(events) + 1)), "Not a complete continuous session from sequence 1."
assert {event["sessionId"] for event in events} == {session.name}
assert not any(event["level"] == "Error" for event in events), "The session contains Error events."
assert not any("LocalInactiveControllerProbe unavailable" in event.get("message", "")
               or "LocalInactiveControllerProbe cleanup is unconfirmed" in event.get("message", "")
               for event in events)


def phase(name):
    prefix = "LocalInactiveControllerProbe " + name + " "
    rows = [(event, json.loads(event["message"][len(prefix):])) for event in events
            if event.get("message", "").startswith(prefix)]
    assert len(rows) == 1, (name, len(rows))
    assert len(rows[0][0]["message"][len(prefix):]) <= 1800
    return rows[0]


def true_fields(value, *keys):
    for key in keys:
        assert value[key] is True, key


def false_fields(value, *keys):
    for key in keys:
        assert value[key] is False, key


def zero_awake(value):
    true_fields(value, "TimingVerifiable")
    for key in ("NativeCalls", "OriginalCalls", "CloneBefore", "CloneAfter"):
        assert type(value[key]) is int and value[key] == 0, key


created_e, created = phase("constructed")
destroy_e, destroy = phase("destroy-requested")
verified_e, verified = phase("verified")
assert created_e["sequence"] < destroy_e["sequence"] < verified_e["sequence"]
assert type(created["frame"]) is int and created["frame"] >= 0
assert type(destroy["frame"]) is int and type(verified["frame"]) is int
assert created["frame"] < destroy["frame"] < verified["frame"], "Both GC/readback and destruction confirmation must occur in strictly later frames."
assert destroy["constructedFrame"] == verified["constructedFrame"] == created["frame"]
assert verified["destructionRequestedFrame"] == destroy["frame"]
assert type(created["attempt"]) is int and 1 <= created["attempt"] <= 180
assert type(created["MatchingCards"]) is int and created["MatchingCards"] >= 1
assert type(created["ScannedElements"]) is int and created["ScannedElements"] >= created["MatchingCards"]
assert created["ScannedElements"] <= 4096
assert created["rootRecords"] == destroy["rootRecords"] == verified["ownedRootRecordsReleased"] == 7

observation = created["observation"]
identities = [created["sourcePrefab"], observation["Controller"], observation["ControllerGameObject"], observation["ParentGameObject"]]
assert all(type(value) is int and value > 0 for value in identities)
assert len(set(identities)) == len(identities), "Source prefab, clone component, clone GameObject and parent must be independent identities."
assert type(created["cardOwner"]) is int and created["cardOwner"] > 0
false_fields(observation, "ParentActiveSelf", "ControllerActiveInHierarchy")
assert type(observation["ControllerActiveSelf"]) is bool
true_fields(observation, "ParentMatches")
assert type(observation["Parameters"]) is int and observation["Parameters"] == 0
assert type(observation["LevelInformation"]) is int and observation["LevelInformation"] == 0
zero_awake(created)
zero_awake(destroy)
true_fields(created, "globalsUnchanged")
baseline = created["globalsBaseline"]
assert type(baseline["OwnerIdentity"]) is int and baseline["OwnerIdentity"] > 0
assert baseline["IsCurrentOwner"] is True and type(baseline["ControllerSet"]) is bool
assert len(baseline["DelegateIdentities"]) == 6
assert all(type(identity) is int and identity >= 0 for identity in baseline["DelegateIdentities"])
assert created["globalsAfterClone"] == baseline
assert destroy["globalsAfterGc"] == baseline
false_fields(created, "controllerActivated", "gameResultOrSavePathInvoked", "canLaunch")
true_fields(destroy, "nativeGcCollectReturned", "inactiveReadbackUnchanged", "sourceStillCurrent", "globalsUnchanged")
false_fields(destroy, "destructionVerified", "controllerActivated")
true_fields(verified, "cloneAndGameObjectAndParentAndTransformsDestroyed", "observationRootsReleased",
            "prefabSourcePreserved", "globalsUnchanged", "nativeGcReadback")
assert type(verified["awakeBefore"]) is int and verified["awakeBefore"] == 0
assert type(verified["awakeAfter"]) is int and verified["awakeAfter"] == 0
false_fields(verified, "controllerActivated", "parametersWritten", "gameCatalogModified",
             "gameResultOrSavePathInvoked", "canLaunch")

build = repo / "docs/validation/foundation-inactive-controller-20261022"
package = json.loads((build / "package-manifest.json").read_text(encoding="utf-8-sig"))
assert len(package["files"]) == 18
assert len({entry["path"] for entry in package["files"]}) == 18
installed = []
for entry in package["files"]:
    target = (game / entry["path"]).resolve()
    assert target.is_relative_to(game.resolve()), "Installed manifest path escaped the game directory."
    installed.append(dict(path=entry["path"], expected=entry["sha256"], actual=sha(target)))
assert all(entry["expected"] == entry["actual"] for entry in installed), "Current install differs from the frozen inactive-controller package."

snapshot = root / session.name
snapshot.mkdir(exist_ok=True)
for source in sources:
    target = snapshot / source.name
    if source.resolve() != target.resolve():
        if target.exists():
            assert sha(target) == source_hashes[source], "An existing frozen file differs; do not overwrite an earlier snapshot."
        else:
            shutil.copy2(source, target)
    assert sha(target) == source_hashes[source], "Frozen copy hash changed."
assert sorted(session.glob("events-*.jsonl")) == logs, "Event segments changed during freezing."
assert all(sha(source) == expected for source, expected in source_hashes.items()), "Session files changed during freezing; normally exit first."

proof = dict(
    verifiedAtUtc=datetime.now(timezone.utc).isoformat(), sessionId=session.name,
    events=len(events), levels=dict(Counter(event["level"] for event in events)),
    warnings=[dict(sequence=event["sequence"], message=event["message"])
              for event in events if event["level"] == "Warning"],
    constructed=dict(sequence=created_e["sequence"], values=created),
    destroyRequested=dict(sequence=destroy_e["sequence"], values=destroy),
    verified=dict(sequence=verified_e["sequence"], values=verified),
    files=[dict(path=path.relative_to(root).as_posix(), sha256=sha(path)) for path in sorted(snapshot.iterdir()) if path.is_file()],
    installedFiles=installed,
    stoppedSessionPrecondition="Operator normally exited and independently confirmed the game stopped before running this verifier; log format has no shutdown marker. Source files were byte-stable during copying.",
    scope="One exact inactive controller clone under a separately owned inactive parent, seven owned root records, no observed Awake/global change, native GC/later-frame readback and delayed owned-object destruction. No activation, parameters, published-global restoration, LOCAL result policy or complete playable LOCAL run acceptance."
)
(root / "runtime-proof.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(sessionId=session.name, events=len(events), levels=proof["levels"],
                      frames=[created["frame"], destroy["frame"], verified["frame"]], ownedRoots=7,
                      awakeBefore=0, awakeAfter=0, installedMatched=len(installed))))
