"""Publish only verified full-build evidence; preserve full B01-B19 scope."""
import hashlib,json,re
from pathlib import Path
b=Path(__file__).resolve().parent;r=b.parents[2]
def read(n):return json.loads((b/n).read_text(encoding="utf-8-sig"))
s=read("build-summary.json");run=read("build-run.json")
assert run["sessionId"]==11589 and run["exitCode"]==run["terminalExitCodeObserved"]==0
assert s["projects"]==25 and s["passed"]==s["total"]==1597
assert s["logSha256"]==run["logSha256"]==hashlib.sha256((b/"build.log").read_bytes()).hexdigest()
assert read("source-archive-check.json")["entries"]==561 and read("package-archive-check.json")["entries"]==22
assert read("build-input-integrity-check.json")["currentChanged"]==[]
assert read("previous-installation-check.json")["allMatched"] and read("original-game-check.json")["allUnchanged"]
d=len(s["compilerDiagnosticLines"]);marker="ENGINE-NODE-LATEST"
def latest(p):
 return f"""<!-- {marker}:BEGIN -->
- **最新：引擎协程节点观察基础已实现，25 套 1597/1597 离线通过。** [本批说明]({p}docs/validation/{b.name}/README.md)。四种实际 native ABI 的专用传输、只读 scalar 桥/多观察者分发及 Adapter 节点代次账本已接；关联同步首执行，分别记录摘链、执行窗口和最后 Release，After 不回读可能已释放的节点。Runtime170、Audio627、Hooks189；561 源/22 包冻结未安装。
- **本批实证**：自有目标真实 Dobby 原生检查3191项（128 original、116观察、4旧call跨Attach、5释放node情形）；真实 Dobby→新CLR→实际账本五进程855检查/44原调用/28配对context。另增10组dispatcher合同与8组ledger回归。full11589 exit0，保留 {d} 条不同原始编译诊断，初次环境遗漏和测试编译失败日志保留。
- **仍缺**：四个 UnityPlayer 入口的独立安全首次发布/V4 双映像凭据及生产 coordinator/tracker 接线，实际引擎 owner/Enumerator/Coroutine关联、scheduler撤销和Start内联清理、全部writer/delegate leaves/future消费者封口、完整资源退休与Retry/Return/Loader。自有scalar/返回受控，未执行游戏；FinalCleanupObserved不等于EngineCancelled。EngineCancelled=false、Pending保留、RequestPlay关闭，B01–B19 active，仅B03整项/B18a子项勾；实测稍后，旧1021安装18项/其326源归档与原游戏3hash匹配。
<!-- {marker}:END -->"""
for name,p in [("README.md",""),(".docs/CURRENT-CHECKPOINT.md","../"),("docs/IMPLEMENTATION-STATUS.md","../"),(".docs/foundation-expansion/CHECKLIST.md","../../")]:
 path=r/name;body=path.read_text(encoding="utf-8-sig")
 body=re.sub(r"<!-- ENGINE-NODE-IN-PROGRESS:BEGIN -->.*?<!-- ENGINE-NODE-IN-PROGRESS:END -->", "", body, flags=re.S)
 body=body.replace("更新：2026-10-24，UTC+8。目标仍是", "更新：2026-10-25，UTC+8。目标仍是").replace("核对日期：2026-10-24（UTC+8）", "核对日期：2026-10-25（UTC+8）")
 old=re.search(r"<!-- (?:ENGINE-NODE-LATEST|OPERATION-PREINIT-LATEST):BEGIN -->.*?<!-- (?:ENGINE-NODE-LATEST|OPERATION-PREINIT-LATEST):END -->",body,re.S);assert old,name
 history=""
 if marker not in old.group() and name!="README.md":
  history="\n\n### 历史1579（操作入口预初始化）\n\n"+re.sub(r"<!-- OPERATION-PREINIT-LATEST:(?:BEGIN|END) -->","",old.group()).replace("**最新：","**历史：")
 body=body[:old.start()]+latest(p)+history+body[old.end():]
 body=body.replace("当前离线Runtime160；", "当前离线Runtime170；").replace("当前离线AudioContent619，", "当前离线AudioContent627，新增四typed engine节点观察/代次账本，尚无固定UnityPlayer安全发布/生产接线；")
 if name.endswith("CHECKLIST.md"):
  body=body.replace("updated: 2026-10-24", "updated: 2026-10-25", 1)
  body=body.replace("当前离线Hooks185；新增三种typed Operation通道，12个mutation声明源码接线及8组broker回归；真实游戏首发仍缺。","当前离线Hooks189；18操作入口pre-init统一27写/V3、生产只读采用和双sink共享已接，4组新增合同、映像5074/6、CLR采用384/2及自有新桥837/5分别通过；真实游戏首发/metadata/callback仍缺。")
  body=re.sub(r"^- \[ \] B19 分发：当前.*$",f"- [ ] B19 分发：当前1597/1597、25套，561源/22包冻结未安装；四typed engine节点传输/标量账本已接，自有原生3191及真实新桥到账本855/5分别通过；固定UnityPlayer首次发布/V4与生产接线仍缺。{d}条去重原始编译诊断，旧1021安装未变；实际冷启动/metadata、部分写故障、干净环境/升级和完整矩阵仍缺。见[证据](../../docs/validation/{b.name}/README.md)。",body,flags=re.M)
 if name=="docs/IMPLEMENTATION-STATUS.md":
  body=body.replace("| B07 精确 Hook | 当前离线Hooks185；新增三类typed Operation观察和永久completion责任；", "| B07 精确 Hook | 当前离线Hooks189；18操作入口已纳入pre-init统一27处发布/V3，生产只读采用和多sink共享已接，真实游戏冷启动/metadata/callback仍缺；三类typed Operation观察和永久completion责任；")
  body=body.replace("| B15 音频/本地内容 | 当前离线AudioContent619；", "| B15 音频/本地内容 | 当前离线AudioContent627；四类engine节点传输、只读CLR桥/dispatcher与节点代次账本已实现，自有真实Dobby→CLR→实际账本855检查/5进程；固定UnityPlayer安全首次发布/生产接线仍缺；")
  body=re.sub(r"^\| B19 分发维护 \|.*$",f"| B19 分发维护 | 当前1597/1597、25套，561源/22包冻结未安装；四typed engine节点传输/标量账本已接，自有原生3191与真实新桥到账本855/5分别验证；{d}条去重原始编译诊断。旧1021安装18项和原游戏3hash匹配。[证据](validation/{b.name}/README.md) | 真实冷启动/metadata/callback、任意部分写故障、干净环境/升级和完整矩阵仍缺 |",body,flags=re.M)
 path.write_text(body,encoding="utf-8",newline="\n")
print("Updated four current-progress documents from full terminal and frozen inputs; acceptance boxes unchanged.")
