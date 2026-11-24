"""Publish this completed joint-fixture batch from its own frozen evidence."""
from pathlib import Path
import json
import re

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
def read(name): return json.loads((batch / name).read_text(encoding="utf-8-sig"))
def write(path, text): path.write_text(text.strip() + "\n", encoding="utf-8")
summary, source, package, changes = [read(name) for name in ["build-summary.json", "source-archive-check.json", "package-archive-check.json", "changes-from-1490.json"]]
joint = read("native-parent-mid-evidence.json")
assert summary["passed"] == summary["total"] == 1496 and summary["projects"] == 25
assert source["entries"] == 440 and package["entries"] == 18
assert len(changes["added"]) == 1 and not changes["removed"] and len(joint["passedCases"]) == 6
diags = len(summary["compilerDiagnosticLines"])
result = "1496/1496、25套；Runtime159/Hooks153/Audio573"
behavior = "新增6条隔离的联合原生回归：真实Dobby父入口→原生函数五字节CALL→mid helper→CLR bridge→broker当前私有凭据→ledger同代次列表采用。固定entry+0x140返回地址由原生标签取得，Dobby后中段CALL保持；空列表/外来递归、worker尾窗口/解绑、采用故障、成功采用后原生SEH和C++异常、跨代旧凭据拒绝均通过。"
limits = "列表图和持根仍是受控backend，未证明IL2CPP GC或真实游戏对象。生产mid并发指令发布仍未解决，patch未发布；实际listener执行、连续mutation与动态转交/取消、全部消费者/资源退休、完整Retry/Return/Loader仍缺。Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false，RequestPlay关闭。"
freeze = f"440源/18暂存，较1490新增1/修改{len(changes['changed'])}/删除0。源码ZIP `{source['archiveSha256']}`；暂存ZIP `{package['archiveSha256']}`；full exit0，日志SHA `{summary['logSha256']}`。旧1021安装18项、旧326归档及原游戏三hash匹配。完整日志含{diags}条不同编译诊断，实际信息见汇总，不宣称干净构建无warning。"

write(batch / "README.md", f"""# 事件父调用与中段观察的联合验证

2026-10-24。**{result}通过并冻结暂存，未安装。** {behavior}

{limits}

## 已执行的六条联合回归

| 用例 | 实际执行和断言 |
|---|---|
| 正常/空列表/外来递归 | 真Dobby和两个原生helper；实际caller/MethodInfo/mid PC；私有token随代次变化；六次原函数/中段/probe、五次实际context配对，外来MethodInfo不捕获 |
| worker尾窗口/解绑 | 真native worker通过mid后阻塞在original，此时mid=0但parent/binding/probe=1且owner不idle；worker不读图、不采用列表；逻辑detach保留原窗口和配对cleanup |
| 采用故障/原异常 | 采用责任先记录后故障，列表不发布；native原函数仍一次执行；SEH/C++在native catch核原code/kind/payload，abnormal不制造正常After |
| 旧凭据重放 | 真实连续调用复用同一个InvokeContext，actual frame相同而token变化；前代凭据在任何新列表采用前被拒绝 |
| 干净owner原生SEH | 冷fixture先成功mid采用，随后original SEH；根责任保留、After0、真实context Dispose和abnormal probe配对，native drain归零 |
| 干净owner原生C++ | 另一个冷fixture先成功采用再抛C++原异常；同样核原code/kind/payload、一次continuation、未伪造正常Returned或释放采用责任 |

六条新用例严格要求三个实际native路径，全部在独立子进程执行；缺依赖会失败，不以SKIP充当通过。新fixture kind4保留旧0–3 ABI，同一个FRAME的首32NOP留给Dobby、中段site=entry+0x13B、返回=entry+0x140。固定测试module由真实entry推导，未放宽Adapter偏移，也未把合成Frame交给ledger。

原生中段自身3559断言继续通过，独立EXE的nativeParentVerified=false仍准确；本批联合证明由[六条Audio新用例](native-parent-mid-evidence.json)单独给出，不倒写旧fixture结论。mid stats只覆盖dispatch，完整窗口依赖native parent。详见[源码复核](review.md)。

## 构建与冻结

{freeze}

[构建汇总](build-summary.json)、[完整日志](build.log)、[源码归档](source-archive-check.json)、[发布归档](package-archive-check.json)、[与1490差异](changes-from-1490.json)。执行命令（仓库根、PowerShell7）：`scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/event-mid-parent-final-20261024`。需要已核哈希的缓存和工具链；完整B19发布矩阵与干净环境复现尚未完成。

23范围/1706指令/6E8及三输入hash继续[重新核对](terminal-evidence-check.json)，沿用1490的[mid静态证据](mid-site-evidence.json)；本批不新增游戏反汇编结论。无安装、游戏/UI操作、游戏进程终止、原游戏文件修改或commit/push；游戏仍1021包。

## 下一步

按[生产安全发布计划](../foundation-event-mid-20261024/next-publication-plan.md)取得可证明的首次执行屏障，再接生产installer并验证actual listener与mutation；当前Loader首次parent安装前不具全局排他证明。继续完整消费者退休和Retry/Return/Loader，而非把本批原生联合通过当作全run关闭凭据。[完整标准](../../../.docs/foundation-expansion/SPEC.md)仅B03整项/B18a子项勾选，用户“实测稍后”保持。[授权范围](scope.md)、[正式报告](../../2026-10-24_底层-事件父链联合验证-report.md)。
""")

