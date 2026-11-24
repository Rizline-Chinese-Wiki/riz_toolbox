"""Prepare late-attach evidence tools; earlier batches stay immutable."""
import json
import shutil
from pathlib import Path

root = Path(__file__).resolve().parent
previous = root.parent / "foundation-void-admission-20261024"
for name in ["freeze-source.py", "freeze-package.py", "verify-terminal-evidence.py"]:
    shutil.copy2(previous / name, root / name)
body = (previous / "summarize-build.py").read_text(encoding="utf-8-sig")
body = body.replace("void-admission-final-20261024", "bootstrap-attach-final-20261024")
needle = '    total = sum(s["total"] for s in suites)'
extra = '''    bootstrap = read(args.native_output / "bootstrap-void" / "fixture-result.json")
    assert bootstrap["passed"] and bootstrap["failures"] == 0 and bootstrap["checks"] > 0
    assert not bootstrap["gameRuntimeVerified"]
    assert "PASS Native bootstrap adopted bridge preserves pre-attach physical calls and exact late-attached native mid callbacks" in body
    write("native-bootstrap-evidence.json", bootstrap)
'''
assert needle in body
body = body.replace(needle, extra + needle)
body = body.replace('nativeVoidAdmission=admission, logSha256=', 'nativeVoidAdmission=admission, nativeBootstrapAttach=bootstrap, logSha256=')
body = body.replace('Staged package is not installed.', 'This batch additionally verifies a one-time native observer attach with per-invocation snapshots and a CLR bridge adopting a configured slot. Production Doorstop pre-init installation is not implemented or accepted. Staged package is not installed.')
needle = '    initialization = root / "native-build" / "initialization-abi"'
extra = '''    bootstrap_output = root / "native-build" / "bootstrap-void"
    bootstrap_output.mkdir(exist_ok=True)
    for name in ["build.log", "fixture-result.json", "fixture-manifest.json", "unwindinfo.txt", "BootstrapVoid.Fixture.exe"]:
        shutil.copy2(args.native_output / "bootstrap-void" / name, bootstrap_output / name)
'''
assert needle in body
body = body.replace(needle, extra + needle)
(root / "summarize-build.py").write_text(body, encoding="utf-8")
body = (previous / "compare-sources.py").read_text(encoding="utf-8-sig")
body = body.replace('previous = root.parent / "foundation-event-mid-parent-20261024"', f'previous = root.parent / "{previous.name}"')
body = body.replace('assert len(added) == 5 and not removed', 'assert added and not removed')
body = body.replace("1496", "1507").replace('tofile="void-admission/"', 'tofile="bootstrap-attach/"')
(root / "compare-sources.py").write_text(body, encoding="utf-8")
body = (previous / "check-docs.py").read_text(encoding="utf-8-sig")
body = body.replace("2026-10-24_底层-原生调用准入-report.md", "2026-10-24_底层-启动观察接入-report.md")
(root / "check-docs.py").write_text(body, encoding="utf-8")
names = ["terminal-evidence.json", "terminal-disassembly.txt", "terminal-metadata.txt", "direct-call-candidates.json", "mid-site-evidence.json"]
for name in names:
    shutil.copy2(previous / name, root / name)
(root / "static-evidence-origin.json").write_text(json.dumps(dict(previousBatch=previous.name, copiedFiles=names,
    scope="Pinned static evidence reused. Bootstrap observer transport does not prove safe production first-install timing."), indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(prepared=True, built=False)))
