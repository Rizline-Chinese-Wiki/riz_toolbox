"""Update the four status entry documents after final terminal and freeze proof."""
import hashlib
import json
import re
import zipfile
from pathlib import Path

batch = Path(__file__).resolve().parent
repo = batch.parents[2]


def read(name):
    return json.loads((batch / name).read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


summary, run = read("build-summary.json"), read("build-run.json")
assert run["exitCode"] == 0 and run["sessionId"] == 21231 and not run["installed"] and not run["gameExecuted"]
assert run["logSha256"] == summary["logSha256"] == sha(batch / "build.log")
assert summary["passed"] == summary["total"] == 1521 and summary["projects"] == len(summary["suites"]) == 25
assert all(suite["passed"] == suite["total"] for suite in summary["suites"])
manifest = read("source-manifest.json")
assert read("build-inputs-before.json")["files"] == manifest["files"]
assert read("build-input-integrity-check.json")["currentChanged"] == []
assert len(manifest["files"]) == 502
for proof, entries, current_root in [
    (read("source-archive-check.json"), manifest["files"], repo),
    (read("package-archive-check.json"), read("package-manifest.json")["files"], repo / "artifacts/package"),
]:
    assert proof["entries"] == len(entries) and proof["allEntryHashesMatched"]
    archive_path = batch / proof["archive"]
    assert sha(archive_path) == proof["archiveSha256"]
    with zipfile.ZipFile(archive_path) as archive:
        assert set(archive.namelist()) == {row["path"] for row in entries} and archive.testzip() is None
        for row in entries:
            assert sha(current_root / row["path"]) == hashlib.sha256(archive.read(row["path"])).hexdigest() == row["sha256"]
assert len(read("package-manifest.json")["files"]) == 22
assert read("staging-check.json")["allMatched"] and not read("staging-check.json")["installed"]
assert read("previous-installation-check.json")["allMatched"] and read("original-game-check.json")["allUnchanged"]
callbacks = read("listener-managed-evidence.json")
assert callbacks["passed"] and callbacks["checks"] == 1630 and callbacks["independentProcesses"] == 12
assert not callbacks["gameCodeExecuted"] and not callbacks["firstInstallReceiptUsed"]
assert all(row["passed"] and row["modeVerified"] and row["nativeInFlight"] == 0 for row in callbacks["modes"])
assert sum(row["actualLedgerCaptureVerified"] for row in callbacks["modes"]) == 3
diagnostics = len(summary["compilerDiagnosticLines"])
changes = read("changes-from-listener-publication.json")
assert changes["previousInputs"] == 493 and changes["currentInputs"] == 502
assert len(changes["added"]) == 9 and len(changes["changed"]) == 10 and changes["removed"] == []


def latest(prefix):
    return ("<!-- LISTENER-CLR-LATEST:BEGIN -->\n"
        f'- **最新：listener 回调与账本已连通，离线1521/1521、25套通过。** [证据]({prefix}docs/validation/{batch.name}/README.md)：502源/22包冻结未安装，Runtime160/Hooks177/Audio573。真实自有native→CLR bridge→生产共享broker core，normal/SEH/C++三场景进入实际ledger；图和根仍controlled。\n'
        f'- **本批实证**：12独立进程1630检查，覆盖三site、双sink、异常、target-read AV无Before的Abnormal(token0)、旧call跨Attach、detach/GC、worker、same/cross-slot nested、foreign-original和观察故障。rawAV不经过broker；非固定游戏receipt回调，非IL2CPP/GC或全部delegate leaves证明。full session21231 exit0，保留{diagnostics}条去重原始编译诊断（含ANSI变体及既有warning）。\n'
        '- **余项**：真实固定游戏receipt callbacks/冷启动、动态leaves与mutation/provider转交和取消、全部future及消费者资源退休、完整Retry/Return/Loader仍缺。RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false；完整B01–B19 active，仅B03整项/B18a子项勾选，用户“实测稍后”保持。游戏仍1021安装18项/其326源归档与原游戏三文件不变。\n'
        "<!-- LISTENER-CLR-LATEST:END -->")


pending = {}
for name, prefix in [(".docs/CURRENT-CHECKPOINT.md", "../"), ("docs/IMPLEMENTATION-STATUS.md", "../"), (".docs/foundation-expansion/CHECKLIST.md", "../../")]:
    path = repo / name
    body = path.read_text(encoding="utf-8-sig")
    if name.endswith("CURRENT-CHECKPOINT.md"):
        body, count = re.subn(r"### 进行中：listener CLR 与 ledger（尚未构建）.*?(?=<!-- LISTENER-PUBLICATION-LATEST:BEGIN -->)", "", body, count=1, flags=re.S)
        if "<!-- LISTENER-CLR-LATEST:BEGIN -->" not in body:
            assert count == 1
    already = "<!-- LISTENER-CLR-LATEST:BEGIN -->" in body
    marker = "LISTENER-CLR-LATEST" if already else "LISTENER-PUBLICATION-LATEST"
    match = re.search(r"<!-- " + marker + r":BEGIN -->.*?<!-- " + marker + r":END -->", body, flags=re.S)
    assert match, name
    history = "" if already else "\n\n### 历史1515（监听发布与只读采用）\n\n" + match.group().replace("<!-- LISTENER-PUBLICATION-LATEST:BEGIN -->", "").replace("<!-- LISTENER-PUBLICATION-LATEST:END -->", "").replace("**最新：", "**历史：")
    body = body[:match.start()] + latest(prefix) + history + body[match.end():]
    if name.endswith("CHECKLIST.md"):
        body = body.replace("B07 精确 Hook：当前离线Hooks171；", "B07 精确 Hook：当前离线Hooks177；")
        body = body.replace("B15 音频/本地内容：当前离线AudioContent573；", "B15 音频/本地内容：当前离线AudioContent573，另有自有listener真实CLR/ledger联合12进程1630检查（图/根后端受控，非游戏/IL2CPP GC）；")
        body, count = re.subn(r"- \[ \] B19 分发：.*", f'- [ ] B19 分发：当前1521/1521、25套，502源/22包冻结未安装；12进程1630检查连通真实自有native listener→CLR→broker与三个实际ledger场景，graph/root受控。保留{diagnostics}条去重原始编译诊断，游戏仍1021。冷启动、干净环境、升级与全部能力矩阵仍缺。见[证据](../../docs/validation/{batch.name}/README.md)。', body, count=1)
        assert count == 1
    pending[path] = body
path = repo / "README.md"
body, count = re.subn(r"2026-10-24 更新：.*", f'2026-10-24 更新：已接通原生监听回调、C#共享broker与实际账本，25套1521/1521和12进程1630项联合检查通过。502源/22包冻结未安装；联合测试使用自有原生目标，图/根后端受控，真实游戏冷启动、动态监听来源与整局资源退休仍缺。RequestPlay关闭，实测稍后。见[本批证据](docs/validation/{batch.name}/README.md)与[实现状态](docs/IMPLEMENTATION-STATUS.md)。', path.read_text(encoding="utf-8-sig"), count=1)
assert count == 1
pending[path] = body
for path, body in pending.items():
    path.write_text(body, encoding="utf-8", newline="\n")
print(json.dumps(dict(documents=len(pending), passed=1521, sourceInputs=502, packageFiles=22, listenerCallbackChecks=1630, compilerDiagnosticLines=diagnostics)))
