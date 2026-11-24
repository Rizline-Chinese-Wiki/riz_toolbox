"""Verify finite initialization evidence against fixed files and normal CFG.

This neither proves Unity scheduling nor follows indirect/native engine calls.
"""
import hashlib
import json
from pathlib import Path

import capstone
import pefile
import UnityPy

root = Path(__file__).resolve().parent
manifest_path = root / "evidence-manifest.json"
if manifest_path.exists():
    manifest = json.loads(manifest_path.read_text(encoding="utf8"))
    for record in manifest["files"]:
        item = root / record["path"]
        assert item.is_file() and hashlib.sha256(item.read_bytes()).hexdigest() == record["sha256"], record["path"]
provenance = json.loads((root / "extractor-provenance.json").read_text(encoding="utf8"))
assert hashlib.sha256(Path(provenance["baseExtractor"]).read_bytes()).hexdigest() == provenance["baseExtractorSha256"]
assert hashlib.sha256((root / "inspect-initialization.py").read_bytes()).hexdigest() == provenance["wrapperSha256"]
doc = json.loads((root / "boundaries.json").read_text(encoding="utf8"))
expected = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll")
metadata = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output")
assert doc["runtimeAcceptance"] is False and doc["gameAssemblySha256"] == expected
assert hashlib.sha256(game.read_bytes()).hexdigest() == expected
assert hashlib.sha256((metadata / "dump.cs").read_bytes()).hexdigest() == doc["dumpSha256"]
assert hashlib.sha256((metadata / "script.json").read_bytes()).hexdigest() == doc["scriptJsonSha256"]
pe = pefile.PE(str(game), fast_load=True)
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
scopes = {x["name"]: x for x in doc["scopes"]}
for scope in scopes.values():
    start, end = int(scope["start"], 16), int(scope["end"], 16)
    assert 0 < end - start <= (0x3000 if scope["name"] == "LevelControl.Start" else 0x1800)
    body = pe.get_data(start, end - start)
    assert hashlib.sha256(body).hexdigest() == scope["rangeSha256"]
    decoded = list(md.disasm(body, start))
    assert len(decoded) == len(scope["instructions"])
    for actual, captured in zip(decoded, scope["instructions"]):
        assert (hex(actual.address), actual.mnemonic, actual.op_str, actual.bytes.hex()) == (
            captured["rva"], captured["mnemonic"], captured["operands"], captured["bytes"])
triage = json.loads((root / "triage.json").read_text(encoding="utf8"))
assert triage["imports"] and triage["exports"] and triage["importCategories"]
assert triage["gameAssemblySha256"] == expected
checks = []


def expect(scope, rva, mnemonic, operands):
    row = next(x for x in scopes[scope]["instructions"] if x["rva"] == hex(rva))
    assert (row["mnemonic"], row["operands"]) == (mnemonic, operands), row
    checks.append({"scope": scope, **row})


expect("LevelControl.Start", 0x3EA5DB, "mov", "r12, rcx")
expect("LevelControl.Start", 0x3EA90E, "mov", "rax, qword ptr [rax + 0x38]")
expect("LevelControl.Start", 0x3EA91B, "mov", "rax, qword ptr [rax + 0x28]")
expect("LevelControl.Start", 0x3EA92C, "mov", "rdx, qword ptr [r12 + 0x20]")
expect("LevelControl.Start", 0x3EA935, "call", "qword ptr [rax + 0x18]")
expect("LevelControl.Start", 0x3ECDF0, "mov", "rax, qword ptr [r12 + 0x20]")
expect("LevelControl.Start", 0x3ECDFE, "mov", "byte ptr [rax + 0xbe], 1")
expect("LevelControl.Start", 0x3ECE0C, "mov", "ecx, ebx")
expect("LevelControl.Start", 0x3ECE0E, "call", "0x48a950")
expect("LevelControl.Start", 0x3ECE22, "xor", "r8d, r8d")
expect("LevelControl.Start", 0x3ECE25, "movaps", "xmm1, xmm12")
expect("LevelControl.Start", 0x3ECE30, "call", "0x48a980")
expect("LevelControl.Start", 0x3ECED7, "jmp", "0x3ece30")
expect("NormalMode.GetInsideLevelParams", 0x3BEF51, "mov", "qword ptr [rdi + 0x28], rbx")
expect("NormalMode.GetInsideLevelParams", 0x3BEF81, "mov", "rcx, qword ptr [rax + 0x28]")
expect("NormalMode.GetInsideLevelParams", 0x3BEF9B, "movss", "dword ptr [rcx + 0x44], xmm0")
expect("Progress.Update", 0x40108D, "mov", "rax, qword ptr [rbx + 0x20]")
expect("Progress.Update", 0x4010AE, "cmp", "byte ptr [rax + 0xbe], 0")
expect("Progress.Update", 0x4010B5, "je", "0x401617")
expect("Progress.Update", 0x401630, "ret", "")
expect("Progress.Update", 0x4014B4, "call", "qword ptr [rax + 0x18]")
expect("Progress.Update", 0x4014DB, "call", "0x48a950")
expect("Progress.Update", 0x40157A, "call", "0x48a3e0")
expect("Progress.Update", 0x4015AA, "call", "0x48a730")
expect("SteamTimeline.SetTimelineTooltip", 0x48A986, "mov", "rbx, rcx")
expect("SteamTimeline.SetTimelineTooltip", 0x48A990, "movaps", "xmm6, xmm1")
expect("SteamTimeline.SetTimelineTooltip", 0x48A9B3, "jmp", "0x44e9e0")
assert scopes["SteamTimeline.SetTimelineTooltip"]["symbols"] == [{
    "Address": 0x48A980,
    "Name": "Steamworks.SteamTimeline$$SetTimelineTooltip",
    "Signature": "System_Void Steamworks_SteamTimeline__SetTimelineTooltip (System_String description, System_Single timeOffsetSeconds, const MethodInfo* method);",
}]


