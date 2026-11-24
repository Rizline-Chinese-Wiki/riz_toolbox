"""Find direct-call candidates, then verify each instruction from a symbol boundary."""
import bisect
import json
import hashlib
import struct
from pathlib import Path
import capstone
import pefile

root = Path(__file__).resolve().parent
raw = Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll").read_bytes()
pe = pefile.PE(data=raw, fast_load=True)
script = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\script.json").read_bytes()
methods = json.loads(script)["ScriptMethod"]
sha = lambda data: hashlib.sha256(data).hexdigest()
assert sha(raw) == "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
symbols = {}
for method in methods:
    symbols.setdefault(method["Address"], []).append(method["Name"])
starts = sorted(symbols)
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
targets = {0x153D490, 0x153D5A0, 0x3BED80, 0x3BED10}
hits = []
for section in pe.sections:
    if not section.Characteristics & 0x20000000:
        continue
    data = section.get_data()
    position = data.find(b"\xe8")
    while position >= 0:
        address = section.VirtualAddress + position
        destination = address + 5 + struct.unpack_from("<i", data, position + 1)[0] if position + 5 <= len(data) else 0
        if destination in targets:
            begin = starts[bisect.bisect_right(starts, address) - 1]
            if address - begin < 0x5000 and any(instruction.address == address and instruction.mnemonic == "call"
                for instruction in decoder.disasm(pe.get_data(begin, address - begin + 5), begin)):
                hits.append(dict(caller=hex(address), returned=hex(address+5), bytes=pe.get_data(address, 5).hex(), target=hex(destination), source=hex(begin), names=symbols[begin][:3]))
        position = data.find(b"\xe8", position+1)
proof = dict(inputs=dict(gameAssemblySha256=sha(raw), scriptJsonSha256=sha(script)), candidates=hits,
    limitation="Direct E8 candidates verified by linear decode from a known symbol within 0x5000 bytes. Indirect/tail/reflection/event calls and missing symbols are not covered; absence is not a complete xref proof.")
(root / "direct-call-candidates.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(candidates=len(hits), gameRuntimeVerified=False)))
