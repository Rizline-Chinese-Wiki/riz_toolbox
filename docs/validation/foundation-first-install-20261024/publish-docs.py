"""Publish the verified bootstrap transport batch, keeping startup limits explicit."""
import hashlib
import json
import re
from pathlib import Path

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
def read(name): return json.loads((batch / name).read_text(encoding="utf-8-sig"))
def write(path, body): path.write_text(body.strip() + "\n", encoding="utf-8")

summary, source, package, changes, native = [read(name) for name in ["build-summary.json", "source-archive-check.json", "package-archive-check.json", "changes-from-1507.json", "native-bootstrap-evidence.json"]]
assert summary["passed"] == summary["total"] == 1511 and summary["projects"] == 25
assert source["entries"] == 453 and package["entries"] == 18 and len(changes["added"]) == 8 and len(changes["changed"]) == 4 and not changes["removed"]
assert native["passed"] and native["failures"] == 0 and native["oldUnobservedCalls"] == 2 and native["observedCalls"] == 18
assert native["foreignMethodRecursions"] == 3 and native["originalExceptions"] == 2 and native["observerExceptions"] == 6
assert not native["firstInstallSafetyVerified"] and not native["gameRuntimeVerified"]
assert read("build-run.json")["exitCode"] == 0
assert all(read(name)[key] for name, key in [("staging-check.json", "allMatched"), ("previous-installation-check.json", "allMatched"), ("original-game-check.json", "allUnchanged")])
result = "1511/1511、25套；Runtime160/Hooks167/Audio573"
behavior = "新增native bootstrap观察通道与adopt-existing托管桥：每次Enter固定一次immutable observer快照，旧调用即使跨过Attach，也不继承新Mid/Exit；新调用三回调按实际native token/原caller/returnPC配对。CLR只采用已经配置的slot/Target/Original/Entry/epoch，不再次Dobby或Configure原通道。"
limits = "实际Doorstop pre-init安装器/生产producer与首次安全发布尚未实现或验收，游戏mid未发布。bootstrap与旧allocator共用native表的统一槽责任、broker受限采用、真实IL2CPP/listener/GC、连续mutation/provider转交/取消、全消费者资源退休及完整Retry/Return/Loader仍缺。Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false，RequestPlay关闭。"
proof = f"453源/18暂存，较1507新增8/修改4/删除0。源码ZIP `{source['archiveSha256']}`；暂存ZIP `{package['archiveSha256']}`；full exit0，日志SHA `{summary['logSha256']}`。旧1021安装18项、旧326归档及原游戏三hash匹配。最终完整日志{len(summary['compilerDiagnosticLines'])}条不同编译诊断；首次full exit0单独保留，静态审查修正旧Info快照竞态后再次完整构建通过。"

