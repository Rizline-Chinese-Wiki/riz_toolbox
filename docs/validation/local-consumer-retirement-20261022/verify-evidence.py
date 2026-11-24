"""Verify frozen source references and finite static instruction anchors.

No native execution, game launch, process observation or consumer-end token issuance.
The first --capture-sources creates an immutable baseline; later checks use that ZIP,
because future implementation changes must not rewrite the earlier audit's evidence.
"""
import argparse
import hashlib
import json
import zipfile
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--capture-sources", action="store_true")
args = parser.parse_args()
root = Path(__file__).resolve().parent
repo = root.parents[2]
expected = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"


def sha(body):
    return hashlib.sha256(body).hexdigest()


paths = ["src/RizToolbox.Adapter/AudioContent/" + name + ".cs" for name in [
    "LocalRunSessionRegistry", "NativeControllerGlobalsBackend", "OwnedNativeControllerGlobals",
    "NativeInactiveControllerFactory", "NativeControllerAwakeLedger", "LocalRunProtectionHost"]]
paths += [path.relative_to(repo).as_posix() for path in sorted((repo / "src/RizToolbox.Runtime/Hooks").glob("ExactHookBroker*.cs"))]
paths += ["docs/validation/" + name for name in [
    "local-launch-20261022/disassembly.txt", "local-launch-20261022/metadata-excerpts.txt", "local-launch-20261022/triage.json",
    "local-metadata-bridge-20261022/disassembly.txt", "local-provider-catalog-20261021/disassembly.txt"]]
manifest_file = root / "source-manifest.json"
archive_file = root / "source-snapshot.zip"
if args.capture_sources:
    assert not manifest_file.exists() and not archive_file.exists(), "Do not replace a frozen audit baseline."
    records = [dict(path=path, sha256=sha((repo / path).read_bytes())) for path in paths]
    with zipfile.ZipFile(archive_file, "x", zipfile.ZIP_DEFLATED) as archive:
        for path in paths:
            archive.write(repo / path, path)
    manifest_file.write_text(json.dumps(dict(files=records, archiveSha256=sha(archive_file.read_bytes()),
        scope="Static audit source baseline only; future working-tree changes are separate."), indent=2) + "\n", encoding="utf-8")
manifest = json.loads(manifest_file.read_text(encoding="utf-8"))
assert sha(archive_file.read_bytes()) == manifest["archiveSha256"]
with zipfile.ZipFile(archive_file) as archive:
    assert archive.testzip() is None and set(archive.namelist()) == {entry["path"] for entry in manifest["files"]}
    for entry in manifest["files"]:
        assert sha(archive.read(entry["path"])) == entry["sha256"], entry["path"]

static = json.loads((root / "retirement-boundaries.json").read_text(encoding="utf-8"))
assert static["gameAssemblySha256"] == expected and static["runtimeAcceptance"] is False
scopes = {scope["name"]: scope for scope in static["scopes"]}
assert set(scopes) == {"SceneManager.ClearDontDestroyOnLoadObject", "NormalMode.LevelOver.staticDelayedCallback"}
for scope in scopes.values():
    body = b"".join(bytes.fromhex(instruction["bytes"]) for instruction in scope["instructions"])
    assert sha(body) == scope["rangeSha256"]
    assert len(body) == int(scope["end"], 16) - int(scope["start"], 16)
callback = scopes["NormalMode.LevelOver.staticDelayedCallback"]["instructions"]
calls = {int(row["rva"], 16): (row["mnemonic"], row["operands"]) for row in callback}
assert calls[0x3C7EB4] == ("call", "0x3827e0") and calls[0x3C7F1C] == ("jmp", "0x38b350")
assert any(row["rva"] == "0x4017e8" and row["mnemonic"] == "call" for row in scopes["SceneManager.ClearDontDestroyOnLoadObject"]["instructions"])
proof = dict(runtimeAcceptance=False, sourceReferences=len(manifest["files"]),
    staticRanges=len(scopes), bytesChecked=sum(int(scope["end"], 16) - int(scope["start"], 16) for scope in scopes.values()),
    sourceArchiveSha256=manifest["archiveSha256"],
    scope="Frozen-source/static-byte verification only. No native consumer retirement was performed or proven.")
(root / "evidence-check.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(proof))
