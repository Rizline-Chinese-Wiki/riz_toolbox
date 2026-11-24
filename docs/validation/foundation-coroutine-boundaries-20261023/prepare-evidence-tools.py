"""Copy prior evidence tooling into this batch without changing historical evidence."""
from pathlib import Path

root = Path(__file__).resolve().parent
previous = root.parent / "foundation-menu-handoff-20261023"
for name in ["freeze-source.py", "freeze-package.py"]:
    (root / name).write_bytes((previous / name).read_bytes())

body = (previous / "summarize-build.py").read_text(encoding="utf-8-sig")
body = body.replace("menu-handoff-final-20261023", "coroutine-boundaries-final-20261023")
needle = '    total = sum(s["total"] for s in suites)'
assert body.count(needle) == 1
body = body.replace(needle, '    assert "PASS native coroutine Start and MoveNext preserve real Dobby ABI, caller, worker, nested publication and SEH C++ unwind" in body\n' + needle)
body = body.replace("Managed inner Judge/Hold and Play/Progress/Timeline original unwind, game image metadata, full activation, active consumer retirement and actual game acceptance remain separate/unverified.",
    "Coroutine Start/MoveNext additionally preserve exact assembly return labels through a tail relay/Dobby, synchronous first MoveNext, raw AL/reference returns, worker/detach and original SEH/C++ exits. Managed inner Judge/Hold and Play/Progress/Timeline original unwind, actual game metadata/coroutine ownership, full launch, active consumer retirement and game acceptance remain separate/unverified.")
(root / "summarize-build.py").write_text(body, encoding="utf-8")

body = (previous / "verify-menu-evidence.py").read_text(encoding="utf-8-sig").replace("menu-evidence", "coroutine-evidence")
needle = 'result = dict(ranges=len(proof["ranges"])'
assert body.count(needle) == 1
body = body.replace(needle, 'for constant in proof["constants"]:\n    assert pe.get_data(int(constant["rva"], 16), 4).hex() == constant["bytes"]\n' + needle)
(root / "verify-coroutine-evidence.py").write_text(body, encoding="utf-8")

body = (previous / "compare-sources.py").read_text(encoding="utf-8-sig")
body = body.replace("foundation-judge-drain-20261023", "foundation-menu-handoff-20261023")
body = body.replace("assert len(added) == 5 and len(changed) == 7 and not removed", "assert len(added) == 7 and not removed")
body = body.replace('fromfile="1298/"', 'fromfile="1315/"').replace('tofile="1315/"', 'tofile="1324/"')
body = body.replace("changes-from-1298", "changes-from-1315")
(root / "compare-sources.py").write_text(body, encoding="utf-8")

body = (previous / "check-documentation.py").read_text(encoding="utf-8-sig")
body = body.replace("2026-10-23_底层-菜单输入与相机交接-report.md", "2026-10-23_底层-协程原生观察边界-report.md")
body = body.replace('["README.md", "scope.md", "review.md"]', '["README.md", "scope.md", "review.md", "next-integration.md"]')
body = body.replace('== 1315 and', '== 1324 and')
(root / "check-documentation.py").write_text(body, encoding="utf-8")
print("Prepared six batch-local evidence tools; historical inputs unchanged.")
