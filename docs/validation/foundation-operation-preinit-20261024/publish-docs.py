"""Publish only verified full-build evidence; preserve full B01-B19 scope."""
import hashlib,json,re
from pathlib import Path
b=Path(__file__).resolve().parent;r=b.parents[2]
def read(n):return json.loads((b/n).read_text(encoding="utf-8-sig"))
s=read("build-summary.json");run=read("build-run.json")
assert run["sessionId"]==94416 and run["exitCode"]==run["terminalExitCodeObserved"]==0
assert s["projects"]==25 and s["passed"]==s["total"]==1579
assert s["logSha256"]==run["logSha256"]==hashlib.sha256((b/"build.log").read_bytes()).hexdigest()
assert read("source-archive-check.json")["entries"]==546 and read("package-archive-check.json")["entries"]==22
assert read("build-input-integrity-check.json")["currentChanged"]==[]
assert read("previous-installation-check.json")["allMatched"] and read("original-game-check.json")["allUnchanged"]
d=len(s["compilerDiagnosticLines"]);marker="OPERATION-PREINIT-LATEST"
def latest(p):
 return f"""<!-- {marker}:BEGIN -->
- **最新：18 个新增操作入口已改为启动前统一安装，25 套 1579/1579 离线通过。** [本批说明]({p}docs/validation/{b.name}/README.md)。12 个监听变化、6 个 Stop 与既有入口统一准备并发布 27 处；生产 CLR 严格要求完整 V3 身份，只 Attach，不在游戏初始化后补装。这解决源码中的晚安装路径，真实游戏冷启动仍待验。546 源/22 包冻结未安装，Hooks189、Audio619、Runtime160。
- **本批实证**：固定游戏映像六进程 5074 检查，全部 18 tuple 与篡改拒绝；同映像生产 CLR 只读采用 384 检查/两进程。自有真实 Dobby 新传输 1477 检查（84 原调用、含跨 Attach 旧调用），新桥→生产 broker 五进程 837 检查/75 原调用。元数据受控，两类测试不拼成真实游戏链。full94416 exit0，保留 {d} 条去重原始编译诊断。
- **仍缺**：真实冷启动和 18 入口活体 metadata/调用、并发首发及任意部分发布故障动态验证；引擎 node 登记/执行/最终清理（已找到 UnityPlayer 静态链）、全部 writer/delegate leaves、provider/future/消费者封口及整局资源退休、完整 Retry/Return/Loader。EngineCancelled=false、Pending 保留、RequestPlay 关闭。B01–B19 active，仅 B03 整项/B18a 子项勾；实测稍后，旧1021安装18项/其326源归档与原游戏3hash匹配。
<!-- {marker}:END -->"""
for name,p in [("README.md",""),(".docs/CURRENT-CHECKPOINT.md","../"),("docs/IMPLEMENTATION-STATUS.md","../"),(".docs/foundation-expansion/CHECKLIST.md","../../")]:
 path=r/name;body=path.read_text(encoding="utf-8-sig")
 old=re.search(r"<!-- (?:OPERATION-PREINIT-LATEST|CANCELLATION-REQUEST-LATEST):BEGIN -->.*?<!-- (?:OPERATION-PREINIT-LATEST|CANCELLATION-REQUEST-LATEST):END -->",body,re.S);assert old,name
 history=""
 if marker not in old.group() and name!="README.md":
  history="\n\n### 历史1575（取消请求与退休屏障）\n\n"+re.sub(r"<!-- CANCELLATION-REQUEST-LATEST:(?:BEGIN|END) -->","",old.group()).replace("**最新：","**历史：")
 body=body[:old.start()]+latest(p)+history+body[old.end():]
 if name.endswith("CHECKLIST.md"):
  body=body.replace("当前离线Hooks185；新增三种typed Operation通道，12个mutation声明源码接线及8组broker回归；真实游戏首发仍缺。","当前离线Hooks189；18操作入口pre-init统一27写/V3、生产只读采用和双sink共享已接，4组新增合同、映像5074/6、CLR采用384/2及自有新桥837/5分别通过；真实游戏首发/metadata/callback仍缺。")
  body=re.sub(r"^- \[ \] B19 分发：当前.*$",f"- [ ] B19 分发：当前1579/1579、25套，546源/22包冻结未安装；27写V3映像与18入口CLR只读采用、自有新传输/新桥回调分别通过。{d}条去重原始编译诊断，旧1021安装未变；实际冷启动/metadata、部分写故障、干净环境/升级和完整矩阵仍缺。见[证据](../../docs/validation/{b.name}/README.md)。",body,flags=re.M)
 if name=="docs/IMPLEMENTATION-STATUS.md":
  body=re.sub(r"^\| B19 分发维护 \|.*$",f"| B19 分发维护 | 当前1579/1579、25套，546源/22包冻结未安装；18入口启动前27写V3、映像5074/6、CLR只读采用384/2、自有新桥837/5分别验证；{d}条去重原始编译诊断。旧1021安装18项和原游戏3hash匹配。[证据](validation/{b.name}/README.md) | 真实冷启动/metadata/callback、任意部分写故障、干净环境/升级和完整矩阵仍缺 |",body,flags=re.M)
 path.write_text(body,encoding="utf-8",newline="\n")
print("Updated four current-progress documents from full terminal and frozen inputs; acceptance boxes unchanged.")
