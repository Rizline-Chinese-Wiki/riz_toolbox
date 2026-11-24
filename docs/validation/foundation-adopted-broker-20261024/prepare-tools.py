"""Prepare a new batch's evidence tools without regenerating frozen predecessors."""
from pathlib import Path
import shutil

root = Path(__file__).resolve().parent
previous = root.parent / "foundation-clr-preinit-20261024"
for name in ["summarize-build.py", "freeze-source.py", "freeze-package.py", "compare-sources.py", "verify-terminal-evidence.py", "check-docs.py"]:
    body = (previous / name).read_text(encoding="utf-8-sig")
    body = body.replace("clr-preinit-final-20261024", "adopted-broker-final-20261024")
    if name == "compare-sources.py":
        body = body.replace("foundation-preinit-producer-20261024", previous.name)
        body = body.replace("changes-from-1515", "changes-from-clr-preinit")
        body = body.replace('fromfile="1515/"', 'fromfile="clr-preinit/"').replace('tofile="clr-preinit/"', 'tofile="adopted-broker/"')
    if name == "check-docs.py":
        body = body.replace("底层-CLR启动采用-report.md", "底层-采用回调联合-report.md")
    if name == "summarize-build.py":
        anchor = '    write("init-call-evidence.json", init_call)\n'
        assert body.count(anchor) == 1
        body = body.replace(anchor, anchor + '''    callbacks = read(args.native_output / "adopted-broker-managed" / "fixture-result.json")
    assert callbacks["kind"] == "native-adopted-broker-callback-summary"
    assert callbacks["passed"] and callbacks["failures"] == 0 and callbacks["independentProcesses"] == 3
    assert callbacks["checks"] > 0 and callbacks["nativeToClrCallbackVerified"] and callbacks["metadataIdentityControlled"]
    assert not any(callbacks[key] for key in ["firstInstallReceiptUsed", "gameCodeExecuted", "gameRuntimeVerified", "installed"])
    assert {row["mode"] for row in callbacks["modes"]} == {"happy", "fault", "late-attach"}
    for row in callbacks["modes"]:
        assert row["passed"] and row["failures"] == 0 and row["checks"] > 0
        assert row["nativeToClrCallbackVerified"] and row["metadataIdentityControlled"]
        assert not any(row[key] for key in ["firstInstallReceiptUsed", "gameCodeExecuted", "gameRuntimeVerified"])
        assert all(row[key] for key in ["sharedProductionAdoptionCoreVerified", "productionGameFrontRejected", "fixtureOwnedOriginal", "sharedPhysicalTokenVerified", "diskHashesUnchanged", "bridgeKnown"])
        assert row["bridgeFactoryCalls"] == 1 and row["brokerInstallCalls"] == 0
        assert row["gateActive"] == row["gateParked"] == row["gateUnknown"] == 0
        assert row["captures"] == row["contextDisposals"] and row["probeEnters"] == row["probeExits"]
        if row["mode"] == "fault":
            assert row["managedObserverFaultVerified"] and row["nativeOriginalExceptionsVerified"] and not row["brokerKnown"]
        else:
            assert row["brokerKnown"]
        if row["mode"] == "late-attach":
            assert row["oldInvocationDidNotInheritObserver"] and row["originalNativeCalls"] == row["observedNativeInvocations"] + 1
    write("adopted-broker-evidence.json", callbacks)
''')
        body = body.replace("productionInitCall=init_call, logSha256=sha(log)", "productionInitCall=init_call, adoptedBrokerCallbacks=callbacks, logSha256=sha(log)")
        body = body.replace('"first-install-unknown", "doorstop-init"]:', '"first-install-unknown", "doorstop-init", "adopted-broker-managed"]:')
        body = body.replace("Real supported startup timing, production CLR callbacks, listener/GC closure and game acceptance remain unverified.",
            "The production shared configured-slot adoption and broker dispatch core additionally executes real native-to-CLR callbacks over a self-owned Dobby fixture in three independent processes, with no first-install receipt used. Supported game startup timing, fixed-game receipt callbacks, real listener/GC closure and game acceptance remain unverified.")
    (root / name).write_text(body, encoding="utf-8", newline="\n")
for name in ["direct-call-candidates.json", "mid-site-evidence.json", "terminal-disassembly.txt", "terminal-evidence.json", "terminal-metadata.txt", "static-evidence-origin.json"]:
    shutil.copy2(previous / name, root / name)
print("Prepared tools and inherited separately revalidated static evidence.")
