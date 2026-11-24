# Single listener 的精确 Play 效果转移

实际 Single 零参 listener 如果是同 run 已接纳 `TweenInfo.Play()` 的稳定闭委托，现在可以将它产生的全部工作转到原 occurrence 账本。一次 listener 创建的 InstantWork 和两个同指针队列项分别保留责任；两个队列项逐项退出前，完成事务继续等待。

fresh 固定基底已通过 **39/39 隔离检查**：11 项新增检查、原 Completion 12 项及 Typed event 16 项。未知 listener、多播、适配委托、变更目标、异常、journal 缺口继续 Pending。本包没有运行游戏或重新执行已过的 Single 订阅生命周期套件。

实现、固定 PE 构造器依据、证据边界与复现见 [专项报告](2026-10-30_implementation-single-play-effects-report.md)。[范围](scope.md)、[过程](timeline.md)、[最终结果](test-result.json)、[完整 owned 覆盖清单](overlay-inputs.json) 和 [证据索引](evidence-index.json) 一同保留。

```powershell
C:/Python314/python.exe docs/validation/foundation-single-play-effects-20261030/run-checks.py --output D:/riz-build-cache-next/single-play-effects-replay-20261030
```

输出必须是不存在的新目录；脚本只从固定 V7 core 复制，再覆盖本 owner 的输入，不编译其它 owner 的 live 修改。