write(batch / "README.md", f"""# 原生启动观察通道与晚接入

2026-10-24。**{result}通过并冻结暂存，未安装。** {behavior}

## 实际通过的范围

独立native fixture有{native['checks']}个断言，2个旧调用在Enter尚无observer、原函数Mid之前等待，Attach后Mid/Exit仍零回调；18个新观察调用包含3次foreign-MethodInfo递归、2次原函数SEH/C++以及6次Enter/Mid/Exit observer SEH/C++。每次observer故障后同线程正常调用继续核token/flags；准确私有TLS归零同时依赖源码finally审查，不能仅用InFlight归零推断。实际mid为MASM CALL，不合成Frame。

新增Runtime联合用例在真实Dobby/MASM/helper上采用slot30：初始化前直通一次、旧worker跨Attach不借新Exit、三次正常/原SEH/C++配对、新lease Verify/Release、CLR observer故障后原函数仍一次且bridge Known=false。3个Hooks回归单列ABI56/40、身份/附着状态拒绝、参数拒绝。原helper ABI不变，总31 exports，新增Configure/Attach/GetInfo；Configure自身不发布代码。

{limits}

## 构建与证据

{proof}

[完整日志](build.log)、[汇总](build-summary.json)、[runner记录](build-run.json)、[首轮](build-before-read-race-fix.log)、[原生结果](native-bootstrap-evidence.json)、[源码归档](source-archive-check.json)、[暂存归档](package-archive-check.json)、[与1507差异](changes-from-1507.json)、[有限审查](review.md)。命令（仓库根、PowerShell7）：`scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/bootstrap-attach-final-20261024`。固定缓存/工具链与干净环境B19复现另行验收。

修正了一处实际并发因果错误：ReadActivity先保存此前是否见过Attached1，再读取native Info，以此判断合法旧Attached0快照，不能用别的线程随后看到的Attached1倒推本次旧读取失效。此改动有静态因果复核和完整回归，尚无确定调度的并发故障注入；Attach已发布但Configure未返回时按实际native Info报告Known，无等待安装锁。

23范围/1706指令/6E8和三输入hash继续[核对](terminal-evidence-check.json)，[来源](static-evidence-origin.json)单列。本批无安装、游戏/UI操作、游戏进程终止、原文件修改或commit/push，游戏仍1021。未验证>256 TLS深度溢出、任意跨槽/observer内再入或并发Attach争抢。

## 后续

[已选启动路线](recommendation.md)、[方案边界](options.md)、[固定发布事务设计](installer-design.md)及[具体接线](next-integration.md)。必须先实现实际producer与broker采用，再接真实游戏listener和全run闭合。[完整标准](../../../.docs/foundation-expansion/SPEC.md)仅B03整项/B18a子项勾选，用户“实测稍后”保持。[授权](scope.md)、[正式报告](../../2026-10-24_底层-启动观察接入-report.md)。
""")

write(repo / "docs/2026-10-24_底层-启动观察接入-report.md", f"""# 原生启动观察接入实现验证

2026-10-24，flavor=null，兼容框架实现验证。{result}完整通过并冻结暂存。{behavior} {limits}

三代理实现native、托管桥和独立fixture并进行有限交叉审查，root唯一builder。按[授权](validation/{batch.name}/scope.md)只推进代码，不安装或操作游戏。完整B01–B19仍未完成。

## Evidence → Finding → Path

| E-id | source_ref | repro_command（仓库根） | content_hash |
|---|---|---|---|
| E-build | [完整构建](validation/{batch.name}/build.log) | `scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/bootstrap-attach-final-20261024` | {summary['logSha256']} |
| E-native | [native结果](validation/{batch.name}/native-bootstrap-evidence.json) | `scripts/Test-BootstrapVoidNative.ps1 -HelperDirectory D:/RizToolbox-build-cache/bootstrap-attach-final-20261024` | {hashlib.sha256((batch/'native-bootstrap-evidence.json').read_bytes()).hexdigest()} |
| E-source | [源码归档](validation/{batch.name}/source-archive-check.json) | `python docs/validation/{batch.name}/freeze-source.py` | {source['archiveSha256']} |

| F-id | severity | evidence_ids | confidence | location | status |
|---|---|---|---|---|---|
| F-snapshot | n/a_re | E-native,E-source | high，冷fixture范围 | BootstrapVoidContext.cpp | 每次Enter固定observer，旧call不继承新Attach，fault保原调用 |
| F-adopt | n/a_re | E-build,E-source | high，真实CLR fixture范围 | NativeBootstrappedVoidBridge | 已有slot严格采用、不重配置、活动与unknown合并 |
| F-read-race | n/a_re | E-source,E-build | high，静态因果/完整回归；未确定调度注入 | ReadActivity的seenAttachedBefore | 修复合法旧Info被稍后状态误判，未夸为全部竞态证明 |
| F-first-install | n/a_re | [路线](validation/{batch.name}/recommendation.md) | high，生产代码缺口 | Doorstop native pre-init / broker采用 | 未实现，生产mid保持未发布 |

P-attach，path_type=callflow：Dobby已安装父入口 → native Void admission → BootstrapShim Enter压记录并snapshot observer（可为空）→ native original → 实际mid CALL/真实Frame → 同一TLS顶记录的Mid → 原函数继续/原异常 → 同一记录Exit observer+finally pop → native父Exit/admission清理。Late Attach只发布一次immutable observer，不改游戏指令；旧记录始终保留旧snapshot。

## 修正与交付边界

首full通过后，审查发现A读旧Attached0、B随后见Attached1并置seen标志、A据此误判unknown的窄竞态。改为_info之前读取seenAttachedBefore；重跑最终完整构建。首轮日志保存，不代替新源码验证。Runtime联合用例为6次真实原函数调用；独立fixture为2旧+18新，二者不是游戏验收。>256深度、任意跨槽/observer内再入、并发Attach争抢没有专项实测。

{proof}

下一步必须接真正Doorstop producer、统一槽责任、broker与ledger，再做真实固定构建冷启动和完整消费者闭合。不能把本组件或测试总数记作首次安全安装已经完成。详见[批次索引](validation/{batch.name}/README.md)和[接线计划](validation/{batch.name}/next-integration.md)。
""")

