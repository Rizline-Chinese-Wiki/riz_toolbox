"""Publish this batch only after the full build and all frozen inputs verify."""
import json
from pathlib import Path

root = Path(__file__).resolve().parent
repo = root.parents[2]
read = lambda name: json.loads((root / name).read_text(encoding="utf-8-sig"))
summary, source, package, changes = [read(name) for name in ["build-summary.json", "source-archive-check.json", "package-archive-check.json", "changes-from-1360.json"]]
assert summary["total"] == summary["passed"] == 1367 and summary["projects"] == 25
assert source["entries"] == 409 and package["entries"] == 18
assert read("staging-check.json")["allMatched"] and read("source-integrity-check.json")["changed"] == []
assert read("terminal-evidence-check.json")["allInputRangeInstructionAndCandidateBytesMatched"]
batch = root.name
totals = "Runtime139/Hooks127/Audio490，新增7组"
headline = f"最新[1367/1367、25套](../../docs/validation/{batch}/README.md)冻结暂存未安装，{totals}。已自然结束的Cutout/Fade观察根按撤身份→redrain→独立free→确认后复用记录，16条限制为同时尚未退休记录；累计计数/required Cutout事实不随复用丢失。Ready帧泵接回收与初始化观察者清理，读取activity期间worker故障在读后再次拒绝。409源/18暂存、旧1021安装18项/旧326归档/三原游戏hash匹配；12范围/991指令和6直接call候选字节再核对。完整增量0编译诊断，前置15既有warning保留。三个子代理有限审查；新增故障仅controlled backend，生产free故障及游戏尚未验收。发现Cutout通过另一个EventTrigger启动延迟协程/UnityEvent，必须单独封口；完整active退休/Retry/Return/Loader仍缺，RequestPlay关闭，仅勾B03/B18a，用户“实测稍后”保持。[下一步](../../docs/validation/{batch}/next-integration.md)。"

path = repo / ".docs/foundation-expansion/CHECKLIST.md"
body = path.read_text(encoding="utf-8-sig")
old = next(line for line in body.splitlines() if line.startswith("最新[1360/1360"))
path.write_text(body.replace(old, headline + "\n\n" + old.replace("最新[", "历史[", 1), 1), encoding="utf-8")

path = repo / "README.md"
body = path.read_text(encoding="utf-8-sig")
old = next(line for line in body.splitlines() if line.startswith("2026-10-23 更新："))
new = f"2026-10-23 更新：已结束协程的观察根可安全释放并复用记录，补充worker故障期间的释放保护；25套1367/1367离线检查通过，409项源码与18项发布文件冻结暂存。新包未安装，现安装仍1021，新backend尚无游戏验收。整局销毁、外置回调结束、资源退休、返回/重试及Loader组合仍缺，播放入口关闭。见[本轮证据](docs/validation/{batch}/README.md)、[架构参考](docs/2026-10-22_架构-InFalsusTools-reference-report.md)和[续接检查点](.docs/CURRENT-CHECKPOINT.md)。"
path.write_text(body.replace(old, new, 1), encoding="utf-8")

