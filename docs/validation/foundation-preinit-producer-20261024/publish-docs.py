"""Publish only the completed, archived pre-init producer batch."""
import hashlib
import json
import re
import shutil
from datetime import datetime, timezone
from pathlib import Path

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
def read(name): return json.loads((batch / name).read_text(encoding="utf-8-sig"))
def write(path, body): path.write_text(body.strip() + "\n", encoding="utf-8", newline="\n")
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
summary, source, package, changes = [read(name) for name in ["build-summary.json", "source-archive-check.json", "package-archive-check.json", "changes-from-1511.json"]]
assert summary["passed"] == summary["total"] == 1515 and summary["projects"] == 25
assert source["entries"] == 474 and package["entries"] == 22
assert len(changes["added"]) == 21 and len(changes["changed"]) == 11 and not changes["removed"]
assert all(read(name)[key] for name, key in [("staging-check.json", "allMatched"), ("previous-installation-check.json", "allMatched"), ("original-game-check.json", "allUnchanged")])
for old, new in [("first-build.log", "doorstop-first-toolchain-failure.log"), ("second-build.log", "doorstop-standalone-success.log")]:
    shutil.copy2(Path("D:/RizToolbox-build-cache/doorstop-preinit-inputs-20261024") / old, batch / new)
run = dict(completedAtUtc=datetime.now(timezone.utc).isoformat(), sessionId=7957, exitCode=0,
    previousSessionId=93340, previousExitCode=1, previousLog="build-first-parse-failure.log",
    previousFailure="PowerShell parser error at the mapped validation script's multiline -or expression; native transports and rejection fixtures had passed. Corrected before final complete build.",
    sourceChangesBeforeFinalBuild=["Mapped script continuation and exact three-write/reentry checks", "DisableIfUnwritten returns Unknown if a concurrent state change made no-write disable unprovable", "Do not bootstrap CLR after original il2cpp_init returned 0", "Dated LGPL modified-file notice"],
    standaloneDoorstopToolchainFailure="Upstream C11 logger __VA_OPT__ rejected by this MSVC; build flags now explicitly select traditional preprocessor /Zc:preprocessor-; first and successful standalone logs retained.",
    logSha256=summary["logSha256"], rootSoleBuilder=True, installed=False, gameExecuted=False)
