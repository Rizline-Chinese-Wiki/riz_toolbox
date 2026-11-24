"""Bounded offline inspection of the pinned image; never loads or executes it."""
import bisect
import hashlib
import json
import struct
from pathlib import Path
import capstone
import pefile

root = Path(__file__).resolve().parent
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline")
reverse = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output")
expected = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
sha = lambda raw: hashlib.sha256(raw).hexdigest()
raw = (game / "GameAssembly.dll").read_bytes()
assert sha(raw) == expected
symbols_raw = (reverse / "script.json").read_bytes()
symbols = {}
for item in json.loads(symbols_raw)["ScriptMethod"]:
    symbols.setdefault(item["Address"], []).append(item)
starts = sorted(symbols)
pe = pefile.PE(data=raw, fast_load=True)
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
decoder.detail = True
scopes = {
    "NormalModeInLevelController.GetInsideLevelParams": 0x3BEDF0,
    "SimpleCoroutine.SimpleDelay": 0x3AE340,
    "SimpleCoroutine.SimpleWaitUntil": 0x3AE480,
    "MonoBehaviour.StartCoroutine+StartCoroutine_Auto": 0x153D380,
    "MonoBehaviour.StartCoroutineManaged2": 0x153D2D0,
    "SimpleCoroutine.Delay.MoveNext": 0x3AF0A0,
    "SimpleCoroutine.WaitUntil.MoveNext": 0x3B2D70,
    "Shared.TouchBlockerReleaseAction": 0x3B1B60,
    "TouchBlocker.Release": 0x3827E0,
    "ClosedAction.Constructor": 0x377890,
    "ClosedPredicate.Constructor": 0x8D3510,
    "Getter.DelayTodo": 0x3C1930,
    "Getter.WaitPredicate": 0x3C1960,
}
ranges, text, decoded = [], [], {}
for name, start in scopes.items():
    assert start in symbols
    end = {0x377890: 0x377920, 0x8D3510: 0x8D35A0}.get(start, starts[bisect.bisect_right(starts, start)])
    assert 0 < end - start <= 0x4000
    data = pe.get_data(start, end - start)
    rows = []
    text.append(f"\n=== {name} [{start:08x},{end:08x}) SHA256 {sha(data)} ===")
    for instruction in decoder.disasm(data, start):
        row = dict(rva=hex(instruction.address), bytes=instruction.bytes.hex(), mnemonic=instruction.mnemonic, operands=instruction.op_str)
        if instruction.mnemonic in ("call", "jmp") and instruction.operands and instruction.operands[0].type == capstone.x86.X86_OP_IMM:
            row["destinationRva"] = hex(instruction.operands[0].imm)
            row["destinationNames"] = [item["Name"] for item in symbols.get(instruction.operands[0].imm, [])]
        rows.append(row)
        decoded[instruction.address] = row
        text.append(f"{instruction.address:08x} {instruction.mnemonic:9} {instruction.op_str}" + " ; " + " | ".join(row.get("destinationNames", [])[:3]))
    ranges.append(dict(name=name, start=hex(start), end=hex(end), sha256=sha(data), symbols=symbols[start], instructions=rows))

edges = [(0x3BEFD5, "call", 0x3AE340), (0x3BF0AD, "call", 0x3AE480),
         (0x3AE3D0, "jmp", 0x153D380), (0x3AE504, "jmp", 0x153D380)]
for source, mnemonic, destination in edges:
    row = decoded[source]
    assert row["mnemonic"] == mnemonic and row["destinationRva"] == hex(destination), row
assert decoded[0x153D3EC]["mnemonic"] == "jmp" and decoded[0x153D3EC]["operands"] == "rax"
public = next(item for item in ranges if item["start"] == "0x153d380")
assert not any(row.get("destinationRva") == "0x153d2d0" for row in public["instructions"])
assert {"UnityEngine.MonoBehaviour$$StartCoroutine", "UnityEngine.MonoBehaviour$$StartCoroutine_Auto"}.issubset({item["Name"] for item in symbols[0x153D380]})
release = next(item for item in ranges if item["start"] == "0x3b1b60")
assert any(row["mnemonic"] == "jmp" and row.get("destinationRva") == "0x3827e0" for row in release["instructions"])
for start in [0x377890, 0x8D3510]:
    constructor = next(item for item in ranges if int(item["start"], 16) == start)
    operands = [row["operands"] for row in constructor["instructions"]]
    assert any("[rcx + 0x20], rdx" in value for value in operands)
    assert any("[rcx + 0x28]" in value for value in operands)
constants = []
for name, rva, bits in [("getterDelaySeconds", 0x17C4E24, "66664640"), ("sceneTransitionSeconds", 0x17C4C30, "00004040")]:
    value = pe.get_data(rva, 4)
    assert value.hex() == bits
    constants.append(dict(name=name, rva=hex(rva), bytes=bits, value=struct.unpack("<f", value)[0]))
dump_raw = (reverse / "dump.cs").read_bytes()
lines = dump_raw.decode("utf-8-sig").splitlines()
excerpts = []
for start, end in [(7680, 7694), (7933, 7936), (93742, 93755), (93817, 93829), (95574, 95583), (95802, 95803)]:
    excerpts.extend(f"{index + 1}: {lines[index]}" for index in range(start - 1, end))
(root / "coroutine-metadata.txt").write_text("\n".join(excerpts) + "\n", encoding="utf-8")
(root / "coroutine-disassembly.txt").write_text("\n".join(text) + "\n", encoding="utf-8")
proof = dict(kind="bounded-coroutine-native-inspection", gameRuntimeVerified=False,
    inputs=dict(gameAssemblySha256=expected, scriptJsonSha256=sha(symbols_raw), dumpSha256=sha(dump_raw)),
    constants=constants, ranges=ranges, directEdges=[dict(source=hex(s), mnemonic=m, destination=hex(d)) for s, m, d in edges],
    getterReturnCallers=["0x3befda", "0x3bf0b2"],
    limitation="Static control flow only. No actual controller/enumerator roots, callback ownership, successful initial unlock, or complete run retirement is proved.")
(root / "coroutine-evidence.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(ranges=len(ranges), instructions=sum(len(item["instructions"]) for item in ranges), gameRuntimeVerified=False)))
