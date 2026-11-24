"""Recheck this batch's bounded disassembly bytes and input hashes on disk."""
import hashlib
import json
from pathlib import Path
import pefile

root = Path(__file__).resolve().parent
game = Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll")
reverse = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output")
proof = json.loads((root / "menu-evidence.json").read_text())
sha = lambda raw: hashlib.sha256(raw).hexdigest()
raw = game.read_bytes()
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
result = dict(ranges=len(proof["ranges"]), instructions=sum(len(s["instructions"]) for s in proof["ranges"]),
    allRangeHashesAndInstructionBytesMatched=True, allThreeInputHashesMatched=True, gameRuntimeVerified=False)
(root / "menu-evidence-check.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result))
