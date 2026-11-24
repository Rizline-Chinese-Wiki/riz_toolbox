"""Prepare admission evidence tools without rewriting prior frozen batches."""
from pathlib import Path
import json
import shutil

root = Path(__file__).resolve().parent
previous = root.parent / "foundation-event-mid-parent-20261024"
for name in ["freeze-source.py", "freeze-package.py", "verify-terminal-evidence.py"]:
    shutil.copy2(previous / name, root / name)
body = (previous / "summarize-build.py").read_text(encoding="utf-8-sig")
body = body.replace("event-mid-parent-final-20261024", "void-admission-final-20261024")
needle = '    total = sum(s["total"] for s in suites)'
extra = '''    admission = read(args.native_output / "void-admission" / "fixture-result.json")
    assert admission["passed"] and admission["failures"] == 0 and admission["checks"] > 0
    assert not admission["initialInstallSafetyVerified"] and not admission["gameRuntimeVerified"]
    assert "PASS Native admission typed receipt and statistics preserve the exact 32 and 40 byte ABI" in body
    assert "PASS Native admission writes and retained unknown outcomes prevent Dispose from opening the gate" in body
    write("native-admission-evidence.json", admission)
'''
assert needle in body
body = body.replace(needle, extra + needle)
body = body.replace('nativeEventMid=mid, logSha256=', 'nativeEventMid=mid, nativeVoidAdmission=admission, logSha256=')
body = body.replace('Staged package is not installed.', 'This batch additionally verifies native admission after an existing Dobby parent and the real bridge lease. Its first installation safety and a production patch publisher remain unverified. Staged package is not installed.')
needle = '    initialization = root / "native-build" / "initialization-abi"'
extra = '''    admission_output = root / "native-build" / "void-admission"
    admission_output.mkdir(exist_ok=True)
    for name in ["build.log", "fixture-result.json", "fixture-manifest.json", "unwindinfo.txt", "VoidAdmission.Fixture.exe"]:
        shutil.copy2(args.native_output / "void-admission" / name, admission_output / name)
'''
assert needle in body
body = body.replace(needle, extra + needle)
(root / "summarize-build.py").write_text(body, encoding="utf-8")
body = (previous / "compare-sources.py").read_text(encoding="utf-8-sig")
body = body.replace('previous = root.parent / "foundation-event-mid-20261024"', f'previous = root.parent / "{previous.name}"')
body = body.replace('assert len(added) == 1 and not removed', 'assert len(added) == 5 and not removed')
body = body.replace("1490", "1496").replace('tofile="event-mid-parent/"', 'tofile="void-admission/"')
(root / "compare-sources.py").write_text(body, encoding="utf-8")
body = (previous / "check-docs.py").read_text(encoding="utf-8-sig")
body = body.replace("2026-10-24_底层-事件父链联合验证-report.md", "2026-10-24_底层-原生调用准入-report.md")
(root / "check-docs.py").write_text(body, encoding="utf-8")
names = ["terminal-evidence.json", "terminal-disassembly.txt", "terminal-metadata.txt", "direct-call-candidates.json", "mid-site-evidence.json"]
for name in names:
    shutil.copy2(previous / name, root / name)
(root / "static-evidence-origin.json").write_text(json.dumps(dict(previousBatch=previous.name, copiedFiles=names,
    scope="Pinned static evidence reused. Admission fixtures do not add game-runtime or publication-safety claims."), indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(prepared=True, built=False)))
