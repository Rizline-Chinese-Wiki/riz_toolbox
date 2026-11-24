"""Bounded offline scene-launch inspection; never executes/loads the sample.

Uses the already-installed pefile, capstone and UnityPy tools. Captures only named
scopes and selected BuildSettings scene names; no unrelated build fields are emitted.
"""
import bisect
import hashlib
import importlib.metadata
import json
import struct
import sys
from pathlib import Path

import capstone
import pefile
import UnityPy

HERE = Path(__file__).resolve().parent
GAME = Path(r"D:\SteamLibrary\steamapps\common\Rizline")
OUTPUT = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output")
EXPECTED = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
SCOPES = {
    "LevelCard.StartPlayLevel.MoveNext": 0x3C6E50,
    "WorkshopCard.StartPlayLevel.MoveNext": 0x3D7630,
    "SceneManager.StartNextScene": 0x402170,
    "SceneManager.StartNextSceneEnumerator.MoveNext": 0x403EF0,
    "Shared.TouchBlockReleaseAction": 0x3B1B60,
    "LevelCard.ActiveScenePredicate": 0x3C8000,
    "WorkshopCard.ActiveScenePredicate": 0x3D9820,
}

def digest(raw):
    return hashlib.sha256(raw).hexdigest()

raw = (GAME / "GameAssembly.dll").read_bytes()
assert digest(raw) == EXPECTED
pe = pefile.PE(data=raw, fast_load=True)
symbols_raw = (OUTPUT / "il2cpp_output/script.json").read_bytes()
by = {}
for entry in json.loads(symbols_raw)["ScriptMethod"]:
    by.setdefault(entry["Address"], []).append(entry)
starts = sorted(by)
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = True
text, ranges = [], []
for name, start in SCOPES.items():
    end = starts[bisect.bisect_right(starts, start)]
    assert start in by and 0 < end - start < 0x1000
    data = pe.get_data(start, end - start)
    text.append(f"\n=== {name} [{start:08x}, {end:08x}) SHA256 {digest(data)} ===")
    rows = []
    for ins in md.disasm(data, start):
        row = {"rva": hex(ins.address), "bytes": ins.bytes.hex(), "mnemonic": ins.mnemonic, "operands": ins.op_str}
        names = []
        if ins.mnemonic in ("call", "jmp") and ins.operands and ins.operands[0].type == capstone.x86.X86_OP_IMM:
            names = [symbol["Name"] for symbol in by.get(ins.operands[0].imm, [])]
            row["destinationNames"] = names
        rows.append(row)
        text.append(f"{ins.address:08x} {ins.mnemonic:9} {ins.op_str}" + (" ; " + " | ".join(names[:3]) if names else ""))
    ranges.append({"name": name, "start": hex(start), "end": hex(end), "sha256": digest(data), "symbols": by[start], "instructions": rows})

settings_path = GAME / "Rizline_Data/globalgamemanagers"
settings_raw = settings_path.read_bytes()
build_settings = [obj.read_typetree()["scenes"] for obj in UnityPy.load(str(settings_path)).objects if obj.type.name == "BuildSettings"]
assert len(build_settings) == 1 and build_settings[0][6] == "Assets/Rizline/Level/Level.unity"
metadata_path = OUTPUT / "metadata/global-metadata.dat"
metadata_raw = metadata_path.read_bytes()
header = struct.unpack_from("<6I", metadata_raw)
assert header[:2] == (0xFAB11BAF, 31)
literal_slot = 0x1E79DF0
encoded = struct.unpack("<I", pe.get_data(literal_slot, 4))[0]
literal_index = (encoded & 0x1FFFFFFF) >> 1
literal_length, literal_offset = struct.unpack_from("<II", metadata_raw, header[2] + literal_index * 8)
literal_valid = literal_length <= header[5] and literal_offset <= header[5] - literal_length
assert not literal_valid  # Recovered table cannot independently establish this literal's text.
constant_rva = 0x17C4C30
assert pe.get_data(constant_rva, 4) == bytes.fromhex("00004040")
evidence = {
    "schemaVersion": 1, "kind": "bounded-offline-native-disassembly", "runtimeAcceptance": False,
    "inputs": {"gameAssemblySha256": EXPECTED, "scriptJsonSha256": digest(symbols_raw),
               "globalGameManagersSha256": digest(settings_raw), "recoveredMetadataSha256": digest(metadata_raw)},
    "tools": {"python": sys.version.split()[0], "capstone": capstone.__version__, "pefile": pefile.__version__,
              "UnityPy": importlib.metadata.version("UnityPy")},
    "ranges": ranges, "buildSettingsScenes": build_settings[0],
    "transitionTime": {"rva": hex(constant_rva), "rawHex": "00004040", "float": 3.0},
    "sceneLiteral": {"slotRva": hex(literal_slot), "encoded": hex(encoded), "index": literal_index,
                     "recoveredTableLength": literal_length, "recoveredTableOffset": literal_offset,
                     "withinRecoveredLiteralData": literal_valid, "text": None,
                     "limitation": "Recovered literal entry is outside the header's literal-data bounds; do not claim its text was decoded."},
}
(HERE / "evidence.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf8")
(HERE / "disassembly.txt").write_text("\n".join(text) + "\n", encoding="utf8")
dump_raw = (OUTPUT / "il2cpp_output/dump.cs").read_bytes()
lines = dump_raw.decode("utf8").splitlines()
excerpt_ranges = [(91487, 91503), (94887, 94901), (94908, 94928), (95915, 95929), (98218, 98251)]
excerpts = [f"dump.cs SHA256 {digest(dump_raw)}"]
for start, end in excerpt_ranges:
    excerpts.extend(f"{index + 1}: {lines[index]}" for index in range(start - 1, end))
    excerpts.append("")
(HERE / "metadata-excerpts.txt").write_text("\n".join(excerpts) + "\n", encoding="utf8")
print(json.dumps({"ranges": len(ranges), "runtimeAcceptance": False, "destinationSceneBuildIndex": 6,
                  "literalTextVerified": False, "evidenceSha256": digest((HERE / "evidence.json").read_bytes())}))
