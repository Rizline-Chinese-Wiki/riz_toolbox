# 实际 Tween 工作观察与 25 通道候选

2026-10-28。已将五个实际 Tween 方法接入预初始化原生发布、CLR 只读采用和正式本地运行的对象/队列账本。候选已实际构建、打包，并通过本批 17 项合成安装场景。没有安装、启动或操作真实游戏；生产准入仍关闭。

## 已实现的调用链

当前 GameAssembly 操作发布统一为唯一 `RizFirstInstall_GetInfoV5`、capability 3、25 个操作、34 次写入、2048 字节。前 416 字节仍是当前 listener 数据，操作行从 448 开始，最后一行偏移 1984；完整 mask 为 `0x1ffffff`。V5 engine transport 是另一套协议，本批没有修改它的 12 组通道或生产首次执行准入。

| ID | 实际方法 | ABI / slot | 固定完整 RF 范围 | 字节数 |
|---|---|---|---|---|
| 20 | TweenInfo.Play | Void0 / 25 | `0x3956f0..0x3964e8` | 3576 |
| 21 | TweenManager.AddTweenToWaitingList | Reference1 / 20 | `0x396be0..0x396cec` | 268 |
| 22 | TweenManager.ExecuteTween | Reference1 / 19 | `0x396e40..0x398554` | 5908 |
| 23 | TweenManager.Update | Void0 / 24 | `0x398d60..0x39a194` | 5172 |
| 24 | TweenManager.Awake | Void0 / 23 | `0x396cf0..0x396e32` | 322 |

