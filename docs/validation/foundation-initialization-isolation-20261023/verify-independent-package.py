"""Tie the independently executed native ABI runner to this frozen staged package."""
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent
repo = root.parents[2]
evidence = root / "independent-native-abi"

def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

result = read(evidence / "fixture-result.json")
assert result["passed"] and result["realDobby"] and result["nativeWorker"] and not result["gameRuntimeVerified"]
executed = read(evidence / "fixture-manifest.json")["files"]
assert len(executed) == 6 and all(sha(Path(item["path"])) == item["sha256"] for item in executed)
by_name = {Path(item["path"]).name: item for item in executed}
package = read(root / "package-manifest.json")["files"]
for name in ["RizToolbox.Runtime.dll", "RizToolbox.Sdk.dll"]:
    frozen = next(item for item in package if Path(item["path"]).name == name)
    assert frozen["sha256"] == by_name[name]["sha256"]
    assert sha(repo / "artifacts/package" / frozen["path"]) == frozen["sha256"]
test = next(item for item in read(root / "source-manifest.json")["files"]
            if item["path"].endswith("/NativeInitializationAbiFixtureRegression.cs"))
assert test["sha256"] == by_name["NativeInitializationAbiFixtureRegression.cs"]["sha256"]
native = root / "native-build/initialization-abi/RizToolbox.InitializationAbiFixture.dll"
assert sha(native) == by_name[native.name]["sha256"]
proof = dict(verifiedAtUtc=datetime.now(timezone.utc).isoformat(), executedFilesChecked=len(executed),
             stagedAssembliesMatched=["RizToolbox.Runtime.dll", "RizToolbox.Sdk.dll"],
             frozenTestSourceMatched=True, archivedNativeFixtureMatched=True, gameRuntimeVerified=False,
             runtimeSha256=by_name["RizToolbox.Runtime.dll"]["sha256"],
             scope="The independent native ABI fixture ran against these exact staged assemblies. This does not prove game metadata, activation, native-original exception unwind or a live LOCAL run.")
(evidence / "package-match.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(proof))