write(batch / "build-run.json", json.dumps(run, ensure_ascii=False, indent=2))
result = "1515/1515、25 套（Runtime160、Hooks171、Audio573）"
behavior = "实际 Doorstop pre-init 路径已接原生 producer；先校验固定游戏与 Dobby 身份，准备 runtime-invoke、UnityEvent 父入口和内段 relay，再按固定顺序发布三处写入。CLR 采用已存在的身份和 epoch，不重新安装这两个通道。"
limits = "真实 Doorstop 冷启动时序、生产 CLR 回调与 IL2CPP/listener/GC 尚未验收；全部 listener/future 来源、连续 mutation/provider 转交和取消、全消费者资源退休及完整 Retry/Return/Loader 仍缺。RequestPlay 保持关闭，Pending 保留，InlineExecutionUnobserved=true、DispatchPathVerified=false。"
proof = f"474 项源码与 22 项暂存包逐项冻结；较 1511 新增21、修改11、删除0。源码 ZIP `{source['archiveSha256']}`，包 ZIP `{package['archiveSha256']}`，完整日志 `{summary['logSha256']}`。旧1021安装18项、其326项归档与原游戏三文件哈希匹配。"
diagnostics = f"完整构建保留 {len(summary['compilerDiagnosticLines'])} 条去重编译诊断（包括 upstream native warning），详见 build-summary；不宣称零警告或干净环境复现。"
write(batch / "README.md", f"""# 原生启动安装与托管采用

2026-10-24。**{result}通过，源码与新包已冻结，未安装。** {behavior}

## 这轮实际增加了什么

| 层 | 已实现/已验证 | 尚缺的联合验收 |
|---|---|---|
| 原生启动 | Doorstop 在 original il2cpp_init 前调用 producer；Ready 后只调用原 init 一次；返回0不启动 CLR，原异常不捕获 | 真正游戏冷启动、时序和异常分支 |
| 首次发布 | 固定 hash/PE/字节/unwind/ABI/实际 init 身份；三处写入、护页恢复、跳转读回；重入转 Unknown | 游戏首次执行前独占合同的实机证据 |
| 托管接入 | 生产 RequireReady、runtime-invoke Adopt 与 broker bootstrap 路径已编译；4 组 ABI/拒绝回归 | 实际 producer 与 CLR 在同进程采用、生产 callback |
| 交付 | 22 项包，修改版 Doorstop 对应源码和 LGPL 文本齐全；allocator 避开已认领槽位 | 干净环境复现、装入游戏和升级矩阵 |

真实原生 runtime-invoke fixture **274 断言**，12 次原调用、8 次 After、2 个跨 Attach 的旧调用不观察，原生/observer SEH与C++、worker、递归及 full-pointer 参数返回检查通过。写前拒绝 **5 个独立进程/100 断言**，均零游戏写入。

实际固定 GameAssembly 在独立进程用 `DONT_RESOLVE_DLL_REFERENCES` 映射：**175 断言**证明真实 producer 的父入口、runtime 及五字节 CALL/relay 三处发布。未执行 DllMain、游戏 init 或任一游戏函数。Ready 读数为重入前的 **phase3/unknown0/writes3**；再次 Before 返回2且内存不再写，最终为 **phase4/unknown1/writes3**。两种时间点分列；本例不证明游戏运行或首次执行屏障安全。

{limits}

## 证据与复现

{proof} {diagnostics}

仓库根 PowerShell7：`scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/preinit-producer-final-20261024 -ValidationGameAssemblyPath D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll`。省略 ValidationGameAssemblyPath 时不会运行真实映像正向检查；不能引用本批175断言作为那次构建的结果。

[完整日志](build.log)、[汇总](build-summary.json)、[runner](build-run.json)、[首轮失败](build-first-parse-failure.log)、[runtime 原生](native-runtime-invoke-evidence.json)、[5拒绝](native-rejection-evidence.json)、[映像发布](mapped-publication-evidence.json)、[Doorstop](doorstop-evidence.json)、[源码归档](source-archive-check.json)、[包归档](package-archive-check.json)、[与1511差异](changes-from-1511.json)、[有限交叉审查](review.md)。首轮 mapped 验证脚本解析失败；修正并补强重入、Unknown 和 init 失败分支后重跑全量，最终 session7957 exit0。独立 Doorstop 工具链首次失败及成功日志也保留。

仅推进代码和离线验证；无安装、游戏/UI操作、进程终止、原文件修改或commit/push。游戏仍旧1021包。静态23范围/1706指令/6E8与三输入hash再核[结果](terminal-evidence-check.json)，[来源](static-evidence-origin.json)单列。

## 下一步

先做[真实 CLR 联合采用及冷启动证据](next-integration.md)，再推进 listener/future 与完整消费者退休。完整[B01–B19](../../../.docs/foundation-expansion/SPEC.md)仍 active，仅 B03 整项/B18a 子项勾选，实测稍后。[授权](scope.md)、[正式报告](../../2026-10-24_底层-原生启动安装-report.md)。
""")
write(batch / "next-integration.md", """# 下一阶段实施与验收

前提：1515批已冻结；原生生产发布与 managed adoption 源码已实现，未安装和运行游戏。用户实测稍后的约束保持。不能把下一步计划写成已通过。

## 1. 实际 CLR 采用（B02/B07/B19）

隔离 managed 子进程用 DONT_RESOLVE_DLL_REFERENCES 映射固定游戏 PE，真实调用 producer，再执行生产 NativeFirstInstall.RequireReady→NativeRuntimeInvokeBridge.Adopt 与 Unity bootstrap Adopt/Attach。记录实际 native Info0→1、slot31、epoch、目标与 helper 身份；重复/错误身份拒绝，无二次游戏字节写入。零调用任何游戏地址，gameRuntimeVerified=false。保留已观察 Ready 与重入后的 Unknown 两个时间点。

另以独立真实 native fixture 覆盖 native→CLR→broker→ledger 回调及 worker、旧调用跨Attach、原异常和CLR故障；受控图不能替代 IL2CPP GC。

## 2. 冷启动时序（B02/B07/B19，支持B01）

用同一 session 记录 pre-init→Ready（三处发布）→原 init 恰好一次/返回或异常→managed Attach→首合法 tick/event。先离线 wrapper 实测 native original 计数、0返回、SEH/C++与重复初始化拒绝；后续获准游戏实测时再用固定新包证明实际首次执行前的合同。缺少证据不能宣称 coldboot accepted。

## 3. 全消费者退休（B05/B08/B11/B13/B15）

沿实际 executing 的三条 listener callsite追踪 leaf、provider、异步转交和连续 mutation；不清楚的来源保持 opaque/Pending。所有 future 来源已撤回或自然结束后，组合 audio、图 roots、globals、menu/result/settings 等每个 owner 的证据，顺序执行 VerifyConsumersEnded→Close→bridge/bundle/reservation/handler/coordinator退休，最后装配同新revision的 Retry/Return/Loader。

至少覆盖正常、失败、取消、局中 provider 撤回及20轮重开：原调用一次、Unknown不释放、条件式恢复当前基线、资源计数不单调增长。完整闭合前 RequestPlay 关闭；整项勾选仍须 SPEC 的真实游戏、版本和失败矩阵。
""")
write(repo / "docs/2026-10-24_底层-原生启动安装-report.md", f"""# 原生启动安装实现验证

2026-10-24，flavor=null，兼容框架工程验证。**{result}通过，未安装。** {behavior} {limits}

按[scope](validation/{batch.name}/scope.md)仅推进代码与隔离进程验证。三个代理实现及有限交叉复核，root唯一builder。完整B01–B19尚未完成。

## Evidence → Finding → Path

| E-id | source_ref | repro_command（仓库根） | content_hash |
|---|---|---|---|
| E-build | [全量](validation/{batch.name}/build.log) | `scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/preinit-producer-final-20261024 -ValidationGameAssemblyPath D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll` | {summary['logSha256']} |
| E-mapped | [映像发布](validation/{batch.name}/mapped-publication-evidence.json) | 同上，执行独立 FirstInstallMapped.Fixture | {sha(batch/'mapped-publication-evidence.json')} |
| E-runtime | [runtime](validation/{batch.name}/native-runtime-invoke-evidence.json) | 同上，执行独立 RuntimeInvoke.Fixture | {sha(batch/'native-runtime-invoke-evidence.json')} |
| E-source | [源码](validation/{batch.name}/source-archive-check.json) | `python docs/validation/{batch.name}/freeze-source.py` | {source['archiveSha256']} |

| F-id | severity | evidence_ids | confidence / location | status |
|---|---|---|---|---|
| F-publisher | n/a_re | E-mapped,E-source | high，固定映像隔离进程；FirstInstall.cpp | 真实三处发布与10次稳定身份读取、护页/relay/unwind核对通过；非冷启动证明 |
| F-reentry | n/a_re | E-mapped,E-source | high，Ready后再次Before | 返回2→Unknown4/1/3，写次数保持3、字节不变 |
| F-runtime | n/a_re | E-runtime,E-build | high，真实Windows x64 fixture | native original一次、原参数/返回/异常保持、observer晚接入与故障隔离 |
| F-managed | n/a_re | E-build,E-source | medium，生产路径编译+ABI/拒绝回归 | 实际CLR与真实producer同进程采用待补；未宣传gameRuntimeVerified |
| F-startup | n/a_re | E-source,E-build | static，Doorstop bootstrap/shim | init失败不启CLR、原异常保持、改版对应源码齐；真实冷启时序待测 |

P-publish，path_type=callflow：Doorstop捕获原init→原生shim→固定hash/PE/代码/unwind核验→Dobby Prepare与native Configure（尚不写游戏）→mid CALL、Unity父entry、runtime entry三次发布→读回known Ready→原init一次→原init成功才启动CLR→RequireReady→观察者Attach。写后失败或重入Unknown不得继续原init/CLR；本批映像fixture只执行到发布/身份/重入，未执行游戏init或回调。

## 修正、交付与限制

第一全量在原生274及5拒绝通过后因验证脚本PowerShell换行解析失败，日志保留。最终前修正该错误、补Ready重入检查、禁止并发Unknown被误报无写禁用，并修正原init返回0仍可能进入CLR的分支。第二全量session7957实际exit0。工具链独立初次错误与传统预处理器修正均保留原始日志。以上不等于每个分支已有精确故障注入。

{proof} {diagnostics}

修改版Doorstop对应源码49entries含必要源码、生成模板、许可证、修改标记与构建recipe；外部工具链哈希固定，未完成干净环境B19复现。没有安装和游戏执行，生产listener/GC/完整资源闭合亦未验收。[批次索引](validation/{batch.name}/README.md)、[下一阶段](validation/{batch.name}/next-integration.md)。
""")
for path, header, relative in [(repo/"docs/IMPLEMENTATION-STATUS.md", "## 已验证到哪里", f"validation/{batch.name}/README.md"), (repo/".docs/CURRENT-CHECKPOINT.md", "## 当前现场", f"../docs/validation/{batch.name}/README.md")]:
    old = path.read_text(encoding="utf-8-sig")
    assert old.count(header) == 1 and "### 历史1511" not in old
    block = f"\n\n- **最新{result}完整构建通过，474源/22包冻结，未安装。** [原生启动安装]({relative})：{behavior}\n- **实证**：native runtime274；写前5进程100；实际映像175（不执行游戏代码），Ready重入转Unknown且无第4写。生产CLR同进程联合采用和真实冷启动仍待验证。{diagnostics}\n- **余项**：{limits} 下一步先做真实CLR采用，再闭合冷启动与全部消费者。完整B01–B19仍active，仅B03整项/B18a子项勾选，实测稍后；旧1021安装18项及游戏3hash不变。\n\n### 历史1511"
    write(path, old.replace(header, header+block, 1).replace("### 历史1511\n\n- **最新1511", "### 历史1511\n\n- **历史1511", 1))