全部源字节和展开前缀来自固定游戏 SHA256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`，包括 RF 内的 switch 表，不把表数据线性反汇编成完整控制流证明。原来的 592 字节缓存已扩大到实际最大函数 5908 字节。[独立核验脚本](../../../scripts/verify-first-install-operation-profile.py)同时核对主 profile 与编译时包含的 `FirstInstallTweenProfile.h`。`Over` 仅写 isOver；`Replay` 和自动播放的 `Start` 汇入 Play，不为这些短函数另装补丁。

[CLR resolver](../../../src/RizToolbox.Runtime/NativeRuntime.TweenOperations.cs)在采用前检查真实非泛型实例声明、确切返回/参数类型、MethodInfo、完整 entry/RVA 与可执行身份。只读采用不会另装补丁。实际 metadata 场景使用既有 SEC_IMAGE 与受控官方绑定验证，未执行原游戏函数。

[Runtime 字段读取](../../../src/RizToolbox.Runtime/NativeRuntime.ExactReadFields.cs)每次核对官方 FieldInfo、声明类、字段类型/静态性、边界与 shadow；实例和静态引用都在 metadata/目标检查前立即发布并由调用者独立保留根。[容器读取](../../../src/RizToolbox.Runtime/NativeRuntime.TweenReadContainers.cs)验证已有 List/Stack 闭类和官方 backing 字段，临时 pinned 数组根由真实 NativePublicationCall 记录。数组读取保留全部容量、null、重复项及顺序，完成后重读整个向量。新增精确 16 字节 Vector4 ABI，不把 Color 或同尺寸类型当作 Vector4。

[生产 backend](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.cs)从本局已保留的实际持久化目标取得 TweenInfo，核对静态 AddTween 的完整实际委托链和具体 manager，独立保留目标、curve、三个子事件、消息、所有容器及数组元素。完整快照涵盖调度字段、参数、各 Vector/Color、fixedStart 和动态值；float 按原始 bits 比较。self/arg 输入在首个 Target/class 读取前全部采用。

[实际 owner](../../../src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.cs)记录原生 Before、After、异常和 context 退出；按实际 Add 序列和完整有序 append 发行独立 admission。相同 target 的重复排队不合并。playing 使用全部非空容量项，避免 Update 尚未写回 workingCount 时漏掉高位活跃槽。manager 的 Awake 变化登记新代次；实际 listener dispatch 可关联具体 Play 来源，未关联的外来调用不伪造来源。

正式 coordinator 在激活前 capture，并在普通和 factory frozen 两条 host pump 中继续观察；失败时保留这项独立责任。当前新增 owner 也进入 Close 前置条件，不能被旧释放表遗漏。

## 明确未完成的功能

五个入口是观察通道，原调用始终保留。**没有 native admission gate，TrySeal 不会成功；也没有 Tween 的最终释放凭据。** 相同 target 在多次 Update 中的重复项仍缺真实 source-slot 转移证据，歧义保留。子事件和 engine setter 的后续效果、直接/序列化/外来写者各有独立责任。方法返回、isOver、一次空队列、场景死亡均不能替代它们。

弱引用方案的实际限制已被确认：[独立原件](D:/riz-build-cache-next/tween-weak-source-r1-20261028/evidence.json)，SHA256 `5e20fa8a8b409a499f8b01ade7e7dae903b61388fcef178e4f9a530694f12ae9`。此游戏明确不支持 `trackResurrection=true`，且报错前已有分配动作；普通弱引用变空不能冒充物理回收。handle 表互斥锁与弱读取的 collector 锁已定位，任意未附加 worker 的安全性未证明。这一事实用于调整下一段 gate 方案，不作为已实现的 seal。

## 本批实际验证

| 证据 | 结果 | 范围 |
|---|---|---|
| [独立 profile](D:/riz-build-cache-next/tween-operation-profile-r1-20261028/profile-verification.json) | 25 / 34，通过 | 全部 RF body、声明 UW 前缀、kind、slot；主/附加 profile 哈希 |
| [原生 r2](D:/riz-build-cache-next/tween-operation-native-r2-20261028/native-manifest.json) | strict 构建 exit0、67 项源不变、实际 PE 导出通过 | 唯一 V5 reader；未重复无关 ABI/机器/性能矩阵 |
| [映像发布 r2](D:/riz-build-cache-next/tween-operation-mapped-r2-20261028/fixture-result.json) | 12 独立进程 / 13185 检查通过 | 25 行、34 写入、13 项错误 header、Unknown，以及五新 body 最后字节实际篡改拒绝；不执行复制的游戏函数 |
| [Unknown 竞争](D:/riz-build-cache-next/tween-operation-unknown-r1-20261028/fixture-result.json) | 916 检查通过 | 细节更新期间 Unknown 原子可见；使用 native-r1 helper |
| [no-write 拒绝](D:/riz-build-cache-next/tween-operation-rejection-r1-20261028/fixture-result.json) | 5 进程 / 200 检查通过 | 错模块/路径等不开始源写入；使用 native-r1 helper |
| [组合源码](D:/riz-build-cache-next/tween-work-core-r1-20261028/test-result.json) | Audio/content 881/881、协调器 28/28、对象身份 10/10 | 新 15 项 Tween owner/backend/真实 broker 场景；Runtime 0 警告，Audio 保留 18 条夹具警告，均 0 错误 |
| [三套专项](D:/riz-build-cache-next/tween-work-managed-r1-20261028/test-result.json) | Runtime 234、Module 26、Hooks 189，合计 449/449 | 新字段/容器 7、resolver 7、Vector4 4 项包含在 Runtime；不是完整 24 套重跑 |
| [实际 CLR 采用](D:/riz-build-cache-next/tween-work-adoption-r1-20261028/fixture-result.json) | 2 进程 / 508 检查通过 | happy 394、reentry 114；真实 producer 全 25 通道采用、共享及错身份拒绝，无晚安装和游戏回调 |
| [六组件 release](D:/riz-build-cache-next/tween-work-release-r1-20261028/managed-build-manifest.json) | exit0，6 DLL，源不变 | 0 错误、1 条既有生成文件 nullable 警告 |
| [本候选安装矩阵](D:/riz-build-cache-next/tween-work-install-r1-20261028/test-result.json) | 17/17，实际 session75064、exit0 | 实际安装入口在合成目录中安装/重装、当前证据拒绝、完整回滚、中断恢复、卸载保留和只读校验；未安装真实游戏 |

15 项 Tween 用例运行实际 owner、broker 或生产输入采用前缀；工作图/原调用由受控 backend 提供，不能代称真实 Unity 队列已完成。字段、容器与 resolver 同样使用生产代码与受控官方绑定。映像、CLR 采用及 owner 三组证据没有拼成真实游戏成功。

首次 mapped-r1 在前七个模式通过后，五新模式被夹具入口遗漏的白名单提前拒绝（exit2），整组失败。修正入口后重新归档 native-r2 并运行 mapped-r2，完整 12 模式才计通过。native-r1 和 r2 helper 字节相同；r2 源归档包含修正后的夹具，失败目录保留。独立输入检查脚本首次错误要求 inline switch 表全部线性解码，在输出证据前拒绝；修正为全字节核对并明确线性解码局限后 r2 实际通过。

## 本批交付

[foundation-tween-candidate-20261028](../../../artifacts/foundation-tween-candidate-20261028/transport-candidate-package-manifest.json)，24 载荷 / 20 证据；manifest SHA256 `6c010a60f7444fb3c3422bf48cf7f18bb1dadf0cb9aeb91d148d71e255f7e6cd`，JudgeContext SHA256 `ad1a245b0dedefc5cad10c9927a81cfa3bfe1fb7fd8af526c44a9737f088287a`。包独立核验 source ZIP、真实 PE、主/附加 profile、12 模式和同源码 CLR 采用；另将采用回执的实际 Runtime.Tests DLL 哈希绑定到源码快照的编译产物。候选保持生产准入和游戏执行/安装标志为 false。

安装结果 SHA256 `56eb4235ac45a0c6e4505b2e6fd185d24c73bd8feae0316ae4cd185ea66b5374`。本批使用最新协议，不维持旧框架向后兼容；性能优化后置。
