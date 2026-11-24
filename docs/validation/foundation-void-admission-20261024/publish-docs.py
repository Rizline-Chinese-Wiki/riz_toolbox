"""Publish the completed admission batch from checked frozen artifacts only."""
from pathlib import Path
import hashlib
import json
import re

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
def read(name): return json.loads((batch / name).read_text(encoding="utf-8-sig"))
def write(path, body): path.write_text(body.strip() + "\n", encoding="utf-8")

summary, source, package, changes = [read(name) for name in ["build-summary.json", "source-archive-check.json", "package-archive-check.json", "changes-from-1496.json"]]
native = read("native-admission-evidence.json")
assert summary["passed"] == summary["total"] == 1507 and summary["projects"] == 25
assert source["entries"] == 445 and package["entries"] == 18 and len(changes["added"]) == 5 and not changes["removed"]
assert native["passed"] and native["entered"] == native["exited"] == 15 and native["inFlight"] == native["active"] == native["parked"] == 0
assert native["successfulCloses"] == 4 and native["timeouts"] == native["selfCloseRejected"] == 3 and native["recursiveCalls"] == 1
assert all(read(name)[key] for name, key in [("staging-check.json", "allMatched"), ("previous-installation-check.json", "allMatched"), ("original-game-check.json", "allUnchanged")])
assert read("build-run.json")["exitCode"] == 0
result = "1507/1507、25套；Runtime159/Hooks164/Audio573"
behavior = "新增原生调用准入门：Close停止新调用进入CLR，等待已准入的Enter/original/Exit全部完成后，签发绑定槽、全局epoch和OS线程的凭据。同步同槽递归继续；任何void槽内自Close被拒绝；超时自动重开，错误Release不开门。运行时可选私有lease在写尝试/Unknown后Dispose保关闭；Unknown贯通bridge。"
limits = "首个父detour安装和生产mid安全发布尚未证明，当前patch未发布；真实IL2CPP/listener、连续mutation/provider转交/取消、全部消费者与资源退休、完整Retry/Return/Loader仍缺。Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false，RequestPlay关闭。"
proof = f"445源/18暂存，较1496新增5/修改{len(changes['changed'])}/删除0。源码ZIP `{source['archiveSha256']}`；暂存ZIP `{package['archiveSha256']}`；full exit0，日志SHA `{summary['logSha256']}`。旧1021安装18项、旧326归档及原游戏三hash匹配。最终完整日志{len(summary['compilerDiagnosticLines'])}条不同编译诊断；首轮完整通过到Package后runner转义错误退出1，原日志保留，补强CLR probe后重跑最终full exit0。"

write(batch / "README.md", f"""# 原生调用准入与运行时责任

2026-10-24。**{result}通过并冻结暂存，未安装。** {behavior}

独立native fixture实际15次Dobby原调用，{native['checks']}个断言、4次成功关闭、3次timeout、3个回调阶段自关闭拒绝、1次Closing期间递归，最终Active/Parked/InFlight全部0。真实bridge联合用例增加CLR boundary probe：Closed停车时0/0，正确owner释放后1/1，timeout恢复后2/2；错OS线程不放行，首次写尝试后Dispose仍关，RetainUnknown后bridge报告Known=false。11组受控协议用例单列，不代替这两类实际native执行。

{limits} 等待API故障、计数/epoch耗尽未注入，首次安装和游戏实测均未完成。

## 验证与冻结

{proof}

[构建汇总](build-summary.json)、[完整日志](build.log)、[runner记录](build-run.json)、[首轮日志](build-first-wrapper.log)、[原生准入结果](native-admission-evidence.json)、[源码归档](source-archive-check.json)、[暂存归档](package-archive-check.json)、[与1496差异](changes-from-1496.json)、[有限交叉审查](review.md)。

执行命令（仓库根，PowerShell7）：`scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/void-admission-final-20261024`。新测试脚本由Build必跑，缺已pin helper/Dobby失败；固定缓存/工具链与干净环境B19复现另行验收。旧的mid3559和父链六条联合用例全部重跑通过，不倒写旧结论。

23范围/1706指令/6E8及三输入hash继续[核对](terminal-evidence-check.json)，[静态证据来源](static-evidence-origin.json)单列；本批不增加游戏调用路径结论。没有安装、游戏/UI操作、游戏进程终止、原游戏文件修改或commit/push，游戏仍1021。

## 后续

[生产接线计划](next-integration.md)：先解决首次执行屏障，再由固定installer签发完整发布结果，才能恢复写后准入并验证actual listener/全run闭合。[完整标准](../../../.docs/foundation-expansion/SPEC.md)仅B03整项/B18a子项勾选，用户“实测稍后”保持。[授权](scope.md)、[正式报告](../../2026-10-24_底层-原生调用准入-report.md)。
""")

