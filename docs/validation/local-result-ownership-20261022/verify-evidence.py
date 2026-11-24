"""Verify finite static ranges and key termination/Timeline instruction anchors.

No process opening, native execution, game launch or dynamic ownership claim.
"""
import hashlib
import json
from pathlib import Path
import pefile

root = Path(__file__).resolve().parent
doc = json.loads((root / "boundaries.json").read_text(encoding="utf-8"))
assert doc["runtimeAcceptance"] is False
assert doc["gameAssemblySha256"] == "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
scopes = {scope["name"]: scope for scope in doc["scopes"]}
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll")
assert hashlib.sha256(game.read_bytes()).hexdigest() == doc["gameAssemblySha256"]
image = pefile.PE(str(game), fast_load=True)
for scope in scopes.values():
    start, end = int(scope["start"], 16), int(scope["end"], 16)
    body = image.get_data(start, end - start)
    assert hashlib.sha256(body).hexdigest() == scope["rangeSha256"], scope["name"]
    assert len(body) == end - start
    # Some finite PE ranges include noninstruction tail bytes. Verify every decoded
    # instruction against the actual range without treating undecoded padding as code.
    for instruction in scope["instructions"]:
        offset = int(instruction["rva"], 16) - start
        value = bytes.fromhex(instruction["bytes"])
        assert offset >= 0 and body[offset:offset + len(value)] == value
checks = []


def expect(scope, rva, mnemonic, operands):
    found = next(instruction for instruction in scopes[scope]["instructions"] if instruction["rva"] == hex(rva))
    assert (found["mnemonic"], found["operands"]) == (mnemonic, operands), found
    checks.append(dict(scope=scope, rva=hex(rva), mnemonic=mnemonic, operands=operands))


expect("PauseUI.Update", 0x3C449D, "call", "qword ptr [rax + 0x188]")
expect("NormalMode.Skip", 0x3C17DE, "jmp", "qword ptr [rax + 0x188]")
expect("NormalMode.LevelOverCoroutine.MoveNext", 0x3C5F02, "call", "0x402170")
expect("Progress.Update", 0x4014B4, "call", "qword ptr [rax + 0x18]")
expect("Progress.Update", 0x4014DB, "call", "0x48a950")
expect("Progress.Update", 0x40157A, "call", "0x48a3e0")
expect("Progress.Update", 0x4015AA, "call", "0x48a730")
expect("Progress.Play", 0x400795, "jmp", "0x48a950")
expect("Progress.Play", 0x4007B5, "jmp", "0x48a950")
assert int(scopes["NormalMode.LevelOver"]["start"], 16) == 0x3C0C00
assert int(scopes["Progress.Update"]["start"], 16) == 0x400FE0
proof = dict(runtimeAcceptance=False, staticRanges=len(scopes), anchors=checks,
    boundariesSha256=hashlib.sha256((root / "boundaries.json").read_bytes()).hexdigest(),
    scope="Static byte/anchor consistency only. Exact LOCAL ownership, around/static ABI gates and native result/save/Steam isolation remain unverified.")
(root / "evidence-check.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(staticRanges=len(scopes), anchors=len(checks), runtimeAcceptance=False)))