write(repo / "docs/2026-10-24_底层-事件父链联合验证-report.md", f"""# 事件父链联合原生验证

2026-10-24，flavor=null，兼容框架实现验证。{result}完整通过并冻结暂存。{behavior} {limits}

范围和授权见[scope](validation/foundation-event-mid-parent-20261024/scope.md)。遵守“先继续代码，实测稍后”，未进行游戏安装/UI或进程终止。完整B01–B19尚未完成。

## Evidence → Finding → Path

| E-id | source_ref | repro_command（仓库根） | content_hash |
|---|---|---|---|
| E-joint | [完整构建日志](validation/foundation-event-mid-parent-20261024/build.log) | `scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/event-mid-parent-final-20261024` | {summary['logSha256']} |
| E-claims | [联合断言范围](validation/foundation-event-mid-parent-20261024/native-parent-mid-evidence.json) | 上述构建中的Audio573，六个隔离native子进程 | 源码manifest/ZIP见[归档校验](validation/foundation-event-mid-parent-20261024/source-archive-check.json) |
| E-native | [fixture清单](validation/foundation-event-mid-parent-20261024/native-build/initialization-abi/native-manifest.json) | `scripts/Build-InitializationAbiFixture.ps1 -OutputDirectory D:/RizToolbox-build-cache/event-mid-parent-final-20261024/initialization-abi` | 清单内DLL/EXE SHA；[unwind](validation/foundation-event-mid-parent-20261024/native-build/initialization-abi/unwindinfo.txt) |

| F-id | severity | evidence_ids | confidence | location | status |
|---|---|---|---|---|---|
| F-joint | n/a_re | E-joint,E-claims,E-native | high，固定fixture范围 | native kind4/MidBridge/Current/ledger | 实际联合链通过，backend图/root受控 |
| F-tail | n/a_re | E-joint,E-claims | high，native worker范围 | Mid0而Parent1的post-mid original阻塞点 | 不因mid计数归零而放行owner idle，解绑保配对 |
| F-publish | n/a_re | [发布时序复核](validation/foundation-event-mid-20261024/next-publication-plan.md) | high，现有Loader路径 | 首次parent安装/生产mid publication | 未解决，生产patch未发布 |

P-joint，path_type=callflow：native caller → Dobby entry → NativeVoid helper Enter/broker physical token → actual original FRAME → actual E8 mid site → near tail relay → mid ASM/C++ → CLR MidBridge → 当前sink的private identity → ledger真实frame核验及受控采用 → mid返回 → native body → parent Exit/context Dispose/probe Exit → native caller或native异常catch。列表采用故障和原生异常均保原函数计数；不会用native catch结果伪造正常After。

## 时间线与交付

三代理先分别冻结原生kind4、联合测试并只读交叉复核；root统一构建。审查中修正六次physical original只有五次capture的计数，并改为每physical probe持本次context，避免借LastContext推测清理。首full在新test缺Runtime命名空间处CS0246失败；补using并消除两处新nullable诊断，四项定向通过后[完整1494](validation/foundation-event-mid-parent-20261024/build-1494.log)通过。复核发现原异常用例此前已经Unknown，追加两个冷owner成功采用后异常的隔离用例，再跑最终完整1496。失败/中间/最终日志分开保留，最终通过后冻结源码与包，复核旧安装/游戏hash。

{freeze}

后续先证明生产安全发布，再接真实游戏路径、IL2CPP采用和listener/future来源封口。完整消费者退休、返回/重试/Loader和19项验收保持原范围；未安装、游戏实测仍稍后。详细[批次索引](validation/foundation-event-mid-parent-20261024/README.md)。
""")

