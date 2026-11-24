"""Copy the established evidence procedures into this new, separate batch."""
from pathlib import Path
batch = Path(__file__).resolve().parent
previous = batch.parent / 'foundation-engine-node-20261025'
for name in ['freeze-before-build.py','freeze-source.py','freeze-package.py','verify-frozen-inputs.py','compare-sources.py','summarize-build.py','run-build.ps1','check-docs.py']:
    target = batch / name
    if target.exists():
        continue
    text = (previous / name).read_text(encoding='utf-8-sig')
    text = text.replace('engine-node-final-20261025', 'engine-binding-mask-final-20261025')
    text = text.replace('2026-10-25_底层-引擎协程节点观察-report.md', '2026-10-25_底层-返回ABI与协程身份-report.md')
    if name == 'compare-sources.py':
        text = text.replace('foundation-operation-preinit-20261024', previous.name).replace('changes-from-operation-preinit', 'changes-from-engine-node')
    if name == 'summarize-build.py':
        text = text.replace('engine_node["checks"] == 3191', 'engine_node["checks"] == 3402')
        text = text.replace('engine_node["actualOriginalCalls"] == 128', 'engine_node["actualOriginalCalls"] == 136')
        text = text.replace('engine_node["exited"] == 116', 'engine_node["exited"] == 124')
        text = text.replace('engine_node["originalFreeNodeCases"] == 5', 'engine_node["originalFreeNodeCases"] == 6')
        text = text.replace('    write("engine-node-native-evidence.json", engine_node)', '''    assert engine_node["transportAbiVersion"] == 2 and engine_node["executeReturnBits"] == 32
    assert engine_node["rawExecuteEaxVerified"] and engine_node["executeFullWidthCases"] == 8 and engine_node["executeFullWidthCompletions"] == 10
    write("engine-node-native-evidence.json", engine_node)''')
        text = text.replace('engine_managed["checks"] == 855', 'engine_managed["checks"] == 889')
        text = text.replace('{"normal":(216,9,9),"original-seh":(155,8,4),"original-cpp":(151,8,4),"detach":(192,11,7),"observer-fault":(141,8,4)}', '{"normal":(227,9,9),"original-seh":(158,8,4),"original-cpp":(154,8,4),"detach":(203,11,7),"observer-fault":(147,8,4)}')
        text = text.replace('    write("engine-node-managed-evidence.json", engine_managed)', '''    assert engine_managed["executeUint32TransportVerified"] and engine_managed["actualIdentityLedgerCaptureVerified"]
    assert engine_managed["trackerIdentityProvidersControlled"] and not engine_managed["actualTrackerBackendExecuted"]
    assert all(row["actualIdentityLedgerCaptureVerified"] and len(row["identityChains"]) == (2 if row["mode"] in {"normal", "detach"} else 1) for row in engine_managed["modes"])
    assert sum(row["checks"] for row in engine_managed["modes"]) == engine_managed["checks"]
    assert next(row for row in engine_managed["modes"] if row["mode"] == "normal")["executeHighBitReturns"][0] == 0xabcdef01
    write("engine-node-managed-evidence.json", engine_managed)''')
        text = text.replace('This batch adds exact native UnityPlayer-shaped node transport, copied scalar bridge/dispatcher and an actual Adapter node ledger in self-owned joint fixtures;', 'This batch corrects Execute to complete uint32 EAX with return ABI2, adds rooted official cached-owner projection and actual tracker identity/node ledgers in self-owned joint fixtures; controlled providers and metadata remain separate from actual game object evidence;')
    target.write_text(text, encoding='utf-8', newline='\n')
print('Prepared separate evidence procedures; previous frozen batch unchanged.')
