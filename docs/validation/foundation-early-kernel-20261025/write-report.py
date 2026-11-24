"""Generate final human reports only from the strict completed archive.

No builds, DLL loads, children, installation, or game writes are performed.
The actual report is intentionally absent until archive-final.py has passed.
"""
from pathlib import Path
import argparse
import hashlib
import json
import zipfile


BATCH = Path(__file__).resolve().parent
REPO = BATCH.parents[2]
REPORT = REPO / "docs/2026-10-25_底层-新核运行链验证-report.md"


def require(value, message):
    if not value:
        raise ValueError(message)


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check-only", action="store_true")
    args = parser.parse_args()
    summary = read(BATCH / "final-summary.json")
    terminal = read(BATCH / "terminal.json")
    require(summary["passed"] is True and summary["sourceInputs"] == 604 and summary["binaryRows"] == 18,
            "Missing complete final archive")
    require(terminal["sessionId"] == summary["sessionId"] and terminal["exitCode"] == 0
            and terminal["terminalExitCodeObserved"] is True and terminal["rootSoleBuilder"] is True,
            "Missing actual root terminal")
    require(sha(BATCH / "terminal.json") == summary["terminalSha256"]
            and sha(BATCH / "command-result.json") == summary["commandSha256"]
            and sha(BATCH / "archive-final.py") == summary["archiverSha256"]
            and sha(Path(__file__)) == summary["reportGeneratorSha256"], "Archive/tool identity changed")
    for manifest_name, archive_name, count in [("source-final-manifest.json", "source-final-inputs.zip", 604),
            ("binary-final-manifest.json", "final-binaries.zip", 18),
            ("raw-final-manifest.json", "final-cache-raw.zip", summary["rawFiles"])]:
        rows = read(BATCH / manifest_name)["files"]
        require(len(rows) == count, "Wrong final report manifest count")
        with zipfile.ZipFile(BATCH / archive_name) as archive:
            require(len(archive.namelist()) == count and set(archive.namelist()) == {r["path"] for r in rows}, "ZIP entries changed")
            for row in rows:
                require(hashlib.sha256(archive.read(row["path"])).hexdigest() == row["sha256"], "ZIP identity changed")
    require(summary["kernel"]["cases"] == 73 and summary["legacyPublisher"]["cases"] == 57
            and summary["legacyEarly"]["cases"] == 73 and summary["coreFullSuitesRerun"] is False,
            "Wrong physical matrix scope")
    require(not any(summary[k] for k in ["productionV4Issued", "gameFirstInstallVerified", "installed", "gameExecuted",
                "allTransitiveOsDependenciesSafeVerified", "linuxRuntimeVerified", "engineCancellationCompleted", "globalRetirementVerified"]),
            "Unsupported expanded production claim")
    if args.check_only:
        print(json.dumps(dict(reportInputsValid=True, reportGenerated=False, sessionId=summary["sessionId"])))
        return
    require(not REPORT.exists(), "Never replace an earlier final report")
    k, p, legacy = summary["kernel"], summary["legacyPublisher"], summary["legacyEarly"]
    debug = k["debuggerProof"]
    binary_rows = read(BATCH / "binary-final-manifest.json")["files"]
    prefix = "validation/foundation-early-kernel-20261025/"
    binary_table = "\n".join(f"| `{r['path']}` | `{r['sha256']}` |" for r in binary_rows)
    history_lines = []
    for row in summary["history"]:
        v = row["result"]
        if v["sessionId"] == 31803:
            result = "实际主执行退出1；新链72个无调试器样本通过，调试器样本1失败，legacy尚未开始"
        else:
            result = f"诊断driver退出{v['actualDriverTerminalExitCode']}；三个实际child退出{v['actualDiagnosticChildrenExitCodes']}，不计正式矩阵"
        history_lines.append(f"- [{v['sessionId']}真实终态]({prefix}{row['path']})：{result}。")
    text = f"""# 底层：新 Kernel 早期运行链验证

2026-10-25。**固定自有宿主的新 Kernel 链73项、旧发布器57项、旧早期链73项均已完成正式验证并归档。** root实际观察session {summary['sessionId']} 终态0；本轮是原生运行链集成，没有重跑旧25套1617项，也没有改变core23528的23项包字节或安装到游戏。

## 这次实现了什么

新链使用`RizKernelOwnedHost → RizFixedOwnedTargets → RizKernelOwnedProxy → RizKernelFixedPublisher / RizToolbox.EngineNodeKernel / Recorder`普通静态依赖。Kernel只含8项engine-node API；新publisher复用canonical固定生产过程，记录版本2，共享wire3，Kernel调用ABI2/scalar record1。它们是不同层，不能互换版本号。

新Kernel移除了早期producer对庞大JudgeContext的普通依赖，但旧JudgeContext59导出仍服务其他typed hooks/atomicKnown。两模块没有自动共享slot、epoch、Ready或Unknown；生产V4/coordinator仍须聚合真实准入与故障责任。新owned链通过不表示生产host已经采用Kernel。

## 正式原件

| 独立矩阵 | 实际cases | 独立断言 / 原生检查 |
| --- | ---: | --- |
| 新Kernel早期链 | 73 | {k['independentAssertions']} event/memory断言，{k['heldFiles']}项held输入 |
| 默认旧固定发布器 | 57 | host {p['hostChecks']}、receipt {p['receiptAssertions']}、unwind {p['unwindAssertions']}、normal calls {p['callsAssertions']} |
| 默认旧早期链 | 73 | {legacy['independentAssertions']} event/memory断言，{legacy['heldFiles']}项held输入 |

这些是203个矩阵case对应的自有宿主进程；附加debugger是监督进程，不能把次数混成一条游戏运行。每个early矩阵含baseline14先行、其余15模式、56个内部故障及一个debug补充。独立异常baseline为跨child同binary/system比较，不是同对象同run。

成功须同时满足[实际终态]({prefix}terminal.json)、[七步骤完成记录]({prefix}command-result.json)、三aggregate/每个process原件、held前后hash和映射/receipt完整性。归档脚本不凭命令log中的PASS生成成功。[最终汇总]({prefix}final-summary.json)、[完整缓存原件ZIP]({prefix}final-cache-raw.zip)保存全部结果、映射、字节、页面、异常和调试器原件。

## 调试器证据及失败历史

新调试器区分每个mapping的instance、文件身份、LOAD/UNLOAD及最终selected映像；验证器确认非selected映像在proxy前退休、文件身份与父读取磁盘对应，并将每行原日志与shared事件配对。真实LOAD共{debug['actualLoadCount']}次、UNLOAD共{debug['actualUnloadCount']}次；不能把一次临时映射当作已完成DLL初始化，或简单忽略同名重复LOAD。

硬件返回观察验证实际Get/Set/Get线程上下文、DR槽/清理和真实RSP；`hardwareReturnStackAndSlotCleanupVerified={str(debug['hardwareReturnStackAndSlotCleanupVerified']).lower()}`，栈不匹配观察次数{debug['returnStackMismatches']}。详细entry/return配对、actualRsp/expectedRsp及before/after上下文保存在[新debugger结果]({prefix}raw/cache/kernel-cases/normal-debugger/result.json)和[原JSONL]({prefix}raw/cache/kernel-cases/normal-debugger/debug-events.jsonl)。retired通知1007实际{debug['retiredUnloadNotifications']}次；零命中时没有宣称该分支动态覆盖。Kernel/Publisher无entry/TLS，日志只记录真实mapping，不编造初始化返回。完整OS loader生命周期闭包仍未证明。

{chr(10).join(history_lines)}

前三次诊断各有自己的源码hash与ZIP；52109使用中间hardware源码，不能代替强化RSP后最终冻结版本。原31803失败没有算作正式通过，历史原件不改写。

## 冻结与保全

权威基线为kernel-install组合599源码；本批604项是12项预期修改加2个profile头、3份实际调用的verifier声明。[604源码清单]({prefix}source-final-manifest.json)及[source ZIP]({prefix}source-final-inputs.zip)逐项校验，SHA256 `{summary['sourceZipSha256']}`。18条二进制行按7+4+7隔离保存，不把两profile同名Targets当作同一个映像；[二进制ZIP]({prefix}final-binaries.zip) SHA256 `{summary['binaryZipSha256']}`。

[23包保全]({prefix}package-preservation-check.json)仍为core23528字节；[真实游戏18安装项及4原始项]({prefix}real-game-preservation-check.json)重新只读核hash全部匹配。没有安装、启动游戏或回写任何历史批次。

| 最终文件行 | 实际SHA256 |
| --- | --- |
{binary_table}

## 下一条实际工作

[production-next]({prefix}production-next.md)给出实际系统WinHTTP加载/按名解析缝、最少新文件和producer→Kernel→CLR只读采用接口。下一步先做系统转发自有宿主与新发布凭据，再核固定游戏启动准入；owned marker不移除，pre-il2cpp_init不当作UnityPlayer早期证明。

本轮`ownedOnly=true`。没有真实系统WinHTTP/游戏首次接入/V4、scheduler/async/Start内联清理或全局退休完成凭据；`EngineCancelled=false`、Pending保留、RequestPlay关闭。Linux和OS全传递依赖闭包未验证。完整B01–B19范围保持。
"""
    REPORT.write_text(text, encoding="utf-8")
    readme = f"""# 新 Kernel 早期运行链：正式归档

2026-10-25。root实际观察session {summary['sessionId']} 终态0；新链73、旧publisher57、旧链73均通过。此为独立原生集成批，旧25套1617项与23项包属于core23528，本批没有重跑或替换。

- [正式报告](../../2026-10-25_底层-新核运行链验证-report.md)、[最终汇总](final-summary.json)、[真实终态](terminal.json)、[七步骤](command-result.json)。
- [604源码清单](source-final-manifest.json) / [冻结ZIP](source-final-inputs.zip)，SHA `{summary['sourceZipSha256']}`。
- [18二进制行](binary-final-manifest.json) / [ZIP](final-binaries.zip)，SHA `{summary['binaryZipSha256']}`；7+4+7隔离身份。
- [完整缓存原件](final-cache-raw.zip) / [原件清单](raw-final-manifest.json)，SHA `{summary['rawZipSha256']}`。
- [新链73](raw/cache/kernel-cases/fixture-result.json)、[旧publisher57](raw/cache/legacy-publisher-cases/fixture-result.json)、[旧链73](raw/cache/legacy-cases/fixture-result.json)。
- [31803原失败](history/first-attempt/terminal-observation.json)、诊断原件均保留在history，不计正式通过。
- [代码接续](production-next.md)、[编译profile](native-notes.md)、[脚本静态审查](scripts-review.md)。

Publisher记录2、共享wire3、Kernel ABI2/scalar1分别核验。旧JudgeContext仍独立存在，两模块不共享Ready/Unknown。仅自有Targets，未授生产V4、游戏接入、取消或全局退休；无安装和游戏执行。
"""
    (BATCH / "README.md").write_text(readme, encoding="utf-8")
    print(json.dumps(dict(report=str(REPORT), reportSha256=sha(REPORT), readmeSha256=sha(BATCH / "README.md"))))


if __name__ == "__main__":
    main()
