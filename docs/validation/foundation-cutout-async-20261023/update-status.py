"""Publish only a verified complete staged build and keep earlier archives intact."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parent
repo = root.parents[2]
read = lambda name: json.loads((root / name).read_text(encoding="utf-8-sig"))
summary, source, package, changes = [read(name) for name in ["build-summary.json", "source-archive-check.json", "package-archive-check.json", "changes-from-1393.json"]]
assert summary["passed"] == summary["total"] == 1412 and summary["projects"] == 25
assert source["entries"] == 419 and package["entries"] == 18 and len(changes["added"]) == 3 and not changes["removed"]
assert read("staging-check.json")["allMatched"] and read("source-integrity-check.json")["changed"] == []
static = read("terminal-evidence-check.json")
assert static["allInputRangeInstructionAndCandidateBytesMatched"] and static["ranges"] == 21 and static["instructions"] == 1592
targeted = (root / "audio-targeted-final.log").read_text(encoding="utf-8-sig")
assert "535/535 audio/content checks passed." in targeted and "FAIL " not in targeted
warnings = sorted({line.strip() for line in targeted.splitlines() if re.search(r"warning (?:CS|MSB|NU)\d+", line, re.I)})
assert len(warnings) == 17 and not summary["compilerDiagnosticLines"]
batch = root.name

features = "reset SimpleDelay和每Trigger延迟事件从真实typed IEnumerator Start/Auto接入，要求actual EventTrigger host、物理StartEvents代次和caller38A60C/38A6B2；original前独立root并发布enum，承接同步firstMove和<=0同步false。Delay支持provider state1和Wait state2，event固定0→1→false，实际Action/Trigger/events/offset保持本record身份；64记录/128根可撤身份redrain后独立free/代次复用。"
limit = "Source仍Pending/分派后UnsealedDispatchSource，未自然false的销毁取消路径、动态listener/provider转交、音频/全部future来源关闭仍缺；不能发全run结束token或Close。完整active退休/Retry/Return/Loader继续，RequestPlay关闭。"
evidence = f"419源/18暂存冻结，较1393新增3/改{len(changes['changed'])}/删0；旧1021安装18项/旧326归档/三原游戏hash匹配。静态21范围/1592指令和6直接E8候选再核对；完整增量0编译诊断，前置17既有测试warning保留。"
headline = f"最新[1412/1412、25套](../../docs/validation/{batch}/README.md)冻结暂存未安装，Runtime139/Hooks127/Audio535，新增19组（17协程+2source窗口）。{features}coordinator及active destruction共享完整在途/纯authority故障屏障。{limit}{evidence}三个子代理按用户要求重启后完成实现/有限审查，新增回归非生产backend/free故障或游戏验收；仅勾B03/B18a，用户“实测稍后”保持。[下一步](../../docs/validation/{batch}/next-integration.md)。"

path = repo / ".docs/foundation-expansion/CHECKLIST.md"
body = path.read_text(encoding="utf-8-sig")
old = next(line for line in body.splitlines() if line.startswith("最新[1393/1393"))
path.write_text(body.replace(old, headline + "\n\n" + old.replace("最新[", "历史[", 1), 1), encoding="utf-8")

for filename, prefix in [(".docs/CURRENT-CHECKPOINT.md", "../docs/validation/"), ("docs/IMPLEMENTATION-STATUS.md", "validation/")]:
    path = repo / filename
    body = path.read_text(encoding="utf-8-sig")
    body = re.sub(r"^- \*\*本轮进行中.*\n\n", "", body, count=1, flags=re.M)
    begin = body.index("- **最新1393/1393")
    end = body.index("### 历史1367", begin)
    historical = body[begin:end].replace("**最新1393/1393", "**1393/1393", 1)
    latest = f"- **最新1412/1412、25套完整构建冻结暂存，未安装；当前安装仍1021四门包。** [Cutout直接异步协程]({prefix}{batch}/README.md)：Runtime139/Hooks127/Audio535，新增19组。{features}\n- **接线与冻结**：Source纯物理窗口、activation前Capture、coordinator Ready帧泵回收及active destruction前后原生idle屏障均已接；读内launch/source故障也拒绝后续native读/发布/free。{evidence}源码ZIP `{source['archiveSha256']}`，暂存ZIP `{package['archiveSha256']}`；build5141 exit0，log `{summary['logSha256']}`。native cache `D:/RizToolbox-build-cache/cutout-async-final-20261023`，构建全部结束；首轮534/535 fixture计数器失败日志保留，修后535/535。三代理被用户明确重启后测试/backend/独立只读审查完成，root唯一builder，无第二runner或游戏验收。\n- **直接下一步**：[listener、取消与全消费者封口]({prefix}{batch}/next-integration.md)。{limit}继续实际UnityEvent的persistent/runtime/executing listeners Target/Method/列表变化及动态provider；音频图、globals/menu、result callbacks/future sources全部取得真实结束证据后才VerifyConsumersEnded→Close→bridge/bundle/handler/coordinator退休及完整命令/Loader。B01–B19仍active，仅B03/B18a勾；用户“先继续代码，实测稍后”保持，无本批安装/UI/游戏输入/进程终止/commit/push。\n\n"
    path.write_text(body[:begin] + latest + "### 历史1393\n\n" + historical + body[end:], encoding="utf-8")

path = repo / "README.md"
body = path.read_text(encoding="utf-8-sig")
old = next(line for line in body.splitlines() if line.startswith("2026-10-23 更新："))
new = f"2026-10-23 更新：Cutout触发的两类直接异步协程已接实际启动/推进、自然结束和观察根回收；25套1412/1412离线检查通过，419项源码与18项发布文件冻结暂存。新包未安装，当前仍1021，新backend尚无游戏验收。动态listener/provider、取消路径、全部资源退休及完整返回/重试/Loader仍缺，播放入口关闭。见[本轮证据](docs/validation/{batch}/README.md)、[实现状态](docs/IMPLEMENTATION-STATUS.md)与[续接点](.docs/CURRENT-CHECKPOINT.md)。"
path.write_text(body.replace(old, new, 1), encoding="utf-8")

path = repo / "docs/2026-10-22_架构-InFalsusTools-reference-report.md"
body = path.read_text(encoding="utf-8-sig")
old = next(line for line in body.splitlines() if line.startswith("最新工程状态以[1393"))
new = f"最新工程状态以[1412直接异步协程批](validation/{batch}/README.md)为准：五对象跨帧失效、EventTrigger来源及两条直接协程链已接生产coordinator；自然结束观察根可回收。动态UnityEvent listener/provider转交、取消路径、全部active消费者与资源退休和完整Retry/Return/Loader仍缺，新backend游戏尚未验收，入口关闭。以下首次审阅与较早状态按各自冻结包理解；上层架构参考不计为我方底层验收。"
path.write_text(body.replace(old, new, 1), encoding="utf-8")

body = f"""# Cutout直接异步协程

