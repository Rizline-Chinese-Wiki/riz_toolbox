"""Read only the missing finite generic-clone thunk used by both stock retries.

This supplements, rather than reruns, inspect-launch.py. The symbol file does not
name this shared generic address. Its exact closed MethodInfo is still unknown.
"""
import argparse
import hashlib
import json
from pathlib import Path

import capstone
import pefile

EXPECTED = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
START, END = 0x5A91B0, 0x5A92C0
parser = argparse.ArgumentParser()
parser.add_argument("--game", type=Path, required=True)
parser.add_argument("--symbols", type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parent
raw = args.game.read_bytes()
sha = hashlib.sha256(raw).hexdigest()
if sha != EXPECTED:
    raise SystemExit("Unsupported image: " + sha)
symbols_raw = args.symbols.read_bytes()
base = json.loads((root / "boundaries.json").read_text(encoding="utf8"))
if hashlib.sha256(symbols_raw).hexdigest() != base["scriptJsonSha256"]:
    raise SystemExit("Symbol input differs from the inherited pinned extraction.")
symbols = json.loads(symbols_raw)["ScriptMethod"]
names = {}
for symbol in symbols:
    names.setdefault(symbol["Address"], []).append(symbol)
assert START not in names, "The naming limitation must be re-evaluated."
pe = pefile.PE(data=raw, fast_load=True)
body = pe.get_data(START, END - START)
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
instructions = []
lines = [f"=== shared generic clone thunk, inferred [{START:08x}, {END:08x}) ==="]
for ins in md.disasm(body, START):
    row = {"rva": hex(ins.address), "mnemonic": ins.mnemonic,
           "operands": ins.op_str, "bytes": ins.bytes.hex()}
    note = ""
    if ins.mnemonic == "call" and ins.op_str.startswith("0x"):
        destination = int(ins.op_str, 16)
        row["destinationSymbols"] = names.get(destination, [])
        note = " ; " + " | ".join(s["Name"] for s in row["destinationSymbols"])
    instructions.append(row)
    lines.append(f"{ins.address:08x} {ins.mnemonic:9} {ins.op_str}{note}")
output = {
    "schemaVersion": 1, "evidenceKind": "bounded-static-disassembly-supplement",
    "runtimeAcceptance": False, "gameAssemblySha256": sha,
    "scriptJsonSha256": base["scriptJsonSha256"], "start": hex(START), "end": hex(END),
    "rangeSha256": hashlib.sha256(body).hexdigest(), "instructions": instructions,
    "identity": "shared generic clone thunk inferred from callsites and instruction behavior",
    "limitations": ["No named ScriptMethod at 0x5A91B0; closed generic MethodInfo is not verified.",
                    "Internal_CloneSingle enters Unity engine; synchronous Awake timing is not observed here.",
                    "No process execution, hook, UI, save or Steam invocation."]
}
(root / "instantiation-thunk.json").write_text(json.dumps(output, indent=2), encoding="utf8")
(root / "instantiation-thunk.txt").write_text("\n".join(lines), encoding="utf8")
print(f"Extracted only {hex(START)}..{hex(END)}; runtimeAcceptance=False.")
