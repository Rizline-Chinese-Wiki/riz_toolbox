# Fresh 与 GetTime 调用点联合验证

自有原生夹具安装 22 个播放来源角色和 1 个 `GetTime`，用同一份 Fresh 票完成来源认领、暂停回执、Clock 激活和实际原生返回。测试从固定哈希的夹具中取得真实的 native CALL 返回地址，检查空 `MethodInfo` 原样传给原函数，并检查最终是否实际应用 Clock 值。

| 场景 | 结果 |
| --- | --- |
| 同一 Fresh 票的调用点选择 | 65 项。登记调用点在宿主线程传入空 `MethodInfo` 时应用 Clock。异址、错误 `MethodInfo` 和 worker 调用均执行一次原函数。Clip 原生实例 ID 改变后，登记调用点也只返回原函数结果。正常结束释放全部根。 [结果](../../../work/r06-fresh-gettime-consumer-20261121/consumer-r3.json) |
| worker 修改受监视的播放器 | 55 项。相同 AudioSource 指针仍可对应新的来源代次。构造器原函数与物理退出各一次，托管来源回调为零，下一次登记调用点无须等待宿主维护即停止消费原 Clock。两张票可撤销，物理状态仍为 Unknown，保留四个根。 [结果](../../../work/r06-fresh-gettime-consumer-20261121/consumer-worker-watch-r3.json) |
| 相邻用例 | 381 项。覆盖激活、取消、`ProgressPlay`、时间写入、接续、重新开局和跨局初始化。 [最终汇总](../../../work/r06-fresh-gettime-consumer-20261121/validation-r3.json) |
| 构建及身份 | 独立 Fresh Clock Release 测试构建 0 错误、38 条既有夹具警告，核对 1809 项源码和 8 个受测二进制。原生 Clock 和播放来源助手来自上一个 V5 候选，托管测试及依赖从当前源码构建。 [构建](../../../work/r06-fresh-gettime-consumer-20261121/build-r3.log) · [材料清单](../../../work/r06-fresh-gettime-consumer-20261121/binding-r3.json) |
| 新 V5 候选 | 当前冻结源码构建六组件，0 错误、2 条既有可空性警告，两个 V7 映像夹具共 554 项。包内两份播放助手的哈希与本批受测助手相同。58 个 ZIP 成员逐项回读，核实首帧匹配与暂停 Clock 激活校验均已编入，隔离 `ValidateOnly` 验证 26 个运行文件。 [候选 ZIP](D:/riz-m2/RizToolbox-0.3.0-V5-paused-clock-candidate-20261121-r1.zip) · [包验证](../../../work/r06-fresh-gettime-consumer-20261121/candidate-validation-r1.json) |

首轮 worker 用例把注销 epoch 票和释放根视为同一个动作，导致末项断言失败。最终用例分别核对两张票和物理活动状态。 [首轮结果](../../../work/r06-fresh-gettime-consumer-20261121/consumer-worker-watch-r2.json)

这组检查执行了自有夹具的真实原生调用、Fresh coordinator 和返回桥。正式 stock foundation 和七根采用、游戏 `ProgressControl.Update` 的固定 CALL、实机 Speed/Seek 待同版实机验收。