2026-10-23。**1412/1412、25套完整离线检查通过，冻结暂存未安装**。Runtime139/Hooks127/Audio535，新增19组（17协程、2source窗口）；承接[1393](../foundation-active-destruction-20261023/README.md)，当前安装仍1021。

## 实现与接线

[tracker](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutCoroutineTracker.cs)/[backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutCoroutineBackend.cs)观察reset SimpleDelay与每Trigger的延迟事件。准入来自same launch实际EventTrigger source、StartEvents物理代次、caller38A60C/38A6B2和独立root的真正IEnumerator。public Start/Auto和共用Delay Move只共享现有typed native patch；事件Move@39B4D0新增逻辑观察。静态Trigger工厂不是ref0实例factory，本批没有新增该ABI。

Start original之前发布enum，承接同步首Move。reset可在<=0首帧自然false/Coroutine为空，正秒数接受provider state1逐帧更改剩余seconds或WaitForSeconds state2；event只走state0→1→false/-1。reset Action必须匹配exact MethodInfo、EventTrigger Target、singlecast/nonvirtual/direct entry；相同RVA的EncoderNLS方法不能借用权限。每record自己的Trigger/events/offset不能换成source中另一合法row。

64同时记录、128根，正常终结后撤身份/redrain、enum/Coroutine根独立各free一次，确认后才清旧身份/复用代次。130次原始地址复用/角色切换已覆盖。新的factory/Start仍继续观测；ObservedEnded只指当前直接协程，不撤future producers或签发全run证明。其它observer触发launch/source fault时，纯ObservationAuthorityUnknown让读取/发布/free立即保责任，局部rootfree失败仍可继续安全独立根的一次清理。

[Source](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutEventSource.cs)给出纯物理分派代次窗口，要求main/非busy/known/own inFlight=1/entered=exited+1；typedcaller与actualhost另核。[coordinator](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs)激活前Capture并接Ready帧泵/快照，[active destruction](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalActiveDestructionTracker.cs)前后核真实协程在途，不能仅凭StartEvents已经返回就继续原生liveness/Destroy/free。

## 验证与限界

[17组受控协程](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalCutoutCoroutineRegression.cs)与[2组真实broker/probe窗口](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalCutoutEventSourceRegression.cs)在[定向535/535](audio-targeted-final.log)及[完整构建](build-summary.json)通过。首轮[534/535](audio-targeted-first.log)为fixture可选callback内计数未执行，修为每次读取无条件增加计数再注入；失败日志保留。

