"""Publish documentation only after this batch's completed build and archives verify."""
import json
from pathlib import Path

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
read = lambda name: json.loads((batch / name).read_text(encoding="utf-8-sig"))
build = read("build-summary.json")
source = read("source-archive-check.json")
package = read("package-archive-check.json")
changes = read("changes-from-1412.json")
assert build["passed"] == build["total"] == 1449 and build["projects"] == 25
assert source["entries"] == 424 and package["entries"] == 18
assert source["allEntryHashesMatched"] and package["allEntryHashesMatched"]
assert changes["bothFrozenArchivesVerified"] and len(changes["added"]) == 5 and not changes["removed"]
assert read("staging-check.json")["allMatched"]

summary = (
    "真实UnityEvent来源、listener图与Invoke前后观察已接coordinator；独立Prepare单独记账，"
    "固定版本Invoke内联Prepare，明确InlineExecutionUnobserved=true/DispatchPathVerified=false。"
    "Source/initial/terminal/active读取与清理补共享launch fault屏障，局部free失败和外部authority故障分开处理。"
)
limits = (
    "监听列表连续mutation、inline实际执行路径、listener/provider转交、销毁取消、全部active消费者与资源退休、"
    "完整Retry/Return/Loader仍缺；Pending保留，不签whole-run结束或Close，RequestPlay关闭。"
)
link = "foundation-unity-events-20261023"
new_status = f"""- **最新1449/1449、25套完整构建冻结暂存，未安装；当前安装仍1021四门包。** [UnityEvent来源与监听图](validation/{link}/README.md)：Runtime153/Hooks127/Audio558，较1412新增37组（metadata14、listener13、跨owner故障10）。{summary}
- **验证与冻结**：424源/18暂存，较1412新增5/修改{len(changes['changed'])}/删除0；旧1021安装18项、旧326源码归档及三原游戏hash匹配。静态23范围/1706指令/6直接E8候选及三输入hash核对，另三函数264指令明确Prepare内联。完整增量0编译诊断；定向构建17条既有测试warning保留。首轮153/153与548/548通过；补跨owner屏障后556/558的两个fixture身份复用错误已修，最终558/558；初次full末尾SDK restore遇NuGet EOF，重试依赖成功后完整再跑通过，失败日志保留。源码ZIP `{source['archiveSha256']}`；暂存ZIP `{package['archiveSha256']}`；build exit0，log `{build['logSha256']}`。三代理并行实现与有限交叉检查，root唯一runner；native cache `D:/RizToolbox-build-cache/unity-events-final-20261023`，所有构建结束。
- **直接下一步**：[inline执行与完整消费者协议](validation/{link}/next-integration.md)。{limits} 受控broker/metadata、部分实际backend的FakeNative故障回归不代替生产metadata初始化、真实listener执行、native original异常展开或新backend游戏验收。B01–B19仍active，仅B03整项/B18a子项勾选；用户“先继续代码，实测稍后”保持，无本批安装、游戏/UI操作、进程终止、原游戏文件修改、commit/push。
"""

status_file = repo / "docs/IMPLEMENTATION-STATUS.md"
status = status_file.read_text(encoding="utf-8-sig")
start = status.index("## 已验证到哪里\n") + len("## 已验证到哪里\n")
end = status.index("### 历史1393", start)
old = status[start:end].strip().replace("**最新1412/", "**1412/")
status_file.write_text(status[:start] + "\n" + new_status + "\n### 历史1412\n\n" + old + "\n\n" + status[end:], encoding="utf-8")

checkpoint_file = repo / ".docs/CURRENT-CHECKPOINT.md"
checkpoint = checkpoint_file.read_text(encoding="utf-8-sig")
start = checkpoint.index("## 当前现场\n") + len("## 当前现场\n")
end = checkpoint.index("### 历史1393", start)
old = checkpoint[start:end]
old = old[old.index("- **最新1412/"):].strip().replace("**最新1412/", "**1412/")
checkpoint_file.write_text(checkpoint[:start] + "\n" + new_status.replace(
    f"(validation/{link}/", f"(../docs/validation/{link}/") + "\n### 历史1412\n\n" + old + "\n\n" + checkpoint[end:], encoding="utf-8")

