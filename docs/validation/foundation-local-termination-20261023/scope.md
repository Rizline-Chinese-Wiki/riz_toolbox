# 范围与授权

用户要求继续建设兼容底层，并明确“先继续代码，实测稍后”。本批仅修改桌面 riz_toolbox 的 Runtime/Adapter、离线回归和文档，构建暂存包并校验既有安装；不安装、不操作游戏、不停止游戏。

实现范围是精确 controller.LevelOver 第五门、Progress.Update 调用上下文与三个 Timeline ABI、原生在途与真实帧桥接、单次结果队列，以及未激活事务的取消销毁。生产入口使用已固定构建、metadata 和实际 NativeRuntime 后端。受控后端、calli 与 helper fixture 不能替代游戏内证据。

当前安装仍以 foundation-local-protection-host-20261022 的 1021 批为准；它只有四门。新五门与桥接包仅暂存。此次不勾选新的 B01–B19，不把从未激活的退休证明推广到已激活局，不宣称已打通 LOCAL RequestPlay。

参考仓库 InFalsusTools 仅静态阅读，固定 HEAD 为 98e910339fd7cd7e55a364221ead115bd031151f；本轮再次通过 git ls-remote 核对相同。没有运行第三方程序、安装其依赖或移植其实现。
