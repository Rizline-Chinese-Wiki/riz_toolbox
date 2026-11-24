"""Verify and freeze the finite static scene evidence; no process/game operations."""
import hashlib
import json
import zipfile
from pathlib import Path
import pefile

root = Path(__file__).resolve().parent
scene = root.parent / "local-launch-scene-transition-20261023"
evidence = json.loads((scene / "evidence.json").read_text(encoding="utf-8-sig"))
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline")
raw = (game / "GameAssembly.dll").read_bytes()
digest = lambda body: hashlib.sha256(body).hexdigest()
assert digest(raw) == evidence["inputs"]["gameAssemblySha256"]
assert evidence["runtimeAcceptance"] is False and len(evidence["ranges"]) == 7
pe = pefile.PE(data=raw, fast_load=True)
checks = []
for scope in evidence["ranges"]:
    start, end = int(scope["start"], 16), int(scope["end"], 16)
    offset = pe.get_offset_from_rva(start)
    assert 0 < end - start <= 16384
    assert digest(raw[offset:offset + end - start]) == scope["sha256"]
    for instruction in scope["instructions"]:
        rva = int(instruction["rva"], 16)
        body = bytes.fromhex(instruction["bytes"])
        assert start <= rva < rva + len(body) <= end
        pos = pe.get_offset_from_rva(rva)
        assert raw[pos:pos + len(body)] == body
    checks.append(dict(name=scope["name"], bytes=end-start, instructions=len(scope["instructions"]), matched=True))
files = sorted(path for path in scene.iterdir() if path.is_file())
manifest = [dict(path=path.name, sha256=digest(path.read_bytes())) for path in files]
archive = root / "scene-static-evidence-complete.zip"
if not archive.exists():
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as opened:
        for path in files:
            opened.write(path, path.name)
with zipfile.ZipFile(archive) as opened:
    assert set(opened.namelist()) == {item["path"] for item in manifest}
    assert opened.testzip() is None
    for item in manifest:
        assert digest(opened.read(item["path"])) == item["sha256"]
proof = dict(evidenceSha256=digest((scene / "evidence.json").read_bytes()), ranges=checks,
    archive=archive.name, archiveSha256=digest(archive.read_bytes()), files=manifest,
    runtimeAcceptance=False, scope="Seven finite disk-code ranges/instruction bytes and the static evidence archive only; no native invocation or actual scene transition.")
(root / "scene-static-check.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(ranges=len(checks), instructions=sum(item["instructions"] for item in checks), archiveSha256=proof["archiveSha256"])))
