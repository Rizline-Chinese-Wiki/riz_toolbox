"""Advance human-readable status after the completed frozen build; no source edits."""
from pathlib import Path

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
checkpoint = repo / ".docs/CURRENT-CHECKPOINT.md"
body = checkpoint.read_text(encoding="utf-8-sig")
start = body.index("**进行中的下一批")
end = body.index("- **最新1324/1324", start)
new = """- **最新1343/1343、25套完整构建冻结暂存，未安装；当前安装仍1021四门包。** [真实初始化协程追踪](../docs/validation/foundation-initialization-coroutines-20261023/README.md)：Runtime138/Hooks126/Audio468，新增18组Audio+1组Hook，既有真实Dobby初始化fixture增强getter窗口断言。新增NativeLocalInitializationCoroutineTracker/Backend，setter后activation前Capture，生产coordinator在commit前固定等自己的两条实际enum自然false/稳定delegate/state-1/native drain；菜单持同launch tracker，在取新的return normal信用前也必须通过。
- **实际身份与窗口**：controller+getter物理caller3BEFDA/3BF0B2+native getter original窗口+独立root enum同时匹配；Start原调用前发布以接同步首Move。Delay同时支持state1 provider计时与state2 WaitForSeconds，初始3.1f/40466666；具名官方delegate字段与已检查ctor验证method/ptr/invoker/target/method_code/非virtual/非multicast，Wait release使用NormalMode精确缓存闭包及MethodInfo。HasCurrentNativeGetter在Before/After/worker/普通子getter/managed-only均不授权。13范围/1731指令及三输入hash/常量再核对；新backend本批未在游戏执行。
- **冻结与验证**：405源ZIP `dec15e2d89dfcdb0dc0c710c939c5640b88d1773f7e809f85d0c3751f4ff3134`，18暂存ZIP `4d8d9c7de41e7df42fb1d9bc7432791ceef2c4ae10b2c3e87e5e964183cc86d6`；最终build72753 exit0，log `5780e31120f3aa7e3e53b03a6f36b75619fa12dd2ca9a2492610dc04f7747dbd`，旧1342/session54321只是增强前过程日志。较1324新增3/改9/删0，旧1021安装18项/旧326源归档及三原游戏hash匹配；15条既有测试warning保留。cache `D:/RizToolbox-build-cache/initialization-coroutines-final-20261023`，所有build结束。root自查，无本批独立子代理/第二runner；旧三agent额度错误，21:06前不重试。
- **直接下一步**：[完整活动来源与退休计划](../docs/validation/foundation-initialization-coroutines-20261023/next-integration.md)。两条初始回调已完成不等于所有run消费者结束；getter自己的Lock/Release仍按菜单外来信用处理。继续ControllerOver/Cutout/fade等真实异步来源、active controller/GO/音频失效、六globals/menu条件恢复、先撤分类redrain再free、bundle/reservation/bridge/handler/coordinator槽退休，最后组合Return/Retry新局与Loader根。RequestPlay关闭；B01–B19 active仅B03/B18a勾选，用户“先继续代码，实测稍后”保持，无安装/UI/游戏输入/进程终止/commit/push。

### 历史1324

"""
body = body[:start] + new + body[end:]
body = body.replace("- **最新1324/1324", "- **1324/1324", 1)
checkpoint.write_text(body, encoding="utf-8")

