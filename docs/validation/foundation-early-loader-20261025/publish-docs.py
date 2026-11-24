"""Publish current status only from the root-observed completed full build."""
import hashlib
import json
import re
from pathlib import Path

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
def read(name): return json.loads((batch / name).read_text(encoding="utf-8-sig"))
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
run, wrapper, summary = read("build-run.json"), read("build.log.run.json"), read("build-summary.json")
assert run["exitCode"] == run["terminalExitCodeObserved"] == wrapper["exitCode"] == 0
assert run["logSha256"] == wrapper["logSha256"] == summary["logSha256"] == sha(batch / "build.log")
assert summary["projects"] == 25 and summary["passed"] == summary["total"] == 1617
assert read("source-archive-check.json")["entries"] == 576
assert read("package-archive-check.json")["entries"] == 22
assert read("build-input-integrity-check.json")["currentChanged"] == []
assert read("previous-installation-check.json")["allMatched"] and read("original-game-check.json")["allUnchanged"]
early = read("early-loader-evidence.json")
assert early == summary["earlyLoader"] and early["passed"] and early["checks"] == 7316 and early["verifiedCases"] == 39
diagnostics = len(summary["compilerDiagnosticLines"])

def latest(prefix):
    return f'''<!-- EARLY-LOADER-LATEST:BEGIN -->
- **最新：自有早期加载宿主已实现并纳入完整构建。** [本批结果]({prefix}docs/validation/{batch.name}/README.md)。25套1617/1617离线回归；另39个独立子进程、7316条父断言通过，两组计数分列。576项声明源码/22项包冻结，新包未安装。
- **本批证据**：真实静态依赖链，四目标发布早于Importer的TLS/C/C++/DllMain；normal86与worker92次原调用，实际嵌套/worker/EAX/原生异常及加载后Attach；迟到已有worker拒绝和24处API边界故障已验。额外debugger子进程观察helper/Dobby真实初始化返回，明确属于内存断点仪器化证据。原件合计324次原调用/168观察配对/24异常捕获，完整构建session{run['sessionId']}实际exit0，保留{diagnostics}条去重原始编译诊断。
- **仍缺**：游戏的生产早期安装、系统WinHTTP转发及独立V4凭据、coordinator实际对象/GC接线、scheduler/async/Start内联清理和全消费者资源退休。Dobby的CRT堆、日志及未解间接路径仍不具备生产loader安全证明，下一段实现固定四入口最小发布器。若干拒绝项是受控expected/selector条件，24故障不等于Dobby内部OOM/写入中断。EngineCancelled=false、Pending保留、RequestPlay关闭；完整B01–B19继续，仅B03整项/B18a子项验收。游戏实测稍后；旧1021安装18项/326旧源ZIP与原游戏3hash匹配。
<!-- EARLY-LOADER-LATEST:END -->'''

for name, prefix in [("README.md", ""), (".docs/CURRENT-CHECKPOINT.md", "../"),
                     ("docs/IMPLEMENTATION-STATUS.md", "../"), (".docs/foundation-expansion/CHECKLIST.md", "../../")]:
    path = repo / name
    body = path.read_text(encoding="utf-8-sig")
    prior = re.search(r"<!-- (?:ENGINE-BINDING-LATEST|EARLY-LOADER-LATEST):BEGIN -->.*?<!-- (?:ENGINE-BINDING-LATEST|EARLY-LOADER-LATEST):END -->", body, re.S)
    assert prior, name
    history = ""
    if "ENGINE-BINDING-LATEST" in prior.group() and name != "README.md":
        historical = re.sub(r"<!-- ENGINE-BINDING-LATEST:(?:BEGIN|END) -->", "", prior.group()).replace("**最新：", "**历史：")
        history = "\n\n### 历史1617（返回ABI与协程身份）\n\n" + historical
    body = body[:prior.start()] + latest(prefix) + history + body[prior.end():]
    if name.endswith("CHECKLIST.md"):
        body = re.sub(r"^- \[ \] B19 分发：当前.*$", f"- [ ] B19 分发：当前25套1617/1617，另39自有早期加载进程/7316父断言；576源/22包冻结未安装。生产安全首次安装/V4、真实对象接线、调度/退休、游戏流程、干净环境与升级矩阵仍缺。见[证据](../../docs/validation/{batch.name}/README.md)。", body, flags=re.M)
    if name == "docs/IMPLEMENTATION-STATUS.md":
        body = re.sub(r"^\| B19 分发维护 \|.*$", f"| B19 分发维护 | 完整25/1617，另39自有早期加载进程/7316父断言；576源/22包冻结未安装，旧安装与原游戏hash一致。[证据](validation/{batch.name}/README.md) | 生产首次发布/V4、实际接线/退休、游戏流程、干净环境与升级完整矩阵仍缺 |", body, flags=re.M)
    path.write_text(body, encoding="utf-8", newline="\n")

