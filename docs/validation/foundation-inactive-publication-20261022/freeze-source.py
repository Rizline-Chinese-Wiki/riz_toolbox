"""Archive only this completed build's exact declared source inputs for later reproduction.

This reads repository files and writes an evidence ZIP. It does not change source, install,
launch a game, or claim that cached dependencies and all release matrices were reproduced.
"""
import hashlib
import json
import zipfile
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent
repo = root.parents[2]
manifest_file = root / "source-manifest.json"
manifest = json.loads(manifest_file.read_text(encoding="utf-8-sig"))
entries = manifest["files"]
assert 0 < len(entries) <= 8192 and len({entry["path"] for entry in entries}) == len(entries)
total = 0
for entry in entries:
    path = (repo / entry["path"]).resolve()
    assert path.is_relative_to(repo) and path.is_file()
    body = path.read_bytes()
    assert hashlib.sha256(body).hexdigest() == entry["sha256"], entry["path"]
    total += len(body)
assert total <= 128 * 1024 * 1024
destination = root / "source-inputs.zip"
if not destination.exists():
    pending = root / "source-inputs.pending.zip"
    assert pending.resolve().parent == root and not pending.exists()
    with zipfile.ZipFile(pending, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for entry in entries:
            archive.write(repo / entry["path"], entry["path"])
    pending.replace(destination)
with zipfile.ZipFile(destination) as archive:
    assert set(archive.namelist()) == {entry["path"] for entry in entries}
    assert archive.testzip() is None
    for entry in entries:
        assert hashlib.sha256(archive.read(entry["path"])).hexdigest() == entry["sha256"]
        assert hashlib.sha256((repo / entry["path"]).read_bytes()).hexdigest() == entry["sha256"]
proof = dict(verifiedAtUtc=datetime.now(timezone.utc).isoformat(), entries=len(entries), uncompressedBytes=total,
    archive="source-inputs.zip", archiveSha256=hashlib.sha256(destination.read_bytes()).hexdigest(),
    manifestSha256=hashlib.sha256(manifest_file.read_bytes()).hexdigest(), allEntryHashesMatched=True,
    scope="Exact declared source inputs for this build only. Game binaries, cached dependencies, toolchains, successful clean reproduction, and the complete B19 release matrix are separate evidence.")
(root / "source-archive-check.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(proof))
