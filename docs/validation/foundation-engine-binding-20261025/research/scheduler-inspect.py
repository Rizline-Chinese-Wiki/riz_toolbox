"""Bounded fixed-image disk inspection; never maps or executes game code."""
from pathlib import Path
import bisect
import hashlib
import json
import struct
import sys

import capstone
import pefile

HERE = Path(__file__).resolve().parent
IMAGE = Path(r"D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll")
GAME = Path(r"D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll")
EXPECTED = {
    IMAGE: "43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1",
    GAME: "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79",
}
inputs = []
for path, expected in EXPECTED.items():
    raw = path.read_bytes()
    actual = hashlib.sha256(raw).hexdigest()
    if actual != expected:
        raise SystemExit(f"Unsupported fixed input: {path}: {actual}")
    inputs.append(dict(path=str(path), bytes=len(raw), sha256=actual))
data = IMAGE.read_bytes()
pe = pefile.PE(data=data, fast_load=True)
assert pe.FILE_HEADER.Machine == 0x8664
base = pe.OPTIONAL_HEADER.ImageBase
directory = pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
pdata = pe.get_data(directory.VirtualAddress, directory.Size)
functions = [struct.unpack_from("<III", pdata, i) for i in range(0, len(pdata)-11, 12)]
starts = [row[0] for row in functions]
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = True


def primary(row):
    seen = set()
    for _ in range(16):
        if row in seen:
            raise ValueError("Unwind chain cycle")
        seen.add(row)
        header = pe.get_data(row[2], 4)
        if header[0] & 7 not in (1, 2):
            raise ValueError("Unsupported unwind version")
        if not (header[0] >> 3) & 4:
            return row
        row = struct.unpack("<III", pe.get_data(row[2]+4+((header[2]+1)&~1)*2, 12))
    raise ValueError("Unwind chain exceeds finite inspection bound")


roots = [primary(row) for row in functions]


def region(entry, label):
    index = bisect.bisect_right(starts, entry)-1
    assert index >= 0 and functions[index][0] <= entry < functions[index][1]
    root = roots[index]
    group = [row for row, parent in zip(functions, roots) if parent == root]
    merged = []
    for lo, hi, _ in sorted(group):
        if merged and lo <= merged[-1][1]:
            merged[-1][1] = max(merged[-1][1], hi)
        else:
            merged.append([lo, hi])
    segments, instructions = [], []
    for lo, hi in merged:
        body = pe.get_data(lo, hi-lo)
        decoded = list(md.disasm(body, lo))
        assert decoded and sum(item.size for item in decoded) == len(body)
        segments.append(dict(begin=hex(lo), end=hex(hi), sha256=hashlib.sha256(body).hexdigest(), bytes=body.hex()))
        for item in decoded:
            row = dict(rva=hex(item.address), bytes=item.bytes.hex(), op=item.mnemonic, args=item.op_str)
            if item.group(capstone.CS_GRP_CALL) or item.group(capstone.CS_GRP_JUMP):
                row["controlFlow"] = "call" if item.group(capstone.CS_GRP_CALL) else "branch"
                row["directTarget"] = hex(item.operands[0].imm) if item.operands[0].type == capstone.CS_OP_IMM else None
            instructions.append(row)
    return dict(label=label, requestedEntry=hex(entry), primaryRuntimeFunction=[hex(x) for x in root],
                beginRva=hex(min(row[0] for row in group)), endRva=hex(max(row[1] for row in group)),
                runtimeFunctions=[dict(begin=hex(a), end=hex(b), unwind=hex(u), unwindHeader=pe.get_data(u, 4).hex()) for a, b, u in group],
                segments=segments, instructions=instructions,
                returnSites=[row for row in instructions if row["op"].startswith("ret")],
                controlFlow=[row for row in instructions if "controlFlow" in row])


selected = [
    (0x792240, "Start: owner list, synchronous Execute and inline final cleanup"),
    (0x78F840, "Execute: native active reference, nested coroutine and scheduler paths"),
    (0x78FA30, "Continue: continuation registration, node payload and cleanup callback"),
    (0x793840, "Detach: owner list and scheduler removal"),
    (0x447AD0, "Scheduler remove: erase and cleanup callback"),
    (0x44D630, "Scheduler register: actual key/payload/callback ownership"),
    (0x78F5E0, "Scheduled node callback: owner check and Execute tail call"),
    (0x78F680, "Release: refcount, final cleanup and node free"),
    (0x78F770, "Managed Coroutine wrapper release: final free versus handle clear"),
    (0x775AD0, "Script reference construction: native handle, type and cache"),
]
regions = [region(entry, label) for entry, label in selected]
result = dict(schemaVersion=1, kind="fixed-unityplayer-scheduler-cleanup-research", inputs=inputs,
              tools=dict(python=sys.version, pefile=pefile.__version__, capstone=capstone.__version__, idaCallableUsed=False),
              scope="Complete selected .pdata CHAININFO groups, bounded static disk reads only. No game mapping/init/native calls/UI/install or build.",
              pe=dict(machine=hex(pe.FILE_HEADER.Machine), imageBase=hex(base), imageSize=hex(pe.OPTIONAL_HEADER.SizeOfImage)),
              functions=regions,
              boundaries=dict(allCallersEnumerated=False, allSchedulerRegistriesEnumerated=False,
                              coherentConcurrentSnapshotsVerified=False, gameCodeExecuted=False,
                              gameRuntimeVerified=False, engineCancellationCompleted=False,
                              firstInstallSafetyVerified=False))
out = HERE/"scheduler-evidence.json"
out.write_text(json.dumps(result, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
lines = []
for function in regions:
    lines.append(f"\n## {function['label']} {function['beginRva']}..{function['endRva']}")
    lines.extend(f"{row['rva']:>10} {row['bytes']:<28} {row['op']:<9} {row['args']}" for row in function['instructions'])
assembly = HERE/"scheduler-disassembly.txt"
assembly.write_text("\n".join(lines)+"\n", encoding="utf-8")
print(json.dumps(dict(functions=len(regions), instructions=sum(len(x["instructions"]) for x in regions),
                      json=str(out), sha256=hashlib.sha256(out.read_bytes()).hexdigest(),
                      assembly=str(assembly), assemblySha256=hashlib.sha256(assembly.read_bytes()).hexdigest(),
                      gameCodeExecuted=False)))
