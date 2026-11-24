"""Publish the verified cancellation-request stage, keeping full B01–B19 scope."""
import hashlib
import json
import re
from pathlib import Path

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
def read(name): return json.loads((batch / name).read_text(encoding="utf-8-sig"))
summary, run = read("build-summary.json"), read("build-run.json")
assert run["sessionId"] == 45961 and run["exitCode"] == run["terminalExitCodeObserved"] == 0
assert summary["projects"] == 25 and summary["passed"] == summary["total"] == 1575
assert summary["logSha256"] == run["logSha256"] == hashlib.sha256((batch / "build.log").read_bytes()).hexdigest()
assert read("source-archive-check.json")["entries"] == 534 and read("package-archive-check.json")["entries"] == 22
assert read("build-input-integrity-check.json")["currentChanged"] == []
assert read("previous-installation-check.json")["allMatched"] and read("original-game-check.json")["allUnchanged"]
assert read("operation-dobby-evidence.json")["checks"] == 1165 and read("operation-ledger-evidence.json")["checks"] == 544
diagnostics = len(summary["compilerDiagnosticLines"])
marker = "CANCELLATION-REQUEST-LATEST"
def latest(prefix):
    return (f"<!-- {marker}:BEGIN -->\n"
        f"- **最新：取消请求与退休屏障已接入，25套1575/1575离线通过。** [本批说明]({prefix}docs/validation/{batch.name}/README.md)。六个Stop声明typed观察关联现有owner/receiver/enum/handle/代次；独立记录请求、原函数返回和异常。共享Pending在provider前发布，阻Ready/Commit及三类协程后续根回收；不生成引擎取消完成。Audio619、Hooks185、Runtime160；534源/22包冻结未安装。\n"
        f"- **本批实证**：自有Dobby实际Prepare/Configure/Commit 1165检查、69观察+1未发布原调用；真实native→CLR→实际mutation/cancellation账本五进程544检查。图/根/provider/初始idle受控，实际引擎Stop与IL2CPP未执行；不是游戏并发首写安全证明。新增8组请求、6组Ready发布、8组退休屏障；full45961 exit0，保留{diagnostics}条去重原始编译诊断。\n"
        "- **仍缺**：18个新增操作入口的真实metadata/安全首发与冷启动、scheduler登记撤销和取消完成、全部writer/delegate leaves、provider/future/消费者封口及整局资源退休、完整Retry/Return/Loader。EngineCancelled=false、Pending保留、RequestPlay关闭。完整B01–B19 active，仅B03整项/B18a子项勾选；实测稍后。旧1021安装18项/其326源归档与原游戏3hash匹配。\n"
        f"<!-- {marker}:END -->")

for name, prefix in [(".docs/CURRENT-CHECKPOINT.md", "../"), ("docs/IMPLEMENTATION-STATUS.md", "../"),
                     (".docs/foundation-expansion/CHECKLIST.md", "../../"), ("README.md", "")]:
    path = repo / name
    body = path.read_text(encoding="utf-8-sig")
    if name == ".docs/CURRENT-CHECKPOINT.md":
        body = re.sub(r"### 进行中：取消请求与退休屏障（尚未整体验证）.*?(?=<!-- LISTENER-MUTATION-LATEST:BEGIN -->)", "", body, count=1, flags=re.S)
    old = re.search(r"<!-- (?:CANCELLATION-REQUEST-LATEST|LISTENER-MUTATION-LATEST):BEGIN -->.*?<!-- (?:CANCELLATION-REQUEST-LATEST|LISTENER-MUTATION-LATEST):END -->", body, re.S)
    assert old, name
    history = ""
    if marker not in old.group() and name != "README.md":
        history = "\n\n### 历史1553（监听变更与取消身份）\n\n" + re.sub(r"<!-- LISTENER-MUTATION-LATEST:(?:BEGIN|END) -->", "", old.group()).replace("**最新：", "**历史：")
    body = body[:old.start()] + latest(prefix) + history + body[old.end():]
    if name.endswith("CHECKLIST.md"):
        body = body.replace("B15 音频/本地内容：当前离线AudioContent597，", "B15 音频/本地内容：当前离线AudioContent619，六Stop源码观察/请求账本与共享Pending保护Ready和退休边界已接；22组新增受控回归及真实native→双账本五进程544检查通过，engine取消完成仍缺；")
        body = body.replace("Stop请求/取消完成尚未接线，", "Stop请求已接线而engine取消完成未证，")
        body = re.sub(r"^- \[ \] B19 分发：当前.*$",
            f"- [ ] B19 分发：当前1575/1575、25套，534源/22包冻结未安装；自有Dobby1165检查及native→实际双账本五进程544检查通过，图/根/provider/初始idle受控。保留{diagnostics}条去重原始编译诊断，旧1021安装未变。真实新增入口安全首发、冷启动、干净环境/升级及全部能力矩阵仍缺。见[证据](../../docs/validation/{batch.name}/README.md)。", body, flags=re.M)
    if name == "docs/IMPLEMENTATION-STATUS.md":
        body = body.replace("当前离线AudioContent597；", "当前离线AudioContent619；六Stop请求观察/共享Pending/Ready与根退休屏障已接，实际取消完成未证；")
        body = re.sub(r"^\| B19 分发维护 \|.*$",
            f"| B19 分发维护 | 当前1575/1575、25套，534源/22暂存包冻结未安装；自有Dobby1165、native→实际双账本五进程544检查，图/根/provider/初始idle受控。{diagnostics}条去重原始编译诊断；旧1021安装18项/其326源归档及原游戏3hash匹配。[本批证据](validation/{batch.name}/README.md) | 新操作入口安全首发、真实冷启动、干净环境/升级及全部能力实机矩阵仍缺 |", body, flags=re.M)
    path.write_text(body, encoding="utf-8", newline="\n")
print("Updated four progress entry documents from terminal/frozen proof; acceptance boxes unchanged.")
