"""Bounded offline PE inspection only. Does not load, execute or alter the game."""
import bisect
import hashlib
import json
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
    "MultiLock.Lock": 0x38B2D0, "MultiLock.UnlockAll": 0x38B310, "MultiLock.Unlock": 0x38B350,
    "TouchBlocker.Block": 0x382720, "TouchBlocker.Release": 0x3827E0, "BottomBar.BackLock": 0x3CB010,
    "Component.get_gameObject": 0x1536400, "GameObject.SetActive": 0x1538AB0,
    "GameObject.get_activeSelf": 0x1538EF0, "Object.op_Implicit": 0x15409B0,
}
ranges, text = [], []
for name, start in scopes.items():
    assert start in symbols
    end = starts[bisect.bisect_right(starts, start)]
    assert 0 < end - start <= 0x1000
    data = pe.get_data(start, end - start)
    rows = []
    text.append(f"\n=== {name} [{start:08x},{end:08x}) SHA256 {sha(data)} ===")
    for instruction in decoder.disasm(data, start):
        row = dict(rva=hex(instruction.address), bytes=instruction.bytes.hex(), mnemonic=instruction.mnemonic, operands=instruction.op_str)
        if instruction.mnemonic in ("call", "jmp") and instruction.operands and instruction.operands[0].type == capstone.x86.X86_OP_IMM:
            row["destinationNames"] = [item["Name"] for item in symbols.get(instruction.operands[0].imm, [])]
        rows.append(row)
        text.append(f"{instruction.address:08x} {instruction.mnemonic:9} {instruction.op_str}" + " ; " + " | ".join(row.get("destinationNames", [])[:3]))
    ranges.append(dict(name=name, start=hex(start), end=hex(end), sha256=sha(data), symbols=symbols[start], instructions=rows))
dump_raw = (reverse / "dump.cs").read_bytes()
lines = dump_raw.decode("utf-8-sig").splitlines()
excerpts = []
for start, end in [(91487, 91503), (92458, 92477), (93497, 93516), (96817, 96839)]:
    excerpts.extend(f"{index + 1}: {lines[index]}" for index in range(start - 1, end))
(root / "menu-metadata.txt").write_text("\n".join(excerpts) + "\n", encoding="utf-8")
(root / "menu-disassembly.txt").write_text("\n".join(text) + "\n", encoding="utf-8")
proof = dict(kind="bounded-menu-native-inspection", runtimeAcceptance=False, inputs=dict(gameAssemblySha256=expected,
    scriptJsonSha256=sha(symbols_raw), dumpSha256=sha(dump_raw)), ranges=ranges)
(root / "menu-evidence.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(ranges=len(ranges), instructions=sum(len(item["instructions"]) for item in ranges), runtimeAcceptance=False)))
