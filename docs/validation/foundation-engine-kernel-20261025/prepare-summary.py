"""Adapt the prior complete evidence checks for this full kernel build.

The prior file is read as a template and remains untouched. This only writes
the current batch's offline evidence summarizer; it executes no target code.
"""
from pathlib import Path

batch=Path(__file__).resolve().parent
previous=batch.parent/'foundation-early-loader-20261025/summarize-build.py'
body=previous.read_text(encoding='utf-8-sig')
def replace_once(old,new):
    global body
    assert body.count(old)==1,old
    body=body.replace(old,new,1)
replace_once('early-loader-aligned-final-20261025','engine-kernel-full-final-20261025')
replace_once('    total = sum(s["total"] for s in suites)', '''    kernel = read(args.native_output / "engine-node-kernel-cases/fixture-result.json")
    assert kernel["passed"] and kernel["allHeldInputHashesUnchanged"]
    assert kernel["nativeRegressionProcesses"] == 1 and kernel["managedRegressionProcesses"] == 5
    assert len(kernel["entryProcesses"]) == 1 and kernel["entryProcesses"][0]["exitCode"] == 0
    assert not kernel["entryProcesses"][0]["timedOut"] and kernel["actualOriginalCalls"] == 180
    assert kernel["nativeRegression"]["checks"] == 3402 and kernel["nativeRegression"]["actualOriginalCalls"] == 136
    assert kernel["managedRegression"]["checks"] == 889 and kernel["managedRegression"]["actualOriginalCalls"] == 44
    assert kernel["entryProof"]["passed"] and kernel["entryProof"]["staticEntryTablesVerified"]
    assert kernel["entryProof"]["actualEntryFixtureChecks"] == 1606 and kernel["entryProof"]["checks"] == 1486
    assert kernel["entryProof"]["tableDir64Relocations"] == 128 and kernel["entryProof"]["entryPointerRejections"] == 256
    assert len(kernel["entryProof"]["tableProofs"]) == 4 and kernel["entryProof"]["invalidCoordinateRequests"] == 12
    assert all(row["beforeSha256"] == row["afterSha256"] for row in kernel["files"])
    changed_after = [dict(path=row["path"],role=row["role"],duringRunSha256=row["afterSha256"],
        currentSha256=sha(Path(row["path"]))) for row in kernel["files"] if sha(Path(row["path"])) != row["afterSha256"]]
    assert len(changed_after) == 1
    assert changed_after[0]["role"] == "supplied-managed-output-file"
    assert Path(changed_after[0]["path"]).name == "RizToolbox.JudgeContext.dll"
    assert changed_after[0]["duringRunSha256"] == "01e5171d2e7a824308e7e9053f6959fa7cbea98bb2c2742e1571d8bf75a66f48"
    assert changed_after[0]["currentSha256"] == sha(args.native_output / "RizToolbox.JudgeContext.dll")
    recovered = root.parent / "foundation-early-fixed-20261025/raw/build/RizToolbox.JudgeContext.dll"
    assert sha(recovered) == changed_after[0]["duringRunSha256"]
    write("kernel-post-run-files.json",dict(heldInputHashesUnchangedDuringKernelRun=True,changedAfterRunnerReturned=changed_after,
        cause="Later Build.ps1 Test-FirstInstallManaged.ps1 copies the current legacy helper beside Runtime.Tests after Kernel runner released its locks.",
        causeSource="scripts/Test-FirstInstallManaged.ps1:29",recoveredExactDuringRunFile=str(recovered),
        scope="The Kernel path and every other supplied input still match. No historical held-file hash was rewritten."))
    assert not any(kernel[key] for key in ["allTransitiveOsDependenciesSafeVerified","firstInstallSafetyVerified",
        "productionV4Issued","schedulerClosureVerified","engineCancellationCompleted","gameCodeExecuted","installed"])
    write("engine-kernel-evidence.json", kernel)
    total = sum(s["total"] for s in suites)''')
replace_once('engineNodeManaged=engine_managed, earlyLoader=early, logSha256=sha(log)',
    'engineNodeManaged=engine_managed, earlyLoader=early, engineKernel=kernel, logSha256=sha(log)')
needle='    write("source-manifest.json", dict(files=sorted(files, key=lambda f: f["path"])))'
replace_once(needle,'''    path = repo / "docs/validation/foundation-engine-kernel-20261025/verify-engine-kernel.py"
    files.append(dict(path=path.relative_to(repo).as_posix(), sha256=sha(path)))
    frozen = read(root / "build-inputs-before.json")["files"]
    assert len(files) == len(frozen) == 599
    assert sorted(files,key=lambda f:f["path"]) == frozen
    write("build-input-integrity-check.json",dict(checked=len(frozen),preBuildMatchesAfterBuild=True,changed=[]))
'''+needle)
replace_once('    for folder in ["early-loader-fixture", "early-loader-cases"]:',
    '    for folder in ["early-loader-fixture", "early-loader-cases", "engine-node-kernel", "engine-node-kernel-cases"]:')
replace_once('earlyLoaderCases=39, earlyLoaderParentAssertions=early["checks"]',
    'earlyLoaderCases=39, earlyLoaderParentAssertions=early["checks"],kernelProcesses=7,kernelNativeChecks=3402,kernelManagedChecks=889,kernelEntryChecks=1606,kernelParentAssertions=1486')
replace_once('    assert len(staged["files"]) == 22','    assert len(staged["files"]) == 23')
replace_once('dict(stagedMatched=22,','dict(stagedMatched=23,')
replace_once('This batch corrects Execute to complete uint32 EAX with return ABI2, adds rooted official cached-owner projection and actual tracker identity/node ledgers in self-owned joint fixtures; controlled providers and metadata remain separate from actual game object evidence;',
    'This batch independently builds the canonical ABI2 engine node transport into an eight-export no-entry/no-TLS/no-CRT kernel, verifies all128 constant entries and actual native/CLR regressions while preserving the legacy helper; controlled providers and metadata remain separate from actual game object evidence;')
(batch/'summarize-build.py').write_text(body,encoding='utf-8',newline='\n')
print('Prepared current full-build summarizer; prior template unchanged.')
