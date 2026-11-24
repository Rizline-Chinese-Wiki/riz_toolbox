"""Compare these two frozen source archives without changing either build."""
import difflib
import hashlib
import json
import zipfile
from pathlib import Path

root = Path(__file__).resolve().parent
previous = root.parent / "foundation-operation-preinit-20261024"


def inputs(batch):
    proof = json.loads((batch / "source-archive-check.json").read_text(encoding="utf-8-sig"))
    manifest_path = batch / "source-manifest.json"
    archive_path = batch / "source-inputs.zip"
    assert hashlib.sha256(manifest_path.read_bytes()).hexdigest() == proof["manifestSha256"]
    assert hashlib.sha256(archive_path.read_bytes()).hexdigest() == proof["archiveSha256"]
    manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))["files"]
    with zipfile.ZipFile(archive_path) as archive:
        assert archive.testzip() is None
        assert set(archive.namelist()) == {entry["path"] for entry in manifest}
        contents = {entry["path"]: archive.read(entry["path"]) for entry in manifest}
    assert all(hashlib.sha256(contents[e["path"]]).hexdigest() == e["sha256"] for e in manifest)
    return contents


before, after = inputs(previous), inputs(root)
added = sorted(set(after) - set(before))
changed = sorted(p for p in set(before) & set(after) if before[p] != after[p])
removed = sorted(set(before) - set(after))
assert added and not removed
lines = []
for name in sorted(set(added + changed + removed)):
    lines.extend(difflib.unified_diff(before.get(name, b"").decode("utf-8-sig").splitlines(keepends=True),
        after.get(name, b"").decode("utf-8-sig").splitlines(keepends=True),
        fromfile="operation-preinit/" + name, tofile="engine-node/" + name))
(root / "changes-from-operation-preinit.diff").write_text("".join(lines), encoding="utf-8")
report = dict(previous=previous.name, current=root.name, previousInputs=len(before), currentInputs=len(after),
    added=added, changed=changed, removed=removed, bothFrozenArchivesVerified=True)
(root / "changes-from-operation-preinit.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(added=len(added), changed=len(changed), removed=len(removed), bothArchivesVerified=True)))
