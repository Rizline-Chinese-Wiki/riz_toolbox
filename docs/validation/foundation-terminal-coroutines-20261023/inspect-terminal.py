"""Inspect bounded pinned-image terminal coroutine ranges without executing the game."""
import bisect
import hashlib
import json
from pathlib import Path
import capstone
import pefile

root = Path(__file__).resolve().parent
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll")
reverse = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output")
sha = lambda data: hashlib.sha256(data).hexdigest()
raw = game.read_bytes()
assert sha(raw) == "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
script = (reverse / "script.json").read_bytes()
dump = (reverse / "dump.cs").read_bytes()
symbols = {}
for entry in json.loads(script)["ScriptMethod"]:
    symbols.setdefault(entry["Address"], []).append(entry)
starts = sorted(symbols)
pe = pefile.PE(data=raw, fast_load=True)
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
decoder.detail = True
scopes = {"Normal.ControllerOver": 0x3BEA60, "Normal.CutoutFactory": 0x3BED10,
    "Normal.FadeFactory": 0x3BED80, "Normal.LevelOver": 0x3C0C00,
    "Normal.Cutout.MoveNext": 0x3C5900, "Normal.Fade.MoveNext": 0x3C5A40,
    "Normal.LevelOverCoroutine.MoveNext": 0x3C5C80,
    "MonoBehaviour.StartCoroutine.String": 0x153D490, "MonoBehaviour.StartCoroutine.StringObject": 0x153D5A0,
    "MonoBehaviour.StartCoroutineManaged": 0x153D320}
ranges, text = [], []
for name, start in scopes.items():
    end = starts[bisect.bisect_right(starts, start)]
    assert start in symbols and 0 < end - start <= 0x4000
    data = pe.get_data(start, end - start)
    rows = []
    text.append(f"\n=== {name} [{start:08x},{end:08x}) ===")
    for instruction in decoder.disasm(data, start):
        row = dict(rva=hex(instruction.address), bytes=instruction.bytes.hex(), mnemonic=instruction.mnemonic, operands=instruction.op_str)
        if instruction.mnemonic in ("call", "jmp") and instruction.operands and instruction.operands[0].type == capstone.x86.X86_OP_IMM:
            row["destinationRva"] = hex(instruction.operands[0].imm)
            row["destinationNames"] = [entry["Name"] for entry in symbols.get(instruction.operands[0].imm, [])]
        rows.append(row)
        text.append(f"{instruction.address:08x} {instruction.mnemonic:9} {instruction.op_str} ; " + " | ".join(row.get("destinationNames", [])[:3]))
    ranges.append(dict(name=name, start=hex(start), end=hex(end), sha256=sha(data), symbols=symbols[start], instructions=rows))
lines = dump.decode("utf-8-sig").splitlines()
decoded = {int(row["rva"], 16): row for scope in ranges for row in scope["instructions"]}
assert decoded[0x3BEAA5].get("destinationRva") == "0x153d490"
assert decoded[0x3C59E3].get("destinationRva") == "0x153e680"
assert decoded[0x153D508]["mnemonic"] == "jmp" and decoded[0x153D508]["operands"] == "rax"
assert decoded[0x3C5A6F]["operands"].endswith("0x3e4ccccd")
excerpts = [f"{index+1}: {lines[index]}" for begin, end in [(95594, 95638), (95783, 95803)] for index in range(begin-1, end)]
(root / "terminal-metadata.txt").write_text("\n".join(excerpts) + "\n", encoding="utf-8")
(root / "terminal-disassembly.txt").write_text("\n".join(text) + "\n", encoding="utf-8")
proof = dict(kind="bounded-terminal-coroutine-inspection", gameRuntimeVerified=False,
    inputs=dict(gameAssemblySha256=sha(raw), scriptJsonSha256=sha(script), dumpSha256=sha(dump)), ranges=ranges)
(root / "terminal-evidence.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(ranges=len(ranges), instructions=sum(len(item["instructions"]) for item in ranges))))
