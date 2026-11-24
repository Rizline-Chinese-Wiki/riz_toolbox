"""Publish actual completed build evidence without changing historical archives."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parent
repo = root.parents[2]
read = lambda name: json.loads((root / name).read_text(encoding="utf-8-sig"))
summary, source, package, changes = [read(name) for name in ["build-summary.json", "source-archive-check.json", "package-archive-check.json", "changes-from-1367.json"]]
assert summary["passed"] == summary["total"] == 1393 and summary["projects"] == 25
assert source["entries"] == 416 and package["entries"] == 18 and len(changes["added"]) == 7
assert read("staging-check.json")["allMatched"] and read("source-integrity-check.json")["changed"] == []
static = read("terminal-evidence-check.json")
assert static["allInputRangeInstructionAndCandidateBytesMatched"] and static["ranges"] == 16 and static["instructions"] == 1399
targeted = (root / "audio-targeted-first.log").read_text(encoding="utf-8-sig")
assert "516/516 audio/content checks passed." in targeted and "FAIL " not in targeted
warnings = {line.strip() for line in targeted.splitlines() if re.search(r"warning (?:CS|MSB|NU)\d+", line, re.I)}
assert len(warnings) == 17
assert not summary["compilerDiagnosticLines"]
batch = root.name

feature = "实际factory五对象的独立销毁观察与Cutout EventSource已在激活前捕获，Ready/销毁阶段帧泵推进；required Cutout完整退出后须更晚真实帧核controller/GO/Transform dead，staging只对自己的空/未激活对象Destroy一次再等后帧。实际EventTrigger/GO/Transform、32上限triggers/UnityEvent/offset持根，StartEvents真实probe与计数纳入共享屏障，expected-dead保closure。"
boundary = "EventSource仍Pending，分派后UnsealedDispatchSource=true；两类外置延迟及动态listener尚未闭合，五对象dead不代表音频图/globals/menu/全部回调结束。全active verifier、资源/协调器退休与完整Retry/Return/Loader仍缺，RequestPlay关闭。"
proof = f"416源/18暂存冻结，较1367新增7/改{len(changes['changed'])}/删0；旧1021安装18项/旧326归档/三原游戏hash匹配。静态16范围/1399指令及6直接E8候选字节核对，完整增量0编译诊断，定向17条warning保留（15既有、2新增测试计数器CS0649）。"
limits = "三个子代理实现/独立有限审查，新增26组受控检查；生产Unity Destroy/free、source get_Item quota故障及新backend游戏尚未直接验证。"
latest = f"最新[1393/1393、25套](../../docs/validation/{batch}/README.md)冻结暂存未安装，Runtime139/Hooks127/Audio516，新增26组（12销毁+14事件）。{feature}{boundary}{proof}{limits}仅勾B03/B18a，用户“实测稍后”保持。[下一步](../../docs/validation/{batch}/next-integration.md)。"

path = repo / ".docs/foundation-expansion/CHECKLIST.md"
body = path.read_text(encoding="utf-8-sig")
old = next(line for line in body.splitlines() if line.startswith("最新[1367/1367"))
path.write_text(body.replace(old, latest + "\n\n" + old.replace("最新[", "历史[", 1), 1), encoding="utf-8")

for filename, prefix in [(".docs/CURRENT-CHECKPOINT.md", "../docs/validation/"), ("docs/IMPLEMENTATION-STATUS.md", "validation/")]:
    path = repo / filename
    body = path.read_text(encoding="utf-8-sig")
    body = re.sub(r"^- \*\*本轮进行中\*\*：.*\n\n", "", body, count=1, flags=re.M)
    begin = body.index("- **最新1367/1367")
    old_end = body.index("### 历史1360", begin)
    prior = body[begin:old_end].replace("**最新1367/1367", "**1367/1367", 1)
    current = f"- **最新1393/1393、25套完整构建冻结暂存，未安装；当前安装仍1021四门包。** [活动销毁与事件来源]({prefix}{batch}/README.md)：Runtime139/Hooks127/Audio516，新增26组。{feature}\n- **冻结与限界**：{proof}{limits}源码ZIP `{source['archiveSha256']}`；暂存ZIP `{package['archiveSha256']}`；build90989 exit0，log `{summary['logSha256']}`。native cache `D:/RizToolbox-build-cache/active-destruction-final-20261023`，build已结束。\n- **直接下一步**：[外置异步与完整消费者]({prefix}{batch}/next-integration.md)。{boundary}继续实际EventTrigger SimpleDelay/StartEventsEnumerator、UnityEvent的listener与转交工作、音频图、全部future sources封口、globals/menu条件恢复，再真实consumer-end verifier、Close/bridge/bundle/handler/coordinator退休与完整命令/Loader。B01–B19 active，仅B03/B18a勾选，用户“先继续代码，实测稍后”保持；无本批安装/UI/游戏输入/进程终止/commit/push。\n\n"
    path.write_text(body[:begin] + current + "### 历史1367\n\n" + prior + body[old_end:], encoding="utf-8")

path = repo / "README.md"
body = path.read_text(encoding="utf-8-sig")
old = next(line for line in body.splitlines() if line.startswith("2026-10-23 更新："))
new = f"2026-10-23 更新：已接实际控制器五对象的跨帧销毁观察与Cutout事件来源账本；25套1393/1393离线检查通过，416项源码与18项发布文件冻结暂存。新包未安装，当前仍1021，新backend尚无游戏验收。外置延迟与动态listener、音频/全部回调结束、资源退休及完整返回/重试/Loader仍缺，播放入口关闭。见[本轮证据](docs/validation/{batch}/README.md)、[实现状态](docs/IMPLEMENTATION-STATUS.md)与[续接点](.docs/CURRENT-CHECKPOINT.md)。"
path.write_text(body.replace(old, new, 1), encoding="utf-8")

path = repo / "docs/2026-10-22_架构-InFalsusTools-reference-report.md"
body = path.read_text(encoding="utf-8-sig")
old = next(line for line in body.splitlines() if line.startswith("最新工程状态以[1367"))
new = f"最新工程状态以[1393活动销毁与事件来源批](validation/{batch}/README.md)为准：初始化/终止协程、结果窗口、独立观察根回收、actual五对象跨帧销毁观察和Cutout EventTrigger来源已接生产coordinator。EventSource明确Pending，外置延迟/listener、音频及全部消费者退休、Retry/Return/Loader仍缺，新backend尚无游戏验收，入口关闭。以下首次审阅与较早状态按各自冻结包理解；上层工具架构参考不计为我方底层验收。"
path.write_text(body.replace(old, new, 1), encoding="utf-8")

body = f"""# 活动销毁与Cutout事件来源

