"""Publish only the terminal result of this batch; preserve the full acceptance scope."""
import hashlib
import json
import re
from pathlib import Path

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
def read(name): return json.loads((batch / name).read_text(encoding="utf-8-sig"))
summary, run = read("build-summary.json"), read("build-run.json")
assert run["sessionId"] == 60161 and run["exitCode"] == 0
assert summary["projects"] == 25 and summary["passed"] == summary["total"] == 1553
assert summary["logSha256"] == run["logSha256"] == hashlib.sha256((batch / "build.log").read_bytes()).hexdigest()
assert read("source-archive-check.json")["entries"] == 522 and read("package-archive-check.json")["entries"] == 22
assert read("build-input-integrity-check.json")["currentChanged"] == []
assert read("previous-installation-check.json")["allMatched"] and read("original-game-check.json")["allUnchanged"]
assert read("operation-native-evidence.json")["checks"] == 1156
assert read("operation-managed-evidence.json")["checks"] == 259
diagnostics = len(summary["compilerDiagnosticLines"])
marker = "LISTENER-MUTATION-LATEST"
def latest(prefix):
    return (f"<!-- {marker}:BEGIN -->\n"
        f"- **最新：监听操作观察与协程身份已接入，25套1553/1553离线通过。** [本批说明]({prefix}docs/validation/{batch.name}/README.md)。生产源码接12个固定操作入口；添加后移除恢复同图仍记录独立代次。实际Coroutine句柄与Enumerator/owner/receiver/槽代次关联。Hooks185、Audio597、Runtime160；522源/22包冻结未安装。\n"
        f"- **实证分层**：native Operation1156检查/72原调用；真实native→CLR bridge/broker五进程259检查/18原调用，均为自有direct-entry目标，无Dobby首写/游戏证明。10组生产CaptureDispatch根采用故障、8组实际mutation账本、6组协程身份与8组broker回归分别通过；账本图/根仍受控，Operation CLR没有串实际ledger。full60161 exit0，保留{diagnostics}条去重原始编译诊断。\n"
        "- **仍缺**：新操作入口真实metadata/安全首发及游戏冷启动、序列化/泛型/直接写者和全部delegate leaves、Stop请求与取消完成、全部future和消费者资源退休、完整Retry/Return/Loader。MutationClosed=false、Pending保留，RequestPlay关闭；完整B01–B19 active，仅B03整项/B18a子项勾选。实测稍后，旧1021安装18项/其326源归档与原游戏3文件仍匹配。\n"
        f"<!-- {marker}:END -->")
for name, prefix in [(".docs/CURRENT-CHECKPOINT.md", "../"), ("docs/IMPLEMENTATION-STATUS.md", "../"),
                     (".docs/foundation-expansion/CHECKLIST.md", "../../")]:
    path = repo / name
    body = path.read_text(encoding="utf-8-sig")
    old = re.search(r"<!-- (?:LISTENER-MUTATION-LATEST|LISTENER-CLR-LATEST):BEGIN -->.*?<!-- (?:LISTENER-MUTATION-LATEST|LISTENER-CLR-LATEST):END -->", body, re.S)
    assert old, name
    history = "" if marker in old.group() else "\n\n### 历史1521（监听回调与账本）\n\n" + re.sub(r"<!-- LISTENER-CLR-LATEST:(?:BEGIN|END) -->", "", old.group()).replace("**最新：", "**历史：")
    body = body[:old.start()] + latest(prefix) + history + body[old.end():]
    if name.endswith("CHECKLIST.md"):
        body = body.replace("B07 精确 Hook：当前离线Hooks177；", "B07 精确 Hook：当前离线Hooks185；新增三种typed Operation通道，12个mutation声明源码接线及8组broker回归；真实游戏首发仍缺。")
        body = body.replace("B15 音频/本地内容：当前离线AudioContent573，", "B15 音频/本地内容：当前离线AudioContent597，新增10组实际生产根采用故障、8组mutation账本、6组协程身份受控回归；Stop请求/取消完成尚未接线，")
        body = re.sub(r"^- \[ \] B19 分发：当前.*$",
            f"- [ ] B19 分发：当前1553/1553、25套，522源/22包冻结未安装；Operation native1156/72原调用、CLR五进程259/18原调用为自有direct-entry，非Dobby首发/游戏证明；ledger图/根受控。保留{diagnostics}条去重原始编译诊断，旧1021安装未变。冷启动、干净环境、升级与全部能力矩阵仍缺。见[证据](../../docs/validation/{batch.name}/README.md)。",
            body, flags=re.M)
    if name == "docs/IMPLEMENTATION-STATUS.md":
        body = body.replace("当前离线AudioContent573；", "当前离线AudioContent597；12个监听变更入口源码接线、实际Coroutine身份和生产根采用故障回归通过；")
        body = body.replace("当前离线Hooks171；", "当前离线Hooks185；新增三类typed Operation观察和永久completion责任；")
        body = re.sub(r"^\| B19 分发维护 \|.*$",
            f"| B19 分发维护 | 当前1553/1553、25套，522源/22暂存包冻结未安装；native Operation1156/72原调用、CLR五进程259/18原调用，均为自有direct-entry。保留{diagnostics}条去重原始编译诊断；旧1021安装18项/其326源归档和原游戏3hash仍匹配。[本批证据](validation/{batch.name}/README.md) | 新操作入口安全首发、真实冷启动、干净环境及升级/全部能力实机矩阵仍缺；不从回归数量推完成率 |",
            body, flags=re.M)
    path.write_text(body, encoding="utf-8", newline="\n")
readme = repo / "README.md"
body = readme.read_text(encoding="utf-8-sig")
old = re.search(r"<!-- (?:LISTENER-MUTATION-LATEST|LISTENER-CLR-LATEST):BEGIN -->.*?<!-- (?:LISTENER-MUTATION-LATEST|LISTENER-CLR-LATEST):END -->", body, re.S)
if old:
    body = body[:old.start()] + latest("") + body[old.end():]
else:
    body, count = re.subn(r"^2026-10-24 更新：.*$", latest(""), body, count=1, flags=re.M)
    assert count == 1
readme.write_text(body, encoding="utf-8", newline="\n")
print("Updated the four status entry documents from terminal and frozen proof; acceptance checkboxes unchanged.")
