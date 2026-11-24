# Seek 暂停音频位置与探针坐标验证

`TimeWrite` 每次派发 `SetTime` 前独立读取暂停音频位置。首次写入对照准备时的基线，后续写入对照上一笔写入完成后的实际读回。外部位置变化会返回 `Conflict`，原始 setter 不会执行。外部恢复到已观察位置后，同一个写入器可以继续，随后读回原位置并释放根引用。

探针发送脚本支持 `-SeekUnit chart-coordinates`。接收端将原数值和单位交给现有 SDK 控制请求，并沿用 run、代次、过期时间和租约检查。用法见[探针控制说明](../../Runtime-probe-control.md)。

- [改前两例复现](../../../work/n2-seek-time-writer-20261119/media-ownership-repro-r1/results.json)。两个子进程均以 `exit 1` 结束，记录了原本不该发生的 `SetTime`。
- [当前构建与回归](../../../work/n2-seek-time-writer-20261119/media-ownership-current-r1/results.json)。Release 解决方案及独立 provider 增量构建均为 0 错误、0 条警告。12 个时间写入场景共 193 项，Gameplay 238 项，三个相邻 provider 场景共 277 项。
- [探针接收端](../../../work/n2-seek-media-ownership-20261121/probe-control.stdout.log)通过 28 项。[实际 PowerShell 发送端](../../../work/n2-seek-media-ownership-20261121/sender-r1/results.json)用隔离目录通过 4 个场景，覆盖显式谱面坐标、默认游戏秒、速度倍率及非法参数组合。
- [封存](../../../work/n2-seek-media-ownership-20261121/validation-r1.json)汇总 740 项当前检查、1807 项源码输入及 5 个修改文件的改前和改后身份。

本轮验证范围为受控音源、当前托管控制路径及探针传输。正式 Fresh、固定游戏 `GetTime` 消费者、真实 Speed/Seek 和实机验收仍待验证。