用户明确“重启他们”后三位代理恢复：activation_finish写17回归，settings_finish写backend，never_active_retirement_code独立只读审查，root负责tracker/接线/统一构建；见[review](review.md)。无第二runner。新增受控故障不代替生产真实metadata/delegate/GC free或游戏验收；native helper31261/61358、initial helper1747及native-only probe18按原范围单独记账，不加入1412。

[21范围/1592指令](terminal-evidence.json)/[逐字节复核](terminal-evidence-check.json)与[四函数证据](next-async-evidence.md)、[EventTrigger路径](event-trigger-research.md)固定三输入hash/6直接E8候选，不是完整listener图。两个throwing Reset及reset Action live代码范围在backend核验，不能把Reset当停止/结束操作。完整增量0编译诊断，前置17条既有测试warning保留。

Source.Pending仍true，分派后UnsealedDispatchSource保留；动态listener/provider转交工作及GO销毁取消未自然false的路径继续未封口，不发全run token、不Close、不释放factory/bundle/bridge/handler/coordinator。完整剩余见[scope](scope.md)、[下一步](next-integration.md)。RequestPlay关闭，仅B03/B18a勾选，完整B01–B19目标active。

## 冻结与复现

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码ZIP](source-inputs.zip)/[核对](source-archive-check.json) | 419项，{source['uncompressedBytes']:,}字节 | `{source['archiveSha256']}` |
| [源码manifest](source-manifest.json) | 419项 | `{source['manifestSha256']}` |
| [暂存ZIP](staged-package.zip)/[核对](package-archive-check.json) | 18项，{package['bytes']:,}字节 | `{package['archiveSha256']}` |
| [完整日志](build.log) | session5141 exit0 | `{summary['logSha256']}` |

[较1393差分](changes-from-1393.diff)/[清单](changes-from-1393.json)：3新增、{len(changes['changed'])}修改、0删除，两源ZIP各自核对。[暂存](staging-check.json)/[源码](source-integrity-check.json)、[旧1021安装和旧326归档](previous-installation-check.json)及[原游戏三文件](original-game-check.json)匹配。

```powershell
pwsh -NoProfile -File scripts/Build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/cutout-async-reproduce-20261023'
```

用户“先继续代码，实测稍后”保持。无本批安装、游戏/UI操作、游戏进程终止、原文件修改、commit/push。
"""
(root / "README.md").write_text(body, encoding="utf-8")

body = f"""# Cutout直接异步协程工程报告

2026-10-23，flavor=null。完整1412/1412、25套通过，Runtime139/Hooks127/Audio535，新增17协程+2source窗口回归。419源输入与18发布文件冻结暂存未安装，当前仍1021；[产物、SHA与复现](validation/{batch}/README.md)。

真实EventTrigger的reset Delay/Trigger延时事件复用typed public Start/Auto、Move边界，original前持actual enum承接同步首Move，按真实caller/分派代次/host/Action或Trigger身份分类。支持<=0同步false、provider state1/Wait state2及事件0→1→false；64记录/128根自然结束后撤身份redrain/free，确认后复用。coordinator和active destruction共享这些真实native在途，source/launch读内故障也保责任。

三个代理按用户要求重启后分工实现/有限审查，root统一构建，无第二runner。21范围/1592指令/6 E8候选再核；完整增量0编译诊断，前置17既有warning保留。首轮534/535夹具计数问题已修，最终定向535/535；生产backend/GC free故障及游戏仍未验收。

动态UnityEvent listener与provider转交、GO销毁取消和全部future consumers尚未封闭，Source.Pending保留。五对象失效或直接协程自然结束均不签全run token，不Close registry。完整active退休、Retry/Return/Loader及SPEC其余B01–B19继续见[下一步](validation/{batch}/next-integration.md)。RequestPlay关闭，仅B03/B18a勾选，用户“实测稍后”保持，无本批安装/游戏操作。
"""
(repo / "docs/2026-10-23_底层-Cutout直接异步协程-report.md").write_text(body, encoding="utf-8")
body = (root.parent / "foundation-active-destruction-20261023/check-documentation.py").read_text(encoding="utf-8-sig")
body = body.replace("2026-10-23_底层-活动销毁与事件来源-report.md", "2026-10-23_底层-Cutout直接异步协程-report.md").replace("== 1393 and", "== 1412 and")
(root / "check-documentation.py").write_text(body, encoding="utf-8")
print(json.dumps(dict(updated=True, passed=1412, sources=419, staged=18, markedItems=["B03", "B18a"])))