path = repo/".docs/foundation-expansion/CHECKLIST.md"
old = path.read_text(encoding="utf-8-sig"); marker = "# Checklist: foundation-expansion\n\n"
assert marker in old
old = old.replace(marker, marker+f"最新[{result}](../../docs/validation/{batch.name}/README.md)已冻结474源/22包，未安装。生产pre-init publisher及托管采用路径已实现，真实映像175断言验证三处发布但不执行游戏代码；真实CLR同进程采用、冷启、listener/GC与全run退休仍缺。RequestPlay关闭，B01–B19 active，仅B03整项/B18a子项勾选，实测稍后。\n\n", 1)
write(path, old.replace("\n最新[1511/1511", "\n历史[1511/1511", 1))
path = repo/"README.md"; old = path.read_text(encoding="utf-8-sig")
old, count = re.subn(r"2026-10-24 更新：[^\n]+", f"2026-10-24 更新：已接实际Doorstop原生pre-init安装与托管采用路径；25套1515/1515、474源/22包冻结，未安装。固定映像三处发布175断言通过（不执行游戏代码）；真实CLR接入、冷启动与完整资源闭合待验收。见[本轮证据](docs/validation/{batch.name}/README.md)、[实现状态](docs/IMPLEMENTATION-STATUS.md)与[续接点](.docs/CURRENT-CHECKPOINT.md)。", old, count=1)
assert count == 1
write(path, old)
print(json.dumps(dict(published=True, total=1515, sourceInputs=474, packageEntries=22, diagnostics=len(summary['compilerDiagnosticLines']))))
