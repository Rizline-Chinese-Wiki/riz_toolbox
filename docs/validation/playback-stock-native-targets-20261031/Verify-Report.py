"""Read-only fixity/result verification; never execute native images."""
import hashlib
import json
import struct
from pathlib import Path

CASE = Path(__file__).resolve().parent


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def verify_original(item):
    length = item["movedLength"]
    data = bytes.fromhex(item["actualDobbyOriginalHex"])
    target = int(item["target"], 16)
    assert length == (7 if item["role"] == 7 else 6) and len(data) == length + 14
    prefixes = {1: "40534883ec30", 2: "40534883ec20", 3: "40534883ec40", 4: "40574883ec50",
                5: "40534883ec30", 6: "48895c240857", 7: "4883ec28385142", 8: "40534883ec20"}
    assert data[:length].hex() == prefixes[item["role"]]
    assert data[length:length+6] == bytes.fromhex("ff2500000000")
    assert struct.unpack("<Q", data[length+6:])[0] == target + length == int(item["continuation"], 16)
    assert item["originalUnwindProbes"] == 4
    assert item["targetUnwindProbes"] == {1: 4, 2: 4, 3: 11, 4: 14, 5: 7, 6: 6, 7: 9, 8: 4}[item["role"]]


def main():
    index_path = CASE / "artifact-index.json"
    index = read(index_path)
    assert index["selfHashExcluded"]
    for item in index["files"]:
        path = Path(item["path"])
        assert path.resolve() != index_path.resolve()
        assert path.stat().st_size == item["bytes"] and sha(path) == item["sha256"], path
    for item in index["nativeDirectories"]:
        actual = {str(path.resolve()).casefold() for path in Path(item["path"]).rglob("*") if path.is_file()}
        assert actual == {path.casefold() for path in item["files"]}, item["path"]
    owned = read(CASE / "native-owned-inputs.json")
    assert len(owned["files"]) == 10 and not owned["contextAbiChanged"]
    for item in owned["files"]:
        assert sha(Path(item["path"])) == sha(CASE / item["snapshot"]) == item["sha256"]
    checks = read(CASE / "evidence/r14/checks/test-result.json")
    assert checks["passed"] and checks["processes"] == 14 and checks["checks"] == 36675
    assert checks["inputsUnchanged"] and checks["frozenInputsUnchanged"]
    for row in checks["rows"]:
        assert row["passed"] and row["actualExitCode"] == 0
        mode = row["mode"]
        actual = json.loads((CASE / f"evidence/r14/checks/{mode}.stdout.log").read_text().splitlines()[-1])
        assert actual == row["result"] and actual["passed"] and actual["failures"] == 0
        assert (CASE / f"evidence/r14/checks/{mode}.exit.txt").read_text() == "0"
        assert (CASE / f"evidence/r14/checks/{mode}.stderr.log").stat().st_size == 0
        assert actual["actualDobbyPrepared"] == actual["actualDobbyCommitted"] == 8
        assert actual["nativeEntered"] == actual["nativeExited"] and actual["nativeInFlight"] == 0
        assert not actual["gameCodeExecuted"] and not actual["productionAdmission"]
        assert len(actual["targets"]) == 8
        for item in actual["targets"]:
            verify_original(item)
        assert sum(item["originalUnwindProbes"] for item in actual["targets"]) == 32
        assert sum(item["targetUnwindProbes"] for item in actual["targets"]) == 59
        assert int(actual["fixtureLoadedBase"], 16) != int(actual["fixturePreferredBase"], 16)
        pitch = next(item for item in actual["targets"] if item["role"] == 1)
        assert int(actual["fixtureDir64Value"], 16) == int(pitch["target"], 16)
    assert sum(row["result"]["checks"] for row in checks["rows"]) == checks["checks"]
    target_mode = next(row["result"] for row in checks["rows"] if row["mode"] == "target-specific")
    assert target_mode["checks"] == 2579 and target_mode["nativeEntered"] == 66 and target_mode["abnormal"] == 11
    proof = read(CASE / "evidence/r14/build/pe-pdb-source-verification.json")
    assert proof["passed"] and len(proof["images"]) == 3 and len(proof["compiledObjects"]) == 5
    assert len(proof["stockTargets"]) == 9
    assert proof["executableDir64"]["baseRelocationTypeValue"] == 10
    assert proof["executableDir64"]["coffRelocationTypeValue"] == 1
    target_proof = read(CASE / "target-proof.json")
    assert target_proof["fixtureReplayResumeProgressRfCounts"] == [3, 3, 3]
    assert target_proof["gameReplayResumeProgressRfCounts"] == [5, 5, 4]
    assert not target_proof["gameCodeExecuted"] and not target_proof["gameDllMainExecuted"]
    for item in target_proof["targets"]:
        verify_original(item["fixtureActualDobby"])
        assert not item["gameOriginalOrDllMainExecuted"] and item["gameActualDobbyPrepareCount"] == 0
        assert not item["fullFixtureBodyIsProductionEquivalent"]
    graph = read(CASE / "evidence-index.json")
    evidence_ids = {item["id"] for item in graph["evidence"]}
    for item in graph["evidence"]:
        artifact = Path(item["artifact_path"])
        if not artifact.is_absolute():
            artifact = CASE / artifact
        assert sha(artifact) == item["content_hash"]
    for item in graph["findings"]:
        assert len(item["evidence_ids"]) >= 2 and set(item["evidence_ids"]) <= evidence_ids
    for item in graph["paths"]:
        assert item["path_type"] == "callflow" and item["residual_risks"]
        for step in item["steps"]:
            assert set(step["evidence_ids"]) <= evidence_ids
    integrity_path = CASE / "report-integrity.json"
    integrity = read(integrity_path)
    assert integrity["selfHashExcluded"]
    for item in integrity["files"]:
        path = Path(item["path"])
        assert path.resolve() != integrity_path.resolve()
        assert path.stat().st_size == item["bytes"] and sha(path) == item["sha256"], path
    print(json.dumps({"passed": True, "hashedArtifacts": len(index["files"]), "nativeDirectories": len(index["nativeDirectories"]),
          "ownedSources": 10, "processes": 14, "checks": 36675, "actualDobbyPrepare": 112, "actualDobbyCommit": 112,
          "nativePePdbPairs": 3, "actualCompiledObjects": 5, "fixtureTargets": 9,
          "originalOsBoundaryProbesPerProcess": 32, "sourceOsBoundaryProbesPerProcess": 59,
          "artifactIndexSha256": sha(index_path), "reportIntegritySha256": sha(integrity_path),
          "gameCodeExecuted": False, "gameDllMainExecuted": False, "productionAdmission": False}))


if __name__ == "__main__":
    main()
