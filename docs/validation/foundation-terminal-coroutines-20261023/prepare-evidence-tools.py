"""Prepare batch-local evidence tooling; old frozen sources remain immutable."""
from pathlib import Path

root = Path(__file__).resolve().parent
previous = root.parent / "foundation-initialization-coroutines-20261023"
for name in ["freeze-source.py", "freeze-package.py"]:
    (root / name).write_bytes((previous / name).read_bytes())
body = (previous / "summarize-build.py").read_text(encoding="utf-8-sig").replace("initialization-coroutines-final-20261023", "terminal-coroutines-final-20261023")
needle = '    total = sum(s["total"] for s in suites)'
assert body.count(needle) == 1
body = body.replace(needle, '    assert "PASS native coroutine Factory preserves full references and publishes an actual enum before a named Start first MoveNext" in body\n' + needle)
body = body.replace("the new Adapter tracker/backend and launch/menu composition", "the new Adapter initial/terminal tracker/backends and launch/result/menu composition")
(root / "summarize-build.py").write_text(body, encoding="utf-8")
body = (previous / "compare-sources.py").read_text(encoding="utf-8-sig").replace("foundation-coroutine-boundaries-20261023", "foundation-initialization-coroutines-20261023")
body = body.replace('fromfile="1324/"', 'fromfile="1343/"').replace('tofile="1343/"', 'tofile="1360/"').replace("changes-from-1324", "changes-from-1343")
(root / "compare-sources.py").write_text(body, encoding="utf-8")
body = (previous / "check-documentation.py").read_text(encoding="utf-8-sig").replace("2026-10-23_底层-真实初始化协程追踪-report.md", "2026-10-23_底层-结算协程与按名启动-report.md").replace("== 1343 and", "== 1360 and")
(root / "check-documentation.py").write_text(body, encoding="utf-8")
print("Prepared five local archive/status tools; historical evidence unchanged.")
