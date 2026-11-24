"""Only inspect the two missing retirement anchors in the fixed native image.

Does not load the DLL, attach to any process, install tools or inspect other code.
Existing launch triage/import/export classification remains the survey evidence.
"""
import argparse
import bisect
import hashlib
import json
from pathlib import Path

import capstone
import pefile
from capstone.x86 import X86_OP_IMM

EXPECTED = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
SCOPES = {
    "SceneManager.ClearDontDestroyOnLoadObject": 0x401710,
    "NormalMode.LevelOver.staticDelayedCallback": 0x3C7E90,
}
ap = argparse.ArgumentParser()
ap.add_argument("--game", type=Path, required=True)
ap.add_argument("--symbols", type=Path, required=True)
ap.add_argument("--dump", type=Path, required=True)
ap.add_argument("--out", type=Path, required=True)
a = ap.parse_args()
raw = a.game.read_bytes()
sha = hashlib.sha256(raw).hexdigest()
if sha != EXPECTED:
    raise SystemExit("Unsupported fixed game image: " + sha)
pe = pefile.PE(data=raw, fast_load=True)
by = {}
for symbol in json.loads(a.symbols.read_text(encoding="utf8"))["ScriptMethod"]:
    by.setdefault(symbol["Address"], []).append(symbol)
starts = sorted(by)
engine = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
engine.detail = True
scopes = []
lines = []
for name, start in SCOPES.items():
    if start not in by:
        raise SystemExit("Missing scope entry: " + name)
    end = starts[bisect.bisect_right(starts, start)]
    if end - start > 0x600:
        raise SystemExit("Rejected large or unbounded scope: " + name)
    code = pe.get_data(start, end - start)
    lines.append(f"\n=== {name} [{start:08x},{end:08x}) ===")
    lines.extend(item["Name"] + " :: " + item["Signature"] for item in by[start])
    instructions = []
    for ins in engine.disasm(code, start):
        row = {"rva": hex(ins.address), "bytes": ins.bytes.hex(),
               "mnemonic": ins.mnemonic, "operands": ins.op_str}
        note = ""
        if ins.mnemonic in ("call", "jmp") and ins.operands and ins.operands[0].type == X86_OP_IMM:
            dest = ins.operands[0].imm
            row["destination"] = hex(dest)
            names = [item["Name"] for item in by.get(dest, [])]
            row["destinationNames"] = names
            note = " ; " + " | ".join(names[:4]) if names else ""
        instructions.append(row)
        lines.append(f"{ins.address:08x} {ins.mnemonic:9} {ins.op_str}{note}")
    scopes.append({"name": name, "start": hex(start), "end": hex(end),
                   "rangeSha256": hashlib.sha256(code).hexdigest(),
                   "symbols": by[start], "instructions": instructions})
dump = a.dump.read_text(encoding="utf8").splitlines()
ranges = [(95573, 95583), (95658, 95670), (97702, 97755),
          (98204, 98251), (112212, 112227), (112641, 112653), (114335, 114353)]
excerpt = []
for first, last in ranges:
    excerpt.extend(f"{i}: {dump[i - 1]}" for i in range(first, last + 1))
    excerpt.append("")
a.out.mkdir(parents=True, exist_ok=True)
(a.out / "retirement-disassembly.txt").write_text("\n".join(lines), encoding="utf8")
(a.out / "metadata-excerpts.txt").write_text("\n".join(excerpt), encoding="utf8")
result = {"schemaVersion": 1, "evidenceKind": "bounded-static-disassembly",
          "runtimeAcceptance": False, "gameAssemblySha256": sha,
          "dumpSha256": hashlib.sha256(a.dump.read_bytes()).hexdigest(),
          "scriptJsonSha256": hashlib.sha256(a.symbols.read_bytes()).hexdigest(),
          "triageRef": "../local-launch-20261022/triage.json", "scopes": scopes}
(a.out / "retirement-boundaries.json").write_text(json.dumps(result, indent=2), encoding="utf8")
for item in scopes:
    print(item["name"], item["start"], item["end"], len(item["instructions"]))
