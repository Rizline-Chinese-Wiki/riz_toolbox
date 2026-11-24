# Single run 订阅退出与同步清理

本包完成 run-owned Single 订阅快照、退出阻止新借用、已借 parent 的一次 Exit/Dispose 清理、选定回调在途及 fault 责任保留。实际 fresh 隔离通过 **65/65 Single + 8/8 native operation 回调子进程 + 17/17 旧 typed/completion 定向回归**；生产 Runtime/Adapter 无构建警告。效果关闭仍为 Pending。

正式结论、证据边界和交接范围见 [专项报告](2026-10-30_implementation-single-subscriptions-report.md)，授权范围见 [scope](scope.md)，过程见 [timeline](timeline.md)，固定结果见 [test-result](test-result.json)，逐文件哈希见 [evidence-index](evidence-index.json)。

从仓库根重现，必须使用不存在的 D 盘目录：

```powershell
C:/Python314/python.exe docs/validation/foundation-single-subscriptions-20261030/run-checks.py --output D:/riz-build-cache-next/single-subscriptions-replay-20261030
```

脚本读取固定 V7 core 基底，只覆盖本包 owned 文件与 Retirement 两个函数；不会把并发修改中的其它功能带入专项结果。固定 helper 只读加载到隔离子进程，没有 native 重编译、游戏、GUI 或真实安装。