latest = f"- **最新1367/1367、25套完整构建冻结暂存，未安装；当前安装仍1021四门包。** [协程观察根回收](REL/README.md)：{totals}；先撤Published、保enum/代次再读真实native activity，非零待后帧，未知保责任；enum/Coroutine独立根各只free一次，确认后才复用slot。64次/原始地址复用/角色切换及worker/reentry/释放失败已覆盖。Ready帧泵继续观察future factory并回收initial已结束观察者，不签全run结束。\n- **冻结与审查**：409源ZIP `{source['archiveSha256']}`，18暂存ZIP `{package['archiveSha256']}`；完整build1689 exit0，log `{summary['logSha256']}`。较1360增{len(changes['added'])}/改{len(changes['changed'])}/删{len(changes['removed'])}；旧1021安装18项/旧326归档/三原游戏hash匹配。静态12范围/991指令和6 E8候选再核对；完整增量0编译诊断，前置Audio15既有warning保留。activation_finish复核读内fault修正，settings_finish核Reset及外置EventTrigger，never_active_retirement_code给真实active顺序；没有独立runner。新增fault仅受控backend，非生产free注入或游戏验收。\n- **直接下一步**：[确切active对象失效与完整退休](REL/next-integration.md)。Cutout在3C5963调用另一个EventTrigger.StartEvents@38A460，后者启动自己的SimpleDelay/StartEventsEnumerator，UnityEvent listener仍动态未知；必须独立追踪，不能用controller dead或StopAllCoroutines作全结束。继续实际controller/GO/音频图跨帧失效、future sources封口、globals/menu条件恢复、全消费者verifier、bundle/bridge/handler/coordinator退休和完整Retry/Return/Loader。RequestPlay关闭，B01–B19 active仅B03/B18a勾；用户“先继续代码，实测稍后”保持。本批无安装/UI/游戏输入/进程终止/commit/push，native cache `D:/RizToolbox-build-cache/terminal-recycling-final-20261023`，build已结束。\n\n"
for filename, prefix, marker in [
    (".docs/CURRENT-CHECKPOINT.md", f"../docs/validation/{batch}", "- **最新1360/1360"),
    ("docs/IMPLEMENTATION-STATUS.md", f"validation/{batch}", "- **最新1360/1360")]:
    path = repo / filename
    body = path.read_text(encoding="utf-8-sig")
    begin = body.index(marker)
    old_end = body.index("### 历史1343", begin)
    prior = body[begin:old_end].replace("**最新1360/1360", "**1360/1360", 1)
    body = body[:begin] + latest.replace("REL", prefix) + "### 历史1360\n\n" + prior + body[old_end:]
    path.write_text(body, encoding="utf-8")

path = repo / "docs/2026-10-22_架构-InFalsusTools-reference-report.md"
body = path.read_text(encoding="utf-8-sig")
old = next(line for line in body.splitlines() if line.startswith("最新工程状态以[1360"))
new = f"最新工程状态以[1367协程观察根回收批](validation/{batch}/README.md)为准：已接initial/terminal tracker与actual结果窗口，本批补独立观察根释放和记录复用；仍缺实际销毁完成、全部future来源关闭与资源退休、Retry/Return/Loader完整组合。Cutout还会启动外置EventTrigger协程，须独立追踪。新backend尚无游戏验收，入口关闭。以下首次审阅与较早状态按各自冻结包理解；上层工具架构参考不计为我方底层验收。"
path.write_text(body.replace(old, new, 1), encoding="utf-8")

