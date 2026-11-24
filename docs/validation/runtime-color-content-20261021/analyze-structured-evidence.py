"""Verify hashes and parse a frozen JSONL without touching the game."""
from pathlib import Path
from datetime import datetime, timezone
import collections
import hashlib
import json
import sys

root = Path(sys.argv[1]).resolve()
manifest = json.loads((root / "evidence-manifest.json").read_text(encoding="utf-8"))
hash_checks = [{"name": f["name"], "matches": hashlib.sha256((root / f["name"]).read_bytes()).hexdigest() == f["sha256"]}
               for f in manifest["files"] if "sha256" in f]
source_metadata = []
for f in manifest["files"]:
    source = Path(f["source"])
    if source.exists() and "sha256" in f:
        source_metadata.append({"name": f["name"], "source": str(source),
            "observedAtUtc": datetime.now(timezone.utc).isoformat(),
            "sourceLastWriteUtc": datetime.fromtimestamp(source.stat().st_mtime, timezone.utc).isoformat(),
            "currentSourceMatchesFrozen": hashlib.sha256(source.read_bytes()).hexdigest() == f["sha256"]})
rows = []
parse_errors = []
for i, line in enumerate((root / "events-000001.jsonl").read_text(encoding="utf-8-sig").splitlines(), 1):
    try:
        rows.append((i, json.loads(line)))
    except ValueError as e:
        parse_errors.append({"line": i, "error": str(e)})
session = "20261021T044121502Z-991c2e0274194ba9838a0e3aa1f1b476"
sequences = [r["sequence"] for _, r in rows]
result = {
    "analyzedAtUtc": datetime.now(timezone.utc).isoformat(), "records": len(rows),
    "parseErrors": parse_errors, "hashChecks": hash_checks, "sourceMetadataAfterExit": source_metadata,
    "sessionIds": dict(collections.Counter(r["sessionId"] for _, r in rows)),
    "allMatchExpectedSession": all(r["sessionId"] == session for _, r in rows),
    "sequenceFirst": min(sequences) if sequences else None,
    "sequenceLast": max(sequences) if sequences else None,
    "sequencesStrictlyIncreasing": all(a < b for a, b in zip(sequences, sequences[1:])),
    "sequenceGapCount": sum(b-a-1 for a, b in zip(sequences, sequences[1:])),
    "firstTimestamp": rows[0][1]["timestamp"] if rows else None,
    "lastTimestamp": rows[-1][1]["timestamp"] if rows else None,
    "levelCounts": dict(collections.Counter(r["level"] for _, r in rows)),
    "notableEvents": [{"jsonlLine": i, **r} for i, r in rows if any(marker in r.get("message", "") for marker in [
        "CONTROL RECEIPT", "hold.end\"", "Observed lifecycle RunEnded", "Observed lifecycle Resumed", "Visual cleanup pending", "MaterialProbe CLONE RELEASED"])],
}
(root / "structured-summary.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
summary = json.loads((root / "summary.json").read_text(encoding="utf-8"))
summary["boundaries"][0] = "The structured diagnostic JSONL was buffered while gameplay was live; after normal exit the frozen file contains validated per-session events. See structured-summary.json."
summary["structuredEvidence"] = {k: v for k, v in result.items() if k != "notableEvents"}
(root / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({k: v for k, v in result.items() if k != "notableEvents"}, ensure_ascii=False, indent=2))
