"""Read-only fixity and result checks for the native r6 report package."""
import hashlib
import json
from pathlib import Path

CASE = Path(__file__).resolve().parent


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def main():
    index_path = CASE / "artifact-index.json"
    index = read(index_path)
    assert index["selfHashExcluded"]
    assert not any(Path(item["path"]).resolve() == index_path for item in index["files"])
    for item in index["files"]:
        path = Path(item["path"])
        assert path.stat().st_size == item["bytes"] and sha(path) == item["sha256"], path
    for item in index["nativeDirectories"]:
        actual = {str(path.resolve()).casefold() for path in Path(item["path"]).rglob("*") if path.is_file()}
        assert actual == {path.casefold() for path in item["files"]}, item["path"]
    owned = read(CASE / "native-owned-inputs.json")
    assert len(owned["files"]) == 10
    for item in owned["files"]:
        assert sha(Path(item["path"])) == sha(CASE / item["snapshot"]) == item["sha256"]
    checks = read(CASE / "evidence/r6/checks/test-result.json")
    assert checks["passed"] and checks["processes"] == 13 and checks["checks"] == 27037
    assert checks["inputsUnchanged"] and checks["frozenInputsUnchanged"]
    for item in checks["rows"]:
        mode = item["mode"]
        assert item["passed"] and item["actualExitCode"] == 0
        assert (CASE / f"evidence/r6/checks/{mode}.exit.txt").read_text() == "0"
        assert (CASE / f"evidence/r6/checks/{mode}.stderr.log").stat().st_size == 0
        actual = json.loads((CASE / f"evidence/r6/checks/{mode}.stdout.log").read_text().splitlines()[-1])
        assert actual == item["result"] and actual["passed"] and actual["failures"] == 0
        assert actual["actualDobbyPrepared"] == actual["actualDobbyCommitted"] == 8
        loaded, preferred, anchor, value = [int(actual[name], 16) for name in
            ["fixtureLoadedBase", "fixturePreferredBase", "fixtureDir64Rva", "fixtureDir64Value"]]
        assert loaded != preferred and anchor == 0x1e70 and value == loaded + 0x19b0
        assert actual["nativeEntered"] == actual["nativeExited"] and actual["nativeInFlight"] == 0
        assert actual["gameCodeExecuted"] is False and actual["productionAdmission"] is False
    proof = read(CASE / "evidence/r6/build/native-symbol-source-proof.json")
    assert proof["passed"] and len(proof["images"]) == 3 and len(proof["compiledObjects"]) == 5
    assert proof["executableDir64"]["baseRelocationTypeValue"] == 10
    assert proof["executableDir64"]["coffRelocationTypeValue"] == 1
    assert proof["executableDir64"]["coffSection"] == ".text$mn"
    evidence = read(CASE / "evidence-index.json")
    for item in evidence["evidence"]:
        assert sha(CASE / item["artifact_path"]) == item["content_hash"], item["id"]
    evidence_ids = {item["id"] for item in evidence["evidence"]}
    for item in evidence["findings"]:
        assert set(item["evidence_ids"]) <= evidence_ids and len(item["evidence_ids"]) >= 2
    integrity_path = CASE / "native-report-integrity.json"
    integrity = read(integrity_path)
    assert integrity["selfHashExcluded"]
    assert not any(Path(item["path"]).resolve() == integrity_path for item in integrity["files"])
    for item in integrity["files"]:
        assert sha(Path(item["path"])) == item["sha256"], item["path"]
    print(json.dumps({"passed": True, "hashedArtifacts": len(index["files"]), "nativeDirectories": len(index["nativeDirectories"]),
        "ownedSources": 10, "processes": 13, "checks": 27037, "actualExitCodes": [item["actualExitCode"] for item in checks["rows"]],
        "nativePePdbPairs": 3, "actualCompiledObjects": 5, "executableDir64": True,
        "artifactIndexSha256": sha(index_path), "reportIntegritySha256": sha(integrity_path), "gameCodeExecuted": False,
        "productionAdmission": False}))


if __name__ == "__main__":
    main()
