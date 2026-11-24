# 手柄 current 替换身份隔离

2026-10-21。`Gamepad.current` 可以从仍有效的设备 A 直接切换为 B，中间没有 null/disabled 采样；原744代码只在缺设备时失效旧边沿，会让A的已排队按键留给B时段。现在每次完整采集读取实际 InputDevice.deviceId，读前读后身份一致才接受；与上次已接受身份不同，先撤销旧手柄 queued/drained epoch，键鼠仍保留，再接纳新设备边沿。

`regression.log` 是专项 **73/73**：原70项加3项设备切换、身份不变保留、新epoch及身份无效/变化全批拒绝，原故障用例还加入读取identity异常。Fake device 调用的是生产 CaptureGamepad/ApplyGamepadIdentity 逻辑；没有实机手柄验收。源码定位及日志哈希见本目录 manifest 和最终整合源码清单。

静态来源是用户 `dump.cs` 的 InputDevice：`get_deviceId():Int32`（47608，RVA `0x584590`）、静态 `InvalidDeviceId`（47576）。backend 运行时校验精确方法返回类型，读取运行时无效ID常量，不猜原生状态偏移。缺方法/字段只撤回Gamepad支持。

整合复核另外把切场景 ClearInput 移到当前手柄采集之前，避免在第一批边沿入队时忘记刚采到的设备身份。该顺序修正在最终完整构建验证；原专项日志先于这处顺序改动，不把它当作完整生产场景时序测试。

复验（仓库根目录）：

```powershell
dotnet run --project tests/RizToolbox.UiInput.Tests/RizToolbox.UiInput.Tests.csproj -c Release
```

真实 current 更换、硬件ID重复/热插拔、多手柄驱动和after-input→SDK完整矩阵仍待实机。旧644实体F8仅证明对应包一次键盘派发。电脑操作工具本轮初始化失败证据在 `../runtime-744-attempt-20261021/computer-use-failure.json`。
