"""Create the ordinary engineering report from final local evidence."""
import hashlib
import json
from pathlib import Path

batch = Path(__file__).resolve().parent
repo = batch.parents[2]


def read(name):
    return json.loads((batch / name).read_text(encoding="utf-8-sig"))


def digest(name):
    return hashlib.sha256((batch / name).read_bytes()).hexdigest()


summary, run = read("build-summary.json"), read("build-run.json")
assert run["exitCode"] == 0 and summary["passed"] == summary["total"] == 1515
assert digest("build.log") == summary["logSha256"] == run["logSha256"]
mapped, unknown, managed = read("mapped-publication-evidence.json"), read("atomic-unknown-evidence.json"), read("managed-adoption-evidence.json")
assert mapped["passed"] and unknown["passed"] and managed["passed"]
source, package = read("source-archive-check.json"), read("package-archive-check.json")
evidence = [("E-001", "固定文件profile", "fixed-profile-evidence.json"),
            ("E-002", "实际隔离映像发布与篡改拒绝", "mapped-publication-evidence.json"),
            ("E-003", "两版安装记录原子Unknown竞争", "atomic-unknown-evidence.json"),
            ("E-004", "生产C#只读采用", "managed-adoption-evidence.json"),
            ("E-005", "完整构建终态", "build-run.json"),
            ("E-006", "源码冻结", "source-archive-check.json"),
            ("E-007", "包冻结", "package-archive-check.json")]
rows = "\n".join(f'| {identity} | {title} | [{name}](validation/{batch.name}/{name}) | `{digest(name)}` |' for identity, title, name in evidence)
body = f'''# 底层监听发布与只读采用报告

日期：2026-10-24；普通兼容底层工程报告，flavor=null。

## 概述

上一批三处invokable的原生前后观察尚未纳入生产预初始化安装，旧144字节安装记录只有三次写入范围。本批把六区发布接进固定游戏版本的实际producer，并增加416字节V2记录和C#只读采用。root完整构建session {run["sessionId"]} exit0，25套1515/1515；493源/22包冻结未安装。映像发布5进程1943检查、原子Unknown713检查与CLR采用72检查/2进程通过。全部不执行游戏代码，尚不证明listener→CLR回调、IL2CPP对象根、全消费者退休或真实冷启动。

## 范围与复现

授权及限制见[scope.md](validation/{batch.name}/scope.md)。用户“先继续代码，实测稍后”保持；本批无安装、游戏UI、游戏进程终止或commit/push。固定输入为Steam25977441/Unity2022.3.62f2/Windows x64，GameAssembly SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`，Dobby SHA-256 `8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded`。

在仓库根目录使用PowerShell7：

```powershell
pwsh -File scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/listener-publication-final-20261024 -ValidationGameAssemblyPath D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll
```

工具为VS2019 14.29.30133、Windows SDK10.0.19041、.NET SDK8与现有固定依赖缓存。完整日志保留{len(summary["compilerDiagnosticLines"])}条去重编译诊断；不声明零warning、字节级可复现或干净环境B19矩阵。隔离映像测试以DONT_RESOLVE映射，不解析/执行游戏导入、不运行目标DllMain或游戏函数。

## Evidence

各JSON记录实际输入、结果与边界，完整日志SHA-256 `{summary["logSha256"]}`。

| ID | 观察 | 原件 | 原件SHA-256 |
|---|---|---|---|
{rows}

E-001复现脚本为本批check-fixed-profile.py，仅解析磁盘PE。E-002至E-004由上述Build.ps1调用对应Test-FirstInstallMappedNative、Test-FirstInstallUnknownNative和Test-FirstInstallManaged脚本。E-005由root真实终态记录，不能仅凭日志出现Package判断退出。E-006/E-007由本批freeze-source.py、freeze-package.py和verify-frozen-inputs.py逐项验证；冻结和验证脚本不运行游戏。

## Findings

### F-001：生产发布范围已扩展至九次且有V2身份

status=validated；category=design；severity=n/a_re；confidence=high；evidence_ids=[E-001,E-002,E-003]。

固定source RVA为1564470/15644C2/156451F，原宽度5/5/6；post为1564475/15644C7/1564525，完整宽度6/9/9。永久4KB RX块保存三个零额外栈post stub、CHAININFO、表与跳板。实际RtlLookup和受控栈上的RtlVirtualUnwind在首写前分别核12次，随后mid、三组pre/post、父入口、runtime共九次发布；原页权限、完整Unity body、实际source与stub/table均读回。任一有写尝试后的不确定永久Unknown，资源进程持有，不回滚。

V1布局144和原导出保持，写数如实为9；V2新增导出RizFirstInstall_GetInfoV2，含完整lane/site/PC/target/stub/table/protection身份。四种独立篡改分别修改source、post、relay和chain，均使V1/V2 Unknown且重入无第10写。V2九种非法header/reserved拒绝，caller伪造identity被实际快照覆盖。两版记录各100次读取都发生在Unknown atomic已置位、详细writer尚暂停期间，均没有误报Ready。

### F-002：C#采用真实V2，监听回调尚未接入

status=validated；category=design；severity=n/a_re；confidence=high；evidence_ids=[E-002,E-004]。

NativeFirstInstall.RequireReady只采用已加载且路径匹配的helper、固定GameAssembly以及实际GetInfoV2/lane176。采用和VerifyReady核完整身份、实际六区CALL/jump、跳板、stub replay、chain/table、OS注册和页权限；没有迟到安装或调用方构造Ready的入口。CLR72检查/2进程验证实际只读采用，以及既有runtime与双idle broker采用。listener仍Attached0、native invocations0、managed callbacks0，不能用此证明listener→CLR或ledger已实现。

### F-003：冻结范围明确，整体验收仍未完成

status=validated；category=other；severity=n/a_re；confidence=high；evidence_ids=[E-005,E-006,E-007]。

493声明源码输入在before、post manifest与current之间一致，22包逐项归档校验。源码ZIP `{source["archiveSha256"]}`；包ZIP `{package["archiveSha256"]}`。旧1021安装18项及其326源档案、原游戏三文件独立核对不变。自有native listener8349检查和既有回归继续通过，但未验dynamic-vtable mutation、target-read AV、foreign/overflow、missing post、完整exception record、prehelper逐指令展开、全部delegate leaves及mutation/StopCoroutine retirement。

## Path

P-001，path_type=callflow：固定profile原件核对（E-001/F-001）→预初始化准备永久代码与展开记录、首写前12+12检查（E-002/F-001）→九处发布并读回（E-002/F-001）→V1/V2完整快照和原子Unknown（E-003/F-001）→生产C#只读采用（E-004/F-002）→完整离线回归和冻结暂存（E-005至E-007/F-003）。

residual_risks：这条证据路径止于离线发布与只读采用；真实Doorstop冷启动和游戏运行没有执行。还须实现listener回调桥与ledger，并闭合动态listener/future来源、IL2CPP/GC、连续mutation/provider转交与取消、全部消费者和资源退休、完整Retry/Return/Loader。RequestPlay保持关闭、Pending保留、InlineExecutionUnobserved=true、DispatchPathVerified=false；完整B01–B19 active，仅B03整项/B18a子项已勾。下一步见[next-integration.md](validation/{batch.name}/next-integration.md)。
'''
destination = repo / "docs/2026-10-24_底层-监听发布与采用-report.md"
destination.write_text(body, encoding="utf-8", newline="\n")
print(str(destination))
