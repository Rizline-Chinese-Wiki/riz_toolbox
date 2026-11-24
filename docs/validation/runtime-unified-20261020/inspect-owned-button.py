"""Pinned local-binary call-site proof; static evidence, never a runtime pass."""
import hashlib
import json
from pathlib import Path
import capstone
import pefile

binary = Path(r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll")
expected = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79"
assert hashlib.sha256(binary.read_bytes()).hexdigest() == expected
pe = pefile.PE(str(binary), fast_load=True)
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)

def instructions(rva, length):
    return [{"rva": hex(i.address), "mnemonic": i.mnemonic, "operands": i.op_str}
            for i in decoder.disasm(pe.get_data(rva, length), rva)]

pointer = instructions(0x15D6D50, 0x1D)
submit = instructions(0x15D6D80, 0x16)
press = instructions(0x15D6E50, 0x89)
assert any(i["rva"] == "0x15d6d68" and i["mnemonic"] == "jmp" and i["operands"] == "0x15d6e50" for i in pointer)
assert any(i["rva"] == "0x15d6d8b" and i["mnemonic"] == "call" and i["operands"] == "0x15d6e50" for i in submit)
assert any(i["rva"] == "0x15d6ec8" and i["mnemonic"] == "jmp" and i["operands"] == "0x15642e0" for i in press)
evidence = {"kind": "static-only", "runtimeActivationVerified": False,
            "binary": str(binary), "sha256": expected, "architecture": "Windows x64",
            "target": {"assembly": "UnityEngine.UI.dll", "namespace": "UnityEngine.UI", "class": "Button",
                       "method": "Press", "parameters": [], "rva": "0x15d6e50", "abi": "InstanceVoid"},
            "pointerEntry": pointer, "submitEntry": submit, "pressBody": press,
            "delivery": "after original, registered framework-owned receivers only; bounded queue drained on host main thread"}
output = Path(__file__).with_name("owned-button-static-proof.json")
output.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"passed": True, "runtimeActivationVerified": False, "output": str(output)}))
