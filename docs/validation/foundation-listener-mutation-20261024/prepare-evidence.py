"""Adapt the previous batch's verified archive procedures; never run a build."""
from pathlib import Path

batch = Path(__file__).resolve().parent
previous = batch.parent / "foundation-listener-clr-20261024"
for name in ["freeze-source.py", "freeze-package.py", "verify-frozen-inputs.py"]:
    (batch / name).write_text((previous / name).read_text(encoding="utf-8-sig"), encoding="utf-8", newline="\n")
body = (previous / "compare-sources.py").read_text(encoding="utf-8-sig")
body = body.replace('"foundation-listener-publication-20261024"', '"foundation-listener-clr-20261024"')
body = body.replace('"listener-publication/"', '"listener-clr/"').replace('tofile="listener-clr/"', 'tofile="listener-mutation/"')
body = body.replace("changes-from-listener-publication", "changes-from-listener-clr")
(batch / "compare-sources.py").write_text(body, encoding="utf-8", newline="\n")
body = (previous / "summarize-build.py").read_text(encoding="utf-8-sig")
body = body.replace("listener-clr-final-20261024", "listener-mutation-final-20261024")
needle = '    total = sum(s["total"] for s in suites)'
assert needle in body
extra = '''    operation = read(args.native_output / "operation-native" / "fixture-result.json")
    assert operation["passed"] and operation["checks"] == 1156 and operation["failures"] == 0
    assert operation["originalCalls"] == operation["entered"] == operation["exited"] == 72
    assert not any(operation[k] for k in ["gameCodeExecuted", "gameRuntimeVerified", "realDobbyPreparedCommit", "firstInstallSafetyVerified", "exceptionFlagsTransparencyVerified"])
    write("operation-native-evidence.json", operation)
    operation_managed = read(args.native_output / "operation-managed" / "fixture-result.json")
    assert operation_managed["passed"] and operation_managed["independentProcesses"] == 5 and operation_managed["checks"] == 259
    assert sum(row["actualOriginalCalls"] for row in operation_managed["modes"]) == 18
    for row in operation_managed["modes"]:
        assert row["passed"] and row["modeVerified"] and row["checks"] > 0 and row["failures"] == row["nativeInFlight"] == 0
        assert row["productionBridgeAndBrokerVerified"] and row["nativeExceptionsCaughtBeforePInvoke"] and row["diskHashesUnchanged"]
        assert not any(row[k] for k in ["gameCodeExecuted", "gameRuntimeVerified", "firstInstallSafetyVerified", "actualIl2CppMetadataResolved", "actualLedgerCaptureVerified", "dobbyPublicationVerified", "installed"])
    write("operation-managed-evidence.json", operation_managed)
'''
body = body.replace(needle, extra + needle)
body = body.replace('listenerNative=listener, logSha256=sha(log)', 'listenerNative=listener, operationNative=operation, operationManaged=operation_managed, logSha256=sha(log)')
body = body.replace('"listener-managed-fixture"]:', '"listener-managed-fixture", "operation-native", "operation-managed", "operation-fixture"]:')
body = body.replace('New package remains staged, not installed.',
    'This batch additionally wires twelve pinned mutation operation declarations, records actual coroutine handle identities, and tests production dispatch-root adoption fault ordering. Operation direct-entry native and CLR fixtures do not prove Dobby installation or game first-write safety; mutation ledger graphs remain controlled. Stop hooks, cancellation completion, all writers/leaves and resource retirement remain open. New package remains staged, not installed.')
(batch / "summarize-build.py").write_text(body, encoding="utf-8", newline="\n")
print("Evidence procedures prepared; no build or source mutation performed.")
