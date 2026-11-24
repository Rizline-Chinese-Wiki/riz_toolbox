"""Create batch-local evidence tools without changing the frozen previous batch."""
from pathlib import Path
import json
import shutil

root = Path(__file__).resolve().parent
previous = root.parent / "foundation-native-void-20261024"
for name in ["freeze-source.py", "freeze-package.py", "verify-terminal-evidence.py"]:
    shutil.copy2(previous / name, root / name)
body = (previous / "summarize-build.py").read_text(encoding="utf-8-sig")
body = body.replace("native-void-final-20261024", "event-mid-final-20261024")
needle = '    total = sum(s["total"] for s in suites)'
extra = '''    mid = read(args.native_output / "event-mid-context" / "fixture-result.json")
    assert mid["kind"] == "native-event-mid-full-state-fixture"
    assert mid["passed"] and mid["checks"] > 0 and mid["failures"] == 0
    assert mid["entered"] == mid["exited"] and mid["inFlight"] == 0
    assert all(mid[key] for key in ["allGpr", "allXmm", "rflags", "mxcsr", "fpcsr", "stackCanaries", "exactFiveByteCall", "nearTailRelay", "fixtureOnlyPatch"])
    assert not mid["nativeParentVerified"] and not mid["gameRuntimeVerified"]
    write("native-mid-evidence.json", mid)
'''
assert needle in body
body = body.replace(needle, extra + needle)
body = body.replace('typedInitializationNativeUnwind=native_unwind, logSha256=sha(log)', 'typedInitializationNativeUnwind=native_unwind, nativeEventMid=mid, logSha256=sha(log)')
body = body.replace('    initialization = root / "native-build" / "initialization-abi"', '''    mid_output = root / "native-build" / "event-mid-context"
    mid_output.mkdir(exist_ok=True)
    for name in ["build.log", "fixture-result.json", "fixture-manifest.json", "unwindinfo.txt", "disassembly.txt", "EventMidContext.Fixture.exe"]:
        shutil.copy2(args.native_output / "event-mid-context" / name, mid_output / name)
    initialization = root / "native-build" / "initialization-abi"''')
body = body.replace('Staged package is not installed.', 'The new standalone native mid fixture covers its own five-byte CALL and register/unwind transport; controlled Hooks/Adapter cases cover physical identity and root adoption. Combined real native parent to mid to managed Adapter, concurrent instruction publication, actual game execution and listener closure remain unverified. Staged package is not installed.')
(root / "summarize-build.py").write_text(body, encoding="utf-8")
body = (previous / "compare-sources.py").read_text(encoding="utf-8-sig")
body = body.replace('foundation-unity-events-20261023', previous.name).replace('1449', '1475')
body = body.replace('native-void/', 'event-mid/').replace('assert len(added) == 7 and not removed', 'assert len(added) == 8 and not removed')
(root / "compare-sources.py").write_text(body, encoding="utf-8")
names = ["terminal-evidence.json", "terminal-disassembly.txt", "terminal-metadata.txt", "direct-call-candidates.json"]
for name in names:
    shutil.copy2(previous / name, root / name)
(root / "static-evidence-origin.json").write_text(json.dumps(dict(
    previousBatch=previous.name, copiedFiles=names,
    scope="Previously pinned ranges retained and rechecked; mid-site-evidence.json separately records the bounded new Invoke NOP/unwind analysis. Neither is game runtime acceptance."), indent=2) + "\n", encoding="utf-8")
body = (previous / "check-docs.py").read_text(encoding="utf-8-sig")
body = body.replace('2026-10-24_底层-原生void父分派-report.md', '2026-10-24_底层-事件内段上下文-report.md')
(root / "check-docs.py").write_text(body, encoding="utf-8")
print(json.dumps(dict(preparedTools=6, copiedStaticEvidence=len(names), built=False)))
