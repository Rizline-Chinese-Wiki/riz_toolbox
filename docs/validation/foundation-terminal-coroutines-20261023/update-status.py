"""Advance status to the completed 1360 batch, preserving historical artifacts."""
from pathlib import Path

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
checkpoint = repo / ".docs/CURRENT-CHECKPOINT.md"
body = checkpoint.read_text(encoding="utf-8-sig")
marker = "## 当前现场\n"
assert body.count(marker) == 1 and "最新1343/1343" in body
body = body.replace(marker, marker + """
- **最新1360/1360、25套完整构建冻结暂存，未安装；当前安装仍1021四门包。** [结算协程与按名称启动](../docs/validation/foundation-terminal-coroutines-20261023/README.md)：Runtime139/Hooks127/Audio483，新增17组（13 tracker、2 result组合、1 Hook Factory、1真实native Factory ABI）。新增NativeLocalTerminalCoroutineTracker/Backend；coordinator setter后activation前安装6入口，经reservation附到实际result capture；registry claim后打开once-only ControllerOver窗口，必须发布实际named Cutout/factory/Start才成功缓存结果，失败留claim/root不重试。
- **关键来源与保护**：ControllerOver在3BEAA5调用String Start@153D490，caller3BEAAA；不是IEnumerator重载。新增Factory kind2 typed ref0，旧Start0/Move1及布局不变；真实Dobby证明full引用/null/caller、named模拟native链factory after发布先于首Move、SEH/C++和worker/解绑在途。Cutout factory3BED10/Move3C5900最终请求Destroy控制器GO；Fade factory3BED80/Move3C5A40。两者先观察state1后false/-1才自然结束，Fade可循环state1；子factory独立，foreign named mask不继承父归属。10范围/957指令、三输入hash与6个E8候选call字节再次核对，非完整xref或游戏验收。
- **冻结与限界**：408源ZIP `4577628c65d73018e727d29388016cccc9041d27bdbce9e2f38f47492c1ad0b2`，18暂存ZIP `74715647e4c9cacb3a555720fe5b2631f13e20cd46722d75e8b4f1e4ed81a1a2`；build73370 exit0，log `9ae386600fef16bf687b74435be0f97655def968024270e144ae97a9137dc2a2`。较1343增3/改17/删0；旧1021安装18项/旧326归档/三原游戏hash匹配。完整增量0编译诊断，前置Audio15既有warning保留。cache `D:/RizToolbox-build-cache/terminal-coroutines-final-20261023`，build全结束。root自查，无本批独立子代理/第二runner；旧三agent额度错误，21:06前不重试。
- **直接下一步**：[实际销毁完成与资源退休](../docs/validation/foundation-terminal-coroutines-20261023/next-integration.md)。terminal当前每局累计16条factory记录/32根，尚不复用或释放活动根；ObservedEnded仅指现有记录，后续factory重新Pending，不能Close registry或签发全run结束。继续Cutout后的确切controller/GO及音频图跨帧失效、专门active图退休阶段、全部future来源撤销/redrain、globals/menu条件恢复、先撤分类redrain再free及独立记录复用，最后bundle/bridge/handler/coordinator释放和Return/Retry/Loader组合。RequestPlay关闭，B01–B19 active仅B03/B18a勾选；用户“先继续代码，实测稍后”保持，无安装/UI/游戏输入/进程终止/commit/push。

### 历史1343
""", 1).replace("- **最新1343/1343", "- **1343/1343", 1)
checkpoint.write_text(body, encoding="utf-8")