for path, header, relative in [
    (repo / "docs/IMPLEMENTATION-STATUS.md", "## 已验证到哪里", f"validation/{batch.name}/README.md"),
    (repo / ".docs/CURRENT-CHECKPOINT.md", "## 当前现场", f"../docs/validation/{batch.name}/README.md")]:
    old = path.read_text(encoding="utf-8-sig")
    block = f"\n\n- **最新{result}完整构建冻结暂存，未安装；游戏仍1021。** [启动观察接入]({relative})：{behavior}\n- **证据与范围**：{proof} native {native['checks']}断言/2旧+18新调用、实际CLR六次原调用和3组ABI/拒绝回归分别通过。{limits}\n- **后续**：实际Doorstop pre-init producer、统一槽责任、broker/ledger受限采用，再做真实listener与全run退休。B01–B19 active，仅B03整项/B18a子项勾选，用户“实测稍后”保持；本批无安装/UI/游戏进程终止/原文件修改/commit/push。\n\n### 历史1507"
    assert old.count(header) == 1
    old = old.replace(header, header + block, 1).replace("### 历史1507\n\n- **最新1507", "### 历史1507\n\n- **历史1507", 1)
    write(path, old)
path = repo / ".docs/foundation-expansion/CHECKLIST.md"
old = path.read_text(encoding="utf-8-sig")
marker = "# Checklist: foundation-expansion\n\n"
assert marker in old
old = old.replace(marker, marker + f"最新[{result}](../../docs/validation/{batch.name}/README.md)已冻结暂存未安装；native observer每次调用snapshot、既有slot托管采用、原生{native['checks']}断言/2旧+18新及CLR联合通过。453源/18暂存；实际pre-init producer、首次安全发布、broker采用与完整闭合仍缺，游戏仍1021。B01–B19 active，仅B03/B18a勾选，RequestPlay关闭，实测稍后。\n\n", 1)
old = old.replace("\n最新[1507/1507", "\n历史[1507/1507", 1)
write(path, old)
path = repo / "README.md"
old = path.read_text(encoding="utf-8-sig")
old, count = re.subn(r"2026-10-24 更新：[^\n]+", f"2026-10-24 更新：新增原生观察晚接入与已有槽位托管采用；旧调用跨Attach不借新回调，25套1511/1511及453源/18暂存冻结。实际pre-init安装器与broker采用仍待补齐，新包未安装，游戏仍1021。见[本轮证据](docs/validation/{batch.name}/README.md)、[实现状态](docs/IMPLEMENTATION-STATUS.md)与[续接点](.docs/CURRENT-CHECKPOINT.md)。", old, count=1)
assert count == 1
write(path, old)
print(json.dumps(dict(published=True, total=summary["total"], sourceInputs=source["entries"], nativeChecks=native["checks"])))
