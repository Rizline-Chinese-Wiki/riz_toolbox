# 范围

- 授权工作区：`C:/Users/71957/Desktop/riz_toolbox`；输出：`D:/riz-build-cache-next/single-play-joined-*` 的新目录。
- 目标：补齐实际 native Single delivery 到正式 reader、私有 Play transfer 与原 occurrence 完成账本的联合正向路径。
- 生产 owned 文件：Runtime 的 `NativeTweenSingleEventBridge.cs` 及新增 controlled partial；AudioContent 的 `SingleDispatch`、新增 `NativeTweenSingleDeliveryReader` 和 `TypedListenerEffects`。native 仅新增两个 fixture 文件。
- 测试 owned 文件、共享 Program 的命令路由片段与未修改的 native 依赖逐项列于 `overlay-inputs.json`；production / fixture / test-or-runner / owned-route-only 分列。
- 未执行：账号操作、网络账号调用、安装或启动真实游戏、GUI、生产 GameAssembly 代码、生产首次准入或新 Ready 发行。未使用任何已撤销或旧账号凭据。
- 固定基底为 `D:/riz-build-cache-next/single-play-effects-frozen-r1-20261030/source-snapshot`。本包只覆盖这次 owned 输入，不用旧结果验收 root 的后续改动。