(batch / "README.md").write_text(f'''# 自有早期加载与四入口发布

2026-10-25。**自有加载机制已实现并经过实际执行验证；游戏生产接入仍未完成。** 完整构建session{run['sessionId']}实际exit0，25套1617/1617；额外39子进程/7316父断言分列，576声明源码和22包已冻结核对，新包未安装。保留{diagnostics}条去重原始编译诊断。[正式报告](../../2026-10-25_底层-自有早期加载验证-report.md)、[范围](scope.md)、[下一段实现](next-integration.md)。

本批新增普通静态依赖Host→Importer→Proxy→Recorder/helper/固定Dobby。Importer在真实TLS、C和C++初始化器、DllMain阶段执行四种目标，Start同步Execute/Release；先4Prepare、再4不可变Configure、最后4Commit并复核，Ready早于这些首次调用。Observer只在加载返回后Attach，覆盖实际worker、完整EAX与SEH/C++原异常。Recorder不依赖CRT，保留原子定长共享事件和实际tuple，父进程先hash并持续持有只读禁止写删的输入句柄。

| 原件 | 实际结果 | 范围 |
| --- | --- | --- |
| [完整构建](build-run.json)、[日志](build.log)、[汇总](build-summary.json) | 25/1617，session{run['sessionId']} exit0 | 包括此前所有回归及本批宿主；未执行游戏 |
| [39宿主汇总](early-loader-evidence.json)、[完整原件树](native-build/early-loader-cases/fixture-result.json) | 7316父断言；324次原调用、168观察配对、24异常捕获 | 38无debug子进程加1额外debug子进程；计数不能当游戏覆盖率 |
| [normal](native-build/early-loader-cases/mode-0/result.json)、[worker](native-build/early-loader-cases/mode-3/result.json) | 86/92原调用，各56观察配对 | 真实四阶段调用、首次安装顺序、加载后Attach/EAX/原生异常 |
| [额外调试观测](native-build/early-loader-cases/normal-debugger/result.json) | 19个DLL load/TLS/真实entry与return事件 | 仅自有child的临时内存断点；helper/Dobby初始化返回先于Proxy安装，不代表无扰动/并发或生产游戏证明 |
| [迟到安装](native-build/early-loader-cases/mode-2/result.json)、[部分写前故障](native-build/early-loader-cases/fault-5-2/result.json)、[部分写后故障](native-build/early-loader-cases/fault-6-2/result.json) | 已有实际worker时0写拒绝；后两者分别保留2/3次完成写入并进入Unknown | 不放行Importer，不重试/假回滚；已Prepare/Configure的永久责任也保留 |
| [Dobby依赖审查](dobby-loader-audit.md)、[原始JSON](research/dobby-loader-evidence.json) | 固定CRT堆、日志/加载候选、未解vcall被明确记录 | 不能从正常39进程通过推出全部loader路径安全 |
| [源码归档核对](source-archive-check.json)、[包归档核对](package-archive-check.json)、[输入前后](build-input-integrity-check.json) | 576源/22包全部hash匹配 | Python父验证器也显式列入构建输入；不包含完整工具链或干净换机复现 |
| [旧安装](previous-installation-check.json)、[原游戏](original-game-check.json) | 18已装文件/326旧源ZIP/3原游戏文件匹配 | 当前游戏仍旧1021安装 |

wrong-hash/architecture/prefix/ABI分别改变expected值，missing-dependency使用假名查询（固定Dobby仍静态加载），dependency-not-ready是selector拒绝。它们证明受控准入拒绝路径，不能称实际换入坏模块、缺少静态DLL或未初始化helper的矩阵。24故障位于成功API的前后边界，未制造Dobby内部OOM、非零返回或机器码中途写失败。

三代理分别实现Proxy/Recorder及Dobby依赖研究、Importer/Host与有限交叉审查、父验证器与报告；root实现builder/debugger并唯一构建。完整B01–B19仍active，仅B03整项/B18a子项验收。生产V4、真实coordinator/对象/GC、调度/async及Start内联清理、全部writer/future/消费者退休和Retry/Return/Loader仍须实现与验收。EngineCancelled=false、Pending保留、RequestPlay关闭，游戏实测按用户安排暂缓。
''', encoding="utf-8", newline="\n")
print("Published early-loader batch and four entry documents from completed proofs.")
