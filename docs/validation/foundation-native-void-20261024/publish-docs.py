"""Publish this batch's docs only after its full build and exact archives pass."""
import json
from pathlib import Path

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
read = lambda name: json.loads((batch / name).read_text(encoding="utf-8-sig"))
build, source, package, changes = map(read, ["build-summary.json", "source-archive-check.json", "package-archive-check.json", "changes-from-1449.json"])
assert build["projects"] == 25 and build["passed"] == build["total"] == 1475
assert source["entries"] == 431 and package["entries"] == 18
assert source["allEntryHashesMatched"] and package["allEntryHashesMatched"] and changes["bothFrozenArchivesVerified"]
assert read("staging-check.json")["allMatched"] and read("native-void-evidence.json")["realDobby"]
batch_name = batch.name
core = (
    "UnityEvent.Invoke已从旧managed-original void观察切换到独立NativeVoid0通道：原函数在native层一次执行，"
    "实际caller/MethodInfo进入同代次并核exit，全线程probe保留原snapshot配对。原生32永久槽、broker4096同时记录有界；"
    "worker不持安装Gate，unknown保责任，逻辑解绑不伪造native drain。Invoke/独立Prepare及每次图读前后均检查共享native Known。"
)
limits = (
    "内联执行集合/三条实际listener调用点、连续mutation、listener/provider转交、销毁取消和future来源封口仍缺；"
    "Pending保留、InlineExecutionUnobserved=true、DispatchPathVerified=false。全部消费者和资源退休、完整Retry/Return/Loader继续缺，RequestPlay关闭。"
)
new = f"""- **最新1475/1475、25套完整构建冻结暂存，未安装；当前安装仍1021四门包。** [原生void父分派](validation/{batch_name}/README.md)：Runtime159/Hooks144/Audio561，较1449新增26组（真实native4、受限resolver2、Hooks17、事件层3）。{core}
- **真实证据与冻结**：新增4组Windows x64真实Dobby/MASM caller、worker/detach、SEH/C++ code/kind/payload与native callback21组合通过；不与受控17组和Adapter图测试混淆，也不当游戏实机。431源/18暂存，较1449新增7/修改{len(changes['changed'])}/删除0；旧1021安装18项/旧326归档/三原游戏hash匹配。静态沿用23范围/1706指令/6E8候选并重新核字节，不新增game调用路径结论。源码ZIP `{source['archiveSha256']}`；暂存ZIP `{package['archiveSha256']}`；full exit0，log `{build['logSha256']}`。首full Runtime159/Hooks144已过但Audio fixture漏INativeDetourEngine编译失败，修正后定向Audio561与full全过；失败日志保留。完整增量编译诊断{len(build['compilerDiagnosticLines'])}条，前置日志既有warning保留，不宣称clean build零warning。三代理实现/有限复核，root唯一builder，cache `D:/RizToolbox-build-cache/native-void-final-20261024`，所有构建结束。
- **直接下一步**：[inline执行与取消](validation/{batch_name}/next-integration.md)。{limits} 真实native fixture不证明UnityEvent游戏实例、生产metadata/GC故障或全run结束。完整B01–B19 active，仅B03整项/B18a子项勾；用户“实测稍后”保持。无本批安装、游戏/UI操作、游戏进程终止、原文件修改、commit/push。
"""
status_path = repo / "docs/IMPLEMENTATION-STATUS.md"
body = status_path.read_text(encoding="utf-8-sig")
start = body.index("## 已验证到哪里\n") + len("## 已验证到哪里\n")
end = body.index("### 历史1412", start)
old = body[start:end].strip().replace("**最新1449/", "**1449/")
status_path.write_text(body[:start] + "\n" + new + "\n### 历史1449\n\n" + old + "\n\n" + body[end:], encoding="utf-8")

checkpoint = repo / ".docs/CURRENT-CHECKPOINT.md"
body = checkpoint.read_text(encoding="utf-8-sig")
start = body.index("## 当前现场\n") + len("## 当前现场\n")
end = body.index("### 历史1412", start)
old = body[start:end]
old = old[old.index("- **最新1449/"):].strip().replace("**最新1449/", "**1449/")
checkpoint.write_text(body[:start] + "\n" + new.replace(f"(validation/{batch_name}/", f"(../docs/validation/{batch_name}/")
    + "\n### 历史1449\n\n" + old + "\n\n" + body[end:], encoding="utf-8")

