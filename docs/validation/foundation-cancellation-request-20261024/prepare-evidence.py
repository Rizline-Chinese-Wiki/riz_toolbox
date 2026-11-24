"""Prepare this batch's independent evidence procedures, without running a build."""
from pathlib import Path

batch = Path(__file__).resolve().parent
prior = batch.parent / "foundation-listener-mutation-20261024"
for name in ["freeze-source.py", "freeze-package.py", "verify-frozen-inputs.py"]:
    (batch / name).write_text((prior / name).read_text(encoding="utf-8-sig"), encoding="utf-8", newline="\n")
body = (prior / "compare-sources.py").read_text(encoding="utf-8-sig")
body = body.replace('"foundation-listener-clr-20261024"', '"foundation-listener-mutation-20261024"')
body = body.replace('fromfile="listener-clr/"', 'fromfile="listener-mutation/"').replace('tofile="listener-mutation/"', 'tofile="cancellation-request/"')
body = body.replace("changes-from-listener-clr", "changes-from-listener-mutation")
(batch / "compare-sources.py").write_text(body, encoding="utf-8", newline="\n")
body = (prior / "summarize-build.py").read_text(encoding="utf-8-sig")
body = body.replace("listener-mutation-final-20261024", "cancellation-request-final-20261024")
marker = '    total = sum(s["total"] for s in suites)'
assert marker in body
extra = '''    operation_dobby = read(args.native_output / "operation-dobby" / "fixture-result.json")
    assert operation_dobby["passed"] and operation_dobby["checks"] == 1165 and operation_dobby["failures"] == 0
    assert operation_dobby["originalCalls"] == operation_dobby["entered"] == operation_dobby["exited"] == 69
    assert operation_dobby["totalActualOriginalCalls"] == 70 and operation_dobby["realDobbyPreparedCommit"]
    assert operation_dobby["preparedTargets"] == 4 and operation_dobby["committedTargets"] == operation_dobby["commitRejections"] == 3
    assert not any(operation_dobby[key] for key in ["gameCodeExecuted", "gameRuntimeVerified", "firstInstallSafetyVerified", "concurrentFirstPublicationVerified", "partialPublicationFailureInjected", "exceptionFlagsTransparencyVerified", "nativeAdmissionGateVerified"])
    write("operation-dobby-evidence.json", operation_dobby)
    operation_ledger = read(args.native_output / "operation-ledger" / "fixture-result.json")
    assert operation_ledger["passed"] and operation_ledger["independentProcesses"] == 5 and operation_ledger["checks"] > 0
    for row in operation_ledger["modes"]:
        assert row["passed"] and row["modeVerified"] and row["failures"] == 0
        assert row["actualCancellationRequestLedgerVerified"] and row["cancellationRequests"] == row["cancellationCompletions"] == row["cancellationDisposals"] == 2
        assert row["identityProviderControlled"] and not row["productionStopBindingVerified"] and not row["actualTrackerProviderUsed"]
        assert not row["engineCancellationExecuted"] and not row["gameCodeExecuted"]
    write("operation-ledger-evidence.json", operation_ledger)
'''
body = body.replace(marker, extra + marker)
body = body.replace('operationManaged=operation_managed, logSha256=sha(log)', 'operationManaged=operation_managed, operationDobby=operation_dobby, operationLedger=operation_ledger, logSha256=sha(log)')
body = body.replace('"operation-fixture"]:', '"operation-fixture", "operation-dobby", "operation-dobby-fixture", "operation-ledger"]:')
body = body.replace('New package remains staged, not installed.',
    'This batch adds six Stop declarations and request-only cancellation identity ledger, a shared Pending hold before provider access and guarded readiness/retirement. Separate real Dobby installation and actual native-to-ledger callbacks are offline self-owned evidence; they do not grant concurrent game first-publication, actual Stop engine completion, IL2CPP GC or whole-run retirement. New package remains staged, not installed.')
body = body.replace('Stop hooks, cancellation completion, all writers/leaves and resource retirement remain open.',
    'Engine cancellation completion, all writers/leaves and whole-run resource retirement remain open; the current batch wires request observation only.')
(batch / "summarize-build.py").write_text(body, encoding="utf-8", newline="\n")
print("Archive/summary procedures prepared; source files and native processes unchanged.")
