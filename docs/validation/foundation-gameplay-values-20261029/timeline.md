# N2 记录

1. 2026-10-29：只读 N2/SPEC、Gameplay SDK/服务/后端/测试及既有 pinned 语义；等待 N1 snapshot-source-before.json 与 source-snapshot.zip 实际捕获。
2. root 明确释放源码后：增加有值请求、run/代次、准备/提交/实际值确认/恢复及独立基线读回，复用既有 journal。
3. 固定时钟 profile 复核实际文件指纹、字段布局、方法元数据/RVA 和正文；同 capture 区分游戏坐标/秒、offset/delay 与 DSP/host 区间，不造速率/延迟。
4. live diagnostic-r1：net6 缺 SHA256.HashData(Stream) 重载，修为实例 ComputeHash；失败原件保留。live diagnostic-r2：遇在途 N4 的 BindingIntent 编译缺口，未改其源码，改用 N1 冻结依赖。
5. isolated-r1：构建报两个目标类型推断的重载歧义，改为明确 GameplayControlRequest；原件保留。
6. isolated-r2：实际通过 Gameplay 58/58、RunModification 17/17、SDK 10/10，源码前后及本包工作区输入 SHA 均不变；含恢复独立读回、参与者部分失败、BPM 双段、NaN、转换耗时及中途切局。
7. static-r1：实际通过 10 函数、5 类元数据、34 条指令/常量、5 份生产 profile 正文哈希；真实单位校准及原生值控制支持仍为 false。
8. isolated-r3：最终源码实际通过 Gameplay 60/60、RunModification 17/17、SDK 10/10，三个构建零警告/零错误，603 输入和工作区 owned overlay 哈希不变；新增逐源 NaN/Infinity 及 clock 读取/转换的重入/stop 根延后释放。
9. static-r2：重新核对最终源码对应五份 profile 哈希及10函数/5类/34指令常量，实际 exit 0；冻结代码并保存 scope、报告与证据索引，未触及游戏和其它任务源码。
