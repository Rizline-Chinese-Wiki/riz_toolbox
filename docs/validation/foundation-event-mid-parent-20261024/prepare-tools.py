"""Prepare a new joint-fixture batch; never modify the frozen 1490 evidence."""
from pathlib import Path
import json
import shutil

root = Path(__file__).resolve().parent
previous = root.parent / "foundation-event-mid-20261024"
for name in ["freeze-source.py", "freeze-package.py", "verify-terminal-evidence.py"]:
    shutil.copy2(previous / name, root / name)
body = (previous / "summarize-build.py").read_text(encoding="utf-8-sig")
body = body.replace("event-mid-final-20261024", "event-mid-parent-final-20261024")
body = body.replace('["native-manifest.json", "build.log", "exports.txt", "disassembly.txt", "native-probe-result.json",', '["native-manifest.json", "build.log", "exports.txt", "disassembly.txt", "unwindinfo.txt", "native-probe-result.json",')
needle = '    total = sum(s["total"] for s in suites)'
extra = '''    joint_cases = [
        "Native UnityEvent joint parent and mid preserve actual caller token empty lists and foreign recursion",
        "Native UnityEvent joint worker tail window and logical detach retain parent physical responsibility",
        "Native UnityEvent joint adoption faults and native SEH Cpp originals preserve exact continuation and cleanup",
        "Native UnityEvent joint next-generation reused context refuses a stale physical identity before adoption",
        "Native UnityEvent joint clean adopted inline scope survives original SEH with paired cleanup and retained roots",
        "Native UnityEvent joint clean adopted inline scope survives original Cpp with paired cleanup and retained roots",
    ]
    assert all("PASS " + name in body for name in joint_cases)
    write("native-parent-mid-evidence.json", dict(
        source="Six named AudioContent cases executed in isolated native-enabled processes in build.log; assertions cover the actual Dobby/MASM/helper/CLR/broker/ledger chain.",
        passedCases=joint_cases, realDobbyParent=True, realNativeMidCall=True, actualReturnOffset=0x140,
        midToManagedBridge=True, brokerCurrentPrivateIdentity=True, ledgerReceivesActualMidFrame=True,
        emptyAndNonemptyLists=True, foreignReceiverAndMethodRecursion=True,
        postMidWorkerParentStillInFlight=True, logicalDetachRetainsParent=True,
        controlledAdoptionFaultRetainsResponsibility=True, nativeSehCppCodeKindPayloadPreserved=True,
        cleanOwnerAdoptsBeforeNativeSehCpp=True, adoptedRootsRetainedAndContextsPairedOnAbnormalExit=True,
        staleCapabilityRejectedBeforeAdoption=True, backendGraphAndRootsControlled=True,
        fixtureOnlyPreInvocationPatch=True, gamePatchPublished=False, gameRuntimeVerified=False,
        limitations="No IL2CPP graph/GC, production resolver or live instruction publication, actual listener invocation/mutation closure, cancellation or whole-run retirement acceptance."))
'''
assert needle in body
body = body.replace(needle, extra + needle)
body = body.replace("Combined real native parent to mid to managed Adapter, concurrent instruction publication, actual game execution and listener closure remain unverified.", "The joint real Dobby parent to native mid CALL to managed bridge/broker and ledger is checked separately in AudioContent; backend graph and roots remain controlled. Concurrent production instruction publication, actual game execution and listener closure remain unverified.")
(root / "summarize-build.py").write_text(body, encoding="utf-8")
body = (previous / "compare-sources.py").read_text(encoding="utf-8-sig")
body = body.replace("foundation-native-void-20261024", previous.name).replace("1475", "1490")
body = body.replace('tofile="event-mid/"', 'tofile="event-mid-parent/"').replace('assert len(added) == 8 and not removed', 'assert len(added) == 1 and not removed')
(root / "compare-sources.py").write_text(body, encoding="utf-8")
body = (previous / "check-docs.py").read_text(encoding="utf-8-sig")
body = body.replace("2026-10-24_底层-事件内段上下文-report.md", "2026-10-24_底层-事件父链联合验证-report.md")
(root / "check-docs.py").write_text(body, encoding="utf-8")
names = ["terminal-evidence.json", "terminal-disassembly.txt", "terminal-metadata.txt", "direct-call-candidates.json", "mid-site-evidence.json"]
for name in names:
    shutil.copy2(previous / name, root / name)
(root / "static-evidence-origin.json").write_text(json.dumps(dict(previousBatch=previous.name, copiedFiles=names,
    scope="Pinned static evidence reused; this batch adds joint offline fixture evidence, not game disassembly or runtime acceptance."), indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(prepared=True, built=False)))
