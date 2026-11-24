"""Verify/freeze scratch native parameters and resource lifecycle, never launch or control the game."""
import hashlib
import json
import math
import shutil
import struct
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent
repo = root.parents[2]
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline")
session = Path(sys.argv[1]).resolve()
logs = sorted(session.glob("events-*.jsonl"))
assert logs
events = [json.loads(line) for p in logs for line in p.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
assert events and [e["sequence"] for e in events] == list(range(1, len(events) + 1))
assert {e["sessionId"] for e in events} == {session.name}
assert not any(e["level"] == "Error" for e in events)
assert not any(e["message"].startswith("LocalParametersProbe unavailable") or "LocalParametersProbe cleanup is unconfirmed" in e["message"] for e in events)


def phase(name):
    prefix = "LocalParametersProbe " + name + " "
    rows = [(e, json.loads(e["message"][len(prefix):])) for e in events if e["message"].startswith(prefix)]
    assert len(rows) == 1, (name, len(rows))
    return rows[0]


created_e, created = phase("constructed")
release_e, release = phase("release-requested")
verified_e, verified = phase("verified")
assert created_e["sequence"] < release_e["sequence"] < verified_e["sequence"]
assert created["owners"] == 2 and created["distinctIdentities"] is True and created["rootRecordsPerOwner"] == 1
assert created["nativeGcCollectReturned"] is True and created["scratchChartOnly"] is True
assert created["controllerCreated"] is False and created["canLaunch"] is False
values = created["first"]
assert len({values[k] for k in ("Metadata", "CoverReference", "MusicReference", "Chart", "JudgeData")}) == 5
assert all(values[k] != 0 for k in ("Metadata", "CoverReference", "MusicReference", "Chart", "JudgeData"))
for scalar, key in (("Speed", "speedBits"), ("Offset", "offsetBits")):
    assert math.isfinite(values[scalar])
    assert struct.unpack("<i", struct.pack("<f", values[scalar]))[0] == created[key]
assert release["parameterRootRecordsReleased"] == 2 and release["metadataRootRecordsReleased"] == 10
assert release["borrowedWrappersReleased"] == 2 and release["scratchUnityObjects"] == 3
assert release["disposedReadRejected"] is True and release["borrowWithdrawalRejected"] is True
assert release["destructionVerified"] is False
assert verified["frame"] > verified["destructionRequestedFrame"] == release["frame"]
for key in ("constructorFiveReferencesPreserved", "gettersAfterNativeGc", "twoIndependentParameterOwners",
            "disposedReadRejected", "borrowWithdrawalRejected", "wrapperFalsePreservedOwnAssets",
            "allThreeScratchUnityObjectsDestroyed", "allObservationRootRecordsReleased"):
    assert verified[key] is True, key
for key in ("controllerCreated", "gameCatalogModified", "gameResultOrSavePathInvoked", "canLaunch"):
    assert verified[key] is False, key


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


package = json.loads((repo / "docs/validation/foundation-local-parameters-20261022/package-manifest.json").read_text(encoding="utf-8-sig"))
installed = [dict(path=f["path"], expected=f["sha256"], actual=sha(game / f["path"])) for f in package["files"]]
assert all(f["expected"] == f["actual"] for f in installed)
snapshot = root / session.name
snapshot.mkdir(exist_ok=True)
for source in logs + [session / "session.json"]:
    target = snapshot / source.name
    if source.resolve() != target.resolve():
        shutil.copy2(source, target)
proof = dict(verifiedAtUtc=datetime.now(timezone.utc).isoformat(), sessionId=session.name, events=len(events),
    levels=dict(Counter(e["level"] for e in events)),
    warnings=[dict(sequence=e["sequence"], message=e["message"]) for e in events if e["level"] == "Warning"],
    constructed=dict(sequence=created_e["sequence"], values=created),
    released=dict(sequence=release_e["sequence"], values=release), verified=dict(sequence=verified_e["sequence"], values=verified),
    files=[dict(path=p.relative_to(root).as_posix(), sha256=sha(p)) for p in sorted(snapshot.iterdir()) if p.is_file()],
    installedFiles=installed,
    scope="Two separate borrowed InsideLevelParams roots with scratch Sprite/AudioClip/Chart/JudgeData and isolated metadata; native GC/readback and delayed own Unity destruction. No playable chart, controller/globals, stock-save isolation or complete LOCAL gameplay acceptance.")
(root / "runtime-proof.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(sessionId=session.name, events=len(events), levels=proof["levels"], speed=values["Speed"], offset=values["Offset"], installedMatched=len(installed))))
