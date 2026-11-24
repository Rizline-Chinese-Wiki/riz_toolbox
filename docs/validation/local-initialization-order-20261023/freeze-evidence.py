"""Record exact case-local evidence and scripts after successful verification."""
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parent
files = []
for path in sorted(root.iterdir()):
    if not path.is_file() or path.name == "evidence-manifest.json":
        continue
    files.append({"path": path.name, "bytes": path.stat().st_size,
                  "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
(root / "evidence-manifest.json").write_text(json.dumps({
    "schemaVersion": 1, "runtimeAcceptance": False, "mode": "finite-offline-file-evidence",
    "files": files,
}, indent=2) + "\n", encoding="utf8")
print(json.dumps({"frozenFiles": len(files), "runtimeAcceptance": False}))
