"""Compare pre-build declared inputs, archived manifest and current input bytes."""
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent
repo = root.parents[2]
before = json.loads((root / "build-inputs-before.json").read_text(encoding="utf-8-sig"))["files"]
after = json.loads((root / "source-manifest.json").read_text(encoding="utf-8-sig"))["files"]
assert before == after, "Declared build inputs changed while the root build was running."
changed = [row["path"] for row in before if hashlib.sha256((repo / row["path"]).read_bytes()).hexdigest() != row["sha256"]]
proof = dict(checkedAtUtc=datetime.now(timezone.utc).isoformat(), checked=len(before),
    preBuildManifestMatchesArchivedManifest=True, currentChanged=changed,
    scope="Declared repository build inputs only; external dependencies, toolchains and game acceptance remain separate evidence.")
(root / "build-input-integrity-check.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
assert not changed, changed
print(json.dumps(proof))