status = repo / "docs/IMPLEMENTATION-STATUS.md"
body = status.read_text(encoding="utf-8-sig")
marker = "## 已验证到哪里\n"
assert body.count(marker) == 1
body = body.replace(marker, marker + """
- **最新1343/1343、25套完整构建冻结暂存，未安装；游戏仍1021四门包。** [真实初始化协程追踪](validation/foundation-initialization-coroutines-20261023/README.md)：Runtime138/Hooks126/Audio468，较1324新增19组。生产tracker/backend已接coordinator的setter后activation前、commit前两条实际enum自然完成核对，菜单取新return normal信用前也核对同launch tracker。真实controller/caller/native getter original窗口/rooted enum及闭delegate同时验证；Delay state1/2均支持，未知状态有界保责任。
- **验证边界与剩余**：15组tracker+3组启动/菜单组合、1组Hook窗口及增强真实Dobby getter窗口断言通过；13范围/1731指令及三输入hash/常量再核对。405源/18发布归档，旧1021安装18项/旧326归档及原游戏三hash匹配，15条既有测试warning保留。尚无新backend游戏验收，Loader未组合，RequestPlay关闭。仅两条初始回调结束，其他异步来源、active资源/全局/协调器退休、完整Retry/Return仍缺；用户“实测稍后”保持，只保留B03/B18a。[下一步](validation/foundation-initialization-coroutines-20261023/next-integration.md)。

### 历史1324
""", 1).replace("- **最新1324/1324", "- **1324/1324", 1)
status.write_text(body, encoding="utf-8")

checklist = repo / ".docs/foundation-expansion/CHECKLIST.md"
body = checklist.read_text(encoding="utf-8-sig")
marker = "# Checklist: foundation-expansion\n"
assert body.count(marker) == 1
body = body.replace(marker, marker + """
最新[1343/1343、25套](../../docs/validation/foundation-initialization-coroutines-20261023/README.md)已冻结暂存未安装，Runtime138/Hooks126/Audio468，新增19组。真实初始化两协程tracker/backend接生产commit/menu返回前核对，实际controller/caller/native getter original窗口/rooted enum及delegate同时要求，Delay state1/2支持。15组tracker+3组合+1Hook、增强真实Dobby窗口检查通过，静态13范围/1731指令及三输入hash/常量再核对。405源/18暂存匹配，旧1021安装18项/旧326归档和原游戏三hash不变，15条既有warning保留。新backend尚无游戏验收，Loader/完整Retry/Return/全活动退休仍缺；RequestPlay关闭，只勾B03/B18a，用户“实测稍后”保持。[下一步](../../docs/validation/foundation-initialization-coroutines-20261023/next-integration.md)。以下1324及更早是各自历史包。
""", 1).replace("最新[1324/1324", "历史[1324/1324", 1)
checklist.write_text(body, encoding="utf-8")

readme = repo / "README.md"
body = readme.read_text(encoding="utf-8-sig")
paragraphs = body.split("\n\n")
indices = [index for index, text in enumerate(paragraphs) if text.startswith("2026-10-23 更新：")]
assert len(indices) == 1
paragraphs[indices[0]] = "2026-10-23 更新：真实初始化协程追踪已接入启动提交与菜单返回前核对，要求实际控制器、调用来源、原生getter执行窗口、独立根和两条协程自然结束；25套1343/1343离线检查通过，405项源码与18项发布文件冻结暂存。新backend尚无游戏验收，新包未安装，现安装仍1021。其他异步消费者和完整活动资源退休、返回/重试命令及Loader组合仍缺，用户播放入口关闭。见[本轮证据](docs/validation/foundation-initialization-coroutines-20261023/README.md)、[架构参考](docs/2026-10-22_架构-InFalsusTools-reference-report.md)和[续接检查点](.docs/CURRENT-CHECKPOINT.md)。"
readme.write_text("\n\n".join(paragraphs), encoding="utf-8")

reference = repo / "docs/2026-10-22_架构-InFalsusTools-reference-report.md"
body = reference.read_text(encoding="utf-8-sig")
old = next(line for line in body.splitlines() if line.startswith("最新工程状态以[1324"))
body = body.replace(old, "最新工程状态以[1343真实初始化协程批](validation/foundation-initialization-coroutines-20261023/README.md)为准：1315菜单交接、1324协程transport继续保留，1343已在生产coordinator/menu调用实际两协程tracker，等待自然完成后才commit/取return信用。但新backend尚无游戏验收，完整active退休、Retry/Return/Loader仍待完成，用户入口保持关闭。以下首次审阅与较早状态按各自日期/冻结包理解；不把上层工具架构参考计为我方底层验收。", 1)
reference.write_text(body, encoding="utf-8")
print("Advanced five status documents to the frozen 1343 batch; historical evidence and task marks preserved.")
