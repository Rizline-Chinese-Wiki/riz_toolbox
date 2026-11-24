"""Retire only this session's generated fixtures and marker after verified process stop.

Run from the repository root after freeze-proof.py. Original fixture bytes and hashes
are copied into this evidence directory before their exact owned files are removed.
No recursive deletion, other marker change, game launch or input is performed.
"""
import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline").resolve()
probes = game / "BepInEx/RizToolbox/probes"
proof = json.loads((root / "runtime-proof.json").read_text(encoding="utf-8"))
stop = json.loads((root / "process-stop-check.json").read_text(encoding="utf-8-sig"))
assert stop["running"] is False
before = json.loads((root / "markers-before.json").read_text(encoding="utf-8-sig"))
assert all(item["exists"] is False for item in before["files"])
marker = probes / "local-native-bundle.enabled"
expected = b"owned bundle validation 20261022"
assert marker.resolve().parent == probes.resolve() and marker.read_bytes() == expected
fixture = Path(proof["phases"]["fixture-directory"]["values"]["path"]).resolve()
assert fixture.parent == probes.resolve() and fixture.name.startswith("bundle-fixtures-")
names = {"tone.wav", "cover.png", "chart.json"}
assert {path.name for path in fixture.iterdir()} == names
assert all((fixture / name).is_file() and not (fixture / name).is_symlink() for name in names)
destination = root / proof["sessionId"] / "original-test-fixtures"
destination.mkdir(exist_ok=True)
records = []
for name in sorted(names):
    source, target = fixture / name, destination / name
    body = source.read_bytes()
    assert not target.exists() or target.read_bytes() == body
    shutil.copy2(source, target)
    assert target.read_bytes() == body
    records.append(dict(name=name, size=len(body), sha256=hashlib.sha256(body).hexdigest(),
                        frozenPath=target.relative_to(root).as_posix()))
for name in sorted(names):
    (fixture / name).unlink()
fixture.rmdir()
marker.unlink()
states = [dict(name=item["name"], exists=(probes / item["name"]).exists()) for item in before["files"]]
assert all(item["exists"] is False for item in states)
record = dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(), markerOriginalState="Absent",
              markerRestored=True, markers=states, fixtureDirectory=str(fixture), fixtureDirectoryRemoved=not fixture.exists(),
              removedOnlyOriginalTestFiles=records, originalFixtureBytesFrozen=True)
(root / "probe-restoration-check.json").write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(markerRestored=True, ownedFixtureFilesRemoved=len(records), otherOptInMarkersAbsent=True)))
