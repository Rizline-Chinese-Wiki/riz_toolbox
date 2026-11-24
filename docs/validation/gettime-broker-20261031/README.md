# GetTime broker 私有挂接凭据

2026-10-31，`flavor=null`。专用离线矩阵最终为 **29 个独立进程、427/427 项检查、14 次实际 Dobby Commit**，所有 child actual exit 0。最终 Runtime r7 同时覆盖首次封存前可执行字节篡改和相邻内存 region 变化误拒；旧 r5 的 24/386、r6 的 28/414 与实际反例均保留，不合并计数。源码、实际 native/managed PE/PDB、COFF 与结果共 304 条冻结在 `D:/riz-build-cache-next/gettime-broker-freeze-r2-20261031/`。

完整行为、复现与 Evidence→Finding→Path 见 [正式报告](2026-10-31_GetTime-broker-report.md)。[范围](scope.md)、[时间线](timeline.md)、[证据索引](evidence-index.json)。

生产准入仍为 `false`。本批只执行自有 `OwnedPlayback.dll`；未安装、启动或调用真实游戏，未操作 UI、账号或已撤销 B 账号。默认真实 Speed/Seek 继续关闭，固定游戏代码 profile 的映像联合验证由根任务单列。