proof_table = f"""| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码ZIP](source-inputs.zip)/[核对](source-archive-check.json) | {source['entries']}项，{source['uncompressedBytes']:,}原始字节 | `{source['archiveSha256']}` |
| [源码manifest](source-manifest.json) | {source['entries']}项 | `{source['manifestSha256']}` |
| [暂存ZIP](staged-package.zip)/[核对](package-archive-check.json) | {package['entries']}项，{package['bytes']:,}原始字节 | `{package['archiveSha256']}` |
| [完整日志](build.log) | session1689 exit0 | `{summary['logSha256']}` |
"""
assert not summary["compilerDiagnosticLines"], summary["compilerDiagnosticLines"]
body = f"""# 协程观察根回收

2026-10-23。**1367/1367、25套完整离线检查通过，冻结暂存未安装**。{totals}；承接[1360](../foundation-terminal-coroutines-20261023/README.md)。当前安装仍1021。本批只回收已结束协程的观察责任，尚未完成整局active退休。

## 代码行为

[tracker](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTerminalCoroutineTracker.cs)将16条记录改成同时尚未退休记录上限。自然结束后先撤Published身份，保留enum/代次识别迟到worker/main调用，再读实际native activity；非零待后帧且不重发布。确认后[backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTerminalCoroutineBackend.cs)分别释放Coroutine/enum根；每根只尝试一次，独立失败不妨碍仍安全的其他根。只有free及末次drain均已确认，才清旧enum并允许下代复用。

worker fault在activity读取期间发生时，读取后再次拒绝；不把已花掉的root操作重复执行，也不把该record报告为成功退休。Created/NaturallyEnded/Retired是累计事实，LiveRecords是当前占用，required Cutout结束事实不随slot/角色复用丢失。后续factory仍可重新进入Pending；ObservedEnded并非producer永久封口。

[coordinator](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs)在Ready帧泵调用回收，以及initial tracker已有的TryReleaseAfterEnded。未关闭terminal producers，未释放controller/bundle或签整局消费者结束。

## 验证与边界

[七组新增回归](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalTerminalRecycleRegression.cs)覆盖64次创建/同地址复用/角色切换、撤身份后二次drain等待、main/worker重入、独立free失败、free后活动、末次drain worker和结束查询worker。使用既有子进程隔离保留未知责任的测试，不修改生产32-owner配额。[定向490/490](audio-targeted-confirmed.log)和[完整构建](build-summary.json)均通过；最初别名编译错误和489/490容量失败日志保留。

activation_finish、settings_finish、never_active_retirement_code三位子代理做有限只读审查，见[review](review.md)。生产backend真实gchandle free异常尚无直接fixture，新backend尚无游戏验收。完整增量0编译诊断，前置定向仍有15条既有测试warning；不声称干净重建零warning。既有native helper31261/61358、initial helper1747、native-only probe18独立记账，不加入1367。

[12范围/991指令](terminal-evidence.json)/[逐字节复核](terminal-evidence-check.json)冻结三输入hash与6直接E8候选。新增Reset@3C5A00/3C5C40各64字节，只构造并raise NotSupportedException，不写枚举状态；禁止将Reset作为停止/结束凭据。另查到[Cutout的外置EventTrigger来源](event-trigger-research.md)，动态UnityEvent listener无法据这些静态函数证明结束。

## 冻结与复现

{proof_table}
[较1360差分](changes-from-1360.diff)/[清单](changes-from-1360.json)：新增{len(changes['added'])}、修改{len(changes['changed'])}、删除{len(changes['removed'])}，两源ZIP独立核对。[暂存](staging-check.json)/[源码](source-integrity-check.json)、[旧1021安装与旧326归档](previous-installation-check.json)、[原游戏三文件](original-game-check.json)匹配。

```powershell
pwsh -NoProfile -File scripts/Build.ps1 -NativeOutputDirectory 'D:/RizToolbox-build-cache/terminal-recycling-reproduce-20261023'
```

[scope](scope.md)与[下一步](next-integration.md)明确未完成项。RequestPlay关闭，仅B03/B18a勾选，B01–B19目标active。用户“先继续代码，实测稍后”保持，无本批安装、游戏/UI操作、进程终止、commit/push。
"""
(root / "README.md").write_text(body, encoding="utf-8")

body = f"""# 协程观察根回收工程报告

2026-10-23，flavor=null。完整1367/1367、25套通过，{totals}；409源输入与18发布文件冻结暂存未安装，实际仍1021。详细产物/hash/日志与复现命令见[批次索引](validation/{batch}/README.md)。

之前terminal记录每局累计16次就耗尽，本批将限制改为同时尚未退休记录：实际自然false、enum/controller/state核验及native退出后，先撤身份，再drain，独立free，最后确认slot复用。用代次和累计事实避免旧enum或旧角色借用新记录；连续64次与地址复用通过。Ready帧泵还调用initial已结束观察者的清理；未来terminal factory仍受观察。

七组新增受控故障回归补充worker/reentry、free失败/释放后活动及读取activity期间的worker故障。三位子代理有限审查，root修复生产free前与末次drain等读取期间fault窗口。完整增量零编译诊断，前置15既有warning保留。静态12范围/991指令和六个直接call候选字节复核；Reset不能重置状态；Cutout另启动EventTrigger自己的延迟协程/UnityEvent，动态listener仍待封口。

生产root free故障没有直接注入验证，新backend与组合尚无游戏验收；观察根回收不能证明全run结束。实际controller/GO/音频失效、外置回调与globals/menu完整关闭、活动资源与协调器退休、完整Retry/Return/Loader以及SPEC其余底层任务继续见[下一步](validation/{batch}/next-integration.md)。RequestPlay仍关闭，仅B03/B18a勾选，完整B01–B19目标active；“实测稍后”保持，无本批安装/游戏操作。
"""
(repo / "docs/2026-10-23_底层-协程观察根回收-report.md").write_text(body, encoding="utf-8")
print(json.dumps(dict(updated=True, total=summary["total"], sources=source["entries"], staged=package["entries"], markedItems=["B03", "B18a"])))