status = repo / "docs/IMPLEMENTATION-STATUS.md"
body = status.read_text(encoding="utf-8-sig")
marker = "## 已验证到哪里\n"
assert body.count(marker) == 1 and "最新1343/1343" in body
body = body.replace(marker, marker + """
- **最新1360/1360、25套完整构建冻结暂存，未安装；游戏仍1021四门包。** [结算协程](validation/foundation-terminal-coroutines-20261023/README.md)：Runtime139/Hooks127/Audio483，新增17组。typed native Factory ref0、public String Start resolver与Cutout/Fade tracker/backend已实现；生产coordinator激活前安装，actual result capture claim后固定包围ControllerOver，必须观察正确named Cutout/factory/Start发布后才成功。真实Dobby Factory fullref/null/caller/同步首Move发布/SEH/C++与worker/解绑、15组Audio及1 Hook回归通过。
- **仍未完整可玩或退休**：静态确认Cutout最后请求Destroy GO；需实际跨帧失效及active图退休，不能当全部来源结束。terminal当前每局累计16条factory记录，活动根尚不回收，ObservedEnded不撤未来producer。完整Retry/Return/Loader与全资源/协调器退休继续缺，RequestPlay关闭；新backend/Unity反射尚无游戏验收。408源/18暂存和旧1021安装18项/旧326归档/原游戏三hash匹配，10范围/957指令/三输入hash/6个E8候选字节再核对；完整增量0诊断，前置15既有warning保留。用户“实测稍后”保持，只勾B03/B18a。[下一步](validation/foundation-terminal-coroutines-20261023/next-integration.md)。

### 历史1343
""", 1).replace("- **最新1343/1343", "- **1343/1343", 1)
status.write_text(body, encoding="utf-8")

checklist = repo / ".docs/foundation-expansion/CHECKLIST.md"
body = checklist.read_text(encoding="utf-8-sig")
marker = "# Checklist: foundation-expansion\n"
assert body.count(marker) == 1
body = body.replace(marker, marker + """
最新[1360/1360、25套](../../docs/validation/foundation-terminal-coroutines-20261023/README.md)冻结暂存未安装，Runtime139/Hooks127/Audio483，新增17组。named String Start/Factory ref0与Cutout/Fade tracker/backend已接coordinator/reservation/actual result capture窗口，缺对应发布不缓存假成功；真实Dobby及15组Audio/1Hook通过。静态10范围/957指令、三输入hash与6个直接E8候选再核对。408源/18暂存、旧1021安装18项/旧326归档/三原游戏hash匹配；完整增量0诊断，前置15条既有warning保留。terminal累计16记录的活动根尚未回收，Destroy请求非实际失效，ObservedEnded非全run结束；active图/全future退出、资源退休、Retry/Return/Loader仍缺，新backend尚无游戏验收。RequestPlay关闭，仅勾B03/B18a，用户“实测稍后”保持。[下一步](../../docs/validation/foundation-terminal-coroutines-20261023/next-integration.md)。以下1343及更早是历史包。
""", 1).replace("最新[1343/1343", "历史[1343/1343", 1)
checklist.write_text(body, encoding="utf-8")

readme = repo / "README.md"
paragraphs = readme.read_text(encoding="utf-8-sig").split("\n\n")
indices = [index for index, text in enumerate(paragraphs) if text.startswith("2026-10-23 更新：")]
assert len(indices) == 1
paragraphs[indices[0]] = "2026-10-23 更新：结算Cutout/Fade协程观察已接入生产启动与实际结果捕获，覆盖按名称启动、工厂返回实际枚举和首次推进；25套1360/1360离线检查通过，408项源码与18项发布文件冻结暂存。新包未安装，现安装仍1021；新backend尚无游戏验收。销毁完成与全部回调退出、活动资源/协调器退休、返回/重试及Loader组合仍缺，播放入口关闭。见[本轮证据](docs/validation/foundation-terminal-coroutines-20261023/README.md)、[架构参考](docs/2026-10-22_架构-InFalsusTools-reference-report.md)和[续接检查点](.docs/CURRENT-CHECKPOINT.md)。"
readme.write_text("\n\n".join(paragraphs), encoding="utf-8")

reference = repo / "docs/2026-10-22_架构-InFalsusTools-reference-report.md"
body = reference.read_text(encoding="utf-8-sig")
old = next(line for line in body.splitlines() if line.startswith("最新工程状态以[1343"))
body = body.replace(old, "最新工程状态以[1360结算协程批](validation/foundation-terminal-coroutines-20261023/README.md)为准：1343已接初始化tracker，1360继续接String Start/Factory与Cutout/Fade及actual结果捕获窗口；仍缺实际销毁完成、全future来源关闭与活动资源退休、Retry/Return/Loader完整组合，新backend尚无游戏验收，入口关闭。以下首次审阅与较早状态按各自冻结包理解；上层工具架构参考不计为我方底层验收。", 1)
reference.write_text(body, encoding="utf-8")
print("Advanced five status documents to frozen 1360; original scope and B marks preserved.")
