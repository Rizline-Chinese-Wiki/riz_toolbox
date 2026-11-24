# 精确 Boolean 观察与返回场景原语

2026-10-23。本批承接 [1266 冻结包](../foundation-actual-launch-20261023/README.md)，范围见 [scope](scope.md)。**1287/1287、25 套完整离线检查通过，已冻结暂存，未安装。** Runtime 136、Hooks 107、AudioContent 433；比上一批新增 21 组检查。

## 本批变化

[Boolean 参数观察](../../../src/RizToolbox.Runtime/Hooks/ExactHookBroker.BooleanArgument.cs)给菜单对象的 SetActive 所有权观察提供精确 ABI 基础：一字节值不转成 Win32 BOOL，receiver/MethodInfo 保真，只观察并保持原函数一次。8 组新回归包括 cdecl calli 的 0/1/0x80/0xff、嵌套/worker/外来 MethodInfo、原函数异常、回调失败、解绑和未知安装。

[返回场景 owner](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalRunReturnTransition.cs)从实际首次转场 owner 取得来源，只授一次返回。生产工厂要求同 registry/session 已 Returning；调用精确 StartNextScene 后必须实际取得自己的 operation。完成时确认目标 loaded、manager async/lock 清除，并独立按原关卡句柄确认卸载。[13 组新回归](../../../tests/RizToolbox.AudioContent.Tests/NativeLocalRunReturnTransitionRegression.cs)覆盖原关卡尚未卸载、外来操作/场景/manager、句柄复用、观察前后变化、一次授权、异常责任、线程与 reentry、未知 free，以及真实 registry 的 Returning admission。

场景完成记录只证明场景维度；它不签发所有消费者结束、不关闭 registry，也不释放 run 的 controller/globals/bundle。两种新原语尚未在 Loader 中装配成完整返回命令，菜单输入/相机交接、active 消费者退休、Retry 和完整用户播放入口继续待补。

## 证据

- [Hooks 定向日志](hooks-targeted-first.log)：107/107。
- [AudioContent 定向日志](audio-targeted-first.log)：433/433。
- [完整构建日志](build.log) / [摘要](build-summary.json)：1287/1287、25 套，build session 96075 已 exit 0。
- [架构参考](../../2026-10-22_架构-InFalsusTools-reference-report.md)：复用现有 SDK、插件/服务、profile 和状态后端。

完整增量日志没有编译诊断；前置 Audio 编译日志保留 14 条共享 FakeNative CS0649 和 1 条既有结果 sink 测试 CS8602。这不构成干净重建零 warning 的结论。旧 native helper fixture 的 31261/61358、新初始化 helper 的 1747 和 native-only probe 的 18 次调用单独记账，不加进 1287。完整 Runtime suite 运行了既有生产 sealed Start/getter Dobby fixture；本批没有另跑独立 ABI runner，新 Boolean 入口仍仅受控 engine/calli 验证，均不代替游戏验收。

## 冻结核对

| 产物 | 数量 | SHA-256 |
|---|---|---|
| [源码 ZIP](source-inputs.zip) / [核对](source-archive-check.json) | 387 项，3,903,357 原始字节 | `6c1c4fc91e9ea4810176feb1b2818d67eefac07f10de88755e52bfb5a576fc12` |
| [源码 manifest](source-manifest.json) | 387 项 | `59ba39139456a4f322229a8da61e7f491ca0e230b12a85cad6ba0614bbb726b4` |
| [暂存 ZIP](staged-package.zip) / [核对](package-archive-check.json) | 18 项，2,991,080 原始字节 | `5d255df5afa2489bea39949e8a2fe5fac03160d007240c73c76a568046bab22a` |
| [完整日志](build.log) | exit 0 | `a23be839193777c31d90793b7866b57328033b84756f99f12c8c1e3abaad1ebe` |

[staging-check](staging-check.json) 和 [source-integrity-check](source-integrity-check.json)核对当前暂存及声明源码。[旧安装核对](previous-installation-check.json)匹配 1021 包 18 项和其原有 326 源归档；[三原游戏文件](original-game-check.json)保持匹配。相比 1266，新增 4 文件、修改 7 文件、未删除文件；见 [差分](changes-from-1266.diff) / [清单](changes-from-1266.json)。[主代理审查](review.md)说明本轮实际复核边界；后续接线见 [next-integration](next-integration.md)。

当前安装仍为 1021 四门包。用户“实测稍后”继续有效；本批未安装或操作游戏，不新增 B01–B19 勾选。旧 1266 的 ZIP/manifest/log 独立保留，其断言不会因本批继续改源码而覆盖。
