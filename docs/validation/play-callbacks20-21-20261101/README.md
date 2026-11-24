# Play source20 与 Execute source21 已有实际正向调用

2026-11-01，`flavor=null`。两个此前缺少执行证据的站点现已分别通过自有 native 进程：实际五字节 CALL 进入 Invoke、到达 mid、调用一个非空 listener，并由现有私有 source/reference/child membership 关联同一来源事务。471 个产品 src 文件与当前主树逐字节相同，产品修改为0；这两项补的是确定站点的真实执行证据。范围见 [scope](scope.md)，历史 source19 没有重跑或并入计数。

联合原件 `D:/riz-build-cache-next/play-callbacks20-21-main-evidence-r1-20261101/test-result.json`，SHA `1248c515128b47d5df5060cdec09f824d49f881ec7c199db478992e3f4e18545`；索引 SHA `dc3dd6e29cc341cc038b7e7a6170a9b87704985638be6878b2f4aaffec0355e2`。入口为 [evidence-index.json](evidence-index.json)。每组冻结910输入：标准785中781相同、4个 tests 接线修改，另有125个 D-only 文件。源码前后不变、实际测试DLL/PE/PDB与源校验均保留，不能表述为“785源全同”。

| Evidence | 实际证明 |
|---|---|
| E-20 | `play-site20-stage-r1-20261101/completion-r1.json` SHA `6ab8c324a827144ec53feb66453076f2b71bcce28b28b61b31d42f63ff20c237`。PlayOnComplete 固定 profile `0x395d50..0x395d55`、`E8 8B E5 1C 01`；自有 fixture实际 CALL→Invoke→mid→非空listener各一次。source20 Before/After两行、一完整事务；onComplete字段0x90的child membership与实际Invoke generation对应；reference TLS 6/6投递完成 |
| E-21 | `execute-site21-stage-r1-20261101/completion-r1.json` SHA `b332e461b4b6a74fa95fbd6641dfbd75eac27c52e154bff5a02c91984898d573`。实际Update24→source14 CALL→Execute19→source21 CALL→Void0/mid/非空listener，与既有playing occurrence关联。随后既有Remove source6清理也真实执行；原件是6/14/21三个来源、六行、三完整事务，reference TLS 21/21完成 |
| E-SYMBOLS | 两组各5 managed PE/PDB、565文档；各1 native PE/PDB、14 COFF对象，C13源checksum与linked PDB module逐项对应。actual native/managed/execution/symbols均0；site20复用已成功的r2 native，只有其新托管与正向进程续跑，未把中断managed父进程说成通过 |
| E-PRODUCT | [收集器](../playback-stock-writer-20261031/Collect-PlayCallbackSites.py)核471个主树产品源、两组before/after清单与标准ZIP差异、实际结果、原始index和符号；本次收集器退出0，没有执行产品。执行命令、helper导入导出、原生来源与完整差异分别在两个D案例的报告/index内 |

F-CALLBACK（`severity=n/a_re`、`category=other`、`status=validated`、`confidence=high`，evidence_ids=[E-20,E-21,E-SYMBOLS,E-PRODUCT]）：现有私有归属路径支持这两个确定站点的实际非空回调。位置为自有Play/Execute caller、NativeTween source/reference observer与child registration。两个独立正向进程分别证明自己的路径，没有声称单个进程覆盖全部站点。

F-EFFECT（同级、validated/high，evidence_ids=[E-20,E-21]）：两组非空listener仍各有一个Unaccounted effect，`EffectsEnded=false`、`rootFrees=0`。原调用返回、source完成与physical exit均不代表非空效果已经结束，不能签退休、完整future source closure或生产准入。

P-20（`path_type=callflow`，E-20/F-CALLBACK）：自有Play原调用→source20 Before→真实CALL/Invoke/mid/listener→source20 After→确切child与reference scope完成→保留未归属效果。

P-21（同类型，E-21/F-CALLBACK/F-EFFECT）：自有Update24/source14→实际Execute19→source21/Invoke/mid/listener→返回原playing occurrence→既有source6清理→保留未归属效果。直接Execute没有合法parent时的诊断边界不因本positive改变。

失败时间线保留：site20 r1因ASM标签重复失败；r2 native0但managed会话中断，随后只续接缺失步骤。root收集器曾错误假定site21只出现14/21，读取实际日志后将既有source6清理列入精确断言，原脚本与失败观察保留于 `play-callbacks20-21-main-collector-failure-r1-20261101`；没有改产品或其成功结果。

这两项没有调用GameAssembly方法、启动/安装游戏或操作账号。完整source generation、未知writer、effect完成、生产准入与B01–B19仍继续推进。