def replace_line(relative, prefix, replacement):
    path = repo / relative
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    index = next(i for i, line in enumerate(lines) if line.startswith(prefix))
    lines[index] = replacement
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")

replace_line("README.md", "2026-10-23 更新：", "2026-10-24 更新：事件父分派已接真实原生void通道，异常、worker和解绑在途已有Dobby验证；25套1475/1475离线检查通过，431项源码和18项发布文件冻结暂存。"
    "新包未安装，当前仍1021。事件内联执行、动态转交/取消、全部资源退休及完整返回/重试/Loader仍缺，播放入口关闭。"
    f"见[本轮证据](docs/validation/{batch_name}/README.md)、[实现状态](docs/IMPLEMENTATION-STATUS.md)与[续接点](.docs/CURRENT-CHECKPOINT.md)。")
replace_line(".docs/foundation-expansion/CHECKLIST.md", "最新[1449/1449", f"最新[1475/1475、25套](../../docs/validation/{batch_name}/README.md)冻结暂存未安装，Runtime159/Hooks144/Audio561，新增26组。"
    + core + limits + f" 431源/18暂存，较1449新增7/改{len(changes['changed'])}/删0；旧安装/旧归档/原游戏三hash匹配。真实native与受控检查分别记账，新游戏验收未做；完整B01–B19继续，仅勾B03/B18a。"
    + f"[下一步](../../docs/validation/{batch_name}/next-integration.md)。")
replace_line("docs/2026-10-22_架构-InFalsusTools-reference-report.md", "最新工程状态以[1449", f"最新工程状态以[1475原生void父分派批](validation/{batch_name}/README.md)为准：已接同一个native父调用所有者和真实异常/worker/drain证据；内联执行集合、动态转交/取消、全部消费者退休和完整Retry/Return/Loader仍缺，游戏新包未安装，入口关闭。上层架构参考不计为我方底层验收。")

