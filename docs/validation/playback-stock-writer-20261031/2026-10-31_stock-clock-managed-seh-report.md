# 播放时钟真实原生异常修复与当前源码验证

2026-10-31，`flavor=null`。本项修复已经进入主目录，验证范围是 S4 加确切的 managed/native 异常分类补丁，先于 S5/S6 合并。没有安装、启动游戏、操作游戏 UI 或账号；不签生产准入和 B01–B19 完成。授权和剩余工作见 [.docs 当前续接点](../../../.docs/CURRENT-CHECKPOINT.md)及 [N2 合同](../../../.docs/foundation-expansion/N2-STOCK-SOURCE-NEXT.md)。

原生 getter 的 SEH 经过 CLR 后会成为 `SEHException`。此前 reader 如果先撤回自己的 policy 再抛出这个异常，会被普通逻辑停用的快捷分支吞掉，错误地保留 `Known=true`。现在快捷分支先检查原生异常，包括包装异常的 InnerException 和 AggregateException 的全部子异常；真实原生故障始终通知 owner 并撤回权限。正常逻辑 Dispose 仍然返回实际 original 的值，保持已知物理状态。

联合结果原件为 `D:/riz-build-cache-next/playback-clock-managed-seh-fix-r1-20261031/validation-r1/test-result.json`，SHA `8c49589b57211060aa180a9351fc7ed9256fa601fb09ea8f6aae9ec07e180a08`；索引 SHA `f90c5f1f9377c534a72373eba770c632093f7ec7c1967b1e9bc092210b8feb23`。入口为 [验证索引](stock-clock-seh-validation-index.json)，collector 为 [Finalize-ClockSehIntegration.py](Finalize-ClockSehIntegration.py)。

## 实际验证

| Evidence | 结果与范围 |
|---|---|
| E-BEFORE | `playback-clock-review-seh-r1-20261031/review-result.json`，SHA `fa30079061d65452ec9af421a32cefb34e5ea6ff864b15b66eda49700e546130`。真实 kernel32!RaiseException；withdrawn-SEH 实际 exit2，Known=true 的反例保留 |
| E-AFTER | `playback-clock-review-seh-r2-20261031/review-result.json`，SHA `cc673ed8114382dd8a9d56e0198dec836a7639c4c40d5b8960745186634c4d95`。五个实际子进程全部 exit0：active、withdrawn、wrapped、aggregate 第二子项的真实 SEH 均 Known=false/owner notified；普通 logical Dispose 为 Known=true、无故障通知。每次 original 恰好一次、返回3、inFlight最终0 |
| E-CURRENT | `foundation-stock-clock-seh-core-r1`、`full-r2`、`release-r1`、`solution-r1`、`sdk-r1`，日期后缀均为 `20261031`。27套2465/2465、六 DLL Release、solution 0 error/0 warning、两个独立 SDK 插件14/14，全部实际 exit0；Release 保留一个既有 generated nullable warning |
| E-CLOCK | 同 fix case 的 `clock-checks-r1`：当前源码重新编译，10进程179项全部 exit0；`clock-symbols-r1` 绑定5组 PE/PDB、469源文档 |
| E-SOURCE | 与冻结 S4 的756个源文件逐项比较，仅 Bridge 一项变化，当前 SHA `79e4947c066ad91ae56cba29db50cf61f8d091777d4456569a2f2afad3450f07`；744 core 输入与4797个唯一证据文件均回核。原 S4 native helper 未改，只复用其独立 native 证明 |

F-SEH（`n/a_re/high/validated`）：E-BEFORE/E-AFTER 直接证明异常分类修复；它不证明所有未知 native 异常或全部 source writer 已覆盖。F-CURRENT（同级别）：E-CURRENT/E-CLOCK/E-SOURCE 证明这一个精确修补后的源码构建和受控回归通过，不能用于验收后续 S5/S6 新源码。

P-SEH（`path_type=callflow`）：实际 native GetTime original → 独立 reader 调用真实 RaiseException → CLR marshalled SEH／异常包装 → 递归原生故障分类 → bridge ReportFault → native policy 撤回与 owner 通知。逻辑停用走实际 inactive readback，保留 original 返回位和真实退出计数。

## 复现与失败保留

真实异常探针、编译命令、实际 stdout/stderr/exit 与精确源覆盖在 review r2 目录。collector 逐项验证实际结果、源码和产物，运行：

```powershell
& 'C:/Python314/python.exe' 'C:/Users/71957/Desktop/riz_toolbox/docs/validation/playback-stock-writer-20261031/Finalize-ClockSehIntegration.py'
```

已经存在结果目录时 collector 拒绝覆盖。重新运行验证需使用新的 case 输出并保留原件；不能把旧 manifest 的 path/hash 改成当前文件来伪造验收。原 full-r1 随会话中断，只有22套完成记录，没有完整终态/exit；其现场和 `full-r1-interruption-observation.json` 已保留，验收采用全新 full-r2。

S5 正式 provider/module、S6 跨局移交继续在独立 staging 开发，不能因为旧专项通过而视作已合主树。未知 lower writer/source generation、首次生产准入、任意 Seek 和 B01–B19 全部验收仍未完成，真实 Speed/Seek 保持关闭。