def reachable(scope_name, cut=None):
    """Normal instruction edges only: calls return; exceptions are out of model."""
    scope = scopes[scope_name]
    rows = {int(x["rva"], 16): x for x in scope["instructions"]}
    pending = [int(scope["start"], 16)]
    seen = set()
    while pending:
        address = pending.pop()
        if address in seen or address == cut:
            continue
        assert address in rows, (scope_name, hex(address))
        seen.add(address)
        row = rows[address]
        mnemonic, operands = row["mnemonic"], row["operands"]
        if mnemonic.startswith("ret") or mnemonic in ("int3", "ud2"):
            continue
        if mnemonic.startswith("j") or mnemonic.startswith("loop"):
            assert operands.startswith("0x"), ("Unmodeled indirect transfer", row)
            pending.append(int(operands, 16))
            if mnemonic == "jmp":
                continue
        successor = address + len(bytes.fromhex(row["bytes"]))
        if successor < int(scope["end"], 16):
            pending.append(successor)
    return seen


normal = reachable("LevelControl.Start")
without_getter = reachable("LevelControl.Start", cut=0x3EA935)
for address in (0x3ECDFE, 0x3ECE0E, 0x3ECE30):
    assert address in normal and address not in without_getter
early_timeline = [x for x in scopes["LevelControl.Start"]["calls"]
                  if int(x["at"], 16) < 0x3EA935 and any(name.startswith("Steamworks.SteamTimeline$$") for name in x["names"])]
assert not early_timeline
epilogue = [x for x in scopes["Progress.Update"]["instructions"] if 0x401617 <= int(x["rva"], 16) <= 0x401630]
assert [x["mnemonic"] for x in epilogue] == ["movaps", "movaps", "mov", "mov", "add", "pop", "ret"]
assert not any(x["mnemonic"].startswith("call") or x["mnemonic"].startswith("j") for x in epilogue)
snapshot = json.loads((root / "scene-references.json").read_text(encoding="utf8"))
data_root = game.parent / "Rizline_Data"
for name, pin in snapshot["sources"].items():
    raw = (data_root / name).read_bytes()
    assert len(raw) == pin["size"] and hashlib.sha256(raw).hexdigest() == pin["sha256"]
scene = next(iter(UnityPy.load(str(data_root / "level6")).files.values()))
scripts = next(iter(UnityPy.load(str(data_root / "globalgamemanagers.assets")).files.values()))
for row in snapshot["objects"]:
    raw = scene.objects[row["pathId"]].get_raw_data()
    assert raw.hex() == row["rawHex"] and hashlib.sha256(raw).hexdigest() == row["rawSha256"]
    header = scene.objects[row["pathId"]].read_typetree(check_read=False)
    assert header == row["baseHeader"]
    script = scripts.objects[row["scriptPathId"]].read()
    assert "InLevel." + script.m_ClassName == row["class"]
    for pointer in row["firstSerializedPointers"]:
        offset = pointer["serializedByteOffset"]
        assert raw[offset:offset + 12].hex() == pointer["rawHex"]
assert scene.objects[58].read_typetree() == snapshot["gameObject58"]
proof = {"runtimeAcceptance": False, "staticRanges": len(scopes), "instructionAnchors": checks,
         "serializedObjects": len(snapshot["objects"]),
         "normalCfg": {"getterCallDominates": ["loadOver=true", "Start.SetTimelineGameMode", "Start.SetTimelineTooltip"],
                       "directTimelineBeforeGetter": 0, "exceptionEdgesModeled": False, "indirectCalleesFollowed": False},
         "loadOverFalsePath": {"condition": "info!=null && info.loadOver==false", "branch": "0x4010b5", "epilogue": epilogue,
                               "levelOverOrTimelineCalls": 0, "preGuardMetadataInitializationExcludedFromNoSideEffectsClaim": True},
         "scope": "Finite file/byte/normal-CFG consistency; runtime ordering, exact ownership, activation and Steam isolation remain unverified."}
(root / "evidence-check.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf8")
print(json.dumps({"staticRanges": len(scopes), "anchors": len(checks), "serializedObjects": len(snapshot["objects"]), "runtimeAcceptance": False}))
