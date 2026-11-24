"""Read fixed Tween code and runtime-function facts without mapping or executing it.

The five non-leaf observation points and three small source paths are separate:
leaf Over does not issue work, and Replay/Start reach Play. This is input for the
native publisher, not proof of first-execution exclusion or consumer retirement.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

import capstone
import pefile


GAME_SHA256 = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
OPERATIONS = (
    ("Play", "TweenInfo", 0x3956F0, "Void0", ()),
    ("AddTweenToWaitingList", "TweenManager", 0x396BE0, "Reference1", ("TweenInfo",)),
    ("ExecuteTween", "TweenManager", 0x396E40, "Reference1", ("TweenInfo",)),
    ("Update", "TweenManager", 0x398D60, "Void0", ()),
    ("Awake", "TweenManager", 0x396CF0, "Void0", ()),
)
LEAVES = (("Over", 0x3956E0, 8), ("Replay", 0x3964F0, 14), ("Start", 0x396500, 17))


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def inspect(path):
    source = path.read_bytes()
    if sha256(source) != GAME_SHA256:
        raise ValueError("Unsupported fixed GameAssembly")
    pe = pefile.PE(data=source, fast_load=True)
    if pe.FILE_HEADER.Machine != 0x8664 or pe.OPTIONAL_HEADER.Magic != 0x20B:
        raise ValueError("AMD64 PE32+ image required")
    directory = pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
    if not directory.VirtualAddress or directory.Size % 12:
        raise ValueError("A complete static runtime-function directory is required")
    # Read the table once: looking up each RF must not copy the whole .pdata.
    pdata = pe.get_data(directory.VirtualAddress, directory.Size)
    functions = [struct.unpack_from("<III", pdata, offset) for offset in range(0, len(pdata), 12)]
    decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    decoder.detail = True
    rows = []
    for name, owner, begin, kind, parameters in OPERATIONS:
        matches = [row for row in functions if row[0] <= begin < row[1]]
        if len(matches) != 1 or matches[0][0] != begin:
            raise ValueError(f"{owner}.{name} lacks one exact static entry RF")
        _, end, unwind = matches[0]
        body = pe.get_data(begin, end - begin)
        instructions = list(decoder.disasm(body, begin))
        # These larger functions include inline switch tables inside their RF
        # extent. Hash every byte, but do not reinterpret table entries as code
        # or claim linear disassembly proves a complete control-flow graph.
        linear_bytes = sum(item.size for item in instructions)
        if linear_bytes < 32:
            raise ValueError(f"{name} entry observation has fewer than 32 decoded bytes")
        header = pe.get_data(unwind, 4)
        version, flags, count = header[0] & 7, header[0] >> 3, header[2]
        if version != 1 or flags & ~7 or flags & 4 and flags & 3:
            raise ValueError(f"{name} unsupported unwind header")
        prefix_size = (4 + count * 2 + 3) & ~3
        prefix_size += 12 if flags & 4 else 4 if flags & 3 else 0
        prefix = pe.get_data(unwind, prefix_size)
        if len(prefix) != prefix_size:
            raise ValueError(f"{name} truncated unwind prefix")
        row = dict(method=name, declaringClass=owner, isStatic=False, returnType="System.Void",
                   parameters=parameters, kind=kind, beginRva=begin, endRva=end, bodyBytes=len(body),
                   bodySha256=sha256(body), fullBodyHex=body.hex(), leadingLinearInstructions=len(instructions),
                   leadingLinearBytes=linear_bytes, remainingBytes=len(body) - linear_bytes,
                   unwindRva=unwind, unwindVersion=version, unwindFlags=flags,
                   unwindPrefixBytes=prefix_size, unwindPrefixHex=prefix.hex(),
                   unwindPrefixSha256=sha256(prefix), languageSpecificTablesInterpreted=False)
        if flags & 4:
            row["chainedRuntimeFunction"] = struct.unpack_from("<III", prefix, prefix_size - 12)
        elif flags & 3:
            row["handlerRva"] = struct.unpack_from("<I", prefix, prefix_size - 4)[0]
        rows.append(row)

    leaf_rows = []
    for name, begin, size in LEAVES:
        body = pe.get_data(begin, size)
        instructions = list(decoder.disasm(body, begin))
        if sum(item.size for item in instructions) != size or any(a <= begin < b for a, b, _ in functions):
            raise ValueError(f"{name} is not the expected fully decoded static-RF-free leaf")
        # This exact sequence proves why these paths do not need tiny detours.
        operations = [(item.mnemonic, item.op_str) for item in instructions]
        if name == "Over" and operations != [("mov", "byte ptr [rcx + 0xd8], 1"), ("ret", "")]:
            raise ValueError("Over is no longer the single isOver store and return")
        if name == "Replay" and (operations[:-1] != [("xor", "edx, edx"), ("mov", "byte ptr [rcx + 0xd8], 1")]
                                 or operations[-1] != ("jmp", "0x3956f0")):
            raise ValueError("Replay no longer reaches the actual Play entry")
        if name == "Start" and operations != [("cmp", "byte ptr [rcx + 0xa0], 0"), ("je", "0x396510"),
                                               ("xor", "edx, edx"), ("jmp", "0x3956f0"), ("ret", "")]:
            raise ValueError("Start no longer returns or reaches the actual Play entry")
        leaf_rows.append(dict(method=name, declaringClass="TweenInfo", beginRva=begin, endRva=begin + size,
                              bodyBytes=size, bodyHex=body.hex(), bodySha256=sha256(body), hasStaticRuntimeFunction=False,
                              decoded=[dict(rva=item.address, mnemonic=item.mnemonic, operands=item.op_str)
                                       for item in instructions], requiresDetour=False,
                              reason="Only stores isOver; it does not issue work" if name == "Over" else "Returns or reaches the gated Play entry"))
    if sha256(path.read_bytes()) != GAME_SHA256:
        raise ValueError("The fixed file changed during inspection")
    return dict(schemaVersion=1, kind="fixed-tween-operation-input-facts", passed=True,
                gameAssembly=dict(path=str(path.resolve()), sha256=GAME_SHA256), operations=rows,
                leafPaths=leaf_rows, minimumMaximumOperationBytes=max(row["bodyBytes"] for row in rows),
                capstoneVersion=capstone.__version__, pefileVersion=pefile.__version__,
                gameExecuted=False, installed=False, productionAdmissionGranted=False,
                limitations=["Static bytes and unwind header/code/handler-or-chain prefixes only; language-specific exception tables remain uninterpreted.",
                             "Full RF extents include inline switch tables; leading linear disassembly is diagnostic, not a control-flow proof.",
                             "The former 592-byte operation buffer cannot hold these actual complete RF bodies.",
                             "These are future publisher inputs; no new native lane or admission seal is installed by this inspection."])


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--game-assembly", type=Path, required=True)
    parser.add_argument("--output-json", type=Path, required=True)
    args = parser.parse_args()
    if args.output_json.exists():
        raise SystemExit("Preserve prior evidence; use a fresh output path")
    result = inspect(args.game_assembly)
    result["inspector"] = dict(path=str(Path(__file__).resolve()), sha256=sha256(Path(__file__).read_bytes()))
    args.output_json.parent.mkdir(parents=True, exist_ok=True)
    args.output_json.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("Verified five actual full Tween RF bodies and three leaf source paths; game not executed.")
