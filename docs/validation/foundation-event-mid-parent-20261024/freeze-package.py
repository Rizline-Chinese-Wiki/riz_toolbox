"""Freeze the declared staged files without installing or operating the game."""
import hashlib
import json
import zipfile
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent
repo = root.parents[2]
package = (repo / "artifacts/package").resolve()
manifest = json.loads((root / "package-manifest.json").read_text(encoding="utf-8-sig"))
entries = manifest["files"]
assert len(entries) == 18 and len({entry["path"] for entry in entries}) == 18
total = 0
for entry in entries:
    source = (package / entry["path"]).resolve()
    assert source.is_relative_to(package) and source.is_file()
    body = source.read_bytes()
    assert hashlib.sha256(body).hexdigest() == entry["sha256"]
    total += len(body)
assert total <= 128 * 1024 * 1024
destination = root / "staged-package.zip"
if not destination.exists():
    pending = root / "staged-package.pending.zip"
    assert not pending.exists()
    with zipfile.ZipFile(pending, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for entry in entries:
            archive.write(package / entry["path"], entry["path"])
    pending.replace(destination)
with zipfile.ZipFile(destination) as archive:
    assert archive.testzip() is None and set(archive.namelist()) == {entry["path"] for entry in entries}
    for entry in entries:
        assert hashlib.sha256(archive.read(entry["path"])).hexdigest() == entry["sha256"]
proof = dict(verifiedAtUtc=datetime.now(timezone.utc).isoformat(), entries=len(entries), bytes=total,
    archive=destination.name, archiveSha256=hashlib.sha256(destination.read_bytes()).hexdigest(),
    allEntryHashesMatched=True, installed=False,
    scope="The 18 declared staged package files only; loader dependencies and game runtime acceptance are separate evidence.")
(root / "package-archive-check.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(proof))
