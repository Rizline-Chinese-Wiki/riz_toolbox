"""Create a review diff between the two frozen input archives, never against HEAD."""
import difflib
import hashlib
import json
import zipfile
from pathlib import Path

root = Path(__file__).resolve().parent
previous = root.parent / "foundation-initialization-native-unwind-20261023"

def frozen(batch):
    proof = json.loads((batch / "source-archive-check.json").read_text(encoding="utf-8-sig"))
    archive = batch / "source-inputs.zip"
    assert hashlib.sha256(archive.read_bytes()).hexdigest() == proof["archiveSha256"]
    manifest = json.loads((batch / "source-manifest.json").read_text(encoding="utf-8-sig"))["files"]
    with zipfile.ZipFile(archive) as opened:
        assert set(opened.namelist()) == {item["path"] for item in manifest}
        files = {item["path"]: opened.read(item["path"]) for item in manifest}
    assert all(hashlib.sha256(files[item["path"]]).hexdigest() == item["sha256"] for item in manifest)
    return files

before, after = frozen(previous), frozen(root)
changes, review = [], []
for path in sorted(before.keys() | after.keys()):
    if before.get(path) == after.get(path):
        continue
    changes.append(dict(path=path, change="added" if path not in before else "removed" if path not in after else "modified"))
    old = before.get(path, b"").decode("utf-8-sig").splitlines(keepends=True)
    new = after.get(path, b"").decode("utf-8-sig").splitlines(keepends=True)
    review.extend(difflib.unified_diff(old, new, fromfile="a/" + path if path in before else "/dev/null",
                                       tofile="b/" + path if path in after else "/dev/null"))
(root / "changes-from-1216.diff").write_text("".join(review), encoding="utf-8")
proof = dict(previousBatch=previous.name, beforeFiles=len(before), afterFiles=len(after),
             changes=changes, changedFiles=len(changes), scope="Frozen declared source archives only; no game installation or HEAD comparison.")
(root / "changes-from-1216.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(beforeFiles=len(before), afterFiles=len(after), changedFiles=len(changes))))
