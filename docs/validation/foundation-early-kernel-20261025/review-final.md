# 最终归档与报告：有限只读复核

2026-10-25。**未发现本轮正式归档、三组结果或报告中的实质不一致。** root实际观察正式session17459终态0，随后archive session9159退出0并生成报告。本复核只读取终态记录、源码/二进制/原件ZIP、每项结果和当前文件hash，没有再次运行构建、native fixture、子进程或游戏。复核者也是publisher profile及归档/报告生成器作者，属于有限作者自查，不是独立全审。

## 正式门与实际范围

[terminal.json](terminal.json)的session17459/exit0/observed=true/rootSoleBuilder=true与[final-summary](final-summary.json)一致；[command-result](command-result.json)实际七项有序步骤全部completed。终态与command哈希、归档器和生成器当前源码hash均与summary固定值一致，未改变脚本来绕过门。

独立逐项读取三组aggregate和各自process/原始结果：

| 原件组 | 实际case | held输入 | 检查结果 |
| --- | ---: | ---: | --- |
| [新Kernel链](raw/cache/kernel-cases/fixture-result.json) | 73 | 38 | 13248断言，逐case passed/exit/mapping hash一致 |
| [默认旧publisher](raw/cache/legacy-publisher-cases/fixture-result.json) | 57 | 4 | host/receipt/unwind与原文件一致，每child实际exit0 |
| [默认旧early链](raw/cache/legacy-cases/fixture-result.json) | 73 | 7 | 13031断言，逐case passed/exit/mapping hash一致 |

合计203个矩阵case，不包含早先失败、各诊断的三个child，也不将附加debugger监督进程混入target计数。三组held before/after及本次读取时的当前文件hash逐项一致。early两组各73个完整449120字节mapping，publisher57组receipt/prepared/memory及独立证明均保存；只读复核不是重新运行证明器。

## 正式debug与预飞分开

直接读[正式JSONL](raw/cache/kernel-cases/normal-debugger/debug-events.jsonl)，实际16行：1001 LOAD五次、1006 UNLOAD零次、四组entry/return（3组PE entry、1组TLS）、1008错RSP三次、1007零次。其hash与原result和summary完全一致。5个selected映像唯一；此正式样本没有非selected映像，不能从“全部非selected已退休”的空集合断言推导其存在或分支覆盖。

独立逐条对照四个initializationPairs的真实Get/Set/Get上下文：entry/return的tid、instanceId、slot相同；返回actualRsp等于expectedRsp，真实DR slot从returnPc清0，DR7使能位清0，DR6状态按记录清理。Proxy三条1008实际Rsp不匹配，保留原return槽并设置RF，未提前完成pending；随后50号实际返回才以正确Rsp完成。这些只是该次观察到的物理匹配，不扩大为任意栈或全OS生命周期闭包证明。

[强化预飞69351](history/final-preflight-terminal-observation.json)对应ZIP三个实际child各6次LOAD、1次UNLOAD、4对entry/return、3次错Rsp、1007零次，源码为最终b8e1...。它与正式5/0样本不同，原件和计数分开；1007的retired通知分支两边均未命中，保持observed=false，没有冒称动态覆盖。

## 失败历史没有并入成功

[31803第一次失败](history/first-attempt/terminal-observation.json)仍为实际退出1：72个无debug样本通过，debugger样本退出1，legacy尚未开始。[49229](history/diagnostic-terminal-observation.json)和[96108](history/lifetime-diagnostic-terminal-observation.json)为driver0而三个child各1；[52109](history/hardware-diagnostic-terminal-observation.json)为driver0/三个child0，使用中间hardware源码。69351虽三个child0，仍明确不是正式73/57/73。报告均保留该区别，没有将driver0解释为其失败child已通过。

## 源码、二进制、原件及保全

独立解压三个最终ZIP，对全部条目重算hash并与清单及当前文件对应：

| 归档 | 条目 | SHA256 |
| --- | ---: | --- |
| [源码](source-final-inputs.zip) | 604 | `b3e20129db468c400a58ac2e806df9965da897cd56c12ad552857ba9252869be` |
| [二进制](final-binaries.zip) | 18 | `34aa3bdf7afec3230c1e4338ed115e7a5b7a6c40c7e0fca8e71fec7533e0fe34` |
| [完整缓存原件](final-cache-raw.zip) | 2655 | `86a0b4db885189c62d0d91c62d61abc14961434595b451a1f2e770e34eab1b51` |

604项相对kernel-install组合599恰12项预期修改及5项新声明；其余没有额外变化。二进制按7+4+7不同目录保存，两profile的同名Targets不混为同一身份；原件ZIP与raw/cache全部2655文件相同。

[23项包](package-preservation-check.json)仍与core23528包清单逐行相同，当前artifacts/package每项重算hash吻合。本次还只读重算[真实游戏保全](real-game-preservation-check.json)所列旧安装18项和原始文件4项，全部与expected/actual一致。没有将203个自有样本扩成真实游戏冷启动；没有再次Build25或安装。

最终[正式报告](../../2026-10-25_底层-新核运行链验证-report.md)SHA为 `08ebae01abcf6d9eb2a42de99f5bfc390e1100466413f96435085e028f03d9ac`，[README](README.md)SHA为 `6fdc60b213c2d8f3a8573f97e2a2f2a2738ebb092d254e03b41b9b0a6dc643ba`；两份共34个本地链接全部可解析。没有修改被summary固定哈希的生成器或报告源码。

## 边界仍保留

报告区分Publisher record2、共享wire3、Kernel ABI2/scalar1；新Kernel8导出和旧JudgeContext59导出继续独立，不自动共享slot/epoch/Ready/Unknown。只有owned Targets权限，生产V4/真实系统WinHTTP/游戏首次准入/coordinator采用均未授予；scheduler/async/Start内联清理、EngineCancelled和全局退休未完成。Linux与OS全部传递依赖闭包未验证，Pending/RequestPlay状态不变。