prefix = f"- **最新{result}完整构建冻结暂存，未安装；游戏仍1021。** [事件父链联合验证]({{link}})：{behavior}\n- **证据与范围**：{freeze} 新增6个Audio原生联合用例，独立mid3559仍通过。{limits}\n- **后续**：生产首次执行屏障和mid发布，实际listener/future来源封口，以及完整资源退休/Retry/Return/Loader。B01–B19 active，仅B03整项/B18a子项勾选，用户“实测稍后”保持；本批无安装/UI/游戏进程终止/原文件修改/commit/push。\n\n### 历史1490\n\n"
for name, marker, link in [
    (".docs/CURRENT-CHECKPOINT.md", "## 当前现场\n\n", "../docs/validation/foundation-event-mid-parent-20261024/README.md"),
    ("docs/IMPLEMENTATION-STATUS.md", "## 已验证到哪里\n\n", "validation/foundation-event-mid-parent-20261024/README.md"),
]:
    path = repo / name; body = path.read_text(encoding="utf-8-sig")
    body = re.sub(r'- \*\*WIP：1490冻结后继续联合父链回归。.*?\n\n', '', body, count=1)
    if "### 历史1490\n" not in body:
        assert marker in body; body = body.replace(marker, marker + prefix.replace("{link}", link), 1)
        write(path, body)
path = repo / "README.md"; body = path.read_text(encoding="utf-8-sig")
body = re.sub(r'2026-10-24 更新：.*?\n\n', '2026-10-24 更新：事件父调用与中段观察已通过真实Dobby/native/CLR/ledger联合回归；25套1496/1496及独立mid3559断言通过，440源/18发布冻结暂存。生产mid安全发布与真实IL2CPP/listener仍缺，新包未安装，游戏仍1021；完整底层继续推进。见[本轮证据](docs/validation/foundation-event-mid-parent-20261024/README.md)、[实现状态](docs/IMPLEMENTATION-STATUS.md)与[续接点](.docs/CURRENT-CHECKPOINT.md)。\n\n', body, count=1); write(path, body)
path = repo / ".docs/foundation-expansion/CHECKLIST.md"; body = path.read_text(encoding="utf-8-sig")
body = body.replace("updated: 2026-10-23", "updated: 2026-10-24", 1)
body = re.sub(r'最新\[1490/1490.*?\n\n', '最新[1496/1496、25套](../../docs/validation/foundation-event-mid-parent-20261024/README.md)已冻结暂存未安装，Runtime159/Hooks153/Audio573，新增6组真实Dobby父调用→mid→bridge/broker/ledger联合回归，包含干净owner成功采用后SEH/C++原异常。440源/18暂存，较1490新增1源；原生mid3559继续通过。图/root仍controlled，生产mid并发安全发布、真实IL2CPP/listener、全消费者退休和完整Retry/Return/Loader仍缺。RequestPlay关闭，仅B03整项/B18a子项勾选；B01–B19 active，用户“实测稍后”保持，旧安装/原游戏hash不变。\n\n', body, count=1); write(path, body)
print(json.dumps(dict(published=True, tests=summary["total"], sourceInputs=source["entries"], compilerDiagnosticLines=diags)))
