"""Prepare the actual pre-init producer evidence tools; do not edit old batches."""
import json
import shutil
from pathlib import Path
root = Path(__file__).resolve().parent
previous = root.parent / "foundation-first-install-20261024"
for name in ["freeze-source.py", "freeze-package.py", "verify-terminal-evidence.py"]:
    body = (previous / name).read_text(encoding="utf-8-sig")
    if name == "freeze-package.py":
        body = body.replace('== 18', '== 22').replace('The 18 declared', 'The 22 declared')
    (root / name).write_text(body, encoding="utf-8")
body = (previous / "summarize-build.py").read_text(encoding="utf-8-sig")
body = body.replace("bootstrap-attach-final-20261024", "preinit-producer-final-20261024")
needle = '    total = sum(s["total"] for s in suites)'
extra = '''    runtime = read(args.native_output / "runtime-invoke" / "fixture-result.json")
    assert runtime["passed"] and runtime["failures"] == 0 and runtime["originalCalls"] == 12
    assert runtime["entered"] == runtime["exited"] == 12 and runtime["inFlight"] == 0
    assert runtime["afterCalls"] == 8 and runtime["oldUnobservedCalls"] == 2 and runtime["observerFaults"] == 2
    assert not runtime["gameRuntimeVerified"]
    write("native-runtime-invoke-evidence.json", runtime)
    rejected = [json.loads(line) for line in (args.native_output / "first-install-rejected" / "fixture-result.json").read_text(encoding="utf-8-sig").splitlines() if line.startswith("{")]
    assert len(rejected) == 5 and all(row["passed"] and row["failures"] == 0 and row["writesStarted"] == 0 for row in rejected)
    write("native-rejection-evidence.json", dict(cases=rejected, passed=True, gameRuntimeVerified=False))
    mapped = read(args.native_output / "first-install-mapped" / "fixture-result.json")
    assert mapped["passed"] and mapped["failures"] == 0 and mapped["phase"] == 3 and mapped["unknown"] == 0 and mapped["writesStarted"] == 3
    assert mapped["mappedImageOnly"] and not mapped["gameCodeExecuted"] and not mapped["gameRuntimeVerified"]
    write("mapped-publication-evidence.json", mapped)
    doorstop = read(args.native_output / "doorstop" / "doorstop-manifest.json")
    assert doorstop["upstreamArchiveSha256"] == "a71789ffcb359f6a37c6d2c7bbdd0527231e9db00bf46637693cff88ba7ffb80"
    write("doorstop-evidence.json", doorstop)
'''
assert needle in body
body = body.replace(needle, extra + needle)
body = body.replace('nativeBootstrapAttach=bootstrap, logSha256=', 'nativeBootstrapAttach=bootstrap, nativeRuntimeInvoke=runtime, mappedPublication=mapped, logSha256=')
body = body.replace('(?:CS|MSB|NU)\\d+', '(?:CS|MSB|NU|C|D|LNK)\\d+')
body = body.replace('Production Doorstop pre-init installation is not implemented or accepted.', 'The production Doorstop pre-init producer and managed adoption path are implemented and built; mapped-image publication validates bytes without executing game code. Real supported startup timing, actual CLR production callbacks, listener closure and game acceptance remain unverified.')
body = body.replace('".cpp", ".h", ".asm", ".json"', '".cpp", ".c", ".h", ".asm", ".json"')
needle = '    for name in ["Directory.Build.props",'
body = body.replace(needle, '''    path = repo / "native/RizToolbox.Doorstop/RIZLINE-MODIFICATIONS.md"
    files.append(dict(path=path.relative_to(repo).as_posix(), sha256=sha(path)))
''' + needle)
needle = '    initialization = root / "native-build" / "initialization-abi"'
extra = '''    for folder in ["runtime-invoke", "first-install-rejected", "first-install-mapped", "doorstop"]:
        destination = root / "native-build" / folder
        destination.mkdir(exist_ok=True)
        for source in (args.native_output / folder).iterdir():
            if source.is_file() and source.suffix in {".json", ".log", ".txt", ".exe", ".dll", ".zip"}:
                shutil.copy2(source, destination / source.name)
'''
assert needle in body
body = body.replace(needle, extra + needle)
body = body.replace('assert len(staged["files"]) == 18', 'assert len(staged["files"]) == 22')
body = body.replace('dict(stagedMatched=18,', 'dict(stagedMatched=22,')
(root / "summarize-build.py").write_text(body, encoding="utf-8")
body = (previous / "compare-sources.py").read_text(encoding="utf-8-sig")
body = body.replace('previous = root.parent / "foundation-void-admission-20261024"', f'previous = root.parent / "{previous.name}"')
body = body.replace("1507", "1511").replace('tofile="bootstrap-attach/"', 'tofile="preinit-producer/"')
(root / "compare-sources.py").write_text(body, encoding="utf-8")
body = (previous / "check-docs.py").read_text(encoding="utf-8-sig")
body = body.replace("2026-10-24_底层-启动观察接入-report.md", "2026-10-24_底层-原生启动安装-report.md")
(root / "check-docs.py").write_text(body, encoding="utf-8")
names = ["terminal-evidence.json", "terminal-disassembly.txt", "terminal-metadata.txt", "direct-call-candidates.json", "mid-site-evidence.json"]
for name in names: shutil.copy2(previous / name, root / name)
(root / "static-evidence-origin.json").write_text(json.dumps(dict(previousBatch=previous.name, copiedFiles=names,
    scope="Pinned prior static evidence rechecked separately; mapped publication executes no game code and does not prove real startup ordering."), indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(prepared=True, built=False)))
