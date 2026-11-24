"""Validate named proof sites and emit a finite observation contract."""
from pathlib import Path
import hashlib
import json

HERE = Path(__file__).resolve().parent
sources = [HERE/"scheduler-evidence.json", HERE/"scheduler-consumption.json", HERE/"scheduler-xrefs.json"]
documents = [json.loads(path.read_text(encoding="utf-8")) for path in sources]
instructions = {}
for document in documents:
    for function in document["functions"]:
        for row in function["instructions"]:
            old = instructions.setdefault(row["rva"], row)
            assert old["bytes"] == row["bytes"]
proofs = [
    ("0x44d683", "mov", "rcx, qword ptr [rbx + 0x18]", "registration payload"),
    ("0x44d68b", "mov", "rcx, qword ptr [rbx + 0x20]", "registration callback"),
    ("0x44d693", "mov", "rcx, qword ptr [rbx + 0x28]", "registration cleanup"),
    ("0x44d743", "mov", "qword ptr [r14], rax", "registration actual node output"),
    ("0x44d746", "mov", "rax, r14", "registration returns output slot"),
    ("0x78fb92", "inc", "dword ptr [rbx + 0x60]", "default scheduler reference"),
    ("0x78fc23", "call", "0x44d630", "default scheduler producer"),
    ("0x78fdac", "call", "0x44d630", "delay scheduler producer"),
    ("0x790129", "call", "0x44d630", "fixed/end-frame/default scheduler producer"),
    ("0x447ae1", "mov", "rbx, qword ptr [rdx + 0x28]", "remove caches cleanup before erase"),
    ("0x447ae5", "mov", "rdi, qword ptr [rdx + 0x18]", "remove caches payload before erase"),
    ("0x447b33", "call", "0x3f01b0", "remove ordinary erase"),
    ("0x447bf5", "call", "0x3f01b0", "remove current-iterator erase"),
    ("0x447c19", "call", "rbx", "remove cleanup after erase/recycle"),
    ("0x447d3f", "call", "0x3f01b0", "erase-only path"),
    ("0x447edf", "call", "0x447c30", "normal consume before callback"),
    ("0x447eea", "call", "r15", "normal scheduler callback"),
    ("0x447ef2", "cmp", "eax, 1", "full EAX, not AL, controls cleanup"),
    ("0x447efa", "call", "rsi", "conditional cleanup after normal callback"),
    ("0x447f3b", "call", "0x44d630", "repeat requeues before erasing old row"),
    ("0x447f46", "call", "0x447c30", "repeat consume old row"),
    ("0x447f51", "call", "r15", "repeat callback without conditional cleanup"),
    ("0x447f60", "call", "0x447ad0", "missing native owner removal"),
    ("0x4477ee", "call", "qword ptr [rbx + 0x40]", "bulk drain optional category-0x40 callback"),
    ("0x447840", "call", "0x3f01b0", "bulk drain direct erase"),
    ("0x447864", "call", "rdi", "bulk drain unconditional cleanup"),
    ("0x78f5ed", "cmp", "qword ptr [rdx + 0x58], rcx", "callback actual owner check"),
    ("0x78f655", "mov", "eax, 1", "callback mismatched-owner return"),
    ("0x78f66e", "jmp", "0x78f840", "callback tail calls Execute"),
    ("0x78fa01", "xor", "eax, eax", "Execute complete uint32 zero"),
    ("0x78fa27", "mov", "eax, 1", "Execute complete uint32 one"),
    ("0x793883", "mov", "qword ptr [rdi + 0x58], rbp", "Detach clears node owner"),
    ("0x793983", "mov", "dword ptr [rax + 0x28], ebp", "Detach invalidates async owner id, not callbacks"),
    ("0x790069", "mov", "dword ptr [rdi + 0x28], ebx", "async registration owner id"),
    ("0x790073", "mov", "qword ptr [rdi + 0x10], rax", "async registration callback"),
    ("0x79007e", "mov", "qword ptr [rdi + 0x18], rax", "async registration cleanup"),
    ("0x790082", "mov", "qword ptr [rdi + 0x20], r14", "async registration node payload"),
    ("0x790098", "mov", "qword ptr [r14 + 0x78], rdi", "node async object reference"),
    ("0x79009c", "lock inc", "dword ptr [rdi + 0xc]", "async shared reference increment"),
    ("0x7924c3", "cmp", "dword ptr [rbx + 0x60], r15d", "Start branch compares reference count to one"),
    ("0x7924c7", "jg", "0x7925a2", "Start external Release path versus inline cleanup"),
    ("0x7924cd", "dec", "dword ptr [rbx + 0x60]", "Start inline cleanup entry"),
    ("0x79256d", "call", "0x17e7110", "Start inline native node free"),
    ("0x792576", "mov", "qword ptr [r12], rdi", "Start inline free branch null output"),
    ("0x792596", "mov", "qword ptr [r12], rdi", "Start inline wrapper branch null output"),
    ("0x7925a5", "call", "0x78f680", "Start non-inline Release"),
    ("0x78f739", "call", "0x17e7110", "Release can free node before returning"),
    ("0x5da645", "call", "qword ptr [rbp + 0x18]", "matching-shape shutdown cleanup, provenance incomplete"),
]
observed = []
for rva, operation, operands, fact in proofs:
    row = instructions[rva]
    assert row["op"] == operation and row["args"] == operands, (rva, row, operation, operands)
    observed.append(dict(fact=fact, **row))
