"""Read-only verification of the original native report plus appended S2 handoff."""
import hashlib
import json
import subprocess
import sys
from pathlib import Path

CASE = Path(__file__).resolve().parent


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def check(item):
    path = Path(item["path"])
    assert path.stat().st_size == item["bytes"] and sha(path) == item["sha256"], path


def main():
    index_path = CASE / "artifact-index-r2.json"
    seal_path = CASE / "native-report-integrity-r2.json"
    index, seal = read(index_path), read(seal_path)
    assert index["selfHashExcluded"] and seal["selfHashExcluded"]
    check(index["previousArtifactIndex"])
    check(index["previousReportSeal"])
    check(seal["previousReportSeal"])
    assert not any(Path(item["path"]).resolve() == index_path for item in index["addedFiles"] + index["staticInputs"])
    assert not any(Path(item["path"]).resolve() == seal_path for item in seal["files"])
    for item in index["addedFiles"] + index["staticInputs"] + seal["files"]:
        check(item)
    evidence = read(CASE / "next-source-hooks-evidence.json")
    assert evidence["targetCount"] == 7 and len(evidence["targets"]) == 7
    assert evidence["actualDobbyPrepareCount"] == 0 and evidence["newDisassemblyPerformed"] is False
    assert all(item["candidateIsActualDobbyObservation"] is False and item["actualDobbyPreparePerformed"] is False
               for item in evidence["targets"])
    previous = subprocess.run([sys.executable, str(CASE / "Verify-Report.py")], capture_output=True, text=True, check=True)
    old_result = json.loads(previous.stdout)
    assert old_result["passed"] and old_result["reportIntegritySha256"] == index["previousReportSeal"]["sha256"]
    print(json.dumps({"passed": True, "previousNativeReport": old_result, "staticTargets": 7,
        "actualNewDobbyPrepareCount": 0, "nativeTestsRerun": False, "gameCodeExecuted": False,
        "artifactIndexR2Sha256": sha(index_path), "reportIntegrityR2Sha256": sha(seal_path)}))


if __name__ == "__main__":
    main()