readme_file = repo / "README.md"
readme = readme_file.read_text(encoding="utf-8-sig")
lines = readme.splitlines()
index = next(i for i, line in enumerate(lines) if line.startswith("2026-10-23 更新："))
lines[index] = (
    "2026-10-23 更新：UnityEvent来源与监听图已接生产协调器，补齐跨owner故障隔离；25套1449/1449离线检查通过，"
    "424项源码与18项发布文件冻结暂存。新包未安装，当前仍1021，新backend尚无游戏验收。"
    "inline执行、动态转交/取消、全部资源退休及完整返回/重试/Loader仍缺，播放入口关闭。"
    f"见[本轮证据](docs/validation/{link}/README.md)、[实现状态](docs/IMPLEMENTATION-STATUS.md)与[续接点](.docs/CURRENT-CHECKPOINT.md)。"
)
readme_file.write_text("\n".join(lines) + "\n", encoding="utf-8")

checklist_file = repo / ".docs/foundation-expansion/CHECKLIST.md"
lines = checklist_file.read_text(encoding="utf-8-sig").splitlines()
index = next(i for i, line in enumerate(lines) if line.startswith("最新[1412/1412"))
lines[index] = (
    f"最新[1449/1449、25套](../../docs/validation/{link}/README.md)冻结暂存未安装，Runtime153/Hooks127/Audio558，"
    "较1412新增37组。" + summary + limits +
    f" 424源/18暂存，较1412新增5/改{len(changes['changed'])}/删0；旧1021安装/旧326归档/三原游戏hash匹配。"
    "静态23范围/1706指令/6E8候选再核对；三代理实现与有限审查，root统一构建；新增离线故障回归不代替游戏验收。"
    f"仅勾B03/B18a，完整B01–B19继续，用户“实测稍后”保持。[下一步](../../docs/validation/{link}/next-integration.md)。"
)
checklist_file.write_text("\n".join(lines) + "\n", encoding="utf-8")

reference_file = repo / "docs/2026-10-22_架构-InFalsusTools-reference-report.md"
lines = reference_file.read_text(encoding="utf-8-sig").splitlines()
index = next(i for i, line in enumerate(lines) if line.startswith("最新工程状态以[1412"))
lines[index] = (
    f"最新工程状态以[1449事件来源与监听图批](validation/{link}/README.md)为准：实际事件根、listener图和Invoke前后观察已接生产coordinator，"
    "四条Ready路径补共享故障屏障。固定版本内联Prepare仍缺实际执行覆盖，动态转交/取消、全部active消费者与资源退休、完整Retry/Return/Loader仍缺，"
    "新backend游戏尚未验收，入口关闭。以下首次审阅与较早状态按各自冻结包理解；上层架构参考不计为我方底层验收。"
)
reference_file.write_text("\n".join(lines) + "\n", encoding="utf-8")