contract = dict(schemaVersion=1, kind="fixed-unityplayer-scheduler-finite-observation-contract",
    artifactInputs=[dict(path=path.name, sha256=hashlib.sha256(path.read_bytes()).hexdigest()) for path in sources],
    fixedImageInputs=documents[0]["inputs"], proofSites=observed,
    requiredLanes=[
        dict(name="register", entry="0x44d630", registers=["RCX tree", "RDX actual output slot", "R8 source record"],
             result="RAX same output slot; *slot actual tree node", afterNodeRead=False),
        dict(name="erase-with-cleanup", entry="0x447ad0", registers=["RCX manager", "RDX record=node+0x20", "R8 tree node"],
             result="known callers ignore RAX; do not invent an unproven wider source return contract", afterNodeRead=False),
        dict(name="consume-without-cleanup", entry="0x447c30", registers=["RCX manager", "RDX opaque unused", "R8 tree node"],
             result="known callers ignore RAX; erased row transfers to callback responsibility", afterNodeRead=False),
        dict(name="pump", entry="0x447d60", registers=["RCX manager", "EDX category mask"],
             result="known callers ignore RAX; must observe actual selected row and invocation phase", afterNodeRead=False),
        dict(name="bulk-drain", entry="0x447750", registers=["RCX manager"],
             result="known callers ignore RAX; per-row native events required", afterNodeRead=False),
        dict(name="scheduled-node-callback", entry="0x78f5e0", registers=["RCX native owner", "RDX native node"],
             result="uint32 EAX: owner mismatch=1, otherwise tail Execute", afterNodeRead=False),
        dict(name="execute", entry="0x78f840", registers=["RCX native node", "RDX optional execution-error byte output"],
             result="uint32 EAX, all selected normal exits exactly 0 or 1", afterNodeRead=False),
        dict(name="start-inline-entry", entry="0x7924cd", registers=["RBX actual native node", "R12 actual Start output slot"],
             result="mid-function protocol, not a C ABI or ordinary detour", afterNodeRead=False),
        dict(name="start-inline-free-complete", entry="0x79257d", registers=["saved Before identity only"],
             result="after actual free and output-null write, before jump to Start epilogue", afterNodeRead=False),
        dict(name="start-inline-wrapper-complete", entry="0x79259d", registers=["saved Before identity only"],
             result="after wrapper handle clear and output-null write, before jump to Start epilogue", afterNodeRead=False),
    ],
    recordLayout=dict(minimumBytes="0x3c", nativeTreeNodeBytes="0x60", treeRecordOffset="0x20",
        fields={"0x00":"double due time bits", "0x08":"int64 frame eligibility", "0x10":"float interval bits",
                "0x14":"uint8 repeat flag", "0x18":"void* payload", "0x20":"callback address",
                "0x28":"cleanup address", "0x30":"uint32 owner id", "0x34":"uint32 category mask", "0x38":"uint32 creation tag"}),
    asyncLayout=dict(fields={"0x0c":"shared reference count", "0x10":"callback", "0x18":"cleanup",
                            "0x20":"node payload", "0x28":"owner id"},
        registrationRange=["0x790069", "0x79009c"], nodeBackPointerOffset="0x78", ordinaryDispatchProven=False),
    completeForAllEngineCancellation=False, schedulerRegistrationAndAllConsumptionClosureProven=False,
    asyncOrdinaryDispatchProven=False, allIndirectCallersEnumerated=False, allNodeWritersEnumerated=False,
    coherentConcurrentSnapshotsVerified=False, firstInstallSafetyVerified=False,
    gameCodeExecuted=False, engineCancellationCompleted=False)
output = HERE/"scheduler-contract.json"
output.write_text(json.dumps(contract, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
print(json.dumps(dict(output=str(output), sha256=hashlib.sha256(output.read_bytes()).hexdigest(),
                      verifiedProofSites=len(proofs), gameCodeExecuted=False)))
