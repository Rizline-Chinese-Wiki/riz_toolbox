"""Verify and freeze one actual LOCAL metadata-only session. No process launch or input."""
import hashlib
import json
import math
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent
repo = root.parents[2]
session = Path(sys.argv[1]).resolve()
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline")
build = repo / "docs/validation/foundation-local-metadata-20261022"
assert session.is_dir()
logs = sorted(session.glob("events-*.jsonl"))
assert logs
events = [json.loads(line) for p in logs for line in p.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
assert events and [e["sequence"] for e in events] == list(range(1, len(events) + 1))
assert {e["sessionId"] for e in events} == {session.name}
assert not any(e.get("level") == "Error" for e in events)
assert not any("LocalMetadataProbe unavailable" in e.get("message", "") or "LocalMetadataProbe cleanup is unconfirmed" in e.get("message", "") for e in events)

def phase(name):
    prefix = "LocalMetadataProbe " + name + " "
    return [(e, json.loads(e["message"][len(prefix):])) for e in events if e.get("message", "").startswith(prefix)]

created = phase("created")
rows = phase("row")
verified = phase("verified")
assert len(created) == len(verified) == 1 and len(rows) == 3
assert created[0][1]["count"] == 3 and created[0][1]["nativeGcCollectReturned"] is True
assert created[0][1]["ownerRootRecords"] == 30 and created[0][1]["canLaunch"] is False
assert [v["index"] for _, v in rows] == [0, 1, 2]
assert created[0][0]["sequence"] < rows[0][0]["sequence"] < rows[1][0]["sequence"] < rows[2][0]["sequence"] < verified[0][0]["sequence"]
expected = [
    ("probe.metadata.a", 1, "Artist A", "LOCAL_A", "2.5", "Designer A", [.15, .35, .65, 1]),
    ("probe.metadata.b", 1, "Artist B", "LOCAL_B", "2.5", "Designer B", [.2, .7, .3, 1]),
    ("probe.metadata.a", 2, "Artist A revision 2", "LOCAL_A_R2", "3.5", "Designer A", [.6, .4, .2, 1]),
]
for (_, row), (namespace, revision, artist, name, lv, designer, color) in zip(rows, expected):
    assert row["id"] == {"Namespace": namespace, "Id": "same-song"}
    assert row["Revision"] == revision and row["ChartId"] == "chart-a"
    value = row["observation"]
    assert value["MusicName"] == "LOCAL Metadata Sentinel" and value["Artist"] == artist
    assert value["LevelInternalName"] == value["LevelDisplayName"] == name
    assert value["Lv"] == lv and value["Charter"] == designer and value["Status"] == 0
    assert value["From"] and all(math.isclose(value["ThemeColor"][k], v, abs_tol=1e-6) for k, v in zip(("R", "G", "B", "Alpha"), color))
assert len({v["nativeIdentity"] for _, v in rows}) == 3
assert len({v["observation"]["From"] for _, v in rows}) == 1
final = verified[0][1]
assert final["owners"] == 3
for key in ("sameTitleCrossProvider", "sameIdDifferentRevision", "getterReadsAfterNativeGc", "disposedReadRejected", "remainingOwnersSurvivedWithdrawal", "allOwnerRootRecordsReleased"):
    assert final[key] is True, key
for key in ("canLaunch", "gameCatalogModified", "gameResultOrSavePathInvoked"):
    assert final[key] is False, key

def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

manifest = json.loads((build / "package-manifest.json").read_text(encoding="utf-8-sig"))
installed = [dict(path=f["path"], expected=f["sha256"], actual=sha(game / f["path"])) for f in manifest["files"]]
assert all(f["expected"] == f["actual"] for f in installed)
snapshot = root / session.name
snapshot.mkdir(exist_ok=True)
for source in logs:
    target = snapshot / source.name
    if source.resolve() != target.resolve():
        shutil.copy2(source, target)
proof = dict(verifiedAtUtc=datetime.now(timezone.utc).isoformat(), sessionId=session.name, eventCount=len(events), errorCount=0,
    created=dict(sequence=created[0][0]["sequence"], values=created[0][1]),
    rows=[dict(sequence=e["sequence"], values=v) for e, v in rows], verified=dict(sequence=verified[0][0]["sequence"], values=final),
    snapshotFiles=[dict(path=p.relative_to(root).as_posix(), sha256=sha(p)) for p in sorted(snapshot.iterdir()) if p.is_file()],
    installedFiles=installed,
    boundary="New ordinary metadata graph, native getters/Color/GC roots only. No LOCAL launch/retry/result path or complete B15 acceptance; owner records are not native heap measurements.")
(root / "runtime-proof.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(sessionId=session.name, events=len(events), rows=len(rows), rootRecords=created[0][1]["ownerRootRecords"], verified=final), ensure_ascii=False))