(batch / "README.md").write_text(f"""# 原生void父分派

2026-10-24。**1475/1475、25套完整离线检查通过，冻结暂存未安装**。Runtime159/Hooks144/Audio561，较[1449](../foundation-unity-events-20261023/README.md)新增26组；当前安装仍1021。

## 实现

[helper](../../../native/RizToolbox.JudgeContext/VoidContext.cpp)提供独立Windows x64 `void(self, MethodInfo*)` transport，40字节Frame/Config、48字节Stats、32不可复用槽。真实caller来自原生入口，original在native层执行一次，`__finally`覆盖正常与异常退出。观察callback故障被包含并计数，原函数的SEH/C++ code/kind/payload继续向原生调用方传播。

[bridge](../../../src/RizToolbox.Runtime/Hooks/NativeVoidContextBridge.cs)与[broker](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.NativeVoid.cs)用Prepare→Configure→Commit发布，未知结果不重试。每次物理调用持token/frame/thread/probe快照；worker不等待安装Gate，错误MethodInfo和foreign child不借父上下文。最多4096同时记录，溢出帧在probe/capture之前保守拒观察、原调用仍一次，既有记录继续配对退出。跨线程退出保留整条记录并Unknown。Binding/probe/exact activity合并真实native stats，After/Dispose结束不等于native Exit已结束；逻辑detach只撤后续投递。

[resolver](../../../src/RizToolbox.Runtime/NativeRuntime.VoidHooks.cs)只授予固定UnityEvent.Invoke官方class、无参instance void、非static/abstract/generic/inflated、RVA `15642E0` 的内部能力。新ABI追加、不改旧值，普通hook API拒绝，不开放任意地址调用。[backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventBackend.cs)/[ledger](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventLedger.cs)已切换，保存实际父caller/MethodInfo并核exit；独立Prepare和图内每次native操作前后也核共享通道Known，防止foreign故障只标记通道后仍继续读图。

## 验证及边界

[4组真实native](../../../tests/RizToolbox.Runtime.Tests/NativeVoidAbiRegression.cs)通过真实Dobby与MASM tail relay核精确返回标签、完整self/MethodInfo、foreign递归、worker堵塞和逻辑detach、原函数一次、SEH/C++异常保真与probe快照。独立native callbacks的7故障模式×3原函数模式共21组合，核flags/token/原异常及计数；不是托管callback吞异常的替代测试。[机器索引](native-void-evidence.json)与[有限复核](real-native-review.md)记录范围。

[17组受控Hooks](../../../tests/RizToolbox.Hooks.Tests/NativeVoidObservationTests.cs)另核publication、4096上限、故障隔离、并发token、callback根和GC后可用。另有2组受限resolver检查，以及事件层3组新检查；原有13组图回归已切换为实际broker+受控native桥。共享Known读门、独立Prepare入口/原调用/接收输出时失效都保持Unknown，不出版新图。受控Frame.Caller不能当游戏caller；metadata、图与实际UnityEvent运行仍需另验。

首[完整构建](build-first.log)Runtime159/Hooks144通过，Audio fixture漏声明INativeDetourEngine导致CS1503；修正后[定向561/561](audio-targeted-first.log)和[最终25套](build-summary.json)通过，未放宽生产接口。完整增量诊断{len(build['compilerDiagnosticLines'])}条，前置既有测试warning保留，不宣称clean build零warning。三代理分工与有限审查见[transport复核](transport-review.md)；root统一构建，无第二runner。

[静态证据](terminal-evidence.json)/[字节核对](terminal-evidence-check.json)沿用同一23范围/1706指令/6E8候选，再核三输入hash；没有新增game中段/leaf执行证据。既有native helper31261/61358与initial1747按原范围另记，不重复加入1475。

{limits} 这轮native parent证明不关闭上述缺口。见[scope](scope.md)与[后续](next-integration.md)。完整B01–B19 active，仅B03整项/B18a子项勾选。

## 冻结

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码ZIP](source-inputs.zip)/[核对](source-archive-check.json) | {source['entries']}项，{source['uncompressedBytes']:,}字节 | `{source['archiveSha256']}` |
| [manifest](source-manifest.json) | {source['entries']}项 | `{source['manifestSha256']}` |
| [暂存ZIP](staged-package.zip)/[核对](package-archive-check.json) | {package['entries']}项，{package['bytes']:,}字节 | `{package['archiveSha256']}` |
| [完整日志](build.log) | exit0 | `{build['logSha256']}` |

[较1449差分](changes-from-1449.diff)/[清单](changes-from-1449.json)：7新增、{len(changes['changed'])}修改、0删除。两源归档各自核对；[暂存](staging-check.json)/[源码](source-integrity-check.json)、[旧1021安装与旧326归档](previous-installation-check.json)及[三原游戏文件](original-game-check.json)匹配。[文档检查](documentation-check.json)只核本地链接。

```powershell
pwsh -NoProfile -File scripts/Build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/native-void-reproduce-20261024'
```

用户“先继续代码，实测稍后”保持，无本批安装、游戏/UI操作、进程终止、原游戏文件修改、commit/push。
""", encoding="utf-8")

(repo / "docs/2026-10-24_底层-原生void父分派-report.md").write_text(f"""# 原生void父分派

2026-10-24。1475/1475离线检查通过，431项源码和18项发布文件已冻结，新包未安装。完整底层仍未完成，详细证据见[本批索引](validation/{batch_name}/README.md)。

{core}

4组真实Dobby/MASM/native helper测试确认精确caller、原函数一次、SEH/C++保真、worker/detach与nativecallback21组合；受控17组Hook与事件/metadata检查另列，不能替代游戏实测。首轮测试适配器漏接口编译失败已修，失败日志保留；最终完整25套通过。旧1021安装、旧326归档及原游戏三hash匹配，无本批安装/UI/游戏进程终止或commit/push。

{limits} 下一步按[执行集合与调用点计划](validation/{batch_name}/next-integration.md)继续，完整B01–B19 active，仅B03和B18a勾选，用户“实测稍后”保持。
""", encoding="utf-8")
print(json.dumps(dict(updatedDocuments=7, passed=build["passed"], sourceInputs=source["entries"], packageFiles=package["entries"])))