2026-10-23。**1393/1393、25套完整离线检查通过，冻结暂存未安装**。Runtime139/Hooks127/Audio516，新增26组；承接[1367](../foundation-terminal-recycling-20261023/README.md)，当前安装仍1021。

## 实现

[active tracker](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalActiveDestructionTracker.cs)/[backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalActiveDestructionBackend.cs)在activation前借[原factory的实际五根](../../../src/RizToolbox.Adapter/AudioContent/OwnedNativeInactiveController.ActiveDestruction.cs)，立即存独立root并核identity/class和真实父子图。required Cutout完整退出之后记真实Unity frame，更晚帧才核三个clone对象Unity-dead；旧origin scene使staging先dead也正常接受。对仍活且本owner持有、未激活、无父无子staging才可请求一次Destroy，再等更晚帧。没有再次Destroy活动clone或套用inactive cleanup。

[EventSource](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutEventSource.cs)/[backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutEventSourceBackend.cs)从真实controller字段取得EventTrigger、GO/Transform、triggers列表和最多32个Trigger/UnityEvent/offset，持独立root及只读快照。StartEvents@38A460使用真实broker/probe覆盖worker/reentry/异常与在途，fault传播同launch标量信号；共享source屏障先于cleanup/销毁取证，读取活动期间的fault也读后拒绝。get_Item的owned参数先存process-held记录，Runtime未采用即拒绝或cleanup异常时保责任。

[coordinator](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs)接激活前Capture、Ready/AwaitingControllerDestruction/ControllerDestroyed帧泵和两个诊断快照。ControllerDestroyed只表示三clone对象，五对象与全部消费者分别记录。EventSource无论是否分派都仍Pending，分派后UnsealedDispatchSource=true；既有外置SimpleDelay/StartEventsEnumerator和动态listener还没有完成追踪。本批不Close registry或释放factory/bundle/bridge/handler/coordinator。

## 验证

[12组销毁](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalActiveDestructionRegression.cs)与[14组事件](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalCutoutEventSourceRegression.cs)在[定向516/516](audio-targeted-first.log)和[完整构建](build-summary.json)通过。覆盖后帧、partial death/复活拒绝、已dead staging、空父Destroy、未知free/Destroy、worker/reentry、实际broker/probe异常/在途、列表变化/快照不可变等。使用既有新进程隔离故障责任，未改生产quota或清静态owner。三位子代理分工与确定缺口修复见[review](review.md)；root唯一构建，无第二runner。

