"""Freeze the exact seven inputs of the completed independent ABI execution."""
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parent / "independent-native-abi"
manifest = json.loads((root / "fixture-manifest.json").read_text(encoding="utf-8-sig"))
assert len(manifest["files"]) == 7
destination = root / "inputs"
destination.mkdir(exist_ok=True)
files = []
for item in manifest["files"]:
    source = Path(item["path"])
    data = source.read_bytes()
    assert hashlib.sha256(data).hexdigest() == item["sha256"]
    target = destination / source.name
    assert target.parent == destination
    if target.exists():
        assert target.read_bytes() == data
    else:
        target.write_bytes(data)
    files.append(dict(path=target.relative_to(root).as_posix(), sha256=item["sha256"]))
assert len({item["path"] for item in files}) == 7
proof = dict(kind="archived-independent-executed-inputs", files=files,
             allMatched=True, gameRuntimeVerified=False)
(root / "evidence-integrity.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(archivedExecutedInputs=len(files), allMatched=True)))