(batch / "README.md").write_text(f"""# UnityEvent来源与监听图

2026-10-23。**1449/1449、25套完整离线检查通过，冻结暂存未安装**。Runtime153/Hooks127/Audio558，较[1412](../foundation-cutout-async-20261023/README.md)新增37组；当前安装仍1021。

## 实现与接线

[ledger](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventLedger.cs)与[backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalUnityEventBackend.cs)在activation前，从同launch的生产Source同步借用实际UnityEvent根并立即持自己的独立根；原Source责任不转移。记录serialized persistent候选、persistent/runtime/executing缓存、delegate Target/Method及只读签名。32事件、128聚合rows/leaves、2048独立根与32物理深度均有界，未知类型/静态/open/virtual/generic或不完整多播保持opaque。

Invoke采用既有early retained观察与全线程physical probe，复制Pre/Post，worker、错误MethodInfo、同event重入和原函数异常保留Unknown。独立List.Prepare采用真实ref0 native观察，原返回list先保根，再核实际executing对象/顺序与独立代次。**固定版本Invoke内联Prepare**，独立入口不能覆盖它；InlineExecutionUnobserved=true/DispatchPathVerified=false始终保留，不虚构caller、不要求每次Invoke有Prepare，也不安装ABI不同但共享RVA的叶Invoke hook。

[Runtime metadata](../../../src/RizToolbox.Runtime/NativeRuntime.ListenerMetadata.cs)只从实际rooted delegate核官方布局、有限MethodInfo与declaring-class成员关系，给只读metadata；受限ref0 resolver只接受两个确切Prepare入口及真实闭返回class。不主动调用listener、Prepare/Rebuild/GetRuntimeCall，不开放任意地址调用。

[coordinator](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs)在激活前Capture、Ready原生操作前后核idle、传播Unknown并显示纯快照。Source、initial、terminal、active四条路径补每次Runtime操作/Target/native读及清理的共享launch故障检查；返回引用先保存已取得的独立根，正常参数消费确认后再核late fault。局部free失败继续其他已知独立根的一次尝试；外部authority故障停住后续操作，最后一根free内故障也不能返回确认成功。

## 验证和限界

[14组Runtime](../../../tests/RizToolbox.Runtime.Tests/ListenerMetadataRegression.cs)与[13组事件](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalUnityEventLedgerRegression.cs)首轮在[153/153](runtime-targeted-first.log)、[548/548](audio-targeted-first.log)通过。跨owner屏障新增10组：Source1、initial4、terminal1（7阶段）、active4。active用真实生产backend/registry与FakeNative publication/unbox，metadata/roots由fixture注入，未走游戏metadata初始化。新增fixture第二实例复用身份造成[556/558](audio-after-authority-first.log)，改为逐实例唯一metadata和wrapper地址后，[最终558/558](audio-targeted-final.log)与[完整25套](build-summary.json)通过；生产distinct约束未放宽、未清owner配额。

[交叉检查](review.md)由三代理分工与root统一runner完成。受控broker/probe/metadata和部分生产backend故障回归不等于实际游戏listener、生产GC free故障注入或native original异常展开验收。native helper和ABI fixtures保持既有范围，未重复计入1449。完整增量0编译诊断；定向构建17条既有测试warning保留，不能宣称全仓clean build零warning。

初次完整构建的24套代码检查通过，但末尾SDK分发隔离restore遇NuGet EOF/连接重置，[原失败日志](build-first-sdk-network-failure.log)保留。相同依赖[单独重试](sdk-restore-retry.log)成功后，完整流程再次从头运行通过；没有跳过SDK分发或改动生产distinct/依赖准入。

[23范围/1706指令](terminal-evidence.json)、[逐字节核对](terminal-evidence-check.json)、[独立三函数264指令](listener-prepare-evidence.json)与[metadata路线说明](listener-metadata-and-prepare.md)固定三输入hash、6直接E8候选；不是完整执行调用图。

{limits} 详见[scope](scope.md)与[下一步](next-integration.md)。B01–B19仍active，仅B03整项/B18a子项勾选。

## 冻结与复现

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码ZIP](source-inputs.zip)/[核对](source-archive-check.json) | {source['entries']}项，{source['uncompressedBytes']:,}字节 | `{source['archiveSha256']}` |
| [源码manifest](source-manifest.json) | {source['entries']}项 | `{source['manifestSha256']}` |
| [暂存ZIP](staged-package.zip)/[核对](package-archive-check.json) | {package['entries']}项，{package['bytes']:,}字节 | `{package['archiveSha256']}` |
| [完整日志](build.log) | exit0 | `{build['logSha256']}` |

[较1412差分](changes-from-1412.diff)/[清单](changes-from-1412.json)：新增5、修改{len(changes['changed'])}、删除0，两源ZIP各自核对。[暂存](staging-check.json)/[源码](source-integrity-check.json)、[旧1021安装与旧326归档](previous-installation-check.json)及[原游戏三文件](original-game-check.json)匹配。

```powershell
pwsh -NoProfile -File scripts/Build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/unity-events-reproduce-20261023'
```

用户“先继续代码，实测稍后”保持。无本批安装、游戏/UI操作、游戏进程终止、原文件修改、commit/push。
""", encoding="utf-8")

report = repo / "docs/2026-10-23_底层-UnityEvent来源与监听图-report.md"
report.write_text(f"""# UnityEvent来源与监听图

2026-10-23。1449/1449离线检查与冻结已完成，新包未安装；完整底层尚未完成。详细证据见[本批索引](validation/{link}/README.md)。

{summary}

实际来源根在activation前独立采用，未转移原owner责任；persistent候选、缓存列表、独立Prepare与Invoke前后图分别记录。受限rooted-delegate metadata接口只给诊断，不能成为任意调用入口。未知listener、动态转交和不支持ABI持续保责任，不清除共享事件来制造完成。

新增37组覆盖metadata14、listener13和跨owner故障10，最终Runtime153/Hooks127/Audio558；三代理分工并有限交叉审查，root顺序运行完整25套。两个新增fixture身份复用失败已修，原失败日志保留。424项源与18项发布文件冻结；旧1021安装、旧326归档及原游戏三hash匹配。受控故障回归和静态固定版本证据仍不能代替新backend实机与实际listener执行验收。

{limits} 完整任务B01–B19继续，仅B03与B18a勾选；用户“实测稍后”保持。后续见[inline执行和完整消费者协议](validation/{link}/next-integration.md)，无本批安装/UI/游戏进程终止或commit/push。
""", encoding="utf-8")
print(json.dumps(dict(updatedDocuments=7, total=build["total"], sourceInputs=source["entries"], packageFiles=package["entries"])))