write(repo / "docs/2026-10-24_底层-原生调用准入-report.md", f"""# 原生调用准入实现验证

2026-10-24，flavor=null，兼容框架实现验证。{result}完整通过并冻结暂存。{behavior} {limits}

用户已确认允许恢复代理；三个代理分别负责native gate、Runtime lease、独立fixture，并进行有限交叉复核，root唯一builder。按[授权](validation/{batch.name}/scope.md)继续代码、不安装和实测，完整B01–B19仍未完成。

## Evidence → Finding → Path

| E-id | source_ref | repro_command（仓库根） | content_hash |
|---|---|---|---|
| E-build | [完整构建](validation/{batch.name}/build.log) | `scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/void-admission-final-20261024` | {summary['logSha256']} |
| E-native | [原生结果](validation/{batch.name}/native-admission-evidence.json) | `scripts/Test-VoidAdmissionNative.ps1 -HelperDirectory D:/RizToolbox-build-cache/void-admission-final-20261024` | {hashlib.sha256((batch/'native-admission-evidence.json').read_bytes()).hexdigest()} |
| E-sources | [归档校验](validation/{batch.name}/source-archive-check.json) | `python docs/validation/{batch.name}/freeze-source.py` | {source['archiveSha256']} |

| F-id | severity | evidence_ids | confidence | location | status |
|---|---|---|---|---|---|
| F-admission | n/a_re | E-build,E-native,E-sources | high，固定native fixture范围 | VoidContext.cpp的Admit/Close/Verify/Release | 三phase计数与原调用一次、异常清理通过 |
| F-runtime | n/a_re | E-build,E-sources | high，实际bridge与受控协议分列 | NativeVoidAdmissionLease/Bridge.Admission | 错线程不消费凭据；Unknown贯通；写后Dispose不开放 |
| F-first-install | n/a_re | [复核](validation/{batch.name}/next-integration.md) | high，现有Loader代码范围 | 首次父detour安装 | 未证明，生产mid未发布 |

P-admission，path_type=callflow：真实Dobby父entry → old physical InFlight登记 → native admission（Open立即进，Closing/Closed新root在CV等待，同槽已准入递归继续）→ CLR Enter/broker probe → native original → CLR Exit/probe → native Active/TLS退出 → 原native caller或原异常catch。另一个OS线程Close先停止准入，等待Active0再发私有receipt；owner Verify/Release重开，停车调用各继续一次。

## 修正与验证边界

交叉审查发现已签发lease转Unknown后未传播到bridge；增加一次ReportUnknown且包含报告器异常，保留本地未知责任。真实CLR probe验证停车期间没有进入managed观察。首轮全部测试和Package已完成，但root命令尾部转义错误使wrapper退出1；保留日志，新增probe后完整重跑exit0作为最终证据。不是以首轮末尾Package输出推定最新源码通过。

{proof}

OS wait故障、计数/epoch耗尽未注入；不声明线程强杀、首patch、真实IL2CPP GC/listener或游戏验收。lease尚未提供写成功的发布凭据；后续固定installer必须补齐这条实际路径。见[批次](validation/{batch.name}/README.md)与[后续计划](validation/{batch.name}/next-integration.md)。
""")

for path, header, relative in [
    (repo / "docs/IMPLEMENTATION-STATUS.md", "## 已验证到哪里", f"validation/{batch.name}/README.md"),
    (repo / ".docs/CURRENT-CHECKPOINT.md", "## 当前现场", f"../docs/validation/{batch.name}/README.md")]:
    old = path.read_text(encoding="utf-8-sig")
    block = f"\n\n- **最新{result}完整构建冻结暂存，未安装；游戏仍1021。** [原生调用准入]({relative})：{behavior}\n- **证据与边界**：{proof} 独立native {native['checks']}断言/15原调用、实际CLR probe和11组受控协议分别通过。{limits}\n- **后续**：首次安装屏障和固定publisher、实际listener/future来源封口及完整资源退休/Retry/Return/Loader。B01–B19 active，仅B03整项/B18a子项勾选，用户“实测稍后”保持；本批无安装/UI/游戏进程终止/原文件修改/commit/push。\n\n### 历史1496"
    assert old.count(header) == 1
    old = old.replace(header, header + block, 1)
    old = old.replace("### 历史1496\n\n- **最新1496", "### 历史1496\n\n- **历史1496", 1)
    write(path, old)

path = repo / ".docs/foundation-expansion/CHECKLIST.md"
old = path.read_text(encoding="utf-8-sig")
marker = "# Checklist: foundation-expansion\n\n"
assert marker in old
old = old.replace(marker, marker + f"最新[{result}](../../docs/validation/{batch.name}/README.md)已冻结暂存未安装；新增准入屏障、实际CLR停车/释放与11组受控协议。445源/18暂存，独立native {native['checks']}断言/15原调用；首patch及生产mid未证明，游戏仍1021。完整B01–B19 active，仅B03/B18a勾选，RequestPlay关闭，实测稍后。\n\n", 1)
old = old.replace("\n最新[1496/1496", "\n历史[1496/1496", 1)
write(path, old)
path = repo / "README.md"
old = path.read_text(encoding="utf-8-sig")
old, count = re.subn(r"2026-10-24 更新：[^\n]+", f"2026-10-24 更新：新增原生调用准入保护，实际Dobby/CLR停车、释放、递归与异常路径通过；25套1507/1507、445源/18暂存冻结。首次安装屏障和生产mid发布仍待补齐，新包未安装，游戏仍1021。见[本轮证据](docs/validation/{batch.name}/README.md)、[实现状态](docs/IMPLEMENTATION-STATUS.md)与[续接点](.docs/CURRENT-CHECKPOINT.md)。", old, count=1)
assert count == 1
write(path, old)
print(json.dumps(dict(published=True, total=summary["total"], sourceInputs=source["entries"], nativeChecks=native["checks"])))
