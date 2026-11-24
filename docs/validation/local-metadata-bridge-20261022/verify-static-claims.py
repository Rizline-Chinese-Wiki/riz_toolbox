"""Verify the finite instruction anchors cited by this directory's static report."""
import hashlib, json
from pathlib import Path

root = Path(__file__).resolve().parent
evidence = json.loads((root / "boundaries.json").read_text(encoding="utf8"))
scopes = {s["name"]: s for s in evidence["scopes"]}
checks = []

def expect(scope, rva, mnemonic, operands):
    row = next(i for i in scopes[scope]["instructions"] if i["rva"] == hex(rva))
    assert (row["mnemonic"], row["operands"]) == (mnemonic, operands), (scope, row)
    checks.append({"scope": scope, "rva": hex(rva), "mnemonic": mnemonic, "operands": operands})

expect("LevelMetadata.ctor", 0x377453, "mov", "qword ptr [rdi + 0x10], rbx")
expect("LevelMetadata.ctor", 0x377463, "mov", "qword ptr [rdi + 0x18], rsi")
expect("LevelMetadata.get_Artist", 0x37748D, "mov", "rax, qword ptr [rax + 0x28]")
expect("LevelMetadata.get_Artist", 0x377496, "mov", "rax, qword ptr [rax + 0x20]")
expect("LevelMetadata.get_MusicName", 0x3777ED, "mov", "rax, qword ptr [rax + 0x28]")
expect("LevelMetadata.get_MusicName", 0x3777F6, "mov", "rax, qword ptr [rax + 0x18]")
expect("LevelMetadata.get_Charter", 0x3774C6, "mov", "rax, qword ptr [rax + 0x28]")
expect("LevelMetadata.get_LevelInternalName", 0x377676, "mov", "rax, qword ptr [rax + 0x18]")
expect("LevelMetadata.get_LevelDisplayName", 0x37763C, "test", "rbx, rbx")
expect("LevelMetadata.get_LevelDisplayName", 0x37764A, "mov", "rax, rbx")
expect("LevelMetadata.get_Lv", 0x377743, "mov", "rcx, qword ptr [rbx + 0x38]")
expect("LevelMetadata.get_Lv", 0x3777C1, "mov", "rax, qword ptr [rbx + 0x38]")
expect("LevelMetadata.get_Status", 0x377826, "cmp", "byte ptr [rax + 0x28], 0")
expect("LevelMetadata.get_Status", 0x37783A, "cmp", "byte ptr [rax + 0x29], cl")
expect("LevelMetadata.get_ThemeColor", 0x377876, "movups", "xmm0, xmmword ptr [rax + 0x30]")
expect("LevelDifference.ctor", 0x37733F, "mov", "qword ptr [rdi + 0x18], rbx")
expect("LevelDifference.ctor", 0x377359, "mov", "qword ptr [rdi + 0x10], rbp")
expect("LevelDifference.ctor", 0x3773A1, "call", "0x7592a0")
expect("LevelDifference.ctor", 0x3773AF, "movups", "xmmword ptr [rdi + 0x20], xmm0")
expect("InsideLevelResult.ToBestScore.LevelMetadata", 0x375443, "mov", "rdx, qword ptr [rdx + 0x10]")
expect("InsideLevelResult.ToBestScore.LevelMetadata", 0x37544B, "mov", "qword ptr [rbx + 0x10], rdx")
expect("InsideLevelResult.ToBestScore.LevelMetadata", 0x375466, "mov", "rdx, qword ptr [rdx + 0x18]")
expect("InsideLevelResult.ToBestScore.LevelMetadata", 0x37546E, "mov", "qword ptr [rbx + 0x18], rdx")
expect("LevelOverControl.ProcessLevelMetadata", 0x3CFB5D, "call", "0x375380")
expect("LevelOverControl.ProcessLevelMetadata", 0x3CFB81, "call", "0x36d170")
expect("LevelOverControl.ProcessLevelMetadata", 0x3CFC37, "call", "0x3899c0")
expect("LevelOverControl.ProcessLevelMetadata", 0x3CFC61, "jmp", "0x3ce440")
expect("LevelOverControl.Start", 0x3D0ECE, "call", "0x375380")
expect("LevelOverControl.Start", 0x3D0EE9, "call", "0x36d170")
expect("LevelOverControl.Start", 0x3D0FAB, "call", "0x3899c0")
expect("LevelOverControl.Start", 0x3D0FBC, "call", "0x3ce440")
expect("LevelOverControl.Start", 0x3D0FC6, "call", "0x3cf1b0")
expect("SaveSystem.DomainWrapper.set_Item", 0x3899C3, "jmp", "0x389370")
expect("SaveSystem.DomainWrapper.Set", 0x38948F, "call", "0x3ada50")
expect("SaveSystem.DomainWrapper.Set", 0x389572, "call", "0x38e0b0")
expect("LevelOverControl.CheckAchievement", 0x3CE7BE, "call", "0x4c2430")
expect("LevelOverControl.CheckAchievement", 0x3CEE59, "call", "0x48bf10")
expect("LevelOverControl.CheckAchievement", 0x3CEF51, "call", "0x48cbe0")
expect("LevelOverControl.AreAllChartsInDisc", 0x3CDEA8, "call", "0x375890")
expect("NormalModeInLevelController.GetInsideLevelParams", 0x3BF0DB, "movss", "dword ptr [rbx + 0x38], xmm0")
expect("NormalModeInLevelController.GetInsideLevelParams", 0x3BF13C, "movss", "dword ptr [rbx + 0x3c], xmm0")
expect("NormalModeInLevelController.GetInsideLevelParams", 0x3C02CF, "mov", "rax, qword ptr [rdi + 0x20]")
expect("NormalModeInLevelController.LoadLevel.MoveNext", 0x3C618F, "call", "0x1544b90")
expect("LevelOverControl.LoadLevel.MoveNext", 0x3D3D7F, "call", "0x1544b90")

# These finite getters have only object fields, literal initialization, strings and IL2CPP helpers.
# This assertion does not claim anything about all game consumers or runtime behavior.
allowed = {"0x264d00", "0x264ff0", "0x263f30", "0x263f60", "0xd86170", "0xd88740", "0xd87190", "0xf171a0", "0x403ea0"}
getter_calls = [c for name, scope in scopes.items() if name.startswith("LevelMetadata.get_") for c in scope["calls"]]
assert all(c["to"] in allowed for c in getter_calls), getter_calls
result = {
    "schemaVersion": 1, "kind": "bounded-static-anchor-verification", "passed": True,
    "runtimeAcceptance": False, "checks": checks, "getterDirectCallsWithinDeclaredFiniteSet": True,
    "scopes": len(scopes), "gameAssemblySha256": evidence["gameAssemblySha256"],
    "boundariesSha256": hashlib.sha256((root / "boundaries.json").read_bytes()).hexdigest(),
    "limitations": ["Static shape and actual native method behavior/ownership are separate gates.",
                    "No gameplay, save, Steam API or native construction was invoked."]
}
(root / "static-claims.json").write_text(json.dumps(result, indent=2), encoding="utf8")
print(f"Verified {len(checks)} static anchors in {len(scopes)} bounded scopes; runtimeAcceptance=False.")
