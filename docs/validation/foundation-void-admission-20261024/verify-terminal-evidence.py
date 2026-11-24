"""Recheck exact bytes, input hashes and bounded direct-call candidates on disk."""
import hashlib
import json
import struct
from pathlib import Path
import pefile

root = Path(__file__).resolve().parent
raw = Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll").read_bytes()
reverse = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output")
sha = lambda data: hashlib.sha256(data).hexdigest()
proof = json.loads((root / "terminal-evidence.json").read_text())
assert sha(raw) == proof["inputs"]["gameAssemblySha256"]
assert sha((reverse / "script.json").read_bytes()) == proof["inputs"]["scriptJsonSha256"]
assert sha((reverse / "dump.cs").read_bytes()) == proof["inputs"]["dumpSha256"]
pe = pefile.PE(data=raw, fast_load=True)
for scope in proof["ranges"]:
    start, end = int(scope["start"], 16), int(scope["end"], 16)
    assert sha(pe.get_data(start, end-start)) == scope["sha256"]
    for instruction in scope["instructions"]:
        data = bytes.fromhex(instruction["bytes"])
        assert pe.get_data(int(instruction["rva"], 16), len(data)) == data
calls = json.loads((root / "direct-call-candidates.json").read_text())
assert calls["inputs"]["gameAssemblySha256"] == proof["inputs"]["gameAssemblySha256"]
assert calls["inputs"]["scriptJsonSha256"] == proof["inputs"]["scriptJsonSha256"]
for candidate in calls["candidates"]:
    address, target = int(candidate["caller"], 16), int(candidate["target"], 16)
    data = pe.get_data(address, 5)
    assert data.hex() == candidate["bytes"] and data[0] == 0xE8 and address+5 == int(candidate["returned"], 16)
    assert address+5+struct.unpack_from("<i", data, 1)[0] == target
result = dict(ranges=len(proof["ranges"]), instructions=sum(len(scope["instructions"]) for scope in proof["ranges"]),
    callCandidates=len(calls["candidates"]), allInputRangeInstructionAndCandidateBytesMatched=True, gameRuntimeVerified=False)
(root / "terminal-evidence-check.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result))
