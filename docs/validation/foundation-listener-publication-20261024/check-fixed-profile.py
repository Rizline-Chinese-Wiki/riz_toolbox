"""Inspect six fixed source regions without loading or executing GameAssembly."""
import hashlib
import json
import struct
from datetime import datetime, timezone
from pathlib import Path

import capstone
import pefile

batch = Path(__file__).resolve().parent
game = Path("D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll")
expected_sha = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
image = game.read_bytes()
assert hashlib.sha256(image).hexdigest() == expected_sha
pe = pefile.PE(data=image, fast_load=True)
assert pe.FILE_HEADER.Machine == 0x8664
assert pe.FILE_HEADER.TimeDateStamp == 0x6AD0A11B
assert pe.OPTIONAL_HEADER.SizeOfImage == 0x243C000
directory = pe.OPTIONAL_HEADER.DATA_DIRECTORY[3]
assert (directory.VirtualAddress, directory.Size) == (0x225B000, 0x14D75C)


def read(rva, size):
    result = pe.get_data(rva, size)
    assert len(result) == size
    return result


parent = (0x15642E0, 0x156454E, 0x1DB3FB4)
pdata = read(directory.VirtualAddress, directory.Size)
overlaps = [entry for entry in struct.iter_unpack("<III", pdata)
            if entry[0] < parent[1] and entry[1] > parent[0]]
assert overlaps == [parent]
unwind = bytes.fromhex("01 61 0a 00 61 e4 09 00 5c 74 08 00 57 54 07 00 52 34 06 00 06 32 02 60")
assert read(parent[2], len(unwind)) == unwind
disassembler = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
disassembler.detail = True
sites = []
for site, (rva, source, continuation) in enumerate([
    (0x1564470, "e83ba1feff", "ffc38bc3eba5"),
    (0x15644C2, "e8e9a0feff", "ffc38bc3e950ffffff"),
    (0x156451F, "ff9078010000", "ffc38bc3e9f2feffff"),
]):
    source_bytes, post_bytes = bytes.fromhex(source), bytes.fromhex(continuation)
    return_rva = rva + len(source_bytes)
    assert read(rva, len(source_bytes)) == source_bytes
    assert read(return_rva, len(post_bytes)) == post_bytes
    call = list(disassembler.disasm(source_bytes, rva))
    replay = list(disassembler.disasm(post_bytes, return_rva))
    assert len(call) == 1 and call[0].mnemonic == "call"
    assert sum(instruction.size for instruction in replay) == len(post_bytes)
    assert [instruction.mnemonic for instruction in replay] == ["inc", "mov", "jmp"]
    assert replay[0].op_str == "ebx" and replay[1].op_str == "eax, ebx"
    assert replay[2].operands[0].imm == 0x1564420
    if site < 2:
        assert call[0].operands[0].imm == 0x154E5B0
    else:
        assert call[0].op_str == "qword ptr [rax + 0x178]"
    section = pe.get_section_by_rva(rva)
    assert section.Characteristics & 0x20000000
    sites.append(dict(site=site, sourceRva=rva, sourceHex=source,
                      sourceWidth=len(source_bytes), returnRva=return_rva,
                      postHex=continuation, postWidth=len(post_bytes),
                      call=call[0].mnemonic + " " + call[0].op_str,
                      replay=[f"{instruction.mnemonic} {instruction.op_str}" for instruction in replay]))

result = dict(schemaVersion=1, kind="listener-fixed-file-profile", passed=True,
              inspectedAtUtc=datetime.now(timezone.utc).isoformat(), path=str(game),
              sha256=expected_sha, imageSize=pe.OPTIONAL_HEADER.SizeOfImage,
              timestamp=pe.FILE_HEADER.TimeDateStamp, parentRuntimeFunction=list(parent),
              unwindHex=unwind.hex(), sites=sites, gameCodeExecuted=False,
              imageLoaded=False, dynamicUnwindVerified=False,
              firstInstallSafetyVerified=False, gameRuntimeVerified=False)
(batch / "fixed-profile-evidence.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(passed=True, fixedRegions=6, sourceWidths=[row["sourceWidth"] for row in sites],
                      postWidths=[row["postWidth"] for row in sites], gameCodeExecuted=False)))