[16范围/1399指令](terminal-evidence.json)/[磁盘再核对](terminal-evidence-check.json)与[EventTrigger路径](event-trigger-research.md)固定三输入hash及6直接E8候选；非完整动态listener图。完整增量0编译诊断，前置17条warning保留（15既有、2新增测试计数器CS0649）。native helper31261/61358、initial helper1747和native-only probe18独立记账，不加入1393。生产metadata/Unity销毁/free故障和get_Item quota并无直接验收，新InstanceVoid original的SEH/C++跨CLR展开未新证明；详见[scope](scope.md)。

## 产物

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码ZIP](source-inputs.zip)/[核对](source-archive-check.json) | 416项，{source['uncompressedBytes']:,}字节 | `{source['archiveSha256']}` |
| [源码manifest](source-manifest.json) | 416项 | `{source['manifestSha256']}` |
| [暂存ZIP](staged-package.zip)/[核对](package-archive-check.json) | 18项，{package['bytes']:,}字节 | `{package['archiveSha256']}` |
| [完整日志](build.log) | session90989 exit0 | `{summary['logSha256']}` |

[较1367差分](changes-from-1367.diff)/[清单](changes-from-1367.json)：7新增、{len(changes['changed'])}修改、0删除；两源ZIP各自核对。[暂存](staging-check.json)/[源码](source-integrity-check.json)、[旧1021安装及326归档](previous-installation-check.json)和[原游戏三文件](original-game-check.json)均匹配。

```powershell
pwsh -NoProfile -File scripts/Build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/active-destruction-reproduce-20261023'
```

[下一步](next-integration.md)继续外置异步/listener、音频/全部future来源、资源退休、完整命令/Loader及SPEC其余底层任务。RequestPlay关闭，仅B03/B18a勾选，完整B01–B19目标active。用户“实测稍后”保持，无本批安装、游戏/UI操作、进程终止、原文件修改、commit/push。
"""
(root / "README.md").write_text(body, encoding="utf-8")
report = f"""# 活动销毁与事件来源工程报告

2026-10-23，flavor=null。1393/1393、25套完整检查通过，Runtime139/Hooks127/Audio516，新增12销毁+14事件回归。416源输入与18发布文件冻结暂存未安装，实际仍1021；[证据/产物/hash](validation/{batch}/README.md)。

实际coordinator激活前捕获factory五对象独立根及Cutout EventTrigger来源。自然Cutout完整退出后，在更晚Unity frame核controller/GO/Transform失效；staging只对自己的空、未激活父对象请求一次Destroy再等待后帧。事件账本保实际列表/UnityEvents/offset，StartEvents真实probe计数纳入同launch共享屏障，worker/reentry与读内fault保持Unknown责任，expected-dead保closure。get_Item参数的pre-adoption拒绝责任也记录。

三位子代理实现/独立只读审查，root接线并唯一构建。16范围/1399指令/6 E8候选再核对；全量增量0编译诊断，前置17条warning保留（15既有、2新增测试计数器CS0649）。受控检查不代替生产Unity Destroy/free或source quota故障注入；新backend游戏未验收，也未新证明managed InstanceVoid original的SEH/C++跨CLR展开。

EventSource仍Pending，动态listener和两类外置延时未闭合。五对象死亡不代表音频图、globals/menu或所有回调结束，本批不发whole-run token、不Close registry。完整active退休、Retry/Return/Loader及SPEC其余B01–B19继续见[下一步](validation/{batch}/next-integration.md)。RequestPlay关闭，只勾B03/B18a；用户“先继续代码，实测稍后”保持，无本批安装/游戏操作。
"""
(repo / "docs/2026-10-23_底层-活动销毁与事件来源-report.md").write_text(report, encoding="utf-8")

body = (root.parent / "foundation-terminal-recycling-20261023/check-documentation.py").read_text(encoding="utf-8-sig")
body = body.replace("2026-10-23_底层-协程观察根回收-report.md", "2026-10-23_底层-活动销毁与事件来源-report.md").replace("== 1367 and", "== 1393 and")
(root / "check-documentation.py").write_text(body, encoding="utf-8")
print(json.dumps(dict(updated=True, passed=1393, sources=416, staged=18, markedItems=["B03", "B18a"])))
